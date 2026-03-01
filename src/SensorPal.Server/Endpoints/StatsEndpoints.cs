using SensorPal.Server.Storage;

namespace SensorPal.Server.Endpoints;

static class StatsEndpoints
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/stats");

        group.MapGet("/", async (StatsRepository repo, TimeProvider time, DateOnly? from, DateOnly? to) =>
        {
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var f = from ?? today.AddDays(-29);
            var t = to   ?? today;
            return Results.Ok(await repo.GetStatsAsync(f, t));
        })
        .WithSummary("Nightly and hourly event statistics for a date range")
        .WithDescription(
            "Returns per-night event counts/dB and 24-hour distribution. " +
            "Defaults to the last 30 days when from/to are omitted.");
    }
}
