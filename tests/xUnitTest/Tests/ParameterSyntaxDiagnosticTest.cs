// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ParameterSyntaxDiagnosticTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("func f(a: i32, ! b: i32) => ()", ",", "ArgumentBoundaryComma_Kd")]
    [InlineData("func f(a: i32 !, b: i32) => ()", ",", "ArgumentBoundaryComma_Kd")]
    [InlineData("func f(a?: i32) => ()", "?", "ParameterNameMarker_Kd")]
    [InlineData("func f(! a: i32 ! b: i32) => ()", "! b", "DuplicateArgumentBoundary_Kd")]
    [InlineData("let f = func (! x: i32) => x", "!", "ArgumentBoundaryContext_Kd")]
    [InlineData("specialize func f<i32>(! x: i32) => ()", "!", "ArgumentBoundaryContext_Kd")]
    [InlineData("func f(!) => ()", ")", "EmptyNamedParameterSection_Kd")]
    [InlineData("func f(a: i32 !) => ()", ")", "EmptyNamedParameterSection_Kd")]
    public void ReportsTheInvalidPunctuation(string source, string marked, string code)
    {
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(new SourceSpan(source.IndexOf(marked, StringComparison.Ordinal), 1), error.Span);
        Assert.False(string.IsNullOrWhiteSpace(error.Label));
        Assert.DoesNotContain("Unexpected token", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func f(! a: i32) => ()")]
    [InlineData("func f(a: i32 ! b: i32,) => ()")]
    [InlineData("let f = func (x: i32) => x")]
    [InlineData("specialize func f<i32>(x: i32) => ()")]
    public void KeepsValidForms(string source) => Assert.Empty(Parse(source).Diagnostics.Finalize().Diagnostics);

    [Theory]
    [InlineData("func f(value => first: i32, value => second: i32) => ()")]
    [InlineData("func f(value => first: i32 ! value => second: i32) => ()")]
    [InlineData("func f(値 => first: i32, 値 => second: i32) => ()", "値")]
    [InlineData("struct S\n    func f(self, self => value: i32) => ()", "self")]
    [InlineData("\nfunc f(\n    value => first: i32,\n    value => second: i32\n) => ()")]
    public void DuplicateExternalNamesIdentifyBothDeclarations(string source, string name = "value")
    {
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal("DuplicateExternalParameterName_Kd", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal($"The external parameter name '{name}' is already used in this parameter list", error.Message);
        Assert.Equal(new SourceSpan(source.LastIndexOf(name, StringComparison.Ordinal), name.Length), error.Span);
        Assert.Equal("external name is repeated", error.Label);
        var first = Assert.Single(error.Related!);
        Assert.Equal("declaration", first.Role);
        Assert.Equal("first external parameter name", first.Label);
        Assert.Equal(new SourceSpan(source.IndexOf(name, StringComparison.Ordinal), name.Length), first.Span);
    }

    [Fact]
    public void ExternalAndInternalNamesHaveSeparateUniquenessRules()
    {
        var c = MinimalEmissionTest.Analyze("func f(first => second: i32 ! second => first: i32) => ()");
        c.Binding.ReportDiagnostics();
        Assert.Empty(c.Diagnostics.Finalize().Diagnostics);
    }

    [Fact]
    public void LongExternalNamesKeepBothFullLocations()
    {
        var name = new string('x', 200);
        var source = $"func f({name} => a: i32, {name} => b: i32) => ()";
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal("DuplicateExternalParameterName_Kd", error.Code);
        Assert.Equal(200, error.Span!.Value.Length);
        Assert.Equal(200, Assert.Single(error.Related!).Span!.Value.Length);
        var fact = Assert.Single(error.Reason!);
        Assert.Equal("name", fact.Name);
        Assert.Contains("…", fact.Value, StringComparison.Ordinal);
        Assert.Contains("already used in this parameter list", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EachDuplicateAndIndependentBindingErrorSurvive()
    {
        const string source = "func f(value => a: i32, value => b: i32 ! value => c: i32) => ()\nlet n: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["DuplicateExternalParameterName_Kd", "DuplicateExternalParameterName_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        foreach (var error in result.Diagnostics.Take(2))
        {
            Assert.Equal(new SourceSpan(source.IndexOf("value", StringComparison.Ordinal), 5), Assert.Single(error.Related!).Span);
        }

        Assert.False(c.Emission.Validate(out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    [Fact]
    public void CliAndLspExplainDuplicateExternalNames()
    {
        const string source = "func f(value => a: i32 ! value => b: i32) => ()";
        var path = Path.GetFullPath("parameter-duplicate.kimi");
        var c = Parse(source, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("DuplicateExternalParameterName_Kd", error.Code);
        var first = Assert.Single(error.Related!);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("^ " + error.Label, console.Text, StringComparison.Ordinal);
        Assert.Contains(first.Label!, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            if (related)
            {
                Assert.Equal(first.Range, Assert.Single(sent.RelatedInformation!).Location.Range);
            }
            else
            {
                Assert.Contains("parameter-duplicate.kimi:1:8: first external parameter name", sent.Message, StringComparison.Ordinal);
            }

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void BothCommasAndIndependentErrorsSurviveRecovery()
    {
        const string source = "func f(a: i32, !, b?: i32) => ()\nlet n = (1";
        var errors = Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["ArgumentBoundaryComma_Kd", "ArgumentBoundaryComma_Kd", "ParameterNameMarker_Kd", "MissingSyntax_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(source.IndexOf(','), errors[0].Span!.Value.Start);
        Assert.Equal(source.LastIndexOf(','), errors[1].Span!.Value.Start);
    }

    [Fact]
    public void ParameterRecoveryPreservesIndependentBindingErrors()
    {
        var c = MinimalEmissionTest.Analyze("func f(a?: i32) => ()\nlet n: i32 = true");
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["ParameterNameMarker_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.False(c.Emission.Validate(out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    [Fact]
    public void CliAndLspSelectTheCommaRatherThanTheNextParameter()
    {
        const string source = "func f(a: i32 !, b: i32) => ()";
        var path = Path.GetFullPath("parameter-syntax.kimi");
        var c = Parse(source, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("A comma cannot appear immediately before or after an argument-name boundary", error.Message);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("^ " + error.Label, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static Compilation Parse(string source, string path = "parameter-syntax.kimi")
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        return c;
    }
}
