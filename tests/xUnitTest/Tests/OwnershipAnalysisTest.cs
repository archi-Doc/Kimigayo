// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class OwnershipAnalysisTest
{
    [Theory]
    [InlineData("Console.writeLine(\"Hello world\")")]
    [InlineData("public func main()\n    let message = \"Hello world\"\n    Console.writeLine(message)")]
    [InlineData("let x: i32\nx = 1\nlet y = x\nlet z = x")]
    [InlineData("var s = \"a\"\nConsole.writeLine(s)\ns = \"b\"\nConsole.writeLine(s)")]
    [InlineData("func echo(x: string) -> string => x@move\nConsole.writeLine(echo(\"x\"))")]
    [InlineData("func echo(x: string) -> string\n    return x@move\nConsole.writeLine(echo(\"x\"))")]
    [InlineData("let s = \"a\"\nConsole.writeLine(s)\nConsole.writeLine(s)")]
    [InlineData("func f(c: bool)\n    let s: string\n    if c\n        s = \"a\"\n    else\n        s = \"b\"\n    Console.writeLine(s)")]
    [InlineData("func f(c: bool)\n    while c\n        let s = \"a\"\n        Console.writeLine(s)")]
    [InlineData("func f(c: bool)\n    while c\n        let s = \"a\"\n        continue")]
    [InlineData("func f()\n    while true\n        let s = \"a\"\n        exit")]
    [InlineData("func f()\n    while true\n        ()")]
    [InlineData("let s = \"a\"\nif s == \"a\"\n    Console.writeLine(s)")]
    [InlineData("#if false\nfunc excluded()\n    defer => Console.writeLine(\"later\")\nConsole.writeLine(\"selected\")")]
    [InlineData("func sink<T>(x: T) => ()\nfunc twice<T>(x: T)\n    T is Copy\n    sink(x)\n    sink(x)")]
    [InlineData("defer => Console.writeLine(\"later\")")]
    public void SupportedProgramsVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("let s: string\nConsole.writeLine(s)", DiagnosticCode.UninitializedPlace_Kd)]
    [InlineData("let s = \"a\"\n_ = s@move\nConsole.writeLine(s)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("let s = \"a\"\n_ = s@move\nif s == \"x\"\n    ()", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("let s = \"a\"\ns = \"b\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("let s = \"a\"\nConsole.writeLine(s)\ns = \"b\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("func f(c: bool)\n    var s: string\n    if c\n        s = \"a\"\n    Console.writeLine(s)", DiagnosticCode.UninitializedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let s = \"a\"\n    if c\n        _ = s@move\n    Console.writeLine(s)", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let s: string\n    if c\n        s = \"a\"\n    s = \"b\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("func f(c: bool)\n    let s = \"a\"\n    while c\n        _ = s@move", DiagnosticCode.MovedPlace_Kd)]
    [InlineData("func f(c: bool)\n    let s: string\n    while c\n        s = \"a\"", DiagnosticCode.ReassignedLet_Kd)]
    [InlineData("let s: string\nif true\n    s = \"a\"\nConsole.writeLine(s)", DiagnosticCode.UninitializedPlace_Kd)]
    public void StateErrorsAreDiagnosedAfterConvergence(string source, DiagnosticCode failure)
    {
        var c = Parse(source);
        Assert.False(c.Analyze().IsVerified);
        Assert.Contains(c.Problems, x => x.Code == failure);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssignment_Kd);
    }

    // SPEC 3.5 (R6-check H1, DECISIONS BU-84): a bare move of a non-Copy Place is Binding's TransferRequired_Kd, not a state error.
    [Fact]
    public void ABareMoveIsBindingsRecord()
        => Assert.Equal(
            [(DiagnosticCode.TransferRequired_Kd, "s")],
            MinimalEmissionTest.Analyze("let s = \"a\"\nlet taken = s").Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));

    // A bare move of a temporary's part is invalid (SPEC 3.5: a bare non-Copy Place, a part of a temporary by 3.4.1 and 3.6), Binding's
    // TransferRequired_Kd, which rejects the program. The expectation that H5 restores the old analysis's Unsupported_Kd here is withdrawn:
    // that record claimed a valid form unimplemented (DECISIONS Q13; H3b round 0).
    [Theory]
    [InlineData("let moved = (\"first\", \"last\").0")]
    public void UnsupportedOwnershipCannotBecomeVerified(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal([(DiagnosticCode.TransferRequired_Kd, "(\"first\", \"last\").0")], c.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));
        Assert.False(c.Accepts);
    }

    [Fact]
    public void OwnedDefaultsNowVerifyThroughTheOrdinaryPipeline()
    {
        var c = Parse("func f(x: string = \"x\") => ()\nf()");
        var result = c.Analyze();
        Assert.True(result.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // The implicit Borrow of a moved local is checked: the MIR's published record is MovedPlace_Kd at the borrowed `s`.
    [Fact]
    public void ImplicitBorrowIsRecordedAndChecksMovedState()
    {
        const string Source = "func show(s: ref/string) => ()\nlet s = \"a\"\n_ = s@move\nshow(s)";
        var c = Parse(Source);
        c.Analyze();
        c.ReportAnalysis();
        var record = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal((nameof(DiagnosticCode.MovedPlace_Kd), MirTargetSide.At("(s)", "s").Resolve(Source)), (record.Code, record.Span));
    }

    [Theory]
    [InlineData("func sink<T>(x: T) => ()\nfunc twice<T>(x: T)\n    sink(x@move)\n    sink(x@move)", false)]
    [InlineData("func sink<T>(x: T) => ()\nfunc once<T>(x: T)\n    sink(x@move)", true)]
    public void UnknownCopyIsCheckedAtTheGenericDefinition(string source, bool valid)
    {
        var c = Parse(source);
        var result = c.Analyze();
        // The Copy-unproven parameter keeps its generic Type; its x@move transfer is checked as a Move (SPEC 13.5.3; its MIR:
        // tests/mir/generics.kimi `wrapSome`).
        Assert.True(valid == result.IsVerified, MinimalEmissionTest.Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Problems, x => x.Code == DiagnosticCode.MovedPlace_Kd);
        }
    }

    [Theory]
    [InlineData("func sink<T>(x: T) => ()\nfunc once<T>(x: T)\n    sink(x)")]
    [InlineData("func once<T>(x: T) -> T => x")]
    public void BareCopyUnprovenGenericValueRequiresATransfer(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        var bound = c.Bind().IsComplete;
        Assert.False(bound && c.Analyze().IsVerified);
        Assert.True(
            c.Binding.Issues.Any(x => x.Code == DiagnosticCode.TransferRequired_Kd) || c.Problems.Any(x => x.Code == DiagnosticCode.TransferRequired_Kd),
            string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")) + MinimalEmissionTest.Describe(c));
    }

    // A return secures its result before the cleanup and the delivery (its MIR: tests/mir/scopes.kimi `securedReturn`).
    [Fact]
    public void ReturnSecuresTheResultBeforeCleanupAndDelivery()
    {
        var c = Parse("func f(s: string) -> string\n    return s@move");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A return inside an argument destroys the inner local before the outer temporary (its MIR: tests/mir/scopes.kimi
    // `returnInArgument`), whatever the newlines.
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\r", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\r", true)]
    public void ReturnInArgumentCleansInnerLocalBeforeOuterTemporary(string newline, bool trailingNewline)
    {
        var source = "func makeText() -> string => \"outer\"\nfunc consume(a: string, b: i32) => ()\nfunc use(c: bool)\n    consume(\n        makeText(),\n        if c\n            let inner = \"inner\"\n            return\n        else => 1\n    )";
        var c = Parse(source.Replace("\n", newline) + (trailingNewline ? newline : string.Empty));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        if (newline != "\n" || trailingNewline)
        {
            Assert.Equal(MirDumpTest.Dump(Parse(source)), MirDumpTest.Dump(c));
        }
    }

    [Fact]
    public void RebindInvalidatesAllVerification()
    {
        var c = Parse("let s = \"a\"\nConsole.writeLine(s)");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "let other: string\nConsole.writeLine(other)");
        c.Bind();
        Assert.False(c.Analysis.IsVerified);
        Assert.False(c.Analyze().IsVerified);
    }

    [Fact]
    public void BodylessRequirementsAreNotOwnershipBodies()
    {
        var c = Parse("contract Named\n    func name(self: ref/Self) -> i32\nConsole.writeLine(\"x\")");
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("let x: i32\nx = 1", true)]
    [InlineData("let x = 1\nx = 2", false)]
    [InlineData("let s: string\nConsole.writeLine(s)", false)]
    [InlineData("let s = \"a\"\nConsole.writeLine(s)\nConsole.writeLine(s)", true)]
    [InlineData("let s = \"a\"\nlet taken = s@move\nConsole.writeLine(s)", false)]
    [InlineData("let moved = (\"first\", \"last\").0", false)]
    [InlineData("Console.writeLine(missing)", false)]
    public async Task ProjectBuildRequiresOwnershipVerification(string source, bool expected)
    {
        var c = Compilation.CreateForTest();
        c.Project.AddSource("ownership.kimi", source);
        Assert.Equal(expected, await c.Project.Check(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("func f(x: i32)\n    x = 2", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData("let x: i32\nfunc f()\n    x = 2", DiagnosticCode.InvalidCaptureBinding_Kd)]
    [InlineData("let x = 1\nx++", DiagnosticCode.InvalidAssignment_Kd)]
    public void StructuralAssignmentErrorsRemainInBinding(string source, DiagnosticCode code)
    {
        // Check's verdict: Binding's record rejects; the analysis verdict is the MIR's (DECISIONS R5-M185; Q13, H3b round 0).
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
        c.Analyze();
        Assert.False(c.Accepts);
    }

    private static Compilation Parse(string source)
        => CompilationTestHelper.BindSuccess(source);

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(32)]
        [InlineData(128)]
        public void WarmBindingAndAnalysisReuseStorage(int count)
        {
            var source = new System.Text.StringBuilder("func f(c: bool)\n");
            for (var i = 0; i < count; i++)
            {
                source.Append("    var s").Append(i).Append(" = \"a\"\n    if c\n        Console.writeLine(s").Append(i).Append(")\n");
            }

            var c = Parse(source.ToString());
            c.Bind();
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                c.Bind();
                c.Analyze();
            }));
        }
    }
}
