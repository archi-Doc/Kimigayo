// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class BorrowingIterationTest
{
    public static string Declarations(bool exclusive, bool lending = false)
    {
        var entry = exclusive ? "UniqIterable" : "Iterable";
        var method = exclusive ? "iterateUniq" : "iterate";
        var mode = exclusive ? "uniq" : "ref";
        var item = lending ? "ref/i32 during step" : "i32";
        return "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is " + item + "\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public init(value: ref/i32 during source) => self.value = value\n    public func next(self: uniq/Self during step) -> Option<" + item + ">\n        if self.count == 3 => return .None\n        self.count += 1\n        return .Some(self.value)\nstruct Values\n    Self is " + entry + "\n    associate " + entry + ".IteratorType(source) is Cursor during source\n    var value: i32 = 7\n    public init() => ()\n    public func " + method + "(self: " + mode + "/Self during source) -> Cursor during source => Cursor.init(self.value@ref)\n";
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BorrowingLoopsUseTheirSelectedEntry(bool exclusive, bool lending)
    {
        var source = Declarations(exclusive, lending) + "var values = Values.init()\nvar sum = 0\nfor item in values" + (exclusive ? "@uniq" : string.Empty) + "\n    sum += item\nrequire sum == 21 else => $abort(\"sum\")\nConsole.writeLine(\"borrow loop\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingLoop" + (exclusive ? "Uniq" : "Ref") + (lending ? "Lending" : "Value"), source, "borrow loop\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericBorrowingLoopCountsUnknownItems(bool exclusive)
    {
        var entry = exclusive ? "UniqIterable" : "Iterable";
        var mode = exclusive ? "uniq" : "ref";
        var source = Declarations(exclusive) + "func count<T>(values: " + mode + "/T) -> i32\n    T is " + entry + "\n    var result = 0\n    for _ in values\n        result += 1\n    return result\nvar values = Values.init()\nrequire count(values@" + mode + ") == 3 else => $abort(\"count\")\nConsole.writeLine(\"generic loop\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingGenericLoop" + mode, source, "generic loop\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfLendingStepEndsBeforeBackedge(bool skipMiddle)
    {
        var source = Declarations(false, true).Replace(".Some(self.value)", ".Some(self.count@ref)", StringComparison.Ordinal) +
            "var values = Values.init()\nvar sum = 0\nfor item in values\n" + (skipMiddle ? "    if item == 2 => continue\n" : string.Empty) + "    sum += item\nrequire sum == " + (skipMiddle ? "4" : "6") + " else => $abort(\"step\")\nConsole.writeLine(\"step\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingSelfStep" + (skipMiddle ? "Continue" : "Backedge"), source, "step\n");
    }

    // SPEC 3.4.1, 14.6.2: the entry is selected at the referent of every reference layer of the Subject.
    [Fact]
    public void EntryIsSelectedThroughEveryReferenceLayer()
    {
        var source = Declarations(false) + "let values = Values.init()\nlet r = values@ref\nlet rr = r@ref\nvar sum = 0\nfor item in rr\n    sum += item\nrequire sum == 21 else => $abort(\"layers\")\nConsole.writeLine(\"layers\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingLayers", source, "layers\n");
    }

    [Fact]
    public void SharedEntryMayReturnMutableOwnedItems()
    {
        var source = Declarations(false) + "var values = Values.init()\nvar sum = 0\nfor var item in values\n    item = 2\n    sum += item\nrequire sum == 6 else => $abort(\"mutable item\")\nConsole.writeLine(\"mutable item\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingMutableItem", source, "mutable item\n");
    }

    [Theory]
    [InlineData(false, "@uniq")]
    [InlineData(true, "")]
    [InlineData(false, "@move")]
    public void OnlyTheSelectedEntryAuthorizesIteration(bool exclusive, string acquisition)
        => Assert.False(MinimalEmissionTest.Analyze(Declarations(exclusive) + "var values = Values.init()\nfor _ in values" + acquisition + " => ()").Binding.Result.IsComplete);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoopProtectsItsBorrowedSource(bool exclusive)
    {
        var c = MinimalEmissionTest.Analyze(Declarations(exclusive) + "var values = Values.init()\nfor item in values" + (exclusive ? "@uniq" : string.Empty) + "\n    values = Values.init()\n    require item == 7 else => $abort(\"source\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    [InlineData("origin b outlives a", false)]
    public void EnumPayloadShorteningNeedsProof(string relation, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("func f(value: ref/i32 during a, other: ref/i32 during b) -> Option<ref/i32 during b>\n    " + relation + "\n    return .Some(value)");
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
