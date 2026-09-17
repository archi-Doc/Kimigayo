// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class NeverContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";
    private const string ScalarStop = Stop + "func value(x: i32) -> i32 => x\nfunc truth(x: i32) -> bool => true\n";

    [Theory]
    [InlineData("let n: i32 = do => loop => continue\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"\nConsole.writeLine(s)\ndo => loop => ()\nConsole.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(y?: i32 = (scope: do\n    let n: i32 = do => loop => continue\n    exit to scope: n\n)) => ()\nf(3)", OwnershipFailure.UninitializedUse)]
    public void TransparentDivergentScopesPreserveOwnershipFacts(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Nested", "var n: i32 = do => do => loop => continue\nn = 3\nlet y = n")]
    [InlineData("Default", "func f(y?: i32 = (do => loop => continue)) -> i32 => y\nvar n: i32 = f()\nn = 3\nlet y = n")]
    public void TransparentDivergentScopesHaveNoRuntimeSuccessor(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverWrapper" + Configuration + name, "Console.writeLine(\"begin\")\n" + source, "begin\n", timeoutMilliseconds: 200);

    [Theory]
    [InlineData("value(stop()) + 1", "i32")]
    [InlineData("1 + value(stop())", "i32")]
    [InlineData("-value(stop())", "i32")]
    [InlineData("+value(stop())", "i32")]
    [InlineData("not truth(stop())", "bool")]
    [InlineData("value(stop()) < 1", "bool")]
    public void MissingOperatorOperandsSupplyNoInitializer(string expression, string type)
    {
        var c = MinimalEmissionTest.Analyze(ScalarStop + "let n: " + type + " = " + expression + "\nlet y = n");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("f()")]
    [InlineData("f(3)")]
    public void DefaultOperatorCannotInitializeFromAMissingOperand(string call)
    {
        var c = MinimalEmissionTest.Analyze("func f(y?: i32 = (scope: do\n    let n: i32 = 1 << (loop => continue)\n    exit to scope: n\n)) => ()\n" + call);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void LaterOperandCheckingEffectsRemainAvailable()
        => ScalarEmissionTest.EmitFixture("NeverOperand" + Configuration + "Effects", ScalarStop + "var x: i32\nvar n: i32 = value(stop()) + (scope: do\n    x = 2\n    exit to scope: 1\n)\nn = 3\nlet y = x", string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void DivergentOperatorSkipsCalleeAndLaterOutput()
        => ScalarEmissionTest.EmitFixture("NeverOperand" + Configuration + "Divergence", "Console.writeLine(\"begin\")\nvar n: i32 = 1 << (loop => continue)\nn = 3\nlet y = n", "begin\n", timeoutMilliseconds: 200);

    [Theory]
    [InlineData("func value(x: i32) -> i32 => x\nlet n: i32 = value(stop())\nlet y = n")]
    [InlineData("func value(x: i32) -> i32 => x\nlet n: i32 = value(value(stop()))\nlet y = n")]
    [InlineData("func value(x: i32) -> string => \"value\"\nlet n: string = value(stop())\nConsole.writeLine(n)")]
    [InlineData("func value(x: i32) -> i32 => x\nvar n: i32\nn = value(stop())\nlet y = n")]
    [InlineData("func value(x?: i32 = (loop => continue)) -> i32 => x\nlet n: i32 = value()\nlet y = n")]
    [InlineData("func value(x?: i32 = (loop => continue), y?: i32 = 1) -> i32 => y\nlet n: i32 = value()\nlet y = n")]
    [InlineData("func value(s: ref/string, x: i32) -> i32 => x\nvar s = \"s\"\nlet n: i32 = value(s, stop())\ns = \"new\"\nlet y = n")]
    public void IncompleteArgumentAcquisitionSuppliesNoCallerValue(string source)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.UninitializedUse);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Reinitialize", "func value(x: i32) -> i32 => x\nvar n: i32 = value(stop())\nn = 2\nlet y = n")]
    [InlineData("LaterAssignment", "func value(x: i32, y: ()) -> i32 => x\nvar n: i32\nvalue(stop(), n = 2)\nlet y = n")]
    [InlineData("Borrow", "func value(s: ref/string, x: i32) -> i32 => x\nvar s = \"s\"\nvalue(s, stop())\ns = \"new\"\nConsole.writeLine(s)")]
    public void IncompleteCallsRetainCheckingEffectsWithoutExecuting(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverAcquisition" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void IncompleteDefaultRetainsCheckingBorrowRelease()
        => ScalarEmissionTest.EmitFixture(
            "NeverAcquisition" + Configuration + "BorrowDefault",
            "func value(s: ref/string, x?: i32 = (loop => continue)) -> i32 => x\nvar s = \"s\"\nConsole.writeLine(\"begin\")\nvalue(s)\ns = \"new\"\nConsole.writeLine(s)",
            "begin\n",
            timeoutMilliseconds: 200);

    [Theory]
    [InlineData("let n: i32 = loop => continue\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let n: i32 = loop => ()\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let n = 1\nloop => continue\nn = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"\nConsole.writeLine(s)\nloop => ()\nConsole.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    return\n    let n: i32 = loop => continue\n    let y = n", OwnershipFailure.UninitializedUse)]
    public void StateNeutralLoopsPreserveInitializationAndMoveFacts(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Continue", "let n = 1\nloop => continue\nlet y = n")]
    [InlineData("Unit", "let n = 1\nloop => ()\nlet y = n")]
    [InlineData("Initializer", "var n: i32 = loop => continue\nn = 2\nlet y = n")]
    [InlineData("Checking", "func f()\n    return\n    var n: i32 = loop => continue\n    n = 2\n    let y = n\nf()\nloop => ()")]
    public void EmitsStateNeutralLoopContinuations(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverLoop" + Configuration + name, "Console.writeLine(\"begin\")\n" + source, "begin\n", timeoutMilliseconds: 200);

    [Theory]
    [InlineData("let n: i32 = stop()\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let n: i32\nstop()\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let n = 1\nstop()\nn = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"\nConsole.writeLine(s)\nstop()\nConsole.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let n: i32 = stop()\nstop()\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("func take(s: string) -> Never => stop()\nlet s = \"s\"\ntake(s)\nConsole.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void NonreturningCallsPreserveInitializationAndMoveFacts(string body, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Stop + body);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Initialized", "let n = 1\nstop()\nlet y = n")]
    [InlineData("FirstWrite", "var n: i32\nstop()\nn = 2\nlet y = n")]
    [InlineData("NeverInitializer", "var n: i32 = stop()\nn = 2\nlet y = n")]
    [InlineData("Chained", "let n = 1\nstop()\nstop()\nlet y = n")]
    [InlineData("Borrow", "func inspect(s: ref/string) -> Never => stop()\nvar s = \"s\"\ninspect(s)\ns = \"new\"\nConsole.writeLine(s)")]
    [InlineData("BorrowElement", "func inspect(s: ref/string) -> Never => stop()\nvar s = (\"s\", 0)\ninspect(s.0)\ns.0 = \"new\"\nConsole.writeLine(s.0)")]
    [InlineData("Cleanup", "let s = \"s\"\ndefer => Console.writeLine(\"cleanup\")\nstop()\nConsole.writeLine(s)")]
    public void EmitsCheckingOnlyContinuations(string name, string body)
        => ScalarEmissionTest.EmitFixture("NeverContinuation" + Configuration + name, Stop + body, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void NoncompletingFirstArgumentSkipsLaterAcquisitionsAndCallee()
        => ScalarEmissionTest.EmitFixture(
            "NeverContinuation" + Configuration + "FirstArgument",
            "func spin() -> Never => loop => ()\nfunc f(a: i32, b: i32) => Console.writeLine(\"bad\")\nvar x = 1\nConsole.writeLine(\"begin\")\nf(spin(), x++)\nConsole.writeLine(\"bad\")",
            "begin\n",
            timeoutMilliseconds: 200);

    [Theory]
    [InlineData("stop()")]
    [InlineData("loop => continue")]
    [InlineData("value(stop())")]
    [InlineData("value(stop()) + 1")]
    [InlineData("do => loop => continue")]
    [InlineData("do => stop()")]
    [InlineData("(scope: do\n    var x: i32 = loop => continue\n    x = 1\n    exit to scope: x\n)")]
    public void NoResultArrivalOrRuntimeStateIsInvented(string initializer)
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func value(x: i32) -> i32 => x\nvar n: i32 = " + initializer + "\nn = 2\nlet y = n");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var body = Assert.Single(c.Ownership.Bodies, b => b.Places.Any(p => p.Kind == OwnershipPlaceKind.Local));
        var local = body.Places.First(p => p.Kind == OwnershipPlaceKind.Local);
        var write = Enumerable.Range(0, body.Operations.Count).Single(i => body.Operations[i] is { Kind: OwnershipOperationKind.Write, Source: BinaryKoto } operation && operation.Place == local.Id);
        Assert.False(body.IsReachable(write));
        Assert.True(body.HasCheckingState(write));
        Assert.Equal(PlaceState.None, body.GetInputState(write, local.Id));
        Assert.False(body.GetCheckingInputState(write, local.Id).HasFlag(PlaceState.MayInit));
        Assert.Equal(PlacementKind.None, body.Operations[write].Placement);
        Assert.DoesNotContain(body.CleanupSteps, x => x.Operation == write);
        c.Bind();
        Assert.False(body.HasCheckingState(write));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("stop()")]
    [InlineData("loop => continue")]
    [InlineData("value(stop())")]
    [InlineData("value(stop()) + 1")]
    [InlineData("do => loop => continue")]
    [InlineData("do => stop()")]
    [InlineData("(scope: do\n    var x: i32 = loop => continue\n    x = 1\n    exit to scope: x\n)")]
    public void ReloadAndWarmPassesRetainCheckingFacts(string initializer)
    {
        var c = MinimalEmissionTest.Analyze(Stop + "func value(x: i32) -> i32 => x\nvar n: i32 = " + initializer + "\nn = 2\nlet y = n");
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
        var allocated = AllocationMeasurement.Measure(() =>
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        });
        Assert.Equal(0, allocated);
        Assert.True(c.Ownership.Result.IsVerified);
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
