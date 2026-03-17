namespace SensorPal.Server.Entities;

sealed class AppSettings
{
    public int Id { get; set; } = 1; // singleton row

    public double NoiseThresholdDb { get; set; }
    public int PreRollSeconds { get; set; }
    public int PostRollSeconds { get; set; }
    public int BackgroundBitrate { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>
    /// Stored as "HH:mm" string. Null disables auto-stop.
    /// </summary>
    public string? AutoStopTime { get; set; }
}
