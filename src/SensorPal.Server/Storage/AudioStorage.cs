namespace SensorPal.Server.Storage;

sealed class AudioStorage
{
    readonly string _storagePath;
    readonly string _clipsPath;

    public AudioStorage(string configuredPath)
    {
        _storagePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        _clipsPath = Path.Combine(_storagePath, "clips");

        Directory.CreateDirectory(_storagePath);
        Directory.CreateDirectory(_clipsPath);
    }

    public string GetBackgroundFilePath(DateTime startedAt) =>
        Path.Combine(_storagePath, $"{startedAt.ToLocalTime():yyyy-MM-dd_HHmmss}_background.mp3");

    public string GetClipFilePath(long eventId, DateTime time) =>
        Path.Combine(_clipsPath, $"{time.ToLocalTime():yyyy-MM-dd_HHmmss}_{eventId}.wav");

    public string DatabasePath => Path.Combine(_storagePath, "sensorpal.db");
}
