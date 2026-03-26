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

    // Mono format derived from _captureFormat — used for all writers.
    // _captureFormat may be stereo (e.g. Focusrite Scarlett reports 2ch even for a mono mic);
    // _writeFormat is always 1ch so the MP3 and WAV clips contain proper mono audio.
    WaveFormat? _writeFormat;

    long _currentSessionId;
    DateTime _backgroundStart;
    int _sessionEventCount;

    bool _calibrating;

    // Guards concurrent access between the WASAPI capture thread (OnDataAvailable) and
    // StopCapture / StopCalibrate. Prevents disposing writers while a write is in progress.
    readonly Lock _writeLock = new();

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
        var preRollBytes = _writeFormat!.AverageBytesPerSecond * s.PreRollSeconds;
        var postRollMs = s.PostRollSeconds * 1000;

        _sessionEventCount = 0;
        _backgroundStart = time.GetUtcNow().UtcDateTime;
        var backgroundFile = storage.GetBackgroundFilePath(_backgroundStart);
        _backgroundWriter = new LameMP3FileWriter(backgroundFile, _writeFormat, s.BackgroundBitrate);

        _currentSessionId = await sessions.StartSessionAsync(backgroundFile);

        _clipRecorder = new ClipRecorder(
            _currentSessionId, _backgroundStart,
            _writeFormat, preRollBytes, postRollMs,
            events, sessions, storage, clipLogger);
        _clipRecorder.ClipPersisted += () => _sessionEventCount++;

        _detector!.EventStarted += _clipRecorder.OnEventStarted;
        _detector.EventEnded += _clipRecorder.OnEventEnded;

        _capture!.DataAvailable += OnDataAvailable;
        _capture.StartRecording();

        logger.LogInformation(
            "Recording started on '{Device}', session #{Id}, format: {Ch}ch {Rate}Hz {Bits}bit {Enc}",
            deviceName, _currentSessionId,
            _captureFormat!.Channels, _captureFormat.SampleRate,
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

        // Acquire the write lock before disposing: ensures any OnDataAvailable call that was
        // already in progress finishes its Write() before we tear down the writers.
        // Without this, the WASAPI capture thread can hold a reference to a disposed
        // LameMP3FileWriter whose internal native callback delegate has been GC'd → crash.
        lock (_writeLock)
        {
            _clipRecorder?.FinishClipIfActive();
            _clipRecorder?.Dispose();
            _clipRecorder = null;

            _backgroundWriter?.Flush();
            _backgroundWriter?.Dispose();
            _backgroundWriter = null;
        }

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

        // Derive a mono write format — WASAPI devices often report stereo even when only
        // one channel carries a mic signal. Writers use this; detector keeps stereo so that
        // threshold calibration is unaffected.
        _writeFormat = _captureFormat.Channels == 1
            ? _captureFormat
            : _captureFormat.Encoding == WaveFormatEncoding.IeeeFloat
                ? WaveFormat.CreateIeeeFloatWaveFormat(_captureFormat.SampleRate, 1)
                : new WaveFormat(_captureFormat.SampleRate, _captureFormat.BitsPerSample, 1);

        _detector = new NoiseDetector(s.NoiseThresholdDb, _captureFormat, time);
        return (s, device.FriendlyName);
    }

    void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Detector always receives the native (possibly stereo) capture data so that
        // threshold calibration remains consistent regardless of channel count.
        _detector!.Process(e.Buffer, 0, e.BytesRecorded);

        if (_calibrating) return;

        // Writers receive channel-0 mono data — prevents silent right channel from
        // producing stereo recordings when a mono mic is connected to input 1.
        var (monoBuffer, monoBytes) = _captureFormat!.Channels > 1
            ? ExtractChannel(e.Buffer, e.BytesRecorded, channel: 0)
            : (e.Buffer, e.BytesRecorded);

        // Hold the write lock for the duration of the write. StopCapture acquires the same
        // lock before disposing the writers, so it will wait until this call completes.
        lock (_writeLock)
        {
            // Guard in case DataAvailable fires one last time after unsubscription races
            // with StopCapture setting writers to null.
            if (_backgroundWriter is null) return;

            _backgroundWriter.Write(monoBuffer, 0, monoBytes);
            _clipRecorder!.ProcessData(monoBuffer, 0, monoBytes);
        }
    }

    // Extracts a single channel from an interleaved multi-channel buffer.
    // Works for any sample width (PCM 16/24/32-bit, IEEE float 32-bit).
    (byte[] buffer, int count) ExtractChannel(byte[] buffer, int byteCount, int channel)
    {
        var bytesPerSample = _captureFormat!.BitsPerSample / 8;
        var channels = _captureFormat.Channels;
        var frameCount = byteCount / (bytesPerSample * channels);
        var mono = new byte[frameCount * bytesPerSample];

        for (var f = 0; f < frameCount; f++)
        {
            var src = f * channels * bytesPerSample + channel * bytesPerSample;
            var dst = f * bytesPerSample;
            Buffer.BlockCopy(buffer, src, mono, dst, bytesPerSample);
        }

        return (mono, mono.Length);
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
