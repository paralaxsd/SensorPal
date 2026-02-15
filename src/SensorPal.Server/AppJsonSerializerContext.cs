using System.Text.Json.Serialization;

namespace SensorPal.Server;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(StatusDto))]
[JsonSerializable(typeof(NoiseEventDto))]
[JsonSerializable(typeof(IReadOnlyList<NoiseEventDto>))]
[JsonSerializable(typeof(MonitoringSessionDto))]
[JsonSerializable(typeof(IReadOnlyList<MonitoringSessionDto>))]
[JsonSerializable(typeof(LiveSessionStatsDto))]
[JsonSerializable(typeof(AudioDeviceDto))]
[JsonSerializable(typeof(IReadOnlyList<AudioDeviceDto>))]
partial class AppJsonSerializerContext : JsonSerializerContext
{
}
