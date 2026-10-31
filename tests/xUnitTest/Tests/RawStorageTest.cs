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
    // SPEC 5.6's Buffer and BufferView: a `during self` Field filled by a claim in the constructor and read through each borrow.
    internal const string Buffer = "public struct Buffer<T>\n    internal let data: raw/T during self\n    internal var length: isize\n    let capacity: isize\n" +
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
    internal const string Block = "struct Block\n    internal var data: raw/u8 during self\n    internal var pair: (raw/u8 during self, isize)\n" +
        "    internal var arr: [2 of raw/u8 during self]\n" +
        "    public init()\n        unsafe\n            self.data = Raw.allocate<u8>(1)@(raw/u8 during self)\n            self.pair = (Raw.allocate<u8>(1)@(raw/u8 during self), 1)\n" +
        "        self.arr = [null, null]\n    drop => unsafe => Raw.release(self.data)\n" +
        "enum Cell\n    Some(raw/u8 during self)\n    Two((raw/u8 during self, i32))\n    None\n" +
        "struct Outer\n    internal var inner: Block\n    public init(fresh: raw/u8)\n        self.inner = Block.init()\n        {0}\n" +
        "func reset(fresh: raw/u8, shared: raw/u8 during static)\n    var b = Block.init()\n    {1}\n";

    // A view whose raw Field states the slot a shared Loan keeps (bu23 S1), and a remainder whose raw Field states the slot an exclusive
    // Loan keeps (S2, S3).
    private const string View = "struct View<T> {source}\n    internal let loan: Loan<ref/T during source>\n    internal let at: raw/T during source\n" +
        "    public init(target: ref/T during source)\n        self.at = target@follow@raw\n        self.loan = Loan.init(target)\n";

    private const string Remainder = "struct R<E> {source}\n    internal let at: raw/E during source\n    internal let loan: Loan<uniq/E during source>\n";

    // A `during self` raw Field beside a scalar, and SPEC 5.7's Lens with a writable raw Field stating its slot (the final review's B1).
    private const string SelfField = "struct B\n    internal var data: raw/u8 during self\n    internal var n: i32\n    public init()\n        self.data = null\n        self.n = 0\n";
    private const string SlotField = "struct Lens {source}\n    internal let loan: Loan<ref/i32 during source>\n    internal var at: raw/i32 during source\n" +
        "    public init(t: ref/i32 during source)\n        self.at = t@follow@raw\n        self.loan = Loan.init(t)\n";

    // The remainder's initializer from one exclusive element, and a pair taken from it by two raw Place borrows, written through.
    private const string PairInit = "    public init(target: uniq/E during source)\n        self.at = target@follow@raw\n        self.loan = Loan.init(target)\n";

    private const string PairUse = "    var state = source@move\n    unsafe\n        let lower = state.at[0]@uniq\n        let upper = state.at[1]@uniq\n" +
        "        return .Some((lower@move, upper@move))\npublic func main()\n    var xs: [2 of i32] = [1, 2]\n    match pair(R<i32>.init(xs[0]@uniq))\n" +
        "        .Some((let a, let b))\n            a@follow = 3\n            b@follow = 4\n        .None => ()\n" +
        "    require xs[0] == 3 and xs[1] == 4 else => $abort(\"pair\")\n    Console.writeLine(\"Pair\")";

    private const string Resource = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n";

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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
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

    // SPEC 5.7 (DECISIONS R4-M105, OQ-R1 (a); R4-M110): through no dereference, except through `self` in the constructors and drop of
    // the Type whose inline storage holds the layer, a `during self` layer's Place admits reads (a Move included), shared borrows and
    // writes of null only, and an enclosing Tuple or fixed-array Place is written and borrowed as the layer's Place is. Every other
    // write, an exclusive borrow, also a receiver's implicit one (SPEC 7.3), and a non-null Case payload is an Origin relation error, one
    // record each; a Move of an enclosing Place is a located Unsupported_Kd (PLAN G212: SPEC does not say what it gives).
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
    [InlineData("()", "let w = b.pair@uniq", "UnsatisfiedOriginRelation_Kd: b.pair@uniq")]
    [InlineData("()", "_ = Kimi.Intrinsics.exchange(b.pair@uniq, with: (fresh, 1))", "UnsatisfiedOriginRelation_Kd: b.pair@uniq")]
    [InlineData("()", "_ = Kimi.Intrinsics.exchange(b.pair@uniq, with: (null, 1))", "UnsatisfiedOriginRelation_Kd: b.pair@uniq")]
    [InlineData("()", "let w = b.arr@(uniq/[2 of raw/u8])", "UnsatisfiedOriginRelation_Kd: b.arr@(uniq/[2 of raw/u8])")]
    [InlineData("()", "var s = b.arr.sliceUniq()\n    s[0] = fresh", "UnsatisfiedOriginRelation_Kd: b.arr")]
    [InlineData("()", "let s = b.arr.iterateUniq()", "UnsatisfiedOriginRelation_Kd: b.arr")]
    [InlineData("()", "let w = b.data@move\n    b.data = fresh", "UnsatisfiedOriginRelation_Kd: b.data = fresh")]
    [InlineData("()", "let t = b.pair@move", "Unsupported_Kd: b.pair@move")]
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
    [InlineData("()", "let w = b.data@move\n    b.data = null")]
    [InlineData("()", "let t = b.pair.0@move\n    b.pair.0 = null")]
    [InlineData("()", "let t = b.arr[0]@move\n    b.arr[0] = null")]
    [InlineData("()", "let w = b.pair@ref")]
    [InlineData("()", "let s = b.arr.slice()")]
    [InlineData("()", "let r = b@uniq\n    let w = r.pair@uniq")]
    [InlineData("()", "let r = b@uniq\n    unsafe => r.pair = (fresh@(raw/u8 during r), 1)")]
    public void ASelfLayerAdmitsReadsSharedBorrowsAndNull(string construction, string use)
        => Assert.Empty(RawPointerSyntaxTest.Records(Block.Replace("{0}", construction, StringComparison.Ordinal).Replace("{1}", use, StringComparison.Ordinal)));

    // SPEC 5.7 (DECISIONS R4-M110): through `self` in the Type's own constructors and drop the allowlist does not apply, so an enclosing
    // Place is moved and exclusively borrowed there as any Field is.
    [Theory]
    [InlineData("    public init()\n        self.pair = (null, 1)\n        let p = self.pair@move\n        self.pair = (null, 2)")]
    [InlineData("    public init() => self.pair = (null, 1)\n    drop\n        let w = self.pair@uniq")]
    public void TheTypesOwnConstructorAndDropMoveAndBorrowAnEnclosingPlace(string members)
        => Assert.Empty(RawPointerSyntaxTest.Records("struct M\n    internal var pair: (raw/u8 during self, isize)\n" + members));

    // SPEC 5.7, 15.6.1, 23.1 (rulings S4): the record's longer end is the Origin of the value that reaches the layer, an aggregate
    // literal's element at the layer's position included; a pointer without `during`, also the one an update or an exclusive borrow
    // admits (the Place's read Type, which states none), shows the Unstated end as omitted.
    [Theory]
    [InlineData("b.data = fresh", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    [InlineData("b.pair = (fresh, 1)", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    [InlineData("b.pair = (shared, 1)", "requires static outlives self, which is false")]
    [InlineData("let c = Cell.Two((shared, 1))", "requires static outlives self, which is false")]
    [InlineData("let w = b.data@uniq", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    [InlineData("let w = b.pair@uniq", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    [InlineData("var s = b.arr.sliceUniq()", "requires the omitted Origin of a raw pointer without `during` outlives self, which is false")]
    public void ASelfLayerRecordShowsTheValueReachingTheLayer(string use, string label)
    {
        var source = Block.Replace("{0}", "()", StringComparison.Ordinal).Replace("{1}", use, StringComparison.Ordinal) + "public func main() => ()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), label), (record.Code, record.Label));
    }

    // SPEC 5.7 (DECISIONS R4-M105, OQ-R2 (a); BU-24): a Field read through a borrow states the borrow's outer Origin, a meet included,
    // whatever the borrow's mode.
    [Theory]
    [InlineData("ref")]
    [InlineData("uniq")]
    public void AFieldReadThroughAMeetStatesTheMeet(string mode)
    {
        var source = Block.Replace("{0}", "()", StringComparison.Ordinal).Replace("{1}", "()", StringComparison.Ordinal) +
            $"func choose(c: bool, a: {mode}/Block, b: {mode}/Block) -> {mode}/Block => if c => a else => b\n" +
            $"func read(c: bool, x: {mode}/Block, y: {mode}/Block) -> raw/u8 => choose(c, x, y).data\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single(static x => x.ToString() == "choose(c, x, y).data").TypeOf()!.RawOrigin!;
        Assert.Equal(OriginKind.Intersection, read.Kind);
    }

    // SPEC 5.6, 5.7 (DECISIONS BU-24, the owner's Q-O3 D5 exception): the default pipeline generates and runs the programs Q-O3's
    // measurement found failing (q-o3-measure: `during self` holders, Case payloads built with null and matched, SPEC 5.6's Buffer and
    // BufferView, Lenses, typed paths and pointers stating different Origins), the forms B1 made valid and G212's Moves of a layer's
    // Place restored by null (DECISIONS R4-M110); each runs natively. A generic instance's raw Place borrows of a remainder's
    // slot-stated Field, met with their anchors, generate as the closed twin does (the owner's second D5 exception, design-final §15 B).
    [Theory]
    [InlineData("RawOriginLegacyPairGeneric", Remainder + PairInit + "func pair<E>(source: R<E> during a) -> Option<(uniq/E during a, uniq/E during a)>\n" + PairUse, "Pair\n")]
    [InlineData("RawOriginLegacyPairClosed", Remainder + PairInit + "func pair(source: R<i32> during a) -> Option<(uniq/i32 during a, uniq/i32 during a)>\n" + PairUse, "Pair\n")]
    [InlineData("RawOriginLegacyHolderNull", "struct Buf\n    internal var data: raw/i32 during self\n    public init() => self.data = null\npublic func main()\n    let b = Buf.init()\n    require b.data == null else => $abort(\"data\")\n    Console.writeLine(\"HolderNull\")", "HolderNull\n")]
    [InlineData("RawOriginLegacyHolderClaim", "struct Buf\n    internal let data: raw/i32 during self\n    public init()\n        unsafe\n            self.data = Raw.allocate<i32>(1)@(raw/i32 during self)\n            Raw.initialize(self.data, 7)\n    public func get(self: ref/Self) -> i32\n        unsafe => return self.data[0]\n    drop => unsafe => Raw.release(self.data)\npublic func main()\n    let b = Buf.init()\n    require b.get() == 7 else => $abort(\"get\")\n    Console.writeLine(\"HolderClaim\")", "HolderClaim\n")]
    [InlineData("RawOriginLegacyHolderDrop", "struct Holder\n    internal var data: raw/i32 during self\n    public init()\n        unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)\n    drop => unsafe => Raw.release(self.data)\npublic func main()\n    let h = Holder.init()\n    Console.writeLine(\"HolderDrop\")", "HolderDrop\n")]
    [InlineData("RawOriginLegacyCaseSome", "enum Cell\n    Some(raw/u8 during self)\n    None\npublic func main()\n    let c = Cell.Some(null)\n    match c@ref\n        Cell.Some(_) => Console.writeLine(\"CaseSome\")\n        Cell.None => Console.writeLine(\"none\")", "CaseSome\n")]
    [InlineData("RawOriginLegacyCaseFollow", "enum Cell\n    Some(raw/u8 during self)\n    None\npublic func main()\n    let c = Cell.Some(null)\n    let p: raw/u8 = match c@ref\n        Cell.Some(let q) => q@follow\n        Cell.None => null\n    require p == null else => $abort(\"follow\")\n    Console.writeLine(\"CaseFollow\")", "CaseFollow\n")]
    [InlineData("RawOriginLegacyCaseTwo", "enum Cell\n    Two((raw/u8 during self, i32))\n    None\npublic func main()\n    let c = Cell.Two((null, 1))\n    let n: i32 = match c@ref\n        Cell.Two((_, let k)) => k@follow\n        Cell.None => 0\n    require n == 1 else => $abort(\"two\")\n    Console.writeLine(\"CaseTwo\")", "CaseTwo\n")]
    [InlineData("RawOriginLegacyBuffer", Buffer + "public func main()\n    var b = Buffer<i32>.init(capacity: 4)\n    b.push(1)\n    b.push(2)\n    var sum: i32 = 0\n    for x in b.items()\n        sum += x\n    let v = b.view()\n    require sum + b.get(1)@follow + v.get(0)@follow == 6 else => $abort(\"sum\")\n    Console.writeLine(\"Buffer\")", "Buffer\n")]
    [InlineData("RawOriginLegacyBufferPoke", Buffer + "func total(b: ref/Buffer<i32>) -> i32\n    var sum: i32 = 0\n    for x in b.items()\n        sum += x\n    return sum\nfunc poke(y: uniq/i32)\n    let p = y@follow@raw\n    unsafe => *p = 7\npublic func main()\n    var b = Buffer<i32>.init(capacity: 4)\n    b.push(1)\n    b.push(2)\n    var n: i32 = 1\n    poke(n@uniq)\n    require total(b@ref) + b.get(1)@follow + n == 12 else => $abort(\"poke\")\n    Console.writeLine(\"BufferPoke\")", "BufferPoke\n")]
    [InlineData("RawOriginLegacyBufferBorrow", Buffer + "func f(b: ref/Buffer<i32>) -> ref/i32 during b\n    unsafe => return b.data[0]@ref\nfunc g(b: ref/Buffer<i32>) -> Slice<i32> during b\n    unsafe => return Raw.slice(b.data, b.length)\npublic func main()\n    var b = Buffer<i32>.init(capacity: 2)\n    b.push(4)\n    require f(b@ref)@follow == 4 and g(b@ref)[0] == 4 else => $abort(\"borrow\")\n    Console.writeLine(\"BufferBorrow\")", "BufferBorrow\n")]
    [InlineData("RawOriginLegacyPointerEquality", "func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b) -> bool => p == q\npublic func main()\n    let x: i32 = 1\n    let y: i32 = 2\n    let rx = x@ref\n    let ry = y@ref\n    require not f(rx, ry, rx@follow@raw, ry@follow@raw) and f(rx, rx, rx@follow@raw, rx@follow@raw) else => $abort(\"equality\")\n    Console.writeLine(\"PointerEquality\")", "PointerEquality\n")]
    [InlineData("RawOriginLegacyBlockRead", Block + "func use(fresh: raw/u8)\n    var b = Block.init()\n    let p: raw/u8 = b.data\npublic func main()\n    use(null)\n    Console.writeLine(\"BlockRead\")", "BlockRead\n")]
    [InlineData("RawOriginLegacyBlockNull", Block + "func use(fresh: raw/u8)\n    var b = Block.init()\n    b.data = null\npublic func main()\n    use(null)\n    Console.writeLine(\"BlockNull\")", "BlockNull\n")]
    [InlineData("RawOriginLegacyBlockCase", Block + "func use(fresh: raw/u8)\n    var b = Block.init()\n    let c = Cell.Some(null)\npublic func main()\n    use(null)\n    Console.writeLine(\"BlockCase\")", "BlockCase\n")]
    [InlineData("RawOriginLegacyBlockCaseMatch", Block + "func use(fresh: raw/u8)\n    var b = Block.init()\n    let c = Cell.Some(null)\n    let p: raw/u8 = match c@ref\n        Cell.Some(let q) => q@follow\n        Cell.Two((let q, _)) => q@follow\n        Cell.None => null\npublic func main()\n    use(null)\n    Console.writeLine(\"BlockCaseMatch\")", "BlockCaseMatch\n")]
    [InlineData("RawOriginLegacyBlockClaim", Block + "func use(fresh: raw/u8)\n    var b = Block.init()\n    let r = b@uniq\n    unsafe => r.data = fresh@(raw/u8 during r)\npublic func main()\n    use(null)\n    Console.writeLine(\"BlockClaim\")", "BlockClaim\n")]
    [InlineData("RawOriginLegacyBlockShared", Block + "func use(fresh: raw/u8)\n    var b = Block.init()\n    let w = b.data@ref\npublic func main()\n    use(null)\n    Console.writeLine(\"BlockShared\")", "BlockShared\n")]
    [InlineData("RawOriginLegacyBlockAggregates", Block + "func use(fresh: raw/u8)\n    var b = Block.init()\n    b.pair = (null, 2)\n    b.arr = [null, null]\n    b.pair.0 = null\n    let c = Cell.Two((null, 1))\npublic func main()\n    use(null)\n    Console.writeLine(\"BlockAggregates\")", "BlockAggregates\n")]
    [InlineData("RawOriginLegacyBlockChoose", Block + "func chooseRef(c: bool, a: ref/Block, b: ref/Block) -> ref/Block => if c => a else => b\nfunc readRef(c: bool, x: ref/Block, y: ref/Block) -> raw/u8 => chooseRef(c, x, y).data\nfunc chooseUniq(c: bool, a: uniq/Block, b: uniq/Block) -> uniq/Block => if c => a else => b\nfunc readUniq(c: bool, x: uniq/Block, y: uniq/Block) -> raw/u8 => chooseUniq(c, x, y).data\npublic func main()\n    var a = Block.init()\n    var b = Block.init()\n    require readRef(true, a@ref, b@ref) != null and readUniq(false, a@uniq, b@uniq) != null else => $abort(\"choose\")\n    Console.writeLine(\"BlockChoose\")", "BlockChoose\n")]
    [InlineData("RawOriginLegacyLensRead", RawPlaceAccessTest.Lenses + "func f(lens: ref/Lens<i32>) -> i32\n    unsafe => return *lens.at\nfunc g(lens: uniq/Lens<P>) -> ref/P during lens\n    unsafe => return (*lens.at)@ref\npublic func main()\n    let n: i32 = 5\n    let r = n@ref\n    let lens = Lens<i32>.init(r, r@follow@raw)\n    require f(lens@ref) == 5 else => $abort(\"lens\")\n    Console.writeLine(\"LensRead\")", "LensRead\n")]
    [InlineData("RawOriginLegacyLensWrite", RawPlaceAccessTest.Lenses + "func f(lens: ref/ULens<i32>)\n    unsafe => *lens.at = 0\nfunc g(lens: uniq/ULens<P>)\n    unsafe => _ = (*lens.at)@move\nfunc h(y: uniq/B)\n    unsafe => *y.data = 1\nfunc choose(c: bool, a: uniq/B, b: uniq/B) -> uniq/B => if c => a else => b\nfunc k(c: bool, x: uniq/B, y: uniq/B)\n    unsafe => *choose(c, x, y).data = 1\npublic func main()\n    Console.writeLine(\"LensWrite\")", "LensWrite\n")]
    [InlineData("RawOriginLegacyOwned", "func keep<T>(value: T)\n    T is Owned\n    _ = value@move\nstruct B<T>\n    let data: raw/T during self\n    public init() => self.data = null\nfunc f() => keep(B<i32>.init())\npublic func main()\n    f()\n    Console.writeLine(\"Owned\")", "Owned\n")]
    [InlineData("RawOriginLegacyLocal", "struct Buffer\n    internal let data: raw/i32 during self\n    public init() => unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)\nfunc shared(b: ref/Buffer) -> raw/i32 => b.data\nfunc pass(r: ref/i32, p: raw/i32 during r) -> raw/i32 => p\nfunc local(n: i32)\n    let r = n@ref\n    let p: raw/i32 during r = null\n    let q: raw/i32 = p\npublic func main()\n    local(1)\n    let b = Buffer.init()\n    require shared(b@ref) != null else => $abort(\"local\")\n    Console.writeLine(\"Local\")", "Local\n")]
    [InlineData("RawOriginLegacyPointerWrite", "struct Block\n    internal var data: raw/u8 during self\n    public init() => self.data = null\nfunc w1(fresh: raw/u8)\n    var b = Block.init()\n    let p = b.data@raw\n    unsafe => *p = fresh\nfunc w2(fresh: raw/u8)\n    var b = Block.init()\n    let r = b.data@ref\n    let s: ref/(raw/u8) = r\npublic func main()\n    w1(null)\n    w2(null)\n    Console.writeLine(\"PointerWrite\")", "PointerWrite\n")]
    [InlineData("RawOriginLegacyNullPayloads", "struct Block\n    internal var data: raw/u8 during self\n    internal var pair: (raw/u8 during self, isize)\n    public init()\n        self.data = null\n        self.pair = (null, 1)\n    drop => ()\nenum Cell\n    Some(raw/u8 during self)\n    Two((raw/u8 during self, i32))\n    None\nfunc a1()\n    var b = Block.init()\n    b.pair = (null, 2)\nfunc a10()\n    let c = Cell.Two((null, 1))\nfunc a11()\n    let c = Cell.Some(null)\npublic func main()\n    a1()\n    a10()\n    a11()\n    Console.writeLine(\"NullPayloads\")", "NullPayloads\n")]
    [InlineData("RawOriginLegacyHolderMove", "struct Block\n    internal var data: raw/u8 during self\n    internal var pair: (raw/u8 during self, isize)\n    internal var arr: [2 of raw/u8 during self]\n    public init()\n        self.data = null\n        self.pair = (null, 1)\n        self.arr = [null, null]\n    drop => ()\nfunc f() -> bool\n    var b = Block.init()\n    let w = b.data@move\n    b.data = null\n    let t = b.pair.0@move\n    b.pair.0 = null\n    let u = b.arr[1]@move\n    b.arr[1] = null\n    return w == null and t == null and u == null and b.pair.1 == 1\npublic func main()\n    require f() else => $abort(\"move\")\n    Console.writeLine(\"HolderMove\")", "HolderMove\n")]
    public void RawOriginsGenerateOnTheDefaultPipeline(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source.Replace("{0}", "()", StringComparison.Ordinal).Replace("{1}", "()", StringComparison.Ordinal), stdout);

    // SPEC 5.7 (design-final §4; bu23 S1, S2): a raw Field stating a slot has a step wherever its holder is held, inline in another
    // struct (its layout is validated after the slots' requirements exist) or behind a universal name an input binds only as a slot
    // argument (the slot's requirement is that name's step), through a `let` local and directly.
    [Theory]
    [InlineData(View + "struct Outer<T> {source}\n    internal let view: View<T> during source\n    public init(target: ref/T during source) => self.view = View<T>.init(target)\n" +
        "func use(x: ref/i32) -> i32\n    let o = Outer<i32>.init(x)\n    unsafe => return *o.view.at")]
    [InlineData(Remainder + "func take<E>(source: R<E> during a) -> uniq/E during a\n    unsafe => return source.at[0]@uniq")]
    [InlineData(Remainder + "func take<E>(source: R<E> during a) -> uniq/E during a\n    let state = source@move\n    unsafe => return state.at[0]@uniq")]
    public void ARawSlotHasAStepWhereverItsHolderIsHeld(string declarations)
        => Assert.Empty(RawPointerSyntaxTest.Records(declarations));

    // SPEC 5.7 Writing a Field, Typed paths (the final review's B1): a raw Field stating `self` or a slot, read or written through a
    // dereference of a raw pointer stating an Origin (a typed path, a parameter, `static`, an element), checks as through one stating
    // none: the old analysis compares the stored part's Type without raw Origins (the D5 exception, the owner's 「D5 例外を広げる
    // (推奨)」), so no InternalInvariant_Kd remains.
    [Theory]
    [InlineData(SelfField + "func f(x: ref/B) -> raw/u8 during x\n    let p = x@follow@raw\n    unsafe\n        let q = (*p).data\n        return q")]
    [InlineData(SelfField + "func f(x: ref/B, p: raw/B during x) -> raw/u8 during x\n    unsafe => return (*p).data")]
    [InlineData(SelfField + "func f(x: ref/B) -> raw/u8 during x\n    let p = x@follow@raw\n    unsafe => return p[0].data")]
    [InlineData(SelfField + "func f(x: uniq/B, p: raw/B during x)\n    unsafe => (*p).data = null")]
    [InlineData(SelfField + "func f(p: raw/B during static)\n    unsafe => (*p).data = null")]
    [InlineData(SelfField + "func f(x: uniq/B, p: raw/B during x, fresh: raw/u8)\n    unsafe => (*p).data = fresh@(raw/u8 during x)")]
    [InlineData(SlotField + "func f(x: ref/Lens) -> raw/i32 during x\n    let p = x@follow@raw\n    unsafe => return (*p).at")]
    [InlineData(SlotField + "func f(x: uniq/Lens, p: raw/Lens during x)\n    unsafe => (*p).at = null")]
    [InlineData(SlotField + "func f(x: ref/Lens, p: raw/Lens during x) -> i32\n    unsafe => return *(*p).at")]
    public void AStatedRawFieldThroughAStatedRawPointerChecks(string declarations)
        => Assert.Empty(RawPointerSyntaxTest.Records(declarations));

    // SPEC 5.2.2, 15.6.5 (bu23 S3): a raw Place borrow's Origin is its anchor, an inferred region, met with the pointer's Origin; the
    // meet outlives an Origin when each operand does, so a borrow kept in a local and then returned has the direct form's verdict: valid
    // through a `var` local whose region's source is the input, an error through a local's own borrow.
    [Theory]
    [InlineData(Remainder + "func take<E>(source: R<E> during a) -> uniq/E during a\n    var state = source@move\n    unsafe\n        let item = state.at[0]@uniq\n        return item@move", null)]
    [InlineData(Remainder + "func take<E>(source: R<E> during a) -> uniq/E during a\n    var state = source@move\n    unsafe => return state.at[0]@uniq", null)]
    [InlineData("func bad(x: uniq/i32) -> uniq/i32 during x\n    var n: i32 = 1\n    let r = n@uniq\n    let p = r@follow@raw\n    unsafe\n        let item = p[0]@uniq\n        return item@move", "UnsatisfiedOriginRelation_Kd: item@move")]
    public void AKeptRawPlaceBorrowHasTheDirectFormsVerdict(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], RawPointerSyntaxTest.Records(declarations));

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
