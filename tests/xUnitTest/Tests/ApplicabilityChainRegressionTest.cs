// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Verification;
using Xunit;

namespace XunitTest;

// PLAN G76: the blocking review cases, with expectations from SPEC 10.4, 10.5, 10.7 and 23.3.6.4.
public class ApplicabilityChainRegressionTest
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ResultOnlyItemOriginsFitFixedEvidence(bool callable, bool reversed, bool stored)
    {
        var parameters = callable ? "f: ref/F" : "f: () -> T";
        parameters = reversed ? "other: T, " + parameters : parameters + ", other: T";
        var declaration = callable
            ? $"func make<T, F>({parameters}) -> T\n    F is Callable<() -> T>\n    return other@move\n"
            : $"func make<T>({parameters}) -> T => other@move\n";
        var item = stored ? "c" : "constant";
        var arguments = reversed ? $"local@ref, {item}" : $"{item}, local@ref";
        var source = "func constant() -> ref/i32 during s => $abort(\"never\")\n" + declaration +
            "let local: i32 = 7\n" + (stored ? "let c = constant\n" : string.Empty) +
            $"let r = make({arguments})\nrequire r@follow == 7 else => $abort(\"fixed result\")";
        ScalarEmissionTest.EmitFixture($"ApplicabilityChainResult{callable}{reversed}{stored}", source, string.Empty);
    }

    [Theory]
    [InlineData("Forward", "func callIt(p: ref/i32, f: () -> ref/i32 during p) -> i32 => 2\nfunc g(p: ref/i32) -> i32 => callIt(p, constant)\n")]
    [InlineData("Reverse", "func callIt(f: () -> ref/i32 during p, p: ref/i32) -> i32 => 2\nfunc g(p: ref/i32) -> i32 => callIt(constant, p)\n")]
    [InlineData("Enclosing", "func make<T>(f: () -> T, other: T) -> T => other@move\nfunc g(p: ref/i32) -> i32 => make(constant, p)@follow\n")]
    [InlineData("Static", "func callIt(f: () -> ref/i32 during static) -> i32 => 2\nfunc g(p: ref/i32) -> i32 => callIt(constant)\n")]
    public void ResultOnlyOriginsFitPublishedParameters(string name, string declarations)
        => ScalarEmissionTest.EmitFixture("ApplicabilityChainPublished" + name, "func constant() -> ref/i32 during s => $abort(\"never\")\n" + declarations + "let local: i32 = 2\nrequire g(local@ref) == 2 else => $abort(\"published result\")", string.Empty);

    [Theory]
    [InlineData("Direct", "func keep(o: Option<i32>) -> i32 => 1\nrequire keep(make()) == 1 else => $abort(\"nested\")")]
    [InlineData("Local", "func keep(o: Option<i32>) -> i32 => 1\nlet k = keep(make())\nrequire k == 1 else => $abort(\"nested\")")]
    [InlineData("Parenthesized", "func keep(o: Option<i32>) -> i32 => 1\nrequire keep((make())) == 1 else => $abort(\"nested\")")]
    [InlineData("Ranked", "func keep(o: Option<i32>, tag: i32) -> i32 => 1\nfunc keep(o: Option<i64>, tag: i64) -> i32 => 2\nrequire keep(make(), 1@i32) == 1 else => $abort(\"nested\")")]
    [InlineData("Named", "func keep(tag: i32, o: Option<i32>) -> i32 => 1\nrequire keep(o: make(), tag: 1) == 1 else => $abort(\"nested\")")]
    [InlineData("Constructor", "struct K\n    public let o: Option<i32>\n    public init(o: Option<i32>) => self.o = o\nlet k = K.init(make())\nmatch k.o\n    .None => ()\n    .Some(let n) => $abort(\"nested\")")]
    [InlineData("Generic", "func keep<T>(o: Option<T>, value: T) -> T => value@move\nrequire keep(make(), 3@i64) == 3 else => $abort(\"nested\")")]
    [InlineData("Deep", "func also<T>(o: Option<T>) -> Option<T> => o@move\nfunc keep(o: Option<i64>) -> i32 => 1\nrequire keep(also(make())) == 1 else => $abort(\"nested\")")]
    [InlineData("Value", "func keep(o: Option<i64>) -> i32 => 1\nlet k: (Option<i64>) -> i32 = keep\nrequire k(make()) == 1 else => $abort(\"nested\")")]
    [InlineData("OverloadedInner", "func inner(x: i32) -> Option<i32> => .Some(x)\nfunc inner(x: i64) -> Option<i64> => .Some(x)\nfunc keep(o: Option<i64>) -> i32 => 1\nrequire keep(inner(1)) == 1 else => $abort(\"nested\")")]
    public void ADeterminedOuterCandidateSuppliesTheNestedExpectation(string name, string body)
        => ScalarEmissionTest.EmitFixture("ApplicabilityChainNested" + name, "func make<T>() -> Option<T> => .None\n" + body, string.Empty);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AFailedCandidateSignatureExplainsTheDependentCall(bool reversed, bool differentTag)
    {
        const string Broken = "func f(x: Option<ref/i32>, y: i32) -> i32 => 1\n";
        var valid = $"func f(x: Option<ref/i32 during static>, y: {(differentTag ? "i64" : "i32")}) -> i32 => 3\n";
        var source = (reversed ? valid + Broken : Broken + valid) +
            "func g(p: ref/i32) -> i32 => f(.Some(p), 1)\npublic func main() -> () => ()\n";
        var result = DiagnosticCorpus.Check(source);
        Assert.False(result.Accepted);
        Assert.Equal(nameof(DiagnosticCode.MissingOriginBinding_Kd), Assert.Single(result.Diagnostics).Code);
        var repaired = source.Replace(Broken, string.Empty, StringComparison.Ordinal).Replace("g(p: ref/i32)", "g(p: ref/i32 during static)", StringComparison.Ordinal);
        Assert.True(DiagnosticCorpus.Check(repaired).Accepted);
    }

    [Fact]
    public void APerCallResultDoesNotMakeTheGenericCandidateBetter()
        => ScalarEmissionTest.EmitFixture("ApplicabilityChainRank", "func make<T>(action: (ref/i32) -> T) -> i32 => 1\nfunc make(action: (ref/i32) -> ref/i32) -> i32 => 2\nfunc pick(n: ref/i32) -> ref/i32 during n => n\nrequire make(pick) == 2 else => $abort(\"rank\")", string.Empty);

    [Fact]
    public void EqualGenericStructuresRemainAmbiguous()
    {
        const string Source = "func make<T>(action: (ref/i32) -> T) -> i32 => 1\nfunc make<U>(action: (ref/i32) -> ref/U) -> i32 => 2\n" +
            "func pick(n: ref/i32) -> ref/i32 during n => n\npublic func main() -> ()\n    let r = make(pick)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), error.Code);
        Assert.Equal("make(pick)", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(2, error.Related!.Length);
    }

    // SPEC 10.5: no Cartesian search, no evidence from a waiting result, and no fallback after the chosen expectation fails.
    [Theory]
    [InlineData("func make<T>() -> Option<T> => .None\nlet value = make()", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "make()")]
    [InlineData("func make<T>() -> Option<T> => .None\nfunc keep<T>(o: Option<T>) => ()\nkeep(make())", nameof(DiagnosticCode.UnboundTypeArgument_Kd), "keep(make())")]
    [InlineData("func make<T>() -> Option<T> => .None\nfunc keep(o: Option<i32>) => ()\nfunc keep(o: Option<i64>) => ()\nkeep(make())", nameof(DiagnosticCode.AmbiguousBinding_Kd), "keep(make())")]
    [InlineData("func make<T>() -> Option<T>\n    T is PrimitiveInteger\n    return .None\nfunc keep(o: Option<string>, tag: i32) => ()\nfunc keep(o: Option<i32>, tag: i64) => ()\nkeep(make(), 1@i32)", nameof(DiagnosticCode.NoApplicableOverload_Kd), "make()")]
    [InlineData("func make<T>(value: i32) -> Option<T> => .None\nfunc keep(o: Option<i32>) => ()\nkeep(make(missing))", nameof(DiagnosticCode.UnresolvedBinding_Kd), "missing")]
    public void NestedInferenceHasOneBoundary(string source, string code, string at)
    {
        var check = DiagnosticCorpus.Check(source);
        Assert.False(check.Accepted);
        var error = Assert.Single(check.Diagnostics);
        Assert.Equal((code, at), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
        if (code == nameof(DiagnosticCode.UnboundTypeArgument_Kd))
        {
            Assert.Equal("Generic parameter 'T' is not bound", error.Label);
            Assert.Contains("No explicit Type argument or evidence binds T", error.Note, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AResultOnlyOriginKeepsItsInvariantOccurrencesCorrelated()
    {
        const string Source = "func constant() -> (uniq/(ref/i32 during s) during a, uniq/(ref/i32 during s) during b) => $abort(\"never\")\n" +
            "func reject(x: ref/i32, y: ref/i32) -> ()\n    let c = constant\n    let f: () -> (uniq/(ref/i32 during x) during x, uniq/(ref/i32 during y) during y) = c\npublic func main() -> () => ()";
        var rejected = DiagnosticCorpus.Check(Source);
        Assert.False(rejected.Accepted);
        var error = Assert.Single(rejected.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnprovenOriginContract_Kd), error.Code);
        Assert.Equal("the result requires x == y, which is not proven", error.Label);
        Assert.Equal("Use equal Origin bindings at the compared positions, or convert an implementation whose bindings match the required Type", error.Advice);
        Assert.True(DiagnosticCorpus.Check(Source.Replace("during y", "during x", StringComparison.Ordinal)).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANamedUniversalIsInstantiatedAtTheRequiredInput(bool callable)
    {
        var declaration = callable ? "func run<T, F>(value: T, f: ref/F) -> T\n    F is Callable<(T) -> T>\n    return f(value@move)\n"
            : "func run<T>(value: T, f: (T) -> T) -> T => f(value@move)\n";
        ScalarEmissionTest.EmitFixture("ApplicabilityChainNamed" + callable, "func pick(n: ref/i32 during a) -> ref/i32 during a => n\n" + declaration + "let local: i32 = 7\nrequire run(local@ref, pick)@follow == 7 else => $abort(\"named\")", string.Empty);
    }

    [Fact]
    public void ANestedExpectationCanBindTheWholeResultSlot()
        => Assert.True(DiagnosticCorpus.Check("func make<T>() -> T => $abort(\"never\")\nfunc keep(n: i64) -> i64 => n\nfunc caller() -> i64 => keep(make())\npublic func main() -> () => ()").Accepted);

    [Fact]
    public void TheFailedDeclarationRecordReachesEveryOutputAndKeepsIndependentErrors()
    {
        const string Source = "func f(x: Option<ref/i32>, y: i32) -> i32 => 1\nfunc f(x: Option<ref/i32 during static>, y: i32) -> i32 => 3\n" +
            "func g(p: ref/i32) -> i32 => f(.Some(p), 1)\npublic func main() -> ()\n    let x = missing";
        var check = DiagnosticCorpus.Check(Source);
        Assert.Equal([nameof(DiagnosticCode.MissingOriginBinding_Kd), nameof(DiagnosticCode.UnresolvedBinding_Kd)], check.Diagnostics.Select(x => x.Code));
        var declaration = check.Diagnostics[0];
        Assert.Equal("ref/i32", Source.Substring(declaration.Span!.Value.Start, declaration.Span.Value.Length));
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("MissingOriginBinding_Kd", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("UnprovenConstraint_Kd", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(check.Sources[declaration.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(check, [identity], identity, related)[identity];
            Assert.Equal(check.Diagnostics.Select(x => (x.Code, x.Display!.Range!.Value)), sent.Select(x => (x.Code, x.Range)));
        }

        var json = System.Text.Json.JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Equal(result, System.Text.Json.JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNestedInferenceAndUniversalErasureAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(VerificationWorkloads.NestedInferenceAndUniversalErasure);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
