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

    [Fact]
    public void SharedAndExclusiveLoopsUseTheEntries()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryLoops",
            "var values: [3 of i32] = [1, 2, 3]\nvar total = 0\nfor v in values => total += v\nfor v in values@uniq => v@follow *= 2\nlet view = values@ref\nfor v in view => total += v\nrequire total == 18 and values[1] == 4 else => $abort(\"loops\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // Dispatch through `C.IteratorType` reaches the iterator's `next` only through the entry's result Type, for a fixed
    // array and for a declared collection alike.
    [Theory]
    [InlineData("Fixed", "[4 of string]")]
    [InlineData("Array", "Array<string>")]
    public void GenericIterationReachesTheIteratorStep(string name, string type)
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryGeneric" + name,
            "func count<C>(items: ref/C) -> i32\n    C is Iterable\n    var n = 0\n    for item in items => n += 1\n    return n\nlet values: " + type + " = [\"a\", \"b\", \"c\", \"d\"]\nrequire count(values) == 4 else => $abort(\"generic\")\nConsole.writeLine(\"ok\")",
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
