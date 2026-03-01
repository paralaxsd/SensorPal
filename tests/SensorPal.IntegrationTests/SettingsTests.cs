using Alba;
using Shouldly;
using SensorPal.Shared.Models;
using Xunit;

namespace SensorPal.IntegrationTests;

public class SettingsTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => app.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetSettings_ReturnsCurrentValues()
    {
        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/settings");
            s.StatusCodeShouldBeOk();
        });

        var settings = result.ReadAsJson<SettingsDto>()!;
        settings.NoiseThresholdDb.ShouldNotBe(0.0); // initialized to a non-zero default
        settings.PreRollSeconds.ShouldBeGreaterThanOrEqualTo(0);
        settings.PostRollSeconds.ShouldBeGreaterThanOrEqualTo(0);
        settings.BackgroundBitrate.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task PutSettings_UpdatesAndPersists()
    {
        var updated = new SettingsDto(-25.0, 3, 7, 128);

        await app.Host.Scenario(s =>
        {
            s.Put.Url("/settings");
            s.WriteJson(updated, null);
            s.StatusCodeShouldBe(204);
        });

        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/settings");
            s.StatusCodeShouldBeOk();
        });

        var settings = result.ReadAsJson<SettingsDto>()!;
        settings.NoiseThresholdDb.ShouldBe(-25.0);
        settings.PreRollSeconds.ShouldBe(3);
        settings.PostRollSeconds.ShouldBe(7);
        settings.BackgroundBitrate.ShouldBe(128);
    }
}
