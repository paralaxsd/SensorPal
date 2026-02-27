using System.ComponentModel.DataAnnotations;

namespace SensorPal.Mobile.Infrastructure;

public sealed class ServerConfig
{
    [Required]
    [Url]
    public required string BaseUrl { get; init; }
}