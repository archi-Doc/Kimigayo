// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class LexicalBoundaryTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("0abc")]
    [InlineData("0_")]
    [InlineData("1_2")]
    [InlineData("0xFF")]
    [InlineData("1e2")]
    [InlineData("123日本語")]
    public void TupleIndexNameContinuationsFormOneMalformedNumber(string index)
    {
        var source = "let t = (1, 2)\nlet n = t." + index;
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.InvalidNumericLiteral_Kd), error.Code);
        Assert.Equal(index, error.Text);
        Assert.Equal(new SourceSpan(source.Length - index.Length, index.Length), error.Span);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("t.0.1")]
    [InlineData("t. 0. 1")]
    [InlineData("t./* index */0.1")]
    public void TupleIndicesKeepTheirDecimalBoundaries(string expression)
        => ScalarEmissionTest.EmitFixture("TupleIndexBoundary" + expression.Length, "let t = ((2, 3), 4)\nrequire " + expression + " == 3 and 0.1 < 1.0 else => $abort(\"index\")", string.Empty);

    [Fact]
    public void AStandaloneComparisonTokenIsNotAGenericCloser()
    {
        var c = Compilation.CreateForTest();
        var source = new SourceDocument("boundary.kimi", ">=");
        var context = new CodeContext(c.Kotonoha, sourceDocument: source);
        var tokenizer = new Tokenizer(context.DiagnosticCollection, source);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(context, ref tokenizer);
            Assert.False(reader.TryConsumeTypeClose(out _, report: false));
            Assert.Equal(TokenKind.GreaterThanEquals, reader.CurrentTokenKind);
            Assert.Equal(new SourceSpan(0, 2), reader.CurrentTokenRange);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    [Fact]
    public void ShiftAssignmentCanCloseTwoGenericLevelsWithoutMutatingTokens()
    {
        var c = Compilation.CreateForTest();
        var source = new SourceDocument("boundary.kimi", ">>=");
        var context = new CodeContext(c.Kotonoha, sourceDocument: source);
        var tokenizer = new Tokenizer(context.DiagnosticCollection, source);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(context, ref tokenizer);
            Assert.True(reader.TryConsumeTypeClose(out var first));
            Assert.Equal(new SourceSpan(0, 1), first);
            Assert.Equal(TokenKind.GreaterThanEquals, reader.CurrentTokenKind);
            Assert.True(reader.TryConsumeTypeClose(out var second));
            Assert.Equal(new SourceSpan(1, 1), second);
            Assert.Equal(TokenKind.Equals, reader.CurrentTokenKind);
            Assert.Equal(TokenKind.GreaterThanGreaterThanEquals, tokenizer.Tokens[0].Kind);
            Assert.Equal(new SourceSpan(0, 3), tokenizer.Tokens[0].Span);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    [Theory]
    [InlineData("let a: Array<i32>= []")]
    [InlineData("func f<T>= ()")]
    public void GenericDeclarationsRequireTheirOwnClosingToken(string source)
    {
        var tree = Parse(source + "\nlet next = 1");
        var error = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), error.Code);
        Assert.Equal(">=", error.Text);
        Assert.Contains("'>'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("let a: Array<i32> = []")]
    [InlineData("let a: Array<Array<i32>>= []")]
    [InlineData("let a = f<i32>>= 4")]
    [InlineData("let a = value >= 4\nlet b = value >> 2\nvalue >>= 1")]
    public void OtherPunctuationBoundariesRemainValid(string source) => AssertValid(Parse(source));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void NumericBoundariesReuseLexicalStorage()
    {
        var c = Compilation.CreateForTest();
        var diagnostics = c.Diagnostics.GetOrAddCollection("boundaries");
        var source = new SourceDocument("boundary.kimi", "let t = ((2, 3), 4)\nlet n = t.0.1\nlet a: Array<Array<i32>>= []\nlet c = n >= 0.1");
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

    [Fact]
    public void TupleIndexFailureKeepsItsRangeAndIndependentErrorsInAdapters()
    {
        const string Source = "let t = (1, 2)\nlet n = t.0abc\nlet wrong: i32 = true";
        var path = Path.GetFullPath("boundary.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["InvalidNumericLiteral_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        Assert.Equal(new SourceRange(new(1, 10), new(1, 14)), result.Diagnostics[0].Display!.Range);
        Assert.Equal(DiagnosticCategory.Language, result.Diagnostics[0].Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("boundary.kimi:2:11", console.Text, StringComparison.Ordinal);
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
