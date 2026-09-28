// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ScalarMatchContinuationTest
{
    [Theory]
    [InlineData("true => return\n                false => x = 3")]
    [InlineData("true => return\n                false => exit")]
    [InlineData("true => yield to choice\n                false => x = 3")]
    public void ScalarArmsRetainTheirOwnTransferExtents(string arms)
    {
        var c = MinimalEmissionTest.Analyze(Source("var x = 1", arms, "x = 4", "let y = x"));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Theory]
    [InlineData("var x: i32", "true => return\n                false => x = 3", "()", "let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("let s = \"s\"", "true\n                    _ = s@move\n                    return\n                false => ()", "()", "Console.writeLine(s)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("let x: i32", "true\n                    x = 3\n                    yield to choice\n                false => ()", "x = 4", "()", OwnershipFailure.ReassignedLet)]
    public void TerminalAndCaughtArmsKeepOwnershipHistory(string declaration, string arms, string after, string use, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(Source(declaration, arms, after, use));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(c.Ownership.ControlFlow!.Issues);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("true", 7)]
    [InlineData("false", 11)]
    [InlineData("true, 19", 19)]
    public void ScalarMatchDefaultsUsePreparedArguments(string arguments, int expected)
    {
        var source = "func choose(flag: bool, value: i32 = (match flag\n    true => 7\n    false => 11\n)) -> i32 => value\n" +
            "if choose(" + arguments + ") != " + expected + " => $abort(\"default mismatch\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    private static string Source(string declaration, string arms, string after, string use)
        => "func stop() -> Never => $abort(\"scalar match\")\nfunc f(c: bool)\n    " + declaration +
            "\n    do\n        loop\n            if c => return else => exit\n            choice: match c\n                " + arms +
            "\n            " + after + "\n        stop()\n    " + use + "\nf(true)";
}
