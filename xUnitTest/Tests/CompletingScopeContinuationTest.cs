// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class CompletingScopeContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("var x: i32", "x = 1", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "()", "x = 1", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "writeLine(x)", "()", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "()", "writeLine(x)", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "x = 1", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("var x = (\"s\", \"t\")", "writeLine(x.0)", "()", "writeLine(x.0)", OwnershipFailure.PossiblyMovedUse)]
    public void CompletingBranchesRetainJoinedFacts(string declaration, string yes, string no, string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, yes, no, tail));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("BothWrite", "var x: i32", "x = 1", "x = 2", "let y = x", "")]
    [InlineData("RemainingPart", "var x = (\"s\", \"t\")", "writeLine(x.0)", "()", "writeLine(x.1)", "s\n")]
    [InlineData("Repair", "var x = (\"s\", \"t\")", "writeLine(x.0)", "()", "x.0 = \"new\"\n    let y = x", "s\n")]
    [InlineData("Nested", "var x: i32", "if c => x = 1 else => x = 2", "x = 3", "let y = x", "")]
    public void BranchEffectsExecuteButCheckingSuccessorsDoNot(string name, string declaration, string yes, string no, string tail, string stdout)
        => ScalarEmissionTest.EmitFixture("NeverCompletingScope" + Configuration + name, Source(declaration, yes, no, tail), stdout, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void DefaultLocalBranchesPreserveCallerState()
        => ScalarEmissionTest.EmitFixture(
            "NeverCompletingScope" + Configuration + "Default",
            "func value(c: bool, y?: i32 = (scope: do\n    var n: i32\n    if c => n = 1 else => n = 2\n    loop => continue\n    exit to scope: n\n)) -> i32 => y\nvar x = 1\nwriteLine(\"begin\")\nvalue(true)\nlet y = x",
            "begin\n",
            timeoutMilliseconds: 200);

    [Theory]
    [InlineData("if c => return\n        stop()")]
    [InlineData("if c => stop() else => ()\n        stop()")]
    [InlineData("if c\n            if c => return\n        stop()")]
    public void PartialTerminationStillRequiresAnOuterJoin(string scoped)
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(c: bool, x: i32)\n    do\n        " + scoped + "\n    let y = x\nf(true, 1)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReusedAndReloadedCompletingJoinsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1", "x = 2", "let y = x"));
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var kotonoha = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }));
    }

    private static string Source(string declaration, string yes, string no, string tail)
        => Stop + "func f(c: bool)\n    " + declaration + "\n    do\n        if c\n            " + yes + "\n        else\n            " + no + "\n        stop()\n    " + tail + "\nf(true)";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
