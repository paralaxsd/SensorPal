using System.Text.Json.Serialization;

namespace SensorPal.Server;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(StatusDto))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
