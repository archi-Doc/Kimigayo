// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ArraySortTest
{
    [Theory]
    [InlineData("i8")]
    [InlineData("u8")]
    [InlineData("i16")]
    [InlineData("u16")]
    [InlineData("i32")]
    [InlineData("u32")]
    [InlineData("i64")]
    [InlineData("u64")]
    [InlineData("i128")]
    [InlineData("u128")]
    [InlineData("isize")]
    [InlineData("usize")]
    public void UsesEachPrimitiveIntegerWitness(string type)
        => ScalarEmissionTest.EmitFixture("ArraySortType" + type, $"var values: Array<{type}> = [3, 1, 2]\nvalues.sort()\nrequire values[0] == 1 and values[1] == 2 and values[2] == 3 else => $abort(\"order\")", string.Empty);

    [Theory]
    [InlineData("String", "string", "\"c\", \"a\", \"b\"", "\"a\"", "\"b\"", "\"c\"")]
    [InlineData("Tuple", "(i32, i32)", "(1, 2), (0, 9), (1, 1)", "(0, 9)", "(1, 1)", "(1, 2)")]
    public void UsesCompositeAndStringWitnesses(string name, string type, string elements, string first, string second, string third)
        => ScalarEmissionTest.EmitFixture("ArraySort" + name, $"var values: Array<{type}> = [{elements}]\nvalues.sort()\nrequire values[0] == {first} and values[1] == {second} and values[2] == {third} else => $abort(\"order\")", string.Empty);

    [Theory]
    // SPEC 10: a receiver that cannot acquire uniq makes this overload inapplicable.
    [InlineData("let values: Array<i32> = [1]\nvalues.sort()", "NoApplicableOverload_Kd")]
    [InlineData("var values: Array<i32> = [1]\nlet view = values[..]\nvalues.sort()\nlet count = view.length", "CallActivationConflict_Kd")]
    [InlineData("var values: Array<i32> = []\nlet view = values[..]\nvalues.sort()\nlet count = view.length", "CallActivationConflict_Kd")]
    public void SortingRequiresExclusiveAccess(string source, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        Assert.False(c.Emission.Validate(out _));
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == code);
    }

    // SPEC 4.7.2: sort uses Comparable.compare, preserves each element and allocates no storage.
    [Theory]
    [InlineData("Empty", "")]
    [InlineData("Single", "42")]
    [InlineData("Pair", "2,1")]
    [InlineData("Sorted", "1,2,3,4,5,6,7")]
    [InlineData("Reverse", "7,6,5,4,3,2,1")]
    [InlineData("Duplicates", "3,1,3,2,1,3,2,1")]
    [InlineData("Equal", "2,2,2,2,2")]
    [InlineData("Extremes", "2147483647,-2147483648,0,-1,1")]
    public void SortsEveryElementInPlace(string name, string input)
    {
        var expected = input.Length == 0 ? [] : input.Split(',').Select(int.Parse).Order().ToArray();
        var source = $"var values: Array<i32> = [{input}]\nlet capacity = values.capacity\nvalues.sort()\n" +
            $"require values.length == {expected.Length} and values.capacity == capacity else => $abort(\"shape\")\n";
        for (var i = 0; i < expected.Length; i++)
        {
            source += $"require values[{i}] == {expected[i]} else => $abort(\"element {i}\")\n";
        }

        ScalarEmissionTest.EmitFixture("ArraySort" + name, source, string.Empty);
    }

    [Fact]
    public void GenericSortUsesTheUserWitnessWithoutCopyingOrDestroyingElements()
    {
        const string source = """
            struct Key
                Self is Comparable
                public let id: i32
                public init(id: i32) => self.id = id
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.id == other.id
                public func compare(self: ref/Self, other: ref/Self) -> i32
                    if self.id > other.id => return -17
                    if self.id < other.id => return 41
                    return 0
                drop
                    if self.id == 1 => Console.writeLine("drop 1")
                    if self.id == 2 => Console.writeLine("drop 2")
                    if self.id == 3 => Console.writeLine("drop 3")
            func order<T>(values: uniq/Array<T>)
                T is Comparable
                values.sort()
            var values: Array<Key> = [Key.init(2), Key.init(1), Key.init(3)]
            order(values@uniq)
            require values[0].id == 3 and values[1].id == 2 and values[2].id == 1 else => $abort("witness")
            Console.writeLine("sorted")
            """;
        ScalarEmissionTest.EmitFixture("ArraySortWitness", source, "sorted\ndrop 1\ndrop 2\ndrop 3\n");
    }

    [Fact]
    public void ConcreteCallbackSortKeepsTheSameHeapTraversal()
    {
        const string source = """
            var values: Array<i32> = [3, 1, 5, 2, 4]
            let compare = func (left: ref/i32, right: ref/i32) -> i32 => right - left
            values.sort(by: compare)
            require values[0] == 5 and values[1] == 4 and values[2] == 3 and values[3] == 2 and values[4] == 1 else => $abort("callback order")
            """;
        ScalarEmissionTest.EmitFixture("ArraySortCallback", source, string.Empty);
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("f64")]
    [InlineData("bool")]
    [InlineData("()")]
    public void NonComparableElementsDoNotAcquireASortOperation(string type)
    {
        var c = MinimalEmissionTest.Analyze($"struct Item\nvar values: Array<{type}> = []\nvalues.sort()");
        c.Binding.ReportDiagnostics();
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(TestDiagnostics.Of(c), static x => x.Code == "NoApplicableOverload_Kd");
        Assert.False(c.Emission.Validate(out _));
    }
}
