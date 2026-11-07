// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class InferenceSlotScalingTest
{
    [Theory]
    [InlineData(false, 65)]
    [InlineData(true, 65)]
    [InlineData(false, 130)]
    [InlineData(true, 130)]
    public void StructuralSlotsHaveNoWordSizeLimit(bool constructor, int count)
    {
        var c = MinimalEmissionTest.Analyze(Wide(count, constructor));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(false, 65)]
    [InlineData(true, 65)]
    [InlineData(false, 130)]
    [InlineData(true, 130)]
    public void AnUnboundHighSlotRemainsRankableAndNamesItsActualSlot(bool constructor, int count)
    {
        var c = MinimalEmissionTest.Analyze(Wide(count, constructor, true));
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnboundTypeArgument_Kd", record.Code);
        Assert.Contains($"T{count - 1}", record.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void AHighWaitingArgumentRetainsItsUnboundSignature()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 65).Select(i => $"p{i}: i32"));
        var values = string.Join(", ", Enumerable.Repeat("0", 65));
        var c = MinimalEmissionTest.Analyze($"func accept<T>({parameters}, action: (T) -> i32) => ()\naccept({values}, func (x) => 0)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnboundTypeArgument);
    }

    // G216 (a) U3a (C-38 B3; DECISIONS BU-104): premise discovery reads the evidence names once per solve, so its reads grow linearly
    // in a literal's elements beside the evidence that names the callee's Origins (`mk([.None x N], q, r)`), not with every leaf.
    [Fact]
    public void PremiseDiscoveryReadsGrowLinearlyInALiteralsElements()
    {
        var work = new[] { 200, 400, 800 }.Select(Work).ToArray();
        Assert.True(MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");

        static long Work(int count)
        {
            var c = MinimalEmissionTest.Analyze(
                "func mk(p: Array<Option<ref/(ref/i32 during a) during b>>, x: ref/i32 during a, y: ref/i32 during b) -> i32 => 0\n" +
                "func k(q: ref/i32, r: ref/i32) -> i32\n    return mk([" + string.Join(", ", Enumerable.Repeat(".None", count)) + "], q, r)\n");
            return c.Binding.EvidenceNameWork;
        }
    }

    // R7-I1's cost rows (the plan's I2 row and exit item 3): BindingBenchmark's `Calls` workload and the argument forms I2 rewrites, each a valid
    // program (SPEC 10.5.1, 14.9.2). A warm rebind allocates nothing at 32 and at 512 calls: revision B's review B5 found 104 B per rebind on a
    // reference join at a call argument, which no regression saw.
    [Theory]
    [InlineData("calls", 32)]
    [InlineData("calls", 512)]
    [InlineData("waiting", 32)]
    [InlineData("waiting", 512)]
    [InlineData("joins", 32)]
    [InlineData("joins", 512)]
    [InlineData("references", 32)]
    [InlineData("references", 512)]
    [InlineData("parts", 32)]
    [InlineData("parts", 512)]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfCallsAllocatesNothing(string workload, int calls)
    {
        var c = MinimalEmissionTest.Analyze(CallWorkload(workload, calls));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c));
    }

    // The cold rows: from 32 to 512 calls a first Bind's inference work grows by one amount per call (272 lies halfway, so the two steps compare),
    // and its allocation is linear as CallableRegionScalingTest's V16 rows define it: three sizes, each double the one before, within factor 3. That
    // passes a mixed term, an added 4n^2 bytes on all five workloads, so a second series spans the plan's 32 to 512: its sizes each quadruple, so
    // linear gives 4, and factor 6 keeps V16's headroom. There an added 3n^2 bytes fails `calls` and `parts`; a smaller term passes both series.
    [Theory]
    [InlineData("calls")]
    [InlineData("waiting")]
    [InlineData("joins")]
    [InlineData("references")]
    [InlineData("parts")]
    [Trait("Purpose", "Allocation")]
    public void AColdBindOfCallsIsLinear(string workload)
    {
        long[] work = [.. new[] { 32, 272, 512 }.Select(calls => Work(Bound(CallWorkload(workload, calls))))];
        Assert.True(work[0] < work[1], $"work {string.Join(", ", work)}");
        Assert.Equal(work[2] - work[1], work[1] - work[0]);
        var doubling = ColdBytes(workload, [128, 256, 512]);
        Assert.True(doubling[0] < doubling[1] && MirBorrowsTest.Grows(doubling, 3), $"bytes {string.Join(", ", doubling)}");
        var span = ColdBytes(workload, [32, 128, 512]);
        Assert.True(span[0] < span[1] && MirBorrowsTest.Grows(span, 6), $"bytes {string.Join(", ", span)}");
    }

    // The n = 40 rows (SPEC 10.5.1: inner calls are never resolved once per outer candidate; 10.3): a nested call that waits is checked at the
    // Type its position gives, once, so the count of candidate evaluations at depth 10, 20 and 40 is linear: nested generic calls, an overloaded
    // call at every level, and a join in every nested call. The count does not read the join walks (labels-log.md, G335), so jn pins the calls
    // around its joins only. A unit other than guard is the plan's unit that makes the row pass: the row is Explicit until that unit lands and
    // removes the marker. P3 owns "prepare arguments once": dev's PrepareCallArguments probes a Call part again at each completion, so its count
    // at depth d is 734 + 4d + d^2 (id) and 734 + 8d + 3d^2 (ov).
    [Theory]
    [InlineData("P3", "id", Explicit = true)]
    [InlineData("P3", "ov", Explicit = true)]
    [InlineData("guard", "jn")]
    public void NestedArgumentsTakeWorkLinearInTheirDepth(string unit, string form)
    {
        long[] work = [.. new[] { 10, 20, 40 }.Select(depth => Work(Bound(NestedArgument(form, depth))))];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"{form} [{unit}]: work {string.Join(", ", work)}");
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmLargeSlotSetsReuseTheirTailStorage()
    {
        var c = MinimalEmissionTest.Analyze(Wide(130, true));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c));
    }

    private static string CallWorkload(string workload, int calls)
    {
        var source = new System.Text.StringBuilder("func identity<T>(value: T) -> T => value@move\nfunc take(value: i64) -> i32 => 0\n" +
            "func see(target: ref/i32, by: i32) -> i32 => by\nlet c = true\nlet x: i64 = 2\nlet a: i32 = 1\nlet b: i32 = 2\n");
        for (var i = 0; i < calls; i++)
        {
            source.Append($"let r{i} = ").Append(workload switch
            {
                "calls" => $"identity({i})",
                "waiting" => $"take(identity({i}))",
                "joins" => $"identity(if c => x else => {i})",
                "references" => $"see(if c => a@ref else => b@ref, {i})",
                _ => $"identity(if c => (x, {i}) else => ({i}, x))",
            }).Append('\n');
        }

        return source.ToString();
    }

    private static string NestedArgument(string form, int depth)
    {
        var argument = "1";
        for (var i = 0; i < depth; i++)
        {
            argument = form switch
            {
                "id" => $"identity({argument})",
                "ov" => $"pick({argument})",
                _ => $"identity(if c => {argument} else => 2)",
            };
        }

        return "func identity<T>(v: T) -> T => v@move\nfunc take(value: i64) -> i32 => 0\nfunc pick(v: i32) -> i32 => v\nfunc pick(v: i64) -> i64 => v\n" +
            $"func k(c: bool) -> i32\n    return take({argument})\n()";
    }

    private static long[] ColdBytes(string workload, int[] calls)
    {
        var compilations = new Compilation[calls.Length];
        var bytes = AllocationMeasurement.MeasureSeries(calls.Length, i =>
        {
            var c = compilations[i] = CompilationTestHelper.Parse(CallWorkload(workload, calls[i]));
            return () => c.Bind();
        });
        Assert.All(compilations, static c => Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c)));
        return bytes;
    }

    private static Compilation Bound(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        c.Binding.MeasureCallInference = true;
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        return c;
    }

    private static long Work(Compilation c)
    {
        var metrics = c.Binding.InferenceMetrics;
        return metrics.Mappings + metrics.Comparisons + metrics.CandidateChecks;
    }

    private static string Wide(int count, bool constructor, bool unbound = false)
    {
        var slots = string.Join(", ", Enumerable.Range(0, count).Select(i => $"T{i}"));
        var parameters = string.Join(", ", Enumerable.Range(0, count - (unbound ? 1 : 0)).Select(i => $"p{i}: T{i}"));
        var values = string.Join(", ", Enumerable.Repeat("n", count - (unbound ? 1 : 0)));
        return (constructor ? $"struct Wide<{slots}>\n    public init({parameters}) => ()\n" : $"func wide<{slots}>({parameters}) => ()\n") +
            "let n: i32 = 42\n" + (constructor ? "let result = Wide.init" : "wide") + $"({values})";
    }
}
