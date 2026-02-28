namespace SensorPal.Server.Services;

/// <summary>
/// Read-only view of the audio capture service consumed by API endpoints.
/// Abstracts the concrete WASAPI implementation so tests can substitute a no-op.
/// </summary>
interface IAudioCaptureService
{
    double? CurrentDb { get; }
    bool IsEventActive { get; }
    DateTime? EventStartedAt { get; }
    long? ActiveSessionId { get; }
    DateTimeOffset? ActiveSessionStartedAt { get; }
    int? ActiveSessionEventCount { get; }
    bool IsCalibrating { get; }
}
