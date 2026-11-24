// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's raw addresses (SPEC 15.6 Addresses and reach, 5.2.2, 5.7; DECISIONS R4-M95, R4-M116): a raw pointer carries the holders it
/// addresses and the Loans it was formed through, a write through it reaches them, a stored raw layer is classed by its step, and a read
/// of a `during self` layer or of a Place enclosing one carries its owner's own-step entry.</summary>
public sealed partial class MirBorrowsTest
{
    // Z5-R2's shapes beside LentShapes: a holder of a Type argument beside an enclosing Tuple, a fixed array of enclosing Tuples, and a
    // holder whose Type argument holds a raw pointer (no `self`, SPEC 5.7 Origins).
    private const string EnclosingShapes = """
        func usen(n: i32) => ()
        struct Hp<T>
            internal var v: T
            internal var pair: (raw/u8 during self, i32)
            public init(v: T)
                self.v = v@move
                self.pair = (null, 0)
        struct Arr
            internal var ta: [2 of (raw/u8 during self, i32)]
            public init() => self.ta = [(null, 1), (null, 2)]
        struct W<T>
            internal var v: T
            public init(v: T) => self.v = v@move

        """;

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

    // A struct holding raw layers without an Origin, built by an untied constructor, written through its own Field by value (writeVia)
    // and through a borrow (writeViaRef).
    private const string PlainShapes = """
        struct Plain
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
        func writeVia(x: Plain)
            unsafe => *x.p = x.q
        func writeViaRef(x: uniq/Plain)
            unsafe => *x.p = x.q

        """;

    // By-value arguments holding raw layers without an Origin (PLAN G266): PlainShapes' struct in a generic box, a struct's Field
    // (Outer) and an enum's payload (E2), and callees taking them by value: written through another argument (writeVia2) or two Fields
    // deep (writeDeep), and in an Option, a box's Field, an Array, an Outer, a generic box or an E2.
    private const string ByValueShapes = PlainShapes + """
        struct Bx<T>
            public var item: T
            public init(item: T) => self.item = item@move
        struct Outer
            public var inner: Plain
            public init(inner: Plain) => self.inner = inner@move
        enum E2
            A(i32)
            B(Plain)
        func writeVia2(x: Plain, q: raw/u8)
            unsafe => *x.p = q
        func writeDeep(x: Outer)
            unsafe => *x.inner.p = x.inner.q
        func writeO(x: Option<Plain>, q: raw/u8) => ()
        func writeBP(x: Bx<Plain>, q: raw/u8) => ()
        func writeA(x: Array<Plain>, q: raw/u8) => ()
        func writeDeepQ(x: Outer, q: raw/u8) => ()
        func writeG<T>(x: Bx<T>, q: raw/u8) => ()
        func writeE(x: E2, q: raw/u8) => ()

        """;

    // Calls whose unstated raw layers give Chain rows (SPEC 15.6.4: rule A, R′): two reads through a raw argument, through a reference,
    // into a raw slot, through the written argument itself, and three behind a result borrow (lent); and a raw pointer passed on.
    private const string ChainShapes = """
        func getRRR(ppp: raw/(raw/(raw/u8))) -> raw/u8
            unsafe => return **ppp
        func getRFR(r: ref/(raw/(raw/u8))) -> raw/u8
            unsafe => return *r@follow
        func fRRR(ppp: raw/(raw/(raw/u8)), slot: raw/(raw/u8))
            unsafe => *slot = **ppp
        func unlinkR(p: raw/(raw/(raw/u8)))
            unsafe => *p = (**p)@raw/(raw/u8)
        func lend3(pppp: raw/(raw/(raw/(raw/u8))), x: ref/i32) -> ref/(raw/u8) during x
            unsafe => return (***pppp)@ref
        func touch(p: raw/u8) -> raw/u8 => p

        """;

    // SPEC 15.6 Addresses and reach (G184, G160 (b), D2): `w@raw` addresses w's holder, so a read through it carries what w holds and a
    // write through it adds to w, also through a raw chain; a pointer keeps the holders it addresses used (D2) though nothing reads
    // through it; a Raw Place borrow of w's referent carries what the pointer reaches (5.2.2); an Adapt the view resolves to a raw Ref
    // (`w@s` under `s is raw`, SPEC 8.14) addresses w's holder as `w@raw` does; the dead twins are valid, and so is partAddresses
    // (PartAddressesInOneHolderCostAsTheirEntries' form at N = 4: kk's Loan stored through part addresses of h, and kk not written after).
    [Fact]
    public void ARawPointerCarriesTheHoldersItAddresses()
        => Assert.Equal(
            ["readThrough: y = 5", "chain: x = 5", "compared: y = 5", "sliced: y = 5", "adapted: y = 5"],
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
                func adapted<s/T>()
                    s is raw
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let p = w@s
                    y = 5
                    let q = p@move
                func adaptedDead<s/T>()
                    s is raw
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let p = w@s
                    y = 5
                """ + "\n" + PartAddresses(4)));

    // SPEC 15.6 Addresses (DECISIONS R4-M146): A5 empties the addresses and layer entries of a view that formed many whole and a small
    // view's entry by entry (MirTable), so the views after one with 400 of each report what they report alone (the test above).
    [Fact]
    public void TheViewsAfterManyAddressesReportWhatTheyReportAlone()
    {
        var many = new StringBuilder("func many()\n");
        for (var i = 0; i < 400; i++)
        {
            many.Append($"    var v{i}: i32 = {i}\n    let c{i} = Cursor.init(p: v{i}@raw)\n    let p{i} = c{i}.p\n");
        }

        Assert.Equal(
            ["readThrough: y = 5", "chain: x = 5"],
            Problems(RawShapes + many + """
                func readThrough()
                    var y: i32 = 1
                    var v: i32 = 2
                    var w: ref/i32 = y@ref
                    let p = w@raw
                    var q: ref/i32 = v@ref
                    unsafe => q = *p
                    y = 5
                    inspect(q)
                func chain()
                    var x: i32 = 1
                    var y: i32 = 2
                    var w: ref/i32 = y@ref
                    let p = w@raw
                    let c = p@raw
                    unsafe => Raw.initialize(*c, x@ref)
                    x = 5
                    inspect(w)
                """));
    }

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

    // SPEC 5.2.1 condition 3, 5.7 Authority, Calls (DECISIONS BU-108): a callee's contract judges a layer a Type argument supplies by its
    // own inputs, so where only a shared input borrow states its Origin the call stores nothing past it: refVia and rawVia are valid
    // (fail-before: a false conflict at `y = 3`). Where a `uniq` input states it too (uniqVia), or a holder keeps the slot it binds by an
    // exclusive Loan Field (slotAlias, its layer declared; slotBoxAlias, in its Field's Type argument), the call may store past it and
    // `y = 3` / `z = 5` err, as before (the review's r04, r27n, r29). bothVia (r05), whose callee states the Origin by `ref` and `uniq`,
    // is valid: h's pointer was taken through `let s = t@ref`, so it has no write authority (SPEC 5.2.1 condition 3, its r@follow@raw
    // example) and rule W writes no holder that only a shared Loan names (SPEC 15.6.4 item 1, DECISIONS R4-M177); its twins err: the
    // callee may store v into t2 through u (bothT2), and a pointer taken through `t@uniq` names t by an exclusive Loan (bothUniq). The
    // cap judges a layer by its route (the owner's O-23, 「aでお願いいたす」): a layer in a Closure's environment steps by its kind, so a
    // captured layer stating an Origin the enclosing signature also states by `uniq` lets the call store w's address into u's referent
    // and `z = 5` errs (capturedBox, a supplied one, accepted on feaff9158; capturedRaw, capturedNested and nestedReceives, whose captured
    // `p` the outer Closure writes, accepted on dev); a layer written in a Closure's or nested function's own signature keeps the cap
    // whatever binds its Origin, so closureParam, nestedParam and paramOnly (a supplied one) are valid (they err on 22d1216a0, paramOnly
    // also on dev).
    [Fact]
    public void ACallStoresPastASuppliedLayerByItsContractsInputs()
        => Assert.Equal(
            [
                "uniqVia: y = 3", "uniqVia: inspect(t)", "bothT2: y = 3", "bothUniq: y = 3", "slotAlias: z = 5", "slotBoxAlias: z = 5",
                "capturedBox: z = 5", "capturedRaw: z = 5", "capturedNested: z = 5", "nestedReceives: z = 5",
            ],
            Problems("""
                struct Wrap<E>
                    var value: E
                    public init(value: E) => self.value = value@move
                struct Box2<E>
                    public var value: E
                    public init(value: E) => self.value = value@move
                struct HS {s}
                    let loan: Loan<uniq/(raw/u8) during s>
                    let b: raw/(raw/u8) during s
                    public init(t: uniq/(raw/u8) during s)
                        self.b = t@follow@raw
                        self.loan = Loan.init(t)
                    public func store(self: uniq/Self, v: raw/u8)
                        unsafe => *self.b = v
                struct HB {s}
                    let loan: Loan<uniq/(raw/u8) during s>
                    var b: Box2<raw/(raw/u8) during s>
                    public init(t: uniq/(raw/u8) during s)
                        self.b = Box2.init(t@follow@raw)
                        self.loan = Loan.init(t)
                    public func store(self: uniq/Self, v: raw/u8)
                        unsafe => *self.b.value = v
                func storeVia(w: uniq/Wrap<raw/(ref/i32 during c) during a>, r: ref/(ref/i32 during c) during a, v: ref/i32 during c) => ()
                func storeViaU(w: uniq/Wrap<raw/(ref/i32 during c) during a>, r: uniq/(ref/i32 during c) during a, v: ref/i32 during c) => ()
                func storeViaB(w: uniq/Wrap<raw/(ref/i32 during c) during a>, r: ref/(ref/i32 during c) during a, u: uniq/(ref/i32 during c) during a, v: ref/i32 during c) => ()
                func storeRawVia(w: uniq/Wrap<raw/(raw/u8) during a>, r: ref/(raw/u8) during a, v: raw/u8) => ()
                func refVia()
                    var x = 1
                    var y = 2
                    var t: ref/i32 = x@ref
                    let s = t@ref
                    var h = Wrap.init(s@follow@raw)
                    storeVia(h@uniq, s, y@ref)
                    y = 3
                    inspect(t)
                func uniqVia()
                    var x = 1
                    var y = 2
                    var t: ref/i32 = x@ref
                    var s = t@uniq
                    var h = Wrap.init(s@follow@raw)
                    storeViaU(h@uniq, s, y@ref)
                    y = 3
                    inspect(t)
                func bothVia()
                    var x = 1
                    var y = 2
                    var x2 = 4
                    var t: ref/i32 = x@ref
                    var t2: ref/i32 = x2@ref
                    let s = t@ref
                    var h = Wrap.init(s@follow@raw)
                    storeViaB(h@uniq, s, t2@uniq, y@ref)
                    y = 3
                    inspect(t)
                func bothT2()
                    var x = 1
                    var y = 2
                    var x2 = 4
                    var t: ref/i32 = x@ref
                    var t2: ref/i32 = x2@ref
                    let s = t@ref
                    var h = Wrap.init(s@follow@raw)
                    storeViaB(h@uniq, s, t2@uniq, y@ref)
                    y = 3
                    inspect(t2)
                func bothUniq()
                    var x = 1
                    var y = 2
                    var x2 = 4
                    var x3 = 5
                    var t: ref/i32 = x@ref
                    var t2: ref/i32 = x2@ref
                    var t3: ref/i32 = x3@ref
                    var s = t@uniq
                    var h = Wrap.init(s@follow@raw)
                    let r3 = t3@ref
                    storeViaB(h@uniq, r3, t2@uniq, y@ref)
                    y = 3
                    inspect(s)
                func rawVia()
                    var y = 2
                    var slot: raw/u8 = null
                    let s = slot@ref
                    var h = Wrap.init(s@follow@raw)
                    let k = y@ref
                    storeRawVia(h@uniq, s, k@raw@raw/u8)
                    y = 3
                    inspect(slot@ref)
                func slotAlias(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during c)
                    origin c outlives a
                    var z = 3
                    let w = z@ref
                    let u2: uniq/(raw/u8) during a = u
                    var hs: HS during a = HS.init(u2)
                    hs.store(w@raw@raw/u8)
                    z = 5
                func slotBoxAlias(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during c)
                    origin c outlives a
                    var z = 3
                    let w = z@ref
                    let u2: uniq/(raw/u8) during a = u
                    var hb: HB during a = HB.init(u2)
                    hb.store(w@raw@raw/u8)
                    z = 5
                func capturedBox(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    let bx = Box2.init(u@follow@raw)
                    var k = func [bx@move] (q: ref/(raw/u8) during a, v: raw/u8)
                        unsafe => *bx.value = v
                    k(r, w@raw@raw/u8)
                    z = 5
                func capturedRaw(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    let p = u@follow@raw
                    var k = func [p] (q: ref/(raw/u8) during a, v: raw/u8)
                        unsafe => *p = v
                    k(r, w@raw@raw/u8)
                    z = 5
                func capturedNested(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    let p = u@follow@raw
                    var outer = func [p] (r2: ref/(raw/u8) during a, v: raw/u8)
                        unsafe => *p = null
                        var inner = func [p] (q: ref/(raw/u8) during a, v2: raw/u8)
                            unsafe => *p = v2
                        inner(r2, v)
                    outer(r, w@raw@raw/u8)
                    z = 5
                func closureParam(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var k = func (q: ref/(raw/u8) during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8) => ()
                    var x = u@follow@raw
                    k(r, x@uniq, w@raw@raw/u8)
                    z = 5
                func nestedParam(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn(q: ref/(raw/u8) during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8) => ()
                    var x = u@follow@raw
                    passOn(r, x@uniq, w@raw@raw/u8)
                    z = 5
                func nestedReceives(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    let p = u@follow@raw
                    var outer = func [p] (r2: ref/(raw/u8) during a, v: raw/u8)
                        unsafe => *p = null
                        var inner = func (q: ref/(raw/u8) during a, pp: raw/(raw/u8) during a, v2: raw/u8) => ()
                        inner(r2, p, v)
                    outer(r, w@raw@raw/u8)
                    z = 5
                func paramOnly(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var other = Box2.init(u@follow@raw)
                    var k = func (q: ref/(raw/u8) during a, h: uniq/Box2<raw/(raw/u8) during a>, v: raw/u8) => ()
                    k(r, other@uniq, w@raw@raw/u8)
                    z = 5
                """));

    // SPEC 15.6.4 (PLAN G266; DECISIONS BU-52): a call writes through the raw layers without an Origin a by-value argument holds, in a
    // struct's Field (viaBad, via2Bad), an enum's payload (optBad), a Type argument's Field (boxPlainBad), an Array's element
    // (arrPlainBad) or behind a `uniq` Field (uBad), and a consuming callee's cleanup does through its drop (consumeWBad, consumeDrop2Bad,
    // arrConsumeBad), so `slot` may keep w's address and with it y's Loan: each errs at `y = 5` as its borrowed twin does (fail-before:
    // accepted). viaUntiedBad leaves `x.p` as `Plain.init` made it, which by that contract may address storage the body does not own, so
    // the write may keep y's Loan there (R4-M116 S2's hiddenInit), however `slot` is used; so do three forms that use it before `y = 5`
    // by a constructor's contract: boxUntiedBad boxes x after `x.p = slot@raw`, and `Bx.init`'s result is untied, so the box's `item.p`
    // may address such storage and writeBP may store w's address there; genUntiedBad's writeG<T> is a valid program, rejected again since c3-5 as on fcd6a5381 (a known limit of this tree's A5, which maps the key past a hidden layer to storage the body does not own: open issue "A5's Φ_E exclusion past a hidden layer", PLAN G935 c3-5): `x.item` is a T, whose
    // binding's raw Field adds no reach of its own, and q is no T; slot, the holder that Field addresses, may take q but is used only
    // before `y = 5` (SPEC 15.6.4, the owner's O-360 (a) and O-312 (a)); deepUntiedBad hands `Plain.init(q: w..)`
    // to `Outer.init`, which may write that chain, y's Loan with it, through `inner.p` as `Plain.init` left it (the reviews' genPOk,
    // boxPlainOk and deepOk). Valid: a Tuple no untied constructor built, used before `y = 5` (tupOk), and arguments whose chain carries
    // no Loan (viaQuietOk, consumeQuietOk).
    [Fact]
    public void ACallWritesThroughTheRawLayersOfAByValueArgument()
        => Assert.Equal(
            [
                "viaBad: y = 5", "via2Bad: y = 5", "viaUntiedBad: y = 5", "consumeWBad: y = 5", "consumeDrop2Bad: y = 5", "arrConsumeBad: y = 5",
                "optBad: y = 5", "boxPlainBad: y = 5", "arrPlainBad: y = 5", "uBad: y = 5", "genUntiedBad: y = 5", "boxUntiedBad: y = 5",
                "deepUntiedBad: y = 5",
            ],
            Problems(RawShapes + ReferentShapes + ByValueShapes + """
                struct WU {a}
                    public var r: uniq/(raw/u8) during a
                    public init(r: uniq/(raw/u8) during a) => self.r = r
                func consumeW(x: Writer) => ()
                func writeT(x: (raw/(raw/u8), raw/u8))
                    unsafe => *x.0 = x.1
                func writeU(x: WU during a, q: raw/u8)
                    x.r@follow = q
                func viaBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: w@raw@raw/u8)
                    x.p = slot@raw
                    writeVia(x@move)
                    y = 5
                    usep(slot)
                func via2Bad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: null)
                    x.p = slot@raw
                    writeVia2(x@move, w@raw@raw/u8)
                    y = 5
                    usep(slot)
                func viaUntiedBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: w@raw@raw/u8)
                    writeVia(x@move)
                    y = 5
                    usep(slot)
                func consumeWBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Writer.init(q: w@raw@raw/u8)
                    x.p = slot@raw
                    consumeW(x@move)
                    y = 5
                    usep(slot)
                func consumeDrop2Bad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Writer.init(q: w@raw@raw/u8)
                    x.p = slot@raw
                    consume(x@move)
                    y = 5
                    usep(slot)
                func arrConsumeBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Writer.init(q: w@raw@raw/u8)
                    x.p = slot@raw
                    var a: Array<Writer> = []
                    a.append(x@move)
                    consume(a@move)
                    y = 5
                    usep(slot)
                func optBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: null)
                    x.p = slot@raw
                    writeO(.Some(x@move), w@raw@raw/u8)
                    y = 5
                    usep(slot)
                func boxPlainBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: null)
                    x.p = slot@raw
                    writeBP(Bx.init(item: x@move), w@raw@raw/u8)
                    y = 5
                    usep(slot)
                func arrPlainBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: null)
                    x.p = slot@raw
                    var a: Array<Plain> = []
                    a.append(x@move)
                    writeA(a@move, w@raw@raw/u8)
                    y = 5
                    usep(slot)
                func uBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    writeU(WU.init(r: slot@uniq), w@raw@raw/u8)
                    y = 5
                    usep(slot)
                func genUntiedBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: null)
                    x.p = slot@raw
                    writeG(Bx.init(item: x@move), w@raw@raw/u8)
                    usep(slot)
                    y = 5
                func boxUntiedBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: null)
                    x.p = slot@raw
                    writeBP(Bx.init(item: x@move), w@raw@raw/u8)
                    usep(slot)
                    y = 5
                func deepUntiedBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Outer.init(inner: Plain.init(q: w@raw@raw/u8))
                    x.inner.p = slot@raw
                    writeDeep(x@move)
                    usep(slot)
                    y = 5
                func tupOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    writeT((slot@raw, w@raw@raw/u8))
                    usep(slot)
                    y = 5
                func viaQuietOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Plain.init(q: null)
                    x.p = slot@raw
                    writeVia(x@move)
                    y = 5
                    usep(slot)
                func consumeQuietOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var slot: raw/u8 = null
                    var x = Writer.init(q: null)
                    x.p = slot@raw
                    consume(x@move)
                    y = 5
                    usep(slot)
                """));

    // SPEC 15.6 Holders, 15.6.4, 5.2.1 (PLAN G582): a keeper held by value (no dereference: a Field or a Tuple element of the argument)
    // is that argument's own value, so a raw layer without an Origin that the call writes through it never keeps the keeper's Loan, and
    // `usep(slot)` is valid (keeperOk, keeperTupOk, as uBad's `usep(slot)`). That exclusion is 15.6.4's raw-layer sentence only: safe
    // storage that can hold a part or a borrow formed through the argument keeps its Loans, and a stated layer retains as its borrow
    // would (5.7 Calls, PLAN G339). A keeper reached past a dereference, in a borrowed argument's referent (keeperBorrowedBad, as the
    // drop forms of ADestructionRetainsAsItsDropsCall) or behind a borrow a by-value argument holds (keeperChainBad), is what a chain of
    // reads through the argument carries, so that raw layer keeps it, and slot's own storage end conflicts too (keeperBorrowedBad's
    // `var slot`, a true record: the layer a declaration states keeps what it admits, 5.7 Calls; D-757; keeperChainBad's the same).
    // Another argument's storage (keeperOtherBad) and a raw result (keeperResultBad, keeperTupResultBad) keep the by-value argument's
    // own value, the keeper's Loan with it (keeperOtherBad's `usep(other)` and `var other`: records added with the A5 landing on a
    // program rejected anyway, DECISIONS Q13).
    [Fact]
    public void AByValueKeeperIsItsArgumentsOwnValue()
        => Assert.Equal(
            [
                "keeperBorrowedBad: usep(slot)", "keeperBorrowedBad: var slot: raw/u8 = null", "keeperChainBad: usep(slot)",
                "keeperChainBad: var slot: raw/u8 = null", "keeperOtherBad: slot = null", "keeperOtherBad: usep(other)",
                "keeperOtherBad: var other: raw/u8 = null", "keeperResultBad: usep(slot)", "keeperTupResultBad: usep(slot)",
            ],
            Problems("""
                func usep<T>(p: raw/T) => ()
                struct K {s}
                    public var r: uniq/(raw/u8) during s
                    public init(r: uniq/(raw/u8) during s) => self.r = r
                func f(w: K during a) => ()
                func fTup(w: (uniq/(raw/u8) during a, i32)) => ()
                func fBorrowed(w: uniq/(K during a)) => ()
                func fChain(w: (uniq/(K during a) during b, i32)) => ()
                func fOther(w: K during a, t: uniq/(raw/u8)) => ()
                func fResult(w: K during a) -> raw/u8
                    return null
                func fTupResult(w: (uniq/(raw/u8) during a, i32)) -> raw/u8
                    return null
                func keeperOk()
                    var slot: raw/u8 = null
                    var w = K.init(r: slot@uniq)
                    f(w@move)
                    usep(slot)
                func keeperTupOk()
                    var slot: raw/u8 = null
                    fTup((slot@uniq, 1))
                    usep(slot)
                func keeperBorrowedBad()
                    var slot: raw/u8 = null
                    var w = K.init(r: slot@uniq)
                    fBorrowed(w@uniq)
                    usep(slot)
                func keeperChainBad()
                    var slot: raw/u8 = null
                    var kk = K.init(r: slot@uniq)
                    fChain((kk@uniq, 1))
                    usep(slot)
                func keeperOtherBad()
                    var other: raw/u8 = null
                    var slot: raw/u8 = null
                    var w = K.init(r: slot@uniq)
                    fOther(w@move, other@uniq)
                    slot = null
                    usep(other)
                func keeperResultBad()
                    var slot: raw/u8 = null
                    var w = K.init(r: slot@uniq)
                    let x = fResult(w@move)
                    usep(slot)
                    usep(x)
                func keeperTupResultBad()
                    var slot: raw/u8 = null
                    let x = fTupResult((slot@uniq, 1))
                    usep(slot)
                    usep(x)
                """));

    // SPEC 4.9.6, 15.6.4 (DECISIONS BU-52, BU-40): `append` and `insertOrReplace` write only their result and their receiver's element
    // storage, so the raw layers an added value holds by value (an enum's payload, a struct's Field, an Option's Type argument) are no
    // storage they write, and `slot`, which the added value addresses, keeps nothing of the call: appEnumOk, dictEnumOk, appStructOk and
    // optAppOk, which write w's address into `cell` through `slot` and use `cell` before `y = 5`, are valid (fail-before: each erred at
    // `y = 5`, the added value's untied record handing `slot` Φ_E; optAppOk on dev too). Their twins that use `cell` after `y = 5` err.
    [Fact]
    public void ATransferOperationWritesNoRawLayerOfAnAddedValue()
        => Assert.Equal(
            ["appEnumBad: y = 5", "dictEnumBad: y = 5", "appStructBad: y = 5"],
            Problems("""
                func usep<T>(p: raw/T) => ()
                func put(at: raw/(raw/u8), v: raw/u8)
                    unsafe => *at = v
                enum E3
                    A(raw/(raw/(raw/u8)))
                    B
                struct P3
                    public var p: raw/(raw/(raw/u8))
                    public init() => self.p = null
                func appEnumOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var cell: raw/u8 = null
                    var slot: raw/(raw/u8) = cell@raw
                    var a: Array<E3> = []
                    a.append(.A(slot@raw))
                    put(slot, w@raw@raw/u8)
                    usep(cell)
                    y = 5
                func appEnumBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var cell: raw/u8 = null
                    var slot: raw/(raw/u8) = cell@raw
                    var a: Array<E3> = []
                    a.append(.A(slot@raw))
                    put(slot, w@raw@raw/u8)
                    y = 5
                    usep(cell)
                func dictEnumOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var cell: raw/u8 = null
                    var slot: raw/(raw/u8) = cell@raw
                    var d: Dictionary<i32, E3> = [:]
                    _ = d.insertOrReplace(1, .A(slot@raw))
                    put(slot, w@raw@raw/u8)
                    usep(cell)
                    y = 5
                func dictEnumBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var cell: raw/u8 = null
                    var slot: raw/(raw/u8) = cell@raw
                    var d: Dictionary<i32, E3> = [:]
                    _ = d.insertOrReplace(1, .A(slot@raw))
                    put(slot, w@raw@raw/u8)
                    y = 5
                    usep(cell)
                func appStructOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var cell: raw/u8 = null
                    var slot: raw/(raw/u8) = cell@raw
                    var x = P3.init()
                    x.p = slot@raw
                    var a: Array<P3> = []
                    a.append(x@move)
                    put(slot, w@raw@raw/u8)
                    usep(cell)
                    y = 5
                func appStructBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var cell: raw/u8 = null
                    var slot: raw/(raw/u8) = cell@raw
                    var x = P3.init()
                    x.p = slot@raw
                    var a: Array<P3> = []
                    a.append(x@move)
                    put(slot, w@raw@raw/u8)
                    y = 5
                    usep(cell)
                func optAppOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var cell: raw/u8 = null
                    var slot: raw/(raw/u8) = cell@raw
                    var a: Array<Option<raw/(raw/(raw/u8))>> = []
                    a.append(.Some(slot@raw))
                    put(slot, w@raw@raw/u8)
                    usep(cell)
                    y = 5
                """));

    // SPEC 15.6 (the owner's 「(B)」 of 2026-10-30, PLAN G264 S1; DECISIONS BU-67): storage the body does not own follows the whole
    // holder, so a value an untied constructor made (Plain.init's, Writer.init's or Bx.init's result) reaches it through every raw Field,
    // also after the caller rewrote one. A call that writes through such a value stores there every chain its row retains, y's Loan with
    // it: x's own (viaOk, consumeDrop2Ok, arrConsumeOk; viaRefOk through a borrow) or input 1's (via2Ok; deepQOk two Fields deep, enumBOk
    // in an enum's payload, optOk in an Option, boxPLateOk in a box's Field, arrPlainOk in an Array). genPLateOk, the box passed to
    // `writeG<T>(x: Bx<T>, q: raw/u8)`, is a valid program, rejected again since c3-5 as on fcd6a5381 (the limit genUntiedBad's row names): `x.item` is storage of writeG's own T, Plain's raw Field lies in T's binding, which adds
    // no reach of its own beyond the address it carries, and q is no T; slot, the holder that Field addresses, may take q (route 1, the
    // owner's O-312 (a)) but is used only before `y = 5` (SPEC 15.6.4; the owner's O-360 (a)); its purpose is
    // boxPLateOk's, whose callee `writeBP(x: Bx<Plain>, ..)` writes the Type. Each other is a correct error at `y = 5`
    // though `slot` is used before it; arrConsumeOk and arrPlainOk also by `append`'s untied receiver store (5.7, 15.6.4). The function
    // names keep their earlier suffix. The old analysis read no retention and accepted the program; since the MIR decides acceptance
    // (R6-check H5, DECISIONS R5-M185) check rejects it with these eleven records, each at its function's `y = 5` (H3b's 1r).
    [Fact]
    public void ARewrittenRawFieldLeavesItsValueReachingStorageTheBodyDoesNotOwn()
    {
        const string Source = WriterShapes + ByValueShapes + """
            func usep<T>(p: raw/T) => ()
            func viaOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Plain.init(q: w@raw@raw/u8)
                x.p = slot@raw
                writeVia(x@move)
                usep(slot)
                y = 5
            func viaRefOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Plain.init(q: w@raw@raw/u8)
                x.p = slot@raw
                writeViaRef(x@uniq)
                usep(slot)
                y = 5
            func consumeDrop2Ok()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                consume(x@move)
                usep(slot)
                y = 5
            func arrConsumeOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Writer.init(q: w@raw@raw/u8)
                x.p = slot@raw
                var a: Array<Writer> = []
                a.append(x@move)
                consume(a@move)
                usep(slot)
                y = 5
            func via2Ok()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Plain.init(q: null)
                x.p = slot@raw
                writeVia2(x@move, w@raw@raw/u8)
                usep(slot)
                y = 5
            func deepQOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Outer.init(inner: Plain.init(q: null))
                x.inner.p = slot@raw
                writeDeepQ(x@move, w@raw@raw/u8)
                usep(slot)
                y = 5
            func enumBOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Plain.init(q: null)
                x.p = slot@raw
                let e: E2 = .B(x@move)
                writeE(e@move, w@raw@raw/u8)
                usep(slot)
                y = 5
            func optOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Plain.init(q: null)
                x.p = slot@raw
                writeO(.Some(x@move), w@raw@raw/u8)
                usep(slot)
                y = 5
            func genPLateOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var b = Bx.init(item: Plain.init(q: null))
                b.item.p = slot@raw
                writeG(b@move, w@raw@raw/u8)
                usep(slot)
                y = 5
            func boxPLateOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var b = Bx.init(item: Plain.init(q: null))
                b.item.p = slot@raw
                writeBP(b@move, w@raw@raw/u8)
                usep(slot)
                y = 5
            func arrPlainOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = Plain.init(q: null)
                x.p = slot@raw
                var a: Array<Plain> = []
                a.append(x@move)
                writeA(a@move, w@raw@raw/u8)
                usep(slot)
                y = 5
            public func main() => ()

            """;
        var c = MirBuildTest.Compile(Source);
        Assert.Equal(
            [
                "viaOk: y = 5", "viaRefOk: y = 5", "consumeDrop2Ok: y = 5", "arrConsumeOk: y = 5", "via2Ok: y = 5", "deepQOk: y = 5", "enumBOk: y = 5",
                "optOk: y = 5", "genPLateOk: y = 5", "boxPLateOk: y = 5", "arrPlainOk: y = 5",
            ],
            Problems(c, Analyze(c), LoanCodes));
        var analyzed = MinimalEmissionTest.Analyze(Source);
        Assert.False(analyzed.Accepts);
        Assert.Equal(string.Join(" | ", Enumerable.Repeat("ComparisonLoanConflict_Kd y = 5", 11)), MinimalEmissionTest.Problems(analyzed));
    }

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
    // store what a read through h carries, v's Loan, where h's untied pointer reaches); a pointer formed through a local that holds no
    // Loan stores y's Loan in that local, and a reference read from it carries the Loan (punCall: z is used after y = 5); a raw
    // argument's pointee is read through it into the result (snap). A pointer that addresses
    // a Loan-free local, or carries an exclusive Loan on one, stores y's Loan in that local, an exposed holder, unused after (punBorrow,
    // punAddress: valid, the A5 value redesign's U6; R4-M120's untracked rule sent the store to E), and one that stores a value with
    // no Loan is valid.
    [Fact]
    public void AnUntiedPointerReachesStorageTheBodyDoesNotOwn()
        => Assert.Equal(
            [
                "hiddenInit: y = 5", "hiddenHolder: y = 5", "filled: y = 5", "filled: var v: i32 = 2", "punCall: y = 5", "snap: y = 5",
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

    // SPEC 15.6.4 ("A raw layer that an argument holds by value through no shared layer, a Tuple element, an Option payload, a Field or a
    // Type argument included, is retained as a raw pointer argument is"; D-933, F's G147 ruling 14): Raw.initialize stores a value whose
    // raw layer addresses w (y's Loan) in storage the body does not own, where it may be used after every point, so `y@follow = 5` errs
    // whether the layer is a Tuple element (tupleBad), a Field (fieldBad), a Case payload (payloadBad), a nested Field (nestedBad), a
    // generic struct's own Field (genericBad) or a Field of its Type argument (argumentBad); each null twin is valid (fail-before: dev
    // accepted fieldBad, payloadBad and nestedBad, whose raw layers were no raw source). Past an rc or arc payload, a shared layer (SPEC
    // 15.6), a raw layer is no raw source in a Field as in a Tuple element (rc/arc Field and Tuple twins, a Type argument held past an rc:
    // each errs through the handle's own retention, on both sides of D-933's C1). The same rule where the storage is the body's own and
    // read after the write (the Middle's RP17 on Q-586, N1u and X4): Raw.initialize into a local cell (localCellBad) and `replace` placing
    // the value in its target (replacedFieldBad, SPEC 15.7.3: the placed value's Loans are kept) err at `y@follow = 5`; their twins
    // without a read after it (localCellOk, replacedFieldOk) are valid (fail-before: dev 8784c091b accepted localCellBad and
    // replacedFieldBad).
    [Fact]
    public void AFieldOrPayloadRawLayerIsARawSource()
        => Assert.Equal(
            [
                "tupleBad: y@follow = 5", "fieldBad: y@follow = 5", "payloadBad: y@follow = 5", "nestedBad: y@follow = 5", "genericBad: y@follow = 5",
                "argumentBad: y@follow = 5", "rcFieldBad: y@follow = 5", "rcTupleBad: y@follow = 5", "arcFieldBad: y@follow = 5",
                "arcTupleBad: y@follow = 5", "argRcFieldBad: y@follow = 5", "argRcTupleBad: y@follow = 5", "localCellBad: y@follow = 5",
                "replacedFieldBad: y@follow = 5",
            ],
            Problems("""
                struct Fq
                    public var p: raw/(raw/u8)
                    public var q: raw/u8
                    public init(q: raw/u8)
                        self.p = null
                        self.q = q
                enum Eq
                    A(raw/u8)
                    B
                struct Inq
                    public var q: raw/u8
                    public init(q: raw/u8) => self.q = q
                struct Nq
                    public var inner: Inq
                    public init(q: raw/u8) => self.inner = Inq.init(q: q)
                struct Gq<T>
                    public var v: T
                    public var q: raw/u8
                    public init(v: T, q: raw/u8)
                        self.v = v@move
                        self.q = q
                struct G2q<T>
                    public var v: T
                    public init(v: T) => self.v = v@move
                func tupleBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/(raw/u8, i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(s, (w@raw@raw/u8, 1))
                    y@follow = 5
                func fieldBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Fq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Fq.init(q: w@raw@raw/u8))
                    y@follow = 5
                func fieldOk(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Fq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Fq.init(q: null))
                    y@follow = 5
                func payloadBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Eq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, .A(w@raw@raw/u8))
                    y@follow = 5
                func payloadOk(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Eq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, .A(null))
                    y@follow = 5
                func nestedBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Nq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Nq.init(q: w@raw@raw/u8))
                    y@follow = 5
                func nestedOk(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Nq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Nq.init(q: null))
                    y@follow = 5
                func genericBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Gq<i32> = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Gq<i32>.init(v: 1, q: w@raw@raw/u8))
                    y@follow = 5
                func genericOk(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Gq<i32> = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Gq<i32>.init(v: 1, q: null))
                    y@follow = 5
                func argumentBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/G2q<raw/u8> = Raw.allocate(1)
                    unsafe => Raw.initialize(s, G2q<raw/u8>.init(v: w@raw@raw/u8))
                    y@follow = 5
                struct Rq
                    public var f: rc/Inq
                    public init(f: rc/Inq) => self.f = f@move
                struct Aq
                    public var f: arc/Inq
                    public init(f: arc/Inq) => self.f = f@move
                struct Bx<T>
                    public var v: T
                    public init(v: T) => self.v = v@move
                struct Gr<T>
                    public var f: rc/Bx<T>
                    public init(f: rc/Bx<T>) => self.f = f@move
                struct Og
                    public var g: Gr<raw/u8>
                    public init(g: Gr<raw/u8>) => self.g = g@move
                func rcFieldBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Rq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Rq.init(f: Kimi.Intrinsics.makeRc(Inq.init(q: w@raw@raw/u8))))
                    y@follow = 5
                func rcTupleBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/(rc/Inq, i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(s, (Kimi.Intrinsics.makeRc(Inq.init(q: w@raw@raw/u8)), 1))
                    y@follow = 5
                func rcFieldOk(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Rq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Rq.init(f: Kimi.Intrinsics.makeRc(Inq.init(q: null))))
                    y@follow = 5
                func arcFieldBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Aq = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Aq.init(f: Kimi.Intrinsics.makeArc(Inq.init(q: w@raw@raw/u8))))
                    y@follow = 5
                func arcTupleBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/(arc/Inq, i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(s, (Kimi.Intrinsics.makeArc(Inq.init(q: w@raw@raw/u8)), 1))
                    y@follow = 5
                func argRcFieldBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/Og = Raw.allocate(1)
                    unsafe => Raw.initialize(s, Og.init(g: Gr<raw/u8>.init(f: Kimi.Intrinsics.makeRc(Bx<raw/u8>.init(v: w@raw@raw/u8)))))
                    y@follow = 5
                func argRcTupleBad(y: uniq/i32)
                    let w = y@follow@ref
                    let s: raw/(Gr<raw/u8>, i32) = Raw.allocate(1)
                    unsafe => Raw.initialize(s, (Gr<raw/u8>.init(f: Kimi.Intrinsics.makeRc(Bx<raw/u8>.init(v: w@raw@raw/u8))), 1))
                    y@follow = 5
                func usep<T>(p: raw/T) => ()
                func localCellBad(y: uniq/i32)
                    let w = y@follow@ref
                    var cell = Inq.init(q: null)
                    unsafe => Raw.initialize(cell@raw, Inq.init(q: w@raw@raw/u8))
                    y@follow = 5
                    inspect(cell@ref)
                func localCellOk(y: uniq/i32)
                    let w = y@follow@ref
                    var cell = Inq.init(q: null)
                    unsafe => Raw.initialize(cell@raw, Inq.init(q: w@raw@raw/u8))
                    y@follow = 5
                func replacedFieldBad(y: uniq/i32)
                    let w = y@follow@ref
                    var x = Inq.init(q: null)
                    let r = x@uniq
                    Kimi.Intrinsics.replace(r@follow@uniq, with: Inq.init(q: w@raw@raw/u8))
                    y@follow = 5
                    usep(x.q)
                func replacedFieldOk(y: uniq/i32)
                    let w = y@follow@ref
                    var x = Inq.init(q: null)
                    let r = x@uniq
                    Kimi.Intrinsics.replace(r@follow@uniq, with: Inq.init(q: w@raw@raw/u8))
                    usep(x.q)
                    y@follow = 5
                """));

    // SPEC 15.6 (a raw pointer that reaches no holder reaches only storage the body does not own, where what is stored may be used after
    // every point; condition 5; DECISIONS R4-M120's departure 1, B1): what that storage holds may refer to it, so a pointer read through a
    // parameter reaches it from any local holder that keeps it, and a call storing x's Loan through the holder keeps it there: a raw local,
    // a Tuple, and a Tuple beside a direct part (whose own Loan the call may store there too, SPEC 15.6.4's per-argument letter, and
    // with it the Loan on y two reads through the Tuple's borrow, `t@uniq` -> t -> w, which 15.6.4's chain clause keeps, so y's storage
    // end conflicts too). Valid: a value with no Loan of the body stored through the pointer read (noLoan). The twins with the pointer
    // formed to a local holder that is dead after the call (formedLocal, formedTuple, formedMixed) are invalid too (A5 value redesign
    // U3c): the written storage (c2, t.0) is a raw layer without an Origin, so the call may write its own pointer there and store x
    // through it in storage the body does not own (SPEC 15.6.4's composition, example twoLayers; 5.7 Calls: also as a Tuple element),
    // with what a chain of reads through the argument carries (the storage ends of v, w and y). Dev accepted them.
    [Fact]
    public void APointerReadFromStorageTheBodyDoesNotOwnReachesIt()
        => Assert.Equal(
            [
                "viaUniqLocal: x = 5", "viaUniqTuple: x = 5", "viaUniqMixed: x = 5", "viaUniqMixed: var w: ref/i32 = y@ref", "viaUniqMixed: var y: i32 = 2",
                "formedLocal: x = 5", "formedLocal: var v: i32 = 2", "formedTuple: x = 5", "formedTuple: var v: i32 = 2", "formedMixed: x = 5",
                "formedMixed: var w: ref/i32 = y@ref", "formedMixed: var v: i32 = 3", "formedMixed: var y: i32 = 2",
            ],
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

    // SPEC 15.6.4's transfer rule (DECISIONS R4-M131, BU-40): a Chain row is a read through the argument, never its own Loans, so a popped
    // or iterated raw item, an exchanged raw pointer's old contents and a user Iterator's item keep no Loan of their receiver or target,
    // while a swapped pointer keeps what a read through the other argument carries (`y`'s Loan through `w`), and an Array constructed from
    // a raw value, alone or in a Tuple, or from a pointer to a replaced one keeps that value and what a read through it carries
    // (`repeating`, the input construction adds, SPEC 4.9.6). Through two or more Derefs (D-935; SPEC 15.6.4's rule A, 15.6.3's parent
    // Loans): the receiver of `rr@follow.flush()` carries r's Loan and x's, and a read through r's Loan finds x's again in r, which is
    // only the own value given back, so Writer's raw slot keeps neither: rr2m, rr3m (three), rr2fm (a Field between), rawo (a raw pointer
    // over the reference), rrcm (a stored Reborrow) and sw2 (`swap`'s second target) are valid, as the one-Deref x8m is, and rawi (a raw
    // intermediate) and sh2m (a shared one, which writes nothing) stay valid. What the read at the last Deref carries stays: rr2tm keeps
    // y's Loan read through h.1's address of w (`y@follow = 5`), adXm and adX2m keep v's Loan, which x.r holds, though h.1 holds it too
    // (`y = 5`; adXm0 without h.1's), stXm keeps it after rr is replaced (the route is never read on the call's state) and mvXm beside
    // the move of c's prefix rr; and rrAi passes the outer reference itself, which passes no Deref, so r's value stays (`var x` and
    // `x@ref`). `var z` is the twins' own record on dev too; stXm's and mvXm's `var x` (a stored Reborrow used through one more Deref)
    // is the open issue D-935 names.
    [Fact]
    public void AChainRowIsAReadThroughTheArgument()
        => Assert.Equal(
            ["swapRawBad: y = 5", "swapRawOwnBad: y = 5", "repRawBad: y = 5", "repTupBad: y = 5", "repTupRepl: y = 5",
                "rr2tm: y@follow = 5", "rrAi: x@ref", "rrAi: var x = Writer.init(q: null)", "adXm: y = 5", "adXm: var z: i32 = 0",
                "adXm0: y = 5", "adXm0: var z: i32 = 0", "adX2m: y = 5", "adX2m: var z: i32 = 0", "stXm: y = 5",
                "stXm: var x = RW.init(r: v)", "stXm: var z: i32 = 0", "mvXm: rr", "mvXm: y = 5", "mvXm: var h = (x@uniq, v)",
                "mvXm: var x = RW.init(r: v)", "mvXm: var z: i32 = 0"],
            Problems("""
                func usep<T>(p: raw/T) => ()
                struct Writer
                    public var p: raw/(raw/u8)
                    public var q: raw/u8
                    public init(q: raw/u8)
                        self.p = null
                        self.q = q
                    public func flush(self: uniq/Self)
                        unsafe => *self.p = self.q
                    drop
                        unsafe => *self.p = self.q
                struct Shown
                    public var p: raw/(raw/u8)
                    public var q: raw/u8
                    public init(q: raw/u8)
                        self.p = null
                        self.q = q
                    public func peek(self: ref/Self)
                        unsafe => *self.p = self.q
                struct RW {a}
                    public var p: raw/(ref/i32 during a)
                    public var r: ref/i32 during a
                    public init(r: ref/i32 during a)
                        self.p = null
                        self.r = r
                    public func flush(self: uniq/Self)
                        unsafe => *self.p = self.r
                func flushWith<T>(a: uniq/RW, b: T)
                    a.flush()
                func deepFlush(a: uniq/(uniq/Writer during w))
                    unsafe => *a@follow@follow.p = a@follow@follow.q
                struct RawIter
                    Self is Iterator
                    associate Iterator.Item is raw/u8
                    var items: Array<raw/u8>
                    public init(items: Array<raw/u8>) => self.items = items@move
                    public func next(self: uniq/Self) -> Option<raw/u8> => self.items.pop()
                func popRaw()
                    var a: Array<raw/u8> = [null, null]
                    let x = a.pop()
                    a.append(null)
                    inspect(x)
                func exchangeRawOwnResultOk()
                    var y: i32 = 1
                    let r = y@ref
                    let p = r@follow@raw
                    var x: raw/i32 = null
                    let old = Kimi.Intrinsics.exchange(x@uniq, with: p)
                    x = null
                    y = 5
                    usep(old)
                func iterRaw()
                    let a: Array<raw/u8> = [null, null]
                    var it = a.iterate()
                    let first = it.next()
                    let second = it.next()
                    inspect(first)
                    inspect(second)
                func userIter()
                    var it = RawIter.init([null, null])
                    let first = it.next()
                    let second = it.next()
                    inspect(first)
                    inspect(second)
                func swapRawBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var x: raw/u8 = w@raw@raw/u8
                    var z: raw/u8 = null
                    Kimi.Intrinsics.swap(x@uniq, z@uniq)
                    y = 5
                    usep(z)
                func swapRawOwnBad()
                    var y: i32 = 1
                    let r = y@ref
                    var x: raw/i32 = r@follow@raw
                    var z: raw/i32 = null
                    Kimi.Intrinsics.swap(x@uniq, z@uniq)
                    y = 5
                    usep(z)
                func repRawBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let q = w@raw@raw/u8
                    let a = Array<raw/u8>.init(repeating: q, count: 2)
                    y = 5
                    inspect(a)
                func repRawOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let q = w@raw@raw/u8
                    let a = Array<raw/u8>.init(repeating: q, count: 2)
                    inspect(a)
                    y = 5
                func repTupBad()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let q = w@raw@raw/u8
                    let a = Array<(raw/u8, i32)>.init(repeating: (q, 1), count: 2)
                    y = 5
                    inspect(a)
                func repTupRepl()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var h: raw/u8 = w@raw@raw/u8
                    let a = Array<raw/(raw/u8)>.init(repeating: h@raw, count: 2)
                    h = null
                    y = 5
                    inspect(a)
                func x8m()
                    var x = Writer.init(q: null)
                    let r = x@uniq
                    r.flush()
                    inspect(x@ref)
                func rr2m()
                    var x = Writer.init(q: null)
                    var r = x@uniq
                    let rr = r@uniq
                    rr@follow.flush()
                    inspect(x@ref)
                func rr3m()
                    var x = Writer.init(q: null)
                    var r = x@uniq
                    var rr = r@uniq
                    let r3 = rr@uniq
                    r3@follow@follow.flush()
                    inspect(x@ref)
                func rr2fm()
                    var x = Writer.init(q: null)
                    var h = (x@uniq, 0)
                    let rr = h@uniq
                    rr@follow.0@follow.flush()
                    inspect(x@ref)
                func rr2tm(y: uniq/i32)
                    let w = y@follow@ref
                    var slot: raw/u8 = null
                    var x = Writer.init(q: null)
                    x.p = slot@raw
                    var h = (x@uniq, w@raw@raw/u8)
                    let rr = h@uniq
                    rr@follow.0@follow.flush()
                    y@follow = 5
                    usep(slot)
                    inspect(x@ref)
                func rrAi()
                    var x = Writer.init(q: null)
                    var r = x@uniq
                    deepFlush(r@uniq)
                    inspect(x@ref)
                func rawo()
                    var x = Writer.init(q: null)
                    var r = x@uniq
                    let pr = r@raw
                    unsafe => (*pr)@follow.flush()
                    inspect(x@ref)
                func rawi()
                    var x = Writer.init(q: null)
                    var p = x@raw
                    let rr = p@uniq
                    unsafe => (*rr@follow).flush()
                    inspect(x@ref)
                func sh2m()
                    var x = Shown.init(q: null)
                    var r = x@uniq
                    let rr = r@ref
                    rr@follow.peek()
                    inspect(x@ref)
                func sw1()
                    var x = Writer.init(q: null)
                    var z = Writer.init(q: null)
                    var r = z@uniq
                    Kimi.Intrinsics.swap(x@uniq, r@follow@uniq)
                    inspect(z@ref)
                    inspect(x@ref)
                func sw2()
                    var x = Writer.init(q: null)
                    var z = Writer.init(q: null)
                    var r = z@uniq
                    let rr = r@uniq
                    Kimi.Intrinsics.swap(x@uniq, rr@follow@follow@uniq)
                    inspect(z@ref)
                    inspect(x@ref)
                func rrcm()
                    var x = Writer.init(q: null)
                    var r = x@uniq
                    var rr = r@uniq
                    let c = rr@follow@uniq
                    c@follow.flush()
                    inspect(x@ref)
                func adXm()
                    var z: i32 = 0
                    var slot: ref/i32 = z@ref
                    var y: i32 = 1
                    let v = y@ref
                    var x = RW.init(r: v)
                    x.p = slot@raw
                    var h = (x@uniq, v)
                    h.0@follow.flush()
                    y = 5
                    inspect(slot@ref)
                func adXm0()
                    var z: i32 = 0
                    var slot: ref/i32 = z@ref
                    var y: i32 = 1
                    let v = y@ref
                    var x = RW.init(r: v)
                    x.p = slot@raw
                    var h = (x@uniq, 0)
                    h.0@follow.flush()
                    y = 5
                    inspect(slot@ref)
                func adX2m()
                    var z: i32 = 0
                    var slot: ref/i32 = z@ref
                    var y: i32 = 1
                    let v = y@ref
                    var x = RW.init(r: v)
                    x.p = slot@raw
                    var h = (x@uniq, v)
                    let rr = h@uniq
                    rr@follow.0@follow.flush()
                    y = 5
                    inspect(slot@ref)
                func stXm()
                    var z: i32 = 0
                    var slot: ref/i32 = z@ref
                    var y: i32 = 1
                    let v = y@ref
                    var q: i32 = 3
                    let u = q@ref
                    var x = RW.init(r: v)
                    x.p = slot@raw
                    var x2 = RW.init(r: u)
                    var h = (x@uniq, v)
                    var h2 = (x2@uniq, u)
                    var rr = h@uniq
                    let c = rr@follow.0@follow@uniq
                    rr = h2@uniq
                    c@follow.flush()
                    y = 5
                    inspect(slot@ref)
                func mvXm()
                    var z: i32 = 0
                    var slot: ref/i32 = z@ref
                    var y: i32 = 1
                    let v = y@ref
                    var q: i32 = 3
                    let u = q@ref
                    var x = RW.init(r: v)
                    x.p = slot@raw
                    var x2 = RW.init(r: u)
                    var h = (x@uniq, v)
                    var rr = h@uniq
                    let c = rr@follow.0@follow@uniq
                    flushWith(c, rr@move)
                    y = 5
                    inspect(slot@ref)
                """));

    // DECISIONS R4-M116 PQ-10 (B′ §4.3; registered for X-2), narrowed by Z5-R1 and Z5-R2: a Copy read of a Tuple or fixed-array Place
    // enclosing a `during self` layer inline (also inside a nested Tuple) is read element by element and carries its owner's own-step
    // entry (enclosingCopy, enclosingOnly, arrayCopy, nestedCopy); a Borrow of a layer's Place or
    // of an enclosing one issues its Loan, and a read through it is placed by its exact lent position (layerRef, enclosingRef; arrayRef
    // reads none), carrying its parents whole (SPEC 15.6 Holders): layerRef's read carries y's Loan, which b holds, into the generic
    // call, so y's end conflicts as for the same read through no dereference or through the holder's borrow (the base's CLC on those
    // twins; the Middle's ruling on Z5-R1, 11-22 13:0x); a Borrow of the holder, a read of the layer through it, a read of an element and
    // of a scalar beside the layer are precise.
    [Fact]
    public void ACopyOrBorrowOfADuringSelfLayersEnclosingPlaceIsPrecise()
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + """
            struct Arrs
                internal var arr: [2 of raw/u8 during self]
                internal var n: i32
                public init()
                    self.arr = [null, null]
                    self.n = 0
            struct Nest
                internal var nest: ((raw/u8 during self, i32), i32)
                public init()
                    self.nest = ((null, 1), 2)
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
            func enclosingOnly()
                let s = Pairs.init()
                let t = s.pair
            func arrayCopy()
                let a = Arrs.init()
                let t = a.arr
            func arrayRef()
                let a = Arrs.init()
                let r = a.arr@ref
            func arrayElement() -> bool
                let a = Arrs.init()
                let p = a.arr[1]
                return p == null
            func arrayScalar() -> i32
                let a = Arrs.init()
                return a.n
            func nestedCopy()
                let s = Nest.init()
                let t = s.nest
            func nestedScalar() -> i32
                let s = Nest.init()
                return s.nest.1
            """);
        Assert.Equal(
            "layerRef: var y: i32 = 1",
            string.Join(" | ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd])));
    }

    // Z5-R2 (SPEC 5.7 Reading and Writing a Field: an enclosing Tuple or fixed-array Place is read element by element, a Move reads the
    // layer's element; 15.6 Holders; 5.2.1 (3)): a Copy or Move read of the Place through no dereference carries its owner's own-step
    // entry with the holder's Loans, and the local it fills carries it whole, so the owning value's or an ancestor's Move, exclusive Borrow
    // or replacing write while the value is used is the Warning (w1, ra5-ra8, ra14, rr5, rr6), a write under the holder's Loan conflicts
    // (ra2f, ra3f), and a Move of the enclosing Place departs nothing (ra1, rr7); no `self` in a Type argument, no entry (rr9). A
    // reference stored beside the owner in the same root keeps its exact lender (rr1-rr4, rv3, rv5, ex1: the own-step entry exposes its
    // owner's storage only). Labels from SPEC (Z5-R2 design, Revision 1 R1.4).
    [Theory]
    [InlineData("ra1", "var b = Packet.init()|let t = b.pair|let m = b.pair@move|b.pair = (null, 2)|usep(t.0)|usep(m.0)", "")]
    [InlineData("ra2vf", "var y: i32 = 1|let b = Hp.init(y@ref)|let t = b.pair|usep(t.0)|y = 2", "")]
    [InlineData("ra13", "let b = Packet.init()|let n = b.pair.1|let c = b@move|usen(n)", "")]
    [InlineData("rr5v", "let a = Arr.init()|let t = a.ta|usep(t[1].0)|let c = a@move", "")]
    [InlineData("rr7", "var b = Packet.init()|let p = b.data|let m = b.pair@move|b.pair = (null, 2)|usep(p)|usep(m.0)", "")]
    [InlineData("rr9", "let w = W.init((fresh, 1))|let t = w.v|let c = w@move|usep(t.0)", "")]
    [InlineData("w1", "let b = Packet.init()|let t = b.pair|let c = b@move|usep(t.0)", "b OwnStepDeparture_Kd")]
    [InlineData("ra5", "let b = Packet.init()|let t = b.arr|let c = b@move|usep(t[0])", "b OwnStepDeparture_Kd")]
    [InlineData("ra6", "var b = Packet.init()|let t = b.pair|let w = b@uniq|usep(t.0)", "b@uniq OwnStepDeparture_Kd")]
    [InlineData("ra7", "var b = Packet.init()|let t = b.pair|b = Packet.init()|usep(t.0)", "b = Packet.init() OwnStepDeparture_Kd")]
    [InlineData("ra8", "let o = Outer.init()|let t = o.inner.pair|let c = o@move|usep(t.0)", "o OwnStepDeparture_Kd")]
    [InlineData("ra14", "var b = Packet.init()|let m = b.nest.0@move|b.nest.0 = (null, 1)|let c = b@move|usep(m.0)", "b OwnStepDeparture_Kd")]
    [InlineData("rr5", "let a = Arr.init()|let t = a.ta|let c = a@move|usep(t[1].0)", "a OwnStepDeparture_Kd")]
    [InlineData("rr6", "let a = Arr.init()|let i: isize = 1|let e = a.ta[i]|let c = a@move|usep(e.0)", "a OwnStepDeparture_Kd")]
    [InlineData("ra2f", "var y: i32 = 1|let b = Hp.init(y@ref)|let t = b.pair|y = 2|usep(t.0)", "y = 2")]
    [InlineData("ra3f", "var y: i32 = 1|let b = Hp.init(y@ref)|let m = b.pair@move|y = 2|usep(m.0)", "y = 2")]
    [InlineData("rr5i", "var a = Arr.init()|let r = a.ta[0]@ref|let m = a.ta@move|a.ta = [(null, 3), (null, 4)]|usen(r@follow.1)", "a.ta|a.ta = [(null, 3), (null, 4)]")]
    [InlineData("rr7i", "var b = Packet.init()|let r = b.pair.0@ref|let m = b.pair@move|b.pair = (null, 2)|usep(r@follow)", "b.pair|b.pair = (null, 2)")]
    [InlineData("rr1", "let d = Packet.init()|let tt = (Packet.init(), d.pair@ref)|let n = tt.0.pair.1|let x = tt.1@follow.0|usep(x)|usen(n)", "")]
    [InlineData("rr2", "let d = Packet.init()|let tt = (Packet.init(), d.pair@ref)|let q = tt.0.pair@ref|let x = tt.1@follow.0|usep(x)|usen(q@follow.1)", "")]
    [InlineData("rv3", "let d = Packet.init()|let tt = (Outer.init(), d.pair@ref)|let n = tt.0.inner.pair.1|let x = tt.1@follow.0|usep(x)|usen(n)", "")]
    [InlineData("rr3", "let d = Packet.init()|let tt = (Packet.init(), d.pair@raw)|let n = tt.0.pair.1|unsafe|    let x = (*tt.1).0|    let c = d@move|    usep(x)|usen(n)", "d OwnStepDeparture_Kd")]
    [InlineData("rr4", "let d = Packet.init()|let tt = (Packet.init(), d.pair@raw)|let t = tt.0.pair|unsafe|    let x = (*tt.1).0|    let c = d@move|    usep(x)|usep(t.0)", "d OwnStepDeparture_Kd")]
    [InlineData("rv5", "let d = Packet.init()|let tt = (Packet.init(), d.pair@ref)|let n = tt.0.pair.1|let c = d@move|usep(tt.1@follow.0)|usen(n)", "d")]
    [InlineData("ex1", "let d = Packet.init()|let tt = (Packet.init(), d@ref)|let t = tt.0.pair|let x = tt.1@follow.pair.0|let c = d@move|usep(x)|usep(t.0)", "d|d OwnStepDeparture_Kd")]
    public void AnEnclosingPlacesCopyOrMoveCarriesItsOwnersEntry(string id, string lines, string expected)
        => Assert.Equal(OwnStepExpected(id, expected), OwnStepProblems(id, lines));

    // Z5-R2's B1 (SPEC 5.7's Origin table: a `during self` pointer addresses storage the value holding the layer owns; 15.6 Addresses):
    // an own-step entry exposes its owner's storage, not its whole root, so a reference stored beside the owner in the same root keeps
    // its exact lender, whatever reads the owner's layer (an enclosing Copy, ex2; the layer's own read, q12, ex2p, q12w): q12's dependent
    // Warning and q12w's Warning on a valid program (5.2.1 (3), Z5-F's t4c) are recovered, ex2 and ex2p leave the Unplaced limit. A plain
    // address exposes the whole root (rv1: `tt.1@raw` may address the reference's own position), which stays at the limit.
    [Theory]
    [InlineData("q12", "let d = Packet.init()|let tt = (Packet.init(), d@ref)|let p = tt.0.pair.0|let x = tt.1@follow.pair.0|let m = d@move|usep(x)|usep(p)", "d|d OwnStepDeparture_Kd")]
    [InlineData("q12w", "let d = Packet.init()|let tt = (Packet.init(), d@raw)|let p = tt.0.pair.0|unsafe|    let x = (*tt.1).pair.0|    let c = d@move|    usep(x)|usep(p)", "d OwnStepDeparture_Kd")]
    [InlineData("ex2", "let d = Packet.init()|let tt = (Packet.init(), d.pair@ref)|let t = tt.0.pair|let x = tt.1@follow.0|usep(x)|usep(t.0)", "")]
    [InlineData("ex2p", "let d = Packet.init()|let tt = (Packet.init(), d.pair@ref)|let p = tt.0.pair.0|let x = tt.1@follow.0|usep(x)|usep(p)", "")]
    [InlineData("rv1", "let d = Packet.init()|let tt = (Packet.init(), d.pair@ref)|let p = tt.1@raw|let x = tt.1@follow.0|usep(x)", "tt.1@follow.0 Unsupported_Kd")]
    public void AnOwnStepEntryKeepsTheLenderOfAReferenceBesideIt(string id, string lines, string expected)
        => Assert.Equal(OwnStepExpected(id, expected), OwnStepProblems(id, lines));

    // Z5-R1 (SPEC 15.6 Holders, 15.6 Addresses, 5.7 Reading a Field through a dereference, 5.2.1 (3); PQ-10 narrowed): a read of a
    // `during self` layer or an enclosing Place through a dereference carries its owner's own-step entry at its exact lent position
    // (MirBorrows.Lent) with every Loan its parents carry, so the owner's Move under it conflicts (and departs the owner's own step where the
    // position names the owner from its root); where the read's own steps name the owner past the dereference, the base's row (g1, g2, g5,
    // ix2: the owner the lender's Place or past it). G1276 (E-a, Revision 1c): an owner step in a lender's steps applies to the Place the
    // steps before it name from the position's root where the body has that Place's row, direct (oh1, oh2: a reference to an owning
    // handle's payload; oh3, oha: that payload's Deref past a whole-handle lender, oha's an address, which carries no Loan), else to its
    // Prefix row past the lender's Deref (Lent's LentPlace), so the read carries its parents whole through a raw lender (pr1, pr2, pr3:
    // two lender segments; pre: an enclosing Tuple; prw: a write; pru: a uniq lender) and a shared one (n2, n2v leave the limit). Where
    // no exact position exists and the own steps name no owner, in a view whose borrow the base marked, the read's located
    // Unsupported_Kd (tw3: a reference stored twice); elsewhere silent (G1301's open classes: prs, a pointer stored twice, and prc2, a
    // call's result, invalid or undecided by the text and accepted; bn1, bn2, valid reads through a call's result, accepted; bn3
    // conflicts by 15.6.4). The Warning of a non-direct entry is PLAN G1277's (N10; pr1: not given); a plain address is a direct entry
    // and gives it (pra). Labels from SPEC (Z5-R1 design section 5; G1276 design section 6 and Revision 1c section 3, before any run;
    // pra's Warning corrected after it, as the design's section 7 measured it).
    [Theory]
    [InlineData("t5c", "let t = (b.pair@ref, d.pair@ref)|let x = t.1@follow.0|let m = b@move|usep(x)", "", "b")]
    [InlineData("mx", "let t = (b.data@ref, d.pair@ref)|let x = t.1@follow.0|let m = b@move|usep(x)", "", "b")]
    [InlineData("mxi", "let t = (b.data@ref, d.pair@ref)|let x = t.1@follow.0|let m = d@move|usep(x)", "", "d|d OwnStepDeparture_Kd")]
    [InlineData("h1", "let t = (b@ref, d@ref)|let x = t.1@follow.pair.0|let m = b@move|usep(x)", "", "b")]
    [InlineData("h1v", "let t = (b@ref, d@ref)|let x = t.1@follow.pair.0|usep(x)|let m = b@move", "", "")]
    [InlineData("n1", "let o = Outer.init()|let r = o.inner.pair@ref|let x = r@follow.0|let m = o@move|usep(x)", "", "o|o OwnStepDeparture_Kd")]
    [InlineData("n1v", "let o = Outer.init()|let r = o.inner.pair@ref|let x = r@follow.0|usep(x)|let m = o@move", "", "")]
    [InlineData("ix1", "var i: isize = 1|let r = b.arr@ref|let x = r@follow[i]|let m = b@move|usep(x)", "", "b|b OwnStepDeparture_Kd")]
    [InlineData("ix1v", "var i: isize = 1|let r = b.arr@ref|let x = r@follow[i]|usep(x)|let m = b@move", "", "")]
    [InlineData("s2c", "let r = b.pair@ref|let r2 = r|let c = r2@follow|let m = b@move|usep(c.0)", "", "b|b OwnStepDeparture_Kd")]
    [InlineData("g1", "let o = Outer.init()|let r = o.inner@ref|let x = r@follow.data|let m = o@move|usep(x)", "", "o")]
    [InlineData("g1v", "let o = Outer.init()|let r = o.inner@ref|let x = r@follow.data|usep(x)|let m = o@move", "", "")]
    [InlineData("g2", "let o = Outer.init()|let r = o@ref|let x = r@follow.inner.pair.0|let m = o@move|usep(x)", "", "o")]
    [InlineData("g5", "let u = Duo.init()|let r = u.duo@ref|let x = r@follow.0.pair.0|let m = u@move|usep(x)", "", "u")]
    [InlineData("ix2", "var i: isize = 1|let a = Arr2.init()|let r = a.ps[i]@ref|let x = r@follow.pair.0|let m = a@move|usep(x)", "", "a")]
    [InlineData("t9", "let o = Outer.init()|let t = (b@ref, o.inner@ref)|let x = t.1@follow.data|let m = b@move|usep(x)", "", "b")]
    [InlineData("cg1", "let x = gt(b.pair@ref)|usep(x)", "func gt(r: ref/(raw/u8, i32)) -> raw/u8 => r@follow.0\n", "")]
    [InlineData("cg1m", "let x = gt(b.pair@ref)|let m = b@move|usep(x)", "func gt(r: ref/(raw/u8, i32)) -> raw/u8 => r@follow.0\n", "b")]
    [InlineData("n2", "let o = Outer.init()|let r = o@ref|let q = r@follow.inner.pair@ref|let x = q@follow.0|let m = o@move|usep(x)", "", "o")]
    [InlineData("n2v", "let o = Outer.init()|let r = o@ref|let q = r@follow.inner.pair@ref|let x = q@follow.0|usep(x)|let m = o@move", "", "")]
    [InlineData("pr1", "let o = Outer.init()|let r = o@ref|let p = r@follow.inner.pair@raw|unsafe|    let x = (*p).0|    let m = o@move|    usep(x)", "", "o")]
    [InlineData("pr1v", "let o = Outer.init()|let r = o@ref|let p = r@follow.inner.pair@raw|unsafe|    let x = (*p).0|    usep(x)|    let m = o@move", "", "")]
    [InlineData("pr2", "let o = Outer.init()|let r = o@ref|let p = r@follow.inner.pair@raw|let p2 = p|unsafe|    let x = (*p2).0|    let m = o@move|    usep(x)", "", "o")]
    [InlineData("prt", "let o = Outer.init()|let r = o@ref|let p = r@follow.inner@raw|unsafe|    let x = (*p).pair.0|    let m = o@move|    usep(x)", "", "o")]
    [InlineData("prtv", "let o = Outer.init()|let r = o@ref|let p = r@follow.inner@raw|unsafe|    let x = (*p).pair.0|    usep(x)|    let m = o@move", "", "")]
    [InlineData("pr3", "let o = Outer.init()|let r = o@ref|let q = r@follow.inner@ref|let p = q@follow.pair@raw|unsafe|    let x = (*p).0|    let m = o@move|    usep(x)", "", "o")]
    [InlineData("pr3v", "let o = Outer.init()|let r = o@ref|let q = r@follow.inner@ref|let p = q@follow.pair@raw|unsafe|    let x = (*p).0|    usep(x)|    let m = o@move", "", "")]
    [InlineData("pre", "let o = Outer.init()|let r = o@ref|let p = r@follow.inner.pair@raw|unsafe|    let t = *p|    let m = o@move|    usep(t.0)", "", "o")]
    [InlineData("prw", "var o = Outer.init()|let r = o@ref|let p = r@follow.inner.pair@raw|unsafe|    let x = (*p).0|    o = Outer.init()|    usep(x)", "", "o = Outer.init()")]
    [InlineData("pru", "var o = Outer.init()|let r = o@uniq|let p = r@follow.inner.pair@raw|unsafe|    let x = (*p).0|    let m = o@move|    usep(x)", "", "o")]
    [InlineData("pra", "let o = Outer.init()|let p = o.inner.pair@raw|unsafe|    let x = (*p).0|    let m = o@move|    usep(x)", "", "o OwnStepDeparture_Kd")]
    [InlineData("oh1", "let h = Packet.init()@obj|let r = h@follow@ref|let x = r@follow.pair.0|let m = h@move|usep(x)", "", "h|h OwnStepDeparture_Kd")]
    [InlineData("oh1v", "let h = Packet.init()@obj|let r = h@follow@ref|let x = r@follow.pair.0|usep(x)|let m = h@move", "", "")]
    [InlineData("oh2", "var h = Packet.init()@obj|let r = h@follow@ref|let x = r@follow.pair.0|let w = h@uniq|usep(x)", "", "h@uniq|h@uniq OwnStepDeparture_Kd")]
    [InlineData("oh2v", "var h = Packet.init()@obj|let r = h@follow@ref|let x = r@follow.pair.0|usep(x)|let w = h@uniq", "", "")]
    [InlineData("oh3", "var h = Packet.init()@obj|let rr = h@ref|let r = rr@follow@follow@ref|let x = r@follow.pair.0|let w = h@uniq|usep(x)", "", "h@uniq|h@uniq OwnStepDeparture_Kd")]
    [InlineData("oh3v", "var h = Packet.init()@obj|let rr = h@ref|let r = rr@follow@follow@ref|let x = r@follow.pair.0|usep(x)|let w = h@uniq", "", "")]
    [InlineData("oha", "var h = Packet.init()@obj|let rp = h@raw|unsafe|    let p = (*rp)@follow@raw|    let x = (*p).pair.0|    let w = h@uniq|    usep(x)", "", "h@uniq OwnStepDeparture_Kd")]
    [InlineData("ohav", "var h = Packet.init()@obj|let rp = h@raw|unsafe|    let p = (*rp)@follow@raw|    let x = (*p).pair.0|    usep(x)|    let w = h@uniq", "", "")]
    [InlineData("prs", "let o = Outer.init()|let o2 = Outer.init()|let r = o@ref|let s = o2@ref|var p = s@follow.inner.pair@raw|p = r@follow.inner.pair@raw|unsafe|    let x = (*p).0|    let m = o@move|    usep(x)", "", "")]
    [InlineData("prc2", "let o = Outer.init()|let p = gp(o@ref)|unsafe|    let x = (*p).0|    let m = o@move|    usep(x)", "func gp(r: ref/Outer) -> raw/(raw/u8 during r, i32) => r@follow.inner.pair@raw\n", "")]
    [InlineData("prc2v", "let o = Outer.init()|let p = gp(o@ref)|unsafe|    let x = (*p).0|    usep(x)|    let m = o@move", "func gp(r: ref/Outer) -> raw/(raw/u8 during r, i32) => r@follow.inner.pair@raw\n", "")]
    [InlineData("prp", "gq(fresh@raw/(raw/u8, i32))", "func gq(p: raw/(raw/u8, i32))\n    unsafe\n        let x = (*p).0\n        usep(x)\n", "")]
    [InlineData("bn1", "var a = (1, 2)|let q = pickN(a@ref)|let t = q@follow|a = (3, 4)|usen(t.0)", "func usen(n: i32) => ()\nfunc pickN(r: ref/(i32, i32)) -> ref/(i32, i32) during r => r\n", "")]
    [InlineData("bn2", "var a = (fresh, 1)|let q = pickR(a@ref)|let x = q@follow.0|a = (fresh, 2)|usep(x)", "func pickR(r: ref/(raw/u8, i32)) -> ref/(raw/u8, i32) during r => r\n", "")]
    [InlineData("bn3", "var a = (1, 2)|let q = pickN(a@ref)|let t = q@follow|a = (3, 4)|usen(t.0)|usen(q@follow.0)", "func usen(n: i32) => ()\nfunc pickN(r: ref/(i32, i32)) -> ref/(i32, i32) during r => r\n", "a = (3, 4)")]
    [InlineData("tw3", "var r = b.pair@ref|r = d.pair@ref|let x = r@follow.0|usep(x)", "", "r@follow.0 Unsupported_Kd")]
    public void ALayerReadThroughADereferenceCarriesItsOwnersEntryAndItsParents(string id, string lines, string declarations, string expected)
    {
        var c = MirBuildTest.Compile(LentShapes + declarations + $"func {id}(fresh: raw/u8)\n    let b = Packet.init()\n    let d = Packet.init()\n" +
            string.Concat(lines.Split('|').Select(static x => "    " + x + "\n")));
        Assert.Equal(
            expected.Length == 0 ? string.Empty : string.Join(" | ", expected.Split('|').Select(x => $"{id}: {x}").Order(StringComparer.Ordinal)),
            string.Join(" | ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnStepDeparture_Kd]).Order(StringComparer.Ordinal)));
    }

    // V16 (SPEC 15.6 Addresses; DECISIONS R4-M116): the address pre-pass walks a view's uses only where a raw Ref was issued or a projection
    // row may be or enclose a `during self` layer, so a body with neither takes no step per location beyond its Loans' scan: 32 more
    // statements cost at least 32 steps fewer than beside a raw Ref, whose view the walk visits.
    [Fact]
    public void AViewWithNothingToAddressIsNotWalked()
    {
        const string Plain = "func shape()\n    var x = 0\n«N:    x = {0}\n»";
        const string Addressed = "func shape()\n    var x = 0\n    let p = x@raw\n«N:    x = {0}\n»    _ = p == null\n";
        long[] plain = [.. new[] { 32, 64 }.Select(n => Measured(Prelude + Expand(Plain, n, 0)).Work)];
        long[] addressed = [.. new[] { 32, 64 }.Select(n => Measured(Prelude + Expand(Addressed, n, 0)).Work)];
        output.WriteLine($"N 32, 64: plain {string.Join(", ", plain)}, addressed {string.Join(", ", addressed)}");
        Assert.True(addressed[1] - addressed[0] - (plain[1] - plain[0]) >= 32, $"plain {string.Join(", ", plain)}, addressed {string.Join(", ", addressed)}");
    }

    // V16 (PLAN G307; DECISIONS R4-M157): the address pre-pass's Type walks visit a Type once per walk, each visit a step, so over BU's
    // family D_i<T> { x: D_{i+1}<(T, T)> }, whose last part is a Tuple DAG of 2^(n-1) positions, A5's work grows linearly in n: Layering's
    // raw test over its raw-free Tuples, and at a read of the part Encloses, which a layer below the written region never steps Own for
    // (DECISIONS BU-58).
    [Theory]
    [InlineData("i32")]
    [InlineData("raw/i32 during static")]
    public void TheAddressWalksVisitATypeDagOncePerWalk(string leaf)
    {
        long[] work = [.. new[] { 8, 12, 16 }.Select(n => Measured(Prelude + TypeDag(leaf, n)).Work)];
        output.WriteLine($"N 8, 12, 16: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 2), $"work {string.Join(", ", work)}");
    }

    // V16 (PLAN G667a): an address names its Place (SPEC 5.2.1 condition 1), so the N part addresses of one root h that one holder t
    // holds are N entries, which each of the N stores through t's elements reads: Work and bytes grow as N², within the class's quadratic
    // bound (5; DECISIONS R4-M159). Work and bytes at N 16, 32, 64 with their growth factors: dev (4562e91f0, one root address) 124,823,
    // 128,842, 136,877 (2.00) and 4,444 flat; G667a 126,068, 133,647, 155,714 (2.91) and 5,044, 6,676, 13,012 (3.88). Its verdict twin
    // is ARawPointerCarriesTheHoldersItAddresses's partAddresses.
    [Fact]
    public void PartAddressesInOneHolderCostAsTheirEntries()
    {
        var (sizes, work, bytes) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            (work[i], bytes[i]) = Measured(Prelude + PartAddresses(sizes[i]));
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, 5), $"bytes {string.Join(", ", bytes)}");
    }

    // SPEC 15.6.4's chain clause (DECISIONS R4-M131 Q4 (a), BU-40, R4-M138): a Chain row carries what any chain of reads through its
    // argument carries, so a pointee the call may retain two or more reads deep keeps its Loan even when a holder on the way is replaced
    // after the call: a result two raw layers below the argument (also through a reference), a raw slot written from them, the written
    // argument read through itself, a lent result three raw layers deep, a popped, exchanged or swapped pointer whose pointee is replaced
    // after the call (SPEC 15.6.4's transfer rule keeps the chain), and a chain through E, the storage the body does not own, where an
    // earlier store left a holder's address (Φ_E read at a later step). Valid twins: the others with the result's use before `y = 5`,
    // and viaEOk without the store. Four such twins (getRRROk, fRRROk, unlinkROk, lend3Ok) are invalid too (A5 value redesign U3c): the
    // written storage (h2, h1, h3) is a raw layer without an Origin, so the call may write its own pointer there and store through it, in storage the body does not
    // own, what a chain of reads through the argument carries (SPEC 15.6.4: "may retain every argument with what any chain of reads
    // through it carries"; example twoLayers), which is used after every point, whatever the result's use; lend3's other argument
    // (z@ref) is kept there as well, so z's storage end conflicts. Dev accepted them.
    [Fact]
    public void AChainRowCarriesWhatAnyChainOfReadsThroughItsArgumentCarries()
        => Assert.Equal(
            ["getRRRRepl: y = 5", "getRRROk: y = 5", "getRFRRepl: y = 5", "fRRRRepl: y = 5", "fRRROk: y = 5", "unlinkRBad: y = 5",
                "unlinkROk: y = 5", "lend3Repl: y = 5", "lend3Repl: var z: i32 = 0", "lend3Ok: y = 5", "lend3Ok: var z: i32 = 0",
                "popLateBad: y = 5", "popLateBadX: y = 5", "exchangeTwoBadX: y = 5", "swapTwoBad: y = 5", "viaEBad: y = 5"],
            Problems(RawShapes + ChainShapes + ChainForms));

    // SPEC 23.1.2: the chain's error stands beside an unrelated error.
    [Fact]
    public void AChainRowsErrorStandsBesideAnUnrelatedError()
        => Assert.Equal(["getRRRRepl: y = 5"], RejectedProblems(RawShapes + ChainShapes + ChainRepl + "    let unrelated: bool = 3\n"));

    // PLAN G255 (C3; DECISIONS R4-M143): popLateOk is SPEC-valid (SPEC 4.9.6; the pointee is replaced and the popped pointer used before
    // `y = 5`), as check accepts it: its literal is one aggregate (DECISIONS R5-M129), which writes no receiver store and leaves
    // no Φ_E in its elements, so pop's Chain row reads no E; built by appends, each receiver store, a referent store, reaches refs(S) only.
    [Fact]
    public void APopOverElementsAnUntiedStoreReachedIsValid()
    {
        const string PopLateOk = "func popLateOk()\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n    var h: raw/u8 = null\n" +
            "    var a: Array<raw/(raw/u8)> = [h@raw]\n    h = w@raw@raw/u8\n    let x = a.pop()\n    inspect(a)\n    h = null\n    inspect(x)\n" +
            "    y = 5\n";
        Assert.Empty(Problems(RawShapes + PopLateOk));
        Assert.Empty(MinimalEmissionTest.Analyze("func inspect<T>(value: ref/T) => ()\n" + PopLateOk + "public func main() => ()\n").Problems);
    }

    // SPEC 15.6.4, 5.7, 5.2 (PLAN G415; DECISIONS BU-91): raw storage holds what is written there, so a borrow `at` forms within the
    // storage a receiver holds through a raw layer, punned to another Type than its pointee's (`during self` or no Origin), keeps the
    // receiver's Loan as the unpunned twin's (IntBuf) does: each err at the free while the result lives (punRefUse also at `b@uniq`;
    // punStoreUse through a stored local, punHolderAssign in the holder's own Tuple). Fails on dev a788e14d5: the pun forms are valid.
    [Fact]
    public void ABorrowFormedInPunnedRawStorageKeepsItsInput()
    {
        const string Source = """
            struct IntBuf
                internal var p: raw/i32
                public init()
                    unsafe
                        self.p = Raw.allocate<i32>(1)
                        Raw.initialize(self.p, 7)
                public func at(self: ref/Self) -> ref/i32
                    unsafe => return (*self.p)@ref
                public func free(self: uniq/Self)
                    unsafe => Raw.release(self.p)
                    self.p = null
            struct PunBuf
                internal var buffer: raw/u8 during self
                public init()
                    unsafe
                        self.buffer = Raw.allocate<u8>(4)@(raw/u8 during self)
                        Raw.initialize(self.buffer@raw/i32, 7)
                public func at(self: ref/Self) -> ref/i32
                    unsafe => return (*(self.buffer@raw/i32))@ref
                public func free(self: uniq/Self)
                    unsafe => Raw.release(self.buffer)
                    self.buffer = null
            struct PunNo
                internal var buffer: raw/u8
                public init()
                    unsafe
                        self.buffer = Raw.allocate<u8>(4)
                        Raw.initialize(self.buffer@raw/i32, 7)
                public func at(self: ref/Self) -> ref/i32
                    unsafe => return (*(self.buffer@raw/i32))@ref
                public func free(self: uniq/Self)
                    unsafe => Raw.release(self.buffer)
                    self.buffer = null
            func intUse()
                var b = IntBuf.init()
                let r = b.at()
                b.free()
                inspect(r)
            func intOk()
                var b = IntBuf.init()
                let r = b.at()
                inspect(r)
                b.free()
            func punUse()
                var b = PunBuf.init()
                let r = b.at()
                b.free()
                inspect(r)
            func punOk()
                var b = PunBuf.init()
                let r = b.at()
                inspect(r)
                b.free()
            func punRefUse()
                var b = PunBuf.init()
                let rb = b@ref
                let r = rb.at()
                let ub = b@uniq
                ub.free()
                inspect(r)
            func punStoreUse()
                var zero = 0
                var keep: ref/i32 = zero@ref
                var b = PunBuf.init()
                keep = b.at()
                b.free()
                inspect(keep)
            func punHolderAssign()
                var zero = 0
                var h: (PunBuf, ref/i32) = (PunBuf.init(), zero@ref)
                h.1 = h.0.at()
                h.0.free()
                inspect(h.1)
            func punNoUse()
                var b = PunNo.init()
                let r = b.at()
                b.free()
                inspect(r)
            func punNoOk()
                var b = PunNo.init()
                let r = b.at()
                inspect(r)
                b.free()

            """;
        Assert.Equal(
            ["intUse: b.free() CallActivationConflict_Kd", "punUse: b.free() CallActivationConflict_Kd", "punRefUse: b@uniq",
                "punRefUse: ub.free() CallActivationConflict_Kd", "punStoreUse: b.free() CallActivationConflict_Kd",
                "punHolderAssign: h.0.free() CallActivationConflict_Kd", "punNoUse: b.free() CallActivationConflict_Kd"],
            Problems(Source));
    }

    // The same rule at a position (PLAN G415's review B1; DECISIONS BU-91): `make`'s result holds `during self` storage that `x` reaches
    // by its Origin, so it keeps `big`'s Loan: posUse errs at `big = 3` while `h` lives, posOk writes after its last use. Fails on dev
    // a788e14d5: posUse valid.
    [Fact]
    public void AResultHoldingRawStorageKeepsAnInputOfAnyType()
        => Assert.Equal(
            ["posUse: big = 3"],
            Problems("""
                struct HolderA {a}
                    public var r: ref/i32 during a
                    public var buffer: raw/u8 during self
                    public init(r: ref/i32 during a)
                        self.r = r
                        unsafe => self.buffer = Raw.allocate<u8>(8)@(raw/u8 during self)
                func make(x: ref/i64 during a, y: ref/i32 during a) -> HolderA during a
                    return HolderA.init(y)
                func posUse()
                    var one = 1
                    var big: i64 = 2
                    let h = make(big@ref, one@ref)
                    big = 3
                    inspect(h.r)
                func posOk()
                    var one = 1
                    var big: i64 = 2
                    let h = make(big@ref, one@ref)
                    inspect(h.r)
                    big = 3

                """));

    // SPEC 4.8.4, 5.6, 15.6.4 (PLAN G780; C-189): a range selection, a written Raw.slice and Raw.release store nothing through their
    // storage, so the holder array `values` keeps `cell`'s address alone and `keep` stores `spare` (r's Loan) in `cell`: valid while
    // `cell` is dead after `kk = 2` (range, written, released); Raw.initialize stores through its storage, here a pointer from an integer
    // (SPEC 5.5), and a read of `cell` after `kk = 2` keeps the Loan live (the Bad twins). The narrowing follows the declaration, not its
    // signature: a user function of Raw.slice's shape may store through its storage (userBad, 15.6.4's public contract). `released`
    // releases a stack element's address and `initializedBad` initializes an Initialized Place: both break 5.6's run-time preconditions
    // and only probe the analysis. A reslice's range selection stores nothing through its backing either, its Slice tied to that backing
    // (DECISIONS R4-M63's amendment): subRange `sl[0..1]`, nested `values[..][1..]`, whole `sl[..]` and ending `sl[..1]` are valid while
    // `values` is dead after `kk = 2`; nestedReadAfter reads it after (ARangeSelectionsSliceReachesWhatItsBackingReaches holds the other
    // twins). Fails on dev 63878b639: range, written and released conflict (Raw.slice's storage took rule A's untied store); the
    // reslices on dev 3dc197857.
    [Fact]
    public void ASliceOrReleaseStoresNothingThroughItsStorage()
        => Assert.Equal(
            ["rangeBad: kk = 2", "writtenBad: kk = 2", "userBad: kk = 2", "initializedBad: kk = 2", "nestedReadAfter: kk = 2"],
            Problems("""
                func keep(slot: raw/(raw/u8), value: raw/u8) => ()
                func mine(storage: raw/(raw/(raw/u8)), length: isize) => ()
                func range()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var cell: raw/u8 = null
                    let values: [2 of raw/(raw/u8)] = [cell@raw, cell@raw]
                    let sl = values[..]
                    keep(values[0], spare)
                    kk = 2
                func rangeBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var cell: raw/u8 = null
                    let values: [2 of raw/(raw/u8)] = [cell@raw, cell@raw]
                    let sl = values[..]
                    keep(values[0], spare)
                    kk = 2
                    let z = cell
                func written()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var cell: raw/u8 = null
                    let values: [2 of raw/(raw/u8)] = [cell@raw, cell@raw]
                    unsafe
                        let w = Raw.slice(values[0]@raw, 2)
                    keep(values[0], spare)
                    kk = 2
                func writtenBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var cell: raw/u8 = null
                    let values: [2 of raw/(raw/u8)] = [cell@raw, cell@raw]
                    unsafe
                        let w = Raw.slice(values[0]@raw, 2)
                    keep(values[0], spare)
                    kk = 2
                    let z = cell
                func userBad()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var cell: raw/u8 = null
                    var values: [2 of raw/(raw/u8)] = [cell@raw, cell@raw]
                    mine(values[0]@raw, 2)
                    keep(values[0], spare)
                    kk = 2
                func released()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var cell: raw/u8 = null
                    let values: [2 of raw/(raw/u8)] = [cell@raw, cell@raw]
                    unsafe
                        Raw.release(values[1]@raw)
                    keep(values[0], spare)
                    kk = 2
                func initializedBad(n: usize)
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    var cell: raw/u8 = null
                    var values: [2 of raw/(raw/u8)] = [cell@raw, cell@raw]
                    let q = n@raw/(raw/u8)
                    unsafe
                        Raw.initialize(values[0]@raw, q)
                    keep(values[0], spare)
                    kk = 2
                func subRange()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    let values: [2 of raw/u8] = [null, null]
                    let sl = values[..]
                    let part = sl[0..1]
                    let p = part[0]@raw
                    keep(p, spare)
                    kk = 2
                func nested()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    let values: [2 of raw/u8] = [null, null]
                    let part = values[..][1..]
                    let p = part[0]@raw
                    keep(p, spare)
                    kk = 2
                func whole()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    let values: [2 of raw/u8] = [null, null]
                    let sl = values[..]
                    let part = sl[..]
                    let p = part[0]@raw
                    keep(p, spare)
                    kk = 2
                func ending()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    let values: [2 of raw/u8] = [null, null]
                    let sl = values[..]
                    let part = sl[..1]
                    let p = part[0]@raw
                    keep(p, spare)
                    kk = 2
                func nestedReadAfter()
                    var kk: u8 = 1
                    let r = kk@uniq
                    var spare: raw/u8 = r@follow@raw
                    let values: [2 of raw/u8] = [null, null]
                    let part = values[..][1..]
                    let p = part[0]@raw
                    keep(p, spare)
                    kk = 2
                    let z = part[0]

                """));

    // Content rows stay one read (SPEC 15.6.4's premises; R4-M138 keys the closure on the row's kind only): an Array iterator's `next`
    // (its premise result row), a Dictionary `remove`'s key borrow and a Utf8Writer `write<T>` (a store untied only by `static`) keep what
    // one read carries, valid where the borrowed values are not used after `y = 5`; the twins use them after it. A Content result through
    // two Derefs (D-935; SPEC 15.6.3's `takeFirst`, 15.6.4): Cell's `get` keeps what a read through its receiver carries (x.v's Loan on
    // y), not the receiver's parent Loans r's read gives back, so rr2c is valid as rr1c is, and rr2ci errs at `y = 2` alone; the Loan
    // the read at the last Deref carries stays where the intermediate holds it too (adGm, adG2m; adGm0 without h.1's) and after the
    // route is replaced (stGm).
    [Fact]
    public void AContentRowStaysOneRead()
        => Assert.Equal(
            ["iterLate: y = 5", "emitLate: y = 5", "rr2ci: y = 2", "adGm: y = 2", "adGm0: y = 2", "adG2m: y = 2", "stGm: y = 2"],
            Problems(RawShapes + """
                struct Cell {a}
                    public var v: ref/i32 during a
                    public init(v: ref/i32 during a) => self.v = v
                    public func get(self: uniq/Self) -> ref/i32 during a
                        return self.v
                func iterOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let a: Array<ref/i32> = [w]
                    var it = a.iterate()
                    let first = it.next()
                    inspect(first)
                    y = 5
                func iterLate()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    let a: Array<ref/i32> = [w]
                    var it = a.iterate()
                    let first = it.next()
                    y = 5
                    inspect(first)
                func dictKeyOk()
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    var d: Dictionary<i32, ref/i32> = [:]
                    var k: i32 = 1
                    _ = d.remove(k)
                    k = 2
                    inspect(d)
                    y = 5
                func emitOk(writer: uniq/Utf8Writer)
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    _ = writer.write(w@follow)
                    inspect(w)
                    y = 5
                func emitLate(writer: uniq/Utf8Writer)
                    var y: i32 = 1
                    var w: ref/i32 = y@ref
                    _ = writer.write(w@follow)
                    y = 5
                    inspect(w)
                func rr1c()
                    let y: i32 = 1
                    var x = Cell.init(v: y@ref)
                    var r = x@uniq
                    let g = r@follow.get()
                    inspect(x@ref)
                    inspect(g)
                func rr2c()
                    let y: i32 = 1
                    var x = Cell.init(v: y@ref)
                    var r = x@uniq
                    let rr = r@uniq
                    let g = rr@follow.get()
                    inspect(x@ref)
                    inspect(g)
                func rr2ci()
                    var y: i32 = 1
                    var x = Cell.init(v: y@ref)
                    var r = x@uniq
                    let rr = r@uniq
                    let g = rr@follow.get()
                    inspect(x@ref)
                    y = 2
                    inspect(g)
                func adGm()
                    var y: i32 = 1
                    let v = y@ref
                    var c = Cell.init(v: v)
                    var h = (c@uniq, v)
                    let g = h.0@follow.get()
                    y = 2
                    inspect(g@ref)
                func adGm0()
                    var y: i32 = 1
                    let v = y@ref
                    var c = Cell.init(v: v)
                    var h = (c@uniq, 0)
                    let g = h.0@follow.get()
                    y = 2
                    inspect(g@ref)
                func adG2m()
                    var y: i32 = 1
                    let v = y@ref
                    var c = Cell.init(v: v)
                    var h = (c@uniq, v)
                    let rr = h@uniq
                    let g = rr@follow.0@follow.get()
                    y = 2
                    inspect(g@ref)
                func stGm()
                    var y: i32 = 1
                    let v = y@ref
                    var q: i32 = 3
                    let u = q@ref
                    var c1 = Cell.init(v: v)
                    var c2 = Cell.init(v: u)
                    var h = (c1@uniq, v)
                    var h2 = (c2@uniq, u)
                    var rr = h@uniq
                    let c = rr@follow.0@follow@uniq
                    rr = h2@uniq
                    let g = c@follow.get()
                    y = 2
                    inspect(g@ref)
                """));

    // V16 (R4-M138): a closure queues each holder once and joins what it read in one n-ary union (MirLoanSets.Union), so the Work its Chain
    // rows add beside the same body without them is linear in what they read: N calls each reading a pointer chain of depth K, a K-cycle
    // of raw holders read once, N vacuous Chain rows (`Raw.allocate`'s count) after E holds M addresses of Loan holders, and a holder
    // fanning out to M addresses, and N sparse rows (the N2 landing review 2's B1: each joins {Φ_E} and one of N Loans, so its least and
    // greatest elements span the body's numbering). A linear measure's growth doubles per doubling (2.00 to 2.04 measured); the bound 2.1
    // leaves its margin for the marker words the union sorts (a log factor's 2.14 to 2.25 for balanced unions; a union reading its range
    // 2.69 and 3.03 on the sparse rows; a quadratic's 4).
    [Fact]
    public void AChainsWorkIsWithinWhatItReads()
    {
        const string Read = "    _ = touch(h)\n";
        long[] calls = [.. new[] { 16, 32, 64 }.Select(n => this.Added(PointerChain(8, n, "w"), PointerChain(8, 0, "w")))];
        long[] depth = [.. new[] { 16, 32, 64 }.Select(k => this.Added(PointerChain(k, 8, "w"), PointerChain(k, 0, "w")))];
        long[] cycle = [.. new[] { 16, 32, 64 }.Select(k => this.Added(PointerChain(k, 1, "h0"), PointerChain(k, 0, "h0")))];
        long[] vacuous = [.. new[] { 16, 32, 64 }.Select(n => this.Added(Escaping(8, n), Escaping(8, 0)))];
        long[] escaped = [.. new[] { 16, 32, 64 }.Select(m => this.Added(Escaping(m, 8), Escaping(m, 0)))];
        long[] fan = [.. new[] { 16, 32, 64 }.Select(m => this.Added(FanOut(m, Read), FanOut(m, string.Empty)))];
        long[] sparse = [.. new[] { 64, 128, 256 }.Select(n => this.Added(Sparse(n, calls: true), Sparse(n, calls: false)))];
        output.WriteLine($"calls {string.Join(", ", calls)}; depth {string.Join(", ", depth)}; cycle {string.Join(", ", cycle)}; " +
            $"vacuous {string.Join(", ", vacuous)}; escaped {string.Join(", ", escaped)}; fan {string.Join(", ", fan)}; sparse {string.Join(", ", sparse)}");
        Assert.All(new[] { calls, depth, cycle, vacuous, escaped, fan, sparse }, static x => Assert.True(Grows(x, 2.1), string.Join(", ", x)));
    }

    // The Work a shape's body adds beside its twin's, both over the raw and chain shapes.
    private long Added(string shape, string twin)
        => Measured(Prelude + RawShapes + ChainShapes + shape).Work - Measured(Prelude + RawShapes + ChainShapes + twin).Work;

    // The chain forms' bodies (sources in the R4 notes' N2 review), each Bad form's valid twin beside it.
    private const string ChainForms = ChainRepl + """
        func getRRROk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            let r = getRRR(h2@raw)
            h = null
            usep(r)
            y = 5
        func getRFRRepl()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            let r = getRFR(h2@ref)
            h = null
            y = 5
            usep(r)
        func getRFROk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            let r = getRFR(h2@ref)
            h = null
            usep(r)
            y = 5
        func fRRRRepl()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            var s: raw/u8 = null
            fRRR(h2@raw, s@raw)
            h = null
            y = 5
            usep(s)
        func fRRROk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            var s: raw/u8 = null
            fRRR(h2@raw, s@raw)
            h = null
            usep(s)
            y = 5
        func unlinkRBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h2: raw/u8 = w@raw@raw/u8
            var h1: raw/(raw/u8) = h2@raw
            unlinkR(h1@raw)
            h2 = null
            y = 5
            let used = h1 == null
        func unlinkROk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h2: raw/u8 = w@raw@raw/u8
            var h1: raw/(raw/u8) = h2@raw
            unlinkR(h1@raw)
            h2 = null
            let used = h1 == null
            y = 5
        func lend3Repl()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            var h3: raw/(raw/(raw/u8)) = h2@raw
            var z: i32 = 0
            var other: raw/u8 = null
            let r = lend3(h3@raw, z@ref)
            h2 = other@raw
            y = 5
            inspect(r)
        func lend3Ok()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            var h3: raw/(raw/(raw/u8)) = h2@raw
            var z: i32 = 0
            var other: raw/u8 = null
            let r = lend3(h3@raw, z@ref)
            h2 = other@raw
            inspect(r)
            y = 5
        func popLateBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = null
            var a: Array<raw/(raw/u8)> = [h@raw]
            h = w@raw@raw/u8
            let x = a.pop()
            h = null
            y = 5
            inspect(x)
        func popLateBadX()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = null
            var a: Array<raw/(raw/u8)> = [h@raw]
            h = w@raw@raw/u8
            let x = a.pop()
            inspect(a)
            h = null
            y = 5
            inspect(x)
        func exchangeTwoBadX()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var x: raw/(raw/u8) = h@raw
            let old = Kimi.Intrinsics.exchange(x@uniq, with: null)
            let seen = x == null
            h = null
            y = 5
            let used = old == null
        func exchangeTwoOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var x: raw/(raw/u8) = h@raw
            let old = Kimi.Intrinsics.exchange(x@uniq, with: null)
            let seen = x == null
            h = null
            let used = old == null
            y = 5
        func swapTwoBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var x: raw/(raw/u8) = h@raw
            var z: raw/(raw/u8) = null
            Kimi.Intrinsics.swap(x@uniq, z@uniq)
            h = null
            y = 5
            let used = z == null
        func swapTwoOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var x: raw/(raw/u8) = h@raw
            var z: raw/(raw/u8) = null
            Kimi.Intrinsics.swap(x@uniq, z@uniq)
            h = null
            let used = z == null
            y = 5
        func viaEBad(sink: raw/(raw/u8))
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            unsafe => *sink = h@raw@raw/u8
            var s = sink
            let r = getRRR(s@raw)
            h = null
            y = 5
            let used = r == null
        func viaEOk(sink: raw/(raw/u8))
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var s = sink
            let r = getRRR(s@raw)
            h = null
            y = 5
            let used = r == null
        """;

    // getRRRRepl: a result two raw layers below the argument, the middle holder replaced after the call (its body ends a statement list).
    private const string ChainRepl = """
        func getRRRRepl()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var h: raw/u8 = w@raw@raw/u8
            var h2: raw/(raw/u8) = h@raw
            let r = getRRR(h2@raw)
            h = null
            y = 5
            usep(r)

        """;

    // N calls of `touch` over a pointer chain of K raw holders h0 -> ... -> h(K-1) -> last (w, which holds a Loan, or h0, a cycle), each
    // call's result used.
    private static string PointerChain(int depth, int calls, string last)
    {
        var text = new StringBuilder("func shape()\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n");
        for (var k = 0; k < depth; k++)
        {
            text.Append($"    var h{k}: raw/u8 = null\n");
        }

        for (var k = 0; k + 1 < depth; k++)
        {
            text.Append($"    h{k} = h{k + 1}@raw@raw/u8\n");
        }

        text.Append($"    h{depth - 1} = {last}@raw@raw/u8\n");
        for (var n = 0; n < calls; n++)
        {
            text.Append($"    _ = touch(h0)\n");
        }

        return text.ToString();
    }

    // M addresses of Loan holders stored where the body does not own them (E), then N vacuous Chain rows (`Raw.allocate`'s `isize` count).
    private static string Escaping(int holders, int calls)
    {
        var text = new StringBuilder("func shape(sink: raw/(raw/u8))\n    var y: i32 = 1\n");
        for (var m = 0; m < holders; m++)
        {
            text.Append($"    var w{m}: ref/i32 = y@ref\n    unsafe => *sink = w{m}@raw@raw/u8\n");
        }

        for (var n = 0; n < calls; n++)
        {
            text.Append($"    let p{n}: raw/u8 = Raw.allocate(1)\n    let used{n} = p{n} == null\n");
        }

        return text.ToString();
    }

    // N Loan holders, each read into a raw holder that otherwise keeps a pointer read from E, then (calls) a call reading each, whose Chain
    // row joins {Φ_E}, element 0, with that holder's Loan, numbered past every holder before it (the N2 landing review 2's probe).
    private static string Sparse(int holders, bool calls)
    {
        var text = new StringBuilder("func shape(sink: raw/(raw/u8), c: i32)\n    var y: i32 = 1\n    var g: raw/u8 = null\n    unsafe => g = *sink\n");
        for (var i = 0; i < holders; i++)
        {
            text.Append($"    var w{i}: ref/i32 = y@ref\n    var k{i}: raw/u8 = g\n    if c == {i} => k{i} = w{i}@raw@raw/u8\n");
            text.Append(calls ? $"    _ = touch(k{i})\n" : string.Empty);
        }

        return text.ToString();
    }

    // A holder that may hold any of M addresses (one per branch) of holders that copy one reference, so the closure's own fan-out is
    // measured (M distinct Loans in the result would cost the checks after it as much under a one-read row), then the tail (a call reading
    // it, or none).
    private static string FanOut(int addresses, string tail)
    {
        var text = new StringBuilder("func shape(c: i32)\n    var y: i32 = 1\n    let first: ref/i32 = y@ref\n    var h: raw/u8 = null\n");
        for (var m = 0; m < addresses; m++)
        {
            text.Append($"    var w{m}: ref/i32 = first\n    if c == {m} => h = w{m}@raw@raw/u8\n");
        }

        return text.Append(tail).ToString();
    }

    // BU's family of a size over a leaf, with a read of D_n's part and of its first leaf.
    private static string TypeDag(string leaf, int size)
    {
        var text = new StringBuilder();
        for (var i = 1; i < size; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct D{i}<T>\n    public var x: D{i + 1}<(T, T)>\n");
        }

        var (path, first) = (string.Concat(Enumerable.Repeat(".x", size - 1)), string.Concat(Enumerable.Repeat(".0", size - 1)));
        return text.Append(System.Globalization.CultureInfo.InvariantCulture, $"struct D{size}<T>\n    public var x: T\n")
            .Append(System.Globalization.CultureInfo.InvariantCulture, $"func rd(d: ref/D1<{leaf}>) -> {leaf}\n")
            .Append(System.Globalization.CultureInfo.InvariantCulture, $"    let v = d{path}.x\n    return v{first}\n").ToString();
    }

    // The addresses of N parts of one fixed array h (constant indices: N Places), stored into one holder t, then a store through each of
    // a pointer carrying kk's Loan (which makes the view a Loan-stratum producer, D5).
    private static string PartAddresses(int n)
        => Expand(
            $"func partAddresses()\n    var kk: u8 = 1\n    let r = kk@uniq\n    var spare: raw/u8 = r@follow@raw\n    var h: [{n} of raw/u8] = [null«K:, null»]\n" +
                $"    var t: [{n} of raw/(raw/u8)] = [null«K:, null»]\n«N:    t[{{0}}] = h[{{0}}]@raw\n»«N:    unsafe => *(t[{{0}}]) = spare\n»",
            n,
            n - 1);

    // The rows' records, "id: subject" with the code where it is no Loan conflict, in order.
    private static string OwnStepExpected(string id, string expected)
        => expected.Length == 0 ? string.Empty : string.Join(" | ", expected.Split('|').Select(x => $"{id}: {x}").Order(StringComparer.Ordinal));

    // A5's Loan conflicts, located Unsupported_Kd and own-step Warnings of a function `id` over LentShapes and EnclosingShapes, in order.
    private static string OwnStepProblems(string id, string lines)
    {
        var c = MirBuildTest.Compile(LentShapes + EnclosingShapes + $"func {id}(fresh: raw/u8)\n" + string.Concat(lines.Split('|').Select(static x => "    " + x + "\n")));
        return string.Join(" | ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnStepDeparture_Kd]).Order(StringComparer.Ordinal));
    }
}
