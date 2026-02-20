using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;
using SensorPal.Server.Configuration;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Services;

sealed class AudioCaptureService(
    IOptions<AudioConfig> options,
    MonitoringStateService stateService,
    SessionRepository sessions,
    EventRepository events,
    SettingsRepository settings,
    AudioStorage storage,
    ILogger<AudioCaptureService> logger) : IHostedService, IDisposable
{
    readonly AudioConfig _config = options.Value;
    int _postRollMs;

    WasapiCapture? _capture;
    LameMP3FileWriter? _backgroundWriter;
    WaveFileWriter? _clipWriter;
    CircularAudioBuffer? _preRoll;
    NoiseDetector? _detector;
    WaveFormat? _captureFormat;

    long _currentSessionId;
    DateTime _backgroundStart;
    int _sessionEventCount;

    // Clip state
    bool _recordingClip;
    bool _inPostRoll;
    int _postRollRemainingMs;

    // Pending event data — held until post-roll expires and clip is finalized
    record struct PendingEvent(DateTime StartedAt, double PeakDb, int DurationMs);
    PendingEvent? _pendingEvent;

    public double? CurrentDb => _detector?.CurrentDb;
    public bool IsEventActive => _detector?.IsEventActive ?? false;
    public DateTime? EventStartedAt => _detector?.EventStartedAt;
    public DateTimeOffset? ActiveSessionStartedAt =>
        _capture is not null ? new DateTimeOffset(_backgroundStart, TimeSpan.Zero) : null;
    public int? ActiveSessionEventCount => _capture is not null ? _sessionEventCount : null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        stateService.MonitoringStarted += OnMonitoringStarted;
        stateService.MonitoringStopped += OnMonitoringStopped;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        stateService.MonitoringStarted -= OnMonitoringStarted;
        stateService.MonitoringStopped -= OnMonitoringStopped;
        StopCapture();
        return Task.CompletedTask;
    }

    void OnMonitoringStarted() => Task.Run(StartCaptureAsync);

    void OnMonitoringStopped() => StopCapture();

    async Task StartCaptureAsync()
    {
        var s = await settings.GetAsync();
        _postRollMs = s.PostRollSeconds * 1000;

        var device = FindDevice(_config.DeviceName);
        _capture = new WasapiCapture(device);
        _captureFormat = _capture.WaveFormat;

        var preRollBytes = _captureFormat.AverageBytesPerSecond * s.PreRollSeconds;
        _preRoll = new CircularAudioBuffer(preRollBytes);
        _detector = new NoiseDetector(s.NoiseThresholdDb, _captureFormat);

        _detector.EventStarted += OnEventStarted;
        _detector.EventEnded += OnEventEnded;

        _sessionEventCount = 0;
        _backgroundStart = DateTime.UtcNow;
        var backgroundFile = storage.GetBackgroundFilePath(_backgroundStart);
        _backgroundWriter = new LameMP3FileWriter(backgroundFile, _captureFormat, s.BackgroundBitrate);

        _currentSessionId = await sessions.StartSessionAsync(backgroundFile);

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();

        logger.LogInformation(
            "Recording started on '{Device}', session #{Id}, format: {Ch}ch {Rate}Hz {Bits}bit {Enc}",
            device.FriendlyName, _currentSessionId,
            _captureFormat.Channels, _captureFormat.SampleRate,
            _captureFormat.BitsPerSample, _captureFormat.Encoding);
    }

    void StopCapture()
    {
        if (_capture is null) return;

        _capture.StopRecording();
        _capture.DataAvailable -= OnDataAvailable;

        if (_detector is not null)
        {
            _detector.EventStarted -= OnEventStarted;
            _detector.EventEnded -= OnEventEnded;
            _detector = null;
        }

        FinishClipIfActive();

        _backgroundWriter?.Flush();
        _backgroundWriter?.Dispose();
        _backgroundWriter = null;

        _capture.Dispose();
        _capture = null;

        _ = sessions.EndSessionAsync(_currentSessionId);
        logger.LogInformation("Recording stopped, session #{Id} closed", _currentSessionId);
    }

    void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _preRoll!.Add(e.Buffer, 0, e.BytesRecorded);
        _backgroundWriter!.Write(e.Buffer, 0, e.BytesRecorded);
        _detector!.Process(e.Buffer, 0, e.BytesRecorded);

        if (!_recordingClip) return;

        _clipWriter!.Write(e.Buffer, 0, e.BytesRecorded);

        if (!_inPostRoll) return;

        var msRecorded = (int)(e.BytesRecorded * 1000.0 / _captureFormat!.AverageBytesPerSecond);
        _postRollRemainingMs -= msRecorded;

        if (_postRollRemainingMs <= 0)
            FinishClipIfActive();
    }

    void OnEventStarted(DateTime startedAt, double peakDb)
    {
        if (_recordingClip) return;

        _inPostRoll = false;
        _recordingClip = true;

        var placeholderPath = storage.GetClipFilePath(0, startedAt);
        _clipWriter = new WaveFileWriter(placeholderPath, _captureFormat!);

        var preRollData = _preRoll!.GetSnapshot();
        _clipWriter.Write(preRollData, 0, preRollData.Length);

        logger.LogInformation("Noise event started at {Time}, peak {Db:F1} dBFS", startedAt, peakDb);
    }

    void OnEventEnded(DateTime startedAt, double peakDb, int durationMs)
    {
        // Hold event data until post-roll expires and clip is finalized
        _pendingEvent = new PendingEvent(startedAt, peakDb, durationMs);
        _inPostRoll = true;
        _postRollRemainingMs = _postRollMs;

        logger.LogInformation("Noise event ended, duration {Duration}ms, peak {Db:F1} dBFS", durationMs, peakDb);
    }

    void FinishClipIfActive()
    {
        if (!_recordingClip) return;

        _inPostRoll = false;
        _recordingClip = false;

        // Compute clip duration from bytes written before disposing the writer
        var clipDurationMs = 0;
        if (_clipWriter is not null && _captureFormat is not null)
        {
            var audioBytes = Math.Max(0L, _clipWriter.Length - 44); // subtract WAV header
            clipDurationMs = (int)(audioBytes * 1000L / _captureFormat.AverageBytesPerSecond);
        }

        _clipWriter?.Dispose();
        _clipWriter = null;

        if (_pendingEvent is { } evt)
        {
            _pendingEvent = null;
            _ = PersistEventAsync(evt.StartedAt, evt.PeakDb, evt.DurationMs, clipDurationMs);
        }
    }

    async Task PersistEventAsync(DateTime startedAt, double peakDb, int durationMs, int clipDurationMs)
    {
        var offsetMs = (long)(startedAt - _backgroundStart).TotalMilliseconds;
        var placeholderPath = storage.GetClipFilePath(0, startedAt);

        var id = await events.SaveEventAsync(_currentSessionId, startedAt, peakDb, durationMs, clipDurationMs, offsetMs, null);

        var finalPath = storage.GetClipFilePath(id, startedAt);
        if (File.Exists(placeholderPath))
            File.Move(placeholderPath, finalPath, overwrite: true);

        await events.UpdateClipFileAsync(id, finalPath);
        await sessions.IncrementEventCountAsync(_currentSessionId);
        _sessionEventCount++;
    }

    static MMDevice FindDevice(string nameSubstring)
    {
        using var enumerator = new MMDeviceEnumerator();

        if (string.IsNullOrEmpty(nameSubstring))
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase))
            ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    }

    public void Dispose()
    {
        _backgroundWriter?.Dispose();
        _clipWriter?.Dispose();
        _capture?.Dispose();
    }
}
