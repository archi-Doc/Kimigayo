// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2.5: the shared Dictionary remainder lends each live entry once, in insertion order, as
/// (ref/K, ref/V) for the Dictionary's Loan, and returns None when nothing remains.</summary>
public class DictionaryRemainderTest
{
    // A probe inside the Kimi Kotonoha, since user source cannot reach Kimi.Storage.
    private const string Probe = """
        public group StorageProbe
            public func weighted(values: ref/Dictionary<i32, i32>) -> i32
                var state = Storage.borrowStorage(values)
                var total: i32 = 0
                var position: i32 = 1
                loop
                    match Storage.splitFirst(state@uniq)
                        .Some(let entry)
                            let key: i32 = entry.0
                            let value: i32 = entry.1
                            require value == key * 10 else => $abort("pair")
                            total += key * position
                            position += 1
                        .None => exit
                return total
        """;

    [Fact]
    public void SharedRemainderLendsLiveEntriesInInsertionOrder()
    {
        var c = CompilationTestHelper.ParseSuccess("""
            var map: Dictionary<i32, i32> = [:]
            _ = map.tryInsert(2, 20)
            _ = map.tryInsert(1, 10)
            _ = map.tryInsert(3, 30)
            _ = map.remove(1)
            _ = map.tryInsert(4, 40)
            require Kimi.StorageProbe.weighted(map@ref) == 20 else => $abort("order")
            let empty: Dictionary<i32, i32> = [:]
            require Kimi.StorageProbe.weighted(empty@ref) == 0 else => $abort("empty")
            Console.writeLine("dictionary storage")
            """);
        c.Library.Kotonoha.CreateCodeContext().Parse(c.Library.Kotonoha.RootKoto, Probe);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.WriteFixture("DictionaryRemainderShared", CompilationTestHelper.WriteIr(c), "dictionary storage\n");
    }

    // The remainder keeps the shared Loan of the Dictionary: an exclusive use of the source before a later step is rejected.
    [Theory]
    [InlineData("values@uniq.clear()", false)]
    [InlineData("_ = values.length", true)]
    public void TheRemainderKeepsTheSourceLoan(string use, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("""
            var map: Dictionary<i32, i32> = [:]
            _ = map.tryInsert(1, 10)
            Kimi.StorageProbe.touch(map@uniq)
            """);
        c.Library.Kotonoha.CreateCodeContext().Parse(c.Library.Kotonoha.RootKoto, "public group StorageProbe\n    public func touch(values: uniq/Dictionary<i32, i32>)\n        var state = Storage.borrowStorage(values)\n        " + use + "\n        _ = Storage.splitFirst(state@uniq)\n");
        var bound = c.Bind().IsComplete;
        Assert.Equal(valid, bound && c.Binding.CheckStartup(OutputKind.Application).IsComplete && c.Ownership.Analyze().IsVerified);
    }

    // String keys and padded small keys place the key and value at their own layout offsets inside each slot.
    [Fact]
    public void KeysAndValuesAreLentFromTheirSlotOffsets()
    {
        var c = CompilationTestHelper.ParseSuccess("""
            var names: Dictionary<string, i8> = [:]
            _ = names.tryInsert("b", 2)
            _ = names.tryInsert("a", 1)
            _ = names.tryInsert("c", 3)
            _ = names.remove("a")
            require Kimi.StorageProbe.names(names@ref) else => $abort("names")
            var wide: Dictionary<i8, i64> = [:]
            _ = wide.tryInsert(7, 7000000000)
            _ = wide.tryInsert(9, 9000000000)
            require Kimi.StorageProbe.wide(wide@ref) == 16000000000 else => $abort("wide")
            Console.writeLine("offsets")
            """);
        const string probe = """
            public group StorageProbe
                public func names(values: ref/Dictionary<string, i8>) -> bool
                    var state = Storage.borrowStorage(values)
                    match Storage.splitFirst(state@uniq)
                        .Some(let first)
                            let key = first.0
                            require key == "b" and first.1 == 2 else => return false
                        .None => return false
                    match Storage.splitFirst(state@uniq)
                        .Some(let second)
                            let key = second.0
                            require key == "c" and second.1 == 3 else => return false
                        .None => return false
                    match Storage.splitFirst(state@uniq)
                        .Some(_) => return false
                        .None => return true
                public func wide(values: ref/Dictionary<i8, i64>) -> i64
                    var state = Storage.borrowStorage(values)
                    var total: i64 = 0
                    loop
                        match Storage.splitFirst(state@uniq)
                            .Some(let entry)
                                let key: i8 = entry.0
                                let value: i64 = entry.1
                                require value == 1000000000 * (key@i64) else => $abort("wide pair")
                                total += value
                            .None => exit
                    return total
            """;
        c.Library.Kotonoha.CreateCodeContext().Parse(c.Library.Kotonoha.RootKoto, probe);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.WriteFixture("DictionaryRemainderOffsets", CompilationTestHelper.WriteIr(c), "offsets\n");
    }
}
