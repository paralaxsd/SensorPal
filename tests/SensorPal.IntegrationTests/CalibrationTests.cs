using Alba;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SensorPal.Server.Storage;
using System.Text.Json.Nodes;
using Xunit;

namespace SensorPal.IntegrationTests;

public class CalibrationTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => app.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Calibration state machine ─────────────────────────────────────────────

    [Fact]
    public async Task CalibrateStart_SetsIsCalibrating()
    {
        await app.Host.Scenario(s =>
        {
            s.Post.Url("/monitoring/calibrate/start");
            s.StatusCodeShouldBeOk();
        });

        var level = await GetLevelAsync();
        level!["isCalibrating"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task CalibrateStop_ClearsIsCalibrating()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/calibrate/start"); s.StatusCodeShouldBeOk(); });
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/calibrate/stop"); s.StatusCodeShouldBeOk(); });

        var level = await GetLevelAsync();
        level!["isCalibrating"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task CalibrateStart_WhenMonitoring_ReturnsConflict()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });

        await app.Host.Scenario(s =>
        {
            s.Post.Url("/monitoring/calibrate/start");
            s.StatusCodeShouldBe(409);
        });
    }

    // ── Signal level checks ───────────────────────────────────────────────────

    [Fact]
    public async Task CalibrateLevel_WithLoudSignal_ExceedsThreshold()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/calibrate/start"); s.StatusCodeShouldBeOk(); });

        app.Capture.InjectAudio(LoudPcm()); // -6 dBFS >> -30 dBFS threshold

        var level = await GetLevelAsync();
        var db = level!["db"]!.GetValue<double>();
        var threshold = level!["thresholdDb"]!.GetValue<double>();

        // This signal would trigger a noise event if monitoring were active.
        db.ShouldBeGreaterThan(threshold);
    }

    [Fact]
    public async Task CalibrateLevel_WithSilentSignal_BelowThreshold()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/calibrate/start"); s.StatusCodeShouldBeOk(); });

        app.Capture.InjectAudio(SilentPcm()); // -100 dBFS << -30 dBFS threshold

        var level = await GetLevelAsync();
        var db = level!["db"]!.GetValue<double>();
        var threshold = level!["thresholdDb"]!.GetValue<double>();

        // This signal would not trigger a noise event.
        db.ShouldBeLessThan(threshold);
    }

    [Fact]
    public async Task Calibration_WithLoudSignal_CreatesNoEvents()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/calibrate/start"); s.StatusCodeShouldBeOk(); });
        app.Capture.InjectAudio(LoudPcm());
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/calibrate/stop"); s.StatusCodeShouldBeOk(); });

        await using var scope = app.Host.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SensorPalDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        ctx.NoiseEvents.ShouldBeEmpty();
    }

    // DC-offset PCM16 mono — amplitude 0.5 → RMS ≈ -6 dBFS (well above -30 threshold)
    static byte[] LoudPcm(int frameCount = 1000)
    {
        var value = (short)(0.5 * 32767);
        var bytes = new byte[frameCount * 2];
        for (var i = 0; i < frameCount; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        return bytes;
    }

    // All-zero PCM16 → RMS = -100 dBFS (silence, well below any threshold)
    static byte[] SilentPcm(int frameCount = 1000) =>
        new byte[frameCount * 2];

    async Task<JsonObject?> GetLevelAsync() =>
        (await app.Host.Scenario(s =>
        {
            s.Get.Url("/monitoring/level");
            s.StatusCodeShouldBeOk();
        })).ReadAsJson<JsonObject>();
}
