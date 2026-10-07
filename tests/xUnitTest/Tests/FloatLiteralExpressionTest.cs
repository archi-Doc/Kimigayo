// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FloatLiteralExpressionTest(ITestOutputHelper output)
{
    private const string Source = """
        func take(value: f32) -> f32 => value
        func borrow(value: ref/f32) -> f32 => value
        let x: f32 = 2.0
        require take(1.0 + 1.0) == x else => $abort("argument")
        require borrow(-(1.0 + 1.0)) == -x else => $abort("borrow")
        require (4.0 / 2.0) == x and x == +(3.0 - 1.0) else => $abort("comparison")
        let branch = if true => (1.0 + 1.0) else => x
        require branch == x else => $abort("branch")
        let array: [2 of f32] = [1.0 + 1.0, 2.0 * 1.0]
        require array[0] == x and array[1] == x else => $abort("array")
        let rounded = take(16777216.0 + 1.0 - 16777216.0)
        let converted = (16777216.0 + 1.0 - 16777216.0)@f32
        require rounded == 0.0 and converted == 1.0 else => $abort("rounding")
        Console.writeLine("ok")
        """;

    [Fact]
    public void CommonContextsFitFloatExpressionsAndRoundEachOperation()
        => ScalarEmissionTest.EmitFixture("FloatLiteralExpressionFitting", Source, "ok\n");

    [Fact]
    public void DefaultDoesNotSelectBetweenFloatCandidates()
    {
        const string Program = "func take(x: f32) => ()\nfunc take(x: f64) => ()\ntake(1.0 + 1.0)";
        var path = Path.GetFullPath("float-literal.kimi");
        var c = MinimalEmissionTest.Analyze(Program, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal("take(1.0 + 1.0)", Program.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(2, error.Related!.Length);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("float-literal.kimi:3:1", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            if (related)
            {
                Assert.Equal(2, sent.RelatedInformation!.Length);
            }

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }

        Assert.True(DiagnosticCorpus.Check(Program.Replace("take(1.0 + 1.0)", "take((1.0 + 1.0)@f32)", StringComparison.Ordinal)).Accepted);
    }

    [Theory]
    [InlineData("func take(x: f32) => ()\nlet value = 1.0 + 1.0\ntake(value)")]
    [InlineData("func take(x: f32) => ()\ntake((1.0 + 1.0)@f64)")]
    [InlineData("func take(x: f32) => ()\nfunc value() -> f64 => 1.0\ntake(value() + 1.0)")]
    [InlineData("func take(x: f32) => ()\ntake(1 + 1.0)")]
    [InlineData("func take(x: f32) => ()\ntake(1.0 + 1)")]
    [InlineData("func take(x: f32) => ()\ntake(1.0 % 1.0)")]
    [InlineData("let length: [(1.0 + 1.0) of u8] = []")]
    [InlineData("let position = ^(1.0 + 1.0)")]
    [InlineData("let range = (1.0 + 1.0)..3")]
    public void FittingDoesNotConvertOrExtendIntegerOnlyForms(string source)
    {
        var result = DiagnosticCorpus.Check(source);
        Assert.False(result.Accepted);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void RebindingAnEditedLiteralRevokesThePreviousCategory()
    {
        const string Program = "func take(x: f32) => ()\ntake(1.0 + 1.0)";
        var c = MinimalEmissionTest.Analyze(Program);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var operation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<BinaryKoto>().Single();
        foreach (var expression in new[] { "1 + 1", "1.0 + 1.0" })
        {
            var donor = MinimalEmissionTest.Analyze(Program.Replace("1.0 + 1.0", expression, StringComparison.Ordinal));
            var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<BinaryKoto>().Single();
            Assert.True(KotoHelper.Replace(operation.Parent!, operation, replacement));
            Assert.Equal(expression == "1.0 + 1.0", c.Bind().IsComplete);
            operation = replacement;
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFittingReusesBindingOwnershipAndEmissionStorage()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
