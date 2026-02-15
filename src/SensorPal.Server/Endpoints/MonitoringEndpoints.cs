using SensorPal.Server.Entities;
using SensorPal.Server.Services;
using SensorPal.Server.Storage;

namespace SensorPal.Server.Endpoints;

static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/monitoring");

        group.MapPost("/start", (MonitoringStateService state) =>
        {
            state.Start();
            return Results.Ok();
        });

        group.MapPost("/stop", (MonitoringStateService state) =>
        {
            state.Stop();
            return Results.Ok();
        });

        group.MapGet("/sessions", async (SessionRepository repo) =>
        {
            var sessions = await repo.GetSessionsAsync();
            return sessions.Select(ToDto).ToList();
        });
    }

    static MonitoringSessionDto ToDto(MonitoringSession s) => new()
    {
        Id = s.Id,
        StartedAt = s.StartedAt,
        EndedAt = s.EndedAt,
        EventCount = s.EventCount,
        IsActive = s.EndedAt is null
    };
}
