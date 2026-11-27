// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
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
    // (h01, `Projection[cell]`; a04, the input `a` its binder states by `uniq`), so the call may store v there and `z = 5` errs. Both pass
    // `h: uniq/Wrap<U>`, put2's own Type parameter: `h.value`, storage of U, holds no `raw/u8` (SPEC 15.6.4; the owner's O-360 (a)), but
    // the holder U's layer addresses is reached, hidden or not, and takes what the borrows its Origin belongs to admit (5.7 Calls; the
    // owner's O-312 (a); PLAN G935 c3-5 reverts c2's relabel of the two to valid; a04's `let vw` record comes back as on fcd6a5381).
    // h01s is the same with a shown Type argument: put2s
    // writes `Wrap<raw/u8>` in its own Type expression, so `h.value` is a raw layer without an Origin the call writes, which may keep v
    // (SPEC 15.6.4, 5.7 Calls "a Type argument's ... included"), and `z = 5` errs while h is used after.
    [Fact]
    public void AMethodsLayerStatingItsSlotReadsItsDeclaredSignature()
        => Assert.Equal(
            [
                "i01: z = 5", "i01: cell@ref", "i02: z = 5", "i02: cell@ref", "h01: cell@ref", "h01: z = 5", "h01s: z = 5",
                "a04: z = 5", "a04: let vw: ViewA04 during a = ViewA04.init(r)",
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
                struct ViewH01S {source}
                    let loan: Loan<ref/(raw/u8) during source>
                    public init(t: ref/(raw/u8) during source) => self.loan = Loan.init(t)
                    public func put2s(self: ref/Self, h: uniq/Wrap<raw/u8>, v: raw/u8) => ()
                func h01s()
                    var z = 3
                    let w = z@ref
                    var cell: u8 = 0
                    var h = Wrap.init(cell@raw)
                    var c2: raw/u8 = null
                    let vw = ViewH01S.init(c2@ref)
                    vw.put2s(h@uniq, w@raw@raw/u8)
                    z = 5
                    inspect(h@ref)
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
    // a02 (a Tuple) and a03 (`ref` through a `ref`-kept slot) err, as on f69d55e12; G148 (1)'s route (BU-108, W-42) accepted s01, s03,
    // s05, a03 and e02/e03 by counting the callee's Type argument `V = ref/… during a`. In s01, s02, s04, s05, a02, a03 and e02 the layer
    // lies in the binding of the callee's own Type parameter: `w.value` (s04: `*w`), storage of that U, holds `v` or `k` only where a
    // premise makes it one (SPEC 15.6.4; the owner's O-360 (a)), but the holder the layer
    // addresses is reached, hidden or not, and takes what the borrows its Origin belongs to admit (5.7 Calls; the owner's O-312 (a);
    // PLAN G935 c3-5 reverts c2's relabel of these rows to valid). a03's `let vw` record comes back with them as on fcd6a5381, an
    // over-report on an invalid program (k's own value, a borrow of vw, fits no `raw/u8` holder; the holder past the layer is read as
    // the input writes it). Where the binder states `a` only by `ref`, a pointer stating it has no write authority, so
    // v01, v02, v03 and r20 (`Wrap.put<U>`) are valid (v02 and r20 erred before), and b01's body-formed `uniq` borrow during `a` does
    // not count (5.2.1); e01 is a guard. A generic's Function Item has that generic's declared signature, so s01 through
    // `let f = g<…>` errs too (t04i; t04v valid). A Closure environment's layer is judged by its binder as well: `ref` alone keeps t10v and t10w valid (they erred
    // before). put's own pair binder `s/T during a` admits `uniq`, so its layer is not capped whatever case the call selects (every
    // admitted Semantics case decides the cap, SPEC 5.2.1, 5.7; the owner's O-36 (a)): t11u, whose pointer comes from `u: uniq`, errs
    // (accepted before), as t11i does (by `z = 5` alone: its former record at `var q` needed p's own value, the exclusive borrow of q,
    // kept in `*x`, storage of putT11i's own T, which holds no such value; PLAN G935). x04 (`uniq` through a slot) and t11v
    // (put's own pair binder) are valid only by their read-only path: each pointer is read through a `ref/` parameter (`r2@follow`,
    // `r@follow`), which reaches what it addresses for reads only, so a `uniq`-kept slot or a pair binder caps that path and grants it
    // nothing (SPEC 5.2.1 condition 3, 15.6.4).
    [Fact]
    public void ATypeArgumentsLayerIsJudgedWhereItsOriginIsBound()
        => Assert.Equal(
            [
                "s01: z = 5", "s02: z = 5", "s03: z = 5", "s04: z = 5", "s05: z = 5", "a02: z = 5", "a03: z = 5",
                "a03: let vw: ViewA03 during a = ViewA03.init(r)", "e02: var z = 3", "e03: var z = 3", "t04i: z = 5",
                "t11u: z = 5", "t11i: z = 5",
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
                func putT11v<s/T>(x: s/T during a, p: uniq/(raw/(raw/u8) during a), v: raw/u8)
                    s is ref or uniq
                    return
                func t11v(r: ref/(raw/u8))
                    var z = 3
                    let w = z@ref
                    var q = r@follow@raw
                    putT11v(r, q@uniq, w@raw@raw/u8)
                    z = 5
                """));

    // SPEC 5.2.1 condition 3, 15.3.4 (DECISIONS BU-113, PLAN G501): a nested signature's own layer is capped by its declared inputs only.
    // n04's passOn2 states `a` by no input, so its caller's Type argument `U = ref/… during a` does not count, and the call may store v
    // into u's referent (`z = 5` errs; accepted before). Its written `ref` input caps it (n04v, n04g, n01, a06), whatever a Type argument
    // carries. A Type argument's layer stating `a` is judged by f's signature: `uniq` there makes n03 and a09 err, `ref` alone keeps a08
    // valid. The layer lies in the binding of g2's or g3's own U, whose storage `h.value` holds no `raw/u8`, but the holder it addresses
    // is reached, hidden or not (the owner's O-312 (a); PLAN G935 c3-5 reverts c2's relabel of n03 and a09). n04a, n04b and n04c are guards.
    [Fact]
    public void ANestedSignatureIsCappedByItsDeclaredInputs()
        => Assert.Equal(
            [
                "n03: z = 5", "n04: z = 5", "n04a: z = 5", "n04b: z = 5", "n04c: z = 5", "n04c: var x = u@follow@raw", "a09: z = 5",
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

    // SPEC 5.3 (PLAN G597's L2; the Middle's C-459 ruling, B's ruling 12 C2): a Storage over a raw pointer Place is `copy p`'s value, so
    // its element step is p's raw referent (MirSteps.Index's table), never p's own storage. Twin r4, labelled from SPEC first, valid in
    // each form, where v's referent is written while the view v lives: after the remainder's raw word p is overwritten ("overwrite") or
    // moved ("move"), and while a shared borrow r of p is live ("borrowed"). A5 reports nothing. A valid-program control: it passes with
    // and without MirSteps.Index's raw row in all three forms, so it pins no difference between the readings (B's ruling 15); the pin is
    // r8, AStorageOverARawPointerWritesWhereThePointerReaches. A hand-built body.
    [Theory]
    [InlineData("overwrite")]
    [InlineData("move")]
    [InlineData("borrowed")]
    public void AStorageOverARawPointerBorrowsNoneOfItsWord(string form)
    {
        var c = MirBuildTest.Compile("func w() -> i32 => 1\n");
        var program = c.BuildMir();
        program.Problems.Clear();
        var body = new MirBody();
        body.Reset(Body(program, "w").Key, default);
        body.AddLocal(new(BoundType.I32, MirLocalKind.Return, default));
        var pointer = c.Types.Pointer(BoundType.I32);
        var reference = c.Types.Reference(SemanticsKind.Ref, pointer);
        MirPlace Local(BoundType type) => new(body.AddLocal(new(type, MirLocalKind.Var, default)));
        var (p, q, v, r) = (Local(pointer), Local(pointer), Local(pointer), Local(reference));
        SourceInfo At(int id) => new(new(0, id), 0, true);
        int Constant(BoundType type, long n) => body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Integer, type, n))));
        var block = body.AddBlock();
        body.Push(block, MirStatement.Assign(p, MirRvalue.Use(Constant(pointer, 0)), At(1)));
        body.Push(block, MirStatement.Assign(v, MirRvalue.Storage(p, pointer), At(2)));
        var during = form switch
        {
            "move" => MirStatement.Assign(q, MirRvalue.Use(body.AddOperand(MirOperand.Move(p))), At(3)),
            "borrowed" => MirStatement.Assign(r, MirRvalue.Ref(MirRefKind.Shared, p, reference), At(3)),
            _ => MirStatement.Assign(p, MirRvalue.Use(Constant(pointer, 0)), At(3)),
        };
        body.Push(block, during);
        body.Push(block, MirStatement.Assign(body.Project(c.Types, v, MirProjectionKind.Deref, 0), MirRvalue.Use(Constant(BoundType.I32, 5)), At(4)));
        if (form == "borrowed")
        {
            body.Push(block, MirStatement.Of(MirStatementKind.FakeRead, r, At(5)));
        }

        body.Push(block, MirStatement.Assign(new(0), MirRvalue.Use(Constant(BoundType.I32, 1)), At(6)));
        body.Terminate(block, MirTerminator.Return(At(7)));
        new Stratum(c.Types).Run(program, Validated(c, body));
        Assert.Empty(program.Problems.All.ToArray());
    }

    // SPEC 5.3 (PLAN G597's L2; the Middle's C-459 answer, B's ruling 16): twins r5 ("stored") and r5r ("returned"), labelled from SPEC
    // first, both valid. `p[n]` is `*(p + n)`, so `v = storage(p)` over the raw pointer parameter p is p's value: v addresses p's
    // referent, the caller's, never p's own storage. r5 keeps v in `(*out).w`, a raw word the caller holds through the exclusive
    // parameter `out`; r5r returns v. A5 reports nothing. Controls too (B's ruling 16 (a)): the Index reading reports nothing in either
    // form, so they pin no difference between the readings; the pin is r8, AStorageOverARawPointerWritesWhereThePointerReaches. A
    // hand-built body.
    [Theory]
    [InlineData("stored")]
    [InlineData("returned")]
    public void AStorageOverARawPointerParameterAddressesItsReferent(string form)
    {
        var stored = form == "stored";
        var c = MirBuildTest.Compile(stored ? "struct Out\n    var w: raw/i32\nfunc w(p: raw/i32, out: uniq/Out)\n    out.w = p\n" : "func w(p: raw/i32) -> raw/i32 => p\n");
        var program = c.BuildMir();
        program.Problems.Clear();
        var shape = Body(program, "w");
        var body = new MirBody();
        body.Reset(shape.Key, default);
        foreach (var local in shape.Locals[..(stored ? 3 : 2)])
        {
            body.AddLocal(local); // The Return local and the Arg locals p (and out), as the builder gives them.
        }

        var (p, pointer) = (new MirPlace(1), shape.Locals[1].Type!);
        var v = new MirPlace(body.AddLocal(new(pointer, MirLocalKind.Var, default)));
        SourceInfo At(int id) => new(new(0, id), 0, true);
        var block = body.AddBlock();
        body.Push(block, MirStatement.Assign(v, MirRvalue.Storage(p, pointer), At(1)));
        var holder = stored ? body.Project(c.Types, body.Project(c.Types, new(2), MirProjectionKind.Deref, 0), MirProjectionKind.Field, 0) : new MirPlace(0);
        body.Push(block, MirStatement.Assign(holder, MirRvalue.Use(body.AddOperand(MirOperand.Copy(v))), At(2)));
        if (stored)
        {
            var unit = body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Unit, BoundType.Unit, 0))));
            body.Push(block, MirStatement.Assign(new(0), MirRvalue.Use(unit), At(3)));
        }

        body.Terminate(block, MirTerminator.Return(At(4)));
        new Stratum(c.Types).Run(program, Validated(c, body));
        Assert.Empty(program.Problems.All.ToArray().Select(static x => $"{x.Code} {x.Subject.Id}"));
    }

    // SPEC 5.3, 5.7 (PLAN G597's L2; U3's twin r7, U2's implementer's candidate, labelled from SPEC first, valid): r5 with Origins. The
    // raw pointer parameter p and the raw word out reaches both state r's Origin; `v = storage(p)` is `p + 0`, p's value, which keeps
    // its Origin (a displacement keeps it, SPEC 5.7), so storing v in `*out` fits `raw/i32 during r`. A5 reports nothing. A control: it
    // passes with and without MirSteps.Index's raw row.
    [Fact]
    public void AStorageOverARawPointerKeepsItsOrigin()
    {
        var c = MirBuildTest.Compile("func w(r: ref/i32, p: raw/i32 during r, out: uniq/(raw/i32 during r))\n    out@follow = p\n");
        var program = c.BuildMir();
        Assert.Empty(program.Problems.All.ToArray().Select(static x => $"{x.Code} {x.Subject.Id}")); // The source body's own check.
        var shape = Body(program, "w");
        var body = new MirBody();
        body.Reset(shape.Key, default);
        foreach (var local in shape.Locals[..4])
        {
            body.AddLocal(local); // The Return local and the Arg locals r, p and out, as the builder gives them.
        }

        var (p, pointer) = (new MirPlace(2), shape.Locals[2].Type!);
        var v = new MirPlace(body.AddLocal(new(pointer, MirLocalKind.Var, default)));
        SourceInfo At(int id) => new(new(0, id), 0, true);
        var block = body.AddBlock();
        body.Push(block, MirStatement.Assign(v, MirRvalue.Storage(p, pointer), At(1)));
        body.Push(block, MirStatement.Assign(body.Project(c.Types, new(3), MirProjectionKind.Deref, 0), MirRvalue.Use(body.AddOperand(MirOperand.Copy(v))), At(2)));
        var unit = body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Unit, BoundType.Unit, 0))));
        body.Push(block, MirStatement.Assign(new(0), MirRvalue.Use(unit), At(3)));
        body.Terminate(block, MirTerminator.Return(At(4)));
        new Stratum(c.Types).Run(program, Validated(c, body));
        Assert.Empty(program.Problems.All.ToArray().Select(static x => $"{x.Code} {x.Subject.Id}"));
    }

    // SPEC 5.2, 5.3 (PLAN G597's L2; B's ruling 16 (a), U3's twin r8, labelled from SPEC first, INVALID): SPEC 5.2's `anchored`
    // example over a Storage. p addresses x (`p = x@raw`), and `v = storage(p)` is `p + 0`, p's value (SPEC 5.3), so a reference
    // written through v (`*v = y@ref`) reaches x: x may now carry y's Loan, and `y = 5` conflicts with x's read below
    // (ComparisonLoanConflict_Kd at `y = 5`). The raw reading of a Storage over a raw pointer Place (MirSteps.Index's raw row) carries
    // what p reaches; the Index reading took p's own storage, so the write would reach p, not x (fail-before: nothing recorded). Its
    // valid counterpart "unread", labelled from SPEC first, drops x's read: no use of x follows `y = 5`, so y's Loan is not live there
    // (SPEC 5.2.1, 15.6) and A5 reports nothing under either reading. The pin of the raw row at the A5 layer: a hand-built body, since
    // the library's one producer writes nothing through its view, so no compiled program observes the row.
    [Theory]
    [InlineData("read")]
    [InlineData("unread")]
    public void AStorageOverARawPointerWritesWhereThePointerReaches(string form)
    {
        var c = MirBuildTest.Compile("func w() -> i32 => 1\n");
        var program = c.BuildMir();
        program.Problems.Clear();
        var body = new MirBody();
        body.Reset(Body(program, "w").Key, default);
        body.AddLocal(new(BoundType.I32, MirLocalKind.Return, default));
        var reference = c.Types.Reference(SemanticsKind.Ref, BoundType.I32);
        var pointer = c.Types.Pointer(reference);
        MirPlace Local(BoundType type) => new(body.AddLocal(new(type, MirLocalKind.Var, default)));
        var (z, y, x, p, v) = (Local(BoundType.I32), Local(BoundType.I32), Local(reference), Local(pointer), Local(pointer));
        SourceInfo At(int id) => new(new(0, id), 0, true);
        int Constant(long n) => body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, n))));
        var block = body.AddBlock();
        body.Push(block, MirStatement.Assign(z, MirRvalue.Use(Constant(0)), At(1)));
        body.Push(block, MirStatement.Assign(y, MirRvalue.Use(Constant(1)), At(2)));
        body.Push(block, MirStatement.Assign(x, MirRvalue.Ref(MirRefKind.Shared, z, reference), At(3)));
        body.Push(block, MirStatement.Assign(p, MirRvalue.Ref(MirRefKind.Raw, x, pointer), At(4)));
        body.Push(block, MirStatement.Assign(v, MirRvalue.Storage(p, pointer), At(5)));
        body.Push(block, MirStatement.Assign(body.Project(c.Types, v, MirProjectionKind.Deref, 0), MirRvalue.Ref(MirRefKind.Shared, y, reference), At(6)));
        body.Push(block, MirStatement.Assign(y, MirRvalue.Use(Constant(5)), At(7)));
        if (form == "read")
        {
            body.Push(block, MirStatement.Of(MirStatementKind.FakeRead, x, At(8)));
        }

        body.Push(block, MirStatement.Assign(new(0), MirRvalue.Use(Constant(1)), At(9)));
        body.Terminate(block, MirTerminator.Return(At(10)));
        new Stratum(c.Types).Run(program, Validated(c, body));
        string[] expected = form == "read" ? ["ComparisonLoanConflict_Kd 7"] : [];
        Assert.Equal(expected, program.Problems.All.ToArray().Select(static e => $"{e.Code} {e.Subject.Id}"));
    }

    // SPEC 5.2.2, 15.6 (PLAN G597's L2, its twins r1-r3, labelled from SPEC first): an exclusive view's elements are its receiver's, so a
    // reference written through the view is held by the receiver: r1 through a whole view and r2 through a displaced one write y's Loan
    // into a, then y is written while a is read below (invalid, the conflict at `y = 5`); r3 without the write (valid).
    [Fact]
    public void AnExclusiveViewsWrittenReferenceIsItsReceivers()
        => Assert.Equal(
            ["r1: y = 5", "r2: y = 5"],
            Problems("""
                func r1()
                    var z: i32 = 0
                    var y: i32 = 1
                    var a: Array<ref/i32> = [z@ref, z@ref]
                    var v = a.sliceUniq(..)
                    v[0] = y@ref
                    y = 5
                    inspect(a@ref)
                func r2()
                    var z: i32 = 0
                    var y: i32 = 1
                    var a: Array<ref/i32> = [z@ref, z@ref]
                    var v = a.sliceUniq(1..)
                    v[0] = y@ref
                    y = 5
                    inspect(a@ref)
                func r3()
                    var z: i32 = 0
                    var y: i32 = 1
                    var a: Array<ref/i32> = [z@ref, z@ref]
                    var v = a.sliceUniq(..)
                    v[0] = y@ref
                    inspect(a@ref)
                """));

    // SPEC 5.3, 8.15.3 (PLAN G597's L2; the Middle's C-476 C3): the MIR-layer observation of MirBuilder.Form, on Storage.sliceUniq's
    // body, labelled from SPEC first, valid. Its call `viewStart(state.storage, selected.start)` is no Call: it is the keyed Storage over
    // the remainder's raw pointer Place `state.storage`, `storage(_1.0, copy _n.0, const type R)`, with the selected start and a Type
    // token of the body's open R, which each instance decides (fail-before: without Form's branch the call stays a Call, which the MIR
    // validator records as an Operation finding, and no keyed Storage line appears). Its view start is `state.storage + start` (SPEC
    // 5.3), an address among the elements the remainder's pointer reaches in what `source` covers, never the by-value parameter state's
    // own storage, so the view is `during source` as the signature states: the library check (A1-A5 over every library body) and the
    // MIR validator report nothing, with and without MirSteps.Index's raw row (it writes nothing through its view), so it pins no
    // difference between the readings.
    [Fact]
    public void SliceUniqsViewStartAddressesTheRemaindersElements()
    {
        var c = SemanticSnapshotTest.Bind("library").Compilation;
        var program = new MirAnalysis(c.Types).Run(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), 1);
        Assert.Empty(program.Problems.All.ToArray().Select(static x => $"{x.Code} {x.Subject.Module}:{x.Subject.Id}"));
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(
            static x => x.Key.Decl is { Name: "sliceUniq", Scope.Owner: DeclarationContainerKoto { Name: "Storage" } });
        var text = new StringBuilder();
        MirDump.Write(body, text, new MirDropTable());
        var view = Assert.Single(text.ToString().Split('\n'), static x => x.Contains("= storage(", StringComparison.Ordinal));
        Assert.Matches(@"^ +(\* )?_\d+ = storage\(_1\.0, copy _\d+\.0, const type R\)", view);
    }
}
