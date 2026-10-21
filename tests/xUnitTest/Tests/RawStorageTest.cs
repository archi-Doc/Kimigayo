// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// SPEC 5.6: Kimi.Raw. allocate is safe and Aborts on a negative count or an unrepresentable size; a zero-byte request returns a
// nonnull substitute that release ignores. release and initialize are unsafe; initialize moves its value in and destroys
// nothing, and placed values leave the storage only through raw Place Take or relocation (SPEC 5.2).
public class RawStorageTest
{
    private const string Resource = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n";

    // SPEC 5.6's Buffer and BufferView: a `during self` Field filled by a claim in the constructor and read through each borrow.
    private const string Buffer = "public struct Buffer<T>\n    internal let data: raw/T during self\n    internal var length: isize\n    let capacity: isize\n" +
        "    public init(capacity: isize)\n        unsafe => self.data = Raw.allocate<T>(capacity)@(raw/T during self)\n        self.length = 0\n        self.capacity = capacity\n" +
        "    public func push(self: uniq/Self, value: T)\n        require self.length < self.capacity else => $abort(\"Buffer is full\")\n" +
        "        unsafe => Raw.initialize(self.data + self.length, value@move)\n        self.length += 1\n" +
        "    public func get(self: ref/Self, index: isize) -> ref/T\n        require 0 <= index and index < self.length else => $abort(\"Index out of range\")\n" +
        "        unsafe => return self.data[index]@ref\n" +
        "    public func items(self: ref/Self) -> Slice<T>\n        unsafe => return Raw.slice(self.data, self.length)\n" +
        "    public func view(self: ref/Self) -> BufferView<T>\n        return BufferView<T>.init(self)\n" +
        "    drop\n        let data = self.data\n        while self.length > 0\n            self.length -= 1\n            unsafe => _ = data[self.length]@move\n" +
        "        unsafe => Raw.release(data)\n" +
        "public struct BufferView<T> {source}\n    let loan: Loan<ref/Buffer<T> during source>\n    let data: raw/T during source\n    let length: isize\n" +
        "    internal init(buffer: ref/Buffer<T> during source)\n        self.data = buffer.data\n        self.length = buffer.length\n        self.loan = Loan.init(buffer)\n" +
        "    public func get(self: ref/Self, index: isize) -> ref/T during self.source\n        require 0 <= index and index < self.length else => $abort(\"Index out of range\")\n" +
        "        unsafe => return self.data[index]@ref\n";

    // A struct holding `during self` layers alone and in a Tuple, and an enum with a `during self` payload (SPEC 5.7, DECISIONS R4-M105).
    private const string Block = "struct Block\n    internal var data: raw/u8 during self\n    internal var pair: (raw/u8 during self, isize)\n" +
        "    internal var arr: [2 of raw/u8 during self]\n" +
        "    public init()\n        unsafe\n            self.data = Raw.allocate<u8>(1)@(raw/u8 during self)\n            self.pair = (Raw.allocate<u8>(1)@(raw/u8 during self), 1)\n" +
        "        self.arr = [null, null]\n    drop => unsafe => Raw.release(self.data)\n" +
        "enum Cell\n    Some(raw/u8 during self)\n    Two((raw/u8 during self, i32))\n    None\n" +
        "struct Outer\n    internal var inner: Block\n    public init(fresh: raw/u8)\n        self.inner = Block.init()\n        {0}\n" +
        "func reset(fresh: raw/u8, shared: raw/u8 during static)\n    var b = Block.init()\n    {1}\n";

    [Fact]
    public void StorageIsAllocatedInitializedTakenAndReleased()
    {
        var source = Resource + """
            struct Pair
                public var text: string
                public var number: i64
                public init(text: string, number: i64)
                    self.text = text@move
                    self.number = number
            public func main()
                let storage = Raw.allocate<Resource>(2)
                unsafe
                    Raw.initialize(storage, Resource.init(1))
                    Raw.initialize(storage + 1, Resource.init(2))
                    require (*storage).value == 1 and storage[1].value == 2 else => $abort("values")
                    let first = (*storage)@move
                    _ = storage[1]@move
                    Raw.release(storage)
                    Console.writeLine("released")
                let pairs = Raw.allocate<Pair>(1)
                let texts = Raw.allocate<string>(1)
                unsafe
                    Raw.initialize(pairs, Pair.init("pair", 7))
                    Raw.initialize(texts, "text")
                    let pair = (*pairs)@move
                    let text = (*texts)@move
                    require pair.text == "pair" and pair.number == 7 and text == "text" else => $abort("aggregate")
                    Raw.release(pairs)
                    Raw.release(texts)
                let units = Raw.allocate<()>(5)
                let none = Raw.allocate<i32>(0)
                require units != null and none != null else => $abort("substitute")
                unsafe
                    Raw.release(units)
                    Raw.release(none)
                    Raw.release(null@raw/i32)
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("RawStorageLifecycle", source, "drop\nreleased\ndrop\ndone\n");
    }

    // SPEC 5.6: slice forms a shared Slice over initialized storage; its result-only Origin is fixed by the caller's expected Type,
    // here the receiver's, and a zero length may use any storage, including null.
    [Fact]
    public void SlicesViewInitializedStorage()
    {
        var source = """
            struct Holder
                let data: raw/i32
                let length: isize
                public init(length: isize)
                    self.data = Raw.allocate<i32>(length)
                    self.length = length
                    var i: isize = 0
                    while i < length
                        unsafe => Raw.initialize(self.data + i, (i * 10)@i32)
                        i += 1
                public func items(self: ref/Self) -> Slice<i32>
                    unsafe => return Raw.slice(self.data, self.length)
                drop
                    unsafe => Raw.release(self.data)
            func emptySlice() -> Slice<i32>
                unsafe => return Raw.slice(null@raw/i32, 0)
            public func main()
                let holder = Holder.init(4)
                var total: i32 = 0
                for item in holder.items()
                    total += item
                let view = holder.items()
                require total == 60 and view.length == 4 and view[3] == 30 else => $abort("view")
                require emptySlice().length == 0 else => $abort("empty")
                Console.writeLine("sliced")
            """;
        ScalarEmissionTest.EmitFixture("RawStorageSlice", source, "sliced\n");
    }

    // SPEC 3.4, 5.2: a value placed in raw storage occupies its own Place, so zero-sized values at one address are placed,
    // taken and destroyed one by one, also by an owning iterator that stops early, and a component of a placed value is
    // taken alone before release.
    [Fact]
    public void ZeroSizedValuesOccupyTheirOwnPlaces()
    {
        var source = """
            struct Token
                drop => Console.writeLine("drop")
            struct Buffer<T>
                let data: raw/T
                var length: isize
                let capacity: isize
                public init(capacity: isize)
                    self.data = Raw.allocate<T>(capacity)
                    self.length = 0
                    self.capacity = capacity
                public func push(self: uniq/Self, value: T)
                    require self.length < self.capacity else => $abort("full")
                    unsafe => Raw.initialize(self.data + self.length, value@move)
                    self.length += 1
                drop
                    let data = self.data
                    while self.length > 0
                        self.length -= 1
                        unsafe => _ = data[self.length]@move
                    unsafe => Raw.release(data)
            func buffer()
                var tokens = Buffer<Token>.init(capacity: 2)
                tokens.push(Token.init())
                tokens.push(Token.init())
                Console.writeLine("buffer")
            func owning()
                let values: Array<Token> = [Token.init(), Token.init(), Token.init()]
                for item in values@move
                    Console.writeLine("item")
                    exit
            func component()
                let p = Raw.allocate<(i64, Token)>(1)
                unsafe
                    Raw.initialize(p, (7, Token.init()))
                    _ = (*p).1@move
                    require (*p).0 == 7 else => $abort("component")
                    Raw.release(p)
                Console.writeLine("component")
            public func main()
                buffer()
                owning()
                component()
            """;
        ScalarEmissionTest.EmitFixture(
            "RawStorageZeroSizedPlaces", source, "buffer\ndrop\ndrop\nitem\ndrop\ndrop\ndrop\ndrop\ncomponent\n");
    }

    [Theory]
    [InlineData("Negative", "-1", "abort KIMI_E_ARG_RANGE: Argument out of range")]
    [InlineData("Oversized", "4611686018427387904", "abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit")]
    public void InvalidCountsAbort(string name, string count, string message)
        => ScalarEmissionTest.EmitFixture("RawStorageCount" + name, "let count: isize = " + count + "\nlet storage = Raw.allocate<i64>(count)\nConsole.writeLine(\"after\")", string.Empty, 1, "Hello.kimi:2:15: " + message + "\n");

    [Theory]
    [InlineData("let storage = Raw.allocate<i32>(1)", true)]
    [InlineData("Raw.release(null@raw/i32)", false)]
    [InlineData("Raw.initialize(null@raw/i32, 1)", false)]
    [InlineData("let view: Slice<i32> = Raw.slice(null@raw/i32, 0)", false)]
    public void OnlyReleaseAndInitializeNeedAnUnsafeContext(string statement, bool valid)
    {
        var diagnostics = DiagnosticCorpus.Check("public func main()\n    " + statement + "\n").Diagnostics;
        if (valid)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.Equal(nameof(DiagnosticCode.UnsafeBlockRequired_Kd), Assert.Single(diagnostics).Code);
        }
    }

    // SPEC 5.6, 8.11.2: allocate is an allocation and release and initialize are raw accesses; none is an environment effect.
    [Fact]
    public void RawOperationsAreNoEnvironmentEffects()
    {
        const string Source = "contract Sink\n    func put(self: uniq/Self, value: i32)\n        effect confined\n" +
            "struct RawSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32)\n        let storage = Raw.allocate<i32>(1)\n" +
            "        unsafe\n            Raw.initialize(storage, value)\n            Raw.release(storage)\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    // SPEC 5.6, 5.7: Buffer's storage is its own (`during self`): the constructor's claim, drop's reads and Take, and the reads through a
    // shared or exclusive receiver; BufferView reads `buffer.data` through `source`, so its Field needs no claim.
    [Fact]
    public void ABufferOwnsItsStorageThroughSelf()
        => Assert.Empty(RawPointerSyntaxTest.Records(Buffer + "func use()\n    var b = Buffer<i32>.init(capacity: 4)\n    b.push(1)\n    let v = b.view()\n    require v.get(0) == 1 else => $abort(\"view\")"));

    // SPEC 5.7: a constructor states the stored Origin; an unstated pointer needs the claim.
    [Fact]
    public void AConstructorFillsSelfStorageWithAClaim()
        => Assert.Equal(
            ["UnsatisfiedOriginRelation_Kd: Raw.allocate<i32>(1)"],
            RawPointerSyntaxTest.Records("struct B\n    internal let data: raw/i32 during self\n    public init() => self.data = Raw.allocate<i32>(1)"));

    // SPEC 5.7 (DECISIONS R4-M105, OQ-R1 (a)): through no dereference, except through `self` in the constructors and drop of the Type
    // whose inline storage holds the layer, a `during self` layer's Place admits reads, shared borrows and writes of null only; the
    // write of an enclosing Tuple Place is one of the layer's Place. Every other write, an exclusive borrow and a non-null Case
    // payload is an Origin relation error; an exclusive borrow of an enclosing Place and a Move of the layer's Place are located
    // Unsupported_Kd (PLAN G212).
    [Theory]
    [InlineData("()", "b.data = fresh", "UnsatisfiedOriginRelation_Kd: b.data = fresh")]
    [InlineData("()", "b.data = shared", "UnsatisfiedOriginRelation_Kd: b.data = shared")]
    [InlineData("()", "unsafe => b.data += 1", "UnsatisfiedOriginRelation_Kd: b.data += 1")]
    [InlineData("()", "let w = b.data@uniq", "UnsatisfiedOriginRelation_Kd: b.data@uniq")]
    [InlineData("()", "b.pair = (fresh, 1)", "UnsatisfiedOriginRelation_Kd: b.pair = (fresh, 1)")]
    [InlineData("()", "b.pair = (b.data, 1)", "UnsatisfiedOriginRelation_Kd: b.pair = (b.data, 1)")]
    [InlineData("()", "b.arr = [null, fresh]", "UnsatisfiedOriginRelation_Kd: b.arr = [null, fresh]")]
    [InlineData("()", "let q = b.pair\n    b.pair = q", "UnsatisfiedOriginRelation_Kd: b.pair = q")]
    [InlineData("()", "b.pair.0 = fresh", "UnsatisfiedOriginRelation_Kd: b.pair.0 = fresh")]
    [InlineData("()", "b.arr[1] = fresh", "UnsatisfiedOriginRelation_Kd: b.arr[1] = fresh")]
    [InlineData("()", "let w = b.pair.0@uniq", "UnsatisfiedOriginRelation_Kd: b.pair.0@uniq")]
    [InlineData("()", "let c = Cell.Some(fresh)", "UnsatisfiedOriginRelation_Kd: fresh")]
    [InlineData("()", "let c = Cell.Two((fresh, 1))", "UnsatisfiedOriginRelation_Kd: (fresh, 1)")]
    [InlineData("self.inner.data = fresh", "()", "UnsatisfiedOriginRelation_Kd: self.inner.data = fresh")]
    [InlineData("()", "let w = b.pair@uniq", "Unsupported_Kd: b.pair@uniq")]
    [InlineData("()", "let w = b.data@move", "Unsupported_Kd: b.data@move")]
    public void ASelfLayerReachedThroughNoDereferenceIsWrittenWithNullOnly(string construction, string use, string record)
        => Assert.Equal([record], RawPointerSyntaxTest.Records(Block.Replace("{0}", construction, StringComparison.Ordinal).Replace("{1}", use, StringComparison.Ordinal)));

    [Theory]
    [InlineData("()", "b.data = null")]
    [InlineData("()", "let w = b.data@ref")]
    [InlineData("()", "let p: raw/u8 = b.data")]
    [InlineData("()", "let c = Cell.Some(null)")]
    [InlineData("()", "b.pair = (null, 2)")]
    [InlineData("()", "b.pair.0 = null")]
    [InlineData("()", "b.arr[1] = null")]
    [InlineData("()", "b.pair.1 = 3")]
    [InlineData("()", "b.arr = [null, null]")]
    [InlineData("()", "b.arr = [2 of null]")]
    [InlineData("()", "let c = Cell.Two((null, 1))")]
    [InlineData("()", "let r = b@uniq\n    unsafe => r.data = fresh@(raw/u8 during r)")]
    [InlineData("()", "let c = Cell.Some(null)\n    let p: raw/u8 = match c@ref\n        Cell.Some(let q) => q@follow\n        Cell.Two((let q, _)) => q@follow\n        Cell.None => null")]
    public void ASelfLayerAdmitsReadsSharedBorrowsAndNull(string construction, string use)
        => Assert.Empty(RawPointerSyntaxTest.Records(Block.Replace("{0}", construction, StringComparison.Ordinal).Replace("{1}", use, StringComparison.Ordinal)));

    // SPEC 5.7, 15.6.1, 23.1 (rulings S4): the record's longer end is the Origin of the value that reaches the layer, an aggregate
    // literal's element at the layer's position included; a pointer without `during`, also the one an update or an exclusive borrow
    // admits (the Place's read Type, which states none), shows the Unstated end as omitted.
    [Theory]
    [InlineData("b.data = fresh", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    [InlineData("b.pair = (fresh, 1)", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    [InlineData("b.pair = (shared, 1)", "requires static outlives self, which is false")]
    [InlineData("let c = Cell.Two((shared, 1))", "requires static outlives self, which is false")]
    [InlineData("let w = b.data@uniq", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    public void ASelfLayerRecordShowsTheValueReachingTheLayer(string use, string label)
    {
        var source = Block.Replace("{0}", "()", StringComparison.Ordinal).Replace("{1}", use, StringComparison.Ordinal) + "public func main() => ()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), label), (record.Code, record.Label));
    }

    // SPEC 5.7 (DECISIONS R4-M105, OQ-R2 (a)): a Field read through a borrow states the borrow's outer Origin, a meet included, formed
    // with the borrow's mode: a shared one keeps the shared view, so authority stays shared; an exclusive one is the meet itself.
    [Theory]
    [InlineData("ref", OriginKind.Shared)]
    [InlineData("uniq", OriginKind.Intersection)]
    public void AFieldReadThroughAMeetStatesTheMeet(string mode, OriginKind kind)
    {
        var source = Block.Replace("{0}", "()", StringComparison.Ordinal).Replace("{1}", "()", StringComparison.Ordinal) +
            $"func choose(c: bool, a: {mode}/Block, b: {mode}/Block) -> {mode}/Block => if c => a else => b\n" +
            $"func read(c: bool, x: {mode}/Block, y: {mode}/Block) -> raw/u8 => choose(c, x, y).data\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single(static x => x.ToString() == "choose(c, x, y).data").TypeOf()!.RawOrigin!;
        Assert.Equal(kind, read.Kind);
        Assert.Equal(OriginKind.Intersection, Binding.RegionOf(read)!.Kind);
    }

    // SPEC 5.2.2, 5.6, 5.7 (D14): a borrow of a raw Place, and Raw.slice's result, lie within the Origin the pointer states, so neither
    // reaches past the borrow the pointer was read through.
    [Theory]
    [InlineData("func f(b: ref/Buffer<i32>) -> ref/i32 during static\n    unsafe => return b.data[0]@ref", "UnprovenOriginRelation_Kd: b.data[0]@ref")]
    [InlineData("func f(b: ref/Buffer<i32>) -> Slice<i32> during static\n    unsafe => return Raw.slice(b.data, b.length)", "UnprovenOriginRelation_Kd: b.data")]
    [InlineData("func f(b: ref/Buffer<i32>) -> ref/i32 during b\n    unsafe => return b.data[0]@ref", null)]
    [InlineData("func f(b: ref/Buffer<i32>) -> Slice<i32> during b\n    unsafe => return Raw.slice(b.data, b.length)", null)]
    public void ARawPlaceBorrowLiesWithinThePointersOrigin(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], RawPointerSyntaxTest.Records(Buffer + declarations));
}
