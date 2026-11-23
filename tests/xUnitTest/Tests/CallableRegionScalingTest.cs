// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
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

    // Z5-F and Z5-R1 (Z5-F's review N3, Z5-R1's design section 7): A5's work and Loan-set bytes on a chain of n Copies of a borrow of a
    // layer's enclosing Place, each read through right after it is made, at n = 64, 128, 256, every read placed by its exact lent position
    // (MirBorrows.Addresses' Place, one Lent per listed row; a whole local's lender kept per view): linear.
    [Fact]
    public void PlacedReadsAreLinearInACopyChain()
        => WorkScales(output, [.. new[] { 64, 128, 256 }.Select(MirBorrowsTest.LentChain)], 2);

    // Z5-R2 (its design section 6): A5's work and Loan-set bytes on a chain of n Copies of a layer's enclosing Place through no
    // dereference, each used right after it is made, at n = 64, 128, 256, each Copy carrying its owner's one own-step entry
    // (MirBorrows.Addresses' Layer, one row per Place a location uses): the work has a dense term, so it keeps the factor 5 of the dense
    // rows (measured: 8,870, 16,488, 32,108, about 116 n + n²/64; on dev, a located Unsupported_Kd at each Copy, 7,484, 13,884, 26,684);
    // the bytes are linear.
    [Fact]
    public void EnclosingCopiesScaleWithTheDenseTerm()
        => WorkScales(output, [.. new[] { 64, 128, 256 }.Select(MirBorrowsTest.EnclosingCopyChain)], 5);

    // Z5-R2 (its design section 6): A5's work and Loan-set bytes on a Field enclosing a layer k Tuples deep, read whole and used through
    // its k steps, at k = 16, 32, 64: within the per-prefix Owned walk's O(k²) (each prefix's walk and Encloses descent are bounded by the
    // Type's depth), the base's order.
    [Fact]
    public void ADeepEnclosingReadIsWithinItsWalk()
        => WorkScales(output, [.. new[] { 16, 32, 64 }.Select(MirBorrowsTest.DeepEnclosingRead)], 4);

    // RP8 (PLAN G492-a (iii); J1, the owner's O-264 Q5 (a), O-286 (a)): a comparison's proof reads the recorded bounds of each region it
    // names, each node and bound once (O(N + B) per request), so k chained `var` locals, each with a later store, and m comparisons over
    // the last one bind with premise work linear in m * k: (8, 8), (64, 8), (8, 64), (64, 64), within 1.5 times linear.
    [Fact]
    public void ComparisonsOverChainedRegionsScaleLinearly()
    {
        long[] work = [.. new[] { (8, 8), (64, 8), (8, 64), (64, 64) }.Select(static x => PremiseWork(ChainedComparisons(x.Item1, x.Item2)))];
        output.WriteLine($"premise work {string.Join(", ", work)}");
        Assert.True(work[1] <= 12 * work[0] && work[2] <= 12 * work[0] && work[3] <= 96 * work[0], $"premise work {string.Join(", ", work)}");
    }

    // PLAN G1082 (R7-O1 e; SPEC 15.3.7, 15.6.1): an anonymous function compared with a required signature of n inputs `ref/U` proves
    // each input well formed from that signature's own well-formedness; the comparison's requests share one premise environment of its
    // use and comparison, built once, so the premise work done (edges written, closure insertions and activations) grows linearly in n
    // at n = 32, 64, 128 (within 2.25x per doubling) with one environment, where each request rebuilt all 2n premises (4,224, 16,640,
    // 66,048). OriginProofMetrics' Nodes and Edges sum each closure's whole graph, so they are not this measure.
    [Fact]
    public void ALiteralsComparisonOverManyInputsScales()
    {
        var measured = new[] { 32, 64, 128 }.Select(static n =>
        {
            var inputs = string.Join(", ", Enumerable.Repeat("ref/U", n));
            var named = string.Join(", ", Enumerable.Range(1, n).Select(static i => $"y{i}: ref/U"));
            var metrics = Metrics("struct U {a}\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\nfunc k(p: ref/i32) -> i32\n" +
                $"    let c: ({inputs}) -> i32 = func [] ({named}) -> i32 => 0\n    return 0\npublic func main() => ()\n");
            return (Work: metrics.WrittenEdges + metrics.Insertions + metrics.Activations, metrics.Environments);
        }).ToArray();
        output.WriteLine($"premise work {string.Join(", ", measured)}");
        Assert.True(measured[1].Work <= 2.25 * measured[0].Work && measured[2].Work <= 2.25 * measured[1].Work, $"premise work {string.Join(", ", measured)}");
        Assert.All(measured, x => Assert.Equal(measured[0].Environments, x.Environments));
    }

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

    // The valid workload: `var x1 = p`, `x1 = q`, `var x2 = x1`, `x2 = q`, ..., then m comparisons `(ref/i64 during xk) -> ref/i64 during p`
    // of a Function Item, each the body's rows (SPEC 15.6.1, 15.6.5), under `origin q outlives p`.
    internal static string ChainedComparisons(int k, int m)
        => "func idf(w: ref/i64 during a) -> ref/i64 during a => w\nfunc outer(p: ref/i64, q: ref/i64) -> i32\n    origin q outlives p\n    var x1 = p\n    x1 = q\n" +
            string.Concat(Enumerable.Range(2, k - 1).Select(static i => $"    var x{i} = x{i - 1}\n    x{i} = q\n")) +
            string.Concat(Enumerable.Range(1, m).Select(i => $"    let f{i}: (ref/i64 during x{k}) -> ref/i64 during p = idf\n")) + "    return 0\npublic func main() => ()\n";

    private static string[] Sizes(string axis) => [.. new[] { 16, 32, 64 }.Select(n => VerificationWorkloads.CallableRegions(axis, n))];

    // The premise closures' nodes, edges and worklist insertions of one binding of a valid source.
    private static long PremiseWork(string source)
    {
        var metrics = Metrics(source);
        return metrics.Nodes + metrics.Edges + metrics.Insertions;
    }

    // The Origin proof counters of one binding of a valid source.
    private static OriginProofMetrics Metrics(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        var metrics = new OriginProofMetrics();
        c.Binding.OriginProofMetrics = metrics;
        c.Bind();
        Assert.Empty(c.Binding.Issues);
        return metrics;
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests(ITestOutputHelper output)
    {
        // V16: the cold analysis's allocation on each axis at N = 16, 32, 64 grows linearly (3), fixed-roots since a shorter meet's operand
        // edges form once per class (MirOrigins.Relate), fixed-joins since its ended sets lie apart from every holder's, so their Removes
        // make no memo entry (MirLoanSets.Remove, PLAN G768), though A3 6.3's "(ended points) × holders" visits stay in its Work.
        [Theory]
        [InlineData("results", 3)]
        [InlineData("regions", 3)]
        [InlineData("fixed-calls", 3)]
        [InlineData("fixed-roots", 3)]
        [InlineData("fixed-joins", 3)]
        public void TheColdAnalysisScalesWithinItsBound(string axis, double factor)
            => ColdAnalysisScales(output, Sizes(axis), factor);

        // PLAN G768 (V16): N chained borrows (`let a_k = a_(k-1)@ref`) end each borrowed local's Loan at its StorageLive, write and
        // StorageDead in every holder (E8), whose sets all lie apart from it, so an apart Remove makes no memo entry (MirLoanSets.Remove)
        // and the cold analysis allocates linearly in N (the memo held one entry per distinct holder set and ended set, N(N + 1)/2).
        [Fact]
        public void TheColdAnalysisOfABorrowChainAllocatesLinearly()
            => ColdAnalysisScales(output, [.. new[] { 128, 256, 512 }.Select(BorrowChain)], 3);

        // RP8 (PLAN G492-a (iii)): a warm rebind of comparisons over chained regions allocates nothing (the row sink is one reused list).
        [Fact]
        public void WarmComparisonsOverChainedRegionsAllocateNothing()
        {
            var c = MinimalEmissionTest.Analyze(ChainedComparisons(8, 8));
            Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
            Assert.True(valid, MinimalEmissionTest.Describe(c));
        }

        // A body of N chained borrows, each a reference to the one before: a_k is ref^k/i32 (SPEC 10.2's explicit acquisition of a
        // reference slot).
        private static string BorrowChain(int n)
            => "func f() -> i32\n    let a0: i32 = 1\n" + string.Concat(Enumerable.Range(1, n).Select(static k => $"    let a{k} = a{k - 1}@ref\n")) +
                "    return 0\npublic func main() => ()\n";
    }
}
