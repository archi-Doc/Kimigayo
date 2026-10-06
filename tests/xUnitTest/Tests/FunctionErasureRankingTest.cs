// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionErasureRankingTest(ITestOutputHelper output)
{
    private const string Concrete = "func apply<F>(action: F) -> i32\n    F is Callable<owner, (i32) -> i32>\n    return action@move(42)\n";
    private const string Common = "func apply(action: (i32) -> i32) -> i32 => action(7)\n";

    [Theory]
    [InlineData("Inline", false)]
    [InlineData("Inline", true)]
    [InlineData("Closure", false)]
    [InlineData("Closure", true)]
    [InlineData("Item", false)]
    [InlineData("Item", true)]
    public void ErasureAndConcreteAcquisitionAreIncomparable(string kind, bool reversed)
    {
        var prefix = kind switch
        {
            "Closure" => "let action = func [] (x: i32) => x\n",
            "Item" => "func action(x: i32) -> i32 => x\n",
            _ => string.Empty,
        };
        var argument = kind == "Inline" ? "func [] (x) => missing" : "action";
        var source = (reversed ? Common + Concrete : Concrete + Common) + prefix + "let result = apply(" + argument + ")";
        this.AssertAmbiguous(source, kind == "Inline");
    }

    [Fact]
    public void OtherArgumentAdvantagesCannotRankErasureAgainstAcquisition()
    {
        const string Source = "func choose<F>(value: ref/(ref/i32 during a), action: F) -> i32\n    F is Callable<owner, (i32) -> i32>\n    return action@move(42)\n" +
            "func choose(value: ref/i32, action: (i32) -> i32) -> i32 => action(7)\n" +
            "let value = 42\nlet view = value@ref\nlet outer = view@ref\nlet action = func [] (x: i32) => x\nlet result = choose(outer, action)";
        this.AssertAmbiguous(Source, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlreadyCommonValuesKeepExactTypeRanking(bool reversed)
    {
        var source = (reversed ? Common + Concrete : Concrete + Common) +
            "let action: (i32) -> i32 = func [] (x) => x\nrequire apply(action@move) == 7 else => $abort(\"exact\")";
        ScalarEmissionTest.EmitFixture("FunctionErasureExact" + reversed, source, string.Empty);
    }

    [Fact]
    public void TwoErasuresAllowOrdinaryDefaultRanking()
    {
        const string Source = Common + "func apply(action: (i32) -> i32, extra: i32 = 0) -> i32 => 0\n" +
            "require apply(func [] (x) => x) == 7 else => $abort(\"erasure\")";
        ScalarEmissionTest.EmitFixture("FunctionErasureDefaults", Source, string.Empty);
    }

    [Fact]
    public void ACommonFunctionPlaceStillRequiresExplicitTransfer()
    {
        var c = MinimalEmissionTest.Analyze(Concrete + Common + "let action: (i32) -> i32 = func [] (x) => x\nlet result = apply(action)");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal("TransferRequired_Kd", Assert.Single(TestDiagnostics.Of(c)).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmExactAndErasedSelectionAllocateNothing()
    {
        const string Source = Concrete + Common + "let action: (i32) -> i32 = func [] (x) => x\nlet result = apply(action@move)\n" +
            "func erased(action: (i32) -> i32) -> i32 => action(1)\nfunc erased(action: (i32) -> i32, extra: i32 = 0) -> i32 => 0\nlet other = erased(func [] (x) => x)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    private void AssertAmbiguous(string source, bool unboundBody)
    {
        var path = Path.GetFullPath("function-erasure-ranking.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("AmbiguousBinding_Kd", error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("no unique best candidate among 2 candidates", error.Label);
        Assert.NotNull(error.Label);
        Assert.Equal("2", Assert.Single(error.Reason!).Value);
        Assert.Equal(2, error.Related!.Length);
        Assert.StartsWith(source.Contains("choose(", StringComparison.Ordinal) ? "choose(" : "apply(", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Null(error.Repairs);
        if (unboundBody)
        {
            Assert.Null(Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).BoundClosure);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Label, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
