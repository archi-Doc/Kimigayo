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

public class DelimiterRecoveryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("([1)", '[', "]")]
    [InlineData("[(1]", '(', ")")]
    public void AnOuterCloserEndsItsGroupingAfterMissingInnerClosers(string expression, char opening, string closer)
    {
        var source = "let n = " + expression + "\nlet next = 2";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal("MissingSyntax_Kd", error.Code);
        var insertion = new SourceSpan(source.IndexOf('1') + 1, 0);
        Assert.Equal(insertion, error.Span);
        Assert.Equal("Missing '" + closer + "'", error.Message);
        Assert.Equal(new SourceSpan(source.IndexOf(opening), 1), Assert.Single(error.Related!).Span);
        var edit = Assert.Single(Assert.Single(error.Repairs!).Edits);
        Assert.Equal(insertion, edit.Span);
        Assert.Equal(closer, edit.Text);
        Assert.Equal(2, c.Kotonoha.GeneratedFunction!.Body!.Items.Count);
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        ScalarEmissionTest.EmitFixture("DelimiterRepair" + (int)opening, source.Insert(edit.Span.Start, edit.Text), string.Empty);
    }

    [Theory]
    [InlineData("(1]", ')', ']')]
    [InlineData("(1])", ')', ']')]
    [InlineData("[1)", ']', ')')]
    public void AWrongKindNamesTheExpectedCloserAndKeepsALaterMatchingCloser(string expression, char expected, char found)
    {
        var source = "let n = " + expression + "\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["ExpectedSyntax_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.Equal($"Expected '{expected}', found '{found}'", errors[0].Message);
        Assert.Equal(new SourceSpan(source.IndexOf(found), 1), errors[0].Span);
        Assert.Equal(new SourceSpan("let n = ".Length, 1), Assert.Single(errors[0].Related!).Span);
        Assert.Null(errors[0].Repairs);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, c.Diagnostics.Finalize(rejected: true).Diagnostics);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(256)]
    public void DeepIntermediateGroupsCloseBeforeTheWrittenOuterToken(int depth)
    {
        var c = Compilation.CreateForTest();
        var source = new SourceDocument("delimiter.kimi", "(" + new string('[', depth) + "1)\nlet next = 2");
        var tokenizer = new Tokenizer(c.Diagnostics.GetOrAddCollection("delimiter"), source);
        try
        {
            tokenizer.ReadAll();
            var missing = 0;
            var written = false;
            foreach (var token in tokenizer.Tokens)
            {
                if (token.IsMissing)
                {
                    Assert.Equal(TokenKind.CloseBracket, token.Kind);
                    Assert.False(written);
                    missing++;
                }
                else if (token.Kind == TokenKind.CloseParenthesis)
                {
                    Assert.False(token.ClosesNothing);
                    Assert.Equal(depth, missing);
                    written = true;
                }
            }

            Assert.True(written);
            Assert.Equal(depth, missing);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    [Fact]
    public void NoOpenGroupingRemainsAnUnmatchedCloser()
    {
        var errors = TestDiagnostics.Of(ParseTestHelper.Parse("let n = 1]\nlet next = 2"));
        Assert.Equal("MisplacedSyntax_Kd", Assert.Single(errors).Code);
    }

    [Fact]
    public void CliAndLspRelateTheWrongKindToItsOpening()
    {
        const string Source = "let n = (1]\nlet wrong: i32 = true";
        var path = Path.GetFullPath("delimiter.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["ExpectedSyntax_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var error = result.Diagnostics[0];
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(new SourceRange(new(0, 10), new(0, 11)), error.Display!.Range);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("delimiter.kimi:1:11", console.Text, StringComparison.Ordinal);
        Assert.Contains("opened here", console.Text, StringComparison.Ordinal);
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
