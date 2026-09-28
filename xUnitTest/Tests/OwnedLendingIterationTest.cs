// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class OwnedLendingIterationTest
{
    private const string Source =
        "struct Cursor\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    var count: i32 = 0\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        if self.count == 3 => return .None\n        self.count += 1\n        return .Some(self.count@ref)\nstruct Values\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Cursor\n    public func intoIterator(self: Self) -> Cursor => Cursor.init()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedEntryCanReturnASelfLendingIterator(bool skipMiddle)
    {
        var source = Source + "var values = Values.init()\nvar sum = 0\nfor item in values@move\n" +
            (skipMiddle ? "    if item == 2 => continue\n" : string.Empty) +
            "    sum += item\nrequire sum == " + (skipMiddle ? "4" : "6") + " else => $abort(\"sum\")\nConsole.writeLine(\"owned lending\")";
        ScalarEmissionTest.EmitFixture("AssociatedOwnedLending" + (skipMiddle ? "Continue" : "Backedge"), source, "owned lending\n");
    }

    [Fact]
    public void GenericOwnedEntryUsesTheLendingRequirement()
    {
        var source = Source + "func count<T>(values: T) -> i32\n    T is IntoIterable\n    var count = 0\n    for _ in values@move => count += 1\n    return count\nrequire count(Values.init()) == 3 else => $abort(\"count\")\nConsole.writeLine(\"generic lending\")";
        ScalarEmissionTest.EmitFixture("AssociatedOwnedLendingGeneric", source, "generic lending\n");
    }

    [Fact]
    public void OwningIterationConsumesItsSource()
    {
        var c = MinimalEmissionTest.Analyze(Source + "var values = Values.init()\nfor _ in values@move => ()\nlet again = values@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void AnOwnedLentItemCannotEscapeTheIterator()
    {
        var c = MinimalEmissionTest.Analyze(Source + "func escape() -> ref/i32 during static\n    for item in Values.init()\n        return item\n    $abort(\"empty\")");
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }
}
