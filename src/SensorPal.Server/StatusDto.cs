namespace SensorPal.Server;

public class StatusDto
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset Now { get; set; }
    public string Mode { get; set; } = string.Empty;
}
