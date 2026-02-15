namespace SensorPal.Server.Services;

/// <summary>
/// Thread-safe circular byte buffer for holding a fixed window of audio pre-roll data.
/// </summary>
sealed class CircularAudioBuffer
{
    readonly byte[] _buffer;
    int _writePos;
    bool _filled;
    readonly Lock _lock = new();

    public CircularAudioBuffer(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
    }

    public void Add(byte[] data, int offset, int count)
    {
        lock (_lock)
        {
            var remaining = count;
            var srcOffset = offset;

            while (remaining > 0)
            {
                var chunk = Math.Min(remaining, _buffer.Length - _writePos);
                Array.Copy(data, srcOffset, _buffer, _writePos, chunk);

                _writePos += chunk;
                srcOffset += chunk;
                remaining -= chunk;

                if (_writePos >= _buffer.Length)
                {
                    _writePos = 0;
                    _filled = true;
                }
            }
        }
    }

    /// <summary>Returns a contiguous snapshot of the buffer contents in chronological order.</summary>
    public byte[] GetSnapshot()
    {
        lock (_lock)
        {
            if (!_filled)
            {
                var partial = new byte[_writePos];
                Array.Copy(_buffer, 0, partial, 0, _writePos);
                return partial;
            }

            var snapshot = new byte[_buffer.Length];
            var tail = _buffer.Length - _writePos;
            Array.Copy(_buffer, _writePos, snapshot, 0, tail);
            Array.Copy(_buffer, 0, snapshot, tail, _writePos);
            return snapshot;
        }
    }
}
