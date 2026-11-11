// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's store through a stated raw layer at a written input's own address (PLAN G338, G598; SPEC 5.2.1, 5.7): W's first step
/// reads the Origin's holders.</summary>
public sealed partial class MirBorrowsTest
{
    private const string StatedForms = """
        func usep<T>(p: raw/T) => ()
        func write(target: uniq/(raw/u8) during a, q: raw/u8 during a) => ()
        func writek(target: uniq/(raw/u8) during a, k: ref/u8, q: raw/u8 during a) => ()
        func heap() -> raw/u8 during static
            unsafe => return Raw.allocate<u8>(8)@(raw/u8 during static)
        func pun(p: raw/usize, v: usize)
            unsafe => *p = v
        func writet(target: uniq/(raw/u8) during a, q: (raw/u8 during a, i32)) => ()
        func writed(target: uniq/(raw/u8) during a, q: raw/u8 during a = null) => ()
        func look(p: ref/(raw/u8 during static)) => ()
        func mk(p: raw/u8) -> raw/u8
            return Raw.allocate<u8>(8)
        struct Cell
            public var q: raw/u8 during static
            public init() => self.q = null
        struct WriterC {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public var q: raw/u8 during a
            public init(target: uniq/(raw/u8) during a, q: raw/u8 during a)
                self.loan = Loan.init(target)
                self.q = q
        struct Keeper {a}
            let loan: Loan<uniq/(raw/u8) during a>
            public init(target: uniq/(raw/u8) during a) => self.loan = Loan.init(target)
        func writew(target: uniq/(raw/u8) during a, w: ref/(Keeper during a), k: ref/u8, q: raw/u8 during a) => ()
        func writeq(q: raw/u8 during a, target: uniq/(raw/u8) during a) => ()
        func writeg<T>(target: uniq/T during a, q: raw/u8 during a) => ()
        struct Marker
            public var p: raw/u8
            public init() => self.p = null
            public func mark(q: raw/u8 during a, self, target: uniq/(raw/u8) during a) => ()
        struct Wrap<T>
            public var value: T
            public init(value: T) => self.value = value@move
        struct RB {a}
            public var k: uniq/u8 during a
            public var p: raw/u8 during a
            public init(k: uniq/u8 during a, p: raw/u8 during a)
                self.k = k
                self.p = p
        struct RB2 {a}
            public let k: ref/u8 during a
            public var p: raw/u8 during a
            public init(k: ref/u8 during a, p: raw/u8 during a)
                self.k = k
                self.p = p
        func writeo(target: uniq/(raw/u8) during a, q: Option<raw/u8 during a>) => ()
        func writeos(target: uniq/(raw/u8) during a, q: Option<raw/u8 during static>) => ()
        func writef(target: uniq/(raw/u8) during a, q: Wrap<raw/u8 during a>) => ()
        func writeft<T>(target: uniq/(raw/u8) during a, q: Wrap<T>) => ()
        func writen(target: uniq/(raw/u8) during a, q: (i32, Option<raw/u8 during a>)) => ()
        func writens(target: uniq/(raw/u8) during a, q: (i32, Option<raw/u8 during static>)) => ()
        func writer(target: uniq/(raw/u8) during a, q: RB during a) => ()
        func writer2(target: uniq/(raw/u8) during a, q: RB2 during a) => ()
        func writett(target: uniq/(raw/u8) during a, q: ((raw/u8 during a, i32), i32)) => ()
        func write2(target: uniq/(raw/u8) during a, q: (raw/u8 during a, raw/u8 during a)) => ()
        func writea(target: uniq/(raw/u8) during a, q: [2 of raw/u8 during a]) => ()
        func writeat(target: uniq/(raw/u8) during a, q: [2 of (raw/u8 during a, i32)]) => ()
        func writeA(target: uniq/(raw/u8) during a, q: Array<raw/u8 during a>) => ()
        func writeu(target: uniq/(raw/u8) during a, q: (raw/u8 during a, raw/u8)) => ()
        func writes(target: uniq/(raw/u8) during a, k: ref/u8 during a, q: (raw/u8 during a, i32)) => ()
        func writeb(target: uniq/(raw/u8) during a, k2: uniq/(raw/u8) during b, q: (raw/u8 during a, raw/u8 during b)) => ()
        func writetg<T>(target: uniq/T during a, q: (raw/u8 during a, i32)) => ()
        func writete<T>(target: uniq/(raw/u8) during a, q: (raw/u8 during a, T)) => ()
        struct Empty
            public init() => ()
        func nop(x: raw/u8) => ()
        func writei<F>(target: uniq/(raw/u8) during a, q: (raw/u8 during a, F))
            F is Callable<(raw/u8) -> ()>
            q.1(target@follow)
        func writec<F>(target: uniq/(raw/u8) during a, q: (raw/u8 during a, F))
            F is Callable<() -> ()>
            q.1()
        func mks(p: raw/u8) -> raw/u8 during static
            unsafe => return Raw.allocate<u8>(8)@(raw/u8 during static)
        func endOk()
            var spare: raw/u8 = null
            write(spare@uniq, null)
        func wholeWriteOk()
            var spare: raw/u8 = null
            write(spare@uniq, null)
            spare = null
        func unrelatedOk()
            var spare: raw/u8 = null
            var kk: u8 = 0
            writek(spare@uniq, kk@ref, null)
            kk = 1
        func constructorOk()
            var spare: raw/u8 = null
            var x = WriterC.init(target: spare@uniq, q: null)
        func heldNullOk()
            var spare: raw/u8 = null
            var q: raw/u8 during static = null
            write(spare@uniq, q)
        func parenNullOk()
            var spare: raw/u8 = null
            write(spare@uniq, (null))
        func staticNullOk()
            var spare: raw/u8 = null
            write(spare@uniq, null@(raw/u8 during static))
        func defaultNullOk()
            var spare: raw/u8 = null
            writed(spare@uniq)
        func joinedNullOk(c: bool)
            var spare: raw/u8 = null
            var q: raw/u8 during static = null
            if c
                q = null
            write(spare@uniq, q)
        func copiedNullOk()
            var spare: raw/u8 = null
            let q0: raw/u8 during static = null
            let q = q0
            write(spare@uniq, q)
        func replacedNullOk()
            var spare: raw/u8 = null
            var q: raw/u8 during static = heap()
            q = null
            write(spare@uniq, q)
        func displacedNullOk()
            var spare: raw/u8 = null
            var q: raw/u8 during static = null
            unsafe => q = q + 0
            write(spare@uniq, q)
        func sharedNullOk()
            var spare: raw/u8 = null
            var q: raw/u8 during static = null
            look(q@ref)
            write(spare@uniq, q)
        func addressAfterNullFalseError()
            var spare: raw/u8 = null
            var q: raw/u8 during static = null
            write(spare@uniq, q)
            let p = q@raw
            usep(p)
        func copyBeforePunFalseError()
            var spare: raw/u8 = null
            var q: raw/u8 during static = null
            let q2 = q
            pun(q@raw@raw/usize, heap()@usize)
            write(spare@uniq, q2)
        func joinedHeapBad(c: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var q: raw/u8 during static = null
            if c
                q = heap()
            write(spare@uniq, q)
            kk = 2
        func storedOverBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var q: raw/u8 during static = null
            let p = q@raw
            unsafe => *p = heap()
            write(spare@uniq, q)
            kk = 2
        func loopHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var q: raw/u8 during static = null
            var i = 0
            while i < 2
                write(spare@uniq, q)
                q = heap()
                i = i + 1
            kk = 2
        func tupleHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writet(spare@uniq, (heap(), 0))
            kk = 2
        func copiedPunBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var q: raw/u8 during static = null
            pun(q@raw@raw/usize, heap()@usize)
            let q2 = q
            write(spare@uniq, q2)
            kk = 2
        func fieldPunBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var w = Cell.init()
            pun(w@raw@raw/usize, heap()@usize)
            write(spare@uniq, w.q)
            kk = 2
        func calledBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var q: raw/u8 during static = null
            unsafe => q = mk(null)@(raw/u8 during static)
            write(spare@uniq, q)
            kk = 2
        func allocatedBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var q: raw/u8 during static = null
            unsafe => q = Raw.allocate<u8>(8)@(raw/u8 during static)
            write(spare@uniq, q)
            kk = 2
        func punnedBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var q: raw/u8 during static = null
            pun(q@raw@raw/usize, heap()@usize)
            write(spare@uniq, q)
            kk = 2
        func readBad()
            var spare: raw/u8 = null
            write(spare@uniq, null)
            usep(spare)
        func constructorReadBad()
            var spare: raw/u8 = null
            var x = WriterC.init(target: spare@uniq, q: null)
            usep(spare)
        func unrelatedReadBad()
            var spare: raw/u8 = null
            var kk: u8 = 0
            writek(spare@uniq, kk@ref, null)
            kk = 1
            usep(spare)
        func secondHolderBad()
            var other: raw/u8 = null
            let b = other@uniq
            let p = b@follow@raw@raw/u8
            var spare: raw/u8 = null
            var kk: u8 = 0
            writek(spare@uniq, kk@ref, p)
            kk = 1
            usep(other)
        func parameterBad(t: uniq/(raw/u8) during x, p: raw/u8 during x)
            var kk: u8 = 0
            writek(t, kk@ref, p)
        func keeperBad()
            var spare: raw/u8 = null
            var slot: raw/u8 = null
            var kk: u8 = 0
            var w = Keeper.init(slot@uniq)
            writew(spare@uniq, w@ref, kk@ref, null)
            kk = 1
            usep(slot)
        func itemOk()
            var spare: raw/u8 = null
            let f = write
            f(spare@uniq, null)
        func itemReadBad()
            var spare: raw/u8 = null
            let f = writeq
            f(null, spare@uniq)
            usep(spare)
        func genericItemOk()
            var spare: raw/u8 = null
            let f = writeg<raw/u8>
            f(spare@uniq, null)
        func genericItemReadBad()
            var spare: raw/u8 = null
            let f = writeg<raw/u8>
            f(spare@uniq, null)
            usep(spare)
        func methodItemOk()
            var m = Marker.init()
            var spare: raw/u8 = null
            let f = Marker.mark
            f(null, m@ref, spare@uniq)
        func methodItemReadBad()
            var m = Marker.init()
            var spare: raw/u8 = null
            let f = Marker.mark
            f(null, m@ref, spare@uniq)
            usep(spare)
        func ownItemOk(target: uniq/(raw/u8) during a, q: raw/u8 during a)
            var spare: raw/u8 = null
            let f = ownItemOk
            f(spare@uniq, null)
        func ownItemReadBad(target: uniq/(raw/u8) during a, q: raw/u8 during a)
            var spare: raw/u8 = null
            let f = ownItemReadBad
            f(spare@uniq, null)
            usep(spare)
        func optionHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writeo(spare@uniq, Option<raw/u8 during static>.Some(heap()))
            kk = 2
        func optionStaticOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writeos(spare@uniq, Option<raw/u8 during static>.Some(heap()))
            kk = 2
        func fieldHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writef(spare@uniq, Wrap<raw/u8 during static>.init(heap()))
            kk = 2
        func fieldHiddenOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writeft<raw/u8 during static>(spare@uniq, Wrap<raw/u8 during static>.init(heap()))
            kk = 2
        func nestedHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writen(spare@uniq, (0, Option<raw/u8 during static>.Some(heap())))
            kk = 2
        func nestedStaticOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writens(spare@uniq, (0, Option<raw/u8 during static>.Some(heap())))
            kk = 2
        func declaredFieldBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: u8 = 0
            writer(spare@uniq, RB.init(other@uniq, heap()))
            kk = 2
        func declaredRefOk()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var other: u8 = 0
            writer2(spare@uniq, RB2.init(other@ref, heap()))
            kk = 2
        func tupleNullOk()
            var spare: raw/u8 = null
            writet(spare@uniq, (null, 0))
        func tupleHeldNullOk()
            var spare: raw/u8 = null
            let t: (raw/u8 during static, i32) = (null, 0)
            writet(spare@uniq, t)
        func nestedTupleNullOk()
            var spare: raw/u8 = null
            writett(spare@uniq, ((null, 1), 0))
        func twoLayerNullOk()
            var spare: raw/u8 = null
            write2(spare@uniq, (null, null))
        func fixedArrayNullOk()
            var spare: raw/u8 = null
            writea(spare@uniq, [null, null])
        func tuplePartNullOk()
            var spare: raw/u8 = null
            var t: (raw/u8 during static, i32) = (null, 0)
            t.1 = 5
            writet(spare@uniq, t)
        func tupleJoinedNullOk(c: bool)
            var spare: raw/u8 = null
            var t: (raw/u8 during static, i32) = (null, 0)
            if c
                t = (null, 1)
            writet(spare@uniq, t)
        func twoOriginsNullOk()
            var spare: raw/u8 = null
            var spare2: raw/u8 = null
            writeb(spare@uniq, spare2@uniq, (null, null))
        func emptyElementNullOk()
            var spare: raw/u8 = null
            writete<Empty>(spare@uniq, (null, Empty.init()))
        func genericElementNullOk()
            var spare: raw/u8 = null
            writete<i32>(spare@uniq, (null, 0))
        func genericTargetNullOk()
            var spare: raw/u8 = null
            writetg<raw/u8>(spare@uniq, (null, 0))
        func arrayOfTuplesNullOk()
            var spare: raw/u8 = null
            let t: [2 of (raw/u8 during static, i32)] = [(null, 0), (null, 1)]
            writeat(spare@uniq, t)
        func joinedOriginsNullOk(c: bool)
            var other: raw/u8 = null
            let b = other@uniq
            var spare: raw/u8 = null
            let t0: (raw/u8 during static, i32) = (null, 0)
            var t: (raw/u8 during b, i32) = t0
            if c
                t = (null, 1)
            writet(spare@uniq, t)
        func itemElementNullOk()
            var spare: raw/u8 = null
            writei(spare@uniq, (null, nop))
        func closureElementNullOk()
            var spare: raw/u8 = null
            let n: i32 = 0
            writec(spare@uniq, (null, func [n] () => ()))
        func copyThenPartNullOk()
            var spare: raw/u8 = null
            let t: (raw/u8 during static, i32) = (null, 0)
            var t2 = t
            t2.0 = heap()
            writet(spare@uniq, t)
            usep(t2.0)
        func arrayNullOk()
            var spare: raw/u8 = null
            let t: Array<raw/u8 during static> = [null, null]
            writeA(spare@uniq, t@move)
        func optionNullOk()
            var spare: raw/u8 = null
            writeo(spare@uniq, .None)
        func optionElementNullOk()
            var spare: raw/u8 = null
            let o: Option<raw/u8 during static> = .None
            writen(spare@uniq, (0, o))
        func fieldCallFalseError()
            var spare: raw/u8 = null
            writef(spare@uniq, Wrap<raw/u8 during static>.init(null))
        func tupleUnstatedFalseError()
            var spare: raw/u8 = null
            writeu(spare@uniq, (null, null))
        func tupleSharedFalseError()
            var spare: raw/u8 = null
            var kk: u8 = 0
            writes(spare@uniq, kk@ref, (null, 0))
        func tupleReadBad()
            var spare: raw/u8 = null
            writet(spare@uniq, (null, 0))
            usep(spare)
        func nestedTupleHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writett(spare@uniq, ((heap(), 1), 0))
            kk = 2
        func twoLayerHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            write2(spare@uniq, (null, heap()))
            kk = 2
        func fixedArrayHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            let t: [2 of raw/u8 during static] = [null, heap()]
            writea(spare@uniq, t)
            kk = 2
        func tupleUnstatedHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            writeu(spare@uniq, (null, heap()))
            kk = 2
        func tuplePartHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var t: (raw/u8 during static, i32) = (null, 0)
            t.0 = heap()
            writet(spare@uniq, t)
            kk = 2
        func tupleJoinedHeapBad(c: bool)
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var t: (raw/u8 during static, i32) = (null, 0)
            if c
                t = (heap(), 1)
            writet(spare@uniq, t)
            kk = 2
        func tuplePunnedBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var t: (raw/u8 during static, i32) = (null, 0)
            pun(t@raw@raw/usize, heap()@usize)
            writet(spare@uniq, t)
            kk = 2
        func tupleLoopHeapBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var t: (raw/u8 during static, i32) = (null, 0)
            var i = 0
            while i < 2
                writet(spare@uniq, t)
                t = (heap(), 0)
                i = i + 1
            kk = 2
        func tupleCopiedPunBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            var t: (raw/u8 during static, i32) = (null, 0)
            pun(t@raw@raw/usize, heap()@usize)
            let t2 = t
            writet(spare@uniq, t2)
            kk = 2
        func tupleCalledBad()
            var kk: u8 = 1
            let r = kk@uniq
            var spare: raw/u8 = r@follow@raw
            let t = (mks(null), 0)
            writet(spare@uniq, t)
            kk = 2
        """;

    // PLAN G338, SPEC 5.2.1 ("storage a pointer addresses `during o` is covered by the Loans of `o`"), 5.7's table, 15.6 Holders: a store through
    // a raw parameter's stated layer writes the holders that Origin's Loans name, whose storage end or whole write ends what it put there,
    // so a null q leaves nothing in E (the Ok rows, each an error at spare's end or the write before the fix, and at `kk = 1` in
    // unrelatedOk). Null is A5's must-null fact, the empty argument set (PLAN G598; SPEC 5.1, 5.3, 5.4, 5.7 Fits): held, parenthesized,
    // claimed to `static`, a default, joined, copied, replaced after a heap address, displaced by zero, or only borrowed shared (5.2.1
    // condition 3), each an error at spare's declaration before. An exposed holder (a non-shared pointer to it formed anywhere in the
    // body) and a call result are never null, so a scalar written through a raw pointer A5 does not follow, a store over q, a heap address
    // on one arm, a later iteration, in a Tuple, from a call or from Raw.allocate keeps E (the Bad rows from punnedBad to allocatedBad,
    // dev's records; fieldPunBad, tuplePunnedBad and tupleCopiedPunBad, a Scalar at a struct's or a Tuple's whole address, rest on the
    // layout since PLAN G667c: MirBorrowsTest.Puns' AScalarOverAStatedLayersHolderRestsOnItsLayout). A null A5 cannot prove at the
    // store stays dev's false error under the owner's exception O-60 (DECISIONS R4-M183):
    // here a null whose address is taken after the call, or whose Copy was taken before a pun of the original (the FalseError rows).
    // Binding's row keeps target's own value through q (BU-24 C4, BU-52 rule A), so a read of spare after the call errs
    // until PLAN G339's row (readBad, constructorReadBad; the owner's O-55 (c)). Guards: a store through q where spare is read (unrelatedReadBad); q
    // addressing a second holder keeps its referents (secondHolderBad); q from a parameter keeps E (parameterBad); an Origin a slot
    // keeper in another input also keeps stays E, since no first step names the keeper's storage (keeperBad). A Function Item's call reads
    // its declaration as a direct call does (PLAN G584; SPEC 7.7.4, 15.6.4 "for direct and indirect calls alike"): under its Item
    // substitution (genericItem*), its slots in declared order, q first (itemReadBad) or a receiver declared second (methodItem*, 7.3.4).
    // The Ok rows were errors at spare's end (and at m's for methodItemOk). In the Item's own body the call is decided as the direct call
    // is (ownItemOk, accepted); elsewhere it keeps the Value call's located Unsupported_Kd at its `null`, outside these Loan codes.
    // A stated layer an argument holds by value stores as tupleHeapBad's does wherever it lies (PLAN G583; SPEC 15.6.4 "a Tuple element, an
    // Option payload, a Field or a Type argument included"): an Option payload, a Field of T, an Option in a Tuple, a Field a declaration
    // states with a slot kept by `uniq` (whose Loan of `other` the store keeps too); each Bad row was accepted before. Their Ok twins state
    // `static`, hide the layer behind T (15.6.4), or keep the slot by `ref` (no write authority, 5.2.1 condition 3).
    // The stated layers a written input holds at its own address cover as a raw parameter's own layer does (PLAN G598; SPEC 5.7 "Tuple and
    // fixed-array elements alike"; TyCtxt.OwnLayers): a null in each, A5's must-null fact, leaves nothing in E (the Ok rows from tupleNullOk
    // to optionElementNullOk, each an error at spare's declaration with G583 alone): a Tuple literal, held, nested, two layers or two
    // Origins, a fixed array, a part store of an integer, joined, a generic element or target, a field-less struct, a Function Item or a
    // Closure element, an Array moved in, an Option payload and an Option in a Tuple. A constructor's result is never certainly null, so
    // a null Field of T keeps E under the owner's O-107 (a) (fieldCallFalseError), as an unstated element beside a stated one and a shared
    // member keep it under O-60 (the other FalseError rows); a read of spare errs as readBad does (tupleReadBad); a heap address in any of
    // these forms keeps E, as tupleHeapBad's (the Bad rows from nestedTupleHeapBad on, dev's records).
    [Theory]
    [InlineData("endOk", "")]
    [InlineData("wholeWriteOk", "")]
    [InlineData("unrelatedOk", "")]
    [InlineData("constructorOk", "")]
    [InlineData("heldNullOk", "")]
    [InlineData("parenNullOk", "")]
    [InlineData("staticNullOk", "")]
    [InlineData("defaultNullOk", "")]
    [InlineData("joinedNullOk", "")]
    [InlineData("copiedNullOk", "")]
    [InlineData("replacedNullOk", "")]
    [InlineData("displacedNullOk", "")]
    [InlineData("sharedNullOk", "")]
    [InlineData("addressAfterNullFalseError", "addressAfterNullFalseError: var spare: raw/u8 = null")]
    [InlineData("copyBeforePunFalseError", "copyBeforePunFalseError: var spare: raw/u8 = null")]
    [InlineData("punnedBad", "punnedBad: kk = 2; punnedBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("joinedHeapBad", "joinedHeapBad: kk = 2; joinedHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("storedOverBad", "storedOverBad: kk = 2; storedOverBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("loopHeapBad", "loopHeapBad: kk = 2; loopHeapBad: spare@uniq; loopHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("tupleHeapBad", "tupleHeapBad: kk = 2; tupleHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("copiedPunBad", "copiedPunBad: kk = 2; copiedPunBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("calledBad", "calledBad: kk = 2; calledBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("allocatedBad", "allocatedBad: kk = 2; allocatedBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("readBad", "readBad: usep(spare)")]
    [InlineData("constructorReadBad", "constructorReadBad: usep(spare)")]
    [InlineData("unrelatedReadBad", "unrelatedReadBad: kk = 1; unrelatedReadBad: usep(spare)")]
    [InlineData("secondHolderBad", "secondHolderBad: kk = 1; secondHolderBad: usep(other)")]
    [InlineData("parameterBad", "parameterBad: var kk: u8 = 0")]
    [InlineData("keeperBad", "keeperBad: kk = 1; keeperBad: usep(slot); keeperBad: var slot: raw/u8 = null; keeperBad: var spare: raw/u8 = null; keeperBad: var w = Keeper.init(slot@uniq)")]
    [InlineData("itemOk", "")]
    [InlineData("itemReadBad", "itemReadBad: usep(spare)")]
    [InlineData("genericItemOk", "")]
    [InlineData("genericItemReadBad", "genericItemReadBad: usep(spare)")]
    [InlineData("methodItemOk", "")]
    [InlineData("methodItemReadBad", "methodItemReadBad: usep(spare)")]
    [InlineData("ownItemOk", "")]
    [InlineData("ownItemReadBad", "ownItemReadBad: usep(spare)")]
    [InlineData("optionHeapBad", "optionHeapBad: kk = 2; optionHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("optionStaticOk", "")]
    [InlineData("fieldHeapBad", "fieldHeapBad: kk = 2; fieldHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("fieldHiddenOk", "")]
    [InlineData("nestedHeapBad", "nestedHeapBad: kk = 2; nestedHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("nestedStaticOk", "")]
    [InlineData("declaredFieldBad", "declaredFieldBad: kk = 2; declaredFieldBad: var other: u8 = 0; declaredFieldBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("declaredRefOk", "")]
    [InlineData("tupleNullOk", "")]
    [InlineData("tupleHeldNullOk", "")]
    [InlineData("nestedTupleNullOk", "")]
    [InlineData("twoLayerNullOk", "")]
    [InlineData("fixedArrayNullOk", "")]
    [InlineData("tuplePartNullOk", "")]
    [InlineData("tupleJoinedNullOk", "")]
    [InlineData("twoOriginsNullOk", "")]
    [InlineData("emptyElementNullOk", "")]
    [InlineData("genericElementNullOk", "")]
    [InlineData("genericTargetNullOk", "")]
    [InlineData("arrayOfTuplesNullOk", "")]
    [InlineData("joinedOriginsNullOk", "")]
    [InlineData("itemElementNullOk", "")]
    [InlineData("closureElementNullOk", "")]
    [InlineData("copyThenPartNullOk", "")]
    [InlineData("arrayNullOk", "")]
    [InlineData("optionNullOk", "")]
    [InlineData("optionElementNullOk", "")]
    [InlineData("fieldCallFalseError", "fieldCallFalseError: var spare: raw/u8 = null")]
    [InlineData("tupleUnstatedFalseError", "tupleUnstatedFalseError: var spare: raw/u8 = null")]
    [InlineData("tupleSharedFalseError", "tupleSharedFalseError: var kk: u8 = 0; tupleSharedFalseError: var spare: raw/u8 = null")]
    [InlineData("tupleReadBad", "tupleReadBad: usep(spare)")]
    [InlineData("nestedTupleHeapBad", "nestedTupleHeapBad: kk = 2; nestedTupleHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("twoLayerHeapBad", "twoLayerHeapBad: kk = 2; twoLayerHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("fixedArrayHeapBad", "fixedArrayHeapBad: kk = 2; fixedArrayHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("tupleUnstatedHeapBad", "tupleUnstatedHeapBad: kk = 2; tupleUnstatedHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("tuplePartHeapBad", "tuplePartHeapBad: kk = 2; tuplePartHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("tupleJoinedHeapBad", "tupleJoinedHeapBad: kk = 2; tupleJoinedHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("tupleLoopHeapBad", "tupleLoopHeapBad: kk = 2; tupleLoopHeapBad: spare@uniq; tupleLoopHeapBad: var spare: raw/u8 = r@follow@raw")]
    [InlineData("tupleCalledBad", "tupleCalledBad: kk = 2; tupleCalledBad: var spare: raw/u8 = r@follow@raw")]
    public void AStoreThroughAStatedLayerWritesWhatItsOriginCovers(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + StatedForms[..StatedForms.IndexOf("func endOk(", StringComparison.Ordinal)] + Form(StatedForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes).Order(StringComparer.Ordinal))}]");
    }
}
