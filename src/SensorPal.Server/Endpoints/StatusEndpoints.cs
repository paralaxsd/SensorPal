using SensorPal.Server.Services;

namespace SensorPal.Server.Endpoints;

static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app, TimeProvider time)
    {
        var startedAt = time.GetLocalNow();

        app.MapGet("/status", (MonitoringStateService state) => new StatusDto
        {
            Name = "SensorPal",
            StartedAt = startedAt,
            Now = time.GetLocalNow(),
            Mode = state.State.ToString()
        })
        .WithSummary("Server health and current monitoring mode")
        .WithDescription("Returns the server name, start time, current time and monitoring state (Idle / Monitoring). " +
            "Used by the mobile app as a connectivity ping.");
    }
}
