// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class WhileConditionContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";

    [Theory]
    [InlineData("let x: i32\nwhile stop() => ()\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = 1\nwhile stop() => ()\nx = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("func take(s?: string) -> Never => stop()\nlet x = \"s\"\nwhile take(x) => ()\nConsole.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("while stop()\n    var n: i32\n    let y = n", OwnershipFailure.UninitializedUse)]
    public void MissingWhileConditionRetainsAcquiredState(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Empty", "let x = 1\nwhile stop() => ()\nlet y = x")]
    [InlineData("Locals", "let x = 1\nwhile stop()\n    var n = 1\n    n += 1\nlet y = x")]
    [InlineData("Borrow", "func inspect(s?: ref/string) -> Never => stop()\nvar s = \"s\"\nwhile inspect(s) => ()\ns = \"new\"\nConsole.writeLine(s)")]
    [InlineData("Scoped", "let x = 1\ndo => while stop() => ()\nlet y = x")]
    public void MissingWhileConditionHasNoBodyOrSuccessorExecution(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverWhileCondition" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void DefaultWhileConditionPreservesCallerState()
        => ScalarEmissionTest.EmitFixture(
            "NeverWhileCondition" + Configuration + "Default",
            "func value(y?: () = (while (loop => continue) => ())) => ()\nvar x = 1\nConsole.writeLine(\"begin\")\nvalue()\nlet y = x",
            "begin\n",
            timeoutMilliseconds: 200);

    [Theory]
    [InlineData("var x = 1\nwhile stop() => x = 2\nlet y = x")]
    [InlineData("let x = \"s\"\nwhile stop() => Console.writeLine(x)\nConsole.writeLine(x)")]
    public void BodyEffectsOutsideTheLoopStillRequireAJoin(string source)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("while stop() => ()")]
    [InlineData("do => while stop() => ()")]
    public void ReloadAndWarmWhileChecksAllocateNothing(string loop)
    {
        var c = MinimalEmissionTest.Analyze(Stop + "var x = 1\n" + loop + "\nlet y = x");
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

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
