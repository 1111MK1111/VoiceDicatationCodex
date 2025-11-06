using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceDictationCodex.Services;

public class WhisperRuntime
{
    private readonly string? _configuredExecutablePath;
    private Process? _whisperProcess;

    public WhisperRuntime(string? executablePath = null)
    {
        _configuredExecutablePath = executablePath;
    }

    public bool IsRunning => _whisperProcess is { HasExited: false };

    public event EventHandler<string>? TranscriptionReceived;
    public event EventHandler<string>? RuntimeMessage;

    public async Task TranscribeAsync(string modelPath, string audioFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path must be provided.", nameof(modelPath));
        }

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The selected Whisper model could not be found.", modelPath);
        }

        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("The captured audio file could not be found.", audioFilePath);
        }

        var executablePath = ResolveExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new FileNotFoundException("Unable to locate the Whisper runtime executable. Place whisper.cpp.exe alongside the application or configure VOICEDICTATION_WHISPER_PATH.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"--model \"{modelPath}\" --file \"{audioFilePath}\" --output-json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            _whisperProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!_whisperProcess.Start())
            {
                throw new InvalidOperationException("Unable to start Whisper runtime process.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Failed to start Whisper runtime at '{executablePath}'.", ex);
        }

        var process = _whisperProcess;
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        });
        int exitCode;
        try
        {
            await Task.WhenAll(
                ReadOutputAsync(process, cancellationToken),
                ReadErrorAsync(process, cancellationToken),
                process.WaitForExitAsync(cancellationToken));

            exitCode = process.ExitCode;
        }
        catch
        {
            Stop();
            throw;
        }

        Stop();

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Whisper runtime exited with code {exitCode}.");
        }
    }

    public void Stop()
    {
        if (_whisperProcess is null)
        {
            return;
        }

        try
        {
            if (!_whisperProcess.HasExited)
            {
                _whisperProcess.Kill(true);
                _whisperProcess.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        catch (ObjectDisposedException)
        {
            // Already cleaned up
        }
        finally
        {
            _whisperProcess.Dispose();
            _whisperProcess = null;
        }
    }

    private async Task ReadOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                TranscriptionReceived?.Invoke(this, line);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReadErrorAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    RuntimeMessage?.Invoke(this, line);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private string? ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_configuredExecutablePath) && File.Exists(_configuredExecutablePath))
        {
            return _configuredExecutablePath;
        }

        var envPath = Environment.GetEnvironmentVariable("VOICEDICTATION_WHISPER_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "whisper.cpp.exe"),
            Path.Combine(baseDir, "whisper.cpp", "whisper.cpp.exe"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "whisper.cpp.exe"),
            Path.Combine(baseDir, "runtimes", "whisper", "whisper.cpp.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
