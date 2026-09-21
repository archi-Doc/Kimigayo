// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GuardedMatchContinuationTest
{
    [Theory]
    [InlineData("let n if n => return\n                _ => x = 3")]
    [InlineData("true if check(x = 2, c) => return\n                false if check(x = 3, c) => exit\n                _ => x = 4")]
    [InlineData("let n if (if n => true else => false) => yield to choice\n                _ => x = 3")]
    [InlineData("let n if (guard: do\n                    x = 2\n                    exit to guard: n\n                ) => return\n                _ => x = 3")]
    [InlineData("let n if (match n\n                    true\n                        yield true\n                    false => false\n                ) => return\n                _ => x = 3")]
    public void CompletingGuardsKeepNormalAndTerminalHistories(string arms)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", arms, "x = 5", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("let s = \"s\"", "true if take(s) => return\n                _ => ()", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "true if check(x = 3, c) => return\n                _ => ()", "x = 4", "()", OwnershipFailure.ReassignedLet)]
    [InlineData("let s = \"s\"", "true if (guard: do\n                    Console.writeLine(s)\n                    exit to guard: false\n                ) => return\n                _ => ()", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("var x: i32", "true if c => return\n                _ => x = 3", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    public void FalseGuardsAndTerminalArmsPreserveEffects(string declaration, string arms, string after, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, arms, after, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("let text\n                    Console.writeLine(text)\n                    return")]
    [InlineData("let text if same(text, \"s\")\n                    Console.writeLine(text)\n                    yield to choice\n                _ => ()")]
    [InlineData("let text if (guard: do\n                    x += 1\n                    exit to guard: same(text, \"s\")\n                )\n                    Console.writeLine(text)\n                    yield to choice\n                _ => return")]
    public void OwnedStringSubjectsKeepAcquisitionAndGuardCleanup(string arms)
    {
        var source = Source("var x = 1", arms, "x = 3", "let y = x", "\"s\"");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void ManyFalseGuardsKeepACompactTargetHistory()
    {
        var arms = new System.Text.StringBuilder();
        for (var i = 0; i < 24; i++)
        {
            arms.Append("true if check(x = 2, c) => return\n                ");
        }

        arms.Append("_ => x = 3");
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", arms.ToString(), "x = 4", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        Assert.True(c.Ownership.Bodies.Sum(x => x.Operations.Count) < 5000);
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var verified = c.Ownership.Analyze().IsVerified;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(verified);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ScalarTupleDecompositionKeepsCandidateAndBodyIdentities()
    {
        var source = Source("var x = 1", "(let flag, var n) if flag\n                    n += 1\n                    return\n                (_, let n) => x = n", "x = 3", "let y = x", "(c, 2)");
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    private static string Source(string declaration, string arms, string after, string use, string subject = "c")
        => "func stop() -> Never => $abort(\"guarded match\")\nfunc same(a: ref/string, b: ref/string) -> bool => a == b\nfunc check(effect: (), value: bool) -> bool => value\nfunc take(text: string) -> bool\n    Console.writeLine(text)\n    return false\nfunc f(c: bool)\n    " + declaration +
            "\n    do\n        loop\n            if c => return else => exit\n            choice: match " + subject + "\n                " + arms +
            "\n            " + after + "\n        stop()\n    " + use + "\nf(true)";
}
