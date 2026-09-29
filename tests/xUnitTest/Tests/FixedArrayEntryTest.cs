// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1, 22.1.2.2 (PLAN G32): a fixed array has no source declaration; its entry members are the receiver
/// functions of the internal Kimi fixed-array group, found by member lookup on any `[N of E]`.</summary>
public class FixedArrayEntryTest
{
    [Fact]
    public void ExplicitSharedEntryLendsEachElement()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryShared",
            "let values: [3 of i32] = [1, 2, 3]\nvar total = 0\nvar it = values.iterate()\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\nrequire total == 6 else => $abort(\"shared\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Theory]
    [InlineData("let it = Kimi.Storage.FixedArray.iterate(values)")]
    [InlineData("let it = ::Kimi.Storage.FixedArray.iterate(values)")]
    [InlineData("let n: i32 = 1\nlet it = n.iterate()")]
    public void TheMemberGroupIsReachableOnlyThroughAFixedArray(string use)
    {
        var c = MinimalEmissionTest.Analyze("let values: [2 of i32] = [1, 2]\n" + use);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void ExplicitExclusiveEntryUpdatesEachElement()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryExclusive",
            "var values: [3 of i32] = [1, 2, 3]\nvar it = values.iterateUniq()\nloop\n    match it.next()\n        .Some(let v) => v@follow += 10\n        .None => exit\nrequire values[0] == 11 and values[2] == 13 else => $abort(\"exclusive\")\nConsole.writeLine(\"ok\")",
            "ok\n");
}
