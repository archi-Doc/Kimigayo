// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class TerminalBranchReplayTest
{
    [Theory]
    [InlineData("Bare", "x = 1\n                return", "if c => return else => return", true)]
    [InlineData("BareExit", "x = 1\n                return", "if c => return else => return", false)]
    [InlineData("Abort", "x = 1\n                return", "if c => stop() else => return", true)]
    [InlineData("NeutralLoop", "x = 1\n                return", "if c => loop => continue else => return", true)]
    [InlineData("OwnedLocal", "x = 1\n                return", "if c\n                let s = \"local\"\n                return\n            else => return", true)]
    [InlineData("Initialize", "return", "if c\n                x = 3\n                return\n            else\n                x = 4\n                return", true)]
    [InlineData("Targets", "return", "if c\n                x = 3\n                return\n            else\n                x = 4\n                exit", true)]
    [InlineData("ElseIf", "x = 1\n                return", "if c => return else if c => exit else => return", true)]
    [InlineData("Nested", "return", "if c\n                if c\n                    x = 3\n                    return\n                else\n                    x = 4\n                    exit\n            else\n                x = 5\n                return", true)]
    [InlineData("Chain", "return", "if c\n                x = 3\n                return\n            else\n                x = 4\n                return\n            x = 5\n            return", true)]
    public void TerminalBranchesReplayEachOriginalTarget(string name, string early, string dead, bool condition)
        => ScalarEmissionTest.EmitFixture("NeverTerminalBranch" + Configuration + name, Source("var x: i32", early, dead, "x = 2", "let y = x", condition) + "\nConsole.writeLine(\"done\")", condition ? "done\n" : string.Empty, condition ? 0 : 1, condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "return", "if c\n                x = 3\n                return\n            else => return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "if c => return else\n                x = 3\n                return", "x = 2", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "return", "if c\n                x = 3\n                return\n            else\n                x = 4\n                return", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "return", "if c\n                Console.writeLine(s)\n                return\n            else => return", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "return", "if c => return else\n                Console.writeLine(s)\n                exit", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "return", "if c\n                x = 3\n                return\n            else => return", "()", "x = 4", OwnershipFailure.ReassignedLet)]
    public void EveryBranchMustSupportTheCommonGuarantee(string declaration, string early, string dead, string tail, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, early, dead, tail, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("if c\n                counter.value = 9\n                return\n            else => return")]
    [InlineData("if c => return else\n                counter.value = 9\n                return")]
    public void StoredLoansFollowEachBranchPrefix(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "return", dead, "()", "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("if c => return else => x = 3")]
    [InlineData("if c => return")]
    [InlineData("if c => loop => x = 3 else => return")]
    [InlineData("if c\n                defer => x = 3\n                return\n            else => return")]
    public void PartialDivergentAndDeferredBranchesRemainGuarded(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x: i32", "x = 1\n                return", dead, "x = 2", "let y = x"));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReloadedBranchPrefixesAllocateNothingWhenWarm()
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "return", "if c\n                let n = counter.value\n                return\n            else => return", "()", "let n = r.value") + Counter);
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
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Ownership.Analyze();
            c.Emission.WriteIr(TextWriter.Null, out _);
        }));
    }

    private static string Source(string declaration, string early, string dead, string tail, string use, bool condition = true)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    " + declaration + "\n    do\n        loop\n            if c\n                " + early + "\n            else => exit\n            " + dead + "\n        " + tail + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
