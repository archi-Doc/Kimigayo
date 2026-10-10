// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class OperationSuffixRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("move?")]
    [InlineData("copy?")]
    [InlineData("ref?")]
    [InlineData("uniq?")]
    [InlineData("objref?")]
    [InlineData("objuniq?")]
    [InlineData("raw?")]
    [InlineData("owner?")]
    [InlineData("move??")]
    [InlineData("ref???")]
    [InlineData("move?.member")]
    [InlineData("copy?()")]
    public void AnOptionalSuffixCannotTurnAnOperationIntoAType(string operation)
    {
        var source = "let value = 1\nlet result = value@" + operation + "\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var records = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["MisplacedSyntax_Kd", "TypeMismatch_Kd"], records.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf('?', StringComparison.Ordinal), 1), records[0].Span);
        Assert.Equal("Syntax.SemanticsShorthandSuffix", Assert.Single(records[0].Reason!, static x => x.Name == "form").Value);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single();
        Assert.IsType<ErrorKoto>(conversion.Right);
        Assert.Null(conversion.Right.TypeOf());
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(records, c.Diagnostics.Finalize(rejected: true).Diagnostics);
    }

    [Theory]
    [InlineData("let value: (i32)? = .Some(7)")]
    [InlineData("let values: [2 of _] = [1, 2]")]
    [InlineData("struct copy\nlet value: copy? = .None")]
    [InlineData("struct move\n    Self is Copy\nlet value: move? = .None\nlet other = value@owner/move?")]
    [InlineData("let value: i32? = .Some(1)\nlet view = value@ref/(i32?)")]
    public void CompleteTypesKeepTheirIndependentSuffixGrammar(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ACompleteOptionalTargetStillUsesTheTypeGrammar()
    {
        var c = CompilationTestHelper.ParseSuccess("let view = source@ref/i32?");
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single();
        Assert.IsType<OptionalTypeKoto>(conversion.Right);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("move")]
    [InlineData("ref")]
    public void RebindingTheFailedTargetReusesItsCause(string operation)
    {
        var c = MinimalEmissionTest.Analyze("let value = 1\nlet result = value@" + operation + "?");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void OtherCallArgumentsRemainIndependentOfTheFailedTarget()
    {
        const string Source = "func consume(first: i32, second: i32) => ()\nlet value = 1\nconsume(value@move?, absent)";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        Assert.Equal(["MisplacedSyntax_Kd", "UnresolvedBinding_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
    }

    [Fact]
    public void ValidOptionalTypesAndBareOperationsExecute()
    {
        const string Source = "let value: (i32)? = .Some(7)\nlet values: [2 of _] = [1, 2]\nlet copied = values[0]@copy\nmatch value@move\n    .Some(let number) => Console.writeLine(\"\\(number + copied)\")\n    .None => $abort(\"missing\")";
        ScalarEmissionTest.EmitFixture("OperationSuffixValid", Source, "8\n");
    }

    [Theory]
    [InlineData("move")]
    [InlineData("copy")]
    public void PublicOutputKeepsAnIndependentOperandFailure(string operation)
    {
        var source = "let result = absent@" + operation + "?\nlet wrong: i32 = true";
        var path = Path.GetFullPath("operation-suffix.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["UnresolvedBinding_Kd", "MisplacedSyntax_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf("absent", StringComparison.Ordinal), 6), result.Diagnostics[0].Span);
        var record = result.Diagnostics[1];
        Assert.Equal(new SourceSpan(source.IndexOf('?', StringComparison.Ordinal), 1), record.Span);
        Assert.Empty(record.Repairs ?? []);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var records = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Code), records.Select(static x => x.Code));
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), records.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(records));
        }
    }
}
