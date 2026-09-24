// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryOperationsTest
{
    [Fact]
    public void MilestonePrefixPreservesInputAndStoredEntryDestructionOrder()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone31.kimi")));
        var end = source.IndexOf("    entries[Key.init(1, 0)]", StringComparison.Ordinal);
        Assert.True(end > 0);
        const string Output = "Duplicate returned both inputs.\nRejected item destroyed.\nRejected key destroyed.\nReplacement key destroyed.\nReplacement returned item 10.\nItem 10 destroyed.\nItem 40 destroyed.\nKey 4 destroyed.\nItem 20 destroyed.\nKey 2 destroyed.\nItem 11 destroyed.\nKey 1 destroyed.\n";
        NativeAllocationAudit.WriteFixture("DictionaryOperationsMilestonePrefix", source[..end], 1, 1, 128, Output);
    }

    [Fact]
    public void InsertRejectReplaceLookupRemoveAndClearPreserveTheirResults()
    {
        const string Source = """
            var entries: Dictionary<i32, i32> = [:]
            match entries.tryInsert(1, 20)
                .Ok(()) => ()
                .Err(_) => $abort("insert")
            match entries.tryInsert(1, 99)
                .Ok(()) => $abort("duplicate")
                .Err((let key, let value)) => require key == 1 and value == 99 else => $abort("inputs")
            match entries.insertOrReplace(1, 22)
                .Some(let old) => require old == 20 else => $abort("replace")
                .None => $abort("absent")
            match entries.tryGet(1)
                .Some(let found) => require found == 22 else => $abort("lookup")
                .None => $abort("absent")
            match entries.remove(1)
                .Some((let key, let value)) => require key == 1 and value == 22 else => $abort("remove")
                .None => $abort("absent")
            require entries.length == 0 else => $abort("length")
            match entries.remove(1)
                .None => ()
                .Some(_) => $abort("absent removal")
            match entries.tryGet(1)
                .None => ()
                .Some(_) => $abort("absent lookup")
            entries.clear()
            """;
        NativeAllocationAudit.WriteFixture("DictionaryOperationsScalar", Source, 1, 1, 96);
    }

    [Fact]
    public void FullCapacityChurnReusesSlotsWithoutAllocations()
    {
        const string Source = """
            var entries: Dictionary<i32, i32> = [:]
            entries.reserve(4)
            _ = entries.tryInsert(1, 10)
            _ = entries.tryInsert(2, 20)
            _ = entries.tryInsert(3, 30)
            _ = entries.tryInsert(4, 40)
            var i = 0
            while i < 2048
                _ = entries.remove(2)
                _ = entries.remove(3)
                _ = entries.tryInsert(3, 30)
                _ = entries.tryInsert(2, 20)
                entries.reserve(0)
                require entries.length == 4 else => $abort("length")
                i += 1
            entries.clear()
            require entries.length == 0 and entries.capacity >= 4 else => $abort("clear")
            _ = entries.tryInsert(7, 70)
            """;
        NativeAllocationAudit.WriteFixture("DictionaryOperationsChurn", Source, 1, 1, 96);
    }

    [Fact]
    public void SearchUsesStoredKeyAndReplacementRetainsIt()
    {
        const string Source = """
            struct Key
                Self is Equatable
                public let code: i32
                public let tag: i32
                public init(code: i32, tag: i32)
                    self.code = code
                    self.tag = tag
                public func equals(self: ref/Self, other: ref/Self) -> bool
                    require self.tag == 1 else => $abort("wrong comparison direction")
                    return self.code == other.code
            var entries: Dictionary<Key, i32> = [:]
            _ = entries.tryInsert(Key.init(7, 1), 20)
            _ = entries.insertOrReplace(Key.init(7, 9), 22)
            match entries.remove(Key.init(7, 0))
                .Some((let key, let value)) => require key.tag == 1 and value == 22 else => $abort("stored key")
                .None => $abort("missing")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryOperationsDirection", Source, 1, 1, 128);
    }

    [Fact]
    public void RemovalReinsertionAndClearKeepReverseInsertionCleanup()
    {
        const string Source = """
            struct Item
                public let name: string
                public init(name: string) => self.name = name@move
                deinit => Console.writeLine(self.name)
            var entries: Dictionary<i32, Item> = [:]
            entries.reserve(3)
            _ = entries.tryInsert(1, Item.init("first"))
            _ = entries.tryInsert(2, Item.init("second"))
            _ = entries.tryInsert(3, Item.init("third"))
            _ = entries.remove(2)
            _ = entries.tryInsert(2, Item.init("again"))
            entries.clear()
            _ = entries.tryInsert(4, Item.init("last"))
            """;
        NativeAllocationAudit.WriteFixture("DictionaryOperationsCleanup", Source, 1, 1, 192, "second\nagain\nthird\nfirst\nlast\n");
    }

    [Fact]
    public void AReturnedLookupBorrowProtectsTheWholeDictionary()
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 42)\nlet found = entries.tryGet(1)\nentries.clear()\nmatch found\n    .Some(let value) => require value == 42 else => $abort(\"value\")\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void TupleKeyEqualityUsesTheContractIncludingNaNs()
    {
        const string Source = """
            let nan: f64 = 0.0 / 0.0
            var entries: Dictionary<(f64, i32), i32> = [:]
            _ = entries.tryInsert((nan, 7), 42)
            match entries.tryInsert((-nan, 7), 99)
                .Err((_, let rejected)) => require rejected == 99 else => $abort("inputs")
                .Ok(()) => $abort("NaN key")
            match entries.tryGet((nan, 7))
                .Some(let found) => require found == 42 else => $abort("lookup")
                .None => $abort("missing")
            require not (nan == nan) else => $abort("IEEE operator")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryOperationsTupleNaN", Source, 1, 1, 160);
    }

    [Fact]
    public void StringKeyTemporariesDoNotLimitTheReturnedValueBorrow()
    {
        const string Source = """
            func search() -> string => "key"
            var entries: Dictionary<string, string> = [:]
            _ = entries.tryInsert("key", "value")
            let result = entries.tryGet(search())
            match result
                .Some(let found) => Console.writeLine(found)
                .None => $abort("missing")
            entries.clear()
            """;
        NativeAllocationAudit.WriteFixture("DictionaryOperationsString", Source, 1, 1, 256, "value\n");
    }
}
