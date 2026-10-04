// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionReferenceRankingTest(ITestOutputHelper output)
{
    private const string Types = "func fail(value: i32) -> Never => $abort(\"not called\")\n";
    private const string Broad = "func choose(value: (i32) -> i32) -> i32 => 0\n";
    private const string Narrow = "func choose(value: (i32) -> Never) -> i32 => 42\n";

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ParameterSubtypingRanksFittingReferences(bool reverse, bool erased, bool borrowed)
    {
        var use = erased ? "let action: ((i32) -> Never) -> i32 = choose\nlet value: (i32) -> Never = fail\nrequire action(value@move) == 42 else => $abort(\"reference\")" :
            "func apply<F>(action: ref/F) -> i32\n    F is Callable<((i32) -> Never) -> i32>\n    let value: (i32) -> Never = fail\n    return action(value@move)\nrequire apply(choose) == 42 else => $abort(\"reference\")";
        var source = Types + (reverse ? Narrow + Broad : Broad + Narrow) + use;
        if (borrowed)
        {
            source = source.Replace("func choose(value: (i32) -> i32)", "func choose(value: ref/((i32) -> i32))", StringComparison.Ordinal)
                .Replace("func choose(value: (i32) -> Never)", "func choose(value: ref/((i32) -> Never))", StringComparison.Ordinal)
                .Replace("((i32) -> Never) -> i32", "(ref/((i32) -> Never)) -> i32", StringComparison.Ordinal)
                .Replace("action(value@move)", "action(value@ref)", StringComparison.Ordinal);
        }

        ScalarEmissionTest.EmitFixture("FunctionReferenceRanking" + reverse + erased + borrowed, source, string.Empty);
    }

    [Fact]
    public void UnsafeBestReferenceDoesNotFallBack()
    {
        var c = MinimalEmissionTest.Analyze(Types + Broad + "unsafe " + Narrow + "let action: ((i32) -> Never) -> i32 = choose");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(DiagnosticCode.UnsafeFunctionValue_Kd, Assert.Single(c.Binding.Issues).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpposingParameterAdvantagesReportOnlyTheFittingCandidates(bool independent)
    {
        const string Declarations = "func choose(a: (i32) -> Never, b: (i32) -> i32) -> i32 => 1\nfunc choose(a: (i32) -> i32, b: (i32) -> Never) -> i32 => 2\nfunc choose(a: bool) -> i32 => 3\n";
        var source = Declarations + "let action: ((i32) -> Never, (i32) -> Never) -> i32 = choose" + (independent ? "\nlet bad: i32 = false" : string.Empty);
        var path = Path.GetFullPath("reference-ranking.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(independent ? 2 : 1, result.Diagnostics.Length);
        var error = Assert.Single(result.Diagnostics, x => x.Code == "AmbiguousBinding_Kd");
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("choose", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("no unique best candidate among 2 candidates", error.Label);
        Assert.Equal("2", Assert.Single(error.Reason!).Value);
        Assert.Equal([0, 1], error.Related!.Select(x => x.Range!.Value.Start.Line));
        Assert.All(error.Related!, x => Assert.Contains("callable signature", x.Label, StringComparison.Ordinal));
        Assert.All(error.Related!, x => Assert.DoesNotContain("…", x.Label, StringComparison.Ordinal));
        Assert.Contains("Results do not rank candidates", error.Note, StringComparison.Ordinal);
        Assert.Contains("Type annotation", error.Advice, StringComparison.Ordinal);
        Assert.Null(error.Repairs);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Length, sent.Length);
            Assert.Contains(sent, x => x.Message.Contains("Results do not rank candidates", StringComparison.Ordinal));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmReferenceRankingReusesCandidateStorage(bool borrowed)
    {
        var source = Types + Broad + Narrow + "let action: ((i32) -> Never) -> i32 = choose\nlet value: (i32) -> Never = fail\nlet result = action(value@move)";
        if (borrowed)
        {
            source = source.Replace("func choose(value: (i32) -> i32)", "func choose(value: ref/((i32) -> i32))", StringComparison.Ordinal)
                .Replace("func choose(value: (i32) -> Never)", "func choose(value: ref/((i32) -> Never))", StringComparison.Ordinal)
                .Replace("((i32) -> Never) -> i32", "(ref/((i32) -> Never)) -> i32", StringComparison.Ordinal)
                .Replace("action(value@move)", "action(value@ref)", StringComparison.Ordinal);
        }

        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
