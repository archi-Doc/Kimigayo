// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class PartialScopeContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";
    private const string Default = "func value(c: bool, y?: i32 = (do\n    var n: i32\n    do\n        if c\n            n = 1\n            loop => continue\n        n = 2\n        loop => continue\n    n\n)) -> i32 => y\n";

    [Theory]
    [InlineData("let s = \"s\"", "writeLine(s)\n            return", "stop()", "writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "return", "writeLine(s)\n        stop()", "writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("var x: i32", "x = 1\n            return", "stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "x = 2\n        stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x: i32", "x = 1\n            return", "stop()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("var x: i32", "if c\n                x = 1\n                return", "stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "x = 1\n            if c => return", "stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "x = 1\n            return", "if c => return\n        x = 2\n        stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "x = 1\n            return", "do\n            if c => return\n            x = 2\n        stop()", "let y = x", OwnershipFailure.UninitializedUse)]
    public void EveryTerminalPathReachesTheScopeContinuation(string declaration, string yes, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, yes, tail, use));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Initialized", "var x: i32", "x = 1\n            return", "x = 2\n        stop()", "let y = x", "done\n")]
    [InlineData("Nested", "var x: i32", "if c => x = 1 else => x = 3\n            return", "x = 2\n        stop()", "let y = x", "done\n")]
    [InlineData("Deep", "var x: i32", "x = 1\n            if c => return\n            stop()", "x = 2\n        stop()", "let y = x", "done\n")]
    [InlineData("MoveBefore", "let s = \"s\"", "writeLine(s)\n            return", "writeLine(s)\n        stop()", "()", "s\ndone\n")]
    public void ReturnPathsExecuteAndLaterSourceIsCheckingOnly(string name, string declaration, string yes, string tail, string use, string stdout)
        => ScalarEmissionTest.EmitFixture("NeverPartialScope" + Configuration + name, Source(declaration, yes, tail, use) + "\nwriteLine(\"done\")", stdout);

    [Fact]
    public void BranchBorrowsEndBeforeEveryTerminalPath()
        => ScalarEmissionTest.EmitFixture(
            "NeverPartialScope" + Configuration + "Borrow",
            "func inspect(s: ref/string) => ()\n" + Source("var s = \"s\"", "inspect(s)\n            return", "inspect(s)\n        stop()", "s = \"new\"\n    writeLine(s)") + "\nwriteLine(\"done\")",
            "done\n");

    [Fact]
    public void LaterTerminationStillAbortsWithoutRunningCheckingSuccessors()
        => ScalarEmissionTest.EmitFixture(
            "NeverPartialScope" + Configuration + "Abort",
            Source("var x: i32", "x = 1\n            return", "x = 2\n        stop()", "let y = x\n    writeLine(\"bad\")").Replace("f(true)", "f(false)", StringComparison.Ordinal),
            string.Empty,
            1,
            "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("let x: i32\nvalue(true)\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"\nwriteLine(s)\nvalue(false)\nwriteLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void OmittedPartialDefaultDoesNotRestoreCallerFacts(string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Default + tail);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void EveryDefaultDeclarationChecksThePartialPath()
    {
        var c = MinimalEmissionTest.Analyze(Default.Replace("            n = 1\n", string.Empty, StringComparison.Ordinal) + "value(true, 3)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void OmittedPartialDefaultKeepsCallerStateWithoutCompleting(string condition)
        => ScalarEmissionTest.EmitFixture(
            "NeverPartialScope" + Configuration + "Default" + condition,
            Default + "var x = 1\nwriteLine(\"begin\")\nvalue(" + condition + ")\nlet y = x",
            "begin\n",
            timeoutMilliseconds: 200);

    [Theory]
    [InlineData("func f(c: bool, d: bool, s: string)\n    if c\n        if d\n            writeLine(s)\n            return\n        stop()\n    else\n        return\n    writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool, d: bool, s: string)\n    if c\n        return\n    else\n        if d => return\n        writeLine(s)\n        stop()\n    writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool, d: bool)\n    var x: i32\n    if c\n        if d => return\n        x = 1\n        stop()\n    else\n        x = 2\n        return\n    let y = x", OwnershipFailure.UninitializedUse)]
    public void TerminalSelectionJoinsIncludeNestedPartialPaths(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("func f(c: bool, d: bool)\n    var x: i32\n    if c\n        loop\n            if d => exit\n            x = 1\n            exit\n        x = 2\n        stop()\n    else\n        x = 3\n        return\n    let y = x")]
    [InlineData("func f(c: bool, d: bool)\n    var x: i32\n    if c\n        inner: do\n            if d => exit to inner\n            x = 1\n        x = 2\n        stop()\n    else\n        x = 3\n        return\n    let y = x")]
    public void LabeledPartialPathsNeverJoinAsTerminal(string source)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("func f(c: bool, s: string)\n    do\n        if c\n            writeLine(s)\n            return\n        defer => loop => ()\n        stop()\n    writeLine(s)")]
    [InlineData("func f(c: bool, s: string) -> bool\n    let r = s == do\n        if c => return true\n        stop()\n    writeLine(s)\n    r")]
    public void CleanupAndLoanPathsRetainTheirGuard(string source)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void CompletingLoopJoinsItsEarlyReturn()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(c: bool, s: string)\n    do\n        loop\n            if c => return\n            exit\n        stop()\n    writeLine(s)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void LabeledScopePathsPreserveMoveHistory()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(c: bool, s: string)\n    loop\n        do\n            if c => exit\n            writeLine(s)\n            stop()\n        writeLine(s)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReloadAndWarmReuseRetainPartialJoins()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1\n            return", "x = 2\n        stop()", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var kotonoha = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }));
        Assert.True(c.Ownership.Result.IsVerified);
    }

    private static string Source(string declaration, string yes, string tail, string use)
        => Stop + "func f(c: bool)\n    " + declaration + "\n    do\n        if c\n            " + yes + "\n        " + tail + "\n    " + use + "\nf(true)";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
