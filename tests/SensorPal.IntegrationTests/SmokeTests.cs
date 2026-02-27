using Alba;
using SensorPal.Shared.Models;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SensorPal.IntegrationTests;

public class SmokeTests(AppFixture app) : IClassFixture<AppFixture>
{
    [Fact]
    public Task GetStatus_Returns200() =>
        app.Host.Scenario(s =>
        {
            s.Get.Url("/status");
            s.StatusCodeShouldBeOk();
        });

    [Fact]
    public async Task MonitoringLifecycle_TogglesMode_AndMonitoringEndpointsRespond()
    {
        using var client = app.Host.Server.CreateClient();

        var before = await GetModeAsync(client);
        Assert.Equal("Idle", before);

        var start = await client.PostAsync("/monitoring/start", content: null);
        start.EnsureSuccessStatusCode();

        var during = await GetModeAsync(client);
        Assert.Equal("Monitoring", during);

        var level = await client.GetAsync("/monitoring/level");
        level.EnsureSuccessStatusCode();

        var sessions = await client.GetFromJsonAsync<List<MonitoringSessionDto>>(
            "/monitoring/sessions");
        Assert.NotNull(sessions);

        var stop = await client.PostAsync("/monitoring/stop", content: null);
        stop.EnsureSuccessStatusCode();

        var after = await GetModeAsync(client);
        Assert.Equal("Idle", after);
    }

    static async Task<string?> GetModeAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<JsonObject>("/status");
        return status?["mode"]?.GetValue<string>();
    }
}
