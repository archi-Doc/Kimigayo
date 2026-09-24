// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DictionaryShrinkTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShrinkSuccessAndFailurePreserveEntriesAndPermitFurtherMutation(bool fail)
    {
        const string Source = """
            var entries: Dictionary<i32, i32> = [:]
            entries.reserve(8)
            _ = entries.tryInsert(1, 10)
            _ = entries.tryInsert(2, 20)
            _ = entries.tryInsert(3, 30)
            _ = entries.tryInsert(4, 40)
            _ = entries.remove(2)
            _ = entries.remove(3)
            entries.shrinkToFit()
            require entries.length == 2 and entries.capacity >= 2 else => $abort("capacity")
            match entries.tryGet(4)
                .Some(let value) => require value == 40 else => $abort("value")
                .None => $abort("missing")
            _ = entries.remove(1)
            _ = entries.tryInsert(5, 50)
            match entries.remove(5)
                .Some((let key, let value)) => require key == 5 and value == 50 else => $abort("reuse")
                .None => $abort("missing")
            """;
        var source = fail ? Source.Replace("entries.shrinkToFit()", "entries.shrinkToFit()\nrequire entries.capacity == 8 else => $abort(\"failed shrink changed capacity\")", StringComparison.Ordinal) : Source;
        NativeAllocationAudit.WriteFixture("DictionaryShrink" + fail, source, 2, fail ? 1 : 2, 240, failAllocation: fail ? 2 : 0);
    }

    [Fact]
    public void ShrinkingAnEmptyCollectionReleasesItsBufferWithoutAllocation()
        => NativeAllocationAudit.WriteFixture("DictionaryShrinkEmpty", "var entries: Dictionary<i32, i32> = [:]\nentries.shrinkToFit()\nentries.reserve(8)\nentries.shrinkToFit()\nrequire entries.length == 0 and entries.capacity == 0 else => $abort(\"empty\")\nentries.shrinkToFit()", 1, 1, 192);

    [Fact]
    public void CompactionPreservesDestructionOrderWithoutCallingUserCode()
    {
        const string Source = """
            struct Item
                public let id: i32
                public init(id: i32) => self.id = id
                deinit
                    match self.id
                        1 => Console.writeLine("one")
                        2 => Console.writeLine("two")
                        3 => Console.writeLine("three")
                        _ => Console.writeLine("four")
            var entries: Dictionary<i32, Item> = [:]
            entries.reserve(8)
            _ = entries.tryInsert(1, Item.init(1))
            _ = entries.tryInsert(2, Item.init(2))
            _ = entries.tryInsert(3, Item.init(3))
            _ = entries.remove(2)
            _ = entries.tryInsert(4, Item.init(4))
            _ = entries.remove(1)
            entries.shrinkToFit()
            Console.writeLine("compacted")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryShrinkOrder", Source, 2, 2, 240, "two\none\ncompacted\nfour\nthree\n");
    }
}
