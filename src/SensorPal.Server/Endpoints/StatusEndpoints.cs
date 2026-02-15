using SensorPal.Server.Services;

namespace SensorPal.Server.Endpoints;

static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var startedAt = DateTimeOffset.Now;

        app.MapGet("/status", (MonitoringStateService state) => new StatusDto
        {
            Name = "SensorPal",
            StartedAt = startedAt,
            Now = DateTimeOffset.Now,
            Mode = state.State.ToString()
        });
    }
}
