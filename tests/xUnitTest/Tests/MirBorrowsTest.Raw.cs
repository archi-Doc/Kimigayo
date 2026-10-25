// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's raw addresses (SPEC 15.6 Addresses and reach, 5.2.2, 5.7; DECISIONS R4-M95, R4-M116): a raw pointer carries the holders it
/// addresses and the Loans it was formed through, a write through it reaches them, a stored raw layer is classed by its step, and the
/// located Unsupported_Kd of a Borrow or read of a `during self` layer's enclosing Place.</summary>
public sealed partial class MirBorrowsTest
{
    // The raw shapes: a box whose raw Field states no Origin, a cursor over a Type argument, an owning buffer, a Field written by a call
    // the contract ties to nothing, a layer inside a Tuple Field, and the calls through them.
    private const string RawShapes = """
        func usep<T>(p: raw/T) => ()
        struct RawBox {a}
            public var p: raw/(ref/i32 during a)
            public init(p: raw/(ref/i32 during a)) => self.p = p
        func storeRaw(b: uniq/(RawBox during a), x: ref/i32 during a)
            unsafe => Raw.initialize(b.p, x)
        func storeRawV(b: RawBox during a, x: ref/i32 during a)
            unsafe => Raw.initialize(b.p, x)
        func storeRaw2(b: uniq/(uniq/(RawBox during a) during c), x: ref/i32 during a)
            unsafe => Raw.initialize(b@follow.p, x)
        func storeRawT(t: uniq/((i32, RawBox during a)), x: ref/i32 during a)
            unsafe => Raw.initialize(t@follow.1.p, x)
        func storeShared(b: ref/(RawBox during a), x: ref/i32 during a)
            unsafe => Raw.initialize(b.p, x)
        struct Cursor<T>
            public var p: raw/T
            public init(p: raw/T) => self.p = p
        func setC(c: uniq/Cursor<ref/i32 during a>, x: ref/i32 during a)
            unsafe => Raw.initialize(c.p, x)
        struct Buf<T>
            internal var data: raw/T during self
            internal var length: isize
            public init(capacity: isize)
                unsafe => self.data = Raw.allocate<T>(capacity)@(raw/T during self)
                self.length = 0
            public func push(self: uniq/Self, value: T)
                unsafe => Raw.initialize(self.data + self.length, value@move)
                self.length += 1
        struct Pairs
            internal var pair: (raw/u8 during self, isize)
            public init()
                unsafe => self.pair = (Raw.allocate<u8>(1)@(raw/u8 during self), 1)
        struct Hid {a}
            public var r: ref/i32 during a
            public var p: raw/(ref/i32 during a)
            public init(r: ref/i32 during a, p: raw/(ref/i32 during a))
                self.r = r
                self.p = p
        func make(r: ref/i32 during a) -> Hid during a
            let cell: raw/(ref/i32 during a) = Raw.allocate(1)
            return Hid.init(r: r, p: cell)
        func fill(h: uniq/(Hid during a))
            h.p = Raw.allocate(1)
        func addr(v: uniq/i64) -> raw/i64 during v => v@follow@raw
        func deref2(p: raw/(ref/i32 during a)) -> ref/i32 during a
            unsafe => return *p

        """;

    // SPEC 15.6 Addresses and reach (G184, G160 (b), D2): `w@raw` addresses w's holder, so a read through it carries what w holds and a
    // write through it adds to w, also through a raw chain; a pointer keeps the holders it addresses used (D2) though nothing reads
    // through it; a Raw Place borrow of w's referent carries what the pointer reaches (5.2.2); the dead twins are valid.
    [Fact]
    public void ARawPointerCarriesTheHoldersItAddresses()
        => Assert.Equal(
            ["readThrough: y = 5", "chain: x = 5", "compared: y = 5", "sliced: y = 5"],
            Problems(RawShapes + """
                func readThrough()
                    var y: i32 = 1
                    var v: i32 = 2
                    var w: ref/i32 = y@ref
                    let p = w@raw
                    var q: ref/i32 = v@ref
                    unsafe => q = *p
                    y = 5
                    inspect(q)
                func readThroughDead()
                    var y: i32 = 1
                    var v: i32 = 2
                    var w: ref/i32 = y@ref
                    let p = w@raw
                    var q: ref/i32 = v@ref
                    unsafe => q = *p
                    y = 5
                func chain()
                    var x: i32 = 1
                    var y: i32 = 2
                    var w: ref/i32 = y@ref
                    let p = w@raw
                    let c = p@raw
                    unsafe => Raw.initialize(*c, x@ref)
                    x = 5
                    inspect(w)
                func compared()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let p = w@raw
                    y = 5
                    _ = p == null
                func sliced()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    unsafe
                        let s = Raw.slice(w@raw, 1)
                        let first = s[0]
                        y = 5
                        inspect(first)
                """));

    // SPEC 5.7 Calls, 15.6, 15.6.4 (G160 (a), (c), (d); D6, D10; DECISIONS R4-M116 S2): a call stores through a raw Field what its contract
    // says, reaching what the holder's pointer reaches; a result whose Type stores a raw Field stating no Origin is tied to no argument (a
    // hidden pointer, condition 5), so such a holder's pointer reaches storage the body does not own: by uniq, by value, past two uniq
    // layers (where the call may also store what a read through the outer layer carries, b's Loan), in a Tuple holder, and a cursor built
    // from w@raw (with the read through it the call may store back); nothing past a shared layer (c, d: a write there is undefined).
    [Fact]
    public void ACallStoresThroughARawFieldWhereItsPointerReaches()
        => Assert.Equal(
            [
                "byUniq: x = 5", "byValue: x = 5", "twoLayers: x = 5", "twoLayers: var b = RawBox.init(p: cell)", "inTuple: x = 5", "cursorLive: x = 5",
                "cursorLive: var y: i32 = 1",
            ],
            Problems(RawShapes + """
                func byUniq()
                    var x: i32 = 1
                    let cell: raw/(ref/i32) = Raw.allocate(1)
                    var b = RawBox.init(p: cell)
                    storeRaw(b@uniq, x@ref)
                    x = 5
                func byValue()
                    var x: i32 = 1
                    let cell: raw/(ref/i32) = Raw.allocate(1)
                    let b = RawBox.init(p: cell)
                    storeRawV(b@move, x@ref)
                    x = 5
                func twoLayers()
                    var x: i32 = 1
                    let cell: raw/(ref/i32) = Raw.allocate(1)
                    var b = RawBox.init(p: cell)
                    var u = b@uniq
                    storeRaw2(u@uniq, x@ref)
                    x = 5
                func inTuple()
                    var x: i32 = 1
                    let cell: raw/(ref/i32) = Raw.allocate(1)
                    var t = (0, RawBox.init(p: cell))
                    storeRawT(t@uniq, x@ref)
                    x = 5
                func pastShared()
                    var x: i32 = 1
                    let cell: raw/(ref/i32) = Raw.allocate(1)
                    let b = RawBox.init(p: cell)
                    storeShared(b@ref, x@ref)
                    x = 5
                func cursorLive()
                    var y: i32 = 1
                    var x: i32 = 2
                    var w: ref/i32 = y@ref
                    var c = Cursor<ref/i32>.init(w@raw)
                    setC(c@uniq, x@ref)
                    x = 5
                    inspect(w)
                """));

    // SPEC 5.7 Reading a Field, 5.2.1 (G184, `during self`): a pointer read from a `during self` Field reaches its holder's own storage, so a
    // Loan pushed into the buffer is read back through it, directly and through a borrow of the buffer; a dead buffer is valid; the Loan
    // the buffer keeps is checked while it lives.
    [Fact]
    public void ADuringSelfLayerReachesItsHolder()
        => Assert.Equal(
            ["direct: y = 5", "throughRef: y = 5", "live: y = 5"],
            Problems(RawShapes + """
                func direct()
                    var y: i32 = 1
                    var v: i32 = 2
                    var b = Buf<ref/i32>.init(4)
                    b.push(y@ref)
                    let p = b.data
                    var e: ref/i32 = v@ref
                    unsafe => e = p[0]
                    y = 5
                    inspect(e)
                func throughRef()
                    var y: i32 = 1
                    var v: i32 = 2
                    var b = Buf<ref/i32>.init(4)
                    b.push(y@ref)
                    let r = b@ref
                    let p = r.data
                    var e: ref/i32 = v@ref
                    unsafe => e = p[0]
                    y = 5
                    inspect(e)
                func live()
                    var y: i32 = 1
                    var b = Buf<ref/i32>.init(4)
                    b.push(y@ref)
                    y = 5
                    inspect(b@ref)
                func dead()
                    var y: i32 = 1
                    var b = Buf<ref/i32>.init(4)
                    b.push(y@ref)
                    y = 5
                """));

    // SPEC 15.6 (condition 5, D6; DECISIONS R4-M116 S2, S3, R-B2, B-B1): a pointer the contract ties to nothing reaches storage the body
    // does not own: in a result's raw Field (hiddenInit, hiddenHolder), written into an input's raw Field by the call (fill, which may also
    // store what a read through h carries, v's Loan, where h's untied pointer reaches), or formed through a local that holds no Loan
    // (punCall; punned, the residual: a Loan-free local keeps no Loan, so E does); a raw argument's pointee is read through it into the
    // result (snap); a pointer that addresses a Loan-free local and stores a value with no Loan is valid.
    [Fact]
    public void AnUntiedPointerReachesStorageTheBodyDoesNotOwn()
        => Assert.Equal(
            [
                "hiddenInit: y = 5", "hiddenHolder: y = 5", "filled: y = 5", "filled: var v: i32 = 2", "punCall: y = 5", "punBorrow: y = 5",
                "punAddress: y = 5", "snap: y = 5",
            ],
            Problems(RawShapes + """
                func hiddenInit()
                    var y: i32 = 1
                    var v: i32 = 2
                    let h = make(v@ref)
                    unsafe => Raw.initialize(h.p, y@ref)
                    y = 5
                func hiddenHolder()
                    var y: i32 = 1
                    var t = (5, y@ref)
                    let h = make(t.0@ref)
                    unsafe => Raw.initialize(h.p, y@ref)
                    y = 5
                func filled()
                    var y: i32 = 1
                    var v: i32 = 2
                    var w: ref/i32 = v@ref
                    var h = Hid.init(r: v@ref, p: w@raw)
                    fill(h@uniq)
                    unsafe => Raw.initialize(h.p, y@ref)
                    y = 5
                func punCall()
                    var y: i32 = 1
                    var v: i32 = 2
                    var x: i64 = 0
                    let q = addr(x@uniq)
                    let q1: raw/(ref/i32) = q@raw/(ref/i32)
                    unsafe => Raw.initialize(q1, y@ref)
                    var z: ref/i32 = v@ref
                    unsafe => z = *q1
                    y = 5
                    inspect(z)
                func punBorrow()
                    var y: i32 = 1
                    var x: i64 = 0
                    let r = x@uniq
                    let q = r@follow@raw
                    let q1: raw/(ref/i32) = q@raw/(ref/i32)
                    unsafe => Raw.initialize(q1, y@ref)
                    y = 5
                func punAddress()
                    var y: i32 = 1
                    var x: i64 = 0
                    let q = x@raw
                    let q1: raw/(ref/i32) = q@raw/(ref/i32)
                    unsafe => Raw.initialize(q1, y@ref)
                    y = 5
                func punValid(k: ref/i32)
                    var x: i64 = 0
                    let q = x@raw
                    let q1: raw/(ref/i32) = q@raw/(ref/i32)
                    unsafe => Raw.initialize(q1, k)
                func snap()
                    var y: i32 = 1
                    var z: i32 = 2
                    var w: ref/i32 = y@ref
                    let res = deref2(w@raw)
                    w = z@ref
                    y = 5
                    inspect(res)
                """));

    // SPEC 15.6 (a raw pointer that reaches no holder reaches only storage the body does not own, where what is stored may be used after
    // every point; condition 5; DECISIONS R4-M120's departure 1, B1): what that storage holds may refer to it, so a pointer read through a
    // parameter reaches it from any local holder that keeps it, and a call storing x's Loan through the holder keeps it there: a raw local,
    // a Tuple, and a Tuple beside a direct part (whose own Loan the call may store there too, SPEC 15.6.4's per-argument letter). Valid
    // twins: the same holders with the pointer formed to a local holder that is dead after the call, and a value with no Loan of the body
    // stored through the pointer read.
    [Fact]
    public void APointerReadFromStorageTheBodyDoesNotOwnReachesIt()
        => Assert.Equal(
            ["viaUniqLocal: x = 5", "viaUniqTuple: x = 5", "viaUniqMixed: x = 5", "viaUniqMixed: var w: ref/i32 = y@ref"],
            Problems(RawShapes + """
                func storeThrough(c: uniq/(raw/(ref/i32 during a)), x: ref/i32 during a)
                    unsafe => Raw.initialize(c@follow, x)
                func storeT(t: uniq/((raw/(ref/i32 during a), i32)), x: ref/i32 during a)
                    unsafe => Raw.initialize(t@follow.0, x)
                func storeM(t: uniq/((raw/(ref/i32 during a), uniq/(ref/i32 during a) during c)), x: ref/i32 during a)
                    unsafe => Raw.initialize(t@follow.0, x)
                func viaUniqLocal(u: uniq/(raw/i64))
                    var x: i32 = 1
                    var c2: raw/(ref/i32) = (u@follow)@raw/(ref/i32)
                    storeThrough(c2@uniq, x@ref)
                    x = 5
                func viaUniqTuple(u: uniq/(raw/i64))
                    var x: i32 = 1
                    let c2: raw/(ref/i32) = (u@follow)@raw/(ref/i32)
                    var t = (c2, 0)
                    storeT(t@uniq, x@ref)
                    x = 5
                func viaUniqMixed(u: uniq/(raw/i64))
                    var x: i32 = 1
                    var y: i32 = 2
                    var w: ref/i32 = y@ref
                    let c2: raw/(ref/i32) = (u@follow)@raw/(ref/i32)
                    var t = (c2, w@uniq)
                    storeM(t@uniq, x@ref)
                    x = 5
                func formedLocal()
                    var x: i32 = 1
                    var v: i32 = 2
                    var cell: ref/i32 = v@ref
                    var c2 = cell@raw
                    storeThrough(c2@uniq, x@ref)
                    x = 5
                func formedTuple()
                    var x: i32 = 1
                    var v: i32 = 2
                    var cell: ref/i32 = v@ref
                    var t = (cell@raw, 0)
                    storeT(t@uniq, x@ref)
                    x = 5
                func formedMixed()
                    var x: i32 = 1
                    var y: i32 = 2
                    var v: i32 = 3
                    var w: ref/i32 = y@ref
                    var cell: ref/i32 = v@ref
                    var t = (cell@raw, w@uniq)
                    storeM(t@uniq, x@ref)
                    x = 5
                func noLoan(u: uniq/(raw/i64), k: ref/i32)
                    let c2: raw/(ref/i32) = (u@follow)@raw/(ref/i32)
                    var t = (c2, 0)
                    storeT(t@uniq, k)
                """));

    // DECISIONS R4-M116 PQ-10 (B′ §4.3; registered for X-2): a Borrow of a `during self` layer's Place, or of a Tuple Place enclosing one,
    // and a Copy read of such a Tuple, are located Unsupported_Kd until the Loan records the layer's own-step entry; a Borrow of the holder
    // and a read of the layer through it are precise.
    [Fact]
    public void ABorrowOfADuringSelfLayerIsUnsupported()
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + """
            func layerRef()
                var y: i32 = 1
                var b = Buf<ref/i32>.init(4)
                b.push(y@ref)
                let r = b.data@ref
                usep(r@follow)
            func enclosingRef()
                let s = Pairs.init()
                let r = s.pair@ref
                usep(r@follow.0)
            func enclosingCopy()
                let s = Pairs.init()
                let t = s.pair
                usep(t.0)
            func holderRef()
                var y: i32 = 1
                var b = Buf<ref/i32>.init(4)
                b.push(y@ref)
                let r = b@ref
                let p = r.data
            """);
        Assert.Equal(
            ["layerRef: b.data@ref Unsupported_Kd", "enclosingRef: s.pair@ref Unsupported_Kd", "enclosingCopy: s.pair Unsupported_Kd"],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }
}
