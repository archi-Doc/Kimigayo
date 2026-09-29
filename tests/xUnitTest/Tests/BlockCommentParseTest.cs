// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class BlockCommentParseTest
{
    [Theory]
    [InlineData("let total = 1 /* inline */ + 2")]
    [InlineData("/* inline */ work()")]
    [InlineData("loop\n    /* inline */ work()")]
    [InlineData("call(1, /* inline */ 2)")]
    public void AllowsSingleLineBlockCommentsBetweenTokens(string source)
        => Assert.Empty(TestDiagnostics.Of(Parse(source)));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void UsesNextLinesIndentationAndPreservesStatementBoundaries(string newline)
    {
        var source = "loop\n    work() /* comment\n*/\n    next()\nfinish()".Replace("\n", newline);
        var tree = Parse(source);
        Assert.Empty(TestDiagnostics.Of(tree));
        var items = tree.GeneratedFunction!.Body!.Items;
        Assert.Equal(2, items.Count);
        var loop = Assert.IsType<LoopKoto>(items[0]);
        Assert.Equal(2, loop.Body.Items.Count);
        Assert.Equal("work()", loop.Body.Items[0].ToString());
        Assert.Equal("next()", loop.Body.Items[1].ToString());
        Assert.Equal("finish()", items[1].ToString());
    }

    [Theory]
    [InlineData("/* comment\n*/\nwork()")]
    [InlineData("    /* comment\n*/\nwork()")]
    [InlineData("loop\n/* comment\n*/\n    work()")]
    [InlineData("work() /* comment\n*/")]
    [InlineData("/* comment\n*/ // trailing comment\nwork()")]
    [InlineData("/* comment\n*/ /* another */\nwork()")]
    [InlineData("/* comment\n*/ /* another\n*/\nwork()")]
    [InlineData("call(\n    1, /* comment\n*/\n    2\n)")]
    [InlineData("work() /* comment\n*/\n    .next()")]
    public void AllowsMultilineCommentsWithOrdinaryContinuation(string source)
        => Assert.Empty(TestDiagnostics.Of(Parse(source)));

    [Theory]
    [InlineData("/* comment\n*/ bad()\nnext()")]
    [InlineData("/* comment\r\n*/ bad()\r\nnext()")]
    [InlineData("/* comment\r*/ bad()\rnext()")]
    [InlineData("    /* comment\n*/ bad()\nnext()")]
    [InlineData("/* comment\n*/ /* another */ bad()\nnext()")]
    public void ReportsTrailingCodeAndRecoversAtNextLine(string source)
    {
        var tree = Parse(source);
        var diagnostic = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Equal(nameof(DiagnosticCode.CodeAfterMultilineComment_Kd), diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(source.IndexOf("bad()", StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Equal("next()", Assert.Single(tree.GeneratedFunction!.Body!.Items).ToString());
    }

    [Theory]
    [InlineData("work() /* comment\n*/ bad()\nnext()")]
    [InlineData("work() /* comment\n*/ /* another */ bad()\nnext()")]
    public void DoesNotJoinTrailingCodeToThePrecedingStatement(string source)
    {
        var tree = Parse(source);
        var diagnostic = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Equal(nameof(DiagnosticCode.CodeAfterMultilineComment_Kd), diagnostic.Code);
        Assert.Equal(source.IndexOf("bad()", StringComparison.Ordinal), diagnostic.Span.Start);
        var items = tree.GeneratedFunction!.Body!.Items;
        Assert.Equal(2, items.Count);
        Assert.Equal("work()", items[0].ToString());
        Assert.Equal("next()", items[1].ToString());
    }

    [Theory]
    [InlineData("/* unterminated")]
    [InlineData("/* unterminated\n")]
    [InlineData("/* comment\n*/ /* unterminated")]
    public void ReportsMissingTerminator(string source)
    {
        var diagnostic = Assert.Single(TestDiagnostics.Of(Parse(source)));
        Assert.Equal(nameof(DiagnosticCode.MissingBlockCommentEnd_Kd), diagnostic.Code);
    }
}
