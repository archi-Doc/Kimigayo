// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5 at a body's delivery (SPEC 6.3.4, 16.2.2, 15.6.3, 5.2.1 condition 3; DECISIONS R4-M156): a Return delivers local 0 to its
/// caller, which uses it, so local 0's holder is live after the Return and every reader of a live holder counts the delivered value: a
/// constructor's Field borrow it still carries is a Loan conflict at the Return, and a pointer it carries formed through its own step the
/// own-step Warning.</summary>
public sealed partial class MirBorrowsTest
{
    // The delivery shapes: a guard with a `drop`, a holder of an object with a raw pointer, and the calls the constructors make.
    private const string DeliveryShapes = """
        struct Watch {w}
            public let r: ref/Cel during w
            public init(r: ref/Cel during w) => self.r = r
            drop => ()
        struct Keep2
            public var h: obj/Cell
            public var q: raw/i32
            public init(h: obj/Cell)
                self.h = h
                self.q = null
        group Del
            public func keepCel(slot: uniq/(ref/Cel during a), v: ref/Cel during a) => slot@follow = v
            public func seeCel(c: ref/Cel) => ()
            public func usePtr(p: raw/i32) => ()

        """;

    // SPEC 6.3.4 (Field borrows end before completion), 16.2.2-16.2.3 (completion is the delivery after Scope Exit), 15.6.3 and 15.6's holders
    // (DECISIONS R4-M156): a Field borrow the delivered receiver still carries is one conflict at the Return, its body block's or, for an
    // early `return`, the declaration's: stored into another Field (stored), through a second exclusive reference (twoUniq), in a `defer`
    // (deferred, deferredUniq), on an early return (early), through a local (viaLocal), in an Option under a branch (optional), and also where
    // the Field is overwritten later, since a part write ends none of the holder's Loans (overwritten). One stored into storage the body does
    // not own stays one record (outParam, outEarly, outDeferred; a Refuted Field borrow's derived, outCall). Valid: a guard local whose Loan
    // ends in Scope Exit (guarded), a `defer` reading a local (deferRead), a replacement ending the child before the delivery (replaced), a
    // local use (localUse), a parameter's Reborrow stored (paramStored), a local read (localRead), and a `drop`, whose Fields are destroyed in
    // place (dropped; DECISIONS R4-M103's reading L).
    [Fact]
    public void AFieldBorrowTheDeliveredReceiverCarriesConflictsAtTheReturn()
        => Assert.Equal(
            [
                "init: self.u = oStored", "init: self.u = oTwo", "init: self.u = oDeferred", "init: self.u = oDeferredU",
                "init: public init(oEarly: uniq/Cel during a, r: ref/Cel during a, flag: bool)", "init: self.u = oVia", "init: self.u = oOpt",
                "init: self.u = oOver", "init: self.u = oOut", "init: public init(oOutE: uniq/Cel during a, out: uniq/(ref/Cel during a), flag: bool)",
                "init: self.u = oOutD", "init: self.cel = Cel.init()",
            ],
            AnchorProblems(DeliveryShapes + """
                struct Stored {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oStored: uniq/Cel during a)
                        self.u = oStored
                        self.view = self.u@follow@ref
                struct TwoUniq {a}
                    public var u: uniq/Cel during a
                    public var w: uniq/Cel during a
                    public init(oTwo: uniq/Cel during a, y: uniq/Cel during a)
                        self.u = oTwo
                        self.w = y
                        self.w = self.u@follow@uniq
                struct Deferred {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oDeferred: uniq/Cel during a, r: ref/Cel during a)
                        self.u = oDeferred
                        self.view = r
                        defer => self.view = self.u@follow@ref
                struct DeferredUniq {a}
                    public var u: uniq/Cel during a
                    public var w: uniq/Cel during a
                    public init(oDeferredU: uniq/Cel during a, y: uniq/Cel during a)
                        self.u = oDeferredU
                        self.w = y
                        defer => self.w = self.u@follow@uniq
                struct Early {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oEarly: uniq/Cel during a, r: ref/Cel during a, flag: bool)
                        self.u = oEarly
                        self.view = r
                        if flag
                            self.view = self.u@follow@ref
                            return
                        self.view = r
                struct ViaLocal {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oVia: uniq/Cel during a, r: ref/Cel during a)
                        self.u = oVia
                        self.view = r
                        let k = self.u@follow@ref
                        self.view = k
                struct Optional {a}
                    public var u: uniq/Cel during a
                    public var view: Option<ref/Cel during a>
                    public init(oOpt: uniq/Cel during a)
                        self.u = oOpt
                        self.view = .None
                        if self.u.n > 0
                            self.view = .Some(self.u@follow@ref)
                struct Overwritten {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oOver: uniq/Cel during a, r: ref/Cel during a)
                        self.u = oOver
                        self.view = self.u@follow@ref
                        self.view = r
                struct OutParam {a}
                    public var u: uniq/Cel during a
                    public init(oOut: uniq/Cel during a, out: uniq/(ref/Cel during a))
                        self.u = oOut
                        out@follow = self.u@follow@ref
                struct OutEarly {a}
                    public var u: uniq/Cel during a
                    public init(oOutE: uniq/Cel during a, out: uniq/(ref/Cel during a), flag: bool)
                        self.u = oOutE
                        if flag
                            out@follow = self.u@follow@ref
                            return
                        let z: i32 = 1
                struct OutDeferred {a}
                    public var u: uniq/Cel during a
                    public init(oOutD: uniq/Cel during a, out: uniq/(ref/Cel during a))
                        self.u = oOutD
                        defer => out@follow = self.u@follow@ref
                struct OutCall {a}
                    public var cel: Cel
                    public init(out: uniq/(ref/Cel during a))
                        self.cel = Cel.init()
                        Del.keepCel(out, self.cel@ref)
                struct Guarded
                    public var cel: Cel
                    public init()
                        self.cel = Cel.init()
                        let g = Watch.init(self.cel@ref)
                struct DeferRead
                    public var cel: Cel
                    public init()
                        self.cel = Cel.init()
                        let r = self.cel@ref
                        defer => Del.seeCel(r)
                struct Replaced {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oRep: uniq/Cel during a, second: uniq/Cel during a)
                        self.u = oRep
                        self.view = self.u@follow@ref
                        self.u = second
                struct LocalUse {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oLocal: uniq/Cel during a, r: ref/Cel during a)
                        self.u = oLocal
                        self.view = r
                        let k = self.u@follow@ref
                        let m = k.n
                struct ParamStored {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oParam: uniq/Cel during a, r: uniq/Cel during a)
                        self.u = oParam
                        self.view = r@follow@ref
                struct LocalRead
                    public var cel: Cel
                    public init()
                        self.cel = Cel.init()
                        let r = self.cel@ref
                        _ = r
                struct Dropped {a}
                    public var u: uniq/Cel during a
                    public var view: ref/Cel during a
                    public init(oDrop: uniq/Cel during a, r: ref/Cel during a)
                        self.u = oDrop
                        self.view = r
                    drop
                        self.view = self.u@follow@ref

                """));

    // SPEC 5.2.1 condition 3 (DECISIONS R4-M156): the delivery ends the receiver as the constructor's holder, an ancestor of an owning value
    // with no dereference between them, so a pointer formed through that value's own step and carried by the delivered value warns at the
    // Return (delivered), as at an ordinary function's `return k` (ADepartureOfTheOwnerWarns), also where its Field was reset first, since a
    // part write ends nothing the holder carries (reset; an ordinary Move warns there too, resetMoved); a pointer used only in the
    // constructor does not (localPointer).
    [Fact]
    public void APointerTheDeliveredReceiverCarriesWarnsAtTheReturn()
        => Assert.Equal(
            ["init: self.h = hDelivered", "init: self.h = hReset", "resetMoved: k"],
            this.Warnings(AnchorShapes + DeliveryShapes + """
                struct Delivered
                    public var h: obj/Cell
                    public var q: raw/i32
                    public init(hDelivered: obj/Cell)
                        self.h = hDelivered
                        self.q = self.h@follow.x@raw
                struct Reset
                    public var h: obj/Cell
                    public var q: raw/i32
                    public init(hReset: obj/Cell)
                        self.h = hReset
                        self.q = self.h@follow.x@raw
                        self.q = null
                struct LocalPointer
                    public var h: obj/Cell
                    public var q: raw/i32
                    public init(hLocal: obj/Cell)
                        self.h = hLocal
                        let p = self.h@follow.x@raw
                        Del.usePtr(p)
                        self.q = null
                func resetMoved()
                    var k = Keep2.init(Kimi.Intrinsics.makeObj<Cell>(Cell.init()))
                    k.q = k.h@follow.x@raw
                    k.q = null
                    let m = k@move
                    usep(m.q)

                """));

    // DIAGNOSTICS (A6 facts, DECISIONS R4-M156): the delivery's record has the Field borrow as its Anchor and no witness, since the
    // delivered value's next use is the caller's; a Refuted Field borrow stored where the body does not own is the derived conflict (its
    // Cause fact), and a store Binding rejects is an Opaque with no Return record.
    [Fact]
    public void TheDeliveryRecordCarriesItsAnchor()
    {
        var c = MirBuildTest.Compile(Prelude + RawShapes + OwnStepShapes + AnchorShapes + DeliveryShapes + """
            struct Stored {a}
                public var u: uniq/Cel during a
                public var view: ref/Cel during a
                public init(o: uniq/Cel during a)
                    self.u = o
                    self.view = self.u@follow@ref
            struct OutCall {a}
                public var cel: Cel
                public init(out: uniq/(ref/Cel during a))
                    self.cel = Cel.init()
                    Del.keepCel(out, self.cel@ref)

            """);
        var program = Analyze(c);
        var found = program.Problems.All.ToArray().Select((x, i) => (Problem: x, Index: i))
            .Where(static x => x.Problem.Code == DiagnosticCode.ComparisonLoanConflict_Kd && x.Problem.Subject.Module == 0)
            .Select(x => $"{Line(c, x.Problem.Subject)}: " + string.Join(", ", program.Problems.Facts(x.Index).ToArray()
                .Select(f => $"{f.Kind} {Line(c, f.Site)}")))
            .ToArray();
        Assert.Equal(
            ["self.u = o: Anchor self.u@follow@ref", "self.cel = Cel.init(): Anchor self.cel@ref, Store self.cel = Cel.init(), Cause self.cel@ref"],
            found);
        Assert.Empty(RejectedProblems(RawShapes + OwnStepShapes + AnchorShapes + DeliveryShapes + """
            struct Assigned {a}
                public var cel: Cel
                public var view: ref/Cel during a
                public init(r: ref/Cel during a)
                    self.cel = Cel.init()
                    self.view = r
                    self.view = self.cel@ref

            """));
    }
}
