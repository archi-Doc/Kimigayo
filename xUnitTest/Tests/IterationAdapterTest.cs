// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2.3: Kimi.Iteration.owned and borrowed adapt a LendingIterator without an entry conformance, and a
/// Type may return Borrowed of itself as its UniqIterable entry.</summary>
public class IterationAdapterTest
{
    private const string Counter = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var value: i32 = 0\n    public init() => ()\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        if self.value == 3 => return .None\n        self.value += 1\n        return .Some(self.value)\n";

    // The SPEC 22.1.2.2 example: one item through the borrowed entry, then the rest by value.
    [Fact]
    public void CountdownEnumeratesThroughBorrowedAndOwnedEntries()
    {
        const string Source = "struct Countdown\n    Self is Iterator\n    Self is IntoIterable\n    Self is UniqIterable\n    associate Iterator.Item is i32\n" +
            "    associate IntoIterable.IteratorType is Self\n    associate UniqIterable.IteratorType(a) is Kimi.Iteration.Borrowed<Self> during a\n    var remaining: i32 = 3\n    public init() => ()\n" +
            "    public func next(self: uniq/Self) -> Option<i32>\n        if self.remaining == 0 => return .None\n        self.remaining -= 1\n        return .Some(self.remaining)\n" +
            "    public func intoIterator(self: Self) -> Self\n        return self@move\n" +
            "    public func iterateUniq(self: uniq/Self during source) -> Kimi.Iteration.Borrowed<Self> during source\n        return Kimi.Iteration.borrowed(self)\n" +
            "var total: i32 = 0\nvar countdown = Countdown.init()\nfor number in countdown@uniq\n    total += number\n    exit\nfor number in countdown@move\n    total += number\n" +
            "require total == 3 else => $abort(\"total\")\nConsole.writeLine(\"countdown\")";
        ScalarEmissionTest.EmitFixture("IterationAdapterCountdown", Source, "countdown\n");
    }

    [Theory]
    [InlineData("owned", "var sum: i32 = 0\nfor n in Kimi.Iteration.owned(Counter.init())\n    sum += n\nrequire sum == 6 else => $abort(\"owned\")\nConsole.writeLine(\"owned\")", "owned\n")]
    [InlineData("borrowed", "var counter = Counter.init()\nvar sum: i32 = 0\nfor n in Kimi.Iteration.borrowed(counter@uniq)\n    sum += n\n    if n == 2 => exit\nfor n in Kimi.Iteration.borrowed(counter@uniq)\n    sum += n\nrequire sum == 6 else => $abort(\"borrowed\")\nConsole.writeLine(\"borrowed\")", "borrowed\n")]
    public void AdaptersEnumerateAnIteratorWithoutAnEntry(string name, string program, string stdout)
        => ScalarEmissionTest.EmitFixture("IterationAdapter" + name, Counter + program, stdout);

    // A lending item keeps the actual Reborrow of the adapter's next as its step.
    [Fact]
    public void BorrowedForwardsLendingItems()
    {
        const string Source = "struct Cursor\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    var value: i32 = 0\n    public init() => ()\n" +
            "    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        if self.value == 2 => return .None\n        self.value += 1\n        return .Some(self.value@ref)\n" +
            "var cursor = Cursor.init()\nvar sum: i32 = 0\nfor item in Kimi.Iteration.borrowed(cursor@uniq)\n    sum += item\nrequire sum == 3 else => $abort(\"lending\")\nConsole.writeLine(\"lending\")";
        ScalarEmissionTest.EmitFixture("IterationAdapterLending", Source, "lending\n");
    }

    [Theory]
    [InlineData("let a = Kimi.Iteration.owned(7)")]
    [InlineData("var n: i32 = 1\nlet a = Kimi.Iteration.borrowed(n@uniq)")]
    public void AdaptersRequireALendingIterator(string program)
    {
        var c = MinimalEmissionTest.Analyze(program);
        Assert.False(c.Binding.Result.IsComplete);
        // A primitive's conformance to a user Contract is not refuted by declaration, so the Constraint stays unproven.
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.NoApplicableOverload_Kd or DiagnosticCode.UnprovenConstraint_Kd);
    }
}
