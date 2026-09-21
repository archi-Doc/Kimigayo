// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class MixedRequireContinuationTest
{
    [Theory]
    [InlineData("Bare", "x = 1\n                return", "require c else => return", true)]
    [InlineData("BareExit", "x = 1\n                return", "require c else => return", false)]
    [InlineData("Initialize", "return", "require c else\n                x = 3\n                return\n            x = 4", true)]
    [InlineData("Exit", "return", "require c else\n                x = 3\n                exit\n            x = 4", true)]
    [InlineData("Nested", "return", "require c else\n                x = 3\n                require c else => return\n                return\n            x = 4", true)]
    [InlineData("Conditional", "return", "require c else\n                if c\n                    x = 3\n                    return\n                else\n                    x = 4\n                    return\n            x = 5", true)]
    [InlineData("Chain", "x = 1\n                return", "require c else => return\n            require c else => exit\n            return", true)]
    public void RequirePathsKeepEachOriginalTarget(string name, string early, string dead, bool condition)
        => ScalarEmissionTest.EmitFixture("NeverMixedRequire" + Configuration + name, Source("var x: i32", early, dead, "let y = x", "x = 2", condition) + "\nConsole.writeLine(\"done\")", condition ? "done\n" : string.Empty, condition ? 0 : 1, condition ? string.Empty : "Hello.kimi:1:25: abort KIMI_E_ABORT: stop\n");

    [Theory]
    [InlineData("var x: i32", "require c else => return\n            x = 4", "let y = x", "x = 2", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "require c else\n                x = 3\n                return", "let y = x", "x = 2", OwnershipFailure.UninitializedUse)]
    [InlineData("var x: i32", "require c else\n                x = 3\n                return\n            x = 4", "let y = x", "()", OwnershipFailure.UninitializedUse)]
    [InlineData("let x: i32", "require c else\n                x = 3\n                return", "x = 4", "()", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"", "require c else\n                Console.writeLine(s)\n                return", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "require c else => return\n            Console.writeLine(s)", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let s = \"s\"", "require take(s) else => return", "Console.writeLine(s)", "()", OwnershipFailure.PossiblyMovedUse)]
    public void EffectsRemainSpecificToTheirSourcePaths(string declaration, string dead, string use, string tail, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, "return", dead, use, tail) + "\nfunc take(s: string) -> bool => true");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("require c else\n                counter.value = 9\n                return")]
    [InlineData("require c else => return\n            counter.value = 9")]
    public void StoredLoansFollowFailureAndSuccessPaths(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "return", dead, "let n = r.value") + Counter);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("require c else => loop => x = 3")]
    [InlineData("require c else\n                defer => x = 3\n                return")]
    public void UnprovenFailureEffectsRemainGuarded(string dead)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", "return", dead, "let y = x"));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void ReloadedRequirePrefixesAllocateNothingWhenWarm()
    {
        var c = MinimalEmissionTest.Analyze(Source("var counter = Counter.init()\n    let r = counter@ref", "return", "require c else\n                let n = counter.value\n                return", "let n = r.value") + Counter);
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

    private static string Source(string declaration, string early, string dead, string use, string tail = "()", bool condition = true)
        => "func stop() -> Never => $abort(\"stop\")\nfunc f(c: bool)\n    " + declaration + "\n    do\n        loop\n            if c\n                " + early + "\n            else => exit\n            " + dead + "\n        " + tail + "\n        stop()\n    " + use + "\nf(" + (condition ? "true" : "false") + ")";

    private const string Counter = "\nstruct Counter\n    public var value: i32 = 0";

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
