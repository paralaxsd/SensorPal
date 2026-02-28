using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;
using SensorPal.Server.Configuration;
using SensorPal.Server.Entities;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Services;

sealed class AudioCaptureService(
    IOptions<AudioConfig> options, MonitoringStateService stateService, SessionRepository sessions,
    EventRepository events, SettingsRepository settings, AudioStorage storage, TimeProvider time,
    ILogger<AudioCaptureService> logger, ILogger<ClipRecorder> clipLogger)
    : IAudioCaptureService, IHostedService, IDisposable
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    readonly AudioConfig _config = options.Value;

    WasapiCapture? _capture;
    LameMP3FileWriter? _backgroundWriter;
    ClipRecorder? _clipRecorder;
    NoiseDetector? _detector;
    WaveFormat? _captureFormat;

    long _currentSessionId;
    DateTime _backgroundStart;
    int _sessionEventCount;

    bool _calibrating;

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    public double? CurrentDb => _detector?.CurrentDb;
    public bool IsEventActive => _detector?.IsEventActive ?? false;
    public DateTime? EventStartedAt => _detector?.EventStartedAt;
    public bool IsCalibrating => _calibrating;

    // Null during calibration — no session is active.
    public long? ActiveSessionId =>
        _capture is { } && !_calibrating ? _currentSessionId : null;
    public DateTimeOffset? ActiveSessionStartedAt =>
        _capture is { } && !_calibrating ? new DateTimeOffset(_backgroundStart, TimeSpan.Zero) : null;
    public int? ActiveSessionEventCount =>
        _capture is { } && !_calibrating ? _sessionEventCount : null;

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public Task StartAsync(CancellationToken cancellationToken)
    {
        stateService.StateChanged += OnStateChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        stateService.StateChanged -= OnStateChanged;
        if (_calibrating)
            StopCalibrate();
        else
            StopCapture();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _backgroundWriter?.Dispose();
        _clipRecorder?.Dispose();
        _capture?.Dispose();
    }

    void OnStateChanged(MonitoringState state)
    {
        switch (state)
        {
            case MonitoringState.Monitoring:   Task.Run(StartCaptureAsync);   break;
            case MonitoringState.Calibrating:  Task.Run(StartCalibrateAsync); break;
            case MonitoringState.Idle:
                if (_calibrating) StopCalibrate(); else StopCapture();
                break;
        }
    }

    // Shared teardown: stops and disposes the WASAPI capture and clears detector.
    void TeardownCapture()
    {
        _capture!.StopRecording();
        _capture.Dispose();
        _capture = null;
        _detector = null;
    }

    async Task StartCaptureAsync()
    {
        var (s, deviceName) = await InitCaptureAsync();
        var preRollBytes = _captureFormat!.AverageBytesPerSecond * s.PreRollSeconds;
        var postRollMs = s.PostRollSeconds * 1000;

        _sessionEventCount = 0;
        _backgroundStart = time.GetUtcNow().UtcDateTime;
        var backgroundFile = storage.GetBackgroundFilePath(_backgroundStart);
        _backgroundWriter = new LameMP3FileWriter(backgroundFile, _captureFormat, s.BackgroundBitrate);

        _currentSessionId = await sessions.StartSessionAsync(backgroundFile);

        _clipRecorder = new ClipRecorder(
            _currentSessionId, _backgroundStart,
            _captureFormat, preRollBytes, postRollMs,
            events, sessions, storage, clipLogger);
        _clipRecorder.ClipPersisted += () => _sessionEventCount++;

        _detector!.EventStarted += _clipRecorder.OnEventStarted;
        _detector.EventEnded += _clipRecorder.OnEventEnded;

        _capture!.DataAvailable += OnDataAvailable;
        _capture.StartRecording();

        logger.LogInformation(
            "Recording started on '{Device}', session #{Id}, format: {Ch}ch {Rate}Hz {Bits}bit {Enc}",
            deviceName, _currentSessionId,
            _captureFormat.Channels, _captureFormat.SampleRate,
            _captureFormat.BitsPerSample, _captureFormat.Encoding);
    }

    void StopCapture()
    {
        if (_capture is null) return;

        _capture.DataAvailable -= OnDataAvailable;

        if (_detector is { } detector && _clipRecorder is { } clipRecorder)
        {
            detector.EventStarted -= clipRecorder.OnEventStarted;
            detector.EventEnded -= clipRecorder.OnEventEnded;
        }

        _clipRecorder?.FinishClipIfActive();
        _clipRecorder?.Dispose();
        _clipRecorder = null;

        _backgroundWriter?.Flush();
        _backgroundWriter?.Dispose();
        _backgroundWriter = null;

        _ = sessions.EndSessionAsync(_currentSessionId);
        logger.LogInformation("Recording stopped, session #{Id} closed", _currentSessionId);

        TeardownCapture();
    }

    async Task StartCalibrateAsync()
    {
        var (_, deviceName) = await InitCaptureAsync();
        _calibrating = true;

        _capture!.DataAvailable += OnDataAvailable;
        _capture.StartRecording();

        logger.LogInformation(
            "Calibration started on '{Device}', format: {Ch}ch {Rate}Hz {Bits}bit",
            deviceName, _captureFormat!.Channels, _captureFormat.SampleRate,
            _captureFormat.BitsPerSample);
    }

    void StopCalibrate()
    {
        if (_capture is null) return;

        _capture.DataAvailable -= OnDataAvailable;
        _calibrating = false;
        logger.LogInformation("Calibration stopped");

        TeardownCapture();
    }

    // Shared setup: opens WASAPI device, initialises format + detector.
    // Returns the settings and device friendly name for callers to use.
    async Task<(AppSettings s, string deviceName)> InitCaptureAsync()
    {
        var s = await settings.GetAsync();
        var device = FindDevice(_config.DeviceName);
        _capture = new WasapiCapture(device);
        _captureFormat = _capture.WaveFormat;
        _detector = new NoiseDetector(s.NoiseThresholdDb, _captureFormat, time);
        return (s, device.FriendlyName);
    }

    void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _detector!.Process(e.Buffer, 0, e.BytesRecorded);

        if (_calibrating) return;

        _backgroundWriter!.Write(e.Buffer, 0, e.BytesRecorded);
        _clipRecorder!.ProcessData(e.Buffer, 0, e.BytesRecorded);
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
}
