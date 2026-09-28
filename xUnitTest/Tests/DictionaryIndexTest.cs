// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryIndexTest
{
    [Fact]
    public void CopyReadAndReplacementPreserveTheStoredKey()
    {
        const string Source = """
            var entries: Dictionary<i32, i32> = [:]
            _ = entries.tryInsert(1, 2)
            entries[1] = 3
            require entries[1] == 3 else => $abort("read")
            entries[1] += 4
            require entries[1] == 7 else => $abort("update")
            require entries.length == 1 else => $abort("length")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIndexScalar", Source, 1, 1, 96);
    }

    [Fact]
    public void SimpleReplacementSecuresRhsThenBorrowsKeyThenDestroysOldValue()
    {
        const string Source = """
            struct Item
                public let name: string
                public init(name: string) => self.name = name@move
                deinit => Console.writeLine(self.name)
            func key() -> i32
                Console.writeLine("key")
                return 1
            func rhs() -> Item
                Console.writeLine("rhs")
                return Item.init("new")
            var entries: Dictionary<i32, Item> = [:]
            _ = entries.tryInsert(1, Item.init("old"))
            entries[key()] = rhs()
            Console.writeLine("placed")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIndexOrder", Source, 1, 1, 192, "rhs\nkey\nold\nplaced\nnew\n");
    }

    [Fact]
    public void TemporaryNonCopyKeyIsBorrowedAndDestroyedOnce()
    {
        const string Source = """
            struct Key
                Self is Equatable
                public let number: i32
                public let name: string
                public init(number: i32, name: string)
                    self.number = number
                    self.name = name@move
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.number == other.number
                deinit => Console.writeLine(self.name)
            var entries: Dictionary<Key, i32> = [:]
            _ = entries.tryInsert(Key.init(1, "stored"), 2)
            entries[Key.init(1, "search")] = 3
            for (key, value) in entries => require value == 3 else => $abort("value")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIndexNonCopyKey", Source, 1, 1, 224, "search\nstored\n");
    }

    [Theory]
    [InlineData("entries[entries[1]] = 7")]
    [InlineData("match entries.tryGet(1)@move\n    .Some(let key) => entries[key] = 7\n    .None => ()")]
    public void KeyLoanCannotEndBeforeReplacement(string replacement)
    {
        var source = "var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 1)\n" + replacement;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void IndependentStoredKeyCanBeBorrowedWithoutCopyingItsReferent()
    {
        const string Source = """
            var keys: Dictionary<i32, i32> = [:]
            _ = keys.tryInsert(1, 2)
            var values: Dictionary<i32, i32> = [:]
            _ = values.tryInsert(2, 3)
            values[keys[1]] = 4
            require values[2] == 4 else => $abort("lookup")
            let key = 2
            values[key@ref] = 5
            require values[2] == 5 else => $abort("reference")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIndexBorrowedKey", Source, 2, 2, 192);
    }

    [Fact]
    public void GenericIndexUsesTheConcreteEqualityWitness()
    {
        const string Source = """
            func consume<K, V>(entries: Dictionary<K, V>, key: ref/K, value: V)
                K is Equatable
                var owned = entries@move
                owned[key] = value@move
            var entries: Dictionary<(i32, i32), string> = [:]
            _ = entries.tryInsert((1, 2), "old")
            consume(entries@move, (1, 2), "new")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIndexGeneric", Source, 1, 1, 192);
    }

    [Fact]
    public void MissingKeyAbortsAfterTheRhsAndBeforePlacement()
    {
        const string Source = """
            func rhs() -> i32
                Console.writeLine("rhs")
                return 7
            var entries: Dictionary<i32, i32> = [:]
            entries[1] = rhs()
            Console.writeLine("unreachable")
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var failure), MinimalEmissionTest.Describe(c, failure));
        ScalarEmissionTest.WriteFixture("DictionaryIndexMissing", output.ToString(), "rhs\n", 1, "Hello.kimi:5:1: abort KIMI_E_MISSING_KEY: Dictionary key was not found\n");
    }

    [Fact]
    public void Milestone31RunsUnchangedWithOneBufferAllocation()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone31.kimi")));
        const string Output = "Duplicate returned both inputs.\nRejected item destroyed.\nRejected key destroyed.\nReplacement key destroyed.\nReplacement returned item 10.\nItem 10 destroyed.\nItem 11 destroyed.\nStored keys and insertion order preserved.\nRemoved the original key and item.\nItem 12 destroyed.\nKey 1 destroyed.\nOwning iteration starts at key 2.\nItem 20 destroyed.\nKey 2 destroyed.\nItem 30 destroyed.\nKey 3 destroyed.\nItem 40 destroyed.\nKey 4 destroyed.\nDictionary run finished.\n";
        NativeAllocationAudit.WriteFixture("DictionaryIndexMilestone31", source, 1, 1, 128, Output);
    }
}
