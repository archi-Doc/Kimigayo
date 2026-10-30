// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class IteratorIndependenceTest
{
    private const string CleanupProgram = "struct Trace\n    public init() => ()\n    drop => Console.writeLine(\"drop\")\nstruct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: ref/i32 during source\n    var count: i32 = 0\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value\n    public init(value: ref/i32 during source) => self.value = value\n    func advance<T>(self: uniq/Self) => self.count += 1\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        let trace = Trace.init()\n        self.advance<i32>()\n        return .Some(self.value)\nlet value = 42\nvar cursor = Cursor.init(value@ref)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let item) => require item == 42 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let item) => require item == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"independent cleanup\")";

    // SPEC 22.2.3, 15.6.3: an item Copied from a shared Field keeps its own Origin and no Loan of the receiver borrow, so next may
    // update the cursor and earlier items stay usable across later calls.
    [Fact]
    public void SharedExternalItemsAllowCursorUpdates()
    {
        const string source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.count += 1\n        return .Some(self.value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void SharedItemsRemainUsableAfterAnotherStep()
    {
        const string source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public init(value: ref/i32 during source) => self.value = value\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.count += 1\n        return .Some(self.value)\nlet value = 42\nvar cursor = Cursor.init(value@ref)\nlet first = cursor.next()\nlet second = cursor.next()\nmatch first\n    .Some(let item) => require item == 42 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch second\n    .Some(let item) => require item == 42 else => $abort(\"second\")\n    .None => $abort(\"empty\")\nConsole.writeLine(\"independent\")";
        ScalarEmissionTest.EmitFixture("AssociatedIteratorIndependentShared", source, "independent\n");
    }

    [Fact]
    public void IndependentCleanupAndAccessorsKeepEarlierItemsUsable()
        => ScalarEmissionTest.EmitFixture("AssociatedIteratorIndependentCleanup", CleanupProgram, "drop\ndrop\nindependent cleanup\n");

    // SPEC 8.11.3, 8.11.2: warm Binding, analysis and generation reuse the confined check's call and destruction state: the cleanup program
    // conforms to a Contract bounded by confined, its next calls a generic helper and destroys a Trace whose drop touches no environment.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void EffectCheckingReusesCallAndDestructionState()
    {
        var source = "contract Feed\n    associate Item\n    func next(self: uniq/Self) -> Option<Self.Item>\n        effect confined\n" +
            CleanupProgram.Replace("drop => Console.writeLine(\"drop\")", "drop => ()", StringComparison.Ordinal).Replace("Iterator", "Feed", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Analyze().IsVerified || !c.Generate(TextWriter.Null))
            {
                throw new InvalidOperationException("Independent iterator compilation failed.");
            }
        }));
    }
}
