using Alba;
using Shouldly;
using SensorPal.Shared.Models;
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
        (await GetModeAsync()).ShouldBe("Idle");

        await app.Host.Scenario(s =>
        {
            s.Post.Url("/monitoring/start");
            s.StatusCodeShouldBeOk();
        });

        (await GetModeAsync()).ShouldBe("Monitoring");

        await app.Host.Scenario(s =>
        {
            s.Get.Url("/monitoring/level");
            s.StatusCodeShouldBeOk();
        });

        var sessionsResult = await app.Host.Scenario(s =>
        {
            s.Get.Url("/monitoring/sessions");
            s.StatusCodeShouldBeOk();
        });
        sessionsResult.ReadAsJson<List<MonitoringSessionDto>>().ShouldNotBeNull();

        await app.Host.Scenario(s =>
        {
            s.Post.Url("/monitoring/stop");
            s.StatusCodeShouldBeOk();
        });

        (await GetModeAsync()).ShouldBe("Idle");
    }

    async Task<string?> GetModeAsync()
    {
        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/status");
            s.StatusCodeShouldBeOk();
        });
        return result.ReadAsJson<JsonObject>()?["mode"]?.GetValue<string>();
    }
}
