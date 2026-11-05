// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class DefaultOwnershipTest
{
    // SPEC 3.5, 7.2.3 (R6-check H1, DECISIONS BU-84): a bare move of a prepared argument or of its owned contents in a default, through
    // parentheses, a projection, an aggregate or a body result, is Binding's DefaultArgumentMove_Kd at the Place (the ownership analysis's
    // before H1); a bare move past a borrow the argument holds is no move of its contents (TransferRequired_Kd). Each row: the records.
    [Theory]
    [InlineData("func f(x: (string, i32), y: string = x.0) => ()", "DefaultArgumentMove_Kd x.0")]
    [InlineData("func f(x: ((i32, string), i32), y: string = x.0.1) => ()", "DefaultArgumentMove_Kd x.0.1")]
    [InlineData("func f(x: [2 of string], y: string = x[0]) => ()", "DefaultArgumentMove_Kd x[0]")]
    [InlineData("func f(x: [2 of string], i: isize, y: string = x[i]) => ()", "DefaultArgumentMove_Kd x[i]")]
    [InlineData("struct S\n    public let text: string\nfunc f(x: S, y: string = x.text) => ()", "DefaultArgumentMove_Kd x.text")]
    [InlineData("func f(x: string, y: (string, i32) = (x, 1)) => ()", "DefaultArgumentMove_Kd x")]
    [InlineData("func f(x: string, y: [1 of string] = [x]) => ()", "DefaultArgumentMove_Kd x")]
    [InlineData("func f(x: string, y: string = (label scope: do\n    var local = \"a\"\n    local = x\n    exit to scope local\n)) => ()", "DefaultArgumentMove_Kd x|TransferRequired_Kd local")]
    [InlineData("func f(x: string, y: string = x) => ()", "DefaultArgumentMove_Kd x")]
    [InlineData("func f(x: string, y: string = x) => ()\nf(\"a\", \"b\")", "DefaultArgumentMove_Kd x")]
    [InlineData("func f(x: string ! y: string = x) => ()\nf(\"a\", y: \"b\")", "DefaultArgumentMove_Kd x")]
    [InlineData("func f(x: string, y: string = ((x))) => ()", "DefaultArgumentMove_Kd ((x))")]
    [InlineData("func f(x: string, y: string = (if false => x else => \"ok\")) => ()", "DefaultArgumentMove_Kd x")]
    [InlineData("func f(x: string, y: string = (do => x)) => ()", "DefaultArgumentMove_Kd x")]
    [InlineData("func f(x: string, y: string = (loop => exit x)) => ()", "DefaultArgumentMove_Kd x")]
    [InlineData("contract C\n    func f(x: string, y: string = x)", "DefaultArgumentMove_Kd x")]
    [InlineData("struct S\n    public let text: string\nfunc f(x: ref/S, y: string = x.text) => ()", "TransferRequired_Kd x.text")]
    public void ABareMoveOfAPreparedArgumentIsBindingsRecord(string source, string records)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(records, string.Join("|", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        c.Binding.ReportDiagnostics();
        foreach (var issue in c.Binding.Issues)
        {
            Assert.Contains(TestDiagnostics.Of(issue.Node.CodeContext), x => x.Code == issue.Code.ToString() && x.Span == issue.Node.Span);
        }

        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData("func f(x: string, y: Option<string> = .Some(x@move)) => ()")]
    public void OwnedSubplacesAndAggregateInputsCannotMoveInDefaults(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        var issue = Assert.Single(c.Problems, x => x.Code == DiagnosticCode.DefaultArgumentMove_Kd);
        c.ReportAnalysis();
        Assert.Contains(TestDiagnostics.Of(c.SyntaxOf(issue.Subject).CodeContext), x => x.Code == nameof(DiagnosticCode.DefaultArgumentMove_Kd) && x.Span == c.SyntaxOf(issue.Subject).Span);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData("func f(x: (string, i32), y: i32 = x.1) => ()")]
    [InlineData("func f(x: (string, i32), y: bool = x.0 == \"a\") => ()")]
    [InlineData("func inspect(value: ref/string) -> i32 => 1\nfunc f(x: [2 of string], y: i32 = inspect(x[0])) => ()")]
    [InlineData("func f(x: i32, y: (i32, i32) = (x, x)) => ()")]
    [InlineData("func f(x: i32, y: Option<i32> = .Some(x)) => ()")]
    public void SubplaceCopiesAndInspectionDoNotMove(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.DefaultArgumentMove_Kd);
    }

    // An explicit Move of a prepared argument is the ownership analysis's until H10b (DECISIONS BU-84); a bare one is Binding's (above).
    [Theory]
    [InlineData("func take(value: string) -> i32 => 1\nfunc f(x: string, y: i32 = take(x@move)) => ()")]
    [InlineData("func f(x: string, y: string = x@move@owner/string) => ()")]
    [InlineData("struct S\n    public func take(self: Self) -> i32 => 1\nfunc f(x: S, y: i32 = x@move.take()) => ()")]
    public void DefinitePreparedArgumentMovesAreDeclarationErrors(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(c.Analysis.ErrorCount > 0, string.Join("\n", c.Problems));
        var issue = Assert.Single(c.Problems, x => x.Code == DiagnosticCode.DefaultArgumentMove_Kd);
        Assert.IsType<IdentifierNameKoto>(c.SyntaxOf(issue.Subject));
        Assert.Equal("x", c.SyntaxOf(issue.Subject).SymbolOf()!.Name);
        c.ReportAnalysis();
        var diagnostic = Assert.Single(TestDiagnostics.Of(c.SyntaxOf(issue.Subject).CodeContext), x => x.Code == nameof(DiagnosticCode.DefaultArgumentMove_Kd));
        Assert.Equal(c.SyntaxOf(issue.Subject).Span, diagnostic.Span);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.DoesNotContain(TestDiagnostics.Of(c.SyntaxOf(issue.Subject).CodeContext), x => x.Code == nameof(DiagnosticCode.Unsupported_Kd));
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 7.2.3, 13.5.3: an explicit transfer moves even a Copy prepared argument, and a default closure's `@move` capture entry is
    // the default's own Move. Each is one DefaultArgumentMove_Kd, at the operand or at the closure that holds the entry (the MIR's
    // span, H5), with no unsupported execution beside it.
    [Theory]
    [InlineData("func f(x: i32, y: i32 = x@move) -> i32 => x + y\n", "x")]
    [InlineData("func f(x: (i32, i32), y: i32 = x.0@move) -> i32 => y\n", "x.0")]
    [InlineData("func f<T>(x: T, y: T = x@move) -> T\n    T is Copy\n    return y\n", "x")]
    [InlineData("func runC(offset: i32, action: (i32) -> i32 = func [offset@move] (x) => x + offset) -> i32 => action(1)\n", "func [offset@move] (x) => x + offset")]
    [InlineData("func runG(text: string, action: () -> () = func [text@move] () => ()) -> () => action()\n", "func [text@move] () => ()")]
    public void CopyTransfersOfPreparedSlotsAreDefaultMoves(string declaration, string text)
    {
        var source = declaration + "public func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.DefaultArgumentMove_Kd), text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    [Theory]
    [InlineData("func f(x: i32, y: i32 = x) => ()\nf(1)")]
    [InlineData("func f(x: string, y: bool = x == \"a\") => ()")]
    [InlineData("func f(x: string, y: string = \"independent\") => ()")]
    [InlineData("func f<T>(x: T, y: T = x)\n    T is Copy\n    ()")]
    [InlineData("func inspect(value: ref/string) -> i32 => 1\nfunc f(x: string, y: i32 = inspect(x)) => ()")]
    [InlineData("func f(x: ref/string during a, y: ref/string during a = x) => ()")]
    public void CopyAndInspectionDoNotBecomeDefaultMoveErrors(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Equal(0, c.Analysis.ErrorCount);
    }

    [Fact]
    public void ReanalysisAndReloadDoNotLoseOrDuplicateDefaultErrors()
    {
        // R6-check H1: the bare move is Binding's record, so each rebind and the reload keep exactly one.
        var c = MinimalEmissionTest.Analyze("func f(x: string, y: string = x) => ()");
        for (var i = 0; i < 3; i++)
        {
            Assert.False(c.Bind().IsComplete);
            Assert.Single(c.Binding.Issues, static x => x.Code == DiagnosticCode.DefaultArgumentMove_Kd);
        }

        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        var issue = Assert.Single(restored.Binding.Issues, static x => x.Code == DiagnosticCode.DefaultArgumentMove_Kd);
        Assert.NotSame(c.Binding.Issues[0].Node, issue.Node);
    }
}
