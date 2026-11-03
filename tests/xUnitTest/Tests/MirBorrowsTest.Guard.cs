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
    // Iterable's iterator may write past itself (the owner's Q1 (a)), so count's parameter end errs; a live iterator keeps the append's
    // activation (cleanRoute); and the conflicts no store causes stay (notHeld, emptyStore, heldDefault's Loan a copy's value keeps, R-a),
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
                "dd3: t = 2", "count: S", "seedRoute: w@follow@follow@ref", "cleanRoute: xs.append(b) CallActivationConflict_Kd", // count's at its parameter's Type (BU-48 (a)).
                "notHeld: x = 2", "heldDefault: k = 2", "emptyStore: k = 2",
            ],
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
    // verdict is its written twin ER's (eh, er: no Loan is held past `.A(null)`). Where the parameter's Type expression writes the subject's
    // argument and the binding is that argument (`Item is T`), the layer is the parameter's, so the store holds no Loan, as for its twin
    // Wrap<raw/u8> (src2, hpair2, nest2, hc, eh2). The old path, which reads AdtDef's view, gives each form its twin's record (none: it
    // accepts the stated forms until R6-check, DECISIONS BU-46).
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
                "nest: z = 5", "esrc: z = 5", "hsrc: z = 5",
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
    // the collector's records and the Loan stratum alone never observe. The old path accepts each too.
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

    // SPEC 23.1.1 (DECISIONS R4-M147): a call whose W reaches a holder the guard leaves undecided (MirWrites.Limit: a supplied layer whose
    // Origin steps Exclusive, whose members Binding does not publish, over a Loan-free pointee) is one located Unsupported_Kd at the call,
    // never a Language problem, and the view's independent problems stay: a Field (flat), a nested instance's Field (nested), a Case
    // payload (optionNested), each call in a loop with an independent conflict beside it (looped: `k = 2`), a constructor lending the holder
    // and a call through it (reachedByStep), and a layer whose Origin is a shared borrow (sharedSupplied, valid, the Limit's cost until
    // Binding publishes its members). A typed pointee decides the guard (nestedPointee errs; touched, valid as on dev), as does an exclusive
    // part beside the layer (mixed errs, no record). A raw layer outside an instance reads its pointee (R4-M120): tupleW is accepted, its
    // typed twin errs (an open issue, PLAN G148). Where a call's pair holds (DECISIONS R4-M154, Revision 4), a callee its instantiated
    // signature admits may store z's borrow into w or v through the instance's uniq or raw layers, whose local regions may coincide, so
    // nestedPointee, mixed and tupleTyped also err at `z = 5`.
    [Fact]
    public void AStoreTheGuardLeavesUndecidedIsUnsupported()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value
                public func put<U>(self: uniq/Self, v: U) => ()
                public func touch(self: uniq/Self) => ()
            struct Outer<P>
                var w: Wrap<P>
                public init(w: Wrap<P>) => self.w = w@move
                public func put<U>(self: uniq/Self, v: U) => ()
            struct Mixed<E> {a}
                var value: E
                var u: uniq/(ref/i32 during a) during a
                public init(value: E, u: uniq/(ref/i32 during a) during a)
                    self.value = value
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
            """);
        Assert.Equal(
            [
                "flat: h.put(z@ref) Unsupported_Kd", "nested: o.put(z@ref) Unsupported_Kd", "optionNested: putAny(o@uniq, z@ref) Unsupported_Kd",
                "looped: h.put(z@ref) Unsupported_Kd", "looped: h.touch() Unsupported_Kd", "looped: k = 2", "reachedByStep: G.init(h@uniq) Unsupported_Kd",
                "reachedByStep: g.put(z@ref) Unsupported_Kd", "sharedSupplied: h.put(z@ref) Unsupported_Kd", "nestedPointee: z = 5",
                "nestedPointee: inspect(w)", "mixed: z = 5", "mixed: inspect(v)", "tupleTyped: z = 5", "tupleTyped: inspect(w)",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // V16 (DECISIONS R4-M147, R4-M140): after a rebind clears the holding memo, A5's guard walks again over a program whose holders reach
    // an Exclusive layer's members (an Array's and a Dictionary's exclusive iteration), an Own layer's Type arguments (a Dictionary's
    // owning iterator), the stored parts of a nested instance and the Limit, and allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void TheGuardsWalkAllocatesNothingWarm()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Wrap<E>
                var value: E
                public init(value: E) => self.value = value
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
                var o = Outer.init(Wrap.init(r@follow@raw@raw/u8))
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
