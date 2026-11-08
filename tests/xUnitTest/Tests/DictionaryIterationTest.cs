// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryIterationTest
{
    [Trait("Purpose", "Allocation")]
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
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
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
                drop => Console.writeLine("key")
            struct Item
                public let name: string
                public init(name: string) => self.name = name@move
                drop => Console.writeLine(self.name)
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

    [Trait("Purpose", "Allocation")]
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

    [Trait("Purpose", "Allocation")]
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

    [Trait("Purpose", "Allocation")]
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

    [Trait("Purpose", "Allocation")]
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

    [Trait("Purpose", "Allocation")]
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
        var source = owning ? Source.Replace("in entries\n", "in entries@move\n", StringComparison.Ordinal) : Source.Replace("= stored\n", "= stored@follow\n", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 8.15.3 (R8-K1a, as R8-A1d for Arrays): a Dictionary view formed behind a borrow reads no entry, so it lays out nothing of K or V:
    // `iterate` and `iterateUniq` over a key or value whose layout fails (a C layout with a zero-sized Field, a size over 2^63 - 1, one over the
    // generation limit) are valid, by a call, as a result and through a generic Iterable; two of them build. An entry's first read needs them.
    [Theory]
    [InlineData("Shared", "#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: ref/Dictionary<Bad, i32>) -> isize\n    var it = d.iterate()\n    return 0\npublic func main()\n    let g = f\n    return\n")]
    [InlineData("Exclusive", "#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: uniq/Dictionary<Bad, i32>) -> isize\n    var it = d.iterateUniq()\n    return 0\npublic func main()\n    let g = f\n    return\n")]
    [InlineData("Result", "#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: ref/Dictionary<Bad, i32>) -> DictionaryIterator<Bad, i32> during d\n    return d.iterate()\npublic func main()\n    let g = f\n    return\n")]
    [InlineData("Value", "#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: ref/Dictionary<i32, Bad>) -> isize\n    var it = d.iterate()\n    return 0\npublic func main()\n    let g = f\n    return\n")]
    [InlineData("Generic", "#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc g<T>(x: ref/T) -> isize\n    T is Iterable\n    var it = x.iterate()\n    return 0\nfunc f(d: ref/Dictionary<Bad, i32>) -> isize\n    return g(d)\npublic func main()\n    let g = f\n    return\n")]
    [InlineData("Oversized", "func f(d: ref/Dictionary<i32, [4611686018427387904 of i64]>) -> isize\n    var it = d.iterate()\n    return 0\npublic func main()\n    let g = f\n    return\n")]
    [InlineData("OverTheGenerationLimit", "func f(d: ref/Dictionary<i32, [1152921504606846974 of i64]>) -> isize\n    var it = d.iterate()\n    return 0\npublic func main()\n    let g = f\n    return\n")]
    public void ADictionaryViewNeedsNoEntryLayout(string name, string source)
    {
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        if (name is "Shared" or "Exclusive")
        {
            ScalarEmissionTest.EmitFixture("DictionaryViewNeedsNoLayout" + name, source, string.Empty);
        }
    }

    // Their invalid counterparts: `next`, `for` and an index read an entry, one record at the use, per instance (DECISIONS R8-M2's grouping,
    // R8-K1a). The slot stride reads each Type alone (DictionaryStorage.slotStride), so a value over the generation limit, also of the largest
    // size its alignment allows, keeps its Resource record and is never OversizedLayout_Kd (IMPL 21.3.2; design review 1, B1).
    [Theory]
    [InlineData("#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: ref/Dictionary<Bad, i32>) -> isize\n    var it = d.iterate()\n    _ = it.next()\n    return 0\npublic func main()\n    let g = f\n    return\n", "InvalidCLayout_Kd", "it.next()")]
    [InlineData("#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: uniq/Dictionary<Bad, i32>) -> isize\n    var it = d.iterateUniq()\n    _ = it.next()\n    return 0\npublic func main()\n    let g = f\n    return\n", "InvalidCLayout_Kd", "it.next()")]
    [InlineData("func f(d: ref/Dictionary<i32, [4611686018427387904 of i64]>) -> isize\n    var it = d.iterate()\n    _ = it.next()\n    return 0\npublic func main()\n    let g = f\n    return\n", "OversizedLayout_Kd", "it.next()")]
    [InlineData("#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: ref/Dictionary<Bad, i32>) -> i32\n    var sum: i32 = 0\n    for (k, v) in d => sum += v\n    return sum\npublic func main()\n    let g = f\n    return\n", "InvalidCLayout_Kd", null)]
    [InlineData("#Layout(\"C\")\nstruct Bad\n    Self is Equatable\n    public var nothing: ()\n    public var value: i32\n    public init(value: i32)\n        self.nothing = ()\n        self.value = value\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(d: ref/Dictionary<Bad, i32>, k: ref/Bad) -> i32\n    return d[k]\npublic func main()\n    let g = f\n    return\n", "InvalidCLayout_Kd", null)]
    [InlineData("func f(d: ref/Dictionary<i32, [1152921504606846974 of i64]>) -> isize\n    var it = d.iterate()\n    _ = it.next()\n    return 0\npublic func main()\n    let g = f\n    return\n", "GenerationResourceLimit_Kd", "it.next()")]
    [InlineData("func f(d: ref/Dictionary<i32, [1152921504606846975 of i64]>) -> isize\n    var it = d.iterate()\n    _ = it.next()\n    return 0\npublic func main()\n    let g = f\n    return\n", "GenerationResourceLimit_Kd", "it.next()")]
    public void ADictionaryEntrysFirstReadNeedsItsLayout(string source, string code, string? at)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.True(at is null || source.Substring(record.Span!.Value.Start, record.Span.Value.Length) == at);
    }

    // IMPL 21.3.2 (R8-K1a, design review 2 item 2): `alignmentOf<T>` places `T` two levels deeper, `(u8, [0 of T])`; Binding's storage-nesting
    // bound still cuts first, so a value nest keeps dev's records at the bound: `clear` accepts the last depth and gives one Unsupported_Kd one
    // deeper, for a Tuple, an Option and a fixed-array nest. A change of either bound that moves these rows exposes the margin.
    [Theory]
    [InlineData("(u8, {0})", 61)]
    [InlineData("Option<{0}>", 30)]
    [InlineData("[1 of {0}]", 61)]
    public void AValueNestKeepsTheStorageBoundsRecords(string layer, int last)
    {
        Assert.Empty(DiagnosticCorpus.Check(Program(last)).Diagnostics);
        var record = Assert.Single(DiagnosticCorpus.Check(Program(last + 1)).Diagnostics);
        Assert.Equal("Unsupported_Kd", record.Code);

        string Program(int depth)
        {
            var type = "i32";
            for (var i = 0; i < depth; i++)
            {
                type = string.Format(System.Globalization.CultureInfo.InvariantCulture, layer, type);
            }

            return $"func f(d: uniq/Dictionary<i32, {type}>) -> isize\n    d.clear()\n    return 0\npublic func main()\n    let g = f\n    return\n";
        }
    }
}
