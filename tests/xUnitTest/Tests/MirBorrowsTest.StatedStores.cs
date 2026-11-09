// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's store through a raw parameter's stated layer (PLAN G338; SPEC 5.2.1, 5.7): W's first step reads the Origin's holders.</summary>
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
        """;

    // PLAN G338, SPEC 5.2.1 ("storage a pointer addresses `during o` is covered by the Loans of `o`"), 5.7's table, 15.6 Holders: a store through
    // a raw parameter's stated layer writes the holders that Origin's Loans name, whose storage end or whole write ends what it put there,
    // so a null q leaves nothing in E (the Ok rows, each an error at spare's end or the write before the fix, and at `kk = 1` in
    // unrelatedOk). Null is A5's must-null fact, the empty argument set (PLAN G598; SPEC 5.1, 5.3, 5.4, 5.7 Fits): held, parenthesized,
    // claimed to `static`, a default, joined, copied, replaced after a heap address, displaced by zero, or only borrowed shared (5.2.1
    // condition 3), each an error at spare's declaration before. An exposed holder (a non-shared pointer to it formed anywhere in the
    // body) and a call result are never null, so a scalar written through a raw pointer A5 does not follow, a store over q, a heap address
    // on one arm, a later iteration, in a Tuple, from a call or from Raw.allocate keeps E (the Bad rows from punnedBad to allocatedBad,
    // dev's records). A null A5 cannot prove at the store stays dev's false error under the owner's exception O-60 (DECISIONS R4-M183):
    // here a null whose address is taken after the call, or whose Copy was taken before a pun of the original (the FalseError rows).
    // Binding's row keeps target's own value through q (BU-24 C4, BU-52 rule A), so a read of spare after the call errs
    // until PLAN G339's row (readBad, constructorReadBad; the owner's O-55 (c)). Guards: a store through q where spare is read (unrelatedReadBad); q
    // addressing a second holder keeps its referents (secondHolderBad); q from a parameter keeps E (parameterBad); an Origin a slot
    // keeper in another input also keeps stays E, since no first step names the keeper's storage (keeperBad). A Function Item's call reads
    // its declaration as a direct call does (PLAN G584; SPEC 7.7.4, 15.6.4 "for direct and indirect calls alike"): under its Item
    // substitution (genericItem*), its slots in declared order, q first (itemReadBad) or a receiver declared second (methodItem*, 7.3.4).
    // The Ok rows were errors at spare's end (and at m's for methodItemOk). In the Item's own body the call is decided as the direct call
    // is (ownItemOk, accepted); elsewhere it keeps the Value call's located Unsupported_Kd at its `null`, outside these Loan codes.
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
    [InlineData("fieldPunBad", "fieldPunBad: kk = 2; fieldPunBad: var spare: raw/u8 = r@follow@raw")]
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
    public void AStoreThroughAStatedLayerWritesWhatItsOriginCovers(string form, string expected)
    {
        var c = MirBuildTest.Compile(Prelude + StatedForms[..StatedForms.IndexOf("func endOk(", StringComparison.Ordinal)] + Form(StatedForms, form));
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), LoanCodes).Order(StringComparer.Ordinal))}]");
    }
}
