// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's interned Loan sets (design A3 §6.1, docs/dev/DECISIONS.md R4-M53) through <see cref="MirLoanSets"/> itself: equal sets
/// have one id, 0 is the empty set, Add, Union, Remove and Range keep the set laws and repeat through their memos, and the storage is
/// pooled.</summary>
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
            var before = GC.GetAllocatedBytesForCurrentThread();
            var sets = new MirLoanSets();
            for (var w = 0; w < runs.Length; w++)
            {
                for (var element = w; element < w + K; element++)
                {
                    runs[w] = sets.Add(runs[w], element);
                }
            }

            var made = sets.Bytes;
            Unions(sets, runs);
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
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
