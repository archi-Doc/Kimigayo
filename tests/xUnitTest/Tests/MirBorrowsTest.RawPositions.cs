// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// A raw layer steps by its position (DECISIONS BU-113; PLAN G487, G493, G501): the probe families of their design (`bu\batch\OI\G487`).
public sealed partial class MirBorrowsTest
{
    // SPEC 5.2.1 condition 3, 5.7 Authority (DECISIONS BU-113, PLAN G487): a method's own layer stating its Type's slot is read as its
    // signature writes it, `self: uniq/Self` stating the slot with its Loan requirement, whatever the receiver binds it to. So c05
    // (`during source`), c06 (`during self.source`), m02 (`ref/Self`), m03 (a generic method) and m06 (the `ref`-kept slot of two) are
    // valid; each erred at its `var vw`, `var q` and `z = 5` before, where the receiver's `var` slot replaced `source`. a05 (the slot bound
    // to an input Origin) and a07 (a `ref` Type argument stating it too) stay valid. The twins err: a `uniq`-kept slot (i01, and i02's
    // second slot) lets put store v into `cell`. A Type argument's layer stating the receiver's Origin is the caller's, not the signature's
    // (h01, `Projection[cell]`; a04, the input `a` its binder states by `uniq`), so the call may store v there and `z = 5` errs.
    [Fact]
    public void AMethodsLayerStatingItsSlotReadsItsDeclaredSignature()
        => Assert.Equal(
            [
                "i01: z = 5", "i01: cell@ref", "i02: z = 5", "i02: cell@ref", "h01: cell@ref", "h01: z = 5", "a04: z = 5",
                "a04: let vw: ViewA04 during a = ViewA04.init(r)",
            ],
            Problems("""
                func putAny<U, V>(t: uniq/U, v: V) => ()
                struct Wrap<E>
                    var value: E
                    public init(value: E) => self.value = value@move
                    public func put<U>(self: uniq/Self, v: U) => ()
                struct ViewC05 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put(self: uniq/Self, p: uniq/(raw/(raw/u8) during source), v: raw/u8) => ()
                func c05(r: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var vw = ViewC05.init(r)
                    var q = r@follow@raw
                    vw.put(q@uniq, w@raw@raw/u8)
                    z = 5
                struct ViewC06 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put(self: uniq/Self, p: uniq/(raw/(raw/u8) during self.source), v: raw/u8) => ()
                func c06(r: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var vw = ViewC06.init(r)
                    var q = r@follow@raw
                    vw.put(q@uniq, w@raw@raw/u8)
                    z = 5
                struct ViewM02 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put(self: ref/Self, p: uniq/(raw/(raw/u8) during source), v: raw/u8) => ()
                func m02(r: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var vw = ViewM02.init(r)
                    var q = r@follow@raw
                    vw.put(q@uniq, w@raw@raw/u8)
                    z = 5
                struct ViewM03 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put<U>(self: uniq/Self, p: uniq/(raw/(raw/u8) during source), v: U) => ()
                func m03(r: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var vw = ViewM03.init(r)
                    var q = r@follow@raw
                    vw.put(q@uniq, w@raw@raw/u8)
                    z = 5
                struct View2M06 {a, b}
                    let la: Loan<ref/(raw/u8) during a>
                    let lb: Loan<uniq/(raw/u8) during b>
                    public init(t: ref/(raw/u8) during a, u: uniq/(raw/u8) during b)
                        self.la = Loan.init(t)
                        self.lb = Loan.init(u)
                    public func put(self: uniq/Self, p: uniq/(raw/(raw/u8) during a), v: raw/u8) => ()
                func m06(r: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var cell: raw/u8 = null
                    var vw = View2M06.init(r, cell@uniq)
                    var q = r@follow@raw
                    vw.put(q@uniq, w@raw@raw/u8)
                    z = 5
                struct ViewUI01 {source}
                    let loan: Loan<uniq/(raw/u8) during source>
                    public init(t: uniq/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put(self: uniq/Self, p: uniq/(raw/(raw/u8) during source), v: raw/u8) => ()
                func i01()
                    var z = 3
                    let w = z@ref
                    var cell: raw/u8 = null
                    let u = cell@uniq
                    var q = u@follow@raw
                    var vw = ViewUI01.init(u@move)
                    vw.put(q@uniq, w@raw@raw/u8)
                    z = 5
                    inspect(cell@ref)
                struct View2I02 {a, b}
                    let la: Loan<ref/(raw/u8) during a>
                    let lb: Loan<uniq/(raw/u8) during b>
                    public init(t: ref/(raw/u8) during a, u: uniq/(raw/u8) during b)
                        self.la = Loan.init(t)
                        self.lb = Loan.init(u)
                    public func put(self: uniq/Self, p: uniq/(raw/(raw/u8) during b), v: raw/u8) => ()
                func i02(r: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var cell: raw/u8 = null
                    let u = cell@uniq
                    var q = u@follow@raw
                    var vw = View2I02.init(r, u@move)
                    vw.put(q@uniq, w@raw@raw/u8)
                    z = 5
                    inspect(cell@ref)
                struct ViewH01 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put2<U>(self: ref/Self, h: uniq/Wrap<U>, v: raw/u8) => ()
                func h01()
                    var z = 3
                    let w = z@ref
                    var cell: raw/u8 = null
                    let u = cell@uniq
                    var h = Wrap.init(u@follow@raw)
                    let vw = ViewH01.init(cell@ref)
                    vw.put2(h@uniq, w@raw@raw/u8)
                    z = 5
                    inspect(u)
                struct ViewA04 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put2<U>(self: ref/Self, h: uniq/Wrap<U>, v: raw/u8) => ()
                func a04(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(u@follow@raw)
                    let vw: ViewA04 during a = ViewA04.init(r)
                    vw.put2(h@uniq, w@raw@raw/u8)
                    z = 5
                struct ViewA05 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put(self: uniq/Self, p: uniq/(raw/(raw/u8) during source), v: raw/u8) => ()
                func a05(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var vw: ViewA05 during a = ViewA05.init(r)
                    var x = u@follow@raw
                    vw.put(x@uniq, w@raw@raw/u8)
                    z = 5
                struct ViewA07 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put<U>(self: uniq/Self, p: uniq/(raw/(raw/u8) during source), v: raw/u8, k: U) => ()
                func a07(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var vw: ViewA07 during a = ViewA07.init(r)
                    var x = u@follow@raw
                    var y = r@follow@raw
                    vw.put(x@uniq, w@raw@raw/u8, y@ref)
                    z = 5
                """));

    // SPEC 5.7 Authority, 5.2.1 condition 3 (DECISIONS BU-113, PLAN G493): a Type argument's raw layer is judged where its Origin is bound,
    // by the binder's declared inputs, never by the callee's instantiated ones. Where the caller states `a` by `uniq` as well as `ref`, the
    // callee may store v through it and `z = 5` (or the scope end, e02 and e03) errs: s01, s03 (a method), s05 (a nested Type argument),
    // a02 (a Tuple), a03 (`ref` through a `ref`-kept slot) and x04 (`uniq` through a slot) err, as on f69d55e12; G148 (1)'s route
    // (BU-108, W-42) accepted s01, s03, s05, a03 and e02/e03 by counting the callee's Type argument `V = ref/… during a`. Where the
    // binder states `a` only by `ref`, a pointer stating it has no write authority, so v01, v02, v03 and r20 (`Wrap.put<U>`) are valid
    // (v02 and r20 erred before), and b01's body-formed `uniq` borrow during `a` does not count (5.2.1). s02, s04 and e01 are guards.
    // A generic's Function Item has that generic's declared signature, so s01 through `let f = g<…>` errs too (t04i, accepted before;
    // t04v valid). A Closure environment's layer is judged by its binder as well: `ref` alone keeps t10v and t10w valid (they erred
    // before). put's own pair binder `s/T during a` admits `uniq`, so its layer is not capped whatever case the call selects: t11u, whose
    // pointer comes from `u: uniq`, errs (accepted before), as t11i does.
    [Fact]
    public void ATypeArgumentsLayerIsJudgedWhereItsOriginIsBound()
        => Assert.Equal(
            [
                "s01: z = 5", "s02: z = 5", "s03: z = 5", "s04: z = 5",
                "s05: z = 5", "a02: z = 5", "a03: z = 5", "a03: let vw: ViewA03 during a = ViewA03.init(r)",
                "e02: var z = 3", "e03: var z = 3", "x04: z = 5", "t04i: z = 5", "t11u: z = 5", "t11i: z = 5", "t11i: var q = u@follow@raw",
            ],
            Problems("""
                func putAny<U, V>(t: uniq/U, v: V) => ()
                struct Wrap<E>
                    var value: E
                    public init(value: E) => self.value = value@move
                    public func put<U>(self: uniq/Self, v: U) => ()
                func gS01<U, V>(w: uniq/Wrap<U>, k: V, v: raw/u8) => ()
                func s01(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(u@follow@raw)
                    gS01(h@uniq, r, w@raw@raw/u8)
                    z = 5
                func gS02<U>(w: uniq/Wrap<U>, v: raw/u8) => ()
                func s02(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(u@follow@raw)
                    gS02(h@uniq, w@raw@raw/u8)
                    z = 5
                struct Wrap2S03<E>
                    var value: E
                    public init(value: E) => self.value = value@move
                    public func put2<V>(self: uniq/Self, k: V, v: raw/u8) => ()
                func s03(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap2S03.init(u@follow@raw)
                    h.put2(r, w@raw@raw/u8)
                    z = 5
                struct PairS04<E, F>
                    var e: E
                    var f: F
                    public init(e: E, f: F)
                        self.e = e@move
                        self.f = f@move
                func gS04<U>(w: uniq/U, v: raw/u8) => ()
                func s04(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = PairS04.init(u@follow@raw, r)
                    gS04(h@uniq, w@raw@raw/u8)
                    z = 5
                func gS05<U, V>(w: uniq/Wrap<U>, k: Option<V>, v: raw/u8) => ()
                func s05(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(Wrap.init(u@follow@raw))
                    gS05(h@uniq, Option.Some(r), w@raw@raw/u8)
                    z = 5
                func gV01<U, V>(w: uniq/Wrap<U>, k: V, v: raw/u8) => ()
                func v01(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(r2@follow@raw)
                    gV01(h@uniq, r, w@raw@raw/u8)
                    z = 5
                func gV02<U>(w: uniq/Wrap<U>, v: raw/u8) => ()
                func v02(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(r2@follow@raw)
                    gV02(h@uniq, w@raw@raw/u8)
                    z = 5
                struct Wrap2V03<E>
                    var value: E
                    public init(value: E) => self.value = value@move
                    public func put2<V>(self: uniq/Self, k: V, v: raw/u8) => ()
                func v03(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap2V03.init(r2@follow@raw)
                    h.put2(r, w@raw@raw/u8)
                    z = 5
                func gA02<U, V>(w: uniq/(U, i32), k: V, v: raw/u8) => ()
                func a02(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = (u@follow@raw, 1)
                    gA02(h@uniq, r, w@raw@raw/u8)
                    z = 5
                struct ViewA03 {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                func gA03<U, V>(w: uniq/Wrap<U>, k: ref/V, v: raw/u8) => ()
                func a03(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(u@follow@raw)
                    let vw: ViewA03 during a = ViewA03.init(r)
                    gA03(h@uniq, vw@ref, w@raw@raw/u8)
                    z = 5
                func gE01<U, V>(w: uniq/Wrap<U>, k: V, v: raw/u8) => ()
                func e01(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a, p: raw/u8)
                    var h = Wrap.init(u@follow@raw)
                    gE01(h@uniq, r, p)
                func gE02<U, V>(w: uniq/Wrap<U>, k: V, v: raw/u8) => ()
                func e02(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(u@follow@raw)
                    gE02(h@uniq, r, w@raw@raw/u8)
                    inspect(w)
                struct Wrap2E03<E>
                    var value: E
                    public init(value: E) => self.value = value@move
                    public func put2<V>(self: uniq/Self, k: V, v: raw/u8) => ()
                func e03(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap2E03.init(u@follow@raw)
                    h.put2(r, w@raw@raw/u8)
                func gB01<U>(w: uniq/Wrap<U>, v: raw/u8) => ()
                func b01(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during c)
                    origin c outlives a
                    var z = 3
                    let w = z@ref
                    let u2: uniq/(raw/u8) during a = u
                    var h = Wrap.init(u2@follow@raw)
                    gB01(h@uniq, w@raw@raw/u8)
                    z = 5
                struct HS2X04 {s}
                    let loan: Loan<uniq/(raw/u8) during s>
                    public init(t: uniq/(raw/u8) during s) => self.loan = Loan.init(t)
                func gX04<U>(w: uniq/Wrap<U>, v: raw/u8) => ()
                func x04(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a, hs: ref/(HS2X04 during a))
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(r2@follow@raw)
                    gX04(h@uniq, w@raw@raw/u8)
                    z = 5
                func r20(s: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(s@follow@raw)
                    h.put(w@raw@raw/u8)
                    z = 5
                    inspect(s)
                func gT04i<U, V>(w: uniq/Wrap<U>, k: V, v: raw/u8) => ()
                func t04i(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(u@follow@raw)
                    let f = gT04i<raw/(raw/u8) during a, ref/(raw/u8) during a>
                    f(h@uniq, r, w@raw@raw/u8)
                    z = 5
                func gT04v<U, V>(w: uniq/Wrap<U>, k: V, v: raw/u8) => ()
                func t04v(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(r2@follow@raw)
                    let f = gT04v<raw/(raw/u8) during a, ref/(raw/u8) during a>
                    f(h@uniq, r, w@raw@raw/u8)
                    z = 5
                func t10v(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    let p = r2@follow@raw
                    var k = func [p] (q: ref/(raw/u8) during a, v: raw/u8)
                        unsafe => *p = v
                    k(r, w@raw@raw/u8)
                    z = 5
                func gT10w<U, V>(w: uniq/U, k: V, v: raw/u8) => ()
                func t10w(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var h = Wrap.init(r2@follow@raw)
                    var c = func [h@move] () => ()
                    gT10w(c@uniq, r, w@raw@raw/u8)
                    z = 5
                func putT11u<s/T>(x: s/T during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8)
                    s is ref or uniq
                    return
                func t11u(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    var q = u@follow@raw
                    putT11u(r, q@uniq, w@raw@raw/u8)
                    z = 5
                func putT11i<s/T>(x: s/T during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8)
                    s is ref or uniq
                    return
                func t11i(u: uniq/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var q = u@follow@raw
                    putT11i(u, q@uniq, w@raw@raw/u8)
                    z = 5
                """));

    // SPEC 5.2.1 condition 3, 15.3.4 (DECISIONS BU-113, PLAN G501): a nested signature's own layer is capped by its declared inputs only.
    // n04's passOn2 states `a` by no input, so its caller's Type argument `U = ref/… during a` does not count, and the call may store v
    // into u's referent (`z = 5` errs; accepted before). Its written `ref` input caps it (n04v, n04g, n01, a06), whatever a Type argument
    // carries. A Type argument's layer stating `a` is judged by f's signature: `uniq` there makes n03 and a09 err, `ref` alone keeps a08
    // valid. n04a, n04b and n04c are guards.
    [Fact]
    public void ANestedSignatureIsCappedByItsDeclaredInputs()
        => Assert.Equal(
            [
                "n03: z = 5", "n04: z = 5", "n04a: z = 5", "n04b: z = 5",
                "n04c: z = 5", "n04c: var x = u@follow@raw", "a09: z = 5",
            ],
            Problems("""
                func putAny<U, V>(t: uniq/U, v: V) => ()
                struct Wrap<E>
                    var value: E
                    public init(value: E) => self.value = value@move
                    public func put<U>(self: uniq/Self, v: U) => ()
                func n01(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn<U>(q: ref/(raw/u8) during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: U) => ()
                    var x = u@follow@raw
                    passOn(r, x@uniq, w@raw@raw/u8, r)
                    z = 5
                func n03(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func g2<U>(q: ref/(raw/u8) during a, h: uniq/Wrap<U>, v: raw/u8) => ()
                    var h = Wrap.init(u@follow@raw)
                    g2(r, h@uniq, w@raw@raw/u8)
                    z = 5
                func n04(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn2<U>(p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: U) => ()
                    var x = u@follow@raw
                    passOn2(x@uniq, w@raw@raw/u8, r)
                    z = 5
                func n04a(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn3(p: uniq/(raw/(raw/u8) during a), v: raw/u8) => ()
                    var x = u@follow@raw
                    passOn3(x@uniq, w@raw@raw/u8)
                    z = 5
                func n04b(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn2<U>(p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: U) => ()
                    var x = u@follow@raw
                    passOn2(x@uniq, w@raw@raw/u8, 1)
                    z = 5
                func n04c(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn2<U>(p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: U) => ()
                    var x = u@follow@raw
                    passOn2(x@uniq, w@raw@raw/u8, u)
                    z = 5
                func n04v(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn4(p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: ref/(raw/u8) during a) => ()
                    var x = u@follow@raw
                    passOn4(x@uniq, w@raw@raw/u8, r)
                    z = 5
                func n04g(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn5<U>(p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: ref/(raw/u8) during a, m: U) => ()
                    var x = u@follow@raw
                    passOn5(x@uniq, w@raw@raw/u8, r, 1)
                    z = 5
                func a06(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn<U>(q: ref/(raw/u8) during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: U) => ()
                    var x = u@follow@raw
                    var y = r@follow@raw
                    passOn(r, x@uniq, w@raw@raw/u8, y@ref)
                    z = 5
                func a08(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func passOn<U>(q: ref/(raw/u8) during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8, k: uniq/Wrap<U>) => ()
                    var x = r2@follow@raw
                    var h = Wrap.init(r@follow@raw)
                    passOn(r, x@uniq, w@raw@raw/u8, h@uniq)
                    z = 5
                func a09(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                    var z = 3
                    let w = z@ref
                    func g3<U, V>(h: uniq/Wrap<U>, k: V, v: raw/u8) => ()
                    var h = Wrap.init(u@follow@raw)
                    g3(h@uniq, r, w@raw@raw/u8)
                    z = 5
                """));
}
