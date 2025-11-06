using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoiceDictationCodex.Models;

public class SessionEntry : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime? _updatedAt;
    private string _sessionFolder = string.Empty;
    private string? _transcriptPath;
    private string? _rawTranscriptPath;
    private string? _sourceAudioPath;
    private TimeSpan _duration = TimeSpan.Zero;
    private int _wordCount;
    private string? _model;
    private string? _language;
    private SessionEntryStatus _status = SessionEntryStatus.Empty;
    private bool _hasTranscript;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    public DateTime? UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (SetProperty(ref _updatedAt, value))
            {
                OnPropertyChanged(nameof(UpdatedDisplay));
            }
        }
    }

    public string SessionFolder
    {
        get => _sessionFolder;
        set => SetProperty(ref _sessionFolder, value);
    }

    public string? TranscriptPath
    {
        get => _transcriptPath;
        set => SetProperty(ref _transcriptPath, value);
    }

    public string? RawTranscriptPath
    {
        get => _rawTranscriptPath;
        set => SetProperty(ref _rawTranscriptPath, value);
    }

    public string? SourceAudioPath
    {
        get => _sourceAudioPath;
        set => SetProperty(ref _sourceAudioPath, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                OnPropertyChanged(nameof(DurationDisplay));
            }
        }
    }

    public int WordCount
    {
        get => _wordCount;
        set => SetProperty(ref _wordCount, value);
    }

    public string? Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    public string? Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    public SessionEntryStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
            }
        }
    }

    public bool HasTranscript
    {
        get => _hasTranscript;
        set => SetProperty(ref _hasTranscript, value);
    }

    public string DurationDisplay => Duration <= TimeSpan.Zero ? "--" : Duration.ToString("mm\\:ss");

    public string UpdatedDisplay => (UpdatedAt ?? CreatedAt).ToLocalTime().ToString("g");

    public string StatusDisplay => Status switch
    {
        SessionEntryStatus.Completed => "Completed",
        SessionEntryStatus.InProgress => "In progress",
        SessionEntryStatus.Recording => "Recording",
        _ => "New"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateFrom(SessionEntry source)
    {
        DisplayName = source.DisplayName;
        CreatedAt = source.CreatedAt;
        UpdatedAt = source.UpdatedAt;
        SessionFolder = source.SessionFolder;
        TranscriptPath = source.TranscriptPath;
        RawTranscriptPath = source.RawTranscriptPath;
        SourceAudioPath = source.SourceAudioPath;
        Duration = source.Duration;
        WordCount = source.WordCount;
        Model = source.Model;
        Language = source.Language;
        Status = source.Status;
        HasTranscript = source.HasTranscript;
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
