using Shouldly;
using Xunit;

namespace SensorPal.Server.Audio;

public sealed class CircularAudioBufferTests
{
    // ── Empty / partial fill ──────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_OnFreshBuffer_ReturnsEmpty()
    {
        var buf = new CircularAudioBuffer(10);
        buf.GetSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void GetSnapshot_PartialFill_ReturnsOnlyWrittenBytes()
    {
        var buf = new CircularAudioBuffer(10);
        buf.Add([1, 2, 3]);

        buf.GetSnapshot().ShouldBe([1, 2, 3]);
    }

    // ── Exact fill ────────────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_ExactFill_ReturnsAllBytesInOrder()
    {
        var buf = new CircularAudioBuffer(5);
        buf.Add([1, 2, 3, 4, 5]);

        buf.GetSnapshot().ShouldBe([1, 2, 3, 4, 5]);
    }

    // ── Wraparound ────────────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_AfterWraparound_ReturnsChronologicalOrder()
    {
        // Fill exactly, then add 2 more bytes to wrap around.
        // buffer[5] after wrap: [6,7,3,4,5] with writePos=2
        // snapshot must be the chronological window: [3,4,5,6,7]
        var buf = new CircularAudioBuffer(5);
        buf.Add([1, 2, 3, 4, 5]);
        buf.Add([6, 7]);

        buf.GetSnapshot().ShouldBe([3, 4, 5, 6, 7]);
    }

    [Fact]
    public void GetSnapshot_MultipleWraps_ReturnsLastCapacityBytes()
    {
        // Three full-buffer writes → should retain only the last 5 bytes.
        var buf = new CircularAudioBuffer(5);
        buf.Add([1, 2, 3, 4, 5]);
        buf.Add([6, 7, 8, 9, 10]);
        buf.Add([11, 12, 13, 14, 15]);

        buf.GetSnapshot().ShouldBe([11, 12, 13, 14, 15]);
    }

    // ── Oversized single Add ──────────────────────────────────────────────────

    [Fact]
    public void Add_OversizedChunk_KeepsMostRecentBytes()
    {
        // Single Add call larger than capacity: buffer keeps only the trailing bytes.
        var buf = new CircularAudioBuffer(5);
        buf.Add([1, 2, 3, 4, 5, 6, 7, 8]);

        buf.GetSnapshot().ShouldBe([4, 5, 6, 7, 8]);
    }

    [Fact]
    public void Add_ChunkExactlyMultipleOfCapacity_KeepsMostRecentBytes()
    {
        var buf = new CircularAudioBuffer(3);
        buf.Add([1, 2, 3, 4, 5, 6, 7, 8, 9]); // 3× capacity

        buf.GetSnapshot().ShouldBe([7, 8, 9]);
    }

    // ── Offset parameter ─────────────────────────────────────────────────────

    [Fact]
    public void Add_WithNonZeroOffset_ReadsFromCorrectPosition()
    {
        var buf = new CircularAudioBuffer(3);
        buf.Add([99, 1, 2, 3, 99], 1, 3); // read bytes at index 1,2,3

        buf.GetSnapshot().ShouldBe([1, 2, 3]);
    }

    // ── Accumulated writes ────────────────────────────────────────────────────

    [Fact]
    public void Add_MultipleSmallWrites_AccumulatesCorrectly()
    {
        var buf = new CircularAudioBuffer(10);
        buf.Add([1, 2, 3, 4, 5]);
        buf.Add([6, 7, 8, 9, 10]);
        buf.Add([11, 12]);

        // After 12 bytes into a 10-byte buffer: keeps [3..12]
        buf.GetSnapshot().ShouldBe([3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAddAndSnapshot_NeverThrowsAndSnapshotLengthIsValid()
    {
        const int capacity = 100;
        var buf = new CircularAudioBuffer(capacity);
        var data = new byte[37]; // intentionally not a multiple of capacity

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 500; i++)
                    buf.Add(data, 0, data.Length);
            }
            catch (Exception ex) { exceptions.Add(ex); }
        }));

        var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 500; i++)
                    buf.GetSnapshot().Length.ShouldBeLessThanOrEqualTo(capacity);
            }
            catch (Exception ex) { exceptions.Add(ex); }
        }));

        await Task.WhenAll([.. writers, .. readers]);
        exceptions.ShouldBeEmpty();
    }
}
