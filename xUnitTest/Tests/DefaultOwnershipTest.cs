// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class DefaultOwnershipTest
{
    [Theory]
    [InlineData("func f(x: (string, i32), y: string = x.0) => ()")]
    [InlineData("func f(x: ((i32, string), i32), y: string = x.0.1) => ()")]
    [InlineData("func f(x: [2 of string], y: string = x[0]) => ()")]
    [InlineData("func f(x: [2 of string], i: isize, y: string = x[i]) => ()")]
    [InlineData("struct S\n    public let text: string\nfunc f(x: S, y: string = x.text) => ()")]
    [InlineData("func f(x: string, y: (string, i32) = (x, 1)) => ()")]
    [InlineData("func f(x: string, y: [1 of string] = [x]) => ()")]
    [InlineData("func f(x: string, y: Option<string> = .Some(x@move)) => ()")]
    [InlineData("func f(x: string, y: string = (scope: do\n    var local = \"a\"\n    local = x\n    exit to scope: local\n)) => ()")]
    public void OwnedSubplacesAndAggregateInputsCannotMoveInDefaults(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        var issue = Assert.Single(c.Ownership.Issues, x => x.Failure == OwnershipFailure.DefaultArgumentMove);
        c.Ownership.ReportDiagnostics();
        Assert.Contains(issue.Source.CodeContext.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.DefaultArgumentMove_Kd) && x.Span == issue.Source.Span);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("func f(x: (string, i32), y: i32 = x.1) => ()")]
    [InlineData("func f(x: (string, i32), y: bool = x.0 == \"a\") => ()")]
    [InlineData("func inspect(value: ref/string) -> i32 => 1\nfunc f(x: [2 of string], y: i32 = inspect(x[0])) => ()")]
    [InlineData("func f(x: i32, y: (i32, i32) = (x, x)) => ()")]
    [InlineData("func f(x: i32, y: Option<i32> = .Some(x)) => ()")]
    [InlineData("struct S\n    public let text: string\nfunc f(x: ref/S, y: string = x.text) => ()")]
    public void SubplaceCopiesAndInspectionDoNotMove(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.DefaultArgumentMove);
    }

    [Theory]
    [InlineData("func f(x: string, y: string = x) => ()")]
    [InlineData("func f(x: string, y: string = x) => ()\nf(\"a\", \"b\")")]
    [InlineData("func f(x: string ! y: string = x) => ()\nf(\"a\", y: \"b\")")]
    [InlineData("func f(x: string, y: string = ((x))) => ()")]
    [InlineData("func f(x: string, y: string = (if false => x else => \"ok\")) => ()")]
    [InlineData("func f(x: string, y: string = (do => x)) => ()")]
    [InlineData("func f(x: string, y: string = (loop => exit x)) => ()")]
    [InlineData("func take(value: string) -> i32 => 1\nfunc f(x: string, y: i32 = take(x@move)) => ()")]
    [InlineData("contract C\n    func f(x: string, y: string = x)")]
    [InlineData("func f(x: string, y: string = x@owner) => ()")]
    [InlineData("struct S\n    public func take(self: Self) -> i32 => 1\nfunc f(x: S, y: i32 = x@move.take()) => ()")]
    public void DefinitePreparedArgumentMovesAreDeclarationErrors(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(c.Ownership.Result.ErrorCount > 0, string.Join("\n", c.Ownership.Issues));
        var issue = Assert.Single(c.Ownership.Issues, x => x.Failure == OwnershipFailure.DefaultArgumentMove);
        Assert.IsType<IdentifierNameKoto>(issue.Source);
        Assert.Equal("x", issue.Source.BoundSymbol!.Name);
        c.Ownership.ReportDiagnostics();
        var diagnostic = Assert.Single(issue.Source.CodeContext.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.DefaultArgumentMove_Kd));
        Assert.Equal(issue.Source.Span, diagnostic.Span);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Entry.Severity);
        Assert.False(c.Emission.Validate(out _));
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
        Assert.Equal(0, c.Ownership.Result.ErrorCount);
    }

    [Fact]
    public void ReanalysisAndReloadDoNotLoseOrDuplicateDefaultErrors()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: string, y: string = x) => ()");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.False(c.Ownership.Analyze().IsVerified);
            Assert.Single(c.Ownership.Issues, x => x.Failure == OwnershipFailure.DefaultArgumentMove);
        }

        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        Assert.False(restored.Ownership.Analyze().IsVerified);
        var issue = Assert.Single(restored.Ownership.Issues, x => x.Failure == OwnershipFailure.DefaultArgumentMove);
        Assert.NotSame(c.Ownership.Issues[0].Source, issue.Source);
    }
}
