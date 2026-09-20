// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class ScopedConditionalContinuationTest
{
    private const string Default = "func value(c?: bool, y?: i32 = (scope: do\n    var n: i32\n    if c\n        loop => continue\n        n = 1\n    else\n        loop => continue\n        n = 2\n    exit to scope: n\n)) -> i32 => y\n";
    private const string MissingConditionDefault = "func value(c?: bool, y?: i32 = (do => if (loop => continue) => 1 else => 2)) -> i32 => y\n";
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void OmittedDefaultKeepsCallerStateWithoutCompleting(string condition)
        => ScalarEmissionTest.EmitFixture("NeverScopedConditional" + Configuration + condition, Default + "var x = 1\nConsole.writeLine(\"begin\")\nvalue(" + condition + ")\nlet y = x", "begin\n", timeoutMilliseconds: 200);

    [Theory]
    [InlineData("let x: i32\nvalue(true)\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = 1\nvalue(false)\nx = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"\nConsole.writeLine(s)\nvalue(true)\nConsole.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void OmittedDefaultCannotRestoreCallerFacts(string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Default + tail);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("value(true)")]
    [InlineData("value(false)")]
    [InlineData("value(true, 3)")]
    public void EveryDefaultDeclarationChecksBothTerminalBranches(string call)
    {
        var c = MinimalEmissionTest.Analyze(Default.Replace("        n = 2\n", string.Empty, StringComparison.Ordinal) + call);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Console.writeLine(s)\n            return", "return")]
    [InlineData("return", "Console.writeLine(s)\n            return")]
    public void ScopedBranchMovesReachTheOuterContinuation(string yes, string no)
    {
        var c = MinimalEmissionTest.Analyze("func f(c?: bool, s?: string)\n    do\n        if c\n            " + yes + "\n        else\n            " + no + "\n    Console.writeLine(s)");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void NestedScopesRetainTheJoinedCheckingAssignments()
        => ScalarEmissionTest.EmitFixture(
            "NeverScopedConditional" + Configuration + "Nested",
            "func f(c?: bool)\n    var x: i32\n    do\n        do\n            if c\n                return\n                x = 1\n            else\n                return\n                x = 2\n    let y = x\n    Console.writeLine(\"bad\")\nf(true)\nf(false)\nConsole.writeLine(\"done\")",
            "done\n");

    [Theory]
    [InlineData(Default)]
    [InlineData(MissingConditionDefault)]
    public void ReloadAndWarmReuseRetainScopedJoins(string declaration)
    {
        var c = MinimalEmissionTest.Analyze(declaration + "var x = 1\nvalue(true)\nlet y = x");
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

    [Theory]
    [InlineData("let x: i32 = do => if stop() => 1 else => 2\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32\ndo\n    if stop() => x = 1\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = \"s\"\ndo\n    if stop() => Console.writeLine(x) else => ()\nConsole.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    public void ScopedMissingConditionsRetainSourceFacts(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("BothWrite", "var x: i32\ndo\n    if stop() => x = 1 else => x = 2\nlet y = x")]
    [InlineData("NoElse", "var x = 1\ndo\n    if stop() => ()\nlet y = x")]
    [InlineData("Borrow", "func inspect(s?: ref/string, b?: bool) => ()\nvar s = \"s\"\ninspect(s, do => if stop() => true else => false)\ns = \"new\"\nConsole.writeLine(s)")]
    public void ScopedMissingConditionsHaveNoRuntimeSuccessor(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverScopedCondition" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void ScopedDefaultConditionDoesNotInventACallerResult()
        => ScalarEmissionTest.EmitFixture("NeverScopedCondition" + Configuration + "Default", MissingConditionDefault + "var x = 1\nConsole.writeLine(\"begin\")\nvalue(true)\nlet y = x", "begin\n", timeoutMilliseconds: 200);

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
