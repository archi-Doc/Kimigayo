// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class OriginRecoveryTest(ITestOutputHelper output)
{
    private const string Prelude = "struct View<T> {source}\n    let value: ref/T during source\n";

    [Theory]
    [InlineData("View<i32>{}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{static}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{_}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a and b}", "OriginBindingSetName_Kd")]
    [InlineData("View<i32>{a => static}", "OriginBindingSetName_Kd")]
    [InlineData("(ref/i32){a}", "OriginBindingSetTarget_Kd")]
    [InlineData("[2 of i32]{a}", "OriginBindingSetTarget_Kd")]
    [InlineData("ref/i32 during a and b", "BorrowOriginIntersection_Kd")]
    [InlineData("i32 during a", "BorrowOriginTarget_Kd")]
    [InlineData("raw/i32 during a", "BorrowOriginSemantics_Kd")]
    [InlineData("ref/i32 during a?", "BorrowOriginSuffixOrder_Kd")]
    [InlineData("ref/i32 during a during b", "DuplicateBorrowOrigin_Kd")]
    public void MalformedAnnotationsEstablishNoTypeAndKeepIndependentErrors(string type, string code)
    {
        var source = Prelude + "func f(x: " + type + ") => ()\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        Assert.Equal([code, "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        var function = ParseTestHelper.GetChildren(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        Assert.Null(function.Parameters[0].Type.TypeOf());
        Assert.NotEqual(BindingState.Resolved, function.Parameters[0].Type.StateOf());
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, c.Diagnostics.Finalize(rejected: true).Diagnostics);
    }

    [Theory]
    [InlineData("a = b")]
    [InlineData("a <= b")]
    [InlineData("a b")]
    [InlineData("a")]
    public void AnInvalidRelationCannotSupplyAnOutlivesPremise(string relation)
    {
        var c = MinimalEmissionTest.Analyze("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin " + relation + "\n    ()\nlet wrong: i32 = true");
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        Assert.Equal(["OriginRelationOperator_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        var function = ParseTestHelper.GetChildren(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var clause = Assert.Single(OriginClauses.Get(function));
        Assert.NotEqual(OriginJudgment.Proven, c.Binding.JudgeOriginRelation(function.Parameters[0].Type.TypeOf()!.Origin!, function.Parameters[1].Type.TypeOf()!.Origin!, function));
        Assert.NotEqual(BindingState.Resolved, clause.StateOf());
        Assert.NotNull(clause.CodeContext.RecoveryCause(clause));
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, c.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.NotEqual(OriginJudgment.Proven, c.Binding.JudgeOriginRelation(function.Parameters[0].Type.TypeOf()!.Origin!, function.Parameters[1].Type.TypeOf()!.Origin!, function));
    }

    [Fact]
    public void AnnotationRecoveryDoesNotHideAnUnknownPayloadType()
    {
        const string Source = "func f(x: View<Missing>{}) => ()";
        var c = MinimalEmissionTest.Analyze(Prelude + Source);
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["UnresolvedBinding_Kd", "OriginBindingSetName_Kd"], errors.Select(static x => x.Code));
    }

    [Theory]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b\n    origin a = b\n    return x")]
    [InlineData("func f(x: [2 of i32]{a}) => ()\nlet values = [1, 2]\nf(values)")]
    public void UsesOfAnInvalidOriginContractRestOnItsCause(string source)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nlet wrong: i32 = true");
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        Assert.Equal([source.Contains("origin a = b", StringComparison.Ordinal) ? "OriginRelationOperator_Kd" : "OriginBindingSetTarget_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
    }

    [Fact]
    public void AnInvalidContractCannotHideAnIndependentlyRefutedLifetime()
    {
        const string Source = """
            func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during static
                origin a = b
                let local = 1
                return local@ref
            let wrong: i32 = true
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        Assert.Equal(["OriginRelationOperator_Kd", "UnsatisfiedOriginRelation_Kd", "TypeMismatch_Kd"], c.Diagnostics.Finalize(rejected: true).Diagnostics.Select(static x => x.Code));
    }

    [Fact]
    public void ValidAnnotationsAndRelationsExecute()
    {
        const string Source = """
            func f(x: ref/i32 during a, y: ref/i32 during b)
                origin a outlives b
                Console.writeLine("\(x) \(y)")
            let x = 1
            let y = 2
            f(x@ref, y@ref)
            """;
        ScalarEmissionTest.EmitFixture("OriginRecoveryValid", Source, "1 2\n");
    }

    [Fact]
    public void AnAbsentProjectionDoesNotHideAnIndependentlyRefutedLifetime()
    {
        const string Source = Prelude + "func f<T>(value: View<T>) -> ref/i32 during static\n    origin value.sorce outlives static\n    let local = 1\n    return local@ref\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(static x => x.Code + " " + x.Span)));
        Assert.Equal(["InvalidOriginBinding_Kd", "UnsatisfiedOriginRelation_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(Source.IndexOf("sorce", StringComparison.Ordinal), 5), errors[0].Span);
        Assert.Equal(new SourceSpan(Source.IndexOf("local@ref", StringComparison.Ordinal), 9), errors[1].Span);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, c.Diagnostics.Finalize(rejected: true).Diagnostics);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void FailedOriginClausesReuseTheirCauseStorage()
    {
        var c = MinimalEmissionTest.Analyze(Prelude + "func f<T>(value: View<T>) -> View<T>{result}\n    origin result.sorce == value.sorce\n    return value\nlet next = 1");
        var invalid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            invalid &= !c.Binding.Result.IsComplete;
        }));
        Assert.True(invalid);
    }

    [Fact]
    public void CliAndLspLocateTheFailedProjectionAndIndependentMismatch()
    {
        const string Source = Prelude + "func f<T>(value: View<T>) -> View<T>{result}\n    origin result.source == value.sorce\n    return 1\nlet next = 2";
        var path = Path.GetFullPath("origin-projection.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["InvalidOriginBinding_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var projection = result.Diagnostics[0];
        Assert.Equal(new SourceSpan(Source.IndexOf("sorce", StringComparison.Ordinal), 5), projection.Span);
        Assert.Equal("View has no Origin slot sorce", projection.Label);
        Assert.Equal("The Origin slots of View are source", projection.Note);
        Assert.Equal(new SourceSpan(Source.IndexOf("{source}", StringComparison.Ordinal), 8), Assert.Single(projection.Related!).Span);
        Assert.Empty(projection.Repairs ?? []);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), sent.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void CliAndLspExplainTheAnnotationCause()
    {
        const string Source = "func f(x: [2 of i32]{a}) => ()\nlet wrong: i32 = true";
        var path = Path.GetFullPath("origin-recovery.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["OriginBindingSetTarget_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(Source.IndexOf("{a}", StringComparison.Ordinal), 3), result.Diagnostics[0].Span);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), sent.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
