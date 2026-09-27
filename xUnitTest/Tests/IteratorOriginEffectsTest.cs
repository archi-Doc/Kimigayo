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
}
