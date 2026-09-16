// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class ConditionOperandContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("var x: i32", "x = 1", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "()", "x = 1", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"", "writeLine(x)", "()", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x = \"s\"", "()", "writeLine(x)", "writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "x = 1", "()", "x = 2", OwnershipFailure.ReassignedLet)]
    public void SourceBranchesContributeFactsAfterTheMissingCondition(string declaration, string yes, string no, string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, yes, no, tail));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("BothWrite", "var x: i32", "x = 1", "x = 2", "let y = x")]
    [InlineData("Initialized", "let x = 1", "()", "()", "let y = x")]
    [InlineData("Repair", "var x = \"s\"", "writeLine(x)", "()", "x = \"new\"\n    writeLine(x)")]
    [InlineData("Transfers", "var x: i32", "return\n        x = 1", "return\n        x = 2", "let y = x")]
    public void CheckingBranchesHaveNoRuntimeExecution(string name, string declaration, string yes, string no, string tail)
        => ScalarEmissionTest.EmitFixture("NeverCondition" + Configuration + name, Source(declaration, yes, no, tail), string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void NoncompletingDefaultConditionRetainsCallerState()
        => ScalarEmissionTest.EmitFixture(
            "NeverCondition" + Configuration + "Default",
            "func value(y?: i32 = (if (loop => continue) => 1 else => 2)) -> i32 => y\nvar x = 1\nwriteLine(\"begin\")\nvalue()\nlet z = x",
            "begin\n",
            timeoutMilliseconds: 200);

    [Fact]
    public void NoncompletingConditionSuppliesNoInitializerResult()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "let x: i32 = if stop() => 1 else => 2\nlet y = x");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void MissingConditionRetainsOuterBorrowUntilAcquisitionEnds()
        => ScalarEmissionTest.EmitFixture(
            "NeverCondition" + Configuration + "Borrow",
            Stop + "func inspect(s: ref/string, ready: bool) => ()\nvar s = \"s\"\ninspect(s, if stop() => true else => false)\ns = \"new\"\nwriteLine(s)",
            string.Empty,
            1,
            "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32\nif stop() => x = 1\nlet y = x")]
    [InlineData("func f(c: bool)\n    var x: i32\n    if c => return\n    else if stop() => x = 1\n    else => x = 2\n    let y = x\nf(false)")]
    public void ImplicitAndEarlierBranchesCannotBeDiscarded(string source)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("NoElse", "var x = 1\nif stop() => x = 2\nlet y = x")]
    [InlineData("ElseIf", "func f(c: bool)\n    var x = 1\n    if c => return\n    else if stop() => x = 2\n    else => x = 3\n    let y = x\nf(false)")]
    public void MissingConditionsNeverExecuteSourceBranches(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverCondition" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void APartiallyTerminatingSourceBranchRetainsTheGuard()
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func f(c: bool, x: string)\n    if stop()\n        if c\n            writeLine(x)\n            return\n    else => ()\n    writeLine(x)\nf(true, \"s\")");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReloadAndWarmMissingConditionChecksAllocateNothing()
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
        => Stop + "func f()\n    " + declaration + "\n    if stop()\n        " + yes + "\n    else\n        " + no + "\n    " + tail + "\nf()";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
