// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class LendingIteratorContractTest
{
    private const string Counter = "struct Counter\n    Self is LendingIterator\n    var value: i32 = 0\n    public init() => ()\n    associate LendingIterator.LentItem(step) is ref/i32 during step\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        if self.value == 2 => return .None\n        self.value += 1\n        return .Some(self.value@ref)\nfunc advance<T>(iterator: uniq/T during step) -> Option<T.(LendingIterator).LentItem(step)>\n    T is LendingIterator\n    return iterator.next()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandardLendingContractReturnsBorrowedOptions(bool generic)
    {
        var next = generic ? "advance(iterator@uniq)" : "iterator.next()";
        var source = Counter + "var iterator = Counter.init()\nlet first = " + next + "\nmatch first\n    .Some(let item) => require item == 1 else => $abort(\"first\")\n    .None => $abort(\"missing first\")\nlet second = " + next + "\nmatch second\n    .Some(let item) => require item == 2 else => $abort(\"second\")\n    .None => $abort(\"missing second\")\nmatch " + next + "\n    .Some(_) => $abort(\"exhaustion\")\n    .None => ()\nConsole.writeLine(\"lending option\")";
        ScalarEmissionTest.EmitFixture("AssociatedLendingOption" + (generic ? "Generic" : "Direct"), source, "lending option\n");
    }

    [Fact]
    public void RetainedOptionItemBlocksTheNextBorrow()
    {
        var source = Counter + "var iterator = Counter.init()\nlet first = iterator.next()\nlet second = iterator.next()\nmatch first\n    .Some(let item) => require item == 1 else => $abort(\"item\")\n    .None => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.NotEmpty(c.Ownership.Issues);
    }

    // SPEC 8.1.2, 15.3.1: a wrapper stores its inner LendingIterator and forwards its lending family: the receiver borrow
    // `uniq/Wrapper<I> during step` proves that I lives during step, so `I.(LendingIterator).LentItem(step)` is well formed.
    [Fact]
    public void AWrapperForwardsTheLendingFamilyOfItsParameter()
    {
        const string Wrapper = "struct Wrapper<I>\n    I is LendingIterator\n    Self is LendingIterator\n    associate LendingIterator.LentItem(step) is I.(LendingIterator).LentItem(step)\n    var inner: I\n" +
            "    public init(inner: I) => self.inner = inner@move\n    public func next(self: uniq/Self during step) -> Option<I.(LendingIterator).LentItem(step)>\n        return self.inner.next()\n";
        var source = Counter + Wrapper + "var wrapper = Wrapper<Counter>.init(Counter.init())\nvar sum: i32 = 0\nloop\n    match wrapper.next()\n        .Some(let n) => sum += n\n        .None => exit\n" +
            "require sum == 3 else => $abort(\"wrapper\")\nConsole.writeLine(\"forwarded\")";
        ScalarEmissionTest.EmitFixture("LendingIteratorWrapper", source, "forwarded\n");
    }
}
