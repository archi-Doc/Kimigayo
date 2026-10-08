// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class LexicalRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("let c = ('x)")]
    [InlineData("let c = (('x))")]
    [InlineData("let c = ['x]")]
    [InlineData("func take(x: char) => ()\ntake('x)")]
    [InlineData("func f()\n    let c = ('x)\nlet next = 1")]
    public void AnUnterminatedCharOwnsItsEnclosingRecovery(string source)
        => AssertLexicalCause(source, "'", "MissingCharLiteralEnd_Kd");

    [Theory]
    [InlineData("let n = (1 /* tail)")]
    [InlineData("let n = [1 /* tail]")]
    [InlineData("let n = ((1 /* tail))")]
    [InlineData("func take(x: i32) => ()\ntake(/* tail)")]
    [InlineData("let n = /* tail")]
    [InlineData("/* tail")]
    public void AnUnterminatedCommentOwnsItsEnclosingRecovery(string source)
        => AssertLexicalCause(source, "/*", "MissingBlockCommentEnd_Kd");

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void LaterStatementsAndClosersKeepTheirOwnCauses(string newline)
    {
        var source = "let c = ('x)" + newline + "let wrong: i32 = true" + newline + "let n = (1";
        var c = Analyze(source);
        Assert.Equal(["MissingCharLiteralEnd_Kd", "TypeMismatch_Kd", "MissingSyntax_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
        var fields = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FieldKoto>().ToArray();
        Assert.Equal(["c", "wrong", "n"], fields.Select(static x => x.NameKoto.IdentifierName));
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
    }

    [Fact]
    public void EarlierIndependentErrorsSurviveAnUnterminatedComment()
    {
        var c = Analyze("let wrong: i32 = true\nlet n = (1 /* tail)");
        Assert.Equal(["TypeMismatch_Kd", "MissingBlockCommentEnd_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
    }

    [Theory]
    [InlineData("let c = ('x)", "MissingCharLiteralEnd_Kd")]
    [InlineData("let n = (1 /* tail)", "MissingBlockCommentEnd_Kd")]
    public void RecoveryDoesNotCrossSourceBoundaries(string source, string code)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("first.kimi", source));
        c.Kotonoha.AddSource(new SourceDocument("second.kimi", "let n = (1"));
        c.Bind();
        c.Binding.ReportDiagnostics();
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal([code, "MissingSyntax_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal([0, 1], result.Diagnostics.Select(static x => x.Source));
    }

    [Fact]
    public void ClosingTheTokensRestoresNativeAcceptance()
        => ScalarEmissionTest.EmitFixture("CharCommentRecovery", "let c = ('x')\nrequire c == 'x' else => $abort(\"char\")\nlet n = (1 /* tail */)\nrequire n == 1 else => $abort(\"comment\")", string.Empty);

    [Theory]
    [InlineData("let value = ('x)", "'", "MissingCharLiteralEnd_Kd")]
    [InlineData("let value = (1 /* tail)", "/*", "MissingBlockCommentEnd_Kd")]
    public void CliAndLspShowTheLexicalOpening(string source, string opening, string code)
    {
        var path = Path.GetFullPath("lexical.kimi");
        var c = Analyze(source, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        var start = source.IndexOf(opening, StringComparison.Ordinal);
        Assert.Equal(code, error.Code);
        Assert.Equal(new SourceSpan(start, opening.Length), error.Span);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains($"lexical.kimi:1:{start + 1}", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Equal(code, sent.Code);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    private static Compilation Analyze(string source, string? path = null)
    {
        var c = MinimalEmissionTest.Analyze(source, path ?? "lexical.kimi");
        c.Binding.ReportDiagnostics();
        return c;
    }

    private static void AssertLexicalCause(string source, string opening, string code)
    {
        var c = Analyze(source);
        var errors = TestDiagnostics.Of(c);
        var error = Assert.Single(errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(opening, error.Text);
        Assert.Equal(new SourceSpan(source.IndexOf(opening, StringComparison.Ordinal), opening.Length), error.Span);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, TestDiagnostics.Of(c));
    }
}
