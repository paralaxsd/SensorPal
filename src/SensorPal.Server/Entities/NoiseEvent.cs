namespace SensorPal.Server.Entities;

sealed class NoiseEvent
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public DateTime DetectedAt { get; set; }
    public double PeakDb { get; set; }
    public int DurationMs { get; set; }
    public int ClipDurationMs { get; set; }
    public long BackgroundOffsetMs { get; set; }
    public string? ClipFile { get; set; }

    public MonitoringSession Session { get; set; } = null!;
}
