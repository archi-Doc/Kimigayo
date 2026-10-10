// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 8.6: normalize the whole contract set, validate its shape, then select by ordinary argument ranking.
public class CallableSignatureSelectionTest
{
    private const string Main = "public func main() -> ()\n    Console.writeLine(\"done\")\n";
    private const string Forward = "func both<T, F>(action: ref/F, value: ref/T) -> i32\n    F is Callable<(ref/T) -> i32>\n    F is Callable<(ref/i32) -> i32>\n    return action(value)\nfunc read(value: ref/i32) -> i32 => value@follow\nlet n: i32 = 42\nrequire both(read, n@ref) == 42 else => $abort(\"selected\")";

    [Theory]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return action(1)\n", "action(1)", DiagnosticCode.AmbiguousBinding_Kd)]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return action(true)\n", "action(true)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func both<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> ()>\n    F is Callable<(ref/i32) -> ()>\n    let n: i32 = 1\n    action(n)\n", "action(n)", DiagnosticCode.ParameterShapeMismatch_Kd)]
    [InlineData("func both<F>(action: uniq/F) -> ()\n    F is Callable<ref, (i32) -> ()>\n    F is Callable<uniq, (i64) -> ()>\n    action(1@i32)\n", "action(1@i32)", DiagnosticCode.ReceiverShapeMismatch_Kd)]
    [InlineData("func generic<T, F>(action: ref/F, value: T) -> T\n    F is Callable<(T) -> T>\n    F is Callable<(i32) -> i32>\n    return action(value)\n", "action(value)", DiagnosticCode.ParameterShapeMismatch_Kd)]
    public void InvalidCallsExplainTheSelectionFailure(string body, string text, DiagnosticCode code)
    {
        var source = body + Main;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((code.ToString(), DiagnosticCategory.Language, text), (error.Code, error.Category, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal(2, error.Related!.Length);
        Assert.False(string.IsNullOrWhiteSpace(error.Note));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedArgumentsSelectIndependentlyOfConstraintOrder(bool reversed)
    {
        var clauses = new[] { "    F is Callable<(i32) -> i32>\n", "    F is Callable<(i64) -> i64>\n" };
        var source = "func both<F>(action: ref/F) -> i32\n" + clauses[reversed ? 1 : 0] + clauses[reversed ? 0 : 1] + "    return action(1@i32)\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ExpectedResultsExcludeButDoNotRankCandidates()
        => Assert.Empty(DiagnosticCorpus.Check("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return action(1)\n" + Main).Diagnostics);

    [Fact]
    public void SelectedContractsExecuteAfterGenericSubstitution()
        => ScalarEmissionTest.EmitFixture("CallableSelectionForward", Forward, string.Empty);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdentityAndPerCallBindersNormalizeBeforeReceiverMerging(bool reversed)
    {
        var a = "    F is Callable<uniq, (ref/T) -> ref/T>\n";
        var b = "    F is Callable<(ref/i32) -> ref/i32>\n";
        var source = "func same<T, F>(f: ref/F, n: ref/i32) -> ref/i32\n    T is i32\n" + (reversed ? b + a : a + b) + "    return f(n)\nfunc identity(n: ref/i32) -> ref/i32 => n\nlet n: i32 = 42\nlet f: (ref/i32) -> ref/i32 = identity\nrequire same<i32, (ref/i32) -> ref/i32>(f, n@ref)@follow == 42 else => $abort(\"identity\")";
        ScalarEmissionTest.EmitFixture("CallableSelectionIdentity" + reversed, source, string.Empty);
    }

    [Theory]
    [InlineData("((i32, bool)) -> i32", "((i64, bool)) -> i64", "(1@i32, true)")]
    [InlineData("([2 of i32]) -> i32", "([3 of i32]) -> i32", "[1, 2]")]
    [InlineData("(i32?) -> i32", "(i64?) -> i64", ".Some(1@i32)")]
    public void ContextualInputsAreProbedWithoutCommittingAnAlternative(string first, string second, string argument)
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<" + first + ">\n    F is Callable<" + second + ">\n    return f(" + argument + ")\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ATypeMustSatisfyEveryCallableConstraint()
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return f(1@i32)\nfunc identity(n: i32) -> i32 => n\nlet n = both(identity)";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        Assert.Equal("both(identity)", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Fact]
    public void TheSelectedSignatureSuppliesAnAnonymousArgumentsContextOnce()
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<((i32) -> i32, i32) -> i32>\n    F is Callable<((i64) -> i64, i64) -> i32>\n    return f(func [] (n) => n, 1@i32)\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ResultsWithoutAnExpectationNeverRank()
    {
        var source = "func both<F>(f: ref/F)\n    F is Callable<(i32) -> i32>\n    F is Callable<(i32) -> i64>\n    _ = f(1@i32)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), error.Code);
    }

    [Fact]
    public void ASelectedAnonymousBodyFailureDoesNotRetryAnotherSignature()
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<((i32) -> i32, i32) -> i32>\n    F is Callable<((i64) -> i64, i64) -> i32>\n    return f(func [] (n) => \"bad\", 1@i32)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TypeMismatch_Kd), error.Code);
        Assert.Equal("\"bad\"", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("i64", false)]
    public void OnlyTheSelectedContractsBoundsApply(string type, bool confined)
    {
        var source = "func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n        effect confined\n    F is Callable<(i64) -> i32>\n    return f(1@" + type + ")\n" + Main;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => x.BoundValueCall is not null);
        var plan = call.BoundValueCall!;
        Assert.Equal(confined, c.Binding.AvailableCallableEffects(plan.CalleeType.Components[0], plan.DeclaredSignature, plan.ReceiverKind, call).Confined);
    }

    [Fact]
    public void NormalizedSignatureEffectsRemainAvailable()
    {
        const string Source = "func bounded<G>(g: ref/G, n: ref/i32) -> ref/i32\n    G is Callable<(ref/i32) -> ref/i32>\n        effect confined\n    return g(n)\nfunc forward<T, F>(f: ref/F, n: ref/i32) -> ref/i32\n    T is i32\n    F is Callable<(ref/T) -> ref/T>\n        effect confined\n    F is Callable<uniq, (ref/i32) -> ref/i32>\n    return bounded(f, n)\n";
        Assert.Empty(DiagnosticCorpus.Check(Source + Main).Diagnostics);
    }

    [Fact]
    public void AChangedContractInvalidatesThePreviousSelection()
    {
        var c = MinimalEmissionTest.Analyze("func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return f(1)\n" + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var clause = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IsKoto>().Last();
        var donor = MinimalEmissionTest.Analyze("func other<F>(f: ref/F)\n    F is Callable<(i64) -> i32>\n    return\n" + Main);
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<IsKoto>().Last();
        Assert.True(KotoHelper.Replace(clause.Parent!, clause, replacement));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmSelectionReusesCandidateAndOperationStorage()
    {
        var c = MinimalEmissionTest.Analyze(Forward);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void RejectedSelectionReusesItsDiagnosticFacts()
    {
        var c = MinimalEmissionTest.Analyze("func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return f(1)\n" + Main);
        Assert.False(c.Binding.Result.IsComplete);
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete));
        Assert.True(rejected);
        Assert.Single(c.Binding.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguityNamesTheAvailableContractsInCliAndLsp(bool checkBound)
    {
        const string Source = "func both<F>(f: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i32>\n    return f(1)\n" + Main;
        var path = Path.GetFullPath("CallableSelection.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        if (checkBound)
        {
            Assert.False(c.Binding.CheckBound().IsComplete);
        }

        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), record.Code);
        Assert.Equal(2, record.Related!.Length);
        Assert.All(record.Related!, static x => Assert.Equal("candidate", x.Role));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Callable<ref, (i32) -> i32>", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    // One signature with a shared and an exclusive receiver is one candidate, and forwarding several signatures makes no call.
    [Theory]
    [InlineData("func one<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<uniq, (i32) -> i32>\n    return action(1)\nfunc inc(value: i32) -> i32 => value + 1\npublic func main() -> ()\n    require one(inc) == 2 else => $abort(\"b\")\n    Console.writeLine(\"done\")\n")]
    [InlineData("func both<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return ()\nfunc outer<G>(action: ref/G) -> ()\n    G is Callable<(i32) -> i32>\n    G is Callable<(i64) -> i64>\n    both(action)\n" + Main)]
    public void OneSignatureOrNoCallIsAccepted(string source)
        => Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
}
