// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayIndexTest
{
    // SPEC 4.7.2: insert, remove, swap, swapRemove and the try-prefixed accesses resolve a position of any Type.
    private const string Operations =
        "var values: Array<i32> = []\nvalues@uniq.insert(^0, 10)\nvalues@uniq.insert(^0, 30)\nlet beforeLast = ^1\nvalues@uniq.insert(beforeLast, 20)\nvalues@uniq.insert(0@u8, 1)\nrequire values.length == 4 and values[0] == 1 and values[1] == 10 and values[2] == 20 and values[3] == 30 else => $abort(\"insert\")\nlet capacity = values.capacity\nlet last = values@uniq.remove(^1)\nlet first = values@uniq.remove(0@i64)\nrequire last == 30 and first == 1 and values.length == 2 and values.capacity == capacity else => $abort(\"remove\")\nlet middle = values@uniq.remove(beforeLast)\nrequire middle == 20 and values[0] == 10 else => $abort(\"saved\")\n" +
        "values@uniq.append(40)\nvalues@uniq.swap(0, ^1)\nrequire values[0] == 40 and values[1] == 10 else => $abort(\"swap\")\nlet moved = values@uniq.swapRemove(0@u16)\nrequire moved == 40 and values.length == 1 and values[0] == 10 else => $abort(\"swapRemove\")\n" +
        "match values@uniq.tryGetUniq(^1)\n    .Some(let slot) => slot@follow = 11\n    .None => $abort(\"uniq\")\nmatch values.tryGet(^2)\n    .Some(_) => $abort(\"before start\")\n    .None => ()\nrequire values[0] == 11 else => $abort(\"slot\")";

    [Fact]
    public void InsertsAndRemovesUsingSavedAndFromEndIndices()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexOperations",
            Operations,
            string.Empty);

    [Fact]
    public void IndexOperationsTransferNonCopyElements()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexStrings",
            "var values: Array<string> = [\"first\"]\nvalues@uniq.insert(^0, \"last\")\nlet last = values@uniq.remove(^1)\nConsole.writeLine(last)\nlet first = values@uniq.remove(0)\nConsole.writeLine(first)",
            "last\nfirst\n");

    [Theory]
    [InlineData("RemoveEnd", "let n = values@uniq.remove(^0)", "self.removeAt(")]
    [InlineData("RemoveBeforeStart", "let n = values@uniq.remove(^2)", "self.removeAt(")]
    [InlineData("InsertBeforeStart", "values@uniq.insert(^2, 7)", "self.insertAt(")]
    [InlineData("InsertAfterEnd", "values@uniq.insert(2@u8, 7)", "self.insertAt(")]
    [InlineData("InsertWide", "values@uniq.insert(18446744073709551615@u64, 7)", "self.insertAt(")]
    public void ChecksResolvedBounds(string name, string statement, string anchor)
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexBounds" + name,
            "var values: Array<i32> = []\nvalues@uniq.append(1)\nConsole.writeLine(\"before\")\n" + statement + "\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            LibrarySource.Location("Array.kimi", anchor) + ": abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    // SPEC 4.6.2, 4.7.2: `^x` construction checks nothing, so later arguments are evaluated before resolution fails.
    [Fact]
    public void NegativeFromEndFailsOnlyInResolution()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexNegativeOrder",
            "func value() -> i32\n    Console.writeLine(\"value\")\n    return 1\nvar values: Array<i32> = []\nvalues@uniq.insert(^(-1), value())",
            "value\n",
            1,
            LibrarySource.Location("Array.kimi", "self.insertAt(") + ": abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void BoundsAreCheckedAfterLaterArguments()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexBoundsAfterValue",
            "func value() -> i32\n    Console.writeLine(\"value\")\n    return 1\nvar values: Array<i32> = []\nvalues@uniq.insert(^1, value())",
            "value\n",
            1,
            LibrarySource.Location("Array.kimi", "self.insertAt(") + ": abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void ReceiverReservationAllowsTemporarySharedInspection()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexReservation",
            "var values: Array<isize> = [1]\nvalues.insert(^0, values.length)\nrequire values.length == 2 and values[1] == 1 else => $abort(\"reservation\")",
            string.Empty);

    [Theory]
    [InlineData("values@ref.insert(^0, 1)")]
    [InlineData("values@uniq.insert(true, 1)")]
    [InlineData("values@uniq.remove(true)")]
    public void RejectsWrongReceiversAndIndices(string statement)
    {
        var c = MinimalEmissionTest.Analyze("var values: Array<i32> = []\n" + statement);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.NoApplicableOverload_Kd or DiagnosticCode.ExclusiveBorrowRequired_Kd);
    }
}
