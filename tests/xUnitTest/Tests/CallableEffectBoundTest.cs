// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 8.4.10.7: bounds constrain selected uses, never overload applicability.
public class CallableEffectBoundTest
{
    private const string Apply = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\n";

    [Fact]
    public void AcceptsAConfinedClosure()
    {
        var c = MinimalEmissionTest.Analyze(Apply + "public func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32 => 12\n    return apply(f@ref)\n");
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ReportsTheViolatingCallableAfterSelection()
    {
        var c = MinimalEmissionTest.Analyze(Apply + "group State\n    public var count: i32 = 0\npublic func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32 => State.count\n    return apply(f@ref)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Analyze().IsVerified);
        c.Ownership.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Code == "UnsatisfiedEffectBound_Kd");
        Assert.Equal("UnsatisfiedEffectBound_Kd", error.Code);
        Assert.Equal("f@ref", error.Text);
        Assert.Contains("confined", error.Label);
        Assert.Contains("mutable static", error.Label);
    }

    [Theory]
    [InlineData("Copy", "F is Copy", "effect confined", "single Callable")]
    [InlineData("Grouped", "F is (Callable<() -> i32>)", "effect confined", "single Callable")]
    [InlineData("Combined", "F is Callable<() -> i32> and Copy", "effect confined", "single Callable")]
    [InlineData("Duplicate", "F is Callable<() -> i32>", "effect confined\n        effect confined", "already declares confined")]
    [InlineData("Owner", "F is Callable<owner, () -> i32>", "effect preserves results", "ref or uniq")]
    public void InvalidDeclarationsExplainTheirCause(string name, string constraint, string bounds, string cause)
    {
        var source = $"func check<F>(f: F) -> ()\n    {constraint}\n        {bounds}\n    return\npublic func main() => ()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("InvalidEffectBound_Kd", record.Code);
        Assert.Contains(cause, record.Label);
        Assert.NotEmpty(record.Related!);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForwardingUsesOnlyDeclaredPremises(bool bounded)
    {
        var source = Apply + "func forward<G>(g: ref/G) -> i32\n    G is Callable<() -> i32>\n" +
            (bounded ? "        effect confined\n" : string.Empty) + "    return apply(g)\npublic func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32 => 8\n    return forward(f@ref)\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(bounded, c.Ownership.Result.IsVerified);
        if (!bounded)
        {
            var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
            Assert.Equal("UnsatisfiedEffectBound_Kd", record.Code);
            Assert.Contains("premise", record.Label);
        }
    }

    [Fact]
    public void ErasureKeepsNoBound()
    {
        var source = Apply + "public func main() => ()\nfunc entry() -> i32\n    let f: () -> i32 = func [] () -> i32 => 5\n    return apply(f@ref)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("UnsatisfiedEffectBound_Kd", error.Code);
        Assert.Contains("erasure", error.Label);
    }

    [Theory]
    [InlineData("State.count", false)]
    [InlineData("42", true)]
    public void FunctionItemsUseTheirTransitiveEffects(string body, bool valid)
    {
        var source = Apply + $"group State\n    public var count: i32 = 0\nfunc inner() -> i32 => {body}\nfunc outer() -> i32 => inner()\npublic func main() => ()\nfunc entry() -> i32 => apply(outer)\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
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
        var check = DiagnosticCorpus.Check(Apply + "func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\npublic func main() => ()\nfunc entry() -> i32 => apply(noisy)\n");
        var record = Assert.Single(check.Diagnostics);
        Assert.Equal("UnsatisfiedEffectBound_Kd", record.Code);
        Assert.Equal(["bound", "effect"], record.Related!.Select(static r => r.Role));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        Assert.Contains("confined", console.Text);
        Assert.Contains("external operation", console.Text);
        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var lsp = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal(record.Code, lsp.Code);
            Assert.Contains("confined", lsp.Message);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EarlierResultsRequirePreservation(bool bounded, bool valid)
    {
        var source = "contract Source\n    associate Item\nfunc twice<S,F>(source: uniq/S, next: ref/F) -> (S.Item, S.Item)\n    S is Source\n    F is Callable<(uniq/S) -> S.Item>\n" +
            (bounded ? "        effect preserves results\n" : string.Empty) +
            "    let first = next(source)\n    let second = next(source)\n    return (first@move, second@move)\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
        if (!valid)
        {
            var diagnostic = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
            Assert.Equal("CallEffectConflict_Kd", diagnostic.Code);
            Assert.Contains("Callable Constraint", diagnostic.Message);
            Assert.Equal(["call", "loan"], diagnostic.Related!.Select(static r => r.Role));
        }
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
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

    [Theory]
    [InlineData("contract C<F>\n    F is Callable<() -> i32>\n        effect confined\n")]
    [InlineData("contract C\n    associate F is Callable<() -> i32>\n        effect confined\n")]
    [InlineData("contract C\nstruct S<F>\n    Self is C when F is Callable<() -> i32>\n        effect confined\n")]
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
        var source = Header + (invalid ? "            effect preserves results\n" : string.Empty) +
            "        return callback@move()\npublic func main() => ()\n" + (independent ? "func broken() -> i32 => true\n" : string.Empty);
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal((invalid ? 1 : 0) + (independent ? 1 : 0), records.Length);
        if (invalid)
        {
            var bound = Assert.Single(records, static record => record.Code == "InvalidEffectBound_Kd");
            Assert.Contains("ref or uniq", bound.Label);
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
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (bounded)
        {
            Assert.Empty(records);
        }
        else
        {
            var diagnostic = Assert.Single(records);
            Assert.Equal("UnsatisfiedEffectBound_Kd", diagnostic.Code);
            Assert.Contains("premise", diagnostic.Label);
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
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (valid)
        {
            Assert.Empty(records);
        }
        else
        {
            var diagnostic = Assert.Single(records);
            Assert.Equal("IncompatibleContractImplementation_Kd", diagnostic.Code);
            Assert.Contains("external operation", diagnostic.Label);
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
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (bounded)
        {
            Assert.Empty(records);
        }
        else
        {
            var diagnostic = Assert.Single(records);
            Assert.Equal("IncompatibleContractImplementation_Kd", diagnostic.Code);
            Assert.Contains("unknown effects", diagnostic.Label);
        }
    }

    [Fact]
    public void FailedSelectedBoundsNeverChooseTheMoreGeneralOverload()
    {
        const string Source = "func choose<F>(f: ref/F, value: i32) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\n" +
            "func choose<F>(f: ref/F, value: i32, spare: i32 = 0) -> i32\n    F is Callable<() -> i32>\n    return 0\n" +
            "func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\npublic func main() => _ = choose(noisy, 1)\n";
        var diagnostic = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal("UnsatisfiedEffectBound_Kd", diagnostic.Code);
        Assert.Contains("never selects another overload", diagnostic.Note);
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

    [Theory]
    [InlineData("ref", false, true)]
    [InlineData("uniq", true, false)]
    public void PreservingBindingsCheckEffectsAgainstResultLoans(string acquisition, bool write, bool valid)
    {
        var source = $"func identity(value: {acquisition}/i32) -> {acquisition}/i32\n" + (write ? "    value@follow += 1\n" : string.Empty) +
            $"    return value\nfunc accept<F>(f: ref/F) -> ()\n    F is Callable<({acquisition}/i32) -> {acquisition}/i32>\n        effect preserves results\n    return\npublic func main() => accept(identity)\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (valid)
        {
            Assert.Empty(records);
        }
        else
        {
            var diagnostic = Assert.Single(records);
            Assert.Equal("UnsatisfiedEffectBound_Kd", diagnostic.Code);
            Assert.Contains("preserves results", diagnostic.Label);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRejectedBoundChecksReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Apply + "func noisy() -> i32\n    Console.writeLine(\"noisy\")\n    return 0\npublic func main() => _ = apply(noisy)\n");
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Ownership.Analyze()));
    }

    [Theory]
    [InlineData("first", true)]
    [InlineData("second", false)]
    public void PreservationDelegationRequiresOneCallableValue(string other, bool valid)
    {
        var source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n        effect preserves results\n" +
            "struct Pair<F,I>\n    I is Iterator\n    F is Callable<uniq, () -> Option<I.Item>>\n        effect preserves results\n    Self is Source\n    associate Source.Item is I.Item\n    var first: F\n    var second: F\n" +
            $"    public func take(self: uniq/Self) -> Option<I.Item>\n        if true => return self.first@uniq()\n        return self.{other}@uniq()\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (valid)
        {
            Assert.Empty(records);
        }
        else
        {
            var diagnostic = Assert.Single(records);
            Assert.Equal("IncompatibleContractImplementation_Kd", diagnostic.Code);
            Assert.Contains("another value", diagnostic.Note);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBoundChecksReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Apply + "public func main() => ()\nfunc entry() -> i32\n    let f = func [] () -> i32 => 12\n    return apply(f@ref)\n");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacingTheCallableCannotReuseTheEarlierValuesGuarantee(bool replace)
    {
        var source = "contract Source\n    associate Item\nfunc twice<S,F>(source: uniq/S, one: F, two: F) -> (S.Item, S.Item)\n    S is Source\n    F is Callable<uniq, (uniq/S) -> S.Item>\n        effect preserves results\n    var f = one@move\n    let first = f(source)\n" +
            (replace ? "    f = two@move\n" : string.Empty) +
            "    let second = f(source)\n    return (first@move, second@move)\npublic func main() => ()\n";
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        if (!replace)
        {
            Assert.Empty(records);
        }
        else
        {
            Assert.Equal("CallEffectConflict_Kd", Assert.Single(records).Code);
        }
    }

    [Fact]
    public void ConfinedCallbacksRunNatively()
        => ScalarEmissionTest.EmitFixture("CallableEffectsConfined", Apply + "let f = func [] () -> i32 => 19\nrequire apply(f@ref) == 19 else => $abort(\"callback\")\n", string.Empty);
}
