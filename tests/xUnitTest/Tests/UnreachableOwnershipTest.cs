// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 14.10.3 checks unreachable source under a type-checking continuation, which this analysis does not provide: it never
// checks unreachable operations, and a body without another ownership issue reports its first unreachable use of a Local or
// Parameter as the location-only Unsupported_Kd (SPEC 23.3.6.1).
public class UnreachableOwnershipTest
{
    // A deferred body repeated at its dead scope end performs the use already checked on the reachable exit.
    [Theory]
    [InlineData("func f()\n    return\n    let y = 1\n    Console.writeLine(\"dead\")")]
    [InlineData("func f()\n    let x = \"s\"\n    defer => Console.writeLine(x)\n    return\n    let y = 1")]
    public void DeadSourceWithoutVariableUsesVerifies(string source)
    {
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
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
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
    }

    [Theory]
    [InlineData("func f(x: string)\n    loop\n        Console.writeLine(x)\n        $abort(\"stop\")\n    Console.writeLine(x)\nf(\"s\")", "x", 5)]
    [InlineData("func f(x: i32)\n    label work: do\n        defer => loop => ()\n        exit to work\n    let y = x\nf(1)", "x", 5)]
    [InlineData("func f(c: bool, x: i32)\n    var n = 0\n    if c\n        loop\n            n += 1\n    else => return\n    let y = x\nf(true, 1)", "x", 7)]
    [InlineData("func f(x: i32)\n    loop => return\n    let y = x\nf(1)", "x", 3)]
    [InlineData("func f()\n    var x = \"s\"\n    return\n    x = \"new\"\n    Console.writeLine(x)\nf()", "x = \"new\"", 4)]
    [InlineData("var x = 1\nloop\n    x = 2\nlet y = x", "x", 4)]
    [InlineData("var x = 1\nloop\n    x++\nlet y = x", "x", 4)]
    [InlineData("func f() => ()\nvar x = 1\nloop\n    f()\nlet y = x", "x", 5)]
    [InlineData("var x = 1\nloop\n    defer => ()\nlet y = x", "x", 4)]
    public void FirstDeadLocalUseIsUnsupported(string source, string use, int line)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, use, line), (issue.Code, issue.Source.ToString(), LineOf(source, issue.Source.Span.Start)));
    }

    // Another ownership issue of the body suppresses the Unsupported_Kd of its unreachable uses.
    [Theory]
    [InlineData("var n: i32\n    let y = n", OwnershipFailure.UninitializedUse, "n")]
    [InlineData("let n = 1\n    n = 2", OwnershipFailure.ReassignedLet, "n = 2")]
    public void LoopLocalUsesStillRequireOrdinaryOwnershipChecks(string body, OwnershipFailure failure, string at)
    {
        var c = MinimalEmissionTest.Analyze("let x = 1\nloop\n    " + body + "\nlet y = x");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal((failure, at), (issue.Failure, issue.Source.ToString()));
    }

    // A Loan begun inside unreachable code conflicts with a later unreachable operation while the body is built.
    [Fact]
    public void DeadCallsStillRejectConflictingArgumentLoans()
    {
        const string Source = "func inspect(a: ref/string, b: string) => ()\nfunc f()\n    let x = \"s\"\n    return\n    inspect(x, x@move)\nf()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Ownership.Issues);
        Assert.Equal((DiagnosticCode.ComparisonLoanConflict_Kd, "x", Source.IndexOf("x@move", StringComparison.Ordinal)), (issue.Code, issue.Source.ToString(), issue.Source.Span.Start));
    }

    [Fact]
    public void DuplicateDeferredSourceDiagnosticsArePublishedOnce()
    {
        var c = Parse("func f()\n    let x = \"s\"\n    defer => Console.writeLine(x)\n    _ = x@move\n    return\n    let y = 1");
        Assert.False(c.Ownership.Analyze().IsVerified);
        c.Ownership.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        Assert.Single(TestDiagnostics.Of(c));
    }

    private static Compilation Parse(string source)
        => CompilationTestHelper.BindSuccess(source);

    private static string Describe(Compilation c)
        => string.Join("\n", c.Ownership.Issues.Select(x => $"{x.Failure}: {x.Source}")) +
            string.Join("\n", c.Ownership.ControlFlow!.Issues.Select(x => x.Message));

    private static int LineOf(string source, int offset)
        => source.AsSpan(0, offset).Count('\n') + 1;

    [TestClass(DisableParallelization = true)]
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
            Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
            var bindingBytes = AllocationMeasurement.Measure(() => c.Bind());
            var analysisBytes = AllocationMeasurement.Measure(() => c.Ownership.Analyze());
            var combinedBytes = AllocationMeasurement.Measure(() =>
            {
                c.Bind();
                c.Ownership.Analyze();
            });
            Assert.True(
                bindingBytes == 0 && analysisBytes == 0 && combinedBytes == 0,
                $"Binding: {bindingBytes}; analysis: {analysisBytes}; combined: {combinedBytes}");
            Assert.True(c.Ownership.Result.IsVerified, Describe(c));
        }
    }
}
