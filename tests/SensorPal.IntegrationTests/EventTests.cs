using Alba;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SensorPal.Server.Storage;
using SensorPal.Shared.Models;
using Xunit;

namespace SensorPal.IntegrationTests;

public class EventTests(AppFixture app) : IClassFixture<AppFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => app.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Event lifecycle (end-to-end) ──────────────────────────────────────────

    [Fact]
    public async Task Monitoring_WithLoudAudio_SavesEventToDb()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });

        app.Capture.InjectAudio(LoudPcm());          // starts event
        app.Time.Advance(TimeSpan.FromSeconds(6));    // past silence timeout (5 s)
        app.Capture.InjectAudio(SilentPcm());         // triggers EventEnded → saved to DB

        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });

        var events = await GetEventsAsync();
        events.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Monitoring_SavedEvent_HasCorrectPeakDb()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });

        app.Capture.InjectAudio(LoudPcm());           // amplitude 0.5 → ≈ -6 dBFS
        app.Time.Advance(TimeSpan.FromSeconds(6));
        app.Capture.InjectAudio(SilentPcm());

        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });

        var events = await GetEventsAsync();
        events[0].PeakDb.ShouldBeGreaterThan(-10.0);
    }

    [Fact]
    public async Task Monitoring_NoLoudAudio_CreatesNoEvents()
    {
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/start"); s.StatusCodeShouldBeOk(); });
        app.Capture.InjectAudio(SilentPcm());
        await app.Host.Scenario(s => { s.Post.Url("/monitoring/stop"); s.StatusCodeShouldBeOk(); });

        var events = await GetEventsAsync();
        events.ShouldBeEmpty();
    }

    // ── GET /events ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvents_WhenNoEvents_ReturnsEmpty()
    {
        var events = await GetEventsAsync();
        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetEvents_ReturnsSeededEvent()
    {
        var sessionId = await SeedSessionAsync();
        await SeedEventAsync(sessionId, app.Time.GetUtcNow().UtcDateTime);

        var events = await GetEventsAsync();
        events.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetEvents_DoesNotReturnEventsFromOtherDates()
    {
        var sessionId = await SeedSessionAsync();

        // Two events: today and 2 days in the future
        await SeedEventAsync(sessionId, app.Time.GetUtcNow().UtcDateTime);
        await SeedEventAsync(sessionId, app.Time.GetUtcNow().UtcDateTime.AddDays(2));

        var events = await GetEventsAsync();
        events.ShouldHaveSingleItem();
    }

    // ── GET /events/{id} ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvent_ById_ReturnsCorrectEvent()
    {
        var sessionId = await SeedSessionAsync();
        var eventId = await SeedEventAsync(sessionId, app.Time.GetUtcNow().UtcDateTime, peakDb: -8.5);

        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url($"/events/{eventId}");
            s.StatusCodeShouldBeOk();
        });

        var ev = result.ReadAsJson<NoiseEventDto>()!;
        ev.Id.ShouldBe(eventId);
        ev.PeakDb.ShouldBe(-8.5);
        ev.HasClip.ShouldBeFalse();
    }

    [Fact]
    public async Task GetEvent_WhenNotFound_ReturnsNotFound()
    {
        await app.Host.Scenario(s =>
        {
            s.Get.Url("/events/999");
            s.StatusCodeShouldBe(404);
        });
    }

    // ── GET /events/{id}/audio ────────────────────────────────────────────────

    [Fact]
    public async Task GetEventAudio_WhenNoClip_ReturnsNotFound()
    {
        var sessionId = await SeedSessionAsync();
        var eventId = await SeedEventAsync(sessionId, app.Time.GetUtcNow().UtcDateTime);

        await app.Host.Scenario(s =>
        {
            s.Get.Url($"/events/{eventId}/audio");
            s.StatusCodeShouldBe(404);
        });
    }

    // ── DELETE /events ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEvents_ByDate_DeletesAndReturnsCount()
    {
        var sessionId = await SeedSessionAsync();
        var t = app.Time.GetUtcNow().UtcDateTime;
        await SeedEventAsync(sessionId, t);
        await SeedEventAsync(sessionId, t.AddHours(1));

        var localDay = DateOnly.FromDateTime(app.Time.GetLocalNow().DateTime);
        var result = await app.Host.Scenario(s =>
        {
            s.Delete.Url($"/events?date={localDay:yyyy-MM-dd}");
            s.StatusCodeShouldBeOk();
        });

        result.ReadAsJson<DeletedDto>()!.Deleted.ShouldBe(2);
        (await GetEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteEvents_WhenNoneExist_ReturnsZero()
    {
        var result = await app.Host.Scenario(s =>
        {
            s.Delete.Url("/events?date=2020-01-01");
            s.StatusCodeShouldBeOk();
        });

        result.ReadAsJson<DeletedDto>()!.Deleted.ShouldBe(0);
    }

    // ── GET /events/days ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventDays_WhenNoEvents_ReturnsEmpty()
    {
        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/events/days");
            s.StatusCodeShouldBeOk();
        });

        result.ReadAsJson<string[]>()!.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetEventDays_DeduplicatesSameDay()
    {
        var sessionId = await SeedSessionAsync();
        var t = app.Time.GetUtcNow().UtcDateTime;

        // Three events within the same UTC day → should produce a single local day
        await SeedEventAsync(sessionId, t);
        await SeedEventAsync(sessionId, t.AddHours(1));
        await SeedEventAsync(sessionId, t.AddHours(2));

        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/events/days");
            s.StatusCodeShouldBeOk();
        });

        result.ReadAsJson<string[]>()!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetEventDays_ReturnsSeparateEntryPerDay()
    {
        var sessionId = await SeedSessionAsync();
        var t = app.Time.GetUtcNow().UtcDateTime;

        // 24 h apart — guaranteed different local days in any timezone
        await SeedEventAsync(sessionId, t);
        await SeedEventAsync(sessionId, t.AddDays(1));

        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/events/days");
            s.StatusCodeShouldBeOk();
        });

        result.ReadAsJson<string[]>()!.Length.ShouldBe(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    async Task<long> SeedSessionAsync()
    {
        await using var scope = app.Host.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionRepository>();
        return await sessions.StartSessionAsync("stub.mp3");
    }

    async Task<long> SeedEventAsync(long sessionId, DateTime detectedAt, double peakDb = -10.0)
    {
        await using var scope = app.Host.Services.CreateAsyncScope();
        var events = scope.ServiceProvider.GetRequiredService<EventRepository>();
        return await events.SaveEventAsync(sessionId, detectedAt, peakDb, 500, 0, 0, null);
    }

    async Task<List<NoiseEventDto>> GetEventsAsync()
    {
        var result = await app.Host.Scenario(s =>
        {
            s.Get.Url("/events");
            s.StatusCodeShouldBeOk();
        });
        return result.ReadAsJson<List<NoiseEventDto>>()!;
    }

    // Amplitude 0.5 → RMS ≈ -6 dBFS, well above -30 dBFS threshold
    static byte[] LoudPcm(int frameCount = 1000)
    {
        var value = (short)(0.5 * 32767);
        var bytes = new byte[frameCount * 2];
        for (var i = 0; i < frameCount; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        return bytes;
    }

    // All-zero PCM16 → RMS = -100 dBFS (silence, below any threshold)
    static byte[] SilentPcm(int frameCount = 1000) => new byte[frameCount * 2];

    // Matches the anonymous type returned by DELETE /events
    sealed record DeletedDto(int Deleted);
}
