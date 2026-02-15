namespace SensorPal.Server.Configuration;

sealed class AudioConfig
{
    /// <summary>
    /// Substring match against the audio device's friendly name.
    /// Leave empty to use the system default capture device.
    /// Configure via user secrets: AudioConfig:DeviceName
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    public int SampleRate { get; init; } = 44100;
    public int Channels { get; init; } = 1;
    public int BackgroundBitrate { get; init; } = 64;
    public int ClipBitrate { get; init; } = 128;
    public double NoiseThresholdDb { get; init; } = -30.0;
    public int PreRollSeconds { get; init; } = 30;
    public int PostRollSeconds { get; init; } = 30;

    /// <summary>
    /// Relative paths are resolved from the server executable directory.
    /// </summary>
    public string StoragePath { get; init; } = "recordings";
}
