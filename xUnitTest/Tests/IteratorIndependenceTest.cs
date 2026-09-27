// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class IteratorIndependenceTest
{
    [Theory]
    [InlineData("Iterator", "Iterator.Item", false)]
    [InlineData("LendingIterator", "LentItem(step)", true)]
    public void RepeatedWriteToPublishedReferentNeedsIndependence(string contract, string item, bool valid)
    {
        var source = "struct Cursor {source}\n    Self is " + contract + "\n    associate " + item + " is ref/i32 during source\n    let value: uniq/i32 during source\n    public func next(self: uniq/Self during step) -> Option<ref/i32 during source>\n        self.value@follow += 1\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Fact]
    public void SharedExternalItemsAllowCursorUpdates()
    {
        const string source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.count += 1\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void SharedItemsRemainUsableAfterAnotherStep()
    {
        const string source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public init(value: ref/i32 during source) => self.value = value\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.count += 1\n        return .Some(self.value)\nlet value = 42\nvar cursor = Cursor.init(value@ref)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let item) => require item == 42 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let item) => require item == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"independent\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorIndependentShared", source, "independent\n");
    }
}
