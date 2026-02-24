using NAudio.Wave;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Services;

/// <summary>
/// Manages WAV clip recording around noise events: pre-roll buffering,
/// live writing, post-roll countdown, and event persistence to the database.
/// One instance per monitoring session — create at session start, dispose at session end.
/// </summary>
sealed class ClipRecorder(
    long sessionId, DateTime backgroundStart,
    WaveFormat captureFormat, int preRollBytes, int postRollMs,
    EventRepository events, SessionRepository sessions,
    AudioStorage storage, ILogger<ClipRecorder> logger) : IDisposable
{
    record struct PendingEvent(DateTime StartedAt, double PeakDb, int DurationMs);

    readonly CircularAudioBuffer _preRoll = new(preRollBytes);

    WaveFileWriter? _clipWriter;
    bool _recordingClip;
    bool _inPostRoll;
    int _postRollRemainingMs;
    PendingEvent? _pendingEvent;

    /// <summary>Raised after each clip is fully written and persisted to the database.</summary>
    public event Action? ClipPersisted;

    /// <summary>
    /// Feeds audio data into the pre-roll buffer and, when a clip is active, into the WAV writer.
    /// Must be called for every audio chunk regardless of whether a clip is recording.
    /// </summary>
    public void ProcessData(byte[] buffer, int offset, int count)
    {
        _preRoll.Add(buffer, offset, count);

        if (!_recordingClip) return;

        _clipWriter!.Write(buffer, offset, count);

        if (!_inPostRoll) return;

        var msRecorded = (int)(count * 1000.0 / captureFormat.AverageBytesPerSecond);
        _postRollRemainingMs -= msRecorded;

        if (_postRollRemainingMs <= 0)
            FinishClipIfActive();
    }

    public void OnEventStarted(DateTime startedAt, double peakDb)
    {
        if (_recordingClip) return;

        _inPostRoll = false;
        _recordingClip = true;

        var placeholderPath = storage.GetClipFilePath(0, startedAt);
        _clipWriter = new WaveFileWriter(placeholderPath, captureFormat);

        var preRollData = _preRoll.GetSnapshot();
        _clipWriter.Write(preRollData, 0, preRollData.Length);

        logger.LogInformation("Noise event started at {Time}, peak {Db:F1} dBFS", startedAt, peakDb);
    }

    public void OnEventEnded(DateTime startedAt, double peakDb, int durationMs)
    {
        _pendingEvent = new PendingEvent(startedAt, peakDb, durationMs);
        _inPostRoll = true;
        _postRollRemainingMs = postRollMs;

        logger.LogInformation("Noise event ended, duration {Duration}ms, peak {Db:F1} dBFS", durationMs, peakDb);
    }

    public void FinishClipIfActive()
    {
        if (!_recordingClip) return;

        _inPostRoll = false;
        _recordingClip = false;

        var clipDurationMs = 0;
        if (_clipWriter is { })
        {
            var audioBytes = Math.Max(0L, _clipWriter.Length - 44); // subtract WAV header
            clipDurationMs = (int)(audioBytes * 1000L / captureFormat.AverageBytesPerSecond);
        }

        _clipWriter?.Dispose();
        _clipWriter = null;

        if (_pendingEvent is { } evt)
        {
            _pendingEvent = null;
            _ = PersistEventAsync(evt.StartedAt, evt.PeakDb, evt.DurationMs, clipDurationMs);
        }
    }

    public void Dispose() => _clipWriter?.Dispose();

    async Task PersistEventAsync(DateTime startedAt, double peakDb, int durationMs, int clipDurationMs)
    {
        var offsetMs = (long)(startedAt - backgroundStart).TotalMilliseconds;
        var placeholderPath = storage.GetClipFilePath(0, startedAt);

        var id = await events.SaveEventAsync(
            sessionId, startedAt, peakDb, durationMs, clipDurationMs, offsetMs, null);

        var finalPath = storage.GetClipFilePath(id, startedAt);
        if (File.Exists(placeholderPath))
            File.Move(placeholderPath, finalPath, overwrite: true);

        await events.UpdateClipFileAsync(id, finalPath);
        await sessions.IncrementEventCountAsync(sessionId);
        ClipPersisted?.Invoke();
    }
}
