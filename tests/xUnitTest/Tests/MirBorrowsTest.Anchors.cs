// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5's raw Place borrows (SPEC 5.2.2, 15.6.2; DECISIONS R4-M156): the Loan of a borrow of a raw Place is its fresh anchor's, which
/// no access of a Place not derived from it reaches, while the Places derived from the anchor and the own-step Warning stay checked.</summary>
public sealed partial class MirBorrowsTest
{
    // The anchor shapes: a holder of a raw pointer and a reference, one whose destruction observes, one owning the Place its pointer
    // addresses, one holding an object, two constructors storing a raw Place borrow, a holder of an exclusive reference, and their calls.
    private const string AnchorShapes = """
        struct Cel
            public var n: i32 = 0
            public init() => ()
        struct Pin {a}
            public var p: raw/Cel
            public var view: ref/Cel during a
            public init(p: raw/Cel, r: ref/Cel during a)
                self.p = p
                self.view = r
        struct Pinned
            public var p: raw/Cel
            public init(p: raw/Cel) => self.p = p
            drop => ()
        struct OwnCel {a}
            public var cel: Cel
            public var p: raw/Cel
            public var view: ref/Cel during a
            public init(r: ref/Cel during a)
                self.cel = Cel.init()
                self.p = null
                self.view = r
        struct ObjPin
            public var o: obj/Cel
            public var p: raw/Cel
            public init(o: obj/Cel)
                self.o = o
                self.p = null
        struct PinView {a}
            public var p: raw/Cel
            public var view: ref/Cel during a
            public init(p: raw/Cel, r: ref/Cel during a)
                self.p = p
                self.view = r
                unsafe => self.view = (*self.p)@ref
        struct PinUniq {a}
            public var p: raw/Cel
            public var w: uniq/Cel during a
            public init(p: raw/Cel, w: uniq/Cel during a)
                self.p = p
                self.w = w
                unsafe => self.w = (*self.p)@uniq
        struct Lent {a}
            public var r: uniq/Cel during a
            public init(r: uniq/Cel during a) => self.r = r
        func seeCel(c: ref/Cel) => ()
        func setCel(c: uniq/Cel) => ()
        func setI(r: uniq/i32) => ()
        func pinBoth(h: uniq/Pin, c: ref/Cel) => ()

        """;

    // SPEC 5.2.2, 15.6.2 (DECISIONS R4-M156): a raw Place borrow's referent is a fresh anchor, not compared with Places not derived from it,
    // so its Loan meets no access of the pointer's holder: a Move of it (moved; ownMoved, whose pointer addresses its own Field, leaves the
    // use after the Move to SPEC 5.2.1's obligation), an exclusive or shared borrow of it, a call activating one, an observing destruction
    // of it, a read or another raw Place borrow through the same pointer, a write of the pointer, and a constructor whose result keeps
    // such a borrow (viewCtor, uniqCtor).
    [Fact]
    public void ARawPlaceBorrowsLoanMeetsNoAccessOfThePointersHolder()
        => Assert.Empty(AnchorProblems("""
            func moved(r0: ref/Cel, q: raw/Cel)
                var h = Pin.init(q, r: r0)
                unsafe => h.view = (*h.p)@ref
                let m = h@move
                seeCel(m.view)
            func ownMoved(r0: ref/Cel)
                var h = OwnCel.init(r0)
                h.p = h.cel@raw
                unsafe => h.view = (*h.p)@ref
                let m = h@move
                seeCel(m.view)
            func borrowedUniq(r0: ref/Cel, q: raw/Cel)
                var h = Pin.init(q, r: r0)
                unsafe
                    let r = (*h.p)@ref
                    let s = h@uniq
                    seeCel(r)
            func borrowedShared(r0: ref/Cel, q: raw/Cel)
                var h = Pin.init(q, r: r0)
                unsafe
                    let r = (*h.p)@uniq
                    let s = h@ref
                    setCel(r)
            func activated(r0: ref/Cel, q: raw/Cel)
                var h = Pin.init(q, r: r0)
                unsafe
                    let r = (*h.p)@ref
                    pinBoth(h@uniq, r)
            func observed(q: raw/Cel, q2: raw/Cel)
                var h = Pinned.init(q)
                unsafe
                    let r = (*h.p)@ref
                    h = Pinned.init(q2)
                    seeCel(r)
            func readThrough(r0: ref/Cel, q: raw/Cel)
                var h = Pin.init(q, r: r0)
                unsafe
                    let r = (*h.p)@uniq
                    let k = (*h.p).n
                    setCel(r)
            func twoAnchors(r0: ref/Cel, q: raw/Cel)
                var h = Pin.init(q, r: r0)
                unsafe
                    let r = (*h.p)@uniq
                    let r2 = (*h.p)@uniq
                    setCel(r)
                    setCel(r2)
            func pointerWritten(r0: ref/Cel, q: raw/Cel, q2: raw/Cel)
                var h = Pin.init(q, r: r0)
                unsafe
                    let r = (*h.p)@ref
                    h.p = q2
                    seeCel(r)
            func viewCtor(r0: ref/Cel, q: raw/Cel) => _ = PinView.init(q, r: r0)
            func uniqCtor(w: uniq/Cel, q: raw/Cel) => _ = PinUniq.init(q, w: w)

            """));

    // Their counterparts: a safe borrow of a Field the moved value owns (ownPartMoved, SPEC 15.6.3), a write through the raw borrow's result
    // while a Reborrow derived from it lives (derivedWrite, under the anchor), and a write of a Place whose parent Loan a Reborrow through a
    // reference reached by a raw pointer keeps (parentThroughRaw) each conflict; where a raw Place's storage is reached through an own
    // step, the Move of its owner is the own-step Warning alone (anchoredStore's `during self` storage, anchoredPayload's object payload;
    // SPEC 5.2.1 condition 3), no Loan conflict.
    [Fact]
    public void PlacesDerivedFromTheAnchorAndSafeBorrowsStillConflict()
        => Assert.Equal(
            [
                "ownPartMoved: h", "derivedWrite: v@follow.n = 2", "parentThroughRaw: x.n = 3", "anchoredStore: b OwnStepDeparture_Kd",
                "anchoredPayload: h OwnStepDeparture_Kd",
            ],
            AnchorProblems("""
                func ownPartMoved(r0: ref/Cel)
                    var h = OwnCel.init(r0)
                    h.view = h.cel@ref
                    let m = h@move
                    seeCel(m.view)
                func derivedWrite(r0: ref/Cel)
                    var h = OwnCel.init(r0)
                    h.p = h.cel@raw
                    unsafe
                        let v = (*h.p)@uniq
                        let w = v@follow@ref
                        v@follow.n = 2
                        seeCel(w)
                func parentThroughRaw()
                    var x = Cel.init()
                    var s = Lent.init(x@uniq)
                    let p: raw/Lent = s@raw
                    unsafe
                        let c = (*p).r@follow@ref
                        x.n = 3
                        seeCel(c)
                func anchoredStore()
                    var b = Store.init()
                    unsafe
                        let e = (*b.data)@uniq
                        let m = b@move
                        setI(e)
                func anchoredPayload()
                    var h = ObjPin.init(Kimi.Intrinsics.makeObj<Cel>(Cel.init()))
                    h.p = h.o@follow@raw
                    unsafe
                        let v = (*h.p)@ref
                        let m = h@move
                        seeCel(v)

                """));

    // The Loan problems and own-step Warnings of a program over the raw, own-step and anchor shapes.
    private static string[] AnchorProblems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + OwnStepShapes + AnchorShapes + source);
        return Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.OwnStepDeparture_Kd]);
    }
}
