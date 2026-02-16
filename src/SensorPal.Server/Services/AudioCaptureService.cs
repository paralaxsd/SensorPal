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
    AudioStorage storage,
    ILogger<AudioCaptureService> logger) : IHostedService, IDisposable
{
    readonly AudioConfig _config = options.Value;
    readonly int _postRollMs = options.Value.PostRollSeconds * 1000;

    WasapiCapture? _capture;
    LameMP3FileWriter? _backgroundWriter;
    WaveFileWriter? _clipWriter;
    CircularAudioBuffer? _preRoll;
    NoiseDetector? _detector;
    WaveFormat? _captureFormat;

    long _currentSessionId;
    DateTime _backgroundStart;
    DateTime _currentEventStart;
    bool _recordingClip;
    int _postRollRemainingMs;

    public double? CurrentDb => _detector?.CurrentDb;

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
        var device = FindDevice(_config.DeviceName);
        _capture = new WasapiCapture(device);
        _captureFormat = _capture.WaveFormat;

        var preRollBytes = _captureFormat.AverageBytesPerSecond * _config.PreRollSeconds;
        _preRoll = new CircularAudioBuffer(preRollBytes);
        _detector = new NoiseDetector(_config.NoiseThresholdDb, _captureFormat);

        _detector.EventStarted += OnEventStarted;
        _detector.EventEnded += OnEventEnded;

        _backgroundStart = DateTime.UtcNow;
        var backgroundFile = storage.GetBackgroundFilePath(DateOnly.FromDateTime(_backgroundStart.ToLocalTime()));
        _backgroundWriter = new LameMP3FileWriter(backgroundFile, _captureFormat, _config.BackgroundBitrate);

        _currentSessionId = await sessions.StartSessionAsync(backgroundFile);

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();

        logger.LogInformation("Recording started on '{Device}', session #{Id}", device.FriendlyName, _currentSessionId);
    }

    void StopCapture()
    {
        if (_capture is null) return;

        _capture.StopRecording();
        _capture.DataAvailable -= OnDataAvailable;

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

        var msRecorded = (int)(e.BytesRecorded * 1000.0 / _captureFormat!.AverageBytesPerSecond);
        _postRollRemainingMs -= msRecorded;

        if (_postRollRemainingMs <= 0)
            FinishClipIfActive();
    }

    void OnEventStarted(DateTime startedAt, double peakDb)
    {
        if (_recordingClip) return;

        _currentEventStart = startedAt;
        _recordingClip = true;
        _postRollRemainingMs = _postRollMs;

        // Use id=0 as placeholder; will be renamed after DB insert
        var placeholderPath = storage.GetClipFilePath(0, startedAt);
        _clipWriter = new WaveFileWriter(placeholderPath, _captureFormat!);

        var preRollData = _preRoll!.GetSnapshot();
        _clipWriter.Write(preRollData, 0, preRollData.Length);

        logger.LogInformation("Noise event started at {Time}, peak {Db:F1} dBFS", startedAt, peakDb);
    }

    void OnEventEnded(DateTime startedAt, double peakDb, int durationMs)
    {
        _postRollRemainingMs = _postRollMs;
        logger.LogInformation("Noise event ended, duration {Duration}ms, peak {Db:F1} dBFS", durationMs, peakDb);
        _ = PersistEventAsync(startedAt, peakDb, durationMs);
    }

    void FinishClipIfActive()
    {
        if (!_recordingClip) return;
        _recordingClip = false;
        _clipWriter?.Dispose();
        _clipWriter = null;
    }

    async Task PersistEventAsync(DateTime startedAt, double peakDb, int durationMs)
    {
        _postRollRemainingMs = 0;
        FinishClipIfActive();

        var offsetMs = (long)(startedAt - _backgroundStart).TotalMilliseconds;
        var placeholderPath = storage.GetClipFilePath(0, startedAt);

        var id = await events.SaveEventAsync(_currentSessionId, startedAt, peakDb, durationMs, offsetMs, null);

        var finalPath = storage.GetClipFilePath(id, startedAt);
        if (File.Exists(placeholderPath))
            File.Move(placeholderPath, finalPath, overwrite: true);

        await events.UpdateClipFileAsync(id, finalPath);
        await sessions.IncrementEventCountAsync(_currentSessionId);
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
