using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace VoiceDictationCodex.Services;

public class TranscriptionService
{
    private readonly WhisperRuntime _runtime;
    private readonly StringBuilder _buffer = new();

    public event EventHandler<string>? TextUpdated;
    public event EventHandler<string>? RuntimeMessage;

    public TranscriptionService(WhisperRuntime runtime)
    {
        _runtime = runtime;
        _runtime.TranscriptionReceived += HandleTranscriptionLine;
        _runtime.RuntimeMessage += (_, message) => RuntimeMessage?.Invoke(this, message);
    }

    public async Task<string> TranscribeAsync(string modelPath, string audioFilePath, CancellationToken cancellationToken = default)
    {
        _buffer.Clear();
        await _runtime.TranscribeAsync(modelPath, audioFilePath, cancellationToken);
        return _buffer.ToString();
    }

    public void Stop()
    {
        _runtime.Stop();
    }

    private void HandleTranscriptionLine(object? sender, string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                var chunk = textElement.GetString();
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    _buffer.Append(chunk);
                    TextUpdated?.Invoke(this, _buffer.ToString());
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON output from runtime, treat as raw text fallback
            if (!string.IsNullOrWhiteSpace(line))
            {
                _buffer.AppendLine(line);
                TextUpdated?.Invoke(this, _buffer.ToString());
            }
        }
    }
}
