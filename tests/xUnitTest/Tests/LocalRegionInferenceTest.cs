// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class LocalRegionInferenceTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Reassign", "let first = 0\nvar view = first@ref\nlet second = 5\nview = second@ref\nrequire view@follow == 5 else => $abort(\"view\")")]
    [InlineData("BothUses", "let first = 0\nvar view = first@ref\nrequire view@follow == 0 else => $abort(\"before\")\nlet second = 5\nview = second@ref\nrequire view@follow == 5 else => $abort(\"after\")")]
    [InlineData("EndedLoan", "var first = 0\nvar view = first@ref\nlet second = 5\nview = second@ref\nfirst = 9\nrequire view@follow == 5 and first == 9 else => $abort(\"ended\")")]
    [InlineData("Annotation", "let first = 0\nvar view: ref/i32 = first@ref\nlet second = 5\nview = second@ref\nrequire view@follow == 5 else => $abort(\"view\")")]
    [InlineData("Inputs", "func pick(a: ref/i32, b: ref/i32) -> ref/i32 during (a and b)\n    var view = a\n    view = b\n    return view\nlet first = 1\nlet second = 5\nrequire pick(first@ref, second@ref)@follow == 5 else => $abort(\"inputs\")")]
    public void UsesOneLocalTypeAndTheCurrentValue(string name, string source)
        => ScalarEmissionTest.EmitFixture("LocalRegion" + name, source, string.Empty);

    [Theory]
    [InlineData("Branch", "let a = 1\nlet b = 2\nvar view = a@ref\nif a == 1\n    view = b@ref\nelse\n    view = a@ref\nrequire view@follow == 2 else => $abort(\"branch\")")]
    [InlineData("Loop", "let a = 1\nlet b = 2\nvar view = a@ref\nvar n = 0\nwhile n < 3\n    require view@follow >= 1 else => $abort(\"before\")\n    view = b@ref\n    n += 1\nrequire view@follow == 2 else => $abort(\"loop\")")]
    [InlineData("CopyEnds", "var a = 1\nlet b = 2\nvar view = a@ref\nlet saved = view\nview = b@ref\nrequire saved@follow == 1 else => $abort(\"saved\")\na = 3\nrequire view@follow == 2 else => $abort(\"current\")")]
    [InlineData("Uniq", "var a = 1\nvar b = 2\nvar view = a@uniq\nview = b@uniq\na = 3\nview@follow = 5\nrequire b == 5 and a == 3 else => $abort(\"uniq\")")]
    public void FollowsControlFlowAndEndsOnlyUnusedLoans(string name, string source)
        => ScalarEmissionTest.EmitFixture("LocalRegion" + name, source, string.Empty);

    [Theory]
    [InlineData("var a = 1\nvar b = 2\nvar view = a@ref\nif a == 1\n    view = b@ref\na = 3\nrequire view@follow == 2 else => $abort(\"branch\")", "a = 3")]
    [InlineData("var a = 1\nvar b = 2\nvar view = a@ref\nvar n = 0\nwhile n < 3\n    b = 3\n    require view@follow >= 1 else => $abort(\"old\")\n    view = b@ref\n    require view@follow == 3 else => $abort(\"loop\")\n    n += 1\nrequire view@follow == 3 else => $abort(\"after\")", "b = 3")]
    public void KeepsEveryLoanReachingAJoin(string source, string location)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Contains(errors, x => x.Code == nameof(DiagnosticCode.ComparisonLoanConflict_Kd) && source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == location);
        Assert.DoesNotContain(errors, x => x.Category == DiagnosticCategory.Unsupported);
    }

    [Theory]
    [InlineData("var first = 0\nvar view = first@ref\nlet saved = view\nlet second = 5\nview = second@ref\nfirst = 9\nrequire saved@follow == 0 else => $abort(\"saved\")", "first = 9")]
    [InlineData("var first = 0\nvar view = first@ref\nvar second = 5\nview = second@ref\nsecond = 9\nrequire view@follow == 5 else => $abort(\"view\")", "second = 9")]
    public void AssignmentKeepsEveryLiveHolder(string source, string location)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        var error = Assert.Single(errors);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(location, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.NotEmpty(error.Related!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    loop => ()\n")]
    public void FiniteToInferredToFixedIsRefutedEvenAfterNoncompletion(string continuation)
    {
        var source = "func bad(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n    view = local@ref\n" + continuation + "    return view\npublic func main() => ()";
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
        var error = Assert.Single(errors);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Contains("outlives", error.Label, StringComparison.Ordinal);
        Assert.Contains(error.Related!, x => x.Role == "origin");
    }

    [Fact]
    public void FixedInputRelationsStillNeedAPremise()
    {
        const string Source = "func bad(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    var view = a\n    view = b\n    return view\npublic func main() => ()";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Proof, error.Category);
        Assert.Contains("b outlives a", error.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void FiniteChainHasTheSameEvidenceInEveryOutput()
    {
        const string Source = "func bad(input: ref/i32) -> ref/i32 during input\n    var view = input\n    let local = 5\n    view = local@ref\n    return view\npublic func main() => ()";
        var check = DiagnosticCorpus.Check(Source);
        var record = Assert.Single(check.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), record.Code);
        Assert.Equal("view", Source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.Equal("requires the borrow local@ref outlives input, which is false", record.Label);
        Assert.Contains(record.Related!, x => Source.Substring(x.Span!.Value.Start, x.Span.Value.Length) == "local@ref");
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("local@ref", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = origin:", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal((record.Code, record.Display!.Range), (sent.Code, sent.Range));
        }

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmConstraintsLoansAndEmissionReuseStorage()
    {
        const string Source = "var a = 1\nlet b = 2\nvar view = a@ref\nlet saved = view\nview = b@ref\nrequire saved@follow == 1 else => $abort(\"saved\")\na = 3\nrequire view@follow == 2 else => $abort(\"current\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }
}
