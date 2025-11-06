using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using NAudio.Wave;
using VoiceDictationCodex.Models;
using VoiceDictationCodex.Services;

namespace VoiceDictationCodex.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly ModelCatalogService _catalogService = new();
    private readonly IModelRepository _modelRepository = new JsonModelRepository();
    private readonly ModelDownloadService _downloadService;
    private readonly WhisperRuntime _runtime = new();
    private readonly TranscriptionService _transcriptionService;
    private readonly AudioCaptureService _audioCapture = new();
    private readonly SessionVaultService _sessionVault = new();
    private readonly DispatcherTimer _durationTimer;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly Stopwatch _recordingStopwatch = new();

    private WhisperModelInfo? _selectedModel;
    private SessionEntry? _selectedSession;
    private SessionEntry? _activeSession;
    private CancellationTokenSource? _transcriptionCancellation;
    private string? _currentRecordingPath;
    private bool _isRecording;
    private bool _isBusy;
    private bool _suppressSessionSelection;
    private bool _pendingAutosave;
    private bool _autoPunctuation = true;
    private bool _smartFormatting = true;
    private bool _autoCapitalization = true;

    private Brush _recordingIndicatorBrush = Brushes.Gray;
    private string _recordingStatusText = "Idle";
    private string _recordingButtonText = "Start Recording";

    public ObservableCollection<WhisperModelInfo> AvailableModels { get; } = new();
    public ObservableCollection<SessionEntry> RecentSessions { get; } = new();
    public TranscriptionState ActiveTranscription { get; } = new();

    public WhisperModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value))
            {
                return;
            }

            if (!_isRecording)
            {
                ActiveTranscription.ModelName = value?.Name ?? string.Empty;
                ActiveTranscription.Language = value?.LanguageSupport ?? "Auto";
                RaisePropertyChanged(nameof(ActiveTranscription));
            }

            UpdateCommandStates();
        }
    }

    public SessionEntry? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (!SetProperty(ref _selectedSession, value) || value is null)
            {
                return;
            }

            if (_suppressSessionSelection)
            {
                return;
            }

            _ = LoadSessionAsync(value);
        }
    }

    public Brush RecordingIndicatorBrush
    {
        get => _recordingIndicatorBrush;
        private set => SetProperty(ref _recordingIndicatorBrush, value);
    }

    public string RecordingStatusText
    {
        get => _recordingStatusText;
        private set => SetProperty(ref _recordingStatusText, value);
    }

    public string RecordingButtonText
    {
        get => _recordingButtonText;
        private set => SetProperty(ref _recordingButtonText, value);
    }

    public bool IsAutoPunctuationEnabled
    {
        get => _autoPunctuation;
        set
        {
            if (SetProperty(ref _autoPunctuation, value))
            {
                ReapplyFormatting();
            }
        }
    }

    public bool IsSmartFormattingEnabled
    {
        get => _smartFormatting;
        set
        {
            if (SetProperty(ref _smartFormatting, value))
            {
                ReapplyFormatting();
            }
        }
    }

    public bool IsAutoCapitalizationEnabled
    {
        get => _autoCapitalization;
        set
        {
            if (SetProperty(ref _autoCapitalization, value))
            {
                ReapplyFormatting();
            }
        }
    }

    public AsyncRelayCommand DownloadSelectedModelCommand { get; }
    public AsyncRelayCommand CreateSessionCommand { get; }
    public AsyncRelayCommand ImportAudioCommand { get; }
    public AsyncRelayCommand ToggleRecordingCommand { get; }
    public AsyncRelayCommand RenameSessionCommand { get; }
    public AsyncRelayCommand DeleteSessionCommand { get; }
    public RelayCommand ExportTextCommand { get; }
    public RelayCommand ExportMarkdownCommand { get; }
    public RelayCommand CopyTranscriptCommand { get; }
    public RelayCommand OpenSessionFolderCommand { get; }

    public MainViewModel()
    {
        _downloadService = new ModelDownloadService(new HttpClient(), _modelRepository);
        _transcriptionService = new TranscriptionService(_runtime);
        _transcriptionService.TextUpdated += (_, text) => DispatchToUi(() =>
        {
            ActiveTranscription.RawText = text;
            ActiveTranscription.Text = PostProcessTranscript(text, isFinal: false);
            ActiveTranscription.WordCount = CountWords(ActiveTranscription.Text);
            ActiveTranscription.LastUpdated = DateTime.UtcNow;
            ActiveTranscription.IsCompleted = false;
            RaisePropertyChanged(nameof(ActiveTranscription));
            CopyTranscriptCommand.RaiseCanExecuteChanged();
            ExportTextCommand.RaiseCanExecuteChanged();
            ExportMarkdownCommand.RaiseCanExecuteChanged();
            TriggerAutosave();
        });
        _transcriptionService.RuntimeMessage += (_, message) => DispatchToUi(() =>
        {
            if (_isBusy)
            {
                RecordingStatusText = $"Processing… {message}";
            }
        });

        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _durationTimer.Tick += (_, _) =>
        {
            ActiveTranscription.Duration = FormatDuration(_recordingStopwatch.Elapsed);
            RaisePropertyChanged(nameof(ActiveTranscription));
        };

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _autosaveTimer.Tick += async (_, _) =>
        {
            _autosaveTimer.Stop();
            if (_pendingAutosave)
            {
                _pendingAutosave = false;
                await PersistSessionSnapshotAsync(isFinal: false);
            }
        };

        DownloadSelectedModelCommand = new AsyncRelayCommand(DownloadSelectedModelAsync, () => SelectedModel is { IsInstalled: false } && !_isRecording && !_isBusy);
        CreateSessionCommand = new AsyncRelayCommand(CreateSessionAsync, () => !_isBusy);
        ImportAudioCommand = new AsyncRelayCommand(ImportAudioAsync, () => SelectedModel?.IsInstalled == true && !_isRecording && !_isBusy);
        ToggleRecordingCommand = new AsyncRelayCommand(ToggleRecordingAsync, () => SelectedModel?.IsInstalled == true && !_isBusy);
        RenameSessionCommand = new AsyncRelayCommand(RenameSessionAsync, () => _activeSession is not null && !_isBusy);
        DeleteSessionCommand = new AsyncRelayCommand(DeleteSessionAsync, () => _activeSession is not null && !_isBusy);
        ExportTextCommand = new RelayCommand(() => ExportTranscript(asMarkdown: false), () => !string.IsNullOrWhiteSpace(ActiveTranscription.Text));
        ExportMarkdownCommand = new RelayCommand(() => ExportTranscript(asMarkdown: true), () => !string.IsNullOrWhiteSpace(ActiveTranscription.Text));
        CopyTranscriptCommand = new RelayCommand(CopyTranscript, () => !string.IsNullOrWhiteSpace(ActiveTranscription.Text));
        OpenSessionFolderCommand = new RelayCommand(OpenSessionFolder, () => !string.IsNullOrWhiteSpace(ActiveTranscription.SessionFolder) && Directory.Exists(ActiveTranscription.SessionFolder));

        RecordingIndicatorBrush = Brushes.Gray;
        RecordingStatusText = "Idle";
        RecordingButtonText = "Start Recording";

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadModelsAsync();
        await LoadSessionsAsync();
        if (RecentSessions.Count == 0)
        {
            await CreateSessionAsync();
        }
    }

    private async Task LoadModelsAsync()
    {
        var catalog = _catalogService.GetBuiltInCatalog();
        var installed = await _modelRepository.LoadAvailableModelsAsync();
        var merged = catalog.Select(model =>
        {
            var saved = installed.FirstOrDefault(savedModel => savedModel.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
            return saved is null ? model : model with { LocalPath = saved.LocalPath, IsInstalled = saved.IsInstalled };
        }).ToList();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            AvailableModels.Clear();
            foreach (var model in merged)
            {
                AvailableModels.Add(model);
            }

            SelectedModel = AvailableModels.FirstOrDefault();
        }
        else
        {
            await dispatcher.InvokeAsync(() =>
            {
                AvailableModels.Clear();
                foreach (var model in merged)
                {
                    AvailableModels.Add(model);
                }

                SelectedModel = AvailableModels.FirstOrDefault();
            });
        }

        UpdateCommandStates();
    }

    private async Task LoadSessionsAsync()
    {
        var sessions = await _sessionVault.GetSessionsAsync();
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            PopulateSessionCollection(sessions);
        }
        else
        {
            await dispatcher.InvokeAsync(() => PopulateSessionCollection(sessions));
        }
    }

    private void PopulateSessionCollection(IReadOnlyList<SessionEntry> sessions)
    {
        RecentSessions.Clear();
        foreach (var session in sessions)
        {
            RecentSessions.Add(session);
        }

        if (RecentSessions.Count > 0)
        {
            _suppressSessionSelection = true;
            SelectedSession = RecentSessions[0];
            _suppressSessionSelection = false;
            _ = LoadSessionAsync(RecentSessions[0]);
        }
    }

    private async Task CreateSessionAsync()
    {
        await CreateSessionInternalAsync();
    }

    private async Task<SessionEntry> CreateSessionInternalAsync()
    {
        ResetCaptureState();
        SetBusy(true);
        try
        {
            var session = await _sessionVault.CreateSessionAsync();
            _activeSession = session;
            PrepareSessionState(session, string.Empty, isFinal: false);

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                RecentSessions.Insert(0, session);
                _suppressSessionSelection = true;
                SelectedSession = session;
                _suppressSessionSelection = false;
            }
            else
            {
                await dispatcher.InvokeAsync(() =>
                {
                    RecentSessions.Insert(0, session);
                    _suppressSessionSelection = true;
                    SelectedSession = session;
                    _suppressSessionSelection = false;
                });
            }

            MoveSessionToTop(session);
            UpdateCommandStates();
            return session;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DownloadSelectedModelAsync()
    {
        if (SelectedModel is null)
        {
            return;
        }

        try
        {
            SetBusy(true);
            RecordingStatusText = "Downloading model…";
            var progress = new Progress<double>(value => RecordingStatusText = $"Downloading {(int)(value * 100)}%");

            var updated = await _downloadService.DownloadAsync(SelectedModel, progress);
            UpdateModel(updated);
            SelectedModel = updated;
            RecordingStatusText = "Model ready";
        }
        catch (Exception ex)
        {
            RecordingStatusText = "Download failed";
            MessageBox.Show($"Unable to download the model. {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateModel(WhisperModelInfo updated)
    {
        var index = AvailableModels.ToList().FindIndex(m => m.Id == updated.Id);
        if (index >= 0)
        {
            AvailableModels[index] = updated;
            RaisePropertyChanged(nameof(AvailableModels));
        }
        else
        {
            AvailableModels.Add(updated);
        }
    }

    private async Task LoadSessionAsync(SessionEntry session)
    {
        ResetCaptureState();
        _activeSession = session;

        var content = await _sessionVault.ReadContentAsync(session);
        var isFinal = session.Status == SessionEntryStatus.Completed;
        var rawText = string.IsNullOrWhiteSpace(content.Raw) ? content.Formatted ?? string.Empty : content.Raw!;
        var formatted = string.IsNullOrWhiteSpace(content.Formatted)
            ? PostProcessTranscript(rawText, isFinal)
            : content.Formatted!;

        session.RawTranscriptPath ??= Path.Combine(session.SessionFolder, "raw-transcript.txt");
        session.TranscriptPath ??= Path.Combine(session.SessionFolder, "transcript.txt");

        PrepareSessionState(session, rawText, isFinal, formatted);

        if (!string.IsNullOrWhiteSpace(session.Model))
        {
            var match = AvailableModels.FirstOrDefault(m => string.Equals(m.Name, session.Model, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                SelectedModel = match;
            }
        }
    }

    private async Task ImportAudioAsync()
    {
        if (SelectedModel?.IsInstalled != true)
        {
            MessageBox.Show("Install a Whisper model before importing audio.", "Model Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.opus|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetBusy(true);
            var session = await EnsureActiveSessionAsync();
            session.Status = SessionEntryStatus.InProgress;
            await _sessionVault.UpdateStatusAsync(session, SessionEntryStatus.InProgress);

            var destination = Path.Combine(session.SessionFolder, $"{Path.GetFileNameWithoutExtension(dialog.FileName)}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(dialog.FileName)}");
            File.Copy(dialog.FileName, destination, overwrite: true);

            var duration = ReadAudioDuration(destination);

            ActiveTranscription.SessionFolder = session.SessionFolder;
            ActiveTranscription.SourceAudioPath = destination;
            ActiveTranscription.Duration = FormatDuration(duration);
            ActiveTranscription.RawText = string.Empty;
            ActiveTranscription.Text = string.Empty;
            ActiveTranscription.WordCount = 0;
            ActiveTranscription.IsCompleted = false;
            ActiveTranscription.LastUpdated = DateTime.UtcNow;
            RaisePropertyChanged(nameof(ActiveTranscription));

            RecordingIndicatorBrush = Brushes.Gray;
            RecordingButtonText = "Start Recording";
            RecordingStatusText = "Transcribing import…";

            await PersistSessionSnapshotAsync(isFinal: false);
            await RunTranscriptionAsync(destination, duration);
        }
        catch (Exception ex)
        {
            RecordingStatusText = "Import failed";
            MessageBox.Show($"Unable to import the selected audio file. {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (_isRecording)
        {
            await StopRecordingAsync();
        }
        else
        {
            await StartRecordingAsync();
        }
    }

    private async Task StartRecordingAsync()
    {
        if (SelectedModel?.IsInstalled != true || string.IsNullOrWhiteSpace(SelectedModel.LocalPath))
        {
            MessageBox.Show("Install a Whisper model before starting a recording.", "Model Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var session = await EnsureActiveSessionAsync();
        session.Status = SessionEntryStatus.Recording;
        await _sessionVault.UpdateStatusAsync(session, SessionEntryStatus.Recording);

        CancelTranscription();
        ActiveTranscription.SessionFolder = session.SessionFolder;
        ActiveTranscription.SourceAudioPath = null;
        ActiveTranscription.RawText = string.Empty;
        ActiveTranscription.Text = string.Empty;
        ActiveTranscription.WordCount = 0;
        ActiveTranscription.Duration = "00:00";
        ActiveTranscription.ModelName = SelectedModel.Name;
        ActiveTranscription.Language = SelectedModel.LanguageSupport;
        ActiveTranscription.IsCompleted = false;
        ActiveTranscription.LastUpdated = DateTime.UtcNow;
        RaisePropertyChanged(nameof(ActiveTranscription));

        _recordingStopwatch.Restart();
        _durationTimer.Start();
        _currentRecordingPath = _audioCapture.Start(session.SessionFolder);
        _isRecording = true;

        RecordingIndicatorBrush = new SolidColorBrush(Color.FromRgb(255, 99, 132));
        RecordingStatusText = "Listening";
        RecordingButtonText = "Stop Recording";

        MoveSessionToTop(session);
        UpdateCommandStates();
    }

    private async Task StopRecordingAsync()
    {
        if (!_isRecording)
        {
            return;
        }

        SetBusy(true);
        _durationTimer.Stop();
        _recordingStopwatch.Stop();

        var recordedPath = _audioCapture.Stop();
        _isRecording = false;
        UpdateCommandStates();

        if (string.IsNullOrWhiteSpace(recordedPath) || !File.Exists(recordedPath) || new FileInfo(recordedPath).Length == 0)
        {
            RecordingIndicatorBrush = Brushes.Gray;
            RecordingButtonText = "Start Recording";
            RecordingStatusText = "No audio captured";
            SetBusy(false);
            return;
        }

        _currentRecordingPath = recordedPath;
        ActiveTranscription.SourceAudioPath = recordedPath;
        ActiveTranscription.Duration = FormatDuration(_recordingStopwatch.Elapsed);
        ActiveTranscription.LastUpdated = DateTime.UtcNow;
        RaisePropertyChanged(nameof(ActiveTranscription));

        RecordingIndicatorBrush = Brushes.Gray;
        RecordingButtonText = "Start Recording";
        RecordingStatusText = "Processing capture…";

        try
        {
            await PersistSessionSnapshotAsync(isFinal: false);
            await RunTranscriptionAsync(recordedPath, _recordingStopwatch.Elapsed);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunTranscriptionAsync(string audioFilePath, TimeSpan? durationOverride = null)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            RecordingStatusText = "Audio file missing";
            return;
        }

        var modelPath = SelectedModel?.LocalPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            RecordingStatusText = "Model not available";
            MessageBox.Show("Select and download a Whisper model before transcribing.", "Model Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CancellationTokenSource? cts = null;
        try
        {
            RecordingStatusText = "Transcribing…";
            cts = new CancellationTokenSource();
            _transcriptionCancellation = cts;
            var finalText = await _transcriptionService.TranscribeAsync(modelPath, audioFilePath, cts.Token);

            var duration = durationOverride ?? ReadAudioDuration(audioFilePath);
            ActiveTranscription.Duration = FormatDuration(duration);
            ActiveTranscription.ModelName = SelectedModel?.Name ?? ActiveTranscription.ModelName;
            ActiveTranscription.Language = SelectedModel?.LanguageSupport ?? ActiveTranscription.Language;
            ActiveTranscription.SourceAudioPath = audioFilePath;
            ActiveTranscription.RawText = finalText;
            ActiveTranscription.Text = PostProcessTranscript(finalText, isFinal: true);
            ActiveTranscription.WordCount = CountWords(ActiveTranscription.Text);
            ActiveTranscription.IsCompleted = true;
            ActiveTranscription.LastUpdated = DateTime.UtcNow;
            RaisePropertyChanged(nameof(ActiveTranscription));

            await PersistSessionSnapshotAsync(isFinal: true);

            RecordingStatusText = "Completed";
            ExportTextCommand.RaiseCanExecuteChanged();
            ExportMarkdownCommand.RaiseCanExecuteChanged();
            CopyTranscriptCommand.RaiseCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            RecordingStatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            RecordingStatusText = "Error";
            MessageBox.Show($"Transcription failed. {ex.Message}", "Transcription Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _transcriptionCancellation = null;
            cts?.Dispose();
        }
    }

    private async Task RenameSessionAsync()
    {
        if (_activeSession is null)
        {
            return;
        }

        var input = Interaction.InputBox("Rename session", "Session Title", _activeSession.DisplayName);
        if (string.IsNullOrWhiteSpace(input) || string.Equals(input, _activeSession.DisplayName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            SetBusy(true);
            await _sessionVault.RenameSessionAsync(_activeSession, input.Trim());
            ActiveTranscription.Title = _activeSession.DisplayName;
            ActiveTranscription.LastUpdated = DateTime.UtcNow;
            RaisePropertyChanged(nameof(ActiveTranscription));
            MoveSessionToTop(_activeSession);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteSessionAsync()
    {
        if (_activeSession is null)
        {
            return;
        }

        var result = MessageBox.Show($"Delete '{_activeSession.DisplayName}'? This will remove audio and transcripts.", "Delete Session", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        CancelTranscription();
        if (_isRecording)
        {
            _audioCapture.Stop();
            _isRecording = false;
        }

        try
        {
            await _sessionVault.DeleteSessionAsync(_activeSession);
            var index = RecentSessions.IndexOf(_activeSession);
            RecentSessions.Remove(_activeSession);
            _activeSession = null;

            if (RecentSessions.Count > 0)
            {
                var newIndex = Math.Clamp(index, 0, RecentSessions.Count - 1);
                _suppressSessionSelection = true;
                SelectedSession = RecentSessions[newIndex];
                _suppressSessionSelection = false;
                await LoadSessionAsync(RecentSessions[newIndex]);
            }
            else
            {
                await CreateSessionInternalAsync();
            }

            UpdateCommandStates();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExportTranscript(bool asMarkdown)
    {
        if (string.IsNullOrWhiteSpace(ActiveTranscription.Text))
        {
            return;
        }

        var extension = asMarkdown ? ".md" : ".txt";
        var filter = asMarkdown ? "Markdown (*.md)|*.md" : "Text Files (*.txt)|*.txt";
        var suggestedName = $"{SanitizeFileName(_activeSession?.DisplayName ?? "transcription")}-{DateTime.Now:yyyyMMdd-HHmmss}{extension}";
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = suggestedName,
            InitialDirectory = !string.IsNullOrWhiteSpace(ActiveTranscription.SessionFolder) && Directory.Exists(ActiveTranscription.SessionFolder)
                ? ActiveTranscription.SessionFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == true)
        {
            var content = asMarkdown ? BuildMarkdownExport() : ActiveTranscription.Text;
            File.WriteAllText(dialog.FileName, content);
            MessageBox.Show($"Transcript exported to {dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private string BuildMarkdownExport()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {ActiveTranscription.Title}");
        builder.AppendLine();
        builder.AppendLine("## Details");
        builder.AppendLine($"- Model: {ActiveTranscription.ModelName}");
        builder.AppendLine($"- Language: {ActiveTranscription.Language}");
        builder.AppendLine($"- Duration: {ActiveTranscription.Duration}");
        builder.AppendLine($"- Words: {ActiveTranscription.WordCount}");
        builder.AppendLine($"- Created: {ActiveTranscription.CreatedAt:g}");
        if (ActiveTranscription.LastUpdated is { } updated)
        {
            builder.AppendLine($"- Updated: {updated:g}");
        }

        builder.AppendLine();
        builder.AppendLine("## Transcript");
        builder.AppendLine();
        builder.AppendLine(ActiveTranscription.Text);
        return builder.ToString();
    }

    private void CopyTranscript()
    {
        if (!string.IsNullOrWhiteSpace(ActiveTranscription.Text))
        {
            Clipboard.SetText(ActiveTranscription.Text);
        }
    }

    private void OpenSessionFolder()
    {
        if (string.IsNullOrWhiteSpace(ActiveTranscription.SessionFolder) || !Directory.Exists(ActiveTranscription.SessionFolder))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ActiveTranscription.SessionFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open session folder. {ex.Message}", "Open Folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelTranscription()
    {
        if (_transcriptionCancellation is { IsCancellationRequested: false })
        {
            _transcriptionCancellation.Cancel();
        }

        _transcriptionService.Stop();
        _transcriptionCancellation = null;
    }

    private void ResetCaptureState()
    {
        CancelTranscription();
        if (_isRecording)
        {
            _audioCapture.Stop();
            _isRecording = false;
        }

        _durationTimer.Stop();
        _recordingStopwatch.Reset();
        _currentRecordingPath = null;

        RecordingIndicatorBrush = Brushes.Gray;
        RecordingStatusText = "Idle";
        RecordingButtonText = "Start Recording";
    }

    private async Task<SessionEntry> EnsureActiveSessionAsync()
    {
        if (_activeSession is not null)
        {
            return _activeSession;
        }

        return await CreateSessionInternalAsync();
    }

    private async Task PersistSessionSnapshotAsync(bool isFinal)
    {
        if (_activeSession is null)
        {
            return;
        }

        var duration = ParseDurationString(ActiveTranscription.Duration);
        await _sessionVault.SaveSnapshotAsync(
            _activeSession,
            ActiveTranscription.RawText,
            ActiveTranscription.Text,
            duration,
            ActiveTranscription.WordCount,
            ActiveTranscription.ModelName,
            ActiveTranscription.Language,
            ActiveTranscription.SourceAudioPath,
            markCompleted: isFinal);

        ActiveTranscription.LastUpdated = _activeSession.UpdatedAt?.ToLocalTime();
        RaisePropertyChanged(nameof(ActiveTranscription));
        MoveSessionToTop(_activeSession);
        OpenSessionFolderCommand.RaiseCanExecuteChanged();
    }

    private void TriggerAutosave()
    {
        _pendingAutosave = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void PrepareSessionState(SessionEntry session, string rawTranscript, bool isFinal, string? formattedOverride = null)
    {
        ActiveTranscription.Title = session.DisplayName;
        ActiveTranscription.SessionFolder = session.SessionFolder;
        ActiveTranscription.SourceAudioPath = session.SourceAudioPath;
        ActiveTranscription.CreatedAt = session.CreatedAt.ToLocalTime();
        ActiveTranscription.LastUpdated = session.UpdatedAt?.ToLocalTime();
        ActiveTranscription.ModelName = session.Model ?? SelectedModel?.Name ?? ActiveTranscription.ModelName;
        ActiveTranscription.Language = session.Language ?? SelectedModel?.LanguageSupport ?? ActiveTranscription.Language;
        ActiveTranscription.Duration = FormatDuration(session.Duration);
        ActiveTranscription.RawText = rawTranscript;
        ActiveTranscription.Text = formattedOverride ?? PostProcessTranscript(rawTranscript, isFinal);
        ActiveTranscription.WordCount = CountWords(ActiveTranscription.Text);
        ActiveTranscription.IsCompleted = isFinal;
        RaisePropertyChanged(nameof(ActiveTranscription));

        CopyTranscriptCommand.RaiseCanExecuteChanged();
        ExportTextCommand.RaiseCanExecuteChanged();
        ExportMarkdownCommand.RaiseCanExecuteChanged();
        OpenSessionFolderCommand.RaiseCanExecuteChanged();
    }

    private void MoveSessionToTop(SessionEntry session)
    {
        var index = RecentSessions.IndexOf(session);
        if (index > 0)
        {
            RecentSessions.Move(index, 0);
        }

        RenameSessionCommand.RaiseCanExecuteChanged();
        DeleteSessionCommand.RaiseCanExecuteChanged();
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        DownloadSelectedModelCommand.RaiseCanExecuteChanged();
        ToggleRecordingCommand.RaiseCanExecuteChanged();
        ImportAudioCommand.RaiseCanExecuteChanged();
        ExportTextCommand.RaiseCanExecuteChanged();
        ExportMarkdownCommand.RaiseCanExecuteChanged();
        CopyTranscriptCommand.RaiseCanExecuteChanged();
        OpenSessionFolderCommand.RaiseCanExecuteChanged();
        RenameSessionCommand.RaiseCanExecuteChanged();
        DeleteSessionCommand.RaiseCanExecuteChanged();
    }

    private static void DispatchToUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private static TimeSpan ReadAudioDuration(string filePath)
    {
        try
        {
            using var reader = new AudioFileReader(filePath);
            return reader.TotalTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static string FormatDuration(TimeSpan span) => span <= TimeSpan.Zero ? "00:00" : span.ToString("mm\\:ss");

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var separators = new[] { ' ', '\n', '\r', '\t' };
        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static TimeSpan ParseDurationString(string duration)
        => TimeSpan.TryParseExact(duration, "mm\\:ss", null, out var parsed) ? parsed : TimeSpan.Zero;

    private string PostProcessTranscript(string text, bool isFinal)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var processed = text;

        if (IsSmartFormattingEnabled)
        {
            processed = NormalizeWhitespace(processed);
        }

        if (IsAutoCapitalizationEnabled)
        {
            processed = CapitalizeSentences(processed);
        }

        if (IsAutoPunctuationEnabled && isFinal)
        {
            processed = EnsureSentenceTermination(processed);
        }

        return processed.Trim();
    }

    private static string NormalizeWhitespace(string text)
    {
        var collapsed = Regex.Replace(text, "\\s+", " ");
        collapsed = collapsed.Replace(" ,", ",").Replace(" .", ".").Replace(" !", "!").Replace(" ?", "?");
        collapsed = Regex.Replace(collapsed, "([,.!?])(?=\S)", "$1 ");
        return collapsed.Replace("  ", " ");
    }

    private static string CapitalizeSentences(string text)
    {
        var result = new StringBuilder(text.Length);
        var capitalizeNext = true;
        foreach (var ch in text)
        {
            if (capitalizeNext && char.IsLetter(ch))
            {
                result.Append(char.ToUpper(ch));
                capitalizeNext = false;
            }
            else
            {
                result.Append(ch);
            }

            if (ch is '.' or '!' or '?' or '\n')
            {
                capitalizeNext = true;
            }
        }

        return result.ToString();
    }

    private static string EnsureSentenceTermination(string text)
    {
        var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!".?!".Contains(line[^1]))
            {
                line += ".";
            }

            lines[i] = line;
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '-');
        }

        return name;
    }

    private void ReapplyFormatting()
    {
        ActiveTranscription.Text = PostProcessTranscript(ActiveTranscription.RawText, ActiveTranscription.IsCompleted);
        ActiveTranscription.WordCount = CountWords(ActiveTranscription.Text);
        RaisePropertyChanged(nameof(ActiveTranscription));
        TriggerAutosave();
    }
}
