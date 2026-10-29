// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
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
    // (`w@s` under `s is raw`, SPEC 8.14) addresses w's holder as `w@raw` does; the dead twins are valid.
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
                """));

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

    // SPEC 15.6.4 (PLAN G266; DECISIONS BU-52): a call writes through the raw layers without an Origin a by-value argument holds, in a
    // struct's Field (viaBad, via2Bad), an enum's payload (optBad), a Type argument's Field (boxPlainBad), an Array's element
    // (arrPlainBad) or behind a `uniq` Field (uBad), and a consuming callee's cleanup does through its drop (consumeWBad, consumeDrop2Bad,
    // arrConsumeBad), so `slot` may keep w's address and with it y's Loan: each errs at `y = 5` as its borrowed twin does (fail-before:
    // accepted). viaUntiedBad leaves `x.p` as `Plain.init` made it, which by that contract may address storage the body does not own, so
    // the write may keep y's Loan there (R4-M116 S2's hiddenInit), however `slot` is used; so do three forms that use it before `y = 5`
    // by a constructor's contract: genUntiedBad and boxUntiedBad box x after `x.p = slot@raw`, and `Bx.init`'s result is untied, so the
    // box's `item.p` may address such storage and writeG or writeBP may store w's address there; deepUntiedBad hands `Plain.init(q: w..)`
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

    // The interim of DECISIONS BU-52 (the owner's 「暫定として認める (推奨)」, then 「類として認める」): each form here uses `slot` before
    // `y = 5` and is valid by the parts its raw layers address, yet errs on the MIR, as dev's viaRefOk (the borrowed twin) does. The class
    // is A5's whole-value Φ_E at calls (DECISIONS R4-M120, departure 1): where a call's row writes through an argument whose value carries
    // Φ_E (here an untied constructor's result, Plain.init's, Writer.init's or Bx.init's, whose raw Field the caller rewrote afterwards),
    // W reaches E at its first step, and E receives every input the row retains, so it keeps every chain the whole value carries, y's
    // Loan with it: x's own (viaOk, consumeDrop2Ok, arrConsumeOk) or input 1's (via2Ok; deepQOk two Fields deep, enumBOk in an enum's
    // payload, optOk in an Option, genPLateOk in a box's Field, arrPlainOk in an Array). Each flips back to valid when part-level raw reach
    // lands (PLAN G303, G264 U2), except that arrConsumeOk and arrPlainOk have a second Φ_E source: `append`'s receiver store is untied
    // (rule A at the element storage it writes), so they also wait for that row's reading (PLAN G264's arrAppendOk item, R4's U2), by
    // which they flip back or are correct errors. The default pipeline's analysis reports nothing: its old analysis reads no retention,
    // and the MIR analyses' problems are unpublished until H3 and decide acceptance only from H5 (DECISIONS R5-M152).
    [Fact]
    public void AWholeValueEscapeKeepsByValueTwinsErringInTheInterim()
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
                "optOk: y = 5", "genPLateOk: y = 5", "arrPlainOk: y = 5",
            ],
            Problems(c, Analyze(c), LoanCodes));
        Assert.Empty(MinimalEmissionTest.Analyze(Source).Problems);
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
    // a Tuple, and a Tuple beside a direct part (whose own Loan the call may store there too, SPEC 15.6.4's per-argument letter, and
    // with it the Loan on y two reads through the Tuple's borrow, `t@uniq` -> t -> w, which 15.6.4's chain clause keeps, so y's storage
    // end conflicts too). Valid twins: the same holders with the pointer formed to a local holder that is dead after the call, and a
    // value with no Loan of the body stored through the pointer read.
    [Fact]
    public void APointerReadFromStorageTheBodyDoesNotOwnReachesIt()
        => Assert.Equal(
            ["viaUniqLocal: x = 5", "viaUniqTuple: x = 5", "viaUniqMixed: x = 5", "viaUniqMixed: var w: ref/i32 = y@ref", "viaUniqMixed: var y: i32 = 2"],
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
    // (`repeating`, the input construction adds, SPEC 4.9.6).
    [Fact]
    public void AChainRowIsAReadThroughTheArgument()
        => Assert.Equal(
            ["swapRawBad: y = 5", "swapRawOwnBad: y = 5", "repRawBad: y = 5", "repTupBad: y = 5", "repTupRepl: y = 5"],
            Problems("""
                func usep<T>(p: raw/T) => ()
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
                """));

    // DECISIONS R4-M116 PQ-10 (B′ §4.3; registered for X-2): a Borrow of a `during self` layer's Place, or of a Tuple or fixed-array Place
    // enclosing one inline (also inside a nested Tuple), and a Copy read of such a Place, are located Unsupported_Kd until the Loan records
    // the layer's own-step entry; a Borrow of the holder, a read of the layer through it, a read of an element and of a scalar beside the
    // layer are precise.
    [Fact]
    public void ABorrowOfADuringSelfLayerIsUnsupported()
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
            ["layerRef: b.data@ref Unsupported_Kd", "enclosingRef: s.pair@ref Unsupported_Kd", "enclosingCopy: s.pair Unsupported_Kd",
                "enclosingOnly: s.pair Unsupported_Kd", "arrayCopy: a.arr Unsupported_Kd", "arrayRef: a.arr@ref Unsupported_Kd",
                "nestedCopy: s.nest Unsupported_Kd"],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // V16 (SPEC 15.6 Addresses; DECISIONS R4-M116): the address pre-pass walks a view's uses only where a raw Ref was issued or a projection
    // row may be or enclose a `during self` layer, so a body with neither takes no step per location beyond its Loans' scan: 32 more
    // statements cost at least 32 steps fewer than beside a raw Ref, whose view the walk visits.
    [Fact]
    public void AViewWithNothingToAddressIsNotWalked()
    {
        const string Plain = "func shape()\n    var x = 0\n«N:    x = {0}\n»";
        const string Addressed = "func shape()\n    var x = 0\n    let p = x@raw\n«N:    x = {0}\n»    _ = p == null\n";
        long[] plain = [.. new[] { 32, 64 }.Select(n => this.Measured(Prelude + Expand(Plain, n, 0)).Work)];
        long[] addressed = [.. new[] { 32, 64 }.Select(n => this.Measured(Prelude + Expand(Addressed, n, 0)).Work)];
        output.WriteLine($"N 32, 64: plain {string.Join(", ", plain)}, addressed {string.Join(", ", addressed)}");
        Assert.True(addressed[1] - addressed[0] - (plain[1] - plain[0]) >= 32, $"plain {string.Join(", ", plain)}, addressed {string.Join(", ", addressed)}");
    }

    // SPEC 15.6.4's chain clause (DECISIONS R4-M131 Q4 (a), BU-40, R4-M138): a Chain row carries what any chain of reads through its
    // argument carries, so a pointee the call may retain two or more reads deep keeps its Loan even when a holder on the way is replaced
    // after the call: a result two raw layers below the argument (also through a reference), a raw slot written from them, the written
    // argument read through itself, a lent result three raw layers deep, a popped, exchanged or swapped pointer whose pointee is replaced
    // after the call (SPEC 15.6.4's transfer rule keeps the chain), and a chain through E, the storage the body does not own, where an
    // earlier store left a holder's address (Φ_E read at a later step). Valid twins: each with the result's use before `y = 5`, and viaEOk
    // without the store.
    [Fact]
    public void AChainRowCarriesWhatAnyChainOfReadsThroughItsArgumentCarries()
        => Assert.Equal(
            ["getRRRRepl: y = 5", "getRFRRepl: y = 5", "fRRRRepl: y = 5", "unlinkRBad: y = 5", "lend3Repl: y = 5", "popLateBad: y = 5",
                "popLateBadX: y = 5", "exchangeTwoBadX: y = 5", "swapTwoBad: y = 5", "viaEBad: y = 5"],
            Problems(RawShapes + ChainShapes + ChainForms));

    // SPEC 23.1.2: the chain's error stands beside an unrelated error.
    [Fact]
    public void AChainRowsErrorStandsBesideAnUnrelatedError()
        => Assert.Equal(["getRRRRepl: y = 5"], RejectedProblems(RawShapes + ChainShapes + ChainRepl + "    let unrelated: bool = 3\n"));

    // PLAN G255 (C3; DECISIONS R4-M143): popLateOk is SPEC-valid (SPEC 4.9.6; the pointee is replaced and the popped pointer used before
    // `y = 5`), as the default pipeline accepts it: its literal is one aggregate (DECISIONS R5-M129), which writes no receiver store and leaves
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

    // Content rows stay one read (SPEC 15.6.4's premises; R4-M138 keys the closure on the row's kind only): an Array iterator's `next`
    // (its premise result row), a Dictionary `remove`'s key borrow and a Utf8Writer `write<T>` (a store untied only by `static`) keep what
    // one read carries, valid where the borrowed values are not used after `y = 5`; the twins use them after it.
    [Fact]
    public void AContentRowStaysOneRead()
        => Assert.Equal(
            ["iterLate: y = 5", "emitLate: y = 5"],
            Problems(RawShapes + """
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
        => this.Measured(Prelude + RawShapes + ChainShapes + shape).Work - this.Measured(Prelude + RawShapes + ChainShapes + twin).Work;

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
}
