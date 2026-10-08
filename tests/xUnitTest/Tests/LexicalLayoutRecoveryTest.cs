// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class LexicalLayoutRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\t", "\n")]
    [InlineData("  \t", "\n")]
    [InlineData("\t", "\r\n")]
    [InlineData("  \t", "\r")]
    public void InvalidIndentationRetainsTheFunctionBody(string indentation, string newline)
    {
        var source = "func f() -> i32" + newline + indentation + "let n = 1" + newline + "    return n" + newline + "let wrong: i32 = true";
        var c = Analyze(source);
        var errors = TestDiagnostics.Of(c);
        Assert.Equal(["TabIndentation_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf('\t'), 1), errors[0].Span);
        var function = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().Single();
        Assert.Equal(2, function.Body!.Items.Count);
        Assert.Equal("n", Assert.IsType<FieldKoto>(function.Body.Items[0]).NameKoto.IdentifierName);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, TestDiagnostics.Of(c));
    }

    [Theory]
    [InlineData("func f() -> i32\n    if true\n        return 1\n    return 2\nlet n = f()")]
    [InlineData("func take<T>(value: T) -> T => value\nfunc f() -> i32\n    let n = take<\n        i32\n    >(1)\n    return n\nlet n = f()")]
    [InlineData("func f() -> i32\n    let n = (if true\n        yield 1\n    else\n        yield 2\n    )\n    return n\nlet n = f()")]
    [InlineData("func take(value: i32) -> i32 => value\nlet n = take(\n    match true\n        _\n            let value = 7\n            yield value\n)")]
    public void LookaheadUsesTheSameRecoveredIndentation(string validSource)
    {
        Assert.Empty(TestDiagnostics.Of(Analyze(validSource)));
        var source = validSource.Replace("    ", "\t", StringComparison.Ordinal);
        var c = Analyze(source + "\nlet wrong: i32 = true");
        var errors = TestDiagnostics.Of(c);
        var tabLines = source.Split('\n').Count(static x => x.StartsWith('\t'));
        Assert.Equal(tabLines + 1, errors.Length);
        Assert.All(errors.Take(tabLines), static x => Assert.Equal("TabIndentation_Kd", x.Code));
        Assert.Equal("TypeMismatch_Kd", errors[^1].Code);
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
    }

    [Theory]
    [InlineData("\tlet n = 1", "TabIndentation_Kd")]
    [InlineData("let n\t= 1", "InvalidCharacter_Kd")]
    [InlineData("\t// comment\nlet n = 1", "TabIndentation_Kd")]
    [InlineData("\t\nlet n = 1", "TabIndentation_Kd")]
    public void TabsKeepTheirLexicalRole(string source, string code)
    {
        var c = Analyze(source + "\nlet wrong: i32 = true");
        var errors = TestDiagnostics.Of(c);
        Assert.Equal([code, "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf('\t'), 1), errors[0].Span);
    }

    [Fact]
    public void TabsInsideLiteralAndCommentContentsAreUnchanged()
        => Assert.Empty(TestDiagnostics.Of(Analyze("// a\tb\n/* a\tb */\nlet text = \"a\tb\"")));

    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("     ")]
    public void AFailedModifierAndIndentationDoNotLoseTheNextStatement(string indentation)
    {
        var c = Analyze("public\n" + indentation + "func f() => ()\nlet wrong: i32 = true");
        Assert.Equal(["MissingSyntax_Kd", "InvalidIndentation_Kd", "TypeMismatch_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
        // The indented region belongs to the omitted header, but the following root statement is independent.
        Assert.Equal("wrong", Assert.IsType<FieldKoto>(Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items)).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("`")]
    [InlineData("\u007f")]
    [InlineData("\u0301")]
    [InlineData("😀")]
    public void AnInvalidTokenStartIsOneCompleteScalar(string character)
    {
        var source = "let n = " + character + "\nlet wrong: i32 = true";
        var errors = TestDiagnostics.Of(Analyze(source));
        Assert.Equal(["InvalidCharacter_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(8, character.Length), errors[0].Span);
    }

    [Theory]
    [InlineData("e\u0301")]
    [InlineData("a\u200d")]
    [InlineData("name\\suffix")]
    public void AnInvalidAttemptedNameKeepsTheWholeSpelling(string spelling)
    {
        var errors = TestDiagnostics.Of(Analyze("let " + spelling + " = 1\nlet wrong: i32 = true"));
        Assert.Equal(["InvalidIdentifier_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(4, spelling.Length), errors[0].Span);
    }

    [Fact]
    public void SpaceIndentationAndUnicodeNamesExecute()
        => ScalarEmissionTest.EmitFixture("LexicalLayoutValid", "func f() -> i32\n    let 数 = 7\n    return 数\nrequire f() == 7 else => $abort(\"indent\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void OrdinaryIndentationAndUnicodeReuseTokenStorage()
    {
        var c = Compilation.CreateForTest();
        var diagnostics = c.Diagnostics.GetOrAddCollection("lexical-layout");
        var source = new SourceDocument("layout.kimi", "func take<T>(value: T) -> T => value\nfunc f() -> i32\n    let 数 = take<\n        i32\n    >(7)\n    return 数");
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            var tokenizer = new Tokenizer(diagnostics, source);
            try
            {
                tokenizer.ReadAll();
            }
            finally
            {
                tokenizer.Dispose();
            }
        }));
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Theory]
    [InlineData("func f() -> i32\n\tlet n = 1\n    return n", "\t", "TabIndentation_Kd")]
    [InlineData("let n = 😀", "😀", "InvalidCharacter_Kd")]
    public void PublicOutputLocatesTheCauseAndKeepsIndependentErrors(string source, string offending, string code)
    {
        source += "\nlet wrong: i32 = true";
        var path = Path.GetFullPath("lexical-layout.kimi");
        var c = Analyze(source, path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal([code, "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var record = result.Diagnostics[0];
        Assert.Equal(new SourceSpan(source.IndexOf(offending, StringComparison.Ordinal), offending.Length), record.Span);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal(DiagnosticSeverity.Error, record.Severity);
        Assert.Empty(record.Repairs ?? []);
        if (code == "TabIndentation_Kd")
        {
            Assert.Equal("tab in indentation", record.Label);
            Assert.Contains("U+0020 spaces", record.Advice);
        }

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

    private static Compilation Analyze(string source, string path = "lexical-layout.kimi")
    {
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        return c;
    }
}
