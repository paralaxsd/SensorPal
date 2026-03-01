using System.Text.Json.Serialization;

namespace SensorPal.Server;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(StatusDto))]
[JsonSerializable(typeof(NoiseEventDto))]
[JsonSerializable(typeof(IReadOnlyList<NoiseEventDto>))]
[JsonSerializable(typeof(MonitoringSessionDto))]
[JsonSerializable(typeof(IReadOnlyList<MonitoringSessionDto>))]
[JsonSerializable(typeof(LiveSessionStatsDto))]
[JsonSerializable(typeof(LiveLevelDto))]
[JsonSerializable(typeof(AudioDeviceDto))]
[JsonSerializable(typeof(IReadOnlyList<AudioDeviceDto>))]
[JsonSerializable(typeof(SettingsDto))]
[JsonSerializable(typeof(EventMarkerDto))]
[JsonSerializable(typeof(IReadOnlyList<EventMarkerDto>))]
[JsonSerializable(typeof(DateOnly[]))]
[JsonSerializable(typeof(DeleteEventResultDto))]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(NightlyStatDto))]
[JsonSerializable(typeof(HourlyStatDto))]
[JsonSerializable(typeof(StatsSummaryDto))]
[JsonSerializable(typeof(List<NightlyStatDto>))]
[JsonSerializable(typeof(List<HourlyStatDto>))]
partial class AppJsonSerializerContext : JsonSerializerContext
{
}
