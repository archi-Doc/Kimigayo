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

    // DECISIONS BU-240 (C-359 C1, E2, E3; G812 rulings 97, 99, 104; SPEC 8.15.2): a check over n bindings that form no Type, its misfit (a
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

    // DECISIONS BU-240 (G812 rulings 136, 143, 156): a resting part is completed at its open formed parameter (OpenForm, one one-run search
    // per part), so a tree chain of n Tuple elements each holding a contextual Case whose payload is an anonymous function
    // (`(.Some(func (x) => true), (...))` at `(Option<(s0/Box) -> i32>, (...))`) takes split runs and formations of the open formed Type
    // (OpenMarkedWork: each declared component formed again below its mark; SN2) at most linear in n, and each body keeps its record
    // (7c04029eb completed none: one run and Unsupported_Kd alone at every n).
    [Fact]
    public void ASplitPartsTreeCompletesItsAnonymousFunctionsInRunsLinearInItsDepth()
    {
        var work = new[] { 10, 20, 40 }.Select(Work).ToArray();
        var (runs, formations) = (work.Select(static x => x.Runs).ToArray(), work.Select(static x => x.Formations).ToArray());
        Assert.True(
            MirBorrowsTest.Grows(runs, 2) && formations[0] < formations[1] && MirBorrowsTest.Grows(formations, 2),
            $"runs {string.Join(", ", runs)}; formations {string.Join(", ", formations)}");

        static (long Runs, long Formations) Work(int depth)
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
            var binding = MinimalEmissionTest.Analyze(source).Binding;
            return (binding.SplitCaseRuns, binding.OpenMarkedWork);
        }
    }

    // DECISIONS BU-240 (G812 ruling 139): a named call's selection on the split is judged in each combination of split cases, each run one
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

    // DECISIONS BU-240 (G812 rulings 141, 142; the Middle's C-359 re-check of 8b5807535): SelectionOnSplit's second search (every combination
    // asked whether it selects the canonical run's candidate, which reads no split) over a tree chain of n rL4-shaped named calls
    // (`h(w_i@move, 6, <level i + 1>)`: `h<s/W>`, refused only through the split, beside the applicable `h<X>`): every level accepted, as
    // h<X> is selected in every case, in split runs linear in n (n = 30, 60, 120; the Middle's rq30 and rq60).
    [Fact]
    public void ANamedCallsSecondSelectionSearchTakesRunsLinearInItsChain()
    {
        var runs = new[] { 30, 60, 120 }.Select(Runs).ToArray();
        Assert.True(runs[0] < runs[1] && MirBorrowsTest.Grows(runs, 2), $"runs {string.Join(", ", runs)}");

        static long Runs(int count)
        {
            var call = "0";
            for (var i = count - 1; i >= 0; i--)
            {
                call = $"h(w{i}@move, 6, {call})";
            }

            var source = "func h<s/W>(m: s/W, n: s/(ref/i64 during b), j: i32) -> i32\n    s is owner\n    return 0\n" +
                "func h<X>(m: X, n: i32, j: i32) -> i32\n    return 1\n" +
                $"func call<t/U>(m: t/U, {string.Join(", ", Enumerable.Range(0, count).Select(i => $"w{i}: U"))}) -> i32\n    t is owner or ref\n    return {call}\n";
            Assert.Equal("clean", DiagnosticCorpus.RecordProfile(source));
            return MinimalEmissionTest.Analyze(source).Binding.SplitCaseRuns;
        }
    }

    // SPEC 10.8 (G812 ruling 147): the waiting inputs and the wholes settle each other (SettleSources): a Tuple literal at `y: s/T` fixes
    // `s` through its whole's meet, and the n waiting inputs `x: s/(ref/X during b)` it held then settle in the next round (two rounds at
    // every n), so the settle's walks (TermWalkWork: each Type HasOpenApplication and FormedApplication enter) grow linearly in n
    // (n = 30, 60, 120) and the call binds.
    [Fact]
    public void WaitingInputsAWholeSettlesTakeWorkLinearInTheirCount()
    {
        long[] work = [.. new[] { 30, 60, 120 }.Select(count => (long)Bound(WaitingThroughAWhole(count)).Binding.TermWalkWork)];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");
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
    [InlineData("slots", 32)]
    [InlineData("slots", 512)]
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
    // G1128 (Q20; design R1.7, F's ruling 38): a slot's skeleton, its bindings' collection and the values' substitution decide each
    // distinct (bindings' sub-Types, polarity) query once (SlotWalkWork), so their work grows linearly in the depth of a DAG-shaped
    // binding, and its slot variables do not grow: two distinct bindings `x = (d, p)` and `y = (d, q)` share the dup chain's DAG `d` of 2^n
    // leaves (n distinct parts). The count is exact at any depth; the depths stay small because Binding's other walks over two
    // distinct bindings of a DAG still take time exponential in it (PLAN G1152, outside G1128). Two identical bindings `pick2(d, d)`
    // take no variables: one Type solves to itself (R1.3 (e)'s shortcut theorem). The rows bind only (the 2^n-leaf local is the
    // layout's limit).
    [Fact]
    public void ASlotsDagBindingIsWalkedOncePerDistinctPart()
    {
        var (w4, v4) = Work(4, "pick2(x, y)");
        var (w8, v8) = Work(8, "pick2(x, y)");
        var (w12, v12) = Work(12, "pick2(x, y)");
        Assert.True(w4 > 0 && v4 > 0, $"{w4} {v4}");
        Assert.Equal(w8 - w4, w12 - w8);
        Assert.Equal((v4, v4), (v8, v12));
        Assert.Equal((0, 0), Work(32, "pick2(d, d)"));

        static (int Walks, int Variables) Work(int depth, string call)
        {
            var chain = "p";
            for (var i = 0; i < depth; i++)
            {
                chain = $"dup({chain})";
            }

            var c = CompilationTestHelper.BindSuccess("func dup<T>(x: T) -> (T, T) => $abort(\"no\")\nfunc pick2<T>(x: T, y: T) -> T => $abort(\"no\")\n" +
                $"func u(p: ref/i32 during a, q: ref/i32 during b) -> i32\n    let d = {chain}\n    let x = (d, p)\n    let y = (d, q)\n    let r = {call}\n    return 0\n");
            return (c.Binding.SlotWalkWork, c.Binding.SlotVariableWork);
        }
    }

    [Theory]
    [InlineData("P3", "id", Explicit = true)]
    [InlineData("P3", "ov", Explicit = true)]
    [InlineData("guard", "jn")]
    public void NestedArgumentsTakeWorkLinearInTheirDepth(string unit, string form)
    {
        long[] work = [.. new[] { 10, 20, 40 }.Select(depth => Work(Bound(NestedArgument(form, depth))))];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"{form} [{unit}]: work {string.Join(", ", work)}");
    }

    // R7-P1's cost rows for the own paths P3 merges into the one call pipeline (R7-P0's census §1): a chain of depth 10, 20 and 40, each
    // level one call of the form, valid (SPEC 10.1, 10.4, 10.5.1): a Function-typed value (vc), an overloaded Function Item selected at a
    // parameter's Function Type (fi), an implicit constructor whose omitted base clause selects its base's (ic, a chain of derived structs,
    // SPEC 6.3.6) and an enum Case whose payload is a direct call's result (ec). The work is the shared pipeline's count, as in the rows above;
    // a form whose own path that count does not see today is Explicit until P3 routes it through the pipeline: on dev 1c6bc5939 fi, ic
    // and ec grow linearly, while value calls are not counted at all (vc's count is 0 at every depth, and a check at depths 10 to 80
    // shows no growth in time either), so vc states that P3's pipeline counts them. No existing row pins vc's form.
    [Theory]
    [InlineData("P3", "vc", Explicit = true)]
    [InlineData("guard", "fi")]
    [InlineData("guard", "ic")]
    [InlineData("guard", "ec")]
    public void EachOwnPathCallFormTakesWorkLinearInItsDepth(string unit, string form)
    {
        long[] work = [.. new[] { 10, 20, 40 }.Select(depth => Work(Bound(OwnPathChain(form, depth))))];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"{form} [{unit}]: work {string.Join(", ", work)}");
    }

    // G379 (a)'s C3 (SPEC 10.5.1, 10.1 step 5): a chain of nested Tuple literals with an erasure at its innermost element, which step 5 reads
    // among the element positions (tl): at depth 10, 20 and 40 the first run is inapplicable there and run<T> is selected, and the one call's
    // selection work does not grow with the depth (each element position is fitted and read once per candidate). From depth 80 the Type's
    // nesting passes the implementation's bounds (OwnershipDepthLimit_Kd, GenerationResourceLimit_Kd, SPEC 23.1.1), as with run<T> alone.
    [Fact]
    public void AnElementsErasureInANestedLiteralChainTakesSelectionWorkIndependentOfItsDepth()
    {
        long[] work = [.. new[] { 10, 20, 40 }.Select(depth => Work(Bound(NestedArgument("tl", depth))))];
        Assert.True(work.Distinct().Count() == 1, $"work {string.Join(", ", work)}");
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
            "func see(target: ref/i32, by: i32) -> i32 => by\nlet c = true\nlet x: i64 = 2\nlet a: i32 = 1\nlet b: i32 = 2\n" +
            (workload == "slots" ? "func pick2<T>(x: T, y: T) -> T => x@move\n" : string.Empty));
        for (var i = 0; i < calls; i++)
        {
            source.Append($"let r{i} = ").Append(workload switch
            {
                "calls" => $"identity({i})",
                "waiting" => $"take(identity({i}))",
                "joins" => $"identity(if c => x else => {i})",
                "references" => $"see(if c => a@ref else => b@ref, {i})",
                "slots" => "pick2(a@ref, b@ref)",
                _ => $"identity(if c => (x, {i}) else => ({i}, x))",
            }).Append('\n');
        }

        return source.ToString();
    }

    private static string WaitingThroughAWhole(int count)
    {
        var all = Enumerable.Range(0, count).ToArray();
        return "func stop() -> Never => $abort(\"stop\")\n" +
            $"func w<s/T, {string.Join(", ", all.Select(i => $"X{i}"))}>({string.Join(", ", all.Select(i => $"x{i}: s/(ref/X{i} during b) during a"))}, y: s/T) -> i32\n" +
            "    s is owner or ref\n    return stop()\n" +
            "func use(p: ref/i32, rr: ref/(ref/i32 during p)) -> i32\n" +
            $"    let q = w({string.Join(", ", all.Select(_ => "rr"))}, (1, 2))\n    return 0\n";
    }

    private static string NestedArgument(string form, int depth)
    {
        var argument = form == "tl" ? "c@move" : "1";
        var element = "(ref/i32 during q) -> i32"; // tl: the first run's Type at the literal's position.
        for (var i = 0; i < depth; i++)
        {
            argument = form switch
            {
                "id" => $"identity({argument})",
                "ov" => $"pick({argument})",
                "tl" => $"({argument}, q)",
                _ => $"identity(if c => {argument} else => 2)",
            };
            element = $"({element}, ref/i32 during q)";
        }

        return form == "tl"
            ? $"func run(q: ref/i32, t: {element}) -> i32 => 0\nfunc run<T>(q: ref/i32, t: T) -> i32 => 1\nfunc k(p: ref/i32, q: ref/i32) -> i32\n" +
                $"    var a: i32 = 1\n    let r = a@ref\n    let c = func [r] (x: ref/i32 during q) -> i32 => 0\n    return run(q, {argument})\n()"
            : "func identity<T>(v: T) -> T => v@move\nfunc take(value: i64) -> i32 => 0\nfunc pick(v: i32) -> i32 => v\nfunc pick(v: i64) -> i64 => v\n" +
                $"func k(c: bool) -> i32\n    return take({argument})\n()";
    }

    private static string OwnPathChain(string form, int depth)
    {
        if (form == "ic")
        {
            return "open struct C0\n    public init() => ()\n" + string.Concat(Enumerable.Range(1, depth).Select(static i => $"open struct C{i}: C{i - 1}\n")) + "()";
        }

        var argument = "1";
        for (var i = 0; i < depth; i++)
        {
            argument = form switch
            {
                "vc" => $"g({argument})",
                "fi" => $"run(pick, {argument})",
                _ => $"tag(E.A({argument}))",
            };
        }

        var head = form switch
        {
            "vc" => "func k(g: (i32) -> i32) -> i32\n",
            "fi" => "func pick(v: i32) -> i32 => v\nfunc pick(v: i64) -> i64 => v\nfunc run(f: (i32) -> i32, v: i32) -> i32 => f(v)\nfunc k() -> i32\n",
            _ => "enum E\n    A(i32)\nfunc tag(e: E) -> i32 => 0\nfunc k() -> i32\n",
        };
        return $"{head}    return {argument}\n()";
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
