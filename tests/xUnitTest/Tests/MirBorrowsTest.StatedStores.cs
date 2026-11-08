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
        func heldNullFalseError()
            var spare: raw/u8 = null
            var q: raw/u8 during static = null
            write(spare@uniq, q)
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
    // so the `null` literal for q leaves nothing in E (the Ok rows, each an error at spare's end or the write before the fix, and at `kk = 1` in
    // unrelatedOk). Only the literal: A5 tracks no scalar written through a raw pointer, so q's empty set may be any address, here the
    // heap's, and E stays (punnedBad, the review's qCallWriteBad); a `null` held in a local is therefore still dev's false error, valid by
    // SPEC and rejected (heldNullFalseError, PLAN G338). Binding's row keeps target's own value through q (BU-24 C4, BU-52 rule A), so a
    // read of spare after the call errs
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
    [InlineData("heldNullFalseError", "heldNullFalseError: var spare: raw/u8 = null")]
    [InlineData("punnedBad", "punnedBad: kk = 2; punnedBad: var spare: raw/u8 = r@follow@raw")]
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
