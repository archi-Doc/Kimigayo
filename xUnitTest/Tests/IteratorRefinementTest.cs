// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class IteratorRefinementTest
{
    [Fact]
    public void IteratorProvidesItsInheritedLendingRequirement()
    {
        const string source = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var value: i32 = 0\n    public init() => ()\n    public func next(self: uniq/Self) -> Option<i32>\n        self.value += 1\n        return .Some(self.value)\nfunc advance<T>(iterator: uniq/T during step) -> Option<T.(LendingIterator).LentItem(step)>\n    T is Iterator\n    return iterator.next()\nvar iterator = Counter.init()\nlet first = advance(iterator@uniq)\nlet second = advance(iterator@uniq)\nmatch first\n    .Some(let value) => require value == 1 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let value) => require value == 2 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"refined\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorRefinement", source, "refined\n");
    }

    [Fact]
    public void IteratorCannotReplaceTheInheritedFamilyWithABorrow()
    {
        const string source = "struct Bad\n    Self is Iterator\n    associate Iterator.Item is i32\n    associate LendingIterator.LentItem(a) is ref/i32 during a\n    public func next(self: uniq/Self) -> Option<i32> => .None";
        Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);
    }
}
