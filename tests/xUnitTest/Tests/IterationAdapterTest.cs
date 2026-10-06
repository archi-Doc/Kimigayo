// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2.3: Kimi.Iteration.owning and borrowing adapt a LendingIterator without an entry conformance, and
/// a Type may return a BorrowingIterator of itself as its UniqIterable entry.</summary>
public class IterationAdapterTest
{
    private const string Counter = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var value: i32 = 0\n    public init() => ()\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        if self.value == 3 => return .None\n        self.value += 1\n        return .Some(self.value)\n";

    private const string NextPair = "func nextPair<I>(iterator: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    let first = iterator.next()\n" +
        "    let second = iterator.next() // The published effect bound permits retaining first.\n    return (first@move, second@move)\n" +
        "func sum(pair: (Option<i32>, Option<i32>)) -> i32\n    var total: i32 = 0\n    match pair.0\n        .Some(let n) => total += n\n        .None => ()\n" +
        "    match pair.1\n        .Some(let n) => total += n\n        .None => ()\n    return total\n";

    private const string Cursor = "struct Cursor\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    var value: i32 = 0\n    public init() => ()\n" +
        "    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        if self.value == 2 => return .None\n        self.value += 1\n        return .Some(self.value@ref)\n";

    // The SPEC 22.1.2.2 example: one item through the borrowed entry, then the rest by value.
    [Fact]
    public void CountdownEnumeratesThroughBorrowedAndOwnedEntries()
    {
        const string Source = "struct Countdown\n    Self is Iterator\n    Self is IntoIterable\n    Self is UniqIterable\n    associate Iterator.Item is i32\n" +
            "    associate IntoIterable.IteratorType is Self\n    associate UniqIterable.IteratorType(a) is Kimi.Iteration.BorrowingIterator<Self> during a\n    var remaining: i32 = 3\n    public init() => ()\n" +
            "    public func next(self: uniq/Self) -> Option<i32>\n        if self.remaining == 0 => return .None\n        self.remaining -= 1\n        return .Some(self.remaining)\n" +
            "    public func intoIterator(self: Self) -> Self\n        return self@move\n" +
            "    public func iterateUniq(self: uniq/Self during source) -> Kimi.Iteration.BorrowingIterator<Self> during source\n        return Kimi.Iteration.borrowing(self)\n" +
            "var total: i32 = 0\nvar countdown = Countdown.init()\nfor number in countdown@uniq\n    total += number\n    exit\nfor number in countdown@move\n    total += number\n" +
            "require total == 3 else => $abort(\"total\")\nConsole.writeLine(\"countdown\")";
        ScalarEmissionTest.EmitFixture("IterationAdapterCountdown", Source, "countdown\n");
    }

    [Theory]
    [InlineData("owning", "var sum: i32 = 0\nfor n in Kimi.Iteration.owning(Counter.init())\n    sum += n\nrequire sum == 6 else => $abort(\"owning\")\nConsole.writeLine(\"owning\")", "owning\n")]
    [InlineData("borrowing", "var counter = Counter.init()\nvar sum: i32 = 0\nfor n in Kimi.Iteration.borrowing(counter@uniq)\n    sum += n\n    if n == 2 => exit\nfor n in Kimi.Iteration.borrowing(counter@uniq)\n    sum += n\nrequire sum == 6 else => $abort(\"borrowing\")\nConsole.writeLine(\"borrowing\")", "borrowing\n")]
    public void AdaptersEnumerateAnIteratorWithoutAnEntry(string name, string program, string stdout)
        => ScalarEmissionTest.EmitFixture("IterationAdapter" + name, Counter + program, stdout);

    // A lending item keeps the actual Reborrow of the adapter's next as its step.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void BorrowingIteratorForwardsLendingItems()
    {
        const string Source = Cursor + "var cursor = Cursor.init()\nvar sum: i32 = 0\nfor item in Kimi.Iteration.borrowing(cursor@uniq)\n    sum += item\nrequire sum == 3 else => $abort(\"lending\")\nConsole.writeLine(\"lending\")";
        ScalarEmissionTest.EmitFixture("IterationAdapterLending", Source, "lending\n");
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        var bytes = AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 8, warmupIterations: 8);
        Assert.True(valid);
        Assert.Equal(0, bytes);
    }

    // SPEC 22.1.2.3: an adapter is an Iterator exactly when its input is, so generic code may retain its items;
    // BorrowingIterator steps its input through the borrow of that input's Storage, which no Item of the input keeps
    // (SPEC 22.1.2.4).
    [Fact]
    public void AdaptersAreIteratorsWhenTheirInputIs()
    {
        const string Source = Counter + NextPair + "var owning = Kimi.Iteration.owning(Counter.init())\nlet first = sum(nextPair(owning@uniq))\n" +
            "var counter = Counter.init()\nvar borrowing = Kimi.Iteration.borrowing(counter@uniq)\nlet second = sum(nextPair(borrowing@uniq))\n" +
            "require first == 3 and second == 3 else => $abort(\"pairs\")\nConsole.writeLine(\"iterators\")";
        ScalarEmissionTest.EmitFixture("IterationAdapterIterators", Source, "iterators\n");
    }

    // SPEC 8.4.8.2: a member declared outside the conditional block is judged under the conditions, so the forwarded
    // `I.(LendingIterator).LentItem(step)` is the step-independent `I.Item` when `I is Iterator`.
    [Fact]
    public void AUserWrapperDeclaresTheConditionalIterator()
    {
        const string Wrapper = "struct Wrapper<I>\n    I is LendingIterator\n    Self is LendingIterator\n    associate LendingIterator.LentItem(step) is I.(LendingIterator).LentItem(step)\n" +
            "    var inner: I\n\n    Self is Iterator when I is Iterator\n        associate Iterator.Item is I.(Iterator).Item\n    public init(inner: I) => self.inner = inner@move\n" +
            "    public func next(self: uniq/Self during step) -> Option<I.(LendingIterator).LentItem(step)>\n        return self.inner.next()\n";
        var source = Counter + NextPair + Wrapper + "var wrapper = Wrapper<Counter>.init(Counter.init())\nrequire sum(nextPair(wrapper@uniq)) == 3 else => $abort(\"wrapper\")\n" +
            "Console.writeLine(\"conditional\")";
        ScalarEmissionTest.EmitFixture("IterationAdapterConditionalWrapper", source, "conditional\n");
    }

    // The adapter of a LendingIterator that is not an Iterator supplies no Iterator conformance.
    [Fact]
    public void ALendingAdapterIsNotAnIterator()
    {
        var c = MinimalEmissionTest.Analyze(Cursor + NextPair + "var cursor = Cursor.init()\nvar adapter = Kimi.Iteration.borrowing(cursor@uniq)\nlet pair = nextPair(adapter@uniq)");
        Assert.False(c.Binding.Result.IsComplete);
        // The refuted condition makes the only candidate inapplicable (SPEC 8.4.8.2).
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }

    [Theory]
    [InlineData("let a = Kimi.Iteration.owning(7)")]
    [InlineData("var n: i32 = 1\nlet a = Kimi.Iteration.borrowing(n@uniq)")]
    public void AdaptersRequireALendingIterator(string program)
    {
        var c = MinimalEmissionTest.Analyze(program);
        Assert.False(c.Binding.Result.IsComplete);
        // SPEC 8.7: a primitive's conformances are fixed, so the only candidate is inapplicable.
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }
}
