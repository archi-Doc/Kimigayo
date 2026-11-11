// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Verification;
using Xunit;

namespace XunitTest;

public class CallableRegionScalingTest(ITestOutputHelper output)
{
    public static TheoryData<string, int> Workloads => new()
    {
        { "candidates", 4 }, { "candidates", 8 }, { "candidates", 16 },
        { "results", 4 }, { "results", 8 }, { "results", 16 },
        { "regions", 4 }, { "regions", 8 }, { "regions", 16 },
        { "fixed-calls", 4 }, { "fixed-calls", 8 }, { "fixed-calls", 16 },
        { "fixed-roots", 4 }, { "fixed-roots", 8 }, { "fixed-roots", 16 },
        { "fixed-joins", 4 }, { "fixed-joins", 8 }, { "fixed-joins", 16 },
    };

    [Theory]
    [MemberData(nameof(Workloads))]
    public void GrowingPlansPreserveBehavior(string axis, int count)
        => ScalarEmissionTest.EmitFixture($"CallableRegionScaling{axis}{count}", VerificationWorkloads.CallableRegions(axis, count), string.Empty);

    [Theory]
    [MemberData(nameof(Workloads))]
    [Trait("Purpose", "Allocation")]
    public void GrowingPlansReuseBoundedStorage(string axis, int count)
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.CallableRegions(axis, count));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // R6-check H8c D8 (H8a OQ2): A5's work and Loan-set bytes (A3 6.2's ν) on each axis at N = 16, 32, 64 grow within the axis's bound.
    // Work: A3 6.3's dense terms "(ended points) × holders" and "accesses × live nodes" with the factor 5 of the dense rows of
    // MirBorrowsTest.TheWorkScalesWithTheProgram, where N holders live across N writes, StorageLives and StorageDeads or N references are
    // read at the end; linear (2) on results. Bytes: linear on every axis. candidates has no MIR term and no row: its N Callable
    // constraints are Binding's, and A5's work and bytes, the MIR size and the cold analysis are constant over N.
    [Theory]
    [InlineData("results", 2)]
    [InlineData("regions", 5)]
    [InlineData("fixed-calls", 5)]
    [InlineData("fixed-roots", 5)]
    [InlineData("fixed-joins", 5)]
    public void EachAxisScalesWithinItsBound(string axis, double factor)
        => WorkScales(output, Sizes(axis), factor);

    // A5's work and Loan-set bytes on three sizes of a workload, each double the one before, without a Loan problem
    // (MirBorrowsTest.Measured): the work grows within the factor, the bytes linearly (MirBorrowsTest.Grows).
    internal static void WorkScales(ITestOutputHelper output, string[] sources, double factor)
    {
        var measured = sources.Select(MirBorrowsTest.Measured).ToArray();
        long[] work = [.. measured.Select(static x => x.Work)];
        long[] bytes = [.. measured.Select(static x => x.Bytes)];
        output.WriteLine($"work {string.Join(", ", work)}; bytes {string.Join(", ", bytes)}");
        Assert.True(MirBorrowsTest.Grows(work, factor), $"work {string.Join(", ", work)}");
        Assert.True(MirBorrowsTest.Grows(bytes, 2), $"bytes {string.Join(", ", bytes)}");
    }

    // The cold Analyze of three sizes of a workload, each double the one before, bound with its startup checked outside the window:
    // each verifies, and the allocation grows within the factor (V16: a[2] - a[1] <= factor (a[1] - a[0]); 3 is linear).
    internal static void ColdAnalysisScales(ITestOutputHelper output, string[] sources, double factor)
    {
        var compilations = new Compilation[sources.Length];
        var allocated = AllocationMeasurement.MeasureSeries(sources.Length, i =>
        {
            var c = compilations[i] = MirBuildTest.Compile(sources[i]);
            return () => c.Analyze();
        });
        output.WriteLine($"cold analysis allocation {string.Join(", ", allocated)}");
        Assert.All(compilations, static c => Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c)));
        Assert.True(allocated[2] - allocated[1] <= factor * (allocated[1] - allocated[0]), $"cold analysis allocation {string.Join(", ", allocated)}");
    }

    private static string[] Sizes(string axis) => [.. new[] { 16, 32, 64 }.Select(n => VerificationWorkloads.CallableRegions(axis, n))];

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests(ITestOutputHelper output)
    {
        // V16: the cold analysis's allocation on each axis at N = 16, 32, 64 grows within the axis's bound: linearly (3), fixed-roots since a
        // shorter meet's operand edges form once per class (MirOrigins.Relate); 5 on fixed-joins, whose Loan stratum's Remove memo
        // (MirLoanSets.removes) holds the Work's distinct Removes, A3 6.3's "(ended points) × holders" term, N²/2 entries.
        [Theory]
        [InlineData("results", 3)]
        [InlineData("regions", 3)]
        [InlineData("fixed-calls", 3)]
        [InlineData("fixed-roots", 3)]
        [InlineData("fixed-joins", 5)]
        public void TheColdAnalysisScalesWithinItsBound(string axis, double factor)
            => ColdAnalysisScales(output, Sizes(axis), factor);

        // PLAN G768 (V16): N chained borrows (`let a_k = a_(k-1)@ref`) end each borrowed local's Loan at its StorageLive, write and
        // StorageDead in every holder (E8), whose sets all lie apart from it, so an apart Remove makes no memo entry (MirLoanSets.Remove)
        // and the cold analysis allocates linearly in N (the memo held one entry per holder and ended point, N^2 in all).
        [Fact]
        public void TheColdAnalysisOfABorrowChainAllocatesLinearly()
            => ColdAnalysisScales(output, [.. new[] { 128, 256, 512 }.Select(BorrowChain)], 3);

        // A body of N chained borrows, each a reference to the one before: a_k is ref^k/i32 (SPEC 10.2's explicit acquisition of a
        // reference slot).
        private static string BorrowChain(int n)
            => "func f() -> i32\n    let a0: i32 = 1\n" + string.Concat(Enumerable.Range(1, n).Select(static k => $"    let a{k} = a{k - 1}@ref\n")) +
                "    return 0\npublic func main() => ()\n";
    }
}
