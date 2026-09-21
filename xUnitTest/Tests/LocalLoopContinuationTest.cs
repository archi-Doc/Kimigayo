// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class LocalLoopContinuationTest
{
    private const string Loop = "loop\n    var n = 1\n    n = 2\n";

    [Theory]
    [InlineData("let x: i32\n", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = 1\n", "x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let x = \"s\"\nConsole.writeLine(x)\n", "Console.writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    public void LocalLoopEffectsDoNotRestoreEnclosingFacts(string before, string after, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(before + Loop + after);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("Local", "var x = 1\n" + Loop + "let y = x")]
    [InlineData("Initializer", "var x: i32 = loop\n    var n = 1\n    n += 1\nx = 3\nlet y = x")]
    [InlineData("Nested", "var x = 1\ndo\n    loop\n        var n = 1\n        while n < 3\n            n += 1\n        if n == 3 => continue else => ()\nlet y = x")]
    [InlineData("Default", "func value(y: i32 = (loop\n    var n = 1\n    n = 2\n)) -> i32 => y\nvar x = 1\nvalue()\nlet y = x")]
    [InlineData("Checking", "func f()\n    return\n    var x: i32 = loop\n        var n = 1\n        n = 2\n    x = 3\n    let y = x\nf()\nloop => continue")]
    [InlineData("ContainedExit", "let x = 1\nloop\n    var n = work: do\n        exit to work: 1\n    n += 1\nlet y = x")]
    public void EmitsLoopsWhoseEffectsStayLocal(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverLocalLoop" + Configuration + name, "Console.writeLine(\"begin\")\n" + source, "begin\n", timeoutMilliseconds: 200);

    [Theory]
    [InlineData("var x = 1\nloop\n    x = 2\nlet y = x")]
    [InlineData("var x = 1\nloop\n    x++\nlet y = x")]
    [InlineData("let x = \"s\"\nloop\n    let y = x\nConsole.writeLine(x)")]
    [InlineData("func f() => ()\nvar x = 1\nloop\n    f()\nlet y = x")]
    [InlineData("var x = 1\nloop\n    defer => ()\nlet y = x")]
    [InlineData("func f(x: i32)\n    loop => return\n    let y = x\nf(1)")]
    public void EnclosingEffectsAndUnverifiedOperationsRemainGuarded(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData(Loop)]
    [InlineData("loop\n    var n = 1\n    if x == 1 => n += 1 else => n = 3\n    while n < 4\n        n++\n")]
    [InlineData("loop\n    var n = work: do\n        exit to work: 1\n    n += 1\n")]
    public void ReloadAndWarmProofReuseAllocateNothing(string loop)
    {
        var c = MinimalEmissionTest.Analyze("var x = 1\n" + loop + "let y = x");
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

    [Theory]
    [InlineData("var n: i32\n    let y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let n = 1\n    n = 2", OwnershipFailure.ReassignedLet)]
    public void LoopLocalUsesStillRequireOrdinaryOwnershipChecks(string body, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze("let x = 1\nloop\n    " + body + "\nlet y = x");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
