using Microsoft.Extensions.Time.Testing;
using NAudio.Wave;
using Shouldly;
using Xunit;

namespace SensorPal.Server.Services;

public sealed class NoiseDetectorTests
{
    // Threshold of -30 dBFS ≈ amplitude 0.032; Pcm16(0.5) → -6 dBFS (well above)
    const double Threshold = -30.0;

    readonly FakeTimeProvider _time = new();
    readonly WaveFormat _mono16 = new WaveFormat(44100, 16, 1);

    // ── RMS / dBFS calculation ────────────────────────────────────────────────

    [Fact]
    public void CurrentDb_AfterSilence_IsNegative100()
    {
        var d = Create();
        Process(d, Silence());
        d.CurrentDb.ShouldBe(-100.0);
    }

    [Fact]
    public void CurrentDb_AfterEmptyBuffer_IsNegative100()
    {
        var d = Create();
        d.Process([], 0, 0);
        d.CurrentDb.ShouldBe(-100.0);
    }

    [Fact]
    public void CurrentDb_AfterFullScale_IsNearZero()
    {
        var d = Create();
        Process(d, Pcm16(1.0));
        d.CurrentDb.ShouldBeInRange(-1.0, 0.0);
    }

    [Theory]
    [InlineData(0.5, -6.02)]
    [InlineData(0.1, -20.0)]
    public void CurrentDb_MatchesExpectedDbfs(double amplitude, double expectedDb)
    {
        var d = Create();
        Process(d, Pcm16(amplitude));
        d.CurrentDb.ShouldBe(expectedDb, tolerance: 0.05);
    }

    [Fact]
    public void CurrentDb_StereoAveragesChannelPower()
    {
        // Left = 0.5, right = 0.0 → averaged power per frame = 0.5²/2
        // → RMS = 0.5/√2 ≈ 0.354 → dBFS ≈ -9.03
        var stereoFormat = new WaveFormat(44100, 16, 2);
        var d = new NoiseDetector(Threshold, stereoFormat, _time);
        Process(d, Pcm16Stereo(0.5, 0.0));

        d.CurrentDb.ShouldBe(-9.03, tolerance: 0.05);
    }

    [Fact]
    public void CurrentDb_32BitFloat_CorrectRms()
    {
        var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var d = new NoiseDetector(Threshold, floatFormat, _time);

        const float amplitude = 0.5f;
        var bytes = new byte[1000 * 4];
        for (var i = 0; i < 1000; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), amplitude);
        Process(d, bytes);

        d.CurrentDb.ShouldBe(-6.02, tolerance: 0.05);
    }

    // ── State machine ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsIdle()
    {
        var d = Create();
        d.IsEventActive.ShouldBeFalse();
        d.EventStartedAt.ShouldBeNull();
    }

    [Fact]
    public void Process_WhenQuiet_DoesNotFireEventStarted()
    {
        var d = Create();
        var fired = false;
        d.EventStarted += (_, _) => fired = true;

        Process(d, Silence());

        fired.ShouldBeFalse();
    }

    [Fact]
    public void Process_WhenLoud_FiresEventStarted()
    {
        var d = Create();
        DateTime? firedAt = null;
        d.EventStarted += (t, _) => firedAt = t;

        var expectedTime = _time.GetUtcNow().UtcDateTime;
        Process(d, Pcm16(0.5));

        firedAt.ShouldNotBeNull();
        firedAt!.Value.ShouldBe(expectedTime);
        d.IsEventActive.ShouldBeTrue();
        d.EventStartedAt.ShouldNotBeNull();
    }

    [Fact]
    public void Process_WhenAlreadyActive_DoesNotFireEventStartedAgain()
    {
        var d = Create();
        var count = 0;
        d.EventStarted += (_, _) => count++;

        Process(d, Pcm16(0.5));
        Process(d, Pcm16(0.5));

        count.ShouldBe(1);
    }

    [Fact]
    public void Process_WhenActive_QuietBelowTimeout_EventStaysActive()
    {
        var d = Create(silenceMs: 5000);
        var ended = false;
        d.EventEnded += (_, _, _) => ended = true;

        Process(d, Pcm16(0.5));
        _time.Advance(TimeSpan.FromMilliseconds(4999));
        Process(d, Silence());

        d.IsEventActive.ShouldBeTrue();
        ended.ShouldBeFalse();
    }

    [Fact]
    public void Process_WhenActive_QuietAtTimeout_FiresEventEnded()
    {
        var d = Create(silenceMs: 5000);
        var ended = false;
        d.EventEnded += (_, _, _) => ended = true;

        Process(d, Pcm16(0.5));
        _time.Advance(TimeSpan.FromMilliseconds(5000));
        Process(d, Silence());

        ended.ShouldBeTrue();
        d.IsEventActive.ShouldBeFalse();
    }

    [Fact]
    public void Process_WhenActive_LoudFrame_ResetsSilenceTimer()
    {
        // Loud at T=0, loud again at T=4000, quiet at T=8000 (only 4000ms since last loud → still active)
        var d = Create(silenceMs: 5000);
        var ended = false;
        d.EventEnded += (_, _, _) => ended = true;

        Process(d, Pcm16(0.5));
        _time.Advance(TimeSpan.FromMilliseconds(4000));
        Process(d, Pcm16(0.5));             // resets silence timer
        _time.Advance(TimeSpan.FromMilliseconds(4000));
        Process(d, Silence());

        ended.ShouldBeFalse();
        d.IsEventActive.ShouldBeTrue();
    }

    [Fact]
    public void Process_EventEnded_HasCorrectPeakDb()
    {
        var d = Create();
        double? peakDb = null;
        d.EventEnded += (_, peak, _) => peakDb = peak;

        Process(d, Pcm16(0.1));   // -20 dBFS — first loud frame, initialises peak
        Process(d, Pcm16(0.5));   // -6 dBFS  — new peak
        Process(d, Pcm16(0.2));   // -14 dBFS — below current peak

        _time.Advance(TimeSpan.FromMilliseconds(5000));
        Process(d, Silence());

        peakDb.ShouldNotBeNull();
        peakDb!.Value.ShouldBe(-6.02, tolerance: 0.05);
    }

    [Fact]
    public void Process_EventEnded_HasCorrectDurationMs()
    {
        // T=0: loud (EventStarted), T=2000: loud (resets timer), T=7000: quiet (EventEnded)
        // duration = 7000 - 0 = 7000 ms
        var d = Create(silenceMs: 5000);
        int? durationMs = null;
        d.EventEnded += (_, _, ms) => durationMs = ms;

        Process(d, Pcm16(0.5));
        _time.Advance(TimeSpan.FromMilliseconds(2000));
        Process(d, Pcm16(0.5));
        _time.Advance(TimeSpan.FromMilliseconds(5000));
        Process(d, Silence());

        durationMs.ShouldBe(7000);
    }

    [Fact]
    public void EventStartedAt_ReflectsActiveState()
    {
        var d = Create();
        d.EventStartedAt.ShouldBeNull();

        Process(d, Pcm16(0.5));
        d.EventStartedAt.ShouldNotBeNull();

        _time.Advance(TimeSpan.FromMilliseconds(5000));
        Process(d, Silence());
        d.EventStartedAt.ShouldBeNull();
    }

    [Fact]
    public void Process_AfterEventEnded_CanStartNewEvent()
    {
        var d = Create(silenceMs: 5000);
        var eventCount = 0;
        d.EventStarted += (_, _) => eventCount++;

        Process(d, Pcm16(0.5));
        _time.Advance(TimeSpan.FromMilliseconds(5000));
        Process(d, Silence());          // event 1 ends

        Process(d, Pcm16(0.5));         // event 2 starts

        eventCount.ShouldBe(2);
        d.IsEventActive.ShouldBeTrue();
    }

    NoiseDetector Create(double threshold = Threshold, int silenceMs = 5000)
        => new(threshold, _mono16, _time, silenceMs);

    // DC-offset buffers: RMS = amplitude exactly (no √2 factor like with sine waves).
    static byte[] Pcm16(double amplitude, int frameCount = 1000)
    {
        var value = (short)(amplitude * 32767);
        var bytes = new byte[frameCount * 2];
        for (var i = 0; i < frameCount; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        return bytes;
    }

    static byte[] Pcm16Stereo(double left, double right, int frameCount = 1000)
    {
        var l = (short)(left * 32767);
        var r = (short)(right * 32767);
        var bytes = new byte[frameCount * 4];
        for (var i = 0; i < frameCount; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), l);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4 + 2), r);
        }
        return bytes;
    }

    static byte[] Silence(int frameCount = 1000) =>
        Pcm16(0.0, frameCount);

    static void Process(NoiseDetector d, byte[] data) =>
        d.Process(data, 0, data.Length);
}
