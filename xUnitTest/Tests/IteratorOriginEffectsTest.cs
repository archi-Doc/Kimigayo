// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class IteratorOriginEffectsTest
{
    [Fact]
    public void ExactExpectedExclusiveReferencesAreReborrowed()
    {
        const string source = "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is ref/i32 during step\n    let value: uniq/i32 during source\n    public init(value: uniq/i32 during source) => self.value = value@move\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during step>\n        do\n            let alias: uniq/i32 during source = self.value\n            alias@follow += 1\n        return .Some(self.value)\nvar value = 40\nvar cursor = Cursor.init(value@uniq)\nlet first = cursor.next()\nmatch first\n    .Some(let n) => require n == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nlet second = cursor.next()\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"exact reborrow\")";
        ScalarEmissionTest.EmitFixture("AssociatedLendingExactReborrow", source, "exact reborrow\n");
    }

    [Theory]
    [InlineData("ref", "a", true)]
    [InlineData("uniq", "a", false)]
    [InlineData("ref", "b", true)]
    [InlineData("uniq", "b", false)]
    public void ImplicitLocalReborrowsKeepTheirOriginalAccessEffect(string semantics, string origin, bool valid)
    {
        var source = "struct Cursor {a, b}\n    origin a outlives b\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let value: uniq/i32 during a\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        do\n            let alias: " + semantics + "/i32 during " + origin + " = self.value\n            _ = alias@move\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Fact]
    public void IntersectionItemsSurviveIndependentSourceUpdates()
    {
        const string source = "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: ref/i32 during a\n    let second: ref/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n    public init(first: ref/i32 during a, second: ref/i32 during b, other: uniq/i32 during c)\n        self.first = first\n        self.second = second\n        self.other = other@move\n    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        self.other@follow += 1\n        self.count += 1\n        if self.count == 1 => return .Some(self.first)\n        return .Some(self.second)\nlet a = 41\nlet b = 42\nvar other = 0\nvar cursor = Cursor.init(a@ref, b@ref, other@uniq)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let n) => require n == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nrequire other == 2 else => $abort(\"other\")\nConsole.writeLine(\"intersection\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorIntersection", source, "intersection\n");
    }

    [Fact]
    public void SharedImplicitReborrowsAllowRetainedItems()
    {
        const string source = "struct Cursor {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let value: uniq/i32 during a\n    public init(value: uniq/i32 during a) => self.value = value@move\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        let alias: ref/i32 during a = self.value\n        require alias == 42 else => $abort(\"alias\")\n        return .Some(alias)\nvar value = 42\nvar cursor = Cursor.init(value@uniq)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let n) => require n == 42 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"implicit shared\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorImplicitShared", source, "implicit shared\n");
    }

    [Theory]
    [InlineData("self.first@follow += 1", false)]
    [InlineData("self.second@follow += 1", false)]
    [InlineData("self.other@follow += 1", true)]
    [InlineData("self.count += 1", true)]
    public void IntersectionResultsKeepEveryPossibleSource(string operation, bool valid)
    {
        var source = "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: uniq/i32 during a\n    let second: uniq/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        " + operation + "\n        if self.count == 0 => return .Some(self.first)\n        return .Some(self.second)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    private const string Point = "struct Point\n    public var x: i32 = 0\n";
    private const string Box = "struct Box\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32 = 0\n" +
        "    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/i32 during self => self.value\n";

    private const string Bump = "group Helpers\n    public func bump(value: uniq/i32) => value@follow += 1\n";

    // SPEC 22.1.2.4: every write that reaches a Loan of an earlier item conflicts, however the Place is written; the same
    // write through an unrelated Origin does not.
    [Theory]
    [InlineData(Point, "let point: uniq/Point during {0}", "self.point.x += 1")]
    [InlineData("", "let values: uniq/Array<i32> during {0}", "self.values.append(1)")]
    [InlineData(Box, "let box: uniq/Box during {0}", "self.box[0] += 1")]
    [InlineData(Box, "let box: uniq/Box during {0}", "_ = self.box[0]@uniq/i32")]
    [InlineData(Bump, "let counter: uniq/i32 during {0}", "Helpers.bump(self.counter)")]
    [InlineData("", "let counter: uniq/i32 during {0}", "self.counter@follow = 5")]
    public void WritesReachingItemLoansAreRejected(string prefix, string field, string operation)
    {
        for (var i = 0; i < 2; i++)
        {
            var origin = i == 0 ? "a" : "b";
            var source = prefix + "struct Cursor {a, b}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let first: ref/i32 during a\n    " +
                string.Format(System.Globalization.CultureInfo.InvariantCulture, field, origin) + "\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        " + operation + "\n        return .Some(self.first)";
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True((origin == "b") == c.Binding.Result.IsComplete, origin + "\n" + MinimalEmissionTest.Describe(c, null));
            if (origin == "a")
            {
                Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
            }
        }
    }

    // Shared reads through the items' Origin keep shared items valid; the use selects index, not indexUniq.
    [Theory]
    [InlineData(Point, "let point: uniq/Point during a", "_ = self.point.x")]
    [InlineData(Box, "let box: uniq/Box during a", "_ = self.box[0]")]
    [InlineData("", "let values: uniq/Array<i32> during a", "_ = self.values.length")]
    public void SharedReadsOfItemSourcesAreAllowed(string prefix, string field, string operation)
    {
        var source = prefix + "struct Cursor {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    let first: ref/i32 during a\n    " + field +
            "\n    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        " + operation + "\n        return .Some(self.first)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 8.4.3, 22.1.2.4: an abstract item may be instantiated with any Loan of the conforming Type's Origins.
    [Theory]
    [InlineData("self.counter@follow += 1", false)]
    [InlineData("self.count += 1", true)]
    public void AbstractItemsKeepEveryOriginOfTheirType(string operation, bool valid)
    {
        var source = "struct Wrap<T> {a}\n    Self is Iterator\n    associate Iterator.Item is T\n    let counter: uniq/i32 during a\n    var count: i32 = 0\n    var pending: Option<T>\n" +
            "    public func next(self: uniq/Self) -> Option<T>\n        " + operation + "\n        return Kimi.Intrinsics.exchange(self.pending@uniq, with: .None)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    // Warm rebinding reruns both effect bounds, through intersection items, generic callees and formatting, without allocating.
    [Fact]
    public void WarmEffectBoundChecksDoNotAllocate()
    {
        const string Source = "group Helpers\n    public func bump<T>(value: uniq/i32, marker: ref/T) => value@follow += 1\n" +
            "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: ref/i32 during a\n    let second: ref/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n" +
            "    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        Helpers.bump(self.other, self.count@ref)\n        self.count += 1\n        if self.count == 1 => return .Some(self.first)\n        return .Some(self.second)\n" +
            "struct Writer\n    Self is BufferWriter\n    var local: i32 = 1\n    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>\n        _ = \"value: \\(self.local)\"\n        return .Err(BufferFull.init())";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }

    // A call without a published effect bound, such as a requirement of a Type parameter, cannot certify next.
    [Fact]
    public void UnboundedRequirementCallsAreRejected()
    {
        const string Source = "struct Outer<I> {a}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during a\n    I is Iterator\n    let first: ref/i32 during a\n    var inner: I\n" +
            "    public func next(self: uniq/Self) -> Option<ref/i32 during a>\n        _ = self.inner.next()\n        return .Some(self.first)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }
}
