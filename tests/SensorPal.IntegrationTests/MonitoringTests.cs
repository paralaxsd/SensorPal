using Alba;
using Shouldly;
using SensorPal.Shared.Models;
using Xunit;

namespace SensorPal.IntegrationTests;

public class MonitoringTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => app.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Session lifecycle ─────────────────────────────────────────────────────

    [Fact]
    public async Task Start_CreatesActiveSessionInDb()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });

        var sessions = await GetSessionsAsync();
        sessions.ShouldHaveSingleItem();
        sessions[0].IsActive.ShouldBeTrue();
        sessions[0].EndedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Stop_ClosesSessionInDb()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });

        var sessions = await GetSessionsAsync();
        sessions.ShouldHaveSingleItem();
        sessions[0].IsActive.ShouldBeFalse();
        sessions[0].EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Sessions_WhenNoHistory_ReturnsEmpty()
    {
        var sessions = await GetSessionsAsync();
        sessions.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartStop_Twice_CreatesTwoSessions()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });

        var sessions = await GetSessionsAsync();
        sessions.Count.ShouldBe(2);
        sessions.ShouldAllBe(s => !s.IsActive);
    }

    [Fact]
    public async Task Stop_WhenIdle_IsIdempotent()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });

        (await GetSessionsAsync()).ShouldBeEmpty();
    }

    // ── Session timestamps ────────────────────────────────────────────────────

    [Fact]
    public async Task Session_StartedAt_MatchesFakeTime()
    {
        var expected = app.Time.GetUtcNow().UtcDateTime;
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });

        var sessions = await GetSessionsAsync();
        sessions[0].StartedAt.UtcDateTime.ShouldBe(expected);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSession_WhenActive_ReturnsConflict()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });
        var id = (await GetSessionsAsync())[0].Id;

        await app.Host.Scenario(s =>
        {
            s.Delete.Url($"/monitoring/{id}");
            s.StatusCodeShouldBe(409);
        });
    }

    [Fact]
    public async Task DeleteSession_WhenInactive_Succeeds()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });
        var id = (await GetSessionsAsync())[0].Id;

        await app.Host.Scenario(s =>
        {
            s.Delete.Url($"/monitoring/{id}");
            s.StatusCodeShouldBeOk();
        });

        (await GetSessionsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteSession_WhenNotFound_ReturnsNotFound()
    {
        await app.Host.Scenario(s =>
        {
            s.Delete.Url("/monitoring/999");
            s.StatusCodeShouldBe(404);
        });
    }

    // ── Markers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Markers_OnNewSession_ReturnsEmpty()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });
        var id = (await GetSessionsAsync())[0].Id;

        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url($"/monitoring/{id}/markers");
            s.StatusCodeShouldBeOk();
        });

        result.ReadAsJson<List<EventMarkerDto>>()!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Markers_WhenSessionNotFound_ReturnsNotFound()
    {
        await app.Host.Scenario(s =>
        {
            s.Get.Url("/monitoring/999/markers");
            s.StatusCodeShouldBe(404);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    async Task<List<MonitoringSessionDto>> GetSessionsAsync()
    {
        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/monitoring/sessions");
            s.StatusCodeShouldBeOk();
        });
        return result.ReadAsJson<List<MonitoringSessionDto>>()!;
    }
}
