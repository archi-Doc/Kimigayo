// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 14.10.3 checks unreachable source under a type-checking continuation: since the MIR decides acceptance (H5, DECISIONS R5-M182),
// an unreachable operation is checked as its reachable twin (R5-M159's twin rule), so a dead use of a Local or Parameter has no record
// and the program is generated. The old analysis's location-only Unsupported_Kd at a body's first unreachable use (SPEC 23.1.1) went
// with it (1a, the owner's list of 2026-11-03).
public class UnreachableOwnershipTest
{
    // A deferred body repeated at its dead scope end performs the use already checked on the reachable exit.
    [Theory]
    [InlineData("func f()\n    return\n    let y = 1\n    Console.writeLine(\"dead\")")]
    [InlineData("func f()\n    let x = \"s\"\n    defer => Console.writeLine(x)\n    return\n    let y = 1")]
    public void DeadSourceWithoutVariableUsesVerifies(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("func stop() -> Never => stop()\nfunc f() -> i32 => stop()")]
    [InlineData("func stop() -> Never => stop()\nfunc f() -> i32\n    stop()")]
    [InlineData("func f() -> i32 => loop => ()")]
    [InlineData("func f() -> i32\n    loop => ()")]
    [InlineData("func f() -> i32\n    return 0\n    loop => ()")]
    public void NonCompletingBodiesDoNotRequireSyntheticResults(string source)
    {
        var c = Parse(source);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A dead use after a body that cannot complete (a loop, `$abort`, `return`, a `defer` that cannot complete) or after a top-level loop
    // has no record, and the program is generated: each declared function and the top-level statements.
    [Theory]
    [InlineData("func f(x: string)\n    loop\n        Console.writeLine(x)\n        $abort(\"stop\")\n    Console.writeLine(x)\nf(\"s\")")]
    [InlineData("func f(x: i32)\n    label work: do\n        defer => loop => ()\n        exit to work\n    let y = x\nf(1)")]
    [InlineData("func f(c: bool, x: i32)\n    var n = 0\n    if c\n        loop\n            n += 1\n    else => return\n    let y = x\nf(true, 1)")]
    [InlineData("func f(x: i32)\n    loop => return\n    let y = x\nf(1)")]
    [InlineData("func f()\n    var x = \"s\"\n    return\n    x = \"new\"\n    Console.writeLine(x)\nf()")]
    [InlineData("var x = 1\nloop\n    x = 2\nlet y = x")]
    [InlineData("var x = 1\nloop\n    x++\nlet y = x")]
    [InlineData("func f() => ()\nvar x = 1\nloop\n    f()\nlet y = x")]
    [InlineData("var x = 1\nloop\n    defer => ()\nlet y = x")]
    public void DeadLocalUsesAreAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Empty(c.Problems);
        MinimalEmissionTest.GeneratedBodies(source, OutputKind.Application, accepted: true);
    }

    // The loop body's own ownership issue is the body's one record; the dead use after the loop has none.
    [Theory]
    [InlineData("var n: i32\n    let y = n", DiagnosticCode.UninitializedPlace_Kd, "n")]
    [InlineData("let n = 1\n    n = 2", DiagnosticCode.ReassignedLet_Kd, "n = 2")]
    public void LoopLocalUsesStillRequireOrdinaryOwnershipChecks(string body, DiagnosticCode failure, string at)
    {
        var c = MinimalEmissionTest.Analyze("let x = 1\nloop\n    " + body + "\nlet y = x");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var issue = Assert.Single(c.Problems);
        Assert.Equal((failure, at), (issue.Code, c.SyntaxOf(issue.Subject).ToString()));
    }

    // A Loan begun inside unreachable code conflicts with a later unreachable operation while the body is built.
    [Fact]
    public void DeadCallsStillRejectConflictingArgumentLoans()
    {
        const string Source = "func inspect(a: ref/string, b: string) => ()\nfunc f()\n    let x = \"s\"\n    return\n    inspect(x, x@move)\nf()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var issue = Assert.Single(c.Problems);
        Assert.Equal((DiagnosticCode.ComparisonLoanConflict_Kd, "x", Source.IndexOf("x@move", StringComparison.Ordinal)), (issue.Code, c.SyntaxOf(issue.Subject).ToString(), c.SyntaxOf(issue.Subject).Span.Start));
    }

    [Fact]
    public void DuplicateDeferredSourceDiagnosticsArePublishedOnce()
    {
        var c = Parse("func f()\n    let x = \"s\"\n    defer => Console.writeLine(x)\n    _ = x@move\n    return\n    let y = 1");
        Assert.False(c.Analyze().IsVerified);
        c.ReportAnalysis();
        c.ReportAnalysis();
        Assert.Single(TestDiagnostics.Of(c));
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
        public void WarmCheckingAndBindingAllocateNothing(int count)
        {
            // The dead steps scale the scan; the final dead return repeats the reachable deferred read, so the replica check runs.
            var source = new StringBuilder("func f()\n    let x = \"s\"\n    defer => Console.writeLine(x)\n    return\n");
            for (var i = 0; i < count; i++)
            {
                source.Append("    var s").Append(i).Append(" = \"s\"\n    Console.writeLine(\"s\")\n");
            }

            source.Append("    return\n");
            var c = Parse(source.ToString());
            Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
            var bindingBytes = AllocationMeasurement.Measure(() => c.Bind());
            var analysisBytes = AllocationMeasurement.Measure(() => c.Analyze());
            var combinedBytes = AllocationMeasurement.Measure(() =>
            {
                c.Bind();
                c.Analyze();
            });
            Assert.True(
                bindingBytes == 0 && analysisBytes == 0 && combinedBytes == 0,
                $"Binding: {bindingBytes}; analysis: {analysisBytes}; combined: {combinedBytes}");
            Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        }
    }
}
