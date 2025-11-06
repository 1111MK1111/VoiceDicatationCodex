using System;
using System.IO;
using System.Linq;
using NAudio.Wave;

namespace VoiceDictationCodex.Services;

public class AudioCaptureService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private EventHandler<WaveInEventArgs>? _dataAvailableHandler;

    public event EventHandler<byte[]>? AudioChunkAvailable;

    public void Start(int deviceNumber = 0)
    {
        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 1)
        };

        _buffer = new MemoryStream();
        _dataAvailableHandler = (_, args) =>
        {
            _buffer?.Write(args.Buffer, 0, args.BytesRecorded);
            AudioChunkAvailable?.Invoke(this, args.Buffer.Take(args.BytesRecorded).ToArray());
        };
        _waveIn.DataAvailable += _dataAvailableHandler;
        _waveIn.StartRecording();
    }

    public byte[] Stop()
    {
        if (_waveIn is not null && _dataAvailableHandler is not null)
        {
            _waveIn.DataAvailable -= _dataAvailableHandler;
        }

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        var data = _buffer?.ToArray() ?? Array.Empty<byte>();
        _buffer?.Dispose();
        _buffer = null;
        _dataAvailableHandler = null;
        return data;
    }

    public void Dispose()
    {
        if (_waveIn is not null && _dataAvailableHandler is not null)
        {
            _waveIn.DataAvailable -= _dataAvailableHandler;
        }

        _waveIn?.Dispose();
        _waveIn = null;
        _buffer?.Dispose();
        _buffer = null;
        _dataAvailableHandler = null;
    }
}
