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
        Assert.False(string.IsNullOrWhiteSpace(error.Advice));
        Assert.DoesNotContain("Unexpected token", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func f(! a: i32) => ()")]
    [InlineData("func f(a: i32 ! b: i32,) => ()")]
    [InlineData("let f = func (x: i32) => x")]
    [InlineData("specialize func f<i32>(x: i32) => ()")]
    public void KeepsValidForms(string source) => Assert.Empty(Parse(source).Diagnostics.Finalize().Diagnostics);

    [Fact]
    public void BothCommasAndIndependentErrorsSurviveRecovery()
    {
        const string source = "func f(a: i32, !, b?: i32) => ()\nlet n = (1";
        var errors = Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["ArgumentBoundaryComma_Kd", "ArgumentBoundaryComma_Kd", "ParameterNameMarker_Kd", "MissingExpectedToken_Kd"], errors.Select(static x => x.Code));
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
        Assert.Contains(error.Advice!, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Advice!, sent.Message, StringComparison.Ordinal);
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
