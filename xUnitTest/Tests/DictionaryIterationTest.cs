// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryIterationTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedIterationRetainsInsertionOrderAfterSlotReuse(bool pair)
    {
        const string Source = """
            var entries: Dictionary<i32, i32> = [:]
            _ = entries.tryInsert(1, 10)
            _ = entries.tryInsert(2, 20)
            _ = entries.tryInsert(3, 30)
            _ = entries.remove(2)
            _ = entries.tryInsert(4, 40)
            var order = 0
            for (key, value) in entries
                let keyReference: ref/i32 = key
                let valueReference: ref/i32 = value
                require valueReference == keyReference * 10 else => $abort("pair")
                order = order * 10 + keyReference
            require order == 134 and entries.length == 3 else => $abort("order")
            """;
        var source = pair ? Source.Replace("for (key, value) in entries", "for pair in entries", StringComparison.Ordinal)
            .Replace("= key\n", "= pair.0\n", StringComparison.Ordinal).Replace("= value\n", "= pair.1\n", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("DictionaryIterationShared" + pair, source, 1, 1, 96);
    }

    [Theory]
    [InlineData("entries.clear()")]
    [InlineData("entries.reserve(additional: 1)")]
    [InlineData("_ = entries@move")]
    public void SharedIterationProtectsTheHandleThroughTheBody(string mutation)
    {
        var source = "var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 2)\nfor (key, value) in entries\n    " + mutation + "\n    require key + value == 3 else => $abort(\"borrow\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwningIterationTransfersPairsAndCleansTheRemainingTail(bool earlyExit)
    {
        const string Source = """
            struct Key
                Self is Equatable
                public let number: i32
                public init(number: i32) => self.number = number
                public func equals(self: ref/Self, other: ref/Self) -> bool => self.number == other.number
                deinit => Console.writeLine("key")
            struct Item
                public let name: string
                public init(name: string) => self.name = name@move
                deinit => Console.writeLine(self.name)
            var entries: Dictionary<Key, Item> = [:]
            _ = entries.tryInsert(Key.init(1), Item.init("one"))
            _ = entries.tryInsert(Key.init(2), Item.init("two"))
            _ = entries.tryInsert(Key.init(3), Item.init("three"))
            for (key, value) in entries@move
                require key.number > 0 else => $abort("key")
                Console.writeLine(value.name)
                continue
            """;
        var source = earlyExit ? Source.Replace("continue", "exit", StringComparison.Ordinal) : Source;
        var expected = earlyExit ? "one\none\nkey\nthree\nkey\ntwo\nkey\n" : "one\none\nkey\ntwo\ntwo\nkey\nthree\nthree\nkey\n";
        NativeAllocationAudit.WriteFixture("DictionaryIterationOwner" + earlyExit, source, 1, 1, 192, expected);
    }

    [Fact]
    public void EmptyOwningAndSharedIterationsAllocateNothing()
    {
        const string Source = """
            var entries: Dictionary<(), ()> = [:]
            for (key, value) in entries => $abort("empty shared")
            for (key, value) in entries@move => $abort("empty owner")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIterationEmpty", Source, 0, 0, 0);
    }

    [Fact]
    public void OwningPairBindingTransfersStrings()
    {
        const string Source = """
            var entries: Dictionary<string, string> = [:]
            _ = entries.tryInsert("key", "value")
            for pair in entries@move
                Console.writeLine(pair.0)
                Console.writeLine(pair.1)
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIterationPair", Source, 1, 1, 256, "key\nvalue\n");
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    public void BorrowedParametersUseSharedPairs(string semantics)
    {
        const string Source = """
            func inspect(entries: ref/Dictionary<i32, string>)
                for (key, value) in entries
                    require key == 1 else => $abort("key")
                    Console.writeLine(value)
            var entries: Dictionary<i32, string> = [:]
            _ = entries.tryInsert(1, "value")
            inspect(entries@ref)
            """;
        var source = semantics == "uniq" ? Source.Replace("ref/Dictionary", "uniq/Dictionary", StringComparison.Ordinal).Replace("entries@ref", "entries@uniq", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("DictionaryIterationBorrow" + semantics, source, 1, 1, 192, "value\n");
    }

    [Fact]
    public void GenericOwningIteratorUsesConcreteComponents()
    {
        const string Source = """
            func consume<K, V>(entries: Dictionary<K, V>)
                K is Equatable
                for (key, value) in entries@move
                    _ = key@move
                    _ = value@move
            var entries: Dictionary<i32, string> = [:]
            _ = entries.tryInsert(1, "value")
            consume(entries@move)
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIterationGeneric", Source, 1, 1, 192);
    }

    [Fact]
    public void NonEmptyUnitPairsStillAdvance()
    {
        const string Source = """
            var entries: Dictionary<(), ()> = [:]
            _ = entries.tryInsert((), ())
            var count = 0
            for (key, value) in entries@move => count += 1
            require count == 1 else => $abort("count")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryIterationUnit", Source, 1, 1, 64);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredReferencesRetainTheirExternalOrigins(bool owning)
    {
        const string Source = """
            var value: i32 = 42
            let input = value@ref
            var entries: Dictionary<i32, ref/i32 during input> = [:]
            _ = entries.tryInsert(1, input)
            for (key, stored) in entries
                let reference: ref/i32 = stored
                value = 7
                require reference == 42 else => $abort("origin")
            """;
        var source = owning ? Source.Replace("in entries\n", "in entries@move\n", StringComparison.Ordinal) : Source;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
