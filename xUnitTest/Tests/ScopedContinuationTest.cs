// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class ScopedContinuationTest
{
    private const string Stop = "func stop() -> Never => $abort(\"stop\")\n";
    private const string Default = "func value(y?: i32 = (scope: do\n    var n: i32 = loop => continue\n    n = 1\n    exit to scope: n\n)) -> i32 => y\n";

    [Theory]
    [InlineData("let n: i32 = do => stop()\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let n: i32\ndo => stop()\nlet y = n", OwnershipFailure.UninitializedUse)]
    [InlineData("let n = 1\ndo => stop()\nn = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"\ndo\n    writeLine(s)\n    stop()\nwriteLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"\ndo\n    writeLine(s)\n    loop => continue\nwriteLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"\ndo\n    stop()\n    writeLine(s)\nwriteLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func take(s: string) -> Never => stop()\nlet s = \"s\"\ndo => take(s)\nwriteLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void TerminalScopeStatePreservesInvalidatedFacts(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Initialized", "let n = 1\ndo => stop()\nlet y = n")]
    [InlineData("CheckingWrite", "var n: i32\ndo\n    stop()\n    n = 2\nlet y = n")]
    [InlineData("Nested", "var n: i32\ndo\n    do => stop()\n    n = 2\nlet y = n")]
    [InlineData("Borrow", "func inspect(s: ref/string) -> Never => stop()\nvar s = \"s\"\ndo => inspect(s)\ns = \"new\"\nwriteLine(s)")]
    public void CheckingScopeEffectsHaveNoRuntimeSuccessor(string name, string source)
        => ScalarEmissionTest.EmitFixture("NeverScope" + Configuration + name, Stop + source, string.Empty, 1, "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Fact]
    public void OmittedScopedDefaultRetainsCallerState()
        => ScalarEmissionTest.EmitFixture("NeverScope" + Configuration + "Default", Default + "var x = 1\nwriteLine(\"begin\")\nvalue()\nlet y = x", "begin\n", timeoutMilliseconds: 200);

    [Fact]
    public void OuterTransferDoesNotExecuteLaterArgumentEffects()
        => ScalarEmissionTest.EmitFixture(
            "NeverScope" + Configuration + "OuterTransfer",
            "func f(a: i32, b: i32) -> i32 => a + b\nvar x = 1\nlet y = outer: do\n    f((inner: do => exit to outer: 7), x++)\nif y == 7 and x == 1 => writeLine(\"ok\") else => writeLine(\"bad\")",
            "ok\n");

    [Theory]
    [InlineData("let x: i32\nvalue()\nlet y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let x = 1\nvalue()\nx = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"\nwriteLine(s)\nvalue()\nwriteLine(s)", OwnershipFailure.PossiblyMovedUse)]
    public void OmittedScopedDefaultDoesNotRestoreCallerFacts(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Default + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("func f(c: bool, s: string)\n    do\n        if c\n            writeLine(s)\n            return\n        stop()\n    writeLine(s)")]
    [InlineData("func f(x: i32)\n    work: do\n        defer => loop => ()\n        exit to work\n    let y = x")]
    public void MultipleTerminalPathsAndCleanupRetainTheirGuard(string source)
    {
        var c = MinimalEmissionTest.Analyze(Stop + source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
