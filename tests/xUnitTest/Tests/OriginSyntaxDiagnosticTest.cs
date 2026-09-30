// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class OriginSyntaxDiagnosticTest(ITestOutputHelper output)
{
    // SPEC 3.3.6 and 15.3.1 fix annotation attachment and order before semantic lookup.
    [Theory]
    [InlineData("ref/i32 from a", "from", "BorrowOriginKeyword_Kd")]
    [InlineData("owner/ref/i32 during a", "during", "BorrowOriginSemantics_Kd")]
    [InlineData("unsafe/ref/i32 during a", "during", "BorrowOriginSemantics_Kd")]
    [InlineData("(ref/i32)? during a", "during", "BorrowOriginTarget_Kd")]
    [InlineData("(ref/i32 during a) during b", "during b", "BorrowOriginTarget_Kd")]
    [InlineData("View<i32>{v} during a", "during", "BorrowOriginBindingSet_Kd")]
    [InlineData("ref/i32 during a?", "?", "BorrowOriginSuffixOrder_Kd")]
    [InlineData("ref/i32 during a during b", "during b", "DuplicateBorrowOrigin_Kd")]
    public void ExplainsAnnotationFailureAtItsSyntax(string type, string marked, string code)
    {
        var source = $"func f(x: {type}) => ()";
        var error = Assert.Single(Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(new SourceSpan(source.IndexOf(marked, StringComparison.Ordinal), marked.StartsWith("during", StringComparison.Ordinal) ? 6 : marked.Length), error.Span);
        Assert.DoesNotContain("Unexpected token", error.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(error.Label));
        Assert.False(string.IsNullOrWhiteSpace(error.Advice));
        Assert.Null(error.Reason);
    }

    [Theory]
    [InlineData("ref/i32 during a")]
    [InlineData("ref/i32? during a")]
    [InlineData("(ref/i32 during a)?")]
    [InlineData("ref/i32 during (a and b)")]
    [InlineData("unsafe/(ref/i32 during a)")]
    [InlineData("ref/(uniq/i32 during b) during a")]
    [InlineData("View<i32>{v}")]
    [InlineData("View<i32> during a")]
    public void AcceptsTheCorrespondingTypeSyntax(string type)
        => Assert.Empty(Parse($"func f(x: {type}) => ()").Diagnostics.Finalize().Diagnostics);

    [Fact]
    public void RepeatedAnnotationsAndIndependentErrorsKeepTheirLocations()
    {
        const string source = "\nfunc f(x: ref/i32 during a during b?) => ()\nlet n = (1";
        var errors = Parse(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["DuplicateBorrowOrigin_Kd", "BorrowOriginSuffixOrder_Kd", "MissingExpectedToken_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf("during b", StringComparison.Ordinal), 6), errors[0].Span);
        Assert.Equal(new SourceSpan(source.IndexOf('?'), 1), errors[1].Span);
    }

    [Fact]
    public void BindingKeepsTheFirstAnnotationAndIndependentTypeFailure()
    {
        const string source = "func f(x: ref/i32 during a during b) -> ref/i32 during a => x\nlet n: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["DuplicateBorrowOrigin_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.False(c.Emission.Validate(out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    [Fact]
    public void CliAndLspExplainAnnotationOrder()
    {
        const string source = "func f(x: ref/i32 during a?) => ()";
        var path = Path.GetFullPath("origin-syntax.kimi");
        var c = Parse(source, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("An optional suffix cannot follow an Origin annotation in the same Type", error.Message);
        Assert.Equal("optional suffix follows the annotation", error.Label);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("^ optional suffix follows the annotation", console.Text, StringComparison.Ordinal);
        Assert.Contains(error.Advice!, console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            Assert.Contains(error.Advice!, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static Compilation Parse(string source, string path = "origin-syntax.kimi")
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        return c;
    }
}
