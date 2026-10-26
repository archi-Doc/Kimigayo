// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The pooled tables a Mir/Analysis owner empties per view or per body (V16, DECISIONS R4-M146) through
/// <see cref="MirTable{TKey}"/> itself: Clear removes the entries one by one while they fill less than a 64th of the storage, else
/// clears it whole, so its work is its own view's, and leaves no entry on either path; a warm fill and clear allocates nothing.</summary>
public sealed class MirTableTest
{
    // R4-M146: Clear leaves no entry on either path, and Cleared is the slots it wrote: after a dense fill the storage's buckets, after a
    // sparse fill of a large table its entries. A stale entry would be a wrong memo hit: each fill finds none of its keys held.
    [Fact]
    public void ClearLeavesNoEntryOnEitherPath()
    {
        var table = new MirTable<(int A, int B)>();
        Fill(table, 4_096);
        Assert.Equal(Buckets(4_096), Clear(table, 4_096));
        Fill(table, 3);
        Assert.Equal(3, Clear(table, 3));
        Fill(table, 2);
        Assert.Equal((0, 1), (table[(0, 0)], table[(1, -1)]));
    }

    // R4-M146 (design review 2, N3): at the listing's bound, a view of ⌊(C − 1)/64⌋ entries in C buckets is cleared entry by entry, every
    // key listed; one more fills a 64th, and its Clear writes the C buckets.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ClearAtTheListingBoundLeavesNoEntry(int past)
    {
        var (table, buckets) = (new MirTable<(int A, int B)>(), Buckets(4_096));
        Fill(table, 4_096);
        Assert.Equal(buckets, Clear(table, 4_096));
        var count = ((buckets - 1) / 64) + past;
        Fill(table, count);
        Assert.Equal(past == 0 ? count : buckets, Clear(table, count));
    }

    // R4-M146 (N3, N5): a view that grows past the storage after a sparse Clear resizes it, and a resize leaves the table dense, so its Clear
    // writes the new buckets; the next view is listed up to the new storage's bound and cleared entry by entry.
    [Fact]
    public void AViewThatResizesAfterASparseClearLeavesNoEntry()
    {
        var (table, buckets) = (new MirTable<(int A, int B)>(), Buckets(4_096));
        Fill(table, 4_096);
        Assert.Equal(buckets, Clear(table, 4_096));
        Fill(table, 3);
        Assert.Equal(3, Clear(table, 3));
        Fill(table, buckets + 1);
        var grown = Buckets(buckets + 1);
        Assert.True(grown > buckets, $"buckets {buckets}, then {grown}");
        Assert.Equal(grown, Clear(table, buckets + 1));
        var count = (grown - 1) / 64;
        Fill(table, count);
        Assert.Equal(count, Clear(table, count));
    }

    // V16 "per-view clearing" (G136): after one view of N entries, whose Clear writes its buckets, M small views of K entries clear their
    // own entries, M·K slots for every N. Fail-first, under the parent's rule (Dictionary's Clear, the whole storage each time): the M
    // clears write M times the large view's buckets, which grow with N.
    [Fact]
    public void AViewsClearingIsItsOwn()
    {
        const int M = 64;
        const int K = 4;
        var sizes = new[] { 1 << 10, 1 << 12, 1 << 14 };
        var (large, small) = (new long[sizes.Length], new long[sizes.Length]);
        for (var s = 0; s < sizes.Length; s++)
        {
            var table = new MirTable<(int A, int B)>();
            Fill(table, sizes[s]);
            large[s] = Clear(table, sizes[s]);
            for (var view = 0; view < M; view++)
            {
                Fill(table, K);
                small[s] += Clear(table, K);
            }
        }

        Assert.Equal(sizes.Select(Buckets).Select(static x => (long)x), large);
        Assert.Equal([M * K, M * K, M * K], small);
    }

    // V16, R4-M146: a warm fill and clear of a sized table on either path allocates nothing: the storage and the listed keys keep their
    // capacity.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmFillAndClearAllocatesNothing()
    {
        var table = new MirTable<(int A, int B)>();
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            Fill(table, 1_024);
            table.Clear();
            Fill(table, 4);
            table.Clear();
        }));
    }

    // Fills a view of keys (i, -i) for i below a count, each valued i, alternately by Add and by Entry, none of them held before.
    private static void Fill(MirTable<(int A, int B)> table, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i % 2 == 0)
            {
                Assert.False(table.TryGetValue((i, -i), out _));
                table.Add((i, -i), i);
            }
            else
            {
                ref var value = ref table.Entry((i, -i), out var exists);
                Assert.False(exists);
                value = i;
            }
        }
    }

    // Clears a table after a view of a count of keys, which it holds none of afterwards; the slots the Clear wrote.
    private static long Clear(MirTable<(int A, int B)> table, int count)
    {
        table.Clear();
        Assert.DoesNotContain(Enumerable.Range(0, count), i => table.TryGetValue((i, -i), out _));
        return table.Cleared;
    }

    // The buckets of a dictionary filled with a count of entries from empty: the storage a table has after a view of that many entries,
    // since it grows only when every slot is used.
    private static int Buckets(int count)
    {
        var twin = new Dictionary<(int A, int B), int>();
        for (var i = 0; i < count; i++)
        {
            twin.Add((i, -i), i);
        }

        return twin.EnsureCapacity(0);
    }
}
