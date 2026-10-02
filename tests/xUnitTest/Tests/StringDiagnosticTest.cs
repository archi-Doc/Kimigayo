// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
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

    // A written string literal spans its delimiters, so a diagnostic at a node that ends with one covers the closing quote.
    [Fact]
    public void ClosureEndingInAStringCoversTheClosingQuote()
    {
        var error = Assert.Single(TestDiagnostics.Of(Analyze("let text = \"owned\"\nlet keep = func () => text == \"owned\"")));
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("func () => text == \"owned\"", error.Text);
        Assert.Empty(TestDiagnostics.Of(Analyze("let text = \"owned\"\nlet keep = func [text@move] () => text == \"owned\"")));
    }

    [Fact]
    public void CliAndLspUnderlineTheClosingQuote()
    {
        const string Closure = "func () => text == \"owned\"";
        var path = Path.GetFullPath("Hello.kimi");
        var c = Analyze("let text = \"owned\"\nlet keep = " + Closure, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(new SourceRange(new(1, 11), new(1, 11 + Closure.Length)), error.Display!.Range);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(new string('^', Closure.Length) + " ", console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('^', Closure.Length + 1), console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(error.Display.Range, sent.Range);
        }
    }

    [Theory]
    [InlineData("\"owned\"", "owned")]
    [InlineData("\"\"", "")]
    [InlineData("\"a\\\"b\"", "a\"b")]
    [InlineData("\"first\nsecond\"", "first\nsecond")]
    [InlineData("\"\"\"raw\\n\"\"\"", "raw\\n")]
    public void LiteralsAndTheirEnclosingNodesSpanTheDelimiters(string literal, string value)
    {
        var source = "let text = " + literal + "\nlet same = text == " + literal;
        var c = Analyze(source);
        Assert.Empty(TestDiagnostics.Of(c));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var literals = nodes.OfType<StringLiteralKoto>().ToArray();
        Assert.Equal(2, literals.Length);
        Assert.All(literals, x => Assert.Equal((literal, value), (Text(source, x.Span), x.Literal)));
        Assert.Equal("text == " + literal, Text(source, Assert.Single(nodes.OfType<EqualsEqualsKoto>()).Span));
    }

    // An interpolation segment has no delimiters of its own: it spans its text, while the literal spans both quotes.
    [Fact]
    public void InterpolationSegmentsSpanTheirTextBetweenTheDelimiters()
    {
        const string Literal = "\"a \\(n) b \\(\"c\")!\"";
        var source = "let n: i32 = 1\nlet text = " + Literal;
        var c = Analyze(source);
        Assert.Empty(TestDiagnostics.Of(c));
        var interpolated = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InterpolatedStringKoto>());
        Assert.Equal(Literal, Text(source, interpolated.Span));
        Assert.Equal(["a ", " b ", "!"], interpolated.Segments.Select(x => Text(source, x.Span)));
        Assert.Equal(["a ", " b ", "!"], interpolated.Segments.Select(static x => x.Literal));
        var nested = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StringLiteralKoto>(), x => !interpolated.Segments.Contains(x));
        Assert.Equal(("\"c\"", "c"), (Text(source, nested.Span), nested.Literal));
    }

    // Escape problems are located at the literal's text, without the delimiters, in literals and interpolation segments alike.
    [Theory]
    [InlineData("let text = \"a\\qb\"", "a\\qb")]
    [InlineData("let text = \"\\u(D800)\"", "\\u(D800)")]
    [InlineData("let n: i32 = 1\nlet text = \"a\\q \\(n) b\"", "a\\q ")]
    [InlineData("let c = '\\q'", "'\\q'")]
    public void EscapeProblemsStayAtTheLiteralText(string source, string text)
    {
        var error = Assert.Single(TestDiagnostics.Of(Analyze(source)));
        Assert.True(error.Code is nameof(DiagnosticCode.UnsupportedEscape_Kd) or nameof(DiagnosticCode.InvalidUnicodeScalar_Kd), error.ToString());
        Assert.Equal(text, error.Text);
    }

    // A missing form is inserted after the closing quote of a string that ends the line.
    [Theory]
    [InlineData("Console.writeLine(\"hello\"")]
    [InlineData("let texts = [\"a\", \"b\"")]
    [InlineData("let texts = [\"a\", \"\"")]
    public void MissingCloserFollowsTheClosingQuote(string source)
    {
        var error = Assert.Single(TestDiagnostics.Of(Analyze(source)));
        Assert.Equal(nameof(DiagnosticCode.MissingSyntax_Kd), error.Code);
        Assert.Equal(new SourceSpan(source.Length, 0), error.Span);
    }

    private static string Text(string source, SourceSpan span) => source.Substring(span.Start, span.Length);

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
