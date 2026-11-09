// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 8.11.4: bounds constrain selected uses, never overload applicability.
public class CallableEffectBoundTest
{
    // The generic programs of LaterCallsOfACallableValueReachByPaths and ReplacingTheCallableChangesNoVerdict's second calls (the same, a
    // replaced and another Callable value), which AbstractCallReachTest also instantiates (L1).
    internal const string TwiceByReference = "func twice<S,F>(source: uniq/S, next: ref/F) -> (S.Item, S.Item)\n    S is Source\n    F is Callable<(uniq/S) -> S.Item>\n" +
        "    let first = next(source)\n    let second = next(source)\n    return (first@move, second@move)\n";

    internal const string SameCallable = "    let second = f(source)\n";

    internal const string ReplacedCallable = "    f = two@move\n    let second = f(source)\n";

    internal const string OtherCallable = "    var g = two@move\n    let second = g(source)\n";

    private const string Apply = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\n";

    [Fact]
    public void AcceptsAConfinedClosure()
    {
        var c = MinimalEmissionTest.Analyze(Apply + "public func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32 => 12\n    return apply(f@ref)\n");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    private static readonly string ApplyFromGroup = "group Callbacks\n    public " + Apply.TrimEnd('\n').Replace("\n", "\n    ", StringComparison.Ordinal) + "\n";

    // SPEC 8.11.2, 7.7.2: mutable static storage is environmental through either its Property name or its accessor's storage name. A4's
    // record is at the selected argument, without the effect's Label or a Note (AssertA4Record).
    [Theory]
    [InlineData("group State\n    public var count: i32 = 0\nfunc entry() -> i32\n    let f = func [] () -> i32 => State.count\n    return Callbacks.apply(f@ref)\n", "f@ref")]
    [InlineData("group State\n    public var count: i32 = 0\n        get() -> i32\n            let f = func [] () -> i32 => storage\n            return Callbacks.apply(f@ref)\n", "f@ref")]
    [InlineData("group State\n    public var count: i32 = 0\n        get() -> i32\n            let f = func [] () -> i32\n                storage = 3\n                return 0\n            return Callbacks.apply(f@ref)\n", "f@ref")]
    [InlineData("group State\n    public var count: i32 = 0\n        get() -> i32\n            func f() -> i32 => storage\n            return Callbacks.apply(f)\n", "f")]
    public void ReportsTheViolatingCallableAfterSelection(string declarations, string argument)
    {
        var c = MinimalEmissionTest.Analyze(ApplyFromGroup + declarations + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analyze().IsVerified);
        c.ReportAnalysis();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Code == "UnsatisfiedEffectBound_Kd");
        Assert.Equal("UnsatisfiedEffectBound_Kd", error.Code);
        Assert.Equal(argument, error.Text);
        Assert.Null(error.Label);
        Assert.Null(error.Note);
    }

    // SPEC 8.11.2: immutable storage is not mutable-static, so the custom getter's closure that reads it keeps apply's confined bound. The
    // old analysis refused the initializer's cleanup with Unsupported_Kd at `storage` and `f@ref`; since the MIR decides acceptance (H5,
    // DECISIONS R5-M185) the program is accepted (H3b 1a, T15, the owner 2026-11-03): check publishes no record, and a Library build (every
    // root, K11) generates the getter and apply.
    [Fact]
    public void ImmutableStaticStorageKeepsTheConfinedBound()
    {
        var source = ApplyFromGroup + "group State\n    public let count: i32 = 0\n        get() -> i32\n            let f = func [] () -> i32 => storage\n            return Callbacks.apply(f@ref)\npublic func main() => ()\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Contains("apply", MinimalEmissionTest.GeneratedBodies(source, OutputKind.Library, accepted: true));
    }

    // SPEC 8.11.4: the Reason names the cause and the Note the rule; `confined` appears at most once in one Constraint.
    [Theory]
    [InlineData("Copy", "F is Copy", "effect confined", "single Callable", "separate clauses")]
    [InlineData("Grouped", "F is (Callable<() -> i32>)", "effect confined", "single Callable", "separate clauses")]
    [InlineData("Combined", "F is Callable<() -> i32> and Copy", "effect confined", "single Callable", "separate clauses")]
    [InlineData("Duplicate", "F is Callable<() -> i32>", "effect confined\n        effect confined", "already declares confined", "declares confined at most once")]
    public void InvalidDeclarationsExplainTheirCause(string name, string constraint, string bounds, string cause, string note)
    {
        var source = $"func check<F>(f: F) -> ()\n    {constraint}\n        {bounds}\n    return\npublic func main() => ()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("InvalidEffectBound_Kd", record.Code);
        Assert.Equal(bounds[(bounds.LastIndexOf('\n') + 1)..].TrimStart(), source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.Contains(cause, record.Label);
        Assert.Contains(note, record.Note);
        Assert.NotEmpty(record.Related!);
        Assert.False(string.IsNullOrEmpty(name));
    }

    // SPEC 8.11.3, 8.11.4: an invalid item supplies no bound, so the clause after it is no duplicate (PLAN G219, c2).
    [Theory]
    [InlineData("effect Runner.run confined", "InvalidEffectBound_Kd", "effect Runner.run confined")]
    [InlineData("effect bogus", "ExpectedSyntax_Kd", "bogus")]
    public void AnInvalidItemIsNoEarlierBound(string invalid, string code, string at)
    {
        var source = "contract Runner\n    func run(self: ref/Self) -> i32\n" +
            $"func check<F>(f: F) -> ()\n    F is Callable<() -> i32>\n        {invalid}\n        effect confined\n    return\npublic func main() => ()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal(source.IndexOf(at, StringComparison.Ordinal), record.Span!.Value.Start);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForwardingUsesOnlyDeclaredPremises(bool bounded)
    {
        var source = Apply + "func forward<G>(g: ref/G) -> i32\n    G is Callable<() -> i32>\n" +
            (bounded ? "        effect confined\n" : string.Empty) + "    return apply(g)\npublic func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32 => 8\n    return forward(f@ref)\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(bounded, c.Analysis.IsVerified);
        if (!bounded)
        {
            AssertA4Record(source, DiagnosticCode.UnsatisfiedEffectBound_Kd, MirTargetSide.At("apply(g)", "g"));
        }
    }

    [Fact]
    public void ErasureKeepsNoBound()
    {
        var source = Apply + "public func main() => ()\nfunc entry() -> i32\n    let f: () -> i32 = func [] () -> i32 => 5\n    return apply(f@ref)\n";
        AssertA4Record(source, DiagnosticCode.UnsatisfiedEffectBound_Kd, MirTargetSide.At("apply(f@ref)", "f@ref"));
    }

    // SPEC 8.11.2 (DECISIONS R5-M145): a function item passed under a bound carries its callees' effects, so outer, whose callee reads mutable
    // static storage, violates confined at its argument and outer over a pure callee does not; the static is declared only where it is read.
    [Theory]
    [InlineData("group State\n    public var count: i32 = 0\n", "State.count", false)]
    [InlineData("", "42", true)]
    public void FunctionItemsUseTheirTransitiveEffects(string declarations, string body, bool valid)
    {
        var source = Apply + declarations + $"func inner() -> i32 => {body}\nfunc outer() -> i32 => inner()\npublic func main() => ()\nfunc entry() -> i32 => apply(outer)\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(valid, c.Analysis.IsVerified);
        var at = (source.LastIndexOf("outer)", StringComparison.Ordinal), "outer".Length);
        var violations = DiagnosticCorpus.Check(source).Diagnostics.Where(static x => x.Code == "UnsatisfiedEffectBound_Kd").Select(static x => (x.Span!.Value.Start, x.Span.Value.Length));
        Assert.Equal(valid ? [] : [at], violations);
    }

    [Theory]
    [InlineData("ref", true)]
    [InlineData("owner", false)]
    public void OwningCallsIncludeRemainingEnvironmentDestruction(string receiver, bool valid)
    {
        var source = "struct Item\n    public var value: i32 = 7\n    drop => Console.writeLine(\"drop\")\n" +
            $"func apply<F>(f: {(receiver == "ref" ? "ref/F" : "F")}) -> i32\n    F is Callable<{receiver}, () -> i32>\n        effect confined\n    return {(receiver == "ref" ? "f()" : "f@move()")}\n" +
            $"public func main() => ()\nfunc entry() -> i32\n    let item = Item.init()\n    let f = func [item@move] () -> i32 => item.value\n    return apply(f@{(receiver == "ref" ? "ref" : "move")})\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(valid, c.Analysis.IsVerified);
    }

    [Fact]
    public void TypeFormationChecksAnErasedArgumentEvenWithoutCalls()
    {
        const string Source = "struct Holder<F>\n    F is Callable<() -> i32>\n        effect confined\nfunc inspect(h: ref/Holder<() -> i32>) => ()\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(Source).Diagnostics;
        Assert.NotEmpty(records);
        Assert.All(records, static r => Assert.Equal("UnsatisfiedEffectBound_Kd", r.Code));
    }

    [Fact]
    public void CallerRecordReachesConsoleAndBothLspPlacements()
    {
        const string Source = Apply + "func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\npublic func main() => ()\nfunc entry() -> i32 => apply(noisy)\n";
        var check = AssertA4Record(Source, DiagnosticCode.UnsatisfiedEffectBound_Kd, MirTargetSide.At("apply(noisy)", "noisy"));
        var record = check.Diagnostics[0];
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        Assert.Contains(record.Message + " : " + record.Code, console.Text);
        Assert.Contains("apply(noisy)", console.Text);
        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var lsp = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal((record.Code, record.Message), (lsp.Code, lsp.Message));
        }
    }

    // SPEC 8.11.4, 8.11.3 (the adopted proposal Abstract Calls Reach by Paths): a call through a Callable-constrained value reaches by
    // path what its receiver and arguments reach; an earlier result that keeps no Loan of those Places does not conflict with a later
    // call of the same value (CallEffectConflict_Kd and R1g's Unsupported_Kd before the adoption).
    [Fact]
    public void LaterCallsOfACallableValueReachByPaths()
    {
        var source = "contract Source\n    associate Item\n" + TwiceByReference + "public func main() => ()\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Theory]
    [InlineData("ref", "ref", true)]
    [InlineData("owner", "ref", true)]
    [InlineData("uniq", "ref", true)]
    [InlineData("ref", "owner", false)]
    [InlineData("uniq", "owner", false)]
    public void OnlyAnEqualOrStrongerReceiverPremiseCoversTheBound(string premise, string required, bool valid)
    {
        var source = $"func need<F>() -> ()\n    F is Callable<{required}, () -> i32>\n        effect confined\n    return\n" +
            $"func forward<G>() -> ()\n    G is Callable<{required}, () -> i32>\n    G is Callable<{premise}, () -> i32>\n        effect confined\n    need<G>()\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(valid, c.Analysis.IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplementationsCannotAddAnUndeclaredCallableBound(bool premise)
    {
        var source = "contract Runner\n    func run<F>(self: ref/Self, callback: ref/F) -> i32\n        F is Callable<() -> i32>\n" +
            (premise ? "            effect confined\n" : string.Empty) +
            "struct Worker\n    Self is Runner\n    public func run<F>(self: ref/Self, callback: ref/F) -> i32\n        F is Callable<() -> i32>\n            effect confined\n        return callback()\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (premise)
        {
            Assert.Empty(records);
        }
        else
        {
            var diagnostic = Assert.Single(records);
            Assert.Equal("IncompatibleContractImplementation_Kd", diagnostic.Code);
            Assert.Contains("confined", diagnostic.Label);
        }
    }

    // SPEC 8.11.4, 8.6.4 (PLAN G671): a selected requirement call's Callable clause reads the Contract's parameters at its reference, so under
    // `C is Mapper<i32>` map's `F is Callable<(T) -> i32>` is `(i32) -> i32`, which G's premise meets; only its bound decides the call. Under
    // `D is Mapper<bool>` it is `(bool) -> i32`, which G's premise does not meet.
    [Theory]
    [InlineData("<C, G>(c: uniq/C, g: ref/G) -> i32\n        C is Mapper<i32>\n        G is Callable<(i32) -> i32>\n            effect confined\n        return c.map(g)\n", "clean")]
    [InlineData("<C, G>(c: uniq/C, g: ref/G) -> i32\n        C is Mapper<i32>\n        G is Callable<(i32) -> i32>\n        return c.map(g)\n", "UnsatisfiedEffectBound_Kd@g")]
    [InlineData(
        "<C, D, G>(c: uniq/C, d: uniq/D, g: ref/G) -> i32\n        C is Mapper<i32>\n        D is Mapper<bool>\n        G is Callable<(i32) -> i32>\n            effect confined\n        return d.map(g)\n",
        "NoApplicableOverload_Kd@d.map(g)")]
    public void ARequirementsCallableClauseReadsItsContractReference(string run, string records)
    {
        var source = "contract Mapper<T>\n    func map<F>(self: uniq/Self, transform: ref/F) -> i32\n        F is Callable<(T) -> i32>\n            effect confined\n" +
            "group M\n    public func run" + run;
        var actual = DiagnosticCorpus.Check(source, "Library").Diagnostics.Select(x => $"{x.Code}@{source.Substring(x.Span!.Value.Start, x.Span.Value.Length)}");
        Assert.Equal(records, string.Join(" | ", actual.DefaultIfEmpty("clean")));
    }

    [Theory]
    [InlineData("contract C<F>\n    F is Callable<() -> i32>\n        effect confined\n")]
    [InlineData("contract C\n    associate F is Callable<() -> i32>\n        effect confined\n")]
    public void RejectsExcludedClausePositions(string declaration)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(declaration + "public func main() => ()\n").Diagnostics);
        Assert.Equal("InvalidEffectBound_Kd", record.Code);
        Assert.Contains("position", record.Label);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnInvalidImplementationBoundAddsNoCompatibilityObligation(bool invalid, bool independent)
    {
        const string Header = "contract Runner\n    func run<F>(self: ref/Self, callback: F) -> i32\n        F is Callable<owner, () -> i32>\n" +
            "struct Worker\n    Self is Runner\n    public func run<F>(self: ref/Self, callback: F) -> i32\n        F is Callable<owner, () -> i32>\n";
        var source = Header + (invalid ? "            effect Runner.run confined\n" : string.Empty) +
            "        return callback@move()\npublic func main() => ()\n" + (independent ? "func broken() -> i32 => true\n" : string.Empty);
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal((invalid ? 1 : 0) + (independent ? 1 : 0), records.Length);
        if (invalid)
        {
            var bound = Assert.Single(records, static record => record.Code == "InvalidEffectBound_Kd");
            Assert.Equal("effect Runner.run confined", source.Substring(bound.Span!.Value.Start, bound.Span.Value.Length));
            Assert.Equal("an effect specification outside a Contract's items", bound.Label);
        }

        if (independent)
        {
            Assert.Single(records, static record => record.Code == "TypeMismatch_Kd");
        }
    }

    [Theory]
    [InlineData("apply<G>", true)]
    [InlineData("apply", true)]
    [InlineData("apply<G>", false)]
    [InlineData("apply", false)]
    public void GenericReferencesDischargeBoundsEvenWhenNeverCalled(string reference, bool bounded)
    {
        var source = Apply + "func reference<G>() -> ()\n    G is Callable<() -> i32>\n" +
            (bounded ? "        effect confined\n" : string.Empty) +
            $"    G is Owned\n    let f: (ref/G) -> i32 = {reference}\npublic func main() => ()\n";
        if (bounded)
        {
            Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        }
        else
        {
            // The selected use: the Type argument of `apply<G>`, or the reference `apply` that infers it.
            var at = MirTargetSide.At($"= {reference}\n", reference.EndsWith('>') ? "G" : reference);
            AssertA4Record(source, DiagnosticCode.UnsatisfiedEffectBound_Kd, at);
        }
    }

    [Theory]
    [InlineData("struct", true)]
    [InlineData("enum", true)]
    [InlineData("struct", false)]
    [InlineData("enum", false)]
    public void TypeFormationsRequireTheEnclosingPremise(string kind, bool bounded)
    {
        var source = $"{kind} Holder<F>\n    F is Callable<() -> i32>\n        effect confined\n" +
            (kind == "enum" ? "    None\n" : string.Empty) +
            "func use<G>(value: ref/Holder<G>) -> ()\n    G is Callable<() -> i32>\n" +
            (bounded ? "        effect confined\n" : string.Empty) + "    return\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (bounded)
        {
            Assert.Empty(records);
        }
        else
        {
            Assert.NotEmpty(records);
            Assert.All(records, static d => Assert.Equal("UnsatisfiedEffectBound_Kd", d.Code));
        }
    }

    [Theory]
    [InlineData("42", true)]
    [InlineData("Helpers.noisy()", false)]
    public void SummariesComposeCallbacksThroughActualArguments(string expression, bool valid)
    {
        var source = "contract Work\n    func run(self: ref/Self) -> i32\n        effect confined\n" +
            "group Helpers\n    public func invoke<F>(f: ref/F) -> i32\n        F is Callable<() -> i32>\n        return f()\n" +
            "    public func noisy() -> i32\n        Console.writeLine(\"noisy\")\n        return 0\n" +
            $"struct Worker\n    Self is Work\n    public func run(self: ref/Self) -> i32 => Helpers.invoke(func [] () -> i32 => {expression})\npublic func main() => ()\n";
        if (valid)
        {
            Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        }
        else
        {
            AssertA4Record(source, DiagnosticCode.IncompatibleContractImplementation_Kd, MirTargetSide.At("Self is Work"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConfinedImplementationCanUseABoundedCallback(bool bounded)
    {
        var bound = bounded ? "            effect confined\n" : string.Empty;
        var source = "contract Work\n    func run<F>(self: ref/Self, f: ref/F) -> i32\n        F is Callable<() -> i32>\n" + bound +
            "        effect confined\nstruct Worker\n    Self is Work\n    public func run<F>(self: ref/Self, f: ref/F) -> i32\n        F is Callable<() -> i32>\n" + bound +
            "        return f()\npublic func main() => ()\n";
        if (bounded)
        {
            Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        }
        else
        {
            AssertA4Record(source, DiagnosticCode.IncompatibleContractImplementation_Kd, MirTargetSide.At("Self is Work"));
        }
    }

    // The bounded overload stays selected: its bound's record is at the argument.
    [Fact]
    public void FailedSelectedBoundsNeverChooseTheMoreGeneralOverload()
    {
        const string Source = "func choose<F>(f: ref/F, value: i32) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\n" +
            "func choose<F>(f: ref/F, value: i32, spare: i32 = 0) -> i32\n    F is Callable<() -> i32>\n    return 0\n" +
            "func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\npublic func main() => _ = choose(noisy, 1)\n";
        AssertA4Record(Source, DiagnosticCode.UnsatisfiedEffectBound_Kd, MirTargetSide.At("choose(noisy, 1)", "noisy"));
    }

    [Fact]
    public void ConditionalConformanceAllowsAnUnboundedCallablePremise()
        => Assert.Empty(DiagnosticCorpus.Check("contract C\nstruct S<F>\n    Self is C when F is Callable<() -> i32>\npublic func main() => ()\n").Diagnostics);

    [Fact]
    public void AnonymousAndRecursiveCallablesReachTheFixedPoint()
    {
        var source = Apply + "func recursive() -> i32\n    if false => return recursive()\n    return 9\n" +
            "public func main()\n    _ = apply(func [] () -> i32 => 8)\n    _ = apply(recursive)\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRejectedBoundChecksReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Apply + "func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\npublic func main() => _ = apply(noisy)\n");
        Assert.False(c.Analysis.IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Analyze()));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBoundChecksReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Apply + "public func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32 => 12\n    return apply(f@ref)\n");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // SPEC 8.11.4, 8.11.3 (the adopted proposal): nothing relates two calls, so the same, a replaced or another Callable value makes
    // no difference (R1g's Unsupported_Kd and CallEffectConflict_Kd before the adoption).
    [Theory]
    [InlineData(SameCallable)]
    [InlineData(ReplacedCallable)]
    [InlineData(OtherCallable)]
    public void ReplacingTheCallableChangesNoVerdict(string second)
    {
        var source = "contract Source\n    associate Item\n" + Replacing(second) + "public func main() => ()\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ConfinedCallbacksRunNatively()
        => ScalarEmissionTest.EmitFixture("CallableEffectsConfined", Apply + "let f = func [] () -> i32 => 19\nrequire apply(f@ref) == 19 else => $abort(\"callback\")\n", string.Empty);

    // A generic function that takes a first result through a Callable value, then makes a second call.
    internal static string Replacing(string second)
        => "func twice<S,F>(source: uniq/S, one: F, two: F) -> (S.Item, S.Item)\n    S is Source\n    F is Callable<uniq, (uniq/S) -> S.Item>\n    var f = one@move\n    let first = f(source)\n" +
            second + "    return (first@move, second@move)\n";

    // R6-check H3b T41: since the MIR decides acceptance (H5, DECISIONS R5-M185), A4 judges a selected Callable's or an implementation's
    // effect bound where Binding did, and check publishes its one record at the obligation's subject: the selected use, or the conformance's
    // `Self is C` clause (docs/dev/DIAGNOSTICS.md, Callable effect bounds). It has no Label, Note or related location until A4 publishes its
    // facts (H0a 4.1's Effect* rows).
    private static CheckOutput AssertA4Record(string source, DiagnosticCode code, SyntaxLocation at)
    {
        var check = DiagnosticCorpus.Check(source);
        var record = Assert.Single(check.Diagnostics);
        Assert.Equal((code.ToString(), at.Resolve(source)), (record.Code, record.Span!.Value));
        Assert.Null(record.Label);
        Assert.Null(record.Note);
        Assert.Null(record.Related);
        return check;
    }
}
