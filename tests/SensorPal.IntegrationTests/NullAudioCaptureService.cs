using Microsoft.Extensions.Hosting;
using SensorPal.Server.Services;

namespace SensorPal.IntegrationTests;

/// <summary>
/// No-op substitute for AudioCaptureService used in integration tests.
/// Avoids WASAPI hardware dependency; all properties return neutral defaults.
/// Replace with an NSubstitute mock in individual tests when you need to control
/// the values returned by /monitoring/level.
/// </summary>
sealed class NullAudioCaptureService : IAudioCaptureService, IHostedService
{
    public double? CurrentDb => null;
    public bool IsEventActive => false;
    public DateTime? EventStartedAt => null;
    public long? ActiveSessionId => null;
    public DateTimeOffset? ActiveSessionStartedAt => null;
    public int? ActiveSessionEventCount => null;
    public bool IsCalibrating => false;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
