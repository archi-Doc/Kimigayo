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

    // PLAN G647 (SPEC 10.2): a Type parameter is inferred from an argument of n ref layers, composed in the body so that no Origin is
    // named, past the 64 components the slot walks (MentionsSlots, Unnameable) once counted. Those two walks have no counter of their
    // own: the row is the acceptance, and the inference's mappings and comparisons, which grow with the layers.
    [Fact]
    public void ASlotIsInferredFromALayeredArgument()
    {
        var work = new[] { 36, 72, 144 }.Select(Work).ToArray();
        Assert.True(MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");

        static long Work(int layers)
        {
            var source = "func id<T>(v: T) -> T => v@move\nfunc f() -> i32\n    let n: i32 = 1\n    let r1 = n@ref\n" +
                string.Concat(Enumerable.Range(1, layers - 1).Select(static i => $"    let r{i + 1} = r{i}@ref\n")) + $"    let z = id(r{layers})\n    return z\n";
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
            c.Binding.MeasureCallInference = true;
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.InferenceMetrics.Mappings + c.Binding.InferenceMetrics.Comparisons;
        }
    }

    // DECISIONS D3 (C-359 C1, E2, E3; G812 rulings 97, 99, 104; SPEC 8.15.2): a check over n bindings that form no Type, its misfit (a
    // Tag at the last `s/Box`) failing in every combination of their cases, is judged in the 9 cases of the one binding its failing
    // part reads, at every n: a failing run blames only that part's bindings, and the bindings earlier, passing parts read keep
    // their case. So for a reference `put<U0, ...>` at S and for the value call of the same Item (32f0cde25: 8n + 1 and
    // 8(2^n - 1) + 1 combinations, resting on the split at the bound from 8 and 4 bindings).
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 4)]
    [InlineData(false, 8)]
    [InlineData(false, 16)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 4)]
    [InlineData(true, 8)]
    public void ASplitCaseSearchJudgesOnlyTheCasesOfItsFailingPart(bool call, int bindings)
    {
        var all = Enumerable.Range(0, bindings).ToArray();
        var reference = $"    let f: ({List(i => $"U{i}")}, {string.Join(", ", Enumerable.Repeat("Box", bindings - 1).Append("Tag"))}) -> i32 = put<{List(i => $"U{i}")}>\n    return 0\n";
        var valueCall = $"    let f = put<{List(i => $"U{i}")}>\n    let g = Tag.init(1)\n" + string.Concat(all.Skip(1).Select(i => $"    let b{i} = Box.init({i})\n")) +
            $"    return f({List(i => $"u{i}@move")}, {string.Join(", ", all.Skip(1).Select(i => $"b{i}@move").Append("g@move"))})\n";
        var source = "struct Box\n    public let v: i32\n    public init(v: i32)\n        self.v = v@move\n" +
            "struct Tag\n    public let v: i32\n    public init(v: i32)\n        self.v = v@move\n" +
            $"func put<{List(i => $"s{i}/W{i}")}>({List(i => $"m{i}: s{i}/W{i}")}, {List(i => $"b{i}: s{i}/Box")}) -> i32\n    return 0\n" +
            $"func call<{List(i => $"t{i}/U{i}")}>({List(i => $"k{i}: t{i}/U{i}, u{i}: U{i}")}) -> i32\n" +
            string.Concat(all.Select(i => $"    t{i} is owner or ref\n    U{i} is Owned\n")) + (call ? valueCall : reference);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(9, c.Binding.SplitCaseRuns);
        Assert.Contains(c.Binding.Issues, x => x.Failure == (call ? BindingFailure.NoApplicableCandidate : BindingFailure.TypeMismatch));

        string List(Func<int, string> item) => string.Join(", ", all.Select(item));
    }

    // DECISIONS D3 (G812 ruling 136): the walk of a split part's tree completes every anonymous function it reaches, each by one
    // one-run search (CanonicalForm), so a tree chain of n Tuple elements each holding a contextual Case whose payload is an anonymous
    // function (`(.Some(func (x) => true), (...))` at `(Option<(s0/Box) -> i32>, (...))`) takes split runs linear in n, and each
    // body keeps its record (7c04029eb completed none: one run and Unsupported_Kd alone at every n).
    [Fact]
    public void ASplitPartsTreeCompletesItsAnonymousFunctionsInRunsLinearInItsDepth()
    {
        var runs = new[] { 10, 20, 40 }.Select(Runs).ToArray();
        Assert.True(MirBorrowsTest.Grows(runs, 2), $"runs {string.Join(", ", runs)}");

        static long Runs(int depth)
        {
            var (type, argument) = ("i32", "1");
            for (var i = 0; i < depth; i++)
            {
                (type, argument) = ($"(Option<(s0/Box) -> i32>, {type})", $"(.Some(func (x) => true), {argument})");
            }

            var source =
                "struct Box\n    public let v: i32\n    public init(v: i32)\n        self.v = v@move\nfunc stop() -> Never => $abort(\"stop\")\n" +
                $"func putN<s0/W0>(m0: s0/W0, n: {type}, k: s0/Box during c) -> i32\n    return 0\n" +
                "func call<t0/U0>(k0: t0/U0, u0: U0) -> i32\n    t0 is owner or ref\n    U0 is Owned\n" +
                $"    let f = putN<U0>\n    return f(u0@move, {argument}, stop())\n";
            Assert.Equal($"{depth}*TypeMismatch_Kd {depth}*UnprovenConstraint_Kd", DiagnosticCorpus.RecordProfile(source));
            return MinimalEmissionTest.Analyze(source).Binding.SplitCaseRuns;
        }
    }

    // DECISIONS D3 (G812 ruling 139): a named call's selection on the split is judged in each combination of split cases, each run one
    // query over the group (SelectionOnSplit): with n split parameters (`b: s0/Box during c`) and a candidate `k: Tag` beside one at
    // `k: s0/Box during c`, the canonical run selects and the search ends there, so the split runs grow linearly in n (n = 10, 20, 40),
    // and the Closure is completed at the selected candidate's `(s0/Box) -> i32` with its body's record; with the second candidate at
    // `n: (s0/Box) -> i64` the call is ambiguous wherever a candidate applies and keeps its AmbiguousBinding_Kd (e5ed671be:
    // Unsupported_Kd alone in both).
    [Theory]
    [InlineData(false, "1*TypeMismatch_Kd 1*UnprovenConstraint_Kd")]
    [InlineData(true, "1*AmbiguousBinding_Kd")]
    public void ANamedCallsSelectionOnTheSplitTakesRunsLinearInItsSplitParameters(bool ambiguous, string records)
    {
        var runs = new[] { 10, 20, 40 }.Select(Runs).ToArray();
        Assert.True(MirBorrowsTest.Grows(runs, 2), $"runs {string.Join(", ", runs)}");

        long Runs(int count)
        {
            var all = Enumerable.Range(0, count).ToArray();
            var parameters = string.Join(", ", all.Select(i => $"b{i}: s0/Box during c"));
            var second = ambiguous ? "n: (s0/Box) -> i64, k: s0/Box during c" : "n: (s0/Box) -> i32, k: Tag";
            var source = "struct Box\n    public let v: i32\n    public init(v: i32)\n        self.v = v@move\n" +
                "struct Tag\n    public let v: i32\n    public init(v: i32)\n        self.v = v@move\nfunc stop() -> Never => $abort(\"stop\")\n" +
                $"func put<s0/W0>(m0: s0/W0, {parameters}, n: (s0/Box) -> i32, k: s0/Box during c) -> i32\n    return 0\n" +
                $"func put<s0/W0>(m0: s0/W0, {parameters}, {second}) -> i32\n    return 0\n" +
                "func call<t0/U0>(k0: t0/U0, u0: U0) -> i32\n    t0 is owner or ref\n    U0 is Owned\n    let g = Tag.init(2)\n" +
                string.Concat(all.Select(i => $"    let b{i} = Box.init({i})\n")) +
                $"    return put<U0>(u0@move, {string.Join(", ", all.Select(i => $"b{i}@move"))}, func (x) => true, {(ambiguous ? "stop()" : "g@move")})\n";
            Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));
            return MinimalEmissionTest.Analyze(source).Binding.SplitCaseRuns;
        }
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

    // The n = 40 rows (SPEC 10.5.1: inner calls are never resolved once per outer candidate; 10.3): a nested call that waits is checked at the
    // Type its position gives, once, so the count of candidate evaluations at depth 10, 20 and 40 is linear: nested generic calls, an overloaded
    // call at every level, and a join in every nested call. No public counter reads the join walks (PLAN G335), so jn pins the calls
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

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        // The cold rows: from 32 to 512 calls a first Bind's inference work grows by one amount per call (272 lies halfway, so the two steps compare),
        // and its allocation is linear as CallableRegionScalingTest's V16 rows define it: three sizes, each double the one before, within factor 3. That
        // passes a mixed term, an added 4n^2 bytes on all five workloads, so a second series spans the plan's 32 to 512: its sizes each quadruple, so
        // linear gives 4, and factor 6 keeps V16's headroom. An added bn^2 bytes then fails one series or the other from about b = 2.1 on `parts`, 2.7 on
        // `waiting`, 2.9 on `calls`, 4.5 on `joins` and 5.1 on `references`, by arithmetic on dev's bytes; a term below its workload's limit passes both.
        [Theory]
        [InlineData("calls")]
        [InlineData("waiting")]
        [InlineData("joins")]
        [InlineData("references")]
        [InlineData("parts")]
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
    }
}
