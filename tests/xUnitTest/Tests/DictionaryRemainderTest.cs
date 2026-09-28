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

    // SPEC 22.1.2.3: Dictionary.iterate returns the standard Iterator; an earlier item stays valid across later steps.
    [Fact]
    public void ExplicitIterationRetainsEarlierItems()
    {
        var c = MinimalEmissionTest.Analyze("""
            var map: Dictionary<i32, i32> = [:]
            _ = map.tryInsert(2, 20)
            _ = map.tryInsert(1, 10)
            _ = map.tryInsert(3, 30)
            _ = map.remove(1)
            var it = map.iterate()
            var total: i32 = 0
            var position: i32 = 1
            loop
                match it.next()
                    .Some(let entry)
                        let key: i32 = entry.0
                        let value: i32 = entry.1
                        require value == key * 10 else => $abort("pair")
                        total += key * position
                        position += 1
                    .None => exit
            require total == 8 else => $abort("order")
            var again = map.iterate()
            match again.next()
                .Some(let first)
                    match again.next()
                        .Some(let second)
                            let k1: i32 = first.0
                            let k2: i32 = second.0
                            require k1 == 2 and k2 == 3 else => $abort("retained")
                        .None => $abort("second")
                .None => $abort("first")
            Console.writeLine("iterator")
            """);
        ScalarEmissionTest.WriteFixture("DictionaryRemainderIterator", CompilationTestHelper.WriteIr(c), "iterator\n");
    }

    // SPEC 14.6.2: exclusive enumeration lends each value exclusively once while keys stay shared; an earlier value
    // stays writable across later steps (region splitting), and the writes reach the Dictionary.
    [Fact]
    public void ExclusiveIterationSplitsEachValueOnce()
    {
        var c = MinimalEmissionTest.Analyze("""
            var map: Dictionary<i32, i32> = [:]
            _ = map.tryInsert(2, 20)
            _ = map.tryInsert(1, 10)
            _ = map.tryInsert(3, 30)
            _ = map.remove(1)
            var it = map.iterateUniq()
            match it.next()
                .Some((let k1, let v1))
                    match it.next()
                        .Some((let k2, let v2))
                            v1@follow += 1
                            v2@follow += 2
                            require k1 == 2 and k2 == 3 else => $abort("keys")
                        .None => $abort("second")
                .None => $abort("first")
            match it.next()
                .Some(_) => $abort("exhausted")
                .None => ()
            var total: i32 = 0
            var shared = map.iterate()
            loop
                match shared.next()
                    .Some(let entry)
                        let value: i32 = entry.1
                        total += value
                    .None => exit
            require total == 53 else => $abort("written")
            Console.writeLine("exclusive")
            """);
        ScalarEmissionTest.WriteFixture("DictionaryRemainderExclusive", CompilationTestHelper.WriteIr(c), "exclusive\n");
    }

    // SPEC 22.1.2.3, 4.7.6: owning enumeration transfers each returned entry once; the unreturned entries are destroyed with
    // the iterator in reverse insertion order, each value before its key, and the buffer is released.
    [Fact]
    public void OwningIterationTransfersEntriesAndDestroysTheRest()
    {
        const string Source = "struct Tracked\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"Dropped \\(self.id)\")\n" +
            "public func main()\n    var map: Dictionary<i32, Tracked> = [:]\n    _ = map.tryInsert(1, Tracked.init(10))\n    _ = map.tryInsert(2, Tracked.init(20))\n" +
            "    _ = map.tryInsert(3, Tracked.init(30))\n    _ = map.tryInsert(4, Tracked.init(40))\n    _ = map.remove(2)\n    Console.writeLine(\"Removed.\")\n" +
            "    var it = (map@move).intoIterator()\n" +
            "    match it.next()\n        .Some((let key, let value)) => require key == 1 and value.id == 10 else => $abort(\"first\")\n        .None => $abort(\"empty\")\n" +
            "    Console.writeLine(\"Stop.\")";
        ScalarEmissionTest.EmitFixture("DictionaryRemainderOwned", Source, "Dropped 20\nRemoved.\nDropped 10\nStop.\nDropped 40\nDropped 30\n");
    }

    // A component bound by a nested pattern (`.Some((let key, let value))`) descends from the iterator that returned the
    // item, so reading it in place while the exclusive iterator stays live for the next step is allowed; using the source
    // Dictionary itself during that time is still rejected.
    [Theory]
    [InlineData("Console.writeLine(value)", true)]
    [InlineData("Console.writeLine(value)\n                _ = entries.length", false)]
    [InlineData("_ = entries.length", false)]
    public void NestedItemComponentsDescendFromTheIterator(string use, bool valid)
    {
        var source = "func inspect(entries: uniq/Dictionary<i32, string>)\n    var it = entries.iterateUniq()\n    loop\n        match it.next()\n            .Some((let key, let value))\n                " + use +
            "\n            .None => exit\nvar entries: Dictionary<i32, string> = [:]\n_ = entries.tryInsert(1, \"one\")\n_ = entries.tryInsert(2, \"two\")\ninspect(entries@uniq)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        if (valid)
        {
            ScalarEmissionTest.WriteFixture("DictionaryRemainderNested", CompilationTestHelper.WriteIr(c), "one\ntwo\n");
        }
    }

    // SPEC 14.6.2: `for` selects the entry by its Subject mode. Exclusive decomposition writes values through the split
    // items; an owning loop that exits early leaves the unreturned entries to its iterator, which destroys them in reverse
    // insertion order after the returned entry's binding.
    [Fact]
    public void ForSelectsTheDictionaryEntryByMode()
    {
        const string Source = "struct Tracked\n    public let id: i32\n    public init(id: i32) => self.id = id\n    deinit => Console.writeLine(\"Dropped \\(self.id)\")\n" +
            "public func main()\n    var totals: Dictionary<string, i32> = [:]\n    _ = totals.tryInsert(\"a\", 1)\n    _ = totals.tryInsert(\"b\", 2)\n" +
            "    for (key, value) in totals@uniq\n        value@follow += 10\n    var sum: i32 = 0\n    for (key, value) in totals\n        let n: i32 = value\n        sum += n\n" +
            "    require sum == 23 else => $abort(\"sum\")\n" +
            "    var map: Dictionary<i32, Tracked> = [:]\n    _ = map.tryInsert(1, Tracked.init(10))\n    _ = map.tryInsert(2, Tracked.init(20))\n    _ = map.tryInsert(3, Tracked.init(30))\n" +
            "    for (key, value) in map@move\n        require key == 1 and value.id == 10 else => $abort(\"first\")\n        exit\n    Console.writeLine(\"After.\")";
        ScalarEmissionTest.EmitFixture("DictionaryRemainderFor", Source, "Dropped 10\nDropped 30\nDropped 20\nAfter.\n");
    }

    // The owning remainder releases the transferred buffer exactly once, including after a partial enumeration.
    [Fact]
    public void OwningIterationReleasesTheBufferOnce()
        => NativeAllocationAudit.WriteFixture(
            "DictionaryRemainderOwnedRelease",
            "var map: Dictionary<i32, i32> = [:]\nmap.reserve(3)\n_ = map.tryInsert(1, 10)\n_ = map.tryInsert(2, 20)\nvar it = (map@move).intoIterator()\nmatch it.next()\n    .Some((let k, let v)) => require k == 1 and v == 10 else => $abort(\"first\")\n    .None => $abort(\"empty\")",
            1,
            1,
            96);

    [Theory]
    [InlineData("_ = map.length", false)]
    [InlineData("_ = map.tryInsert(5, 50)", false)]
    [InlineData("_ = 1", true)]
    public void ALiveExclusiveIteratorKeepsTheParentLoan(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("var map: Dictionary<i32, i32> = [:]\n_ = map.tryInsert(1, 10)\nvar it = map.iterateUniq()\n" + use + "\n_ = it.next()");
        Assert.Equal(valid, c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Theory]
    [InlineData("_ = map.tryInsert(5, 50)", false)]
    [InlineData("map.clear()", false)]
    [InlineData("_ = map.length", true)]
    public void ALiveIteratorKeepsTheDictionaryLoan(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("var map: Dictionary<i32, i32> = [:]\n_ = map.tryInsert(1, 10)\nvar it = map.iterate()\n" + use + "\n_ = it.next()");
        Assert.Equal(valid, c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
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
        c.Library.Kotonoha.CreateCodeContext().Parse(c.Library.Kotonoha.RootKoto, "public group StorageProbe\n    public func touch(values: uniq/Dictionary<i32, i32>)\n        var state = Storage.borrowStorage(values@follow@ref)\n        " + use + "\n        _ = Storage.splitFirst(state@uniq)\n");
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
