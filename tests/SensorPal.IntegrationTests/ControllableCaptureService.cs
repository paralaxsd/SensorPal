using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NAudio.Wave;
using SensorPal.Server.Services;

namespace SensorPal.IntegrationTests;

/// <summary>
/// Test substitute for AudioCaptureService. Acts as a no-op when idle, but
/// on calibration start creates a real NoiseDetector so tests can inject audio
/// samples and observe the resulting dBFS level via the /monitoring/level endpoint.
/// </summary>
sealed class ControllableCaptureService : IAudioCaptureService, IHostedService
{
    static readonly WaveFormat Format = new WaveFormat(44100, 16, 1);

    readonly FakeTimeProvider _time;
    NoiseDetector? _detector;

    public double? CurrentDb => _detector?.CurrentDb;
    public bool IsEventActive => _detector?.IsEventActive ?? false;
    public DateTime? EventStartedAt => _detector?.EventStartedAt;
    public long? ActiveSessionId => null;
    public DateTimeOffset? ActiveSessionStartedAt => null;
    public int? ActiveSessionEventCount => null;
    public bool IsCalibrating { get; private set; }

    public ControllableCaptureService(MonitoringStateService state, FakeTimeProvider time)
    {
        _time = time;
        state.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Feed raw PCM bytes into the detector. Only has effect during calibration.
    /// </summary>
    public void InjectAudio(byte[] data) => _detector?.Process(data, 0, data.Length);

    void OnStateChanged(MonitoringState next)
    {
        switch (next)
        {
            case MonitoringState.Calibrating:
                _detector = new NoiseDetector(-30.0, Format, _time);
                IsCalibrating = true;
                break;
            case MonitoringState.Idle:
                IsCalibrating = false;
                _detector = null;
                break;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
