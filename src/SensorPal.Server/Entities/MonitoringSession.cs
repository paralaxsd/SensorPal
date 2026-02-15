namespace SensorPal.Server.Entities;

sealed class MonitoringSession
{
    public long Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int EventCount { get; set; }
    public string? BackgroundFile { get; set; }

    public ICollection<NoiseEvent> Events { get; set; } = [];
}
