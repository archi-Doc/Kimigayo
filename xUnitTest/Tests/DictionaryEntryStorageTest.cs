// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DictionaryEntryStorageTest
{
    [Fact]
    public void UnitKeysAndValuesUseLinksWithoutPayloadBytes()
    {
        const string Source = """
            var entries: Dictionary<(), ()> = [:]
            _ = entries.tryInsert((), ())
            match entries.tryInsert((), ())
                .Err(((), ())) => ()
                .Ok(()) => $abort("duplicate")
            require entries.length == 1 else => $abort("length")
            _ = entries.remove(())
            require entries.length == 0 else => $abort("remove")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryEntryStorageUnit", Source, 1, 1, 64);
    }

    [Fact]
    public void BooleanKeysAndValuesUseByteStorage()
    {
        const string Source = """
            var entries: Dictionary<bool, bool> = [:]
            _ = entries.tryInsert(true, false)
            _ = entries.tryInsert(false, true)
            match entries.tryGet(false)
                .Some(let value) => require value else => $abort("bool")
                .None => $abort("missing")
            match entries.insertOrReplace(true, true)
                .Some(let old) => require not old else => $abort("replace")
                .None => $abort("missing")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryEntryStorageBool", Source, 1, 1, 96);
    }

    [Fact]
    public void GenericOperationsUseTheirConcreteEqualityWitness()
    {
        const string Source = """
            func insert<K, V>(entries: uniq/Dictionary<K, V>, key: K, value: V) -> Result<(), (K, V)>
                K is Equatable
                return entries.tryInsert(key@move, value@move)
            var entries: Dictionary<(i32, i32), string> = [:]
            _ = insert(entries@uniq, (1, 2), "value")
            match entries.tryGet((1, 2))
                .Some(let value) => Console.writeLine(value)
                .None => $abort("missing")
            """;
        NativeAllocationAudit.WriteFixture("DictionaryEntryStorageGeneric", Source, 1, 1, 192, "value\n");
    }

    [Fact]
    public void ZeroSizedValuesStillRunTheirDestructors()
    {
        const string Source = """
            struct Empty
                public init() => ()
                deinit => Console.writeLine("destroyed")
            var entries: Dictionary<i32, Empty> = [:]
            _ = entries.tryInsert(1, Empty.init())
            entries.clear()
            """;
        NativeAllocationAudit.WriteFixture("DictionaryEntryStorageEmptyDestructor", Source, 1, 1, 96, "destroyed\n");
    }
}
