// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayIndexTest
{
    [Fact]
    public void InsertsAndRemovesUsingSavedAndFromEndIndices()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexOperations",
            "var values: Array<i32> = []\nvalues@uniq.insert(^0, 10)\nvalues@uniq.insert(^0, 30)\nlet beforeLast = ^1\nvalues@uniq.insert(beforeLast, 20)\nvalues@uniq.insert(Index.init(0), 1)\nrequire values.length == 4 and values[0] == 1 and values[1] == 10 and values[2] == 20 and values[3] == 30 else => $abort(\"insert\")\nlet capacity = values.capacity\nlet last = values@uniq.remove(^1)\nlet first = values@uniq.remove(Index.init(0))\nrequire last == 30 and first == 1 and values.length == 2 and values.capacity == capacity else => $abort(\"remove\")\nlet middle = values@uniq.remove(beforeLast)\nrequire middle == 20 and values[0] == 10 else => $abort(\"saved\")",
            string.Empty);

    [Fact]
    public void IndexOperationsTransferNonCopyElements()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexStrings",
            "var values: Array<string> = [\"first\"]\nvalues@uniq.insert(^0, \"last\")\nlet last = values@uniq.remove(^1)\nConsole.writeLine(last)\nlet first = values@uniq.remove(Index.init(0))\nConsole.writeLine(first)",
            "last\nfirst\n");

    [Theory]
    [InlineData("RemoveEnd", "let n = values@uniq.remove(^0)", "Hello.kimi:4:9")]
    [InlineData("RemoveBeforeStart", "let n = values@uniq.remove(^2)", "Hello.kimi:4:9")]
    [InlineData("InsertBeforeStart", "values@uniq.insert(^2, 7)", "Hello.kimi:4:1")]
    [InlineData("InsertAfterEnd", "values@uniq.insert(Index.init(2), 7)", "Hello.kimi:4:1")]
    public void ChecksResolvedBounds(string name, string statement, string location)
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexBounds" + name,
            "var values: Array<i32> = []\nvalues@uniq.append(1)\nConsole.writeLine(\"before\")\n" + statement + "\nConsole.writeLine(\"after\")",
            "before\n",
            1,
            location + ": abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Fact]
    public void NegativeIndexPreventsLaterArgumentEvaluation()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexNegativeOrder",
            "func value() -> i32\n    Console.writeLine(\"value\")\n    return 1\nvar values: Array<i32> = []\nvalues@uniq.insert(^(-1), value())",
            string.Empty,
            1,
            "Hello.kimi:5:20: abort KIMI_E_ARGUMENT: Invalid argument value\n");

    [Fact]
    public void BoundsAreCheckedAfterLaterArguments()
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayIndexBoundsAfterValue",
            "func value() -> i32\n    Console.writeLine(\"value\")\n    return 1\nvar values: Array<i32> = []\nvalues@uniq.insert(^1, value())",
            "value\n",
            1,
            "Hello.kimi:5:1: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

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
