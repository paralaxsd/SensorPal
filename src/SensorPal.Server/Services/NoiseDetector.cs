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
        // 16-bit PCM only
        var sampleCount = count / 2;
        if (sampleCount == 0) return -100.0;

        double sumSq = 0;
        for (var i = offset; i < offset + count - 1; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            var normalized = sample / 32768.0;
            sumSq += normalized * normalized;
        }

        var rms = Math.Sqrt(sumSq / sampleCount);
        return rms < 1e-10 ? -100.0 : 20 * Math.Log10(rms);
    }
}
