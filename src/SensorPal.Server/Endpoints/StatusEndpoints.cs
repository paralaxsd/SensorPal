using SensorPal.Server.Services;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Endpoints;

static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app, TimeProvider time)
    {
        var startedAt = time.GetLocalNow();

        app.MapGet("/status", async (MonitoringStateService state, SettingsRepository settings) =>
        {
            var s = await settings.GetAsync();
            return new StatusDto
            {
                Name = "SensorPal",
                StartedAt = startedAt,
                Now = time.GetLocalNow(),
                Mode = state.State.ToString(),
                AutoStopTime = s.AutoStopTime,
            };
        })
        .WithSummary("Server health and current monitoring mode")
        .WithDescription("Returns the server name, start time, current time and monitoring state (Idle / Monitoring). " +
            "Used by the mobile app as a connectivity ping.");
    }
}
