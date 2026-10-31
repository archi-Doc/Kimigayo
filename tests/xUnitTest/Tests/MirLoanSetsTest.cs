// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's interned Loan sets (design A3 §6.1, docs/dev/DECISIONS.md R4-M53) through <see cref="MirLoanSets"/> itself: equal sets
/// have one id, 0 is the empty set, Add, Union (of two or of any number, R4-M138), Remove, Range and Of keep the set laws and repeat
/// through their memos (Of through its run), and the storage is pooled.</summary>
public sealed class MirLoanSetsTest(ITestOutputHelper output)
{
    // R4-M53: a set is its id: the same elements added in any order, joined by Union or left by Remove are one node, and the empty set is
    // node 0 however it is reached.
    [Fact]
    public void EqualSetsHaveOneId()
    {
        var sets = new MirLoanSets();
        var set = Of(sets, 1, 5, 9);
        Assert.Equal(set, Of(sets, 9, 1, 5));
        Assert.Equal(set, sets.Union(Of(sets, 5), Of(sets, 9, 1)));
        Assert.Equal(set, sets.Remove(Of(sets, 1, 2, 5, 9), Of(sets, 2)));
        Assert.Equal([1, 5, 9], sets.Elements(set).ToArray());
        Assert.Equal(0, Of(sets));
        Assert.Equal(0, sets.Remove(set, Of(sets, 9, 5, 1)));
        Assert.Equal(0, sets.Remove(Of(sets, 3), Of(sets, 3, 4)));
        Assert.Equal((0, false, 0), (sets.Count(0), sets.Contains(0, 0), sets.Elements(0).Length));
    }

    // R4-M53: Union is idempotent, commutative and associative, with the empty set as its identity; Add is a Union with one element;
    // Remove keeps the elements the removed set lacks, so removing absent elements or the empty set keeps the set, and removing from the
    // empty set gives it. Contains and Count read the ascending elements.
    [Fact]
    public void OperationsKeepTheSetLaws()
    {
        var sets = new MirLoanSets();
        var (a, b, c) = (Of(sets, 1, 4, 7), Of(sets, 2, 4, 8), Of(sets, 0, 7, 9));
        Assert.Equal([a, a, a], new[] { sets.Union(a, a), sets.Union(a, 0), sets.Union(0, a) });
        Assert.Equal(sets.Union(a, b), sets.Union(b, a));
        Assert.Equal(sets.Union(sets.Union(a, b), c), sets.Union(a, sets.Union(b, c)));
        Assert.Equal([0, 1, 2, 4, 7, 8, 9], sets.Elements(sets.Union(sets.Union(a, b), c)).ToArray());
        Assert.Equal(sets.Union(a, Of(sets, 5)), sets.Add(a, 5));
        Assert.Equal(sets.Add(a, 5), sets.Add(sets.Add(a, 5), 5));
        Assert.Equal(a, sets.Add(a, 4));
        Assert.Equal([1, 7], sets.Elements(sets.Remove(a, b)).ToArray());
        Assert.Equal(sets.Remove(a, b), sets.Remove(sets.Union(a, b), b));
        Assert.Equal([a, a, 0], new[] { sets.Remove(a, Of(sets, 3, 5, 100)), sets.Remove(a, 0), sets.Remove(0, a) });
        Assert.Equal((3, true, false), (sets.Count(a), sets.Contains(a, 7), sets.Contains(a, 5)));
    }

    // R4-M53, A3 §12 E8: Range is the set of a run of consecutive elements, memoized, the empty run node 0. Remove scans its smaller operand:
    // removing absent elements returns the set itself, and a result equal to an existing set makes no node.
    [Fact]
    public void ARangeIsTheSetOfItsRun()
    {
        var sets = new MirLoanSets();
        var (run, absent, pair, four) = (sets.Range(3, 4), Of(sets, 1, 9), Of(sets, 1, 4), Of(sets, 4));
        Assert.Equal([3, 4, 5, 6], sets.Elements(run).ToArray());
        Assert.Equal([run, run, 0], new[] { Of(sets, 6, 5, 4, 3), sets.Range(3, 4), sets.Range(9, 0) });
        var bytes = sets.Bytes;
        Assert.Equal([run, Of(sets, 1), 0], new[] { sets.Remove(run, absent), sets.Remove(pair, run), sets.Remove(four, run) });
        Assert.Equal(bytes, sets.Bytes);
        Assert.Equal([3, 5, 6], sets.Elements(sets.Remove(run, four)).ToArray());
    }

    // DECISIONS R4-M132 (the design review's A-B1): Of interns an ascending run in one pass, the set its elements make one at a time, the
    // empty run node 0; a run equal to an existing set makes no node, and it scans the run once.
    [Fact]
    public void OfIsTheSetOfItsAscendingRun()
    {
        var sets = new MirLoanSets();
        var set = sets.Of([2, 5, 8, 13]);
        Assert.Equal([set, 0], new[] { Of(sets, 13, 8, 5, 2), sets.Of([]) });
        Assert.Equal([2, 5, 8, 13], sets.Elements(set).ToArray());
        var (bytes, scanned) = (sets.Bytes, sets.Scanned);
        Assert.Equal(set, sets.Of([2, 5, 8, 13]));
        Assert.Equal((bytes, scanned + 4), (sets.Bytes, sets.Scanned));
    }

    // R4-M53, A3 §6.2: an operation repeated, in either operand order for a Union, returns its first result and makes no node, and a
    // result equal to an existing set makes none either, so Bytes counts each distinct node's elements once.
    [Fact]
    public void RepeatedOperationsMakeNoNode()
    {
        var sets = new MirLoanSets();
        var (a, b) = (Of(sets, 1, 2), Of(sets, 3));
        Assert.Equal(4 * sizeof(int), sets.Bytes); // {1}, {1, 2} and {3}.
        var union = sets.Union(a, b);
        Assert.Equal(7 * sizeof(int), sets.Bytes);
        Assert.Equal([union, union, union, a], new[] { sets.Union(a, b), sets.Union(b, a), sets.Add(a, 3), sets.Remove(union, b) });
        Assert.Equal(7 * sizeof(int), sets.Bytes);
    }

    // V16, A3 §6.2 (re-review): Scanned counts the elements the operations read and write, which A5's work counts: a Union both runs, a
    // Remove its smaller operand and, when it removes an element, the run it copies, a Range its run; a repeated operation reads its memo.
    [Fact]
    public void ScannedCountsTheElementsOperationsRead()
    {
        var sets = new MirLoanSets();
        var (six, three) = (sets.Range(0, 6), sets.Range(4, 3));
        Assert.Equal(9, sets.Scanned);
        var union = sets.Union(six, three);
        Assert.Equal((union, 18L), (sets.Union(three, six), sets.Scanned));
        Assert.Equal(six, sets.Remove(six, sets.Range(10, 2)));
        Assert.Equal(22, sets.Scanned);
        Assert.Equal([0, 1, 2, 3], sets.Elements(sets.Remove(union, three)).ToArray());
        Assert.Equal(32, sets.Scanned);
    }

    // R4-M138: the union of any number of sets is the fold of their binary Unions: none or only empty sets give the empty set, one distinct
    // set (with empty sets and repeats) is itself, and duplicates, disjoint sets, element 0 (A5's Φ_E) beside a high element and parts that
    // cross marker words give the fold's node; consecutive unions over random parts in universes of 64 to 5,000 elements agree with the fold,
    // so the marker is clear between calls.
    [Fact]
    public void TheUnionOfAnyNumberIsTheFoldOfUnions()
    {
        var sets = new MirLoanSets();
        var (a, b, high) = (Of(sets, 1, 4, 70), Of(sets, 4, 130), Of(sets, 0, 100_000));
        Assert.Equal([0, 0, a, a], new[] { sets.Union([]), sets.Union([0, 0]), sets.Union([a]), sets.Union([0, a, a, 0]) });
        Assert.Equal(sets.Union(a, b), sets.Union([a, b, a, b]));
        Assert.Equal([0, 1, 4, 70, 130, 100_000], sets.Elements(sets.Union([a, high, b])).ToArray());
        Assert.Equal([0, 7], sets.Elements(sets.Union([Of(sets, 7), Of(sets, 0)])).ToArray());
        var random = new Random(264);
        foreach (var universe in new[] { 64, 65, 200, 1_000, 5_000 })
        {
            for (var round = 0; round < 200; round++)
            {
                var parts = new int[random.Next(0, 9)];
                for (var i = 0; i < parts.Length; i++)
                {
                    parts[i] = random.Next(4) == 0 ? 0 : Of(sets, [.. Enumerable.Range(0, random.Next(1, 6)).Select(_ => random.Next(universe))]);
                }

                Assert.Equal(parts.Aggregate(0, sets.Union), sets.Union(parts));
            }
        }
    }

    // V16 (R4-M138, the N2 landing review 2's B1): the union of any number of sets scans the elements it reads, the marker words it sets
    // (sorted) and the set it makes, never the range between its least and its greatest element: element 0 beside 64 costs what it costs
    // beside 2^20.
    [Fact]
    public void TheUnionOfAnyNumberScansNoRange()
    {
        var sets = new MirLoanSets();
        var (zero, near, far) = (Of(sets, 0), Of(sets, 64), Of(sets, 1 << 20));
        var scanned = sets.Scanned;
        _ = sets.Union([zero, near]);
        var nearly = sets.Scanned - scanned;
        scanned = sets.Scanned;
        _ = sets.Union([zero, far]);
        Assert.Equal(nearly, sets.Scanned - scanned);
    }

    // R4-M53: Clear restarts at the empty set: ids and Bytes begin again, and no result of an operation before it answers for the sets
    // made after it. AWarmRunAllocatesNothing shows that it keeps the storage.
    [Fact]
    public void ClearRestartsAtTheEmptySet()
    {
        var sets = new MirLoanSets();
        var (three, seven) = (sets.Add(0, 3), sets.Add(0, 7));
        Assert.Equal((1, 2, 3), (three, seven, sets.Union(three, seven)));
        sets.Clear();
        Assert.Equal(0, sets.Bytes);
        (seven, var five) = (sets.Add(0, 7), sets.Add(0, 5));
        Assert.Equal((1, 2), (seven, five));
        Assert.Equal([5, 7], sets.Elements(sets.Union(seven, five)).ToArray());
        Assert.Equal([3], sets.Elements(sets.Add(0, 3)).ToArray());
        Assert.Equal(5 * sizeof(int), sets.Bytes);
    }

    // R4-M53, R4-M146: Clear empties the nodes and memos of a large view whole and a small view's entry by entry (MirTable), and a view
    // after either gets the sets a fresh instance gives, so no node or memo of an earlier view, keyed by ids that restart, answers for it.
    [Fact]
    public void AViewAfterALargeOneGetsAFreshInstancesSets()
    {
        var sets = new MirLoanSets();
        for (var i = 0; i < 2_000; i++)
        {
            _ = sets.Remove(sets.Union(Of(sets, i, i + 1), sets.Range(i, 3)), Of(sets, i + 2));
        }

        sets.Clear();
        Assert.Equal(View(new MirLoanSets(), 1), View(sets, 1));
        sets.Clear();
        Assert.Equal(View(new MirLoanSets(), 2), View(sets, 2));

        // A small view's sets, each its id and elements, from a first element: Add, Union, Range and Remove repeat the earlier view's keys.
        static string[] View(MirLoanSets sets, int first)
        {
            var a = sets.Add(0, first);
            var b = sets.Add(a, 2);
            var c = sets.Union(a, sets.Range(2, 2));
            return [.. new[] { a, b, c, sets.Remove(c, b) }.Select(x => $"{x}: {string.Join(", ", sets.Elements(x).ToArray())}")];
        }
    }

    // V16, R4-M53: a warm run after Clear allocates nothing: the element pool, the nodes and the memos keep their storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunAllocatesNothing()
    {
        var sets = new MirLoanSets();
        var result = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            sets.Clear();
            result = Workload(sets);
        }));

        var expected = new HashSet<int>();
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < 64; i++)
            {
                expected.UnionWith([i, (i * 7) % 64]);
                expected.Remove((i * 3) % 64);
            }
        }

        Assert.Equal(expected.Order(), sets.Elements(result).ToArray());
    }

    // V16, R4-M138: a warm union of any number of sets allocates nothing: its marker and its list of words keep their storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmUnionOfAnyNumberAllocatesNothing()
    {
        var sets = new MirLoanSets();
        var parts = new int[4];
        var result = 0;
        void Run()
        {
            sets.Clear();
            (parts[0], parts[1]) = (sets.Add(sets.Add(0, 0), 3), sets.Add(sets.Add(0, 64), 3));
            (parts[2], parts[3]) = (sets.Add(0, 5_000), sets.Add(sets.Add(0, 0), 70));
            result = sets.Union(parts);
        }

        Run();
        Assert.Equal(0, AllocationMeasurement.Measure(Run));
        Assert.Equal([0, 3, 64, 70, 5_000], sets.Elements(result).ToArray());
    }

    // V16, A3 §6.2's ν: n Unions of overlapping runs of k elements, [w, w + k) and [w + 1, w + k + 1), each make one node of k + 1
    // elements, so the bytes created grow by exactly that, and the cold allocation of the runs and the Unions grows linearly in n;
    // repeated, the Unions make no node.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void UnionsScaleLinearlyInTheBytesCreated()
    {
        const int K = 8;
        var (sizes, bytes, allocated) = (new[] { 256, 512, 1024 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var runs = new int[sizes[i] + 1];
            MirLoanSets sets = null!;
            long made = 0;
            allocated[i] = AllocationMeasurement.MeasureOnce(() =>
            {
                sets = new MirLoanSets();
                for (var w = 0; w < runs.Length; w++)
                {
                    for (var element = w; element < w + K; element++)
                    {
                        runs[w] = sets.Add(runs[w], element);
                    }
                }

                made = sets.Bytes;
                Unions(sets, runs);
            });
            bytes[i] = sets.Bytes - made;
            Assert.Equal(sizes[i] * (K + 1) * sizeof(int), bytes[i]);
            Unions(sets, runs);
            Assert.Equal(made + bytes[i], sets.Bytes);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: Union bytes created {string.Join(", ", bytes)}, cold allocation {string.Join(", ", allocated)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold allocation {string.Join(", ", allocated)}");

        static void Unions(MirLoanSets sets, int[] runs)
        {
            for (var w = 0; w + 1 < runs.Length; w++)
            {
                sets.Union(runs[w], runs[w + 1]);
            }
        }
    }

    // A set of elements, added in their order.
    private static int Of(MirLoanSets sets, params int[] elements) => elements.Aggregate(0, sets.Add);

    // Adds, Unions and Removes over 64 elements, the second round repeating the first's operations.
    private static int Workload(MirLoanSets sets)
    {
        var all = 0;
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < 64; i++)
            {
                all = sets.Union(all, sets.Add(sets.Add(0, i), (i * 7) % 64));
                all = sets.Remove(all, sets.Add(0, (i * 3) % 64));
            }
        }

        return all;
    }
}
