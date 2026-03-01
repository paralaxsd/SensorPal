using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NAudio.Wave;
using SensorPal.Server.Services;
using SensorPal.Server.Storage;

namespace SensorPal.IntegrationTests;

/// <summary>
/// Test substitute for AudioCaptureService. Acts as a no-op when idle, but:
/// <list type="bullet">
///   <item>On calibration start: creates a real NoiseDetector so tests can inject audio
///     samples and observe the resulting dBFS level via the /monitoring/level endpoint.</item>
///   <item>On monitoring start: creates a session in the database to enable full lifecycle
///     testing of the /monitoring/sessions and /monitoring/{id} endpoints.</item>
/// </list>
/// </summary>
sealed class ControllableCaptureService : IAudioCaptureService, IHostedService
{
    static readonly WaveFormat Format = new WaveFormat(44100, 16, 1);

    readonly SessionRepository _sessions;
    readonly FakeTimeProvider _time;
    NoiseDetector? _detector;

    bool _isMonitoring;
    long _currentSessionId;
    DateTime _sessionStartedAt;

    public double? CurrentDb => _detector?.CurrentDb;
    public bool IsEventActive => _detector?.IsEventActive ?? false;
    public DateTime? EventStartedAt => _detector?.EventStartedAt;
    public bool IsCalibrating { get; private set; }

    public long? ActiveSessionId => _isMonitoring ? _currentSessionId : null;
    public DateTimeOffset? ActiveSessionStartedAt =>
        _isMonitoring ? new DateTimeOffset(_sessionStartedAt, TimeSpan.Zero) : null;
    public int? ActiveSessionEventCount => _isMonitoring ? 0 : null;

    public ControllableCaptureService(
        MonitoringStateService state, SessionRepository sessions, FakeTimeProvider time)
    {
        _sessions = sessions;
        _time = time;
        state.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Feed raw PCM bytes into the detector. Only has effect during calibration or monitoring.
    /// </summary>
    public void InjectAudio(byte[] data) => _detector?.Process(data, 0, data.Length);

    void OnStateChanged(MonitoringState next)
    {
        switch (next)
        {
            case MonitoringState.Monitoring:
                _detector = new NoiseDetector(-30.0, Format, _time);
                _sessionStartedAt = _time.GetUtcNow().UtcDateTime;
                _currentSessionId = _sessions.StartSessionAsync("stub.mp3").GetAwaiter().GetResult();
                _isMonitoring = true;
                break;

            case MonitoringState.Calibrating:
                _detector = new NoiseDetector(-30.0, Format, _time);
                IsCalibrating = true;
                break;

            case MonitoringState.Idle:
                if (_isMonitoring)
                {
                    _isMonitoring = false;
                    _sessions.EndSessionAsync(_currentSessionId).GetAwaiter().GetResult();
                }
                IsCalibrating = false;
                _detector = null;
                break;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
