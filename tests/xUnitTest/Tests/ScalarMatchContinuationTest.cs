// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ScalarMatchContinuationTest
{
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
}
