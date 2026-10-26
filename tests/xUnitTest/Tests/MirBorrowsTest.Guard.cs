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
    // Loan<…> holder's stores reached through the closure (closureRoute), err(ended)'s γ (guardP13), a later write through a reference
    // (writeQ) and E5's seed (seedRoute) are valid, as rustc says: an iterator's and a Loan's Type argument has no address. An abstract
    // Iterable's iterator may write past itself (the owner's Q1 (a)), so count's parameter end errs; a live iterator keeps the append's
    // activation (cleanRoute); and the conflicts no store causes stay (notHeld, emptyStore, heldDefault's Loan a copy's value keeps, R-a),
    // a conflict inside an inline default copy its template's (heldCopy, dd3; R4-M69). The pair cases' B6-for (pairs) reports
    // `v@follow = x`, a known wrong record (PLAN G147 (8i)), pinned so that BU's unit 0c flips it.
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
                "dd3: t = 2", "pairs: v@follow = x", "count: func count<S>(items: S) -> i32", "cleanRoute: xs.append(b) CallActivationConflict_Kd",
                "notHeld: x = 2", "heldDefault: k = 2", "emptyStore: k = 2",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 5.7 Calls, 15.6.4 (DECISIONS R4-M147; the owner's Q2 (a) and 「進める (推奨)」): a stored raw layer is read by its step on the
    // instance holding it. `during self` admits the instance's Type arguments by their actual Types, so a bag erasing its element to
    // raw/u8 writes past itself as its typed twin does (erased, typed), and the write fact where the instance has an Origin argument (P1:
    // keepPhantom's `y = 5`, which no part names; its `inspect(w)` errs on dev too); an Origin-less layer the declaration states takes the
    // write fact (P2: rawBufDead, whose store reaches E, which may be used after every point). Dev held erased and accepted the P1 and P2
    // records.
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
            """);
        Assert.Equal(
            [
                "erased: z = 5", "erased: inspect(w1)", "erased: var w1: ref/i32 = x@ref", "erased: var y = 2", "typed: z = 5", "typed: inspect(w1)",
                "typed: var w1: ref/i32 = x@ref", "typed: var y = 2", "keepPhantom: y = 5", "keepPhantom: inspect(w)", "rawBufDead: z = 5",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 23.1.1 (DECISIONS R4-M147): a call whose W reaches a holder the guard leaves undecided (MirWrites.Limit: a supplied layer whose
    // Origin steps Exclusive, whose members Binding does not publish, over a Loan-free pointee) is one located Unsupported_Kd at the call,
    // never a Language problem, and the view's independent problems stay: a Field (flat), a nested instance's Field (nested), a Case
    // payload (optionNested), each call in a loop with an independent conflict beside it (looped: `k = 2`), a constructor lending the holder
    // and a call through it (reachedByStep), and a layer whose Origin is a shared borrow (sharedSupplied, valid, the Limit's cost until
    // Binding publishes its members). A typed pointee decides the guard (nestedPointee errs; touched, valid as on dev), as does an exclusive
    // part beside the layer (mixed errs, no record). A raw layer outside an instance reads its pointee (R4-M120): tupleW is accepted, its
    // typed twin errs (an open issue, PLAN G148).
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
                "reachedByStep: g.put(z@ref) Unsupported_Kd", "sharedSupplied: h.put(z@ref) Unsupported_Kd", "nestedPointee: inspect(w)",
                "mixed: inspect(v)", "tupleTyped: inspect(w)",
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
