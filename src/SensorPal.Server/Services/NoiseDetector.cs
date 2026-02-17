using NAudio.Wave;

namespace SensorPal.Server.Services;

sealed class NoiseDetector
{
    readonly double _thresholdDb;
    readonly int _silenceTimeoutMs;
    readonly WaveFormat _format;

    bool _eventActive;
    DateTime _eventStart;
    double _peakDb;
    DateTime _lastSignalTime;

    public event Action<DateTime, double>? EventStarted;
    public event Action<DateTime, double, int>? EventEnded;

    public double CurrentDb { get; private set; } = -100.0;
    public bool IsEventActive => _eventActive;
    public DateTime? EventStartedAt => _eventActive ? _eventStart : null;

    public NoiseDetector(double thresholdDb, WaveFormat format, int silenceTimeoutMs = 5000)
    {
        _thresholdDb = thresholdDb;
        _format = format;
        _silenceTimeoutMs = silenceTimeoutMs;
    }

    public void Process(byte[] buffer, int offset, int count)
    {
        var rmsDb = CalculateRmsDb(buffer, offset, count);
        CurrentDb = rmsDb;

        var now = DateTime.UtcNow;
        var isLoud = rmsDb >= _thresholdDb;

        if (!_eventActive)
        {
            if (!isLoud) return;

            _eventActive = true;
            _eventStart = DateTime.UtcNow;
            _peakDb = rmsDb;
            _lastSignalTime = now;
            EventStarted?.Invoke(_eventStart, rmsDb);
            return;
        }

        if (isLoud)
        {
            _lastSignalTime = now;
            _peakDb = Math.Max(_peakDb, rmsDb);
            return;
        }

        if ((now - _lastSignalTime).TotalMilliseconds < _silenceTimeoutMs) return;

        _eventActive = false;
        var durationMs = (int)(now - _eventStart).TotalMilliseconds;
        EventEnded?.Invoke(_eventStart, _peakDb, durationMs);
    }

    double CalculateRmsDb(byte[] buffer, int offset, int count)
    {
        var bytesPerSample = _format.BitsPerSample / 8;
        var bytesPerFrame = bytesPerSample * _format.Channels;
        var frameCount = count / bytesPerFrame;
        if (frameCount == 0) return -100.0;

        var isFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat;
        double sumSq = 0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            // For multi-channel capture: average power across channels per frame.
            double frameSumSq = 0;
            for (var ch = 0; ch < _format.Channels; ch++)
            {
                var pos = offset + frame * bytesPerFrame + ch * bytesPerSample;
                var s = ReadNormalizedSample(buffer, pos, bytesPerSample, isFloat);
                frameSumSq += s * s;
            }
            sumSq += frameSumSq / _format.Channels;
        }

        var rms = Math.Sqrt(sumSq / frameCount);
        return rms > 0 ? Math.Max(20 * Math.Log10(rms), -100.0) : -100.0;
    }

    static double ReadNormalizedSample(byte[] buffer, int offset, int bytesPerSample, bool isFloat)
        => bytesPerSample switch
        {
            2 => (short)(buffer[offset] | (buffer[offset + 1] << 8)) / 32768.0,
            3 => (buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16)) / 8388608.0,
            4 when isFloat => BitConverter.ToSingle(buffer, offset),
            4 => BitConverter.ToInt32(buffer, offset) / 2147483648.0,
            _ => 0.0
        };
}
