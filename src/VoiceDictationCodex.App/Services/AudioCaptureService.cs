using System;
using System.IO;
using System.Linq;
using NAudio.Wave;

namespace VoiceDictationCodex.Services;

public class AudioCaptureService : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private EventHandler<WaveInEventArgs>? _dataAvailableHandler;
    private string? _currentFilePath;
    private readonly object _syncRoot = new();

    public event EventHandler<byte[]>? AudioChunkAvailable;

    public string Start(string sessionFolder, int deviceNumber = 0)
    {
        if (_waveIn is not null)
        {
            throw new InvalidOperationException("Audio capture is already running.");
        }

        Directory.CreateDirectory(sessionFolder);
        _currentFilePath = Path.Combine(sessionFolder, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.wav");

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 1)
        };

        _writer = new WaveFileWriter(_currentFilePath, _waveIn.WaveFormat);
        _dataAvailableHandler = (_, args) =>
        {
            lock (_syncRoot)
            {
                _writer?.Write(args.Buffer, 0, args.BytesRecorded);
                _writer?.Flush();
            }

            AudioChunkAvailable?.Invoke(this, args.Buffer.Take(args.BytesRecorded).ToArray());
        };

        _waveIn.DataAvailable += _dataAvailableHandler;
        _waveIn.StartRecording();
        return _currentFilePath;
    }

    public string Stop()
    {
        if (_waveIn is null)
        {
            return _currentFilePath ?? string.Empty;
        }

        if (_dataAvailableHandler is not null)
        {
            _waveIn.DataAvailable -= _dataAvailableHandler;
        }

        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;

        lock (_syncRoot)
        {
            _writer?.Dispose();
            _writer = null;
        }

        var path = _currentFilePath ?? string.Empty;
        _currentFilePath = null;
        _dataAvailableHandler = null;
        return path;
    }

    public void Dispose()
    {
        if (_waveIn is not null && _dataAvailableHandler is not null)
        {
            _waveIn.DataAvailable -= _dataAvailableHandler;
        }

        _waveIn?.Dispose();
        _waveIn = null;
        lock (_syncRoot)
        {
            _writer?.Dispose();
            _writer = null;
        }
        _dataAvailableHandler = null;
        _currentFilePath = null;
    }
}
