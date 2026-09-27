// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class IteratorOriginEffectsTest
{
    [Fact]
    public void IntersectionItemsSurviveIndependentSourceUpdates()
    {
        const string source = "struct Cursor {a, b, c}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during (a and b)\n    let first: ref/i32 during a\n    let second: ref/i32 during b\n    let other: uniq/i32 during c\n    var count: i32 = 0\n    public init(first: ref/i32 during a, second: ref/i32 during b, other: uniq/i32 during c)\n        self.first = first\n        self.second = second\n        self.other = other@move\n    public func next(self: uniq/Self) -> Option<ref/i32 during (a and b)>\n        self.other@follow += 1\n        self.count += 1\n        if self.count == 1 => return .Some(self.first)\n        return .Some(self.second)\nlet a = 41\nlet b = 42\nvar other = 0\nvar cursor = Cursor.init(a@ref, b@ref, other@uniq)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let n) => require n == 41 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let n) => require n == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nrequire other == 2 else => $abort(\"other\")\nConsole.writeLine(\"intersection\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorIntersection", source, "intersection\n");
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
