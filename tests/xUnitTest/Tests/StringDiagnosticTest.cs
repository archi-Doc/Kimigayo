// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public sealed class StringDiagnosticTest(ITestOutputHelper output)
{
    private const string BrokenCall = "::Kimi.Console.writeLine(\"Hello, world!)";
    private const string Advice = "Close the string before any code that follows it.";

    // SPEC 23.3.6.2–4: the lexical cause owns the explanation, including recovery of its argument and enclosing delimiters.
    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void MissingQuoteExplainsTheCallWithoutCascades(string lineEnd)
    {
        var c = Analyze(BrokenCall + lineEnd);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MissingStringLiteralEnd_Kd), error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(new SourceSpan(25, 1), error.Span);
        Assert.Equal(new SourceRange(new(0, 25), new(0, 26)), error.Display!.Range);
        Assert.Equal("Unterminated string literal; expected closing \".", error.Message);
        Assert.Equal("string starts here", error.Label);
        Assert.Equal("\"", Assert.Single(error.Reason!).Value);
        Assert.Equal("expected", error.Reason![0].Name);
        Assert.Equal(Advice, error.Advice);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(result, c.Diagnostics.Finalize(rejected: true));
    }

    [Theory]
    [InlineData("Console.writeLine((\"hello))", 1)]
    [InlineData("let texts = [\"hello]", 1)]
    [InlineData("let text = \"hello", 1)]
    [InlineData("Console.writeLine(text: \"hello)", 1)]
    [InlineData("Console.writeLine(\"hello\\\")", 1)]
    [InlineData("Console.writeLine(\"\"\"hello)", 3)]
    [InlineData("Console.writeLine(\"\"\"\"hello)", 4)]
    [InlineData("Console.writeLine((\"hello))\nlet next = 1", 1)]
    [InlineData("Console.writeLine(\n    \"hello\n)", 1)]
    [InlineData("func f()\n    Console.writeLine(\"hello)\nlet next = 1", 1)]
    public void RecoveryRetainsTheInvalidValueAndOpeningDelimiter(string source, int quotes)
    {
        var error = Assert.Single(Analyze(source).Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.MissingStringLiteralEnd_Kd), error.Code);
        Assert.Equal(new SourceSpan(source.IndexOf('"'), quotes), error.Span);
        Assert.Equal(new string('"', quotes), Assert.Single(error.Reason!).Value);
    }

    [Theory]
    [InlineData("missing(\"hello)", "UnresolvedBinding_Kd,MissingStringLiteralEnd_Kd")]
    [InlineData("Console.writeLine(missing, \"hello)", "UnresolvedBinding_Kd,MissingStringLiteralEnd_Kd")]
    [InlineData("let before = (1\n" + BrokenCall, "MissingSyntax_Kd,MissingStringLiteralEnd_Kd")]
    [InlineData("Console.writeLine(0x)", "InvalidNumericLiteral_Kd")]
    [InlineData("Console.writeLine(true)", "NoApplicableOverload_Kd")]
    [InlineData("Console.writeLine(\"hello\"", "MissingSyntax_Kd")]
    public void IndependentRequirementsStillExplainTheirFailures(string source, string codes)
    {
        var errors = Analyze(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(codes.Split(','), errors.Select(static x => x.Code));
    }

    [Fact]
    public void LiteralRecoveryDoesNotCrossSourceBoundaries()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("first.kimi", BrokenCall));
        c.Kotonoha.AddSource(new SourceDocument("second.kimi", "let n = (1"));
        c.Bind();
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal([nameof(DiagnosticCode.MissingStringLiteralEnd_Kd), nameof(DiagnosticCode.MissingSyntax_Kd)], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal(["first.kimi", "second.kimi"], result.Sources.Select(static x => x.Path));
        Assert.Equal([0, 1], result.Diagnostics.Select(static x => x.Source));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void IndependentErrorsSurviveRecovery(string lineEnd)
    {
        var source = lineEnd + BrokenCall + lineEnd + "let wrong: i32 = true" + lineEnd + "missing()" + lineEnd + "let x = (1";
        var errors = Analyze(source).Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(
            [nameof(DiagnosticCode.MissingStringLiteralEnd_Kd), nameof(DiagnosticCode.TypeMismatch_Kd), nameof(DiagnosticCode.UnresolvedBinding_Kd), nameof(DiagnosticCode.MissingSyntax_Kd)],
            errors.Select(static x => x.Code));
        Assert.Equal(new SourceRange(new(1, 25), new(1, 26)), errors[0].Display!.Range);
    }

    [Theory]
    [InlineData("::Kimi.Console.writeLine(\"Hello, world!\")")]
    [InlineData("Console.writeLine(\"\"\"hello\"\"\")")]
    [InlineData("Console.writeLine(\"first\nsecond\")")]
    [InlineData("Console.writeLine(\"hello\\\"\")")]
    public void ClosingTheStringRestoresAcceptance(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Empty(c.Diagnostics.Finalize().Diagnostics);
    }

    [Fact]
    public void CliAndLspExplainTheSameOpeningQuote()
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = Analyze(BrokenCall, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Hello.kimi:1:26", console.Text, StringComparison.Ordinal);
        Assert.Contains("^ string starts here", console.Text, StringComparison.Ordinal);
        Assert.Contains(Advice, console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("NoApplicableOverload", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(error.Code, sent.Code);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            Assert.Contains(Advice, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static Compilation Analyze(string source, string path = "Hello.kimi")
    {
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        c.Ownership.ControlFlow!.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        return c;
    }
}
