// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class CopySnapshotTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedReferencesRetainTheirOwnLoans(bool conflict)
    {
        var source = "func copy<T>(value: ref/T) -> T\n    T is Copy\n    return value\nvar value = 7\nlet pair = (value@ref, 1)\nlet borrowed = pair@ref\nlet snapshot = copy(borrowed)\n" +
            (conflict ? "value = 9\n" : string.Empty) + "let read: i32 = snapshot.0\nrequire read == 7 else => $abort(\"nested origin\")";
        if (conflict)
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
            Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
        else
        {
            ScalarEmissionTest.EmitFixture("CopySnapshotNestedReference", source, string.Empty);
        }
    }

    [Fact]
    public void EnumSnapshotPreservesActiveCase()
    {
        const string source = """
            enum Choice
                Self is Copy
                A(i32)
                B
            func read(value: ref/Choice) -> Choice => value
            var value: Choice = .A(7)
            let snapshot = read(value@ref)
            value = .B
            match snapshot
                .A(7) => ()
                _ => $abort("payload")
            match read(value@ref)
                .B => ()
                _ => $abort("empty")
            """;
        ScalarEmissionTest.EmitFixture("CopySnapshotEnum", source, string.Empty);
    }

    [Fact]
    public void SharedStructComparisonSnapshotsOperands()
    {
        const string source = """
            struct Key
                Self is Copy
                Self is Comparable
                public var value: i32
                public init(value: i32) => self.value = value
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.value == other.value
                public func compare(self: ref/Self, other: ref/Self) -> i32
                    if self.value < other.value => return -1
                    if self.value > other.value => return 1
                    return 0
            func same(a: ref/Key, b: ref/Key) -> bool => a == b
            func before(a: ref/Key, b: ref/Key) -> bool => a < b
            func change(value: uniq/Key) -> Key
                value.value = 9
                return Key.init(8)
            let a = Key.init(7)
            let b = Key.init(8)
            require same(a@ref, a@ref) and not same(a@ref, b@ref) and before(a@ref, b@ref) else => $abort("calls")
            require a@ref == a@ref and a@ref < b@ref else => $abort("direct")
            var mutable = Key.init(7)
            require mutable@ref < change(mutable@uniq) and mutable.value == 9 else => $abort("evaluation order")
            Console.writeLine("Snapshots compared.")
            """;
        ScalarEmissionTest.EmitFixture("CopySnapshotComparison", source, "Snapshots compared.\n");
    }

    [Theory]
    [InlineData("Unit", "()", "()", "()", "true")]
    [InlineData("Tuple", "(i32, bool)", "(7, true)", "(9, false)", "snapshot.0 == 7 and value.0 == 9")]
    [InlineData("Array", "[2 of i32]", "[7, 8]", "[9, 10]", "snapshot[0] == 7 and value[0] == 9")]
    public void ReturnedSnapshotHasIndependentStorage(string name, string type, string initial, string replacement, string condition)
    {
        var source = $"func read(value: ref/{type}) -> {type} => value\nvar value: {type} = {initial}\nlet snapshot = read(value@ref)\nvalue = {replacement}\nrequire {condition} else => $abort(\"snapshot\")";
        ScalarEmissionTest.EmitFixture("CopySnapshot" + name, source, string.Empty);
    }
}
