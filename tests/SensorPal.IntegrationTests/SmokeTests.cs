using Alba;
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
}
