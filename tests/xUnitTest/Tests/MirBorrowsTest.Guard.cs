// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's per-part guard (SPEC 15.6.4, 5.7 Calls, 15.3.5; docs/dev/DECISIONS.md R4-M147): a call writes past a holder only where a
/// part of it may be an exclusive reference to storage that may hold a Loan, a struct's or enum's Type argument giving Loan facts only and
/// each stored raw layer read by its step on the instance holding it; a store past a holder the guard leaves undecided (MirWrites.Limit)
/// is the located Unsupported_Kd at its call.</summary>
public sealed partial class MirBorrowsTest
{
    // A3's listed loss P13, decided per part (DECISIONS R4-M147, which removed R4-M73's body hold): a generic loop over a local Array<T>,
    // then an append (g1), a shared loop over a local Array<uniq/(ref/i32)> (sharedOverExclusive, as its twin without next), and a
    // Loan<…> holder's stores reached through the closure (closureRoute), err(ended)'s γ (guardP13) and a later write through a reference
    // (writeQ) are valid, as rustc says: an iterator's and a Loan's Type argument has no address. E5's seed (seedRoute) errs at its
    // read of `w`: `Loan.init(w@uniq)`'s instantiated signature admits a callee storing a reborrow through its argument's layers into the
    // argument's own referent, whose two local regions may coincide (DECISIONS R4-M154, Revision 4), a self-row returning a route's Loan
    // to its holder, SPEC's letter (R4-M147's no-loss class); rustc's acceptance is PLAN G148 (2)'s open question (GXS). An abstract
    // Iterable's iterator writes by provenance (SPEC 15.6.4's reach rule, R4-M170): its Loan on items is shared, so a call writes nothing
    // past it and count is valid, an owner-decision reversal: the owner's G264 (S) instruction of 2026-11-04 overrides the owner's Q1 (a)
    // of 2026-10-25 (an abstract part may be an exclusive reference), under which count's parameter end erred. A5 still errs on count
    // (`$for.iterator`, `for _ in items`) until PLAN G416: the lent item's composed erased destruction stores next()'s Loan into the
    // iterator, a self-row; an (e) row of H5's list (G264 (S) S2 lands without GXS, the owner's O-9). A live iterator keeps the
    // append's activation (cleanRoute); and the conflicts no store causes stay (notHeld, emptyStore, heldDefault's Loan a copy's value keeps, R-a),
    // a conflict inside an inline default copy its template's (heldCopy, dd3; R4-M69). The pair cases' B6-for (pairs) is valid: its Array
    // literal is no call (DECISIONS R5-M129, as litPairOk), so PLAN G147 (8i)'s self-store row of a call taking the pair carrier by value
    // (TyCtxt.Retains, `i Value -> i`) does not reach it.
    [Fact]
    public void TheGuardReadsEachPart()
    {
        var c = MirBuildTest.Compile(Prelude + """
            func storeL(l: uniq/Loan<uniq/(ref/(ref/i32 during p) during q) during a>, v: ref/(ref/i32 during p) during q) => ()
            func storeD(l: uniq/Loan<uniq/(ref/Data during q) during a>, v: ref/Data during q) -> bool => true
            func storeU(l: uniq/Loan<uniq/(uniq/(ref/i32 during p) during q) during a>, v: uniq/(ref/i32 during p) during q) => ()
            struct WQ {a}
                public var p: uniq/(ref/i32 during a) during a
                public var q: ref/(ref/i32 during a) during a
                public init(p: uniq/(ref/i32 during a) during a, q: ref/(ref/i32 during a) during a)
                    self.p = p
                    self.q = q
            func storeQ(l: uniq/Loan<uniq/WQ during q> during m, v: ref/(ref/i32 during p) during q) => ()
            func dd3(n: i32, m: i32 = (do
                var t = n
                let r = t@ref
                t = 2
                yield size(r)
            )) => ()
            func sameD(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y
            func g1<T>(a: T, b: T)
                T is Copy
                var xs: Array<T> = []
                xs.append(a)
                for e in xs
                    _ = e
                xs.append(b)
            func pairs<s/T>(value: s/T, x: T)
                s is owner or uniq
                T is Copy
                var v = value@move
                let child = v
                do
                    var arr: Array<s/T> = [child@move]
                    for e in arr
                        _ = e@follow@follow
                v@follow = x
            func count<S>(items: S) -> i32
                S is Iterable
                var n: i32 = 0
                for _ in items
                    n += 1
                return n
            func sharedOverExclusive()
                var x = 1
                var w = x@ref
                do
                    var arr: Array<uniq/(ref/i32)> = [w@uniq]
                    for e in arr
                        _ = e
                inspect(w)
            func sharedOverExclusiveTwin()
                var x = 1
                var w = x@ref
                do
                    var arr: Array<uniq/(ref/i32)> = [w@uniq]
                    var it = arr.iterate()
                    _ = it@uniq
                inspect(w)
            func closureRoute()
                var x = 1
                var y = 2
                var z: ref/i32 = x@ref
                var w: ref/(ref/i32) = z@ref
                var lh = Loan<uniq/(ref/(ref/i32))>.init(w@uniq)
                var b: ref/i32 = y@ref
                storeL(lh@uniq, b@ref)
                y = 5
                inspect(w)
            func guardP13()
                let packet: Packet = .Data(Data.init())
                let other = Data.init()
                var saved = other@ref
                var lh = Loan<uniq/(ref/Data)>.init(saved@uniq)
                match packet
                    .Data(let d) if storeD(lh@uniq, d) => ()
                    _ => ()
                inspect(saved)
            func writeQ()
                var x = 1
                var y = 2
                var c = 3
                var z: ref/i32 = x@ref
                var z2: ref/i32 = x@ref
                var zz = z2@ref
                var w = WQ.init(p: z@uniq, q: zz)
                var lh = Loan<uniq/WQ>.init(w@uniq)
                var b: ref/i32 = y@ref
                storeQ(lh@uniq, b@ref)
                w.p@follow = c@ref
                c = 5
                inspect(b)
            func seedRoute()
                var x = 1
                var y = 2
                var z: ref/i32 = x@ref
                var w: uniq/(ref/i32) = z@uniq
                var lh = Loan<uniq/(uniq/(ref/i32))>.init(w@uniq)
                var b: ref/i32 = y@ref
                storeU(lh@uniq, b@uniq)
                let r = w@follow@follow@ref
                inspect(r)
            func cleanRoute<T>(a: T, b: T)
                T is Copy
                var xs: Array<T> = []
                xs.append(a)
                var it = xs.iterate()
                _ = it.next()
                xs.append(b)
                _ = it.next()
            func notHeld()
                var x = 1
                var xs: Array<ref/i32> = []
                xs.append(x@ref)
                var it = xs.iterate()
                _ = it.next()
                x = 2
                _ = it.next()
            func heldCopy<T>(a: T, b: T)
                T is Copy
                var xs: Array<T> = []
                xs.append(a)
                for e in xs
                    _ = e
                xs.append(b)
                dd3(1)
            func heldDefault<T>(a: T, b: T)
                T is Copy
                var xs: Array<T> = []
                xs.append(a)
                for e in xs
                    _ = e
                var k = 1
                let r = sameD(k@ref)
                k = 2
                inspect(r)
            func emptyStore<T>()
                var xs: Array<Option<T>> = []
                xs.append(.None)
                var k = 1
                let r = k@ref
                k = 2
                inspect(r)
            """);
        Assert.Equal(
            [
                "dd3: t = 2", ": $for.iterator", "count: for _ in items", "seedRoute: w@follow@follow@ref", "cleanRoute: xs.append(b) CallActivationConflict_Kd",
                "notHeld: x = 2", "heldDefault: k = 2", "emptyStore: k = 2",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 15.6.4's reach rule (DECISIONS R4-M170; G264 (S) S1′): an abstract part in a holder's own storage writes by provenance. Past it
    // a call writes the holders its exclusive Loans and addresses name and what is reached on from them by reads through no shared layer
    // (laterWriterBad: x addresses slot; chainBad: x → pp → slot), never a holder only a shared Loan names (count, TheGuardReadsEachPart),
    // and E only where E was reached through no shared layer (Φ_W): an untied result (laterWriterOk, clearRcOk and sharedRawGen err, the
    // owner's 「Yes, writable」; sharedRawGen's `T.make` result is tied to no argument, so x reaches E writable, item 5; a tied result read
    // through a shared Deref of a `ref/` parameter is PLAN G419's false error), a raw pointer it derives from (allocBad, alloc2Bad,
    // allocOptBad, allocTupBad, allocViaUBad), and a parameter or a capture (ownParamBad, ownParamGen, directParamGen, exchGen, capGen,
    // nestGen). A generic Writer (writerBad) errs by its call's own raw-argument retention, an exclusive reference
    // (paramBad) past it by the write fact, and a live exclusive Loan (uniqBad) by the store. Every row errs on dev too, there through the
    // owner's Q1 (a), which the owner's G264 (S) instruction of 2026-11-04 overrides; writerOk is valid on both. gSetQBad (PLAN G935):
    // a generic callee whose contract hands `setQ`, a requirement with a Self receiver, may call `dst.setQ(p)`, a call through a value
    // of its abstract T that writes the holders x's addresses name (slot), so slot may keep w's address and `y@follow = 5` errs.
    // fMake's contract hands only `make`, whose result may keep p in `*dst` (15.6.4 "through its result"), whatever Type T is bound to:
    // with T = Wk, whose only Field no handed signature names, mkConcBad (x read after `y = 5`) errs and mkConcOk (x read before) is
    // valid, though rejected at `y = 5` again since c3-5 as on fcd6a5381 (x.k carries no holder's address; a known limit of this tree's A5, which maps the key past a hidden layer to storage the body does not own: open issue "A5's Φ_E exclusion past a hidden layer", PLAN G935 c3-5). A `ref/Self` receiver writes
    // nothing (15.6.4 "never through a shared Loan"): setQRefParamOk is valid (fail-before on prototype 16: `y = 5`), its `uniq/Self`
    // twin setQUniqParamBad errs.
    [Fact]
    public void AnAbstractPartWritesByProvenance()
    {
        var c = MirBuildTest.Compile(Prelude + """
            contract MakeR
                func make(p: raw/(raw/u8), q: raw/u8) -> Self
            contract MakeS
                func make(p: raw/(raw/u8)) -> Self
                func setQ(self: uniq/Self, q: raw/u8)
            contract MakeP
                func make(p: raw/(raw/(raw/u8))) -> Self
                func setQ(self: uniq/Self, q: raw/u8)
            contract MakeV
                func make(v: raw/u8) -> Self
                func setQ(self: uniq/Self, q: raw/u8)
            contract MakeO
                func make(v: Option<raw/u8>) -> Self
                func setQ(self: uniq/Self, q: raw/u8)
            contract MakeT
                func make(v: (raw/u8, i32)) -> Self
                func setQ(self: uniq/Self, q: raw/u8)
            contract MakeU
                func make(v: uniq/i32) -> Self
                func setQ(self: uniq/Self, q: raw/u8)
            contract SetQ
                func setQ(self: uniq/Self, q: raw/u8)
            contract MakeQ
                func make(p: raw/(raw/u8), q: raw/u8) -> Self
                func setQ(self: uniq/Self, q: raw/u8)
            func usep(p: raw/u8) => ()
            func consume<T>(x: T) => ()
            func idp(p: raw/u8) -> raw/u8 => p
            func fSetQ<T>(dst: uniq/T, p: raw/u8)
                T is MakeQ
                return
            struct Wk
                Self is MakeR
                public var k: raw/(raw/(raw/u8))
                public init() => self.k = null
                public func make(p: raw/(raw/u8), q: raw/u8) -> Self => Wk.init()
            func fMake<T>(dst: uniq/T, p: raw/u8)
                T is MakeR
                return
            contract SetQR
                func setQ(self: ref/Self, q: raw/u8)
            func fSetQR<T>(dst: uniq/T, p: raw/u8)
                T is SetQR
                return
            func fSetQU<T>(dst: uniq/T, p: raw/u8)
                T is SetQ
                return
            func writerBad<T>()
                T is MakeR
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = T.make(p: slot@raw, q: w@raw@raw/u8)
                consume(x@move)
                y = 5
                usep(slot)
            func writerOk<T>()
                T is MakeR
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = T.make(p: slot@raw, q: w@raw@raw/u8)
                consume(x@move)
                usep(slot)
                y = 5
            func laterWriterBad<T>()
                T is MakeS
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = T.make(p: slot@raw)
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                y = 5
                usep(slot)
            func laterWriterOk<T>()
                T is MakeS
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = T.make(p: slot@raw)
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                usep(slot)
                y = 5
            func chainBad<T>()
                T is MakeP
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var pp: raw/(raw/u8) = slot@raw
                var x = T.make(p: pp@raw)
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                y = 5
                usep(slot)
            func allocBad<T>()
                T is MakeV
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var m: raw/u8 = Raw.allocate<u8>(1)
                var x = T.make(v: m)
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                y = 5
            func alloc2Bad<T>()
                T is MakeV
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var m: raw/u8 = Raw.allocate<u8>(1)
                var n = idp(m)
                var x = T.make(v: n)
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                y = 5
            func allocOptBad<T>()
                T is MakeO
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var m: raw/u8 = Raw.allocate<u8>(1)
                var x = T.make(v: .Some(m))
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                y = 5
            func allocTupBad<T>()
                T is MakeT
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var m: raw/u8 = Raw.allocate<u8>(1)
                var x = T.make(v: (m, 1))
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                y = 5
            func allocViaUBad<T, U>()
                T is MakeV
                U is MakeV
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var m: raw/u8 = Raw.allocate<u8>(1)
                var u = U.make(v: m)
                u.setQ(w@raw@raw/u8)
                consume(u@move)
                y = 5
            func paramBad<T>(x: uniq/T)
                T is MakeV
                var y: i32 = 1
                var w: ref/i32 = y@ref
                x.setQ(w@raw@raw/u8)
                y = 5
            func ownParamBad<T>(x: T)
                T is MakeV
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var x2 = x@move
                x2.setQ(w@raw@raw/u8)
                consume(x2@move)
                y = 5
            func ownParamGen<T>(x: T)
                T is SetQ
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var x2 = x@move
                x2.setQ(w@raw@raw/u8)
                consume(x2@move)
                y = 5
            func directParamGen<T>(x: T)
                T is SetQ
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var x2 = x@move
                x2.setQ(w@raw@raw/u8)
                y = 5
                consume(x2@move)
            func sharedRawGen<T>(r: ref/(raw/u8))
                T is MakeV
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var x = T.make(v: r@follow)
                x.setQ(w@raw@raw/u8)
                consume(x@move)
                y = 5
            func exchGen<T>(x: uniq/T, fresh: T)
                T is SetQ
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var old = Kimi.Intrinsics.exchange(x, with: fresh@move)
                old.setQ(w@raw@raw/u8)
                consume(old@move)
                y = 5
            func capGen<T>(x: T)
                T is SetQ
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var q: raw/u8 = w@raw@raw/u8
                var c = func [var x@move, q] () => x.setQ(q)
                c()
                consume(c@move)
                y = 5
            func nestGen<T>(x: T)
                T is SetQ
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var z = x@move
                var r = z@uniq
                var t = (r@move, 0)
                t.0.setQ(w@raw@raw/u8)
                consume(t@move)
                consume(z@move)
                y = 5
            func uniqBad<T>()
                T is MakeU
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var z: i32 = 2
                var x = T.make(v: z@uniq)
                x.setQ(w@raw@raw/u8)
                y = 5
                z = 3
                consume(x@move)
            func clearRcOk<T>()
                T is MakeR
                T is ObjectPayload
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var a: Array<rc/T> = []
                a.append(Kimi.Intrinsics.makeRc(T.make(p: slot@raw, q: w@raw@raw/u8)))
                a.clear()
                consume(a@move)
                usep(slot)
                y = 5
            func clearRcBad<T>()
                T is MakeR
                T is ObjectPayload
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var a: Array<rc/T> = []
                a.append(Kimi.Intrinsics.makeRc(T.make(p: slot@raw, q: w@raw@raw/u8)))
                a.clear()
                consume(a@move)
                y = 5
                usep(slot)
            func gSetQBad<T>(y: uniq/i32)
                T is MakeQ
                let w = y@follow@ref
                var slot: raw/u8 = null
                var x = T.make(p: slot@raw, q: null)
                fSetQ(x@uniq, w@raw@raw/u8)
                y@follow = 5
                usep(slot)
                consume(x@move)
            func mkConcBad()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var x = Wk.init()
                fMake(x@uniq, w@raw@raw/u8)
                y = 5
                inspect(x@ref)
            func mkConcOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var x = Wk.init()
                fMake(x@uniq, w@raw@raw/u8)
                inspect(x@ref)
                y = 5
            func setQRefParamOk<X>(x: uniq/X)
                X is SetQR
                var y: i32 = 1
                var w: ref/i32 = y@ref
                fSetQR(x, w@raw@raw/u8)
                y = 5
            func setQUniqParamBad<X>(x: uniq/X)
                X is SetQ
                var y: i32 = 1
                var w: ref/i32 = y@ref
                fSetQU(x, w@raw@raw/u8)
                y = 5
            """);
        Assert.Equal(
            [
                "writerBad: y = 5", "laterWriterBad: y = 5", "laterWriterOk: y = 5", "chainBad: y = 5", "allocBad: y = 5",
                "alloc2Bad: y = 5", "allocOptBad: y = 5", "allocTupBad: y = 5", "allocViaUBad: y = 5", "paramBad: y = 5",
                "ownParamBad: y = 5", "ownParamGen: y = 5", "directParamGen: y = 5", "sharedRawGen: y = 5", "exchGen: y = 5",
                "capGen: y = 5", "nestGen: y = 5", "uniqBad: y = 5", "clearRcOk: y = 5", "clearRcBad: y = 5", "gSetQBad: y@follow = 5",
                "mkConcBad: y = 5", "mkConcOk: y = 5", "setQUniqParamBad: y = 5",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 8.2.3 ("identity is closed under replacement inside any Type"), 15.6.4 ("only where the premises make it one"; PLAN G935
    // c3-2, class 1): the callee's own abstract Type is kept only in the form its function's view gives it. Under `T is Option<V>`
    // storage of T is an Option<V>, whose payload keeps `value: V` (idFormBad: m holds spare, read after `kk = 2`), and a `value: T`
    // holds a V by value, which storage of V keeps (idFormInBad); under `T is E`, E the enclosing Type's parameter, `h.value` is
    // storage of E, which the receiver's instance completes, a `raw/(raw/u8)` the caller states by `uniq` (idEnclBad). Fail-before on
    // fce0d5078: the three were accepted (the storage walk kept T against the key walk's View). Their valid twins: a `raw/u8` input
    // that is no V (idFormOtherOk), the holder read before the write (idFormInOk, idAbsOk), the Origin stated only by `ref`
    // (idEnclOk). The callee without the premise (idEnclNoPremBad) errs too since c3-5: its own T holds no `raw/u8`, but the holder
    // T's layer addresses, `*u`, is reached, hidden or not (the owner's O-312 (a); F's ruling 30). Two own abstract Types a premise
    // identifies (`T is V`) both stay kept and fit each other (idAbsBad, rejected before c3-2 too). A raw layer a premise shows
    // steps by its binder's declared inputs (DECISIONS BU-113): under `T is raw/(raw/u8) during a`, p is a layer stating `a`, which
    // x states by `uniq` (idRawStepBad: fr may store v into `*x`) or only by `ref` (idRawStepOk: no write authority, 5.2.1
    // condition 3). A generic caller whose own premise gives its X the form is read alike (idFormGenBad, idFormGenOk). A handed
    // callable's result under `T is U` is a T (retIdWBad: mk's RetW may hold w's address, so dst is used after `y = 5`; F's ruling 52,
    // fail-before on 5f22c9b6c: accepted), as it is under `mk: (E) -> T` (retPlainWBad); without the premise f cannot store mk's U in
    // dst (retNoIdWOk). Identity is closed under replacement inside any Type: under `E is D` a writer `set: (uniq/T, BxI<D>)` takes
    // `y: BxI<E>` (structIdBad; F's ruling 55, fail-before on a6e0d4be4: accepted), as `set: (uniq/T, BxI<E>)` does with no premise
    // (structPlainBad); without the premise BxI<E> and BxI<D> are distinct and nothing keeps the pointer in dst (structNoIdOk). The view
    // is the body the comparison stands in (F's ruling 57): in nested g's kept reading g's own `E is D` makes `(T, E)` `(T, D)` though T
    // is the outer function's (nestIdBad; fail-before on b826b8c05: accepted), the components swapped alike (nestId2Bad), as with no
    // premise (nestPlainBad), and without it too (nestNoIdBad; the owner's O-423 (i), 15.6.4 "decided per argument, never per Origin":
    // set takes a T, which g may take from y, and writes what T hides, so T's hidden storage may keep y's Loans, p's included, as a part
    // y holds by value; formerly nestNoIdOk); outer's `T is U` holds in g's view, its instantiated reading at outer's S
    // included (nestOuterIdBad, nestOuterNoIdOk). U's requirement setQ, handed to fReq's body, may store q through PlainR's own raw
    // layer in storage the body does not own, so `y = 5` conflicts (rvNoIdReqOk, G964's twin, I by SPEC 15.6.4). Its `var o` record is
    // a known pre-existing over-retention, dev's and b826b8c05's too (open issue PLAN G989): o is recorded as retaining its own `o@uniq`
    // Loan, though 15.6.4 excludes the own value of the argument through which the call writes and a self-link is no retention, so no
    // storage retains that Loan; the record goes when G989 is fixed, a correction and not a weakening.
    [Fact]
    public void AKeptTypeIsReadInItsFunctionsView()
    {
        var c = MirBuildTest.Compile(Prelude + """
            func fo<T, V>(slot: uniq/(uniq/T during a), value: V)
                T is Option<V>
                return
            func fw<T, V>(slot: uniq/(uniq/T during a), value: raw/u8, w: V)
                T is Option<V>
                return
            func fi<T, V>(slot: uniq/(uniq/V during a), value: T)
                T is Option<V>
                return
            func fa<T, V>(slot: uniq/(uniq/T during a), value: V)
                T is V
                return
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
            struct Host<E>
                var e: E
                public init(e: E) => self.e = e@move
                public func put3<T>(self: ref/Self, h: uniq/Wrap<T>, v: raw/u8)
                    T is E
                    return
                public func put3n<T>(self: ref/Self, h: uniq/Wrap<T>, v: raw/u8) => ()
            func fr<T>(x: uniq/(raw/u8) during a, p: uniq/T, v: raw/u8)
                T is raw/(raw/u8) during a
                return
            func frr<T>(x: ref/(raw/u8) during a, p: uniq/T, v: raw/u8)
                T is raw/(raw/u8) during a
                return
            struct RetW
                public var q: raw/u8
                public init(q: raw/u8) => self.q = q
            func mkRetW(q: raw/u8) -> RetW => RetW.init(q: q)
            func fmkT<T, E>(dst: uniq/T, p: E, mk: (E) -> T) -> ()
                return
            func fmkU<T, U, E>(dst: uniq/T, p: E, mk: (E) -> U) -> ()
                T is U
                return
            func fmkN<T, U, E>(dst: uniq/T, p: E, mk: (E) -> U) -> ()
                return
            struct BxI<X>
                public var item: X
                public init(item: X) => self.item = item@move
            func setRetW(x: uniq/RetW, b: BxI<raw/u8>) => ()
            func fsId<T, E, D>(dst: uniq/T, y: BxI<E>, set: (uniq/T, BxI<D>) -> ()) -> ()
                E is D
                return
            func fsPlain<T, E>(dst: uniq/T, y: BxI<E>, set: (uniq/T, BxI<E>) -> ()) -> ()
                return
            func fsNo<T, E, D>(dst: uniq/T, y: BxI<E>, set: (uniq/T, BxI<D>) -> ()) -> ()
                return
            func idFormBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var m: Option<raw/u8> = .None
                var x = m@uniq
                fo(x@uniq, spare)
                kk = 2
                inspect(m@ref)
            func idFormOtherOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var m: Option<u64> = .None
                var x = m@uniq
                fw(x@uniq, spare, 0@u64)
                kk = 2
                inspect(m@ref)
            func idFormInBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var m: raw/u8 = null
                var x = m@uniq
                fi(x@uniq, Option.Some(spare))
                kk = 2
                inspect(m@ref)
            func idFormInOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var m: raw/u8 = null
                var x = m@uniq
                fi(x@uniq, Option.Some(spare))
                inspect(m@ref)
                kk = 2
            func idAbsBad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var m: raw/u8 = null
                var x = m@uniq
                fa(x@uniq, spare)
                kk = 2
                inspect(m@ref)
            func idAbsOk()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var m: raw/u8 = null
                var x = m@uniq
                fa(x@uniq, spare)
                inspect(m@ref)
                kk = 2
            func idEnclBad(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                var z = 3
                let w = z@ref
                var h = Wrap.init(u@follow@raw)
                let host = Host.init(r@follow@raw)
                host.put3(h@uniq, w@raw@raw/u8)
                z = 5
            func idEnclOk(r: ref/(raw/u8) during a, r2: ref/(raw/u8) during a)
                var z = 3
                let w = z@ref
                var h = Wrap.init(r2@follow@raw)
                let host = Host.init(r@follow@raw)
                host.put3(h@uniq, w@raw@raw/u8)
                z = 5
            func idEnclNoPremBad(r: ref/(raw/u8) during a, u: uniq/(raw/u8) during a)
                var z = 3
                let w = z@ref
                var h = Wrap.init(u@follow@raw)
                let host = Host.init(r@follow@raw)
                host.put3n(h@uniq, w@raw@raw/u8)
                z = 5
            func idRawStepBad(u: uniq/(raw/u8))
                var z = 3
                let w = z@ref
                var q = u@follow@raw
                fr(u, q@uniq, w@raw@raw/u8)
                z = 5
            func idRawStepOk(r: ref/(raw/u8))
                var z = 3
                let w = z@ref
                var q = r@follow@raw
                frr(r, q@uniq, w@raw@raw/u8)
                z = 5
            func idFormGenBad<X>(m: uniq/X)
                X is Option<raw/u8>
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var x = m
                fo(x@uniq, spare)
                kk = 2
                inspect(x@ref)
            func idFormGenOk<X>(m: uniq/X, p: raw/u8)
                X is Option<raw/u8>
                var x = m
                fo(x@uniq, p)
                inspect(x@ref)
            func retPlainWBad()
                var y: i32 = 1
                let w: ref/i32 = y@ref
                var dst = RetW.init(q: null)
                fmkT(dst@uniq, w@raw@raw/u8, mkRetW)
                y = 5
                inspect(dst@ref)
            func retIdWBad()
                var y: i32 = 1
                let w: ref/i32 = y@ref
                var dst = RetW.init(q: null)
                fmkU(dst@uniq, w@raw@raw/u8, mkRetW)
                y = 5
                inspect(dst@ref)
            func retNoIdWOk()
                var y: i32 = 1
                let w: ref/i32 = y@ref
                var dst = RetW.init(q: null)
                fmkN(dst@uniq, w@raw@raw/u8, mkRetW)
                y = 5
                inspect(dst@ref)
            func structIdBad()
                var y: i32 = 1
                let w: ref/i32 = y@ref
                var dst = RetW.init(q: null)
                fsId(dst@uniq, BxI.init(item: w@raw@raw/u8), setRetW)
                y = 5
                inspect(dst@ref)
            func structPlainBad()
                var y: i32 = 1
                let w: ref/i32 = y@ref
                var dst = RetW.init(q: null)
                fsPlain(dst@uniq, BxI.init(item: w@raw@raw/u8), setRetW)
                y = 5
                inspect(dst@ref)
            func structNoIdOk()
                var y: i32 = 1
                let w: ref/i32 = y@ref
                var dst = RetW.init(q: null)
                fsNo(dst@uniq, BxI.init(item: w@raw@raw/u8), setRetW)
                y = 5
                inspect(dst@ref)
            func nestIdBad<T>(t: T, wr: (uniq/RetW, (T, raw/u8)) -> ())
                func gId<S, E, D>(slot: uniq/S, y: (T, E), set: (uniq/S, (T, D)) -> ()) -> ()
                    E is D
                    return
                var y0: i32 = 1
                let w: ref/i32 = y0@ref
                var dst = RetW.init(q: null)
                let p: raw/u8 = w@raw@raw/u8
                gId<RetW, raw/u8, raw/u8>(dst@uniq, (t@move, p), wr@move)
                y0 = 5
                inspect(dst@ref)
            func nestId2Bad<T>(t: T, wr: (uniq/RetW, (raw/u8, T)) -> ())
                func gId2<S, E, D>(slot: uniq/S, y: (E, T), set: (uniq/S, (D, T)) -> ()) -> ()
                    E is D
                    return
                var y0: i32 = 1
                let w: ref/i32 = y0@ref
                var dst = RetW.init(q: null)
                let p: raw/u8 = w@raw@raw/u8
                gId2<RetW, raw/u8, raw/u8>(dst@uniq, (p, t@move), wr@move)
                y0 = 5
                inspect(dst@ref)
            func nestPlainBad<T>(t: T, wr: (uniq/RetW, (T, raw/u8)) -> ())
                func gPlain<S, E>(slot: uniq/S, y: (T, E), set: (uniq/S, (T, E)) -> ()) -> ()
                    return
                var y0: i32 = 1
                let w: ref/i32 = y0@ref
                var dst = RetW.init(q: null)
                let p: raw/u8 = w@raw@raw/u8
                gPlain<RetW, raw/u8>(dst@uniq, (t@move, p), wr@move)
                y0 = 5
                inspect(dst@ref)
            func nestNoIdBad<T>(t: T, wr: (uniq/RetW, (T, raw/u8)) -> ())
                func gNo<S, E, D>(slot: uniq/S, y: (T, E), set: (uniq/S, (T, D)) -> ()) -> ()
                    return
                var y0: i32 = 1
                let w: ref/i32 = y0@ref
                var dst = RetW.init(q: null)
                let p: raw/u8 = w@raw@raw/u8
                gNo<RetW, raw/u8, raw/u8>(dst@uniq, (t@move, p), wr@move)
                y0 = 5
                inspect(dst@ref)
            func nestOuterIdBad<T, U, S>(t: T, d: S, wr: (uniq/S, (U, raw/u8)) -> ())
                T is U
                func gOuter(slot: uniq/S, y: (T, raw/u8), set: (uniq/S, (U, raw/u8)) -> ()) -> ()
                    return
                var y0: i32 = 1
                let w: ref/i32 = y0@ref
                var dst = d@move
                let p: raw/u8 = w@raw@raw/u8
                gOuter(dst@uniq, (t@move, p), wr@move)
                y0 = 5
                inspect(dst@ref)
            func nestOuterNoIdOk<T, U, S>(t: T, d: S, wr: (uniq/S, (U, raw/u8)) -> ())
                func gOuterNo(slot: uniq/S, y: (T, raw/u8), set: (uniq/S, (U, raw/u8)) -> ()) -> ()
                    return
                var y0: i32 = 1
                let w: ref/i32 = y0@ref
                var dst = d@move
                let p: raw/u8 = w@raw@raw/u8
                gOuterNo(dst@uniq, (t@move, p), wr@move)
                y0 = 5
                inspect(dst@ref)
            contract SetQ
                func setQ(self: uniq/Self, q: raw/u8)
            struct PlainR
                Self is SetQ
                public var p: raw/(raw/u8)
                public init() => self.p = null
                public func setQ(self: uniq/Self, q: raw/u8)
                    unsafe => *((*self.p)@raw/(raw/u8)) = q
            struct Bx<T>
                public var item: T
                public init(item: T) => self.item = item@move
            func usep<T>(p: raw/T) => ()
            func fReq<T, U>(x: Bx<T>, u: uniq/U, q: raw/u8) -> ()
                U is SetQ
                return
            func rvNoIdReqOk()
                var y: i32 = 1
                var w: ref/i32 = y@ref
                var slot: raw/u8 = null
                var x = PlainR.init()
                x.p = slot@raw
                var o = PlainR.init()
                fReq(Bx.init(item: x@move), o@uniq, w@raw@raw/u8)
                usep(slot)
                y = 5
            """);
        Assert.Equal(
            [
                "idFormBad: kk = 2", "idFormBad: m@ref", "idFormInBad: kk = 2", "idAbsBad: kk = 2", "idEnclBad: z = 5",
                "idEnclBad: let host = Host.init(r@follow@raw)", "idEnclNoPremBad: z = 5",
                "idEnclNoPremBad: let host = Host.init(r@follow@raw)", "idRawStepBad: z = 5",
                "idRawStepBad: var q = u@follow@raw",
                "idFormGenBad: kk = 2", "retPlainWBad: y = 5", "retIdWBad: y = 5", "structIdBad: y = 5", "structPlainBad: y = 5",
                "nestIdBad: y0 = 5", "nestId2Bad: y0 = 5", "nestPlainBad: y0 = 5", "nestNoIdBad: y0 = 5", "nestOuterIdBad: y0 = 5",
                "rvNoIdReqOk: y = 5", "rvNoIdReqOk: var o = PlainR.init()",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 15.6.4 (route 1: "the holders its exclusive Loans and addresses name"; "adds no reach of its own"), 5.7 Calls (a raw layer
    // in a parameter, "a Type argument's ... included"; with an Origin, what the borrows it belongs to admit), the owner's O-312 (a)
    // ("the carried addresses are reached, hidden or not"; PLAN G935 c3-5, class 1): storage of the callee's own Type parameter takes
    // only the callee's terms' rows, but a holder that a raw layer of its binding addresses is reached, and the call may store there
    // what that holder admits. w, addressed by F's `raw/(raw/u8)` in a Tuple (hidPunUseEntryBad, the Middle's) or by U's in a struct's
    // Field (hidPunUnstatedWrapBad), may hold spare and is read after `kk = 2`; h, addressed by U's layer, may be replaced by wa, so
    // `h@follow = spare` stores into storage the body does not own (o312p2Bad, the owner's O-312 program 2); cell, addressed by T's
    // layer past `raw/T`, is read after the write (keepTCellUsedBad), and so it is where hop, an address of cell, is used after it
    // (keepTHopUsedBad: SPEC 15.6, a use of an address of a holder uses the holder; invalid by the owner's O-389 (a), which corrects
    // O-353 (a)'s line for it); w, whose plain address `t.0` carries, may hold z's borrow (hidAddrBad); and a layer stating an Origin
    // reaches no less: `*u`, which the caller states by `uniq`, may hold v and is read after `z = 5` (hidStatedUseBad). Fail-before
    // on 4b788f544: all seven were accepted (c2 kept the callee's Type past every layer its binding holds); fcd6a5381 rejected them.
    // The valid twins: a pointee that admits no pointer (hidPunU8PointeeOk), the holder read only before the write (hidPunEarlyOk,
    // keepTCellEarlyOk) or not at all (hidPunUnusedOk; keepTUnusedOk, the owner's O-353 (a)).
    [Fact]
    public void TheHoldersAKeptBindingAddressesAreReached()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct WrapH<E>
                var value: E
                public init(value: E) => self.value = value@move
            func usePtrH<T>(p: raw/T) => ()
            func usePPH(p: raw/(raw/u8)) => ()
            func touchH<F>(t: uniq/(F, u8), v: raw/u8) => ()
            func wrapH<U>(w: uniq/WrapH<U>, v: raw/u8) => ()
            func waH<U>(p: raw/U) => ()
            func keepTH<T>(slot: raw/T, value: raw/u8) => ()
            func putAnyH<U, V>(t: uniq/U, v: V) => ()
            func hidPunUseEntryBad()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var w: raw/u8 = null
                let z8: u8 = 0
                var t = (w@raw@raw/(raw/u8), z8)
                touchH(t@uniq, spare)
                kk = 2
                usePtrH(w)
            func hidPunEarlyOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var w: raw/u8 = null
                let z8: u8 = 0
                var t = (w@raw@raw/(raw/u8), z8)
                touchH(t@uniq, spare)
                usePtrH(w)
                kk = 2
            func hidPunUnusedOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var w: raw/u8 = null
                let z8: u8 = 0
                var t = (w@raw@raw/(raw/u8), z8)
                touchH(t@uniq, spare)
                kk = 2
            func hidPunU8PointeeOk()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var b: u8 = 0
                let z8: u8 = 0
                var t = (b@raw, z8)
                touchH(t@uniq, spare)
                kk = 2
                usePtrH(b@raw)
            func hidPunUnstatedWrapBad()
                var kk: u8 = 1
                let rk = kk@uniq
                var spare: raw/u8 = rk@follow@raw
                var w: raw/u8 = null
                var h = WrapH.init(w@raw@raw/(raw/u8))
                wrapH(h@uniq, spare)
                kk = 2
                usePtrH(w)
            func o312p2Bad()
                var kk: u8 = 1
                let r = kk@uniq
                var spare: raw/u8 = r@follow@raw
                var a: raw/u8 = null
                var b: raw/u8 = null
                var m = a@uniq
                var h = b@uniq
                var x: (raw/(uniq/(raw/u8)), raw/(raw/(raw/u8))) = (m@raw, h@raw@raw/(raw/(raw/u8)))
                waH(x@raw)
                h@follow = spare
                kk = 2
            func keepTCellUsedBad(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepTH(hop@raw, w@raw@raw/u8)
                y@follow = 5
                usePtrH(cell)
            func keepTCellEarlyOk(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepTH(hop@raw, w@raw@raw/u8)
                usePtrH(cell)
                y@follow = 5
            func keepTHopUsedBad(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepTH(hop@raw, w@raw@raw/u8)
                y@follow = 5
                usePPH(hop)
            func keepTUnusedOk(y: uniq/i32)
                let w = y@follow@ref
                var cell: raw/u8 = null
                var hop: raw/(raw/u8) = cell@raw
                keepTH(hop@raw, w@raw@raw/u8)
                y@follow = 5
            func hidAddrBad()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var t = (w@raw, 0)
                putAnyH(t@uniq, z@ref)
                z = 5
                inspect(w)
            func hidStatedUseBad(u: uniq/(raw/u8) during a)
                var z = 3
                let w = z@ref
                var h = WrapH.init(u@follow@raw)
                wrapH(h@uniq, w@raw@raw/u8)
                z = 5
                usePtrH(u@follow)
            """);
        Assert.Equal(
            [
                "hidPunUseEntryBad: kk = 2", "hidPunUnstatedWrapBad: kk = 2", "o312p2Bad: kk = 2", "keepTCellUsedBad: y@follow = 5",
                "keepTHopUsedBad: y@follow = 5", "hidAddrBad: z = 5", "hidStatedUseBad: z = 5",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 15.6.4 (DECISIONS R4-M170, the owner's Q11 ruling 「この方針で」): a tied abstract value retains every raw layer an input holds
    // by value through no shared layer, an Option payload and a Tuple element included, so r's setQ may store w's address where m points,
    // storage the body does not own, and `y = 5` and S's end err (as tiedRawBad does with a raw input; tiedOk, whose r holds only the
    // shared Loan on s, is valid). FAIL-FIRST: S1′ alone accepts tiedOptBad and tiedTupBad (Binding's retention keeps a raw-typed input
    // only, BU-66 rule 3); BU's raw-sources Binding unit closes them, and S1′ lands with or after it.
    [Theory]
    [InlineData("tiedOptBad", "r.absorb(.Some(m))")]
    [InlineData("tiedTupBad", "r.absorbT((m, 1))")]
    [InlineData("tiedRawBad", "r.absorbR(m)")]
    [InlineData("tiedOk", "")]
    public void ATiedAbstractValueRetainsARawLayerAnInputHoldsByValue(string name, string absorb)
    {
        var c = MirBuildTest.Compile(Prelude + """
            contract Mk
                associate R(source) is Abs for ref/Self during source
                func opn(self: ref/Self during source) -> Self.R(source)
            contract Abs
                func absorb(self: uniq/Self, v: Option<raw/u8>)
                func absorbR(self: uniq/Self, v: raw/u8)
                func absorbT(self: uniq/Self, v: (raw/u8, i32))
                func setQ(self: uniq/Self, q: raw/u8)
            func consume<T>(x: T) => ()

            """ + $"func {name}<S>(s: S)\n    S is Mk\n    var y: i32 = 1\n    var w: ref/i32 = y@ref\n" +
            (absorb.Length > 0 ? $"    var m: raw/u8 = Raw.allocate<u8>(1)\n    var r = s.opn()\n    {absorb}\n" : "    var r = s.opn()\n") +
            "    r.setQ(w@raw@raw/u8)\n    consume(r@move)\n    y = 5\n");
        Assert.Equal(
            name == "tiedOk" ? [] : [$"{name}: y = 5", $"{name}: S"],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // The same retention per raw source (DECISIONS BU-66, A2; the owner's 「A2 を Chain の案でGO」): a raw layer past an exclusive borrow
    // keeps what a chain of reads through the input carries, never the borrow's own Loan, so tiedExclBad errs at `y = 5` and S's end as
    // tiedRawBad does, while tiedExclOk, tiedExclOk2 and writerExclOk, which keep the borrowed `m` past the call, are valid (fail before: `r`,
    // `m = …` and `var m`). A by-value input keeps its own value whole (R4-M154's grain, the owner's precision line, which accepts the
    // over-approximation): writerPairOk's `t` keeps the `ref` Loan on `k` beside the raw layer, the accepted false error at `var k`.
    [Theory]
    [InlineData("tiedExclBad", "<S>(s: S)\n    S is Mk\n", "var y: i32 = 1\nvar w: ref/i32 = y@ref\nvar m: raw/u8 = Raw.allocate<u8>(1)\nvar r = s.opn()\nr.absorbX(m@uniq)\nr.setQ(w@raw@raw/u8)\nconsume(r@move)\ny = 5", "tiedExclBad: y = 5|tiedExclBad: S")]
    [InlineData("tiedExclOk", "<S>(s: S)\n    S is Mk\n", "var m: raw/u8 = Raw.allocate<u8>(1)\nvar r = s.opn()\nr.absorbX(m@uniq)\nconsume(r@move)", "")]
    [InlineData("tiedExclOk2", "<S>(s: S)\n    S is Mk\n", "var m: raw/u8 = Raw.allocate<u8>(1)\nvar r = s.opn()\nr.absorbX(m@uniq)\nm = Raw.allocate<u8>(1)\nconsume(r@move)", "")]
    [InlineData("writerExclOk", "<T>(t: uniq/T)\n    T is Abs\n", "var m: raw/u8 = Raw.allocate<u8>(1)\nt.absorbX(m@uniq)", "")]
    [InlineData("writerPairOk", "<T>(t: uniq/T)\n    T is Abs\n", "var k: i32 = 1\nvar m: raw/u8 = Raw.allocate<u8>(1)\nt.absorbP((k@ref, m))", "writerPairOk: var k: i32 = 1")]
    public void ATiedAbstractValueRetainsARawLayerPastAnExclusiveBorrowByItsChain(string name, string head, string body, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + """
            contract Mk
                associate R(source) is Abs for ref/Self during source
                func opn(self: ref/Self during source) -> Self.R(source)
            contract Abs
                func absorbX(self: uniq/Self, v: uniq/(raw/u8))
                func absorbP(self: uniq/Self, v: (ref/i32 during a, raw/u8))
                func setQ(self: uniq/Self, q: raw/u8)
            func consume<T>(x: T) => ()

            """ + $"func {name}{head}" + string.Concat(body.Split('\n').Select(static x => $"    {x}\n")));
        Assert.Equal(
            expected.Length == 0 ? [] : expected.Split('|'),
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 5.7 Calls, 15.6.4 (DECISIONS R4-M147; the owner's Q2 (a) and 「進める (推奨)」): a stored raw layer is read by its step on the
    // instance holding it. `during self` admits the instance's Type arguments by their actual Types, so a bag erasing its element to
    // raw/u8 writes past itself as its typed twin does (erased, typed), and the write fact where the instance has an Origin argument (P1:
    // keepPhantom's `y = 5`, which no part names; its `inspect(w)` errs on dev too); an Origin-less layer takes the write fact unless the
    // parameter's Type expression writes it (SPEC 5.7 Calls' per-parameter rule, P2: rawBufDead, whose store reaches E, which may be used
    // after every point). Dev held erased and accepted the P1 and P2 records. A layer written in a Type argument of the declaration's own
    // Field or Case payload is the declaration's, which its kept part states (nestedDead, optionalDead; DECISIONS BU-46, R4-M151).
    [Fact]
    public void AStoredRawLayerIsReadByItsStep()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct ErasedBag<T>
                var data: raw/u8 during self
                public init() => self.data = null
                public func put(self: uniq/Self, value: T) => ()
            struct TypedBag<T>
                var data: raw/T during self
                public init() => self.data = null
                public func put(self: uniq/Self, value: T) => ()
            struct KeepPhantom {o}
                var data: raw/u8 during self
                public init(anchor: ref/i32 during o) => self.data = null
                public func aim(self: uniq/Self, anchor: uniq/(ref/i32 during o) during o) => ()
                public func put(self: uniq/Self, value: ref/i32 during o) => ()
            struct RawBuf
                var data: raw/u8
                public init() => self.data = null
                public func put<T>(self: uniq/Self, value: T) => ()
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
            struct Holder2
                var b: Wrap<raw/u8>
                public init() => self.b = Wrap<raw/u8>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HoldOpt
                var o: Option<raw/u8>
                public init() => self.o = .None
                public func put<T>(self: uniq/Self, value: T) => ()
            func erased()
                var x = 1
                var y = 2
                var z = 3
                var w1: ref/i32 = x@ref
                var b = ErasedBag<(Option<uniq/(ref/i32)>, ref/i32)>.init()
                b.put((.Some(w1@uniq), y@ref))
                b.put((.None, z@ref))
                z = 5
                inspect(w1)
            func typed()
                var x = 1
                var y = 2
                var z = 3
                var w1: ref/i32 = x@ref
                var b = TypedBag<(Option<uniq/(ref/i32)>, ref/i32)>.init()
                b.put((.Some(w1@uniq), y@ref))
                b.put((.None, z@ref))
                z = 5
                inspect(w1)
            func keepPhantom()
                var x = 1
                var y = 2
                var w: ref/i32 = x@ref
                var k = KeepPhantom.init(x@ref)
                k.aim(w@uniq)
                k.put(y@ref)
                y = 5
                inspect(w)
            func rawBufDead()
                var z = 3
                var b = RawBuf.init()
                b.put(z@ref)
                z = 5
            func nestedDead()
                var z = 3
                var h = Holder2.init()
                h.put(z@ref)
                z = 5
            func optionalDead()
                var z = 3
                var h = HoldOpt.init()
                h.put(z@ref)
                z = 5
            """);
        Assert.Equal(
            [
                "erased: z = 5", "erased: inspect(w1)", "erased: var w1: ref/i32 = x@ref", "erased: var y = 2", "typed: z = 5", "typed: inspect(w1)",
                "typed: var w1: ref/i32 = x@ref", "typed: var y = 2", "keepPhantom: y = 5", "keepPhantom: inspect(w)", "rawBufDead: z = 5",
                "nestedDead: z = 5", "optionalDead: z = 5",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 5.7 Calls (DECISIONS BU-46, R4-M151): a raw layer without an Origin that a declaration writes at any depth below the holder gives
    // the write fact, as the holder's own layer does (direct), so a call storing a borrow in the holder keeps its Loan in E and `z = 5`
    // errs: beside a supplied argument (pairM), in a user enum's payload (enumHeld), a Tuple in a Type argument (tupleWrap), a nested holder
    // (deeper), a pair slot's argument (pairSlot, also stated `static`, hpst) and its application `s/U` (hpu), and outside a stored part,
    // an Array element of a Field (hold2) and a `during self` pointee's content (selfBuf); where the parameter's Type expression writes it
    // over a Loan-free pointee, nothing past it, so the store holds no Loan (supplied, pairM2, pu, wrap).
    [Fact]
    public void ALayerADeclarationWritesGivesTheWriteFact()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct RawBuf
                var data: raw/u8
                public init() => self.data = null
                public func put<T>(self: uniq/Self, value: T) => ()
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
                public func put<T>(self: uniq/Self, value: T) => ()
            struct Outer<P>
                var w: Wrap<P>
                public init(w: Wrap<P>) => self.w = w@move
                public func put<T>(self: uniq/Self, value: T) => ()
            struct Holder2
                var b: Wrap<raw/u8>
                public init() => self.b = Wrap<raw/u8>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct Two<A, B>
                var a: A
                var b: B
                public init(a: A, b: B)
                    self.a = a@move
                    self.b = b@move
            struct M<P>
                var w: Two<raw/u8, P>
                public init(w: Two<raw/u8, P>) => self.w = w@move
                public func put<T>(self: uniq/Self, value: T) => ()
            struct M2<P>
                var w: Two<P, P>
                public init(w: Two<P, P>) => self.w = w@move
                public func put<T>(self: uniq/Self, value: T) => ()
            enum MyOpt<T>
                Has(T)
                Not
            struct HoldEnum
                var e: MyOpt<raw/u8>
                public init() => self.e = .Not
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HoldTW
                var t: Wrap<(raw/u8, i32)>
                public init() => self.t = Wrap<(raw/u8, i32)>.init((null, 0))
                public func put<T>(self: uniq/Self, value: T) => ()
            struct H3
                var h: Holder2
                public init() => self.h = Holder2.init()
                public func put<T>(self: uniq/Self, value: T) => ()
            struct PS<s/T>
                var t: s/T
                public init(t: s/T) => self.t = t@move
                public func put<V>(self: uniq/Self, value: V) => ()
            struct HPSo
                var p: PS<raw/u8>
                public init() => self.p = PS<raw/u8>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HPSt
                var p: PS<raw/u8 during static>
                public init() => self.p = PS<raw/u8>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct PU<s/T>
                s is raw or owner
                var v: s/i32
                public init(v: s/i32) => self.v = v@move
                public func put<V>(self: uniq/Self, value: V) => ()
            struct HPU
                var p: PU<raw/u8>
                public init() => self.p = PU<raw/u8>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct Hold2
                var items: Array<raw/u8>
                public init() => self.items = [null]
                public func put<T>(self: uniq/Self, value: T) => ()
            struct SB
                var data: raw/(raw/u8) during self
                public init() => self.data = null
                public func put<T>(self: uniq/Self, value: T) => ()
            func direct()
                var z = 3
                var h = RawBuf.init()
                h.put(z@ref)
                z = 5
            func pairM()
                var z = 3
                var h = M<raw/u8>.init(Two<raw/u8, raw/u8>.init(null, null))
                h.put(z@ref)
                z = 5
            func enumHeld()
                var z = 3
                var h = HoldEnum.init()
                h.put(z@ref)
                z = 5
            func tupleWrap()
                var z = 3
                var h = HoldTW.init()
                h.put(z@ref)
                z = 5
            func deeper()
                var z = 3
                var h = H3.init()
                h.put(z@ref)
                z = 5
            func pairSlot()
                var z = 3
                var h = HPSo.init()
                h.put(z@ref)
                z = 5
            func hpst()
                var z = 3
                var h = HPSt.init()
                h.put(z@ref)
                z = 5
            func hpu()
                var z = 3
                var h = HPU.init()
                h.put(z@ref)
                z = 5
            func hold2()
                var z = 3
                var h = Hold2.init()
                h.put(z@ref)
                z = 5
            func selfBuf()
                var z = 3
                var h = SB.init()
                h.put(z@ref)
                z = 5
            func supplied()
                var z = 3
                var h = Outer<raw/u8>.init(Wrap<raw/u8>.init(null))
                h.put(z@ref)
                z = 5
            func pairM2()
                var z = 3
                var h = M2<raw/u8>.init(Two<raw/u8, raw/u8>.init(null, null))
                h.put(z@ref)
                z = 5
            func pu()
                var z = 3
                var h = PU<raw/u8>.init(null)
                h.put(z@ref)
                z = 5
            func wrap()
                var z = 3
                var h = Wrap<raw/u8>.init(null)
                h.put(z@ref)
                z = 5
            """);
        Assert.Equal(
            [
                "direct: z = 5", "pairM: z = 5", "enumHeld: z = 5", "tupleWrap: z = 5", "deeper: z = 5", "pairSlot: z = 5", "hpst: z = 5", "hpu: z = 5",
                "hold2: z = 5", "selfBuf: z = 5",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 5.7 Calls, 8.6.1, 8.2.3 (DECISIONS BU-50, PLAN G271): an instance's kept part completes an associated projection by its
    // subject's conformance, so `S.Item` as `HA<Src>`, Src binding `Item` to `raw/u8`, is a layer Src's declaration writes and gives the
    // write fact, as the written twin HSrc's `Src.Item` does: `z = 5` errs (ha), also below a Type argument (ha2), a held holder (hha), an
    // inherited binding (inh: a base clause's argument), a base clause naming the layer through a projection (derq: `BaseI<Src2<raw/u8>.
    // Item>` is `BaseI<raw/u8>`, completed before the mark) and a store into the Field such a base clause holds (derf, as its twin derg,
    // each a moved parameter), a holder whose declaration writes the subject's argument (hh2, also through a condition, hhc), a nested
    // binding (nest: `Item is S.Item`) and an enum subject (esrc). An enum holder's Case payload completes as a Field does, so `EH<Src>`'s
    // verdict is its written twin ER's (eh, er: `z = 5` errs, PLAN G841's rule 4 (4), the owner's O-373 (a): through A's payload layer,
    // which the declaration states, `put` may store what a raw layer it writes retains, 5.7 Calls). Where the parameter's Type expression writes the subject's
    // argument and the binding is that argument (`Item is T`), the layer is the parameter's, so the store holds no Loan, as for its twin
    // Wrap<raw/u8> (src2, hpair2, nest2, hc, eh2). The old path, which read AdtDef's view, gave each form its twin's record (none: it
    // accepted the stated forms, DECISIONS BU-46) until R6-check H10b deleted it.
    [Fact]
    public void ACompletedProjectionGivesItsSubjectsLayer()
    {
        const string Declarations = """
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
                public func put<T>(self: uniq/Self, value: T) => ()
            contract HasItem
                associate Item
            struct Src
                Self is HasItem
                associate HasItem.Item is raw/u8
            struct Src2<T>
                Self is HasItem
                associate HasItem.Item is T
            open struct BaseI<P>
                Self is HasItem
                associate HasItem.Item is P
            struct DerI : BaseI<raw/u8>
                var m: i32
                public init() => self.m = 0
            struct DerQ : BaseI<Src2<raw/u8>.Item>
                var m: i32
                public init() => self.m = 0
            open struct BaseF<P>
                public var f: P
            struct DerF : BaseF<Src2<raw/u8>.Item>
                var m: i32
                public func put<T>(self: uniq/Self, value: T) => ()
            struct DerG : BaseF<raw/u8>
                var m: i32
                public func put<T>(self: uniq/Self, value: T) => ()
            func derf(d: DerF)
                var z = 3
                var h = d@move
                h.put(z@ref)
                z = 5
            func derg(d: DerG)
                var z = 3
                var h = d@move
                h.put(z@ref)
                z = 5
            struct Src3<S>
                S is HasItem
                Self is HasItem
                associate HasItem.Item is S.Item
            struct SrcC<T>
                Self is HasItem when T is Copy
                    associate HasItem.Item is T
            enum ESrc
                Self is HasItem
                associate HasItem.Item is raw/u8
                A
            enum EH<S>
                S is HasItem
                A(S.Item)
                B
                public func put<T>(self: uniq/Self, value: T) => ()
            enum ER
                A(raw/u8)
                B
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HA<S>
                S is HasItem
                var x: S.Item
                public init(x: S.Item) => self.x = x@move
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HA2<S>
                S is HasItem
                var w: Wrap<S.Item>
                public init(w: Wrap<S.Item>) => self.w = w@move
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HHA
                var h: HA<Src>
                public init() => self.h = HA<Src>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HH2
                var h: HA<Src2<raw/u8>>
                public init() => self.h = HA<Src2<raw/u8>>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HHC
                var h: HA<SrcC<raw/u8>>
                public init() => self.h = HA<SrcC<raw/u8>>.init(null)
                public func put<T>(self: uniq/Self, value: T) => ()
            struct HSrc
                var x: Src.Item
                public init() => self.x = null
                public func put<T>(self: uniq/Self, value: T) => ()

            """;
        (string Name, string Holder, string Twin)[] forms =
        [
            ("ha", " = HA<Src>.init(null)", "hsrc"), ("ha2", " = HA2<Src>.init(Wrap<Src.Item>.init(null))", "hsrc"), ("hha", " = HHA.init()", "hsrc"),
            ("inh", " = HA<DerI>.init(null)", "hsrc"), ("derq", " = HA<DerQ>.init(null)", "hsrc"), ("hh2", " = HH2.init()", "hsrc"),
            ("hhc", " = HHC.init()", "hsrc"),
            ("nest", " = HA<Src3<Src>>.init(null)", "hsrc"), ("esrc", " = HA<ESrc>.init(null)", "hsrc"), ("eh", ": EH<Src> = .A(null)", "er"), ("er", ": ER = .A(null)", "er"),
            ("hsrc", " = HSrc.init()", "hsrc"), ("src2", " = HA<Src2<raw/u8>>.init(null)", "wrap"),
            ("hpair2", " = HA2<Src2<raw/u8>>.init(Wrap<Src2<raw/u8>.Item>.init(null))", "wrap"), ("nest2", " = HA<Src3<Src2<raw/u8>>>.init(null)", "wrap"),
            ("hc", " = HA<SrcC<raw/u8>>.init(null)", "wrap"), ("eh2", ": EH<Src2<raw/u8>> = .A(null)", "wrap"), ("wrap", " = Wrap<raw/u8>.init(null)", "wrap"),
        ];
        var source = Declarations + string.Concat(forms.Select(static x => $"func {x.Name}()\n    var z = 3\n    var h{x.Holder}\n" +
            "    h.put(z@ref)\n    z = 5\n"));
        var c = MirBuildTest.Compile(Prelude + source);
        Assert.Equal(
            [
                "derf: z = 5", "derg: z = 5", "ha: z = 5", "ha2: z = 5", "hha: z = 5", "inh: z = 5", "derq: z = 5", "hh2: z = 5", "hhc: z = 5",
                "nest: z = 5", "esrc: z = 5", "eh: z = 5", "er: z = 5", "hsrc: z = 5",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
        var old = MinimalEmissionTest.Analyze(source);
        var records = old.Problems.Select(x => (Function: Function(old.SyntaxOf(x.Subject)), x.Code)).ToArray();
        Assert.All(forms.Where(static x => x.Name != x.Twin), x => Assert.Equal(Of(x.Twin), Of(x.Name)));

        DiagnosticCode[] Of(string function) => [.. records.Where(x => x.Function == function).Select(static x => x.Code)];
    }

    // SPEC 8.6.1, 8.3.3 (DECISIONS BU-50's decision 10, PLAN G271): a projection over a subject with an abstract part stays pending in the
    // generic declaration's storage, and the instance a use forms under its own premise completes it, so a generic body holding one has
    // no problem: `HAd<Wrapping<T>>` under `T is PrimitiveInteger` returned (wrapRet), in a local Tuple (wrapLocal), in a Field
    // (wrapField) and as an enum's payload (wrapPayload), a conditional conformance proven only by the use's premise (premise),
    // `HK<Range<T, T>>` (range) and `HK<[2 of T]>` (fixedArray), each with its closed use. Before (ca97a317e's final none), the generic
    // declaration's storage was Rejected and A4's retention over it gave its limit verdict, Unsupported_Kd at the generic function's
    // signature, its local, the init and the return: a problem of the whole MIR analysis, which Binding's issues, the program's findings,
    // the collector's records and the Loan stratum alone never observe. The old path accepted each too (deleted at H10b).
    [Fact]
    public void APendingProjectionIsCompletedByTheInstanceAUseForms()
    {
        const string Source = """
            contract HasItem
                associate Item
            struct SrcC<T>
                Self is HasItem when T is Copy
                    associate HasItem.Item is T
            struct HAd<S>
                S is Addable<S>
                public var x: S.Output
                public init(x: S.Output) => self.x = x@move
            struct HP<S>
                S is HasItem
                public var x: S.Item
                public init(x: S.Item) => self.x = x@move
            struct HK<S>
                S is IntoIterable
                public var it: S.IteratorType
                public init(s: S) => self.it = (s@move).intoIterator()
            struct Outer<T>
                T is PrimitiveInteger
                public var h: HAd<Wrapping<T>>
                public init(a: Wrapping<T>) => self.h = HAd<Wrapping<T>>.init(x: a)
            enum EW<S>
                S is Addable<S>
                A(S.Output)
                B
            func wrapRet<T>(a: Wrapping<T>) -> HAd<Wrapping<T>>
                T is PrimitiveInteger
                let h = HAd<Wrapping<T>>.init(x: a + a)
                return h@move
            func wrapLocal<T>(a: Wrapping<T>) -> ()
                T is PrimitiveInteger
                let h = HAd<Wrapping<T>>.init(x: a + a)
                let p = (h@move, 1)
                return
            func wrapPayload<T>(a: Wrapping<T>) -> ()
                T is PrimitiveInteger
                let e: EW<Wrapping<T>> = .B
                return
            func premise<T>(u: T) -> HP<SrcC<T>>
                T is Copy
                let h = HP<SrcC<T>>.init(x: u)
                return h@move
            func range<T>(r: Range<T, T>) -> HK<Range<T, T>>
                T is PrimitiveInteger
                let h = HK<Range<T, T>>.init(s: r)
                return h@move
            func fixedArray<T>(a: [2 of T]) -> HK<[2 of T]>
                T is Copy
                let h = HK<[2 of T]>.init(s: a)
                return h@move
            func uses() -> ()
                let v = wrapRet(3@Wrapping<i32>)
                let w = v.x
                wrapLocal(3@Wrapping<i32>)
                let o = Outer<i32>.init(a: 3@Wrapping<i32>)
                let f = o.h.x
                wrapPayload(3@Wrapping<i32>)
                let p = premise(1)
                let q = p.x
                var r = range(0..3@i32)
                let x = r.it.next()
                let a: [2 of i32] = [1, 2]
                var k = fixedArray(a)
                let y = k.it.next()
                return
            public func main() -> () => ()

            """;
        Assert.Empty(Records(MinimalEmissionTest.Analyze(Source)));

        // The analysis's records: the function and the code of each.
        static string[] Records(Compilation c) => [.. c.Problems.Select(x => $"{Function(c.SyntaxOf(x.Subject))}: {x.Code}")];
    }

    // SPEC 5.7 Origins, Authority, Calls, 15.6.4 item 1 (PLAN G148 (1), R7 plan Z6; DECISIONS R4-M177): through a raw layer stating an
    // Origin a call may store what the Referent Type of each borrow that Origin belongs to admits (Binding.AtomMembers: a body atom's
    // published Borrows, an input's or a slot's by its declaration, a local region's by the local's own borrow Type), and only by an
    // exclusive Loan of it (W skips holders only a shared Loan names), whether the instance's declaration states the layer or a Type
    // argument supplies it; the pointee is no second admission. So the layer over w's exclusive borrow lets the call store z's borrow into
    // w, used after (`z = 5`, `inspect(w)`): a Field (flat, reborrow through rr), a nested instance's Field (nested), a Case payload
    // (optionNested), each call in a loop beside an independent conflict (looped: `k = 2`), a written `during r` local (duringUniq), one
    // whose value is null (duringNull: by the letter, the Origin decides), a part beside a shared one (twoUniq: v), a Tuple's or a struct's
    // part (tuplePartD, fieldPartD: b) and a Field beside a static raw part (twoStatic, which also takes storage-end records at y, w and
    // x: dependent records on a program rejected anyway, DECISIONS Q13; F1's open issue); the same over a shared borrow, a borrow of an
    // i32, or a referent that holds no Loan stores nothing: sharedSupplied, paramShared, paramUniq (an input's referent
    // `ref/i32 during a` admits no local borrow), methodShared, methodUniq, ctorShared, i32Referent, loopShared, twoShared, tuplePart and
    // fieldPart (a part's referent i32), slotPart and slotKept (a slot's keeper's referent i32), paramSlot and t11 (one root, two part
    // borrows); a constructor lending the holder (reachedByStep) stores z's borrow, but nothing holding it is used after `z = 5` (SPEC 15.4;
    // Z6's O5-4). An address taken past the shared s whose atom also has an exclusive borrow keeps the located Unsupported_Kd (mixedAtom,
    // t11s, valid: Z6's named limit R6.1, R6.8 (b2); removal: route (a)), as does duringShared (valid): `during s` names s's own region,
    // which Binding states by another atom than s's borrow, so the written initializer is a fit from another Origin (FittedIntoHolder,
    // the whitelist's cost; removal: D-1116 (1)'s one atom). A claim states its atom; an address
    // carries no mode, so a claimed atom with an exclusive member stays the located Unsupported_Kd (claimUniq, invalid; removal: per-layer
    // address provenance in A5), and one whose borrows are all shared stores nothing (claimShared, claimMixed, claimRaw, claimRawMixed: a
    // later exclusive borrow of w is P[w]'s, not s's region's; claimInput). A raw layer's region slot in a `var` local assigned from two
    // borrows (regionRaw, invalid) stays the located Unsupported_Kd (R7 plan Z6b). Unchanged: a typed pointee decides (nestedPointee;
    // touched, valid), as does an exclusive part beside the layer (mixed errs). A raw layer outside an instance reads its pointee (R4-M120):
    // tupleW is accepted, its typed twin errs. Where a call's pair holds (DECISIONS R4-M154, Revision 4), a callee its instantiated
    // signature admits may store z's borrow into w or v through the instance's uniq or raw layers, whose local regions may coincide, so
    // nestedPointee and mixed also err at `z = 5`. tupleTyped's `putAny<U, V>(t: uniq/U, v: V)` errs there too: `t.0` carries w's address
    // by a raw layer of U's binding (SPEC 15.6.4; the owner's O-312 (a); PLAN G935 c3-5). FAIL-FIRST: on the base every row above that now
    // errs or is accepted was the located Unsupported_Kd at the call (sharedSupplied's the Limit's cost), claimRaw and claimRawMixed a false
    // `z = 5`. Labels: Z6's design tables (p01-p20, t01-t11, n01-n04, t08*).
    [Fact]
    public void AStoreThroughARawLayerReadsItsOriginsBorrows()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
                public func put<U>(self: uniq/Self, v: U) => ()
                public func touch(self: uniq/Self) => ()
            struct Two<E, F>
                var a: E
                var b: F
                public init(a: E, b: F)
                    self.a = a@move
                    self.b = b@move
                public func put<U>(self: uniq/Self, v: U) => ()
            struct FG {a}
                public var f: i32
                public var g: ref/i32 during a
                public init(f: i32, g: ref/i32 during a)
                    self.f = f
                    self.g = g
            struct HSlot {s}
                var b: Wrap<raw/u8 during s>
                var k: uniq/i32 during s
                public init(k: uniq/i32 during s)
                    self.b = Wrap.init(k@follow@raw@raw/u8)
                    self.k = k
                public func put<U>(self: uniq/Self, v: U) => ()
            struct BoxS {a}
                public var w: ref/i32 during a
                public init(w: ref/i32 during a) => self.w = w
                public func methodShared(self: ref/Self)
                    var z = 3
                    var h = Wrap.init(self.w@raw@raw/u8)
                    h.put(z@ref)
                    z = 5
            struct BoxU {a}
                public var w: ref/i32 during a
                public init(w: ref/i32 during a) => self.w = w
                public func methodUniq(self: uniq/Self)
                    var z = 3
                    var h = Wrap.init(self.w@raw@raw/u8)
                    h.put(z@ref)
                    z = 5
            struct BoxK {a}
                public var w: uniq/i32 during a
                public init(w: uniq/i32 during a) => self.w = w
                public func slotKept(self: uniq/Self)
                    var z = 3
                    var h = Wrap.init(self.w@follow@raw@raw/u8)
                    h.put(z@ref)
                    z = 5
            struct Outer<P>
                var w: Wrap<P>
                public init(w: Wrap<P>) => self.w = w@move
                public func put<U>(self: uniq/Self, v: U) => ()
            struct Mixed<E> {a}
                var value: E
                var u: uniq/(ref/i32 during a) during a
                public init(value: E, u: uniq/(ref/i32 during a) during a)
                    self.value = value@move
                    self.u = u
                public func put<U>(self: uniq/Self, v: U) => ()
            struct G<E> {a}
                var r: uniq/(Wrap<E>) during a
                public init(r: uniq/(Wrap<E>) during a) => self.r = r
                public func put<U>(self: uniq/Self, v: U) => ()
            func putAny<U, V>(t: uniq/U, v: V) => ()
            func flat()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var h = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func nested()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var o = Outer.init(Wrap.init(r@follow@raw@raw/u8))
                o.put(z@ref)
                z = 5
                inspect(w)
            func optionNested()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var o = Option.Some(Wrap.init(r@follow@raw@raw/u8))
                putAny(o@uniq, z@ref)
                z = 5
                inspect(w)
            func looped(n: i32)
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var h = Wrap.init(r@follow@raw@raw/u8)
                var k = 1
                let rk = k@ref
                var i = 0
                while i < n
                    h.put(z@ref)
                    h.touch()
                    i += 1
                k = 2
                inspect(rk)
                z = 5
                inspect(w)
            func reachedByStep()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var h = Wrap.init(r@follow@raw@raw/u8)
                var g = G.init(h@uniq)
                g.put(z@ref)
                z = 5
            func sharedSupplied()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var h = Wrap.init(s@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func nestedPointee()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var o = Outer.init(Wrap.init(r@follow@raw))
                o.put(z@ref)
                z = 5
                inspect(w)
            func touched()
                var x = 1
                var w: ref/i32 = x@ref
                var r = w@uniq
                var h = Wrap.init(r@follow@raw)
                h.touch()
            func mixed()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                var r = w@uniq
                var m = Mixed.init(r@follow@raw@raw/u8, v@uniq)
                m.put(z@ref)
                z = 5
                inspect(v)
            func tupleW()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var t = (r@follow@raw@raw/u8, 0)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func tupleTyped()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var t = (r@follow@raw, 0)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func mixedAtom()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var h = Wrap.init(s@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
                var u = w@uniq
                u@follow = y@ref
                inspect(w)
            func duringShared()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                let q: raw/u8 during s = s@follow@raw@raw/u8
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            func duringUniq()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                let q: raw/u8 during r = r@follow@raw@raw/u8
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            func duringNull()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                let q: raw/u8 during r = null
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            func paramShared(y: ref/(ref/i32 during a))
                var z = 3
                var h = Wrap.init(y@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
            func paramUniq(y: uniq/(ref/i32 during a))
                var z = 3
                var h = Wrap.init(y@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
            func paramSlot(y: ref/(uniq/i32 during a))
                var z = 3
                var h = Wrap.init(y@follow@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
            func ctorShared()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var h = Wrap.init(s@follow@raw@raw/u8)
                var g = G.init(h@uniq)
                g.put(z@ref)
                z = 5
                inspect(w)
            func i32Referent()
                var x = 1
                var z = 3
                var r = x@uniq
                var h = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(x)
            func loopShared(n: i32)
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var h = Wrap.init(s@follow@raw@raw/u8)
                var i = 0
                while i < n
                    h.put(z@ref)
                    i += 1
                z = 5
                inspect(w)
            func twoShared()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var r = v@uniq
                var h = Two.init(s@follow@raw@raw/u8, r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func twoUniq()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var r = v@uniq
                var h = Two.init(s@follow@raw@raw/u8, r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func twoStatic()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var y: raw/u8 during static = null
                var r = w@uniq
                var h = Two.init(r@follow@raw@raw/u8, y@uniq)
                h.put(z@ref)
                z = 5
                inspect(w)
            func reborrow()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var rr = r@follow@uniq
                var h = Wrap.init(rr@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func tuplePart()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var b = (0, w)
                var r = b.0@uniq
                var h = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(b)
            func tuplePartD()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var b = (0, w)
                var r = b.1@uniq
                var h = Wrap.init(r@follow@raw)
                h.put(z@ref)
                z = 5
                inspect(b)
            func fieldPart()
                var x = 1
                var z = 3
                var b = FG.init(0, x@ref)
                var r = b.f@uniq
                var h = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(b)
            func fieldPartD()
                var x = 1
                var z = 3
                var b = FG.init(0, x@ref)
                var r = b.g@uniq
                var h = Wrap.init(r@follow@raw)
                h.put(z@ref)
                z = 5
                inspect(b)
            func slotPart()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var t = (0, w)
                var hs = HSlot.init(t.0@uniq)
                hs.put(z@ref)
                z = 5
                inspect(t)
            func t11()
                var y = 2
                var z = 3
                var b = (1, y@ref)
                var r = b.0@uniq
                var h = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                var u = b.1@uniq
                u@follow = y@ref
                z = 5
                inspect(b)
            func t11s()
                var y = 2
                var z = 3
                var b = (1, y@ref)
                let s = b.0@ref
                var h = Wrap.init(s@follow@raw@raw/u8)
                h.put(z@ref)
                var u = b.1@uniq
                u@follow = y@ref
                z = 5
                inspect(b)
            func claimShared()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var q: raw/u8 during s = null
                unsafe => q = w@raw@(raw/u8 during s)
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            func claimMixed()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var q: raw/u8 during s = null
                unsafe => q = w@raw@(raw/u8 during s)
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
                var u = w@uniq
                u@follow = y@ref
                inspect(w)
            func claimRaw()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var q: raw/(raw/u8) during s = null
                unsafe => q = w@raw@(raw/(raw/u8) during s)
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            func claimRawMixed()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var q: raw/(raw/u8) during s = null
                unsafe => q = w@raw@(raw/(raw/u8) during s)
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
                var u = w@uniq
                u@follow = y@ref
                inspect(w)
            func claimUniq()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var q: raw/u8 during r = null
                unsafe => q = w@raw@(raw/u8 during r)
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            func claimInput(y: ref/(ref/i32 during a))
                var c: ref/i32 = y@follow
                var z = 3
                var q: raw/u8 during y = null
                unsafe => q = c@raw@(raw/u8 during y)
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(c)
            func regionRaw(c: bool)
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                var r = w@uniq
                var u = v@uniq
                var q = r@follow@raw@raw/u8
                if c => q = u@follow@raw@raw/u8
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            """);
        Assert.Equal(
            [
                "flat: z = 5", "flat: inspect(w)", "nested: z = 5", "nested: inspect(w)", "optionNested: z = 5",
                "optionNested: inspect(w)", "looped: k = 2", "looped: z = 5", "looped: inspect(w)",
                "nestedPointee: z = 5", "nestedPointee: inspect(w)", "mixed: z = 5", "mixed: inspect(v)", "tupleTyped: z = 5",
                "tupleTyped: inspect(w)", "mixedAtom: h.put(z@ref) Unsupported_Kd", "duringShared: h.put(z@ref) Unsupported_Kd", "duringUniq: z = 5",
                "duringUniq: inspect(w)", "duringNull: z = 5", "twoUniq: z = 5", "twoUniq: inspect(v)", "twoStatic: z = 5", "twoStatic: inspect(w)",
                "twoStatic: var y: raw/u8 during static = null", "twoStatic: var w: ref/i32 = x@ref", "twoStatic: var x = 1", "reborrow: z = 5",
                "reborrow: inspect(w)", "tuplePartD: z = 5", "tuplePartD: b", "fieldPartD: z = 5", "fieldPartD: b",
                "t11s: h.put(z@ref) Unsupported_Kd",
                "claimUniq: h.put(z@ref) Unsupported_Kd", "regionRaw: h.put(z@ref) Unsupported_Kd",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 15.6.4 item 1, 5.2.1 condition 3 (DECISIONS R4-M177, PLAN G474): a call's store writes no holder that only a shared Loan names,
    // at its first step, past a holder or at a Deep step (MirBorrowsTest.DepthLimit). s = w@ref is a body-local shared borrow, so what is
    // reached through it is read-only and z = 5 conflicts with nothing: a raw layer past it in a Tuple (ss8) or an instance (ss8i), s
    // itself beside an exclusive part whose holder is not read after (ss8s, ss8mv), the same by value (ss8v, ss8vr), and a concrete callee
    // whose parameter writes Origin-less raw layers, w reached only through s (r1cv; another holder's address beside it). FAIL-FIRST: each
    // erred at `z = 5`. The exclusive routes still write: the exclusive part's holder read after (ss8m, ss8vb), an exclusive and a shared
    // Loan on parts of one holder (ss8g, by value ss8gv; shared at the first step and exclusive past h, r1a), w's plain address (ss8a; one
    // level down through r1cv's callee, r1c), an exclusive Loan past a holder beside a shared one (ss8c) and the holder a second argument
    // reaches exclusively (ss8two); a write while s's Loan lives errs (ss8w). r1cv, whose q addresses another holder, is invalid too (A5
    // value redesign U3c): t.0, a `raw/(ref/i32 during a)`, is a raw layer without an Origin in storage stp writes, so stp may write its
    // own pointer there and store v through it in storage the body does not own, used after every point (SPEC 15.6.4's composition,
    // example twoLayers; 5.7 Calls: also as a Tuple element; dev accepted it), and that storage keeps what a chain of reads through t
    // carries, so the storage ends of the holders it names conflict (r1c's and r1cv's; SPEC 23.1.2).
    // PLAN G935 (the owner's O-360 (a)): `putAny<U, V>(t: uniq/U,
    // v: V)` and `putTwo<U, V, W>(t: U, u: uniq/V, v: W)` store v nowhere in their own terms (`*t`, `*u` are storage of the callee's own
    // U or V, which holds no V or W), so `z = 5` no longer errs in ss8two (valid as a whole: `*u` is putTwo's own V). But a holder that
    // U's binding names by an exclusive Loan or addresses is no storage of U: "a safe reference the binding holds is no such layer, and
    // the routes above apply to it" (SPEC 15.6.4 route 1, PLAN G935 B2), and so is a raw layer's (the owner's O-312 (a), c3-5). putAny
    // may write it with z's borrow, so `z = 5` errs again, as on fcd6a5381, where that holder is read after: ss8m (v), ss8g (g.1), ss8c
    // (v, reached through r's exclusive layer), r1a (g.1 through h), ss8a (w's plain address, t.1), and hidRefBad (w), the same through
    // a reference to a reference (hidRefNestBad) and with U the reference itself (hidRefDirectBad). FAIL-FIRST on 768d6899d: ss8g, ss8c,
    // r1a and the three hidRef rows lost their `z = 5` (c2 kept the referent as storage of U). The `inspect(w)` records of the hidRef
    // rows are a known false record (D-776, the safe reference in a kept binding): hidRefEarlyOk, valid (w read before `z = 5`, t not
    // used after), has only it. The valid ss8, ss8s and ss8mv are valid before this rule is read. The rule's two directions are kept by
    // the concrete twins, whose callee writes the Tuple Type in its own signature (putAnyV's by-value rows still read the binding):
    // cs8s (no raw layer in `*t`) and the by-value cs8vr and cs8mvr (stAV and stMV write no layer of the caller's storage, and their
    // t.0 is w's address read through the shared s) are valid; cs8m, cs8g, cs8a, cs8c, cs8two and cr1a err at `z = 5`, and cs8m and
    // cs8a also at the storage ends of the holders the callee's storage keeps, as r1c and r1cv (independent records, SPEC 23.1.2). A callee taking
    // such a Tuple through `uniq` with a raw layer without an Origin in `*t` is no valid twin: it may write its own pointer at t.0 and
    // store v through it in storage the body does not own (SPEC 15.6.4's composition and its twoLayers example; 5.7 Calls "also as a
    // Tuple element"). Those programs (the `uniq` forms of cs8vr and cs8mvr, and this test's older r1cv, which the A5 chain relabels)
    // are rejected only with the chain's reader (PLAN G935's class 1 list), so no row here expects one accepted beyond r1cv.
    [Fact]
    public void AStoreWritesNoHolderOnlyASharedLoanNames()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
                public func put<U>(self: uniq/Self, v: U) => ()
            func putAny<U, V>(t: uniq/U, v: V) => ()
            func putAnyV<U, V>(t: U, v: V) => ()
            func putTwo<U, V, W>(t: U, u: uniq/V, v: W) => ()
            func stp(t: uniq/((raw/(ref/i32 during a), uniq/((raw/(ref/i32 during a), i32)) during c)) during e, v: ref/i32 during a) => ()
            func stAV(t: (raw/(ref/i32 during a), i32), v: ref/i32 during a) => ()
            func stMV(t: (raw/(ref/i32 during a), uniq/(ref/i32 during a) during c), v: ref/i32 during a) => ()
            func stS(t: uniq/((ref/(ref/i32 during a) during b, uniq/(ref/i32 during a) during c)) during e, v: ref/i32 during a) => ()
            func stM(t: uniq/((raw/(ref/i32 during a), uniq/(ref/i32 during a) during c)) during e, v: ref/i32 during a) => ()
            func stAA(t: uniq/((raw/(ref/i32 during a), raw/(ref/i32 during a))) during e, v: ref/i32 during a) => ()
            func stC(t: uniq/((ref/(ref/i32 during a) during b, uniq/(uniq/(ref/i32 during a) during c) during d)) during e, v: ref/i32 during a) => ()
            func stTwo(t: ref/(ref/i32 during a) during b, u: uniq/(ref/i32 during a) during c, v: ref/i32 during a) => ()
            func ss8()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var t = (s@follow@raw, 0)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func ss8i()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var h = Wrap.init(s@follow@raw)
                h.put(z@ref)
                z = 5
                inspect(w)
            func ss8s()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var t = (s, v@uniq)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func ss8mv()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var t = (s@follow@raw, v@uniq)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func ss8v()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                putAnyV((s, v@uniq), z@ref)
                z = 5
                inspect(w)
            func ss8vr()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                putAnyV((s@follow@raw, 0), z@ref)
                z = 5
                inspect(w)
            func ss8m()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var t = (s@follow@raw, v@uniq)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(v)
            func ss8vb()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                putAnyV((s, v@uniq), z@ref)
                z = 5
                inspect(v)
            func ss8g()
                var x = 1
                var y = 2
                var z = 3
                var g: (ref/i32, ref/i32) = (x@ref, y@ref)
                var t = (g.0@ref, g.1@uniq)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(g)
            func ss8gv()
                var x = 1
                var y = 2
                var z = 3
                var g: (ref/i32, ref/i32) = (x@ref, y@ref)
                putAnyV((g.0@ref, g.1@uniq), z@ref)
                z = 5
                inspect(g)
            func ss8a()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var t = (s@follow@raw, w@raw)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func ss8c()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                var r = v@uniq
                let s = w@ref
                var t = (s, r@uniq)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(v)
            func ss8two()
                var x = 1
                var y = 2
                var z = 3
                var g: (ref/i32, ref/i32) = (x@ref, y@ref)
                putTwo(g.0@ref, g.1@uniq, z@ref)
                z = 5
                inspect(g)
            func r1a()
                var x = 1
                var y = 2
                var z = 3
                var g: (ref/i32, ref/i32) = (x@ref, y@ref)
                var h = g.1@uniq
                var t = (g.0@ref, h@uniq)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(g)
            func r1c()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var q: (raw/(ref/i32), i32) = (w@raw, 0)
                var t: (raw/(ref/i32), uniq/((raw/(ref/i32), i32))) = (s@follow@raw, q@uniq)
                stp(t@uniq, z@ref)
                z = 5
                inspect(w)
            func r1cv()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var u: ref/i32 = y@ref
                let s = w@ref
                var q: (raw/(ref/i32), i32) = (u@raw, 0)
                var t: (raw/(ref/i32), uniq/((raw/(ref/i32), i32))) = (s@follow@raw, q@uniq)
                stp(t@uniq, z@ref)
                z = 5
                inspect(w)
            func ss8w()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var t = (s@follow@raw, 0)
                putAny(t@uniq, z@ref)
                w = y@ref
                inspect(s)
            func cs8vr()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                stAV((s@follow@raw, 0), z@ref)
                z = 5
                inspect(w)
            func cs8s()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var t = (s, v@uniq)
                stS(t@uniq, z@ref)
                z = 5
                inspect(w)
            func cs8mvr()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                stMV((s@follow@raw, v@uniq), z@ref)
                z = 5
                inspect(w)
            func cs8m()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var t: (raw/(ref/i32), uniq/(ref/i32)) = (s@follow@raw, v@uniq)
                stM(t@uniq, z@ref)
                z = 5
                inspect(v)
            func cs8g()
                var x = 1
                var y = 2
                var z = 3
                var g: (ref/i32, ref/i32) = (x@ref, y@ref)
                var t = (g.0@ref, g.1@uniq)
                stS(t@uniq, z@ref)
                z = 5
                inspect(g)
            func cs8a()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var t: (raw/(ref/i32), raw/(ref/i32)) = (s@follow@raw, w@raw)
                stAA(t@uniq, z@ref)
                z = 5
                inspect(w)
            func cs8c()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                var r = v@uniq
                let s = w@ref
                var t = (s, r@uniq)
                stC(t@uniq, z@ref)
                z = 5
                inspect(v)
            func cs8two()
                var x = 1
                var y = 2
                var z = 3
                var g: (ref/i32, ref/i32) = (x@ref, y@ref)
                stTwo(g.0@ref, g.1@uniq, z@ref)
                z = 5
                inspect(g)
            func cr1a()
                var x = 1
                var y = 2
                var z = 3
                var g: (ref/i32, ref/i32) = (x@ref, y@ref)
                var h = g.1@uniq
                var t = (g.0@ref, h@uniq)
                stC(t@uniq, z@ref)
                z = 5
                inspect(g)
            func hidRefBad()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var t = (w@uniq, 0)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func hidRefEarlyOk()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var t = (w@uniq, 0)
                putAny(t@uniq, z@ref)
                inspect(w)
                z = 5
            func hidRefNestBad()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var w2 = w@uniq
                var t = (w2@uniq, 0)
                putAny(t@uniq, z@ref)
                z = 5
                inspect(w)
            func hidRefDirectBad()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var u = w@uniq
                putAny(u@uniq, z@ref)
                z = 5
                inspect(w)
            """);
        Assert.Equal(
            [
                "ss8m: z = 5", "ss8m: inspect(v)", "ss8vb: z = 5", "ss8g: z = 5", "ss8g: g", "ss8gv: z = 5", "ss8a: z = 5", "ss8c: z = 5",
                "ss8c: inspect(v)", "r1a: z = 5", "r1a: g", "r1c: z = 5", "r1c: var q: (raw/(ref/i32), i32) = (w@raw, 0)", "r1c: var w: ref/i32 = x@ref",
                "r1c: var x = 1", "r1cv: z = 5", "r1cv: var q: (raw/(ref/i32), i32) = (u@raw, 0)", "r1cv: var w: ref/i32 = x@ref", "r1cv: var y = 2",
                "r1cv: var x = 1", "ss8w: w = y@ref", "cs8m: z = 5", "cs8m: inspect(v)", "cs8m: var v: ref/i32 = y@ref", "cs8m: var w: ref/i32 = x@ref",
                "cs8m: var y = 2", "cs8m: var x = 1", "cs8g: z = 5", "cs8g: g", "cs8a: z = 5", "cs8a: var w: ref/i32 = x@ref", "cs8a: var x = 1", "cs8c: z = 5",
                "cs8c: inspect(v)", "cs8two: z = 5", "cr1a: z = 5", "cr1a: g", "hidRefBad: z = 5", "hidRefBad: inspect(w)", "hidRefEarlyOk: inspect(w)",
                "hidRefNestBad: z = 5", "hidRefNestBad: inspect(w)", "hidRefDirectBad: z = 5", "hidRefDirectBad: inspect(w)",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 5.7 Fits and claims, Calls, 5.2.1 condition 3, 15.6.4 item 1 (PLAN G148 (1), R7 plan Z6 Revisions 6-7.1; the owner's O-498
    // 「O-498 Q1 i, Q2 b」): the named limits keep the located Unsupported_Kd where the stated Origin's borrows do not bound what a raw
    // layer holds. A value of another Origin fitted into the holder's layer keeps its source's authority (Q1 (i)), so the layer is read by
    // its stated Origin only where nothing else is fitted into it (Q2 (b)): an assignment (fitAssigned, invalid: `put` may store z's borrow
    // in v), a written Type (fitWritten), a fit narrowing to another Origin (fitNarrowed: q still writes w), a public Field write
    // (fitField) and a write through a reference (fitThrough), each invalid, keep it; the typed twin keeps its pointee's records
    // (fitAssignedTyped); fitWrittenSame (valid) is the whitelist's cost. An address taken past a shared path writes nothing (SPEC 5.2.1
    // condition 3), which an atom's members do not show: sharedFollow, sharedImplicit and sharedProjection (valid) keep it, and the typed
    // twin keeps the pointee's records (sharedFollowTyped, a pre-existing conservative pair; removal: route (a)). Decided now (FAIL-FIRST:
    // the located Unsupported_Kd at the call): nestedExclusive (invalid: q's address is r's, `z = 5` found; its storage-end records at w and
    // x are dependent, DECISIONS Q13), anchored (invalid, SPEC
    // 5.2.2's anchor: `z = 5`) and a layer over an exclusive Field's address whose referent admits no local borrow (fieldExclusive, valid).
    // `null` is never a fitted value (nullFit, valid). The fit mark is LimitMarks.FittedIntoHolder (Z6-F2), whose paths (a call's
    // argument, a generic result, a clause) TyCtxtTest.AFitFromAnotherOriginMarksTheHoldersAtoms asserts. Z6 Revision 8 on the owner's
    // O-507 (B) (a slot judged by the Type declaration that binds it; LimitMarks.FittedExclusive, which also reaches the holder's own
    // region): a declared layer whose slot keeps only `ref` Loans gives no write authority whatever was fitted (slotRefKept, valid, as on
    // dev), nor does a layer an abstract callee's Type does not show (slotAbstract, valid, SPEC 15.6.4); a `uniq`-kept slot's layer holding
    // r's exclusive address keeps it (slotUniqKept), its shared twin not (slotUniqShared, valid), and so does a pointer copied out of it
    // into a Type argument's layer (slotUniqCopied) or into a second `uniq`-kept holder (slotUniqChained). Those three are valid by the
    // owner's O-512 R1 (SPEC 5.7 Calls: what the layer's Origin belongs to is judged where it is bound, HU's slot kept by an i32's `uniq`
    // Loan, so `put` may store only an i32 through it, never z's borrow): their located Unsupported_Kd is the whitelist's cost. Further
    // rows of that cost (O-498 Q2 (b); valid, the located Unsupported_Kd, Q13 moves named on Q-702): the
    // same copy out of a `ref`-kept layer (slotCopied, SPEC 5.2.1 condition 3's declared-layer sentence; dev limits it too), and into a
    // `uniq`-kept slot's layer, which dev accepts (slotUniqFromRefKept); a pointer read from a `uniq`-kept holder's layer fitted into a
    // second one, its source past a shared path (slotUniqChainedShared, SPEC 5.2.1 condition 3's first limit) or `null`
    // (slotUniqChainedNull: the holder's region may write by its `uniq` keeper, judged where it is bound); and any value of another
    // Origin fitted straight into a `uniq`-kept slot's layer by FittedIntoHolder, a shared address too (slotUniqSet, Z6's M3a).
    [Fact]
    public void ANamedLimitKeepsTheLocatedUnsupportedWhereTheStatedOriginDoesNotBoundTheLayer()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
                public func put<U>(self: uniq/Self, v: U) => ()
            struct Pub<E>
                public var value: E
                public init(value: E) => self.value = value@move
                public func put<U>(self: uniq/Self, v: U) => ()
            struct Cell {o}
                var p: raw/u8 during o
                var k: uniq/i32 during o
                public init(p: raw/u8 during o, k: uniq/i32 during o)
                    self.p = p
                    self.k = k
                public func put<U>(self: uniq/Self, v: U) => ()
            struct HF {s}
                var p: raw/u8 during s
                var k: ref/i32 during s
                public init(k: ref/i32 during s, q: raw/u8 during s)
                    self.p = q
                    self.k = k
                public func put<U>(self: uniq/Self, v: U) => ()
            struct HU {s}
                public var p: raw/u8 during s
                var k: uniq/i32 during s
                public init(k: uniq/i32 during s, q: raw/u8 during s)
                    self.p = q
                    self.k = k
                public func put<U>(self: uniq/Self, v: U) => ()
                public func setp(self: uniq/Self, q: raw/u8 during s) => self.p = q
            struct HP {s}
                public var p: raw/u8 during s
                var k: ref/i32 during s
                public init(k: ref/i32 during s, q: raw/u8 during s)
                    self.p = q
                    self.k = k
            func putg<H>(h: uniq/H, v: ref/i32) => ()
            struct HB {s}
                var k: ref/i32 during s
                var m: uniq/i32 during s
                public init(k: ref/i32 during s, m: uniq/i32 during s)
                    self.k = k
                    self.m = m
                public func fieldExclusive(self: uniq/Self)
                    var z = 3
                    var h = Wrap.init(self.m@follow@raw@raw/u8)
                    h.put(z@ref)
                    z = 5
            func fitAssigned(c: bool)
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var r = v@uniq
                var h = Wrap.init(s@follow@raw@raw/u8)
                if c => h = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func fitAssignedTyped(c: bool)
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var r = v@uniq
                var h = Wrap.init(s@follow@raw)
                if c => h = Wrap.init(r@follow@raw)
                h.put(z@ref)
                z = 5
                inspect(v)
            func fitWritten(c: bool)
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var r = v@uniq
                var h: Wrap<raw/u8 during s> = Wrap.init(s@follow@raw@raw/u8)
                if c => h = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func fitWrittenSame()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let s = w@ref
                var h: Wrap<raw/u8 during s> = Wrap.init(s@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func fitNarrowed()
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                var k = v@uniq
                var r = w@uniq
                let q: raw/u8 during k = r@follow@raw@raw/u8
                var h = Wrap.init(q)
                h.put(z@ref)
                z = 5
                inspect(w)
            func fitField(c: bool)
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var r = v@uniq
                var h = Pub.init(s@follow@raw@raw/u8)
                if c => h.value = r@follow@raw@raw/u8
                h.put(z@ref)
                z = 5
                inspect(v)
            func fitThrough(c: bool)
                var x = 1
                var y = 2
                var z = 3
                var w: ref/i32 = x@ref
                var v: ref/i32 = y@ref
                let s = w@ref
                var r = v@uniq
                var h = Wrap.init(s@follow@raw@raw/u8)
                if c
                    let hr = h@uniq
                    hr@follow = Wrap.init(r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func sharedFollow()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                let c = r@ref
                var h = Wrap.init(c@follow@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func sharedFollowTyped()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                let c = r@ref
                var h = Wrap.init(c@follow@follow@raw)
                h.put(z@ref)
                z = 5
                inspect(w)
            func sharedImplicit()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var t = (w@uniq, 0)
                let c = t@ref
                var h = Wrap.init(c.0@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func sharedProjection()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var t = (w@uniq, 0)
                let c = t@ref
                var h = Wrap.init(c@follow.0@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(w)
            func nestedExclusive()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                var q = r@follow@raw@raw/u8
                var h = Wrap.init(q@raw)
                h.put(z@ref)
                z = 5
                inspect(w)
            func anchored()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                let pw = w@raw
                unsafe
                    let e = (*pw)@uniq
                    var h = Wrap.init(e@follow@raw@raw/u8)
                    h.put(z@ref)
                z = 5
                inspect(w)
            func nullFit()
                var z = 3
                var b = (0, 1)
                var k = b.0@uniq
                let s = k@ref
                let a = s@follow@follow@raw@raw/u8
                var h = Cell.init(null, k)
                h.put(z@ref)
                z = 5
                inspect(b)
            func slotRefKept()
                var x = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                var r = v@uniq
                var h = HF.init(x@ref, r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func slotAbstract()
                var x = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                var r = v@uniq
                var h = HF.init(x@ref, r@follow@raw@raw/u8)
                putg(h@uniq, z@ref)
                z = 5
                inspect(v)
            func slotUniqKept()
                var x = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                var r = v@uniq
                var h = HU.init(x@uniq, r@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func slotUniqShared()
                var x = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                let sv = v@ref
                var h = HU.init(x@uniq, sv@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func slotUniqCopied()
                var x = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                var r = v@uniq
                var h = HU.init(x@uniq, r@follow@raw@raw/u8)
                var w = Wrap.init(h.p)
                w.put(z@ref)
                z = 5
                inspect(v)
            func slotCopied()
                var x = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                var r = v@uniq
                var h = HP.init(x@ref, r@follow@raw@raw/u8)
                var w = Wrap.init(h.p)
                w.put(z@ref)
                z = 5
                inspect(v)
            func slotUniqFromRefKept()
                var x = 1
                var y = 2
                var z = 3
                var n = 4
                var v: ref/i32 = y@ref
                var r = v@uniq
                var hf = HP.init(x@ref, r@follow@raw@raw/u8)
                var hu = HU.init(n@uniq, hf.p)
                hu.put(z@ref)
                z = 5
                inspect(v)
            func slotUniqSet()
                var x = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                let sv = v@ref
                var h = HU.init(x@uniq, null)
                h.setp(sv@follow@raw@raw/u8)
                h.put(z@ref)
                z = 5
                inspect(v)
            func slotUniqChained()
                var x1 = 1
                var x2 = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                var r = v@uniq
                var h1 = HU.init(x1@uniq, r@follow@raw@raw/u8)
                var h2 = HU.init(x2@uniq, h1.p)
                h2.put(z@ref)
                z = 5
                inspect(v)
            func slotUniqChainedShared()
                var x1 = 1
                var x2 = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                let sv = v@ref
                var h1 = HU.init(x1@uniq, sv@follow@raw@raw/u8)
                var h2 = HU.init(x2@uniq, h1.p)
                h2.put(z@ref)
                z = 5
                inspect(v)
            func slotUniqChainedNull()
                var x1 = 1
                var x2 = 1
                var y = 2
                var z = 3
                var v: ref/i32 = y@ref
                var h1 = HU.init(x1@uniq, null)
                var h2 = HU.init(x2@uniq, h1.p)
                h2.put(z@ref)
                z = 5
                inspect(v)
            """);
        Assert.Equal(
            [
                "fitAssigned: h.put(z@ref) Unsupported_Kd", "fitAssignedTyped: z = 5", "fitAssignedTyped: inspect(v)", "fitWritten: h.put(z@ref) Unsupported_Kd",
                "fitWrittenSame: h.put(z@ref) Unsupported_Kd", "fitNarrowed: h.put(z@ref) Unsupported_Kd", "fitField: h.put(z@ref) Unsupported_Kd",
                "fitThrough: h.put(z@ref) Unsupported_Kd", "sharedFollow: h.put(z@ref) Unsupported_Kd", "sharedFollowTyped: z = 5",
                "sharedFollowTyped: inspect(w)", "sharedImplicit: h.put(z@ref) Unsupported_Kd", "sharedProjection: h.put(z@ref) Unsupported_Kd",
                "nestedExclusive: z = 5", "nestedExclusive: inspect(w)", "nestedExclusive: var w: ref/i32 = x@ref", "nestedExclusive: var x = 1",
                "anchored: z = 5", "slotUniqKept: h.put(z@ref) Unsupported_Kd", "slotUniqCopied: w.put(z@ref) Unsupported_Kd",
                "slotCopied: w.put(z@ref) Unsupported_Kd", "slotUniqFromRefKept: hu.put(z@ref) Unsupported_Kd",
                "slotUniqSet: h.setp(sv@follow@raw@raw/u8) Unsupported_Kd", "slotUniqSet: h.put(z@ref) Unsupported_Kd",
                "slotUniqChained: h2.put(z@ref) Unsupported_Kd", "slotUniqChainedShared: h2.put(z@ref) Unsupported_Kd",
                "slotUniqChainedNull: h2.put(z@ref) Unsupported_Kd",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // V16 (DECISIONS R4-M147, R4-M140): after a rebind clears the holding memo, A5's guard walks again over a program whose holders reach
    // an Exclusive layer's members (an Array's and a Dictionary's exclusive iteration), an Own layer's Type arguments (a Dictionary's
    // owning iterator), the stored parts of a nested instance and the Limit (an address taken past the shared c, Z6's named limit R6.1),
    // and allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void TheGuardsWalkAllocatesNothingWarm()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value@move
                public func put<U>(self: uniq/Self, v: U) => ()
            struct Outer<P>
                var w: Wrap<P>
                public init(w: Wrap<P>) => self.w = w@move
                public func put<U>(self: uniq/Self, v: U) => ()
            func iterated()
                var x = 1
                var y = 2
                var refs: Array<ref/i32> = []
                refs.append(x@ref)
                for e in refs@uniq
                    e@follow = y@ref
                inspect(refs@ref)
            func dict()
                var x = 1
                var y = 2
                var refs: Dictionary<i32, ref/i32> = [:]
                _ = refs.tryInsert(1, x@ref)
                for (k, e) in refs@uniq
                    e@follow = y@ref
                inspect(refs@ref)
            func owned()
                var x = 1
                var w: ref/i32 = x@ref
                do
                    var d: Dictionary<i32, uniq/(ref/i32)> = [:]
                    _ = d.tryInsert(1, w@uniq)
                    var it = d@move.intoIterator()
                    _ = it.next()
                inspect(w)
            func limited()
                var x = 1
                var z = 3
                var w: ref/i32 = x@ref
                var r = w@uniq
                let c = r@ref
                var o = Outer.init(Wrap.init(c@follow@follow@raw@raw/u8))
                o.put(z@ref)
                var t = Wrap.init(r@follow@raw)
                t.put(z@ref)
                z = 5
                inspect(w)
            """);
        var stratum = new Stratum(c.Types);
        stratum.Run(c.BuildMir());
        MirProgram? program = null;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            stratum.Run(program = c.BuildMir());
        }));
        Assert.Contains("limited: o.put(z@ref) Unsupported_Kd", Problems(c, program!, [DiagnosticCode.Unsupported_Kd]));
    }
}
