// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin-atom borrows (SPEC 5.7 Origins: "the outer Origin of a borrow value (an input's or, in a body, a local's) [belongs] to that
// borrow"; 15.6.5: a finite Origin local to a body, a Borrow's; contract of Binding.RawOrigins.cs; originpub design Revision 3, G147
// rulings 18, 19, 66 and 75; DECISIONS BU-243, Z6).
// - Responsibility: for each Projection or Anchor atom, the Borrows Binding commits with it as their outer Origin (each such operand of a
//   meet), each as RawMember(-1, referent, exclusive) by BorrowReferent of its Type, equal referents joining their modes, in commit order;
//   and for any atom, its limit marks (LimitMarks, one value per named limit): Claimed where a published claim's Type states it (5.7 Fits
//   and claims), SharedAddress where a published address whose authority is shared does (5.2.1 condition 3, 5.4), FittedIntoHolder
//   where a raw fit's target does, or at an invariant position its value (5.7 under O-498 Q1 (i)), and FittedExclusive where a value
//   that may write is fitted into a layer stating the atom, also through a region graph row's finite sources (5.7 Authority, Z6 Revision 8).
// - Inputs: the borrow facts Binding commits (the facts HIR publishes, BU-27's HIR contract), each formed once by its writer, outside
//   candidate trials and collected fits (FitConjunct's rule; G147 rulings 19, 25, 36, 37): the node's own facts at BindNode's end
//   (OwnBorrows: its written Borrow, Slice view or object view, its receiver operation, its calls' operations);
//   where a writer binds sub-nodes that can state a fact's atom after writing it, that writer before them: a match's Subject borrow by
//   BindMatch (before the arms), a match's or a user iteration's by-reference bindings where their Patterns bind
//   (AcquireReferenceBindings, before the guards and bodies), a source call's selected operations by CompleteArguments (before an
//   awaiting argument), an enum construction's payload operations by CompletePayloads (each at its write, before the payloads after
//   it), a Closure's written capture entries by BindConcreteClosure (before its body); a recorded adaptation by the adaptations
//   column's setter (AdaptationBorrow); and the conversions the HIR publisher publishes, whose limit marks the publisher forms
//   (MarkLimits: a claim's Origin, ClaimedOrigin; an address's, where its Place's path, folded once by PlaceAuthority's PathFold, is
//   reached through shared, SPEC 15.6.2's shared layer anywhere on the path (a Deref through a `ref` or `objref` borrow, a pair layer in
//   a shared case and a Slice element among them), or passes a conformance selection, SPEC 5.4 and 4.7.1 (a user index, an Array or
//   Dictionary element: PathPrefix.Conformance)); and the pass's raw fits (Binding.ValueFits.cs' rawFits) and the region graph rows' finite
//   sources (LocalRegionSources), which the publication's start reads for FittedIntoHolder and FittedExclusive (BeginBorrowPublication).
// - Outputs: Borrows(atom, members), the atom's entries; AtomMarks(atom), its limit marks (None without a row), the one row read of Z6's
//   one Marks reader; and RetentionState, the key field of TyCtxt's retention memo: the region and borrow versions until the pass's
//   Check has run, Complete afterwards.
// - Invariants: an atom's forming Borrow is formed before any Type stating the atom can be read (a typed path, a Field read or a
//   substitution states the atom only from a Type that carries it), except where the fact is not written yet while a part that states
//   its atom binds: a call's operations while its waiting Closure parts bind (CompleteWaiting, before the selection completes): the
//   part's inferred return Type is never outer evidence (SPEC 10.5.4), so such a call fails and its operations are never formed (ap3,
//   ap4, invalid); and a payload's operation while an awaiting payload before it binds (CompletePayloads completes the payloads in
//   source order: pl4, invalid). A reader there finds no row; every program that reaches it is invalid (the erasure whose retention
//   reads it fails Owned, SPEC 7.7.4), and what Z6's reader answers there is Z6's (Q13). The rows may hold more than HIR publishes:
//   a recorded adaptation whose operation the publisher's Adjust replaces (ChildAdjust) stays formed, and the census does not see it
//   (both its sides read the recorded column there). The rows only grow within a
//   pass, each new entry or changed mode moving borrowVersion; the marks are the publisher's own, cleared at its start
//   (BeginBorrowPublication) and formed only by it, and move no version: the order keeps them safe without one. Every reader of a
//   mark runs after the publication that formed it, under RetentionComplete: A5's StoredMembers (TyCtxt.RawMembers from
//   MirTypes.Holding, keyed by its Holdings read) and retention's binder reading (TyCtxt's retention memo, keyed by RetentionState,
//   reached from MirBorrows.CallRows, .Destructions, .Reach, .Storing and MirValidator.Calls); a Final pass publishes inside Check and
//   any other at PublishHir, and a re-check (CheckBound) binds nothing, so it forms the same marks again. Binding's one retention read
//   (ReceiverIndependence) runs while the pass's bodies bind, before its publication forms any mark of its part (a reset pass's
//   rows have none; the library part's marks, formed by the library pass, never change after it); a library body's rows
//   are the layer's part, written only by the library pass, an identical re-record by another pass writing nothing; at the publication
//   the census (CheckBorrows) compares the rows with the borrows the HIR publisher itself writes (Published*, G147 ruling 28): misses
//   always, extras and modes exactly where the publication met no failed node, a difference the internal-invariant form; it compares
//   no mark.
// - Rule (SPEC 5.4): an address `P@raw` is no Borrow ("checks like an immediately ending @ref ... and leaves no Loan"): no writer forms
//   a borrow fact of it, and its shared authority (its path reached through shared, or passing a conformance selection) is the
//   SharedAddress mark alone. No form is left unmarked after G147 ruling 72; the mark only over-reads a pair layer by its weakest
//   admitted case (its `uniq` case marked: dev's limit, not a hole). Z6's route (a) removes the mark, and with it that over-read.
//   (SPEC 5.7, O-498 Q1 (i)) a fitted raw pointer keeps its source's authority: FittedIntoHolder's two sources are the placing fits'
//   raw positions (FitOuterOrigins' pointer arm) and a call's substituted clause relating an input raw layer's Origin to another; a fit
//   form that places a value without a placing entry is that form's defect, not a case here (G147 ruling 75). The raw fits are
//   complete when the publication reads them (every placing fit runs while the pass binds, before the publication), so the row is
//   safe without a version by the order above; Z6's route (a) removes it. FittedExclusive (SPEC 5.7 Authority, a value judged where its
//   Origin is bound) is the least fixed point over those fits (a target takes it where the value may write, MayWrite, and at an
//   invariant position the value where the target may) and each row of the region graph (a row takes it where a finite source has it:
//   a local's region restates the Origin of a holder moved or initialized into the local; any other bounded row only over-marks, toward
//   the located Unsupported_Kd), formed after FittedIntoHolder, when the rows and the region graph are complete too; it ends since marks
//   only grow over the pass's atoms. A declared layer whose slot keeps only `ref` Loans stays capped by its declaration (DeclaredStep;
//   5.2.1 condition 3's declared-layer sentence, the owner's O-507 (B)).
// - Prohibitions: no entry for another atom kind, none in a candidate trial or a collected fit, no mode from syntax, no default entry, no
//   second list of borrow facts; no mark outside the publication, no second flag beside LimitMarks (a new named limit is a value of it,
//   with its removal condition); RegionVersion is not moved; no reader but Z6's (the entries by the binder reading; the marks by A5's
//   StoredMembers and retention's binder reading, both through Z6's one Marks reader, G147 ruling 67), FittedExclusive's formation
//   (MayWrite reads the entries and that mark), the validator and tests.
public sealed partial class Binding
{
    // RetentionState's value once the pass's borrow writers have run: outside every sum of the two versions within a run.
    internal const int RetentionComplete = int.MaxValue;

    // The publication side of CheckBorrows (the published borrow positions), reused.
    private readonly Dictionary<BoundOrigin, BorrowList> publishedBorrows = new(ReferenceEqualityComparer.Instance);

    // MayWrite's scratch (an atom's members), reused.
    private readonly List<RawMember> fitMembers = new();

    // Whether the publication met a failed node (Binding's Unresolved or Invalid, or published Error), whose Borrows HIR may not publish:
    // no extras check then.
    private bool publishedFailure;

    // The running pass's part of the rows (PassPart.Borrows, EnterPass).
    private Dictionary<BoundOrigin, BorrowList> borrows;
    private int borrowVersion;

    // Whether the running pass's borrow writers have all run (RunPass after Check, after which none runs; StartPass clears it): true
    // between Binds.
    private bool borrowsComplete;

    // A row's limit marks (DECISIONS BU-243; G147 rulings 66, 67): one value per named limit, a fact about the atom that its Borrows do
    // not show and that bounds what a raw layer stating the atom addresses. Z6's one Marks reader reads them for a marked atom with an
    // exclusive member: A5's StoredMembers answers the limit (the located Unsupported_Kd), retention's binder reading gives no members.
    // Each value names its removal condition.
    [Flags]
    internal enum LimitMarks : byte
    {
        None = 0,

        // SPEC 5.7 Fits and claims: a published claim's Type states the atom. An address has no mode, so neither which borrow a claimed
        // value's Origin is nor whether an address it carries lies in what that Origin covers is bounded (Z6's R2.3, named limit 1).
        // Removal: per-layer address provenance in A5.
        Claimed = 1,

        // SPEC 5.2.1 condition 3: a published address `P@raw` whose authority is shared states the atom: P's path is reached through
        // shared, or passes a conformance selection, through which the address selects `index` (SPEC 5.4, 4.7.1). It has no write
        // authority, which its members do not show, since an address forms no borrow fact (SPEC 5.4; Z6's R6.1; G147 ruling 72).
        // Removal: Z6's route (a), A5's W reading condition 3 per address (O-498 Q2 (b)'s removal condition, a later stream Z row).
        SharedAddress = 2,

        // SPEC 5.7 under O-498 Q1 (i): a value whose raw layer states another Origin is fitted into a raw layer stating the atom (a
        // raw fit's target; at an invariant position its value too), so the layer may hold a pointer with that source's authority, which
        // the atom's members do not show. Its sources (Binding.ValueFits.cs' rawFits): the placing fits' raw positions, and a call's
        // substituted clause relating an input raw layer's Origin to another (G147 ruling 75). It bounds both arms; the address mark
        // stays at the binder arm (Z6 R7.1.2). Removal: Z6's route (a), the fit's source Origins published and read, so that a fitted
        // layer is read by the join of its sources' members (O-498 Q2 (b)'s removal condition).
        FittedIntoHolder = 4,

        // SPEC 5.7 Authority, 5.2.1 condition 3 under O-498 Q1 (i) and O-507 (B) (Z6 Revision 8): a value that may carry write authority
        // is fitted into a raw layer stating the atom, through the pass's raw fits and the finite sources of each row of its region graph
        // (a local's region restates the Origin of the holder moved or initialized into the local), which the atom's own fit mark does
        // not reach. It bounds both arms as FittedIntoHolder does; a declared layer whose slot keeps only `ref` Loans is capped by its
        // declaration before any mark is read. Removal: Z6's route (a), with FittedIntoHolder.
        FittedExclusive = 8,
    }

    // The key field of TyCtxt's retention memo (DECISIONS G570, as the publication amends it): until the rows are complete, the region
    // graph's and the borrows' versions, which only rise within a run, so distinct states have distinct sums; Complete afterwards, which
    // no mid-binding answer has.
    internal int RetentionState => this.borrowsComplete ? RetentionComplete : this.regionVersion + this.borrowVersion;

    // The program part's rows as tests read them (TyCtxtTest), every pass's rows being their own part's.
    internal IEnumerable<KeyValuePair<BoundOrigin, BorrowList>> ProgramBorrows => this.programPart.Borrows;

    // Whether a commit forms its borrow facts now: outside candidate trials and collected fits (their facts are the selected candidate's,
    // formed when its node is bound).
    private bool FormsBorrows => this.candidateTrials == 0 && !this.collectingFits;

    // SPEC 5.7 Fits and claims: a claim, `E@(raw/U during o)` in an unsafe context, a pointer conversion whose written target states an
    // Origin; that Origin, null for any other node. The one classification of a claim (the publication's marks read it).
    internal static BoundOrigin? ClaimedOrigin(Koto node)
        => node is ConversionKoto { ConversionBinding: ConversionBinding.Pointer, Right.BoundType: { } target } conversion &&
            target.IsPointer && target.RawOrigin is { } stated && KotoHelper.IsUnsafeContext(conversion) ? stated : null;

    // The Borrows of `atom`, appended to `members`; false where the atom has no entry (its reader's limit, never a default). It reads no
    // mark (R6.2): the marks are AtomMarks'.
    internal bool Borrows(BoundOrigin atom, List<RawMember> members)
    {
        _ = PassPart.Find(PassPart.Parts(this.Library.Layer.Part.Borrows, this.borrows), atom, out var row);
        if (row is not { Count: > 0 })
        {
            return false;
        }

        foreach (var member in row.Items)
        {
            members.Add(member);
        }

        return true;
    }

    // The atom's limit marks, None for an atom without a row: the one row read of Z6's one Marks reader (R6.2), which joins a meet's
    // operands' marks with `|` and reads each named limit's answer from the joined value.
    internal LimitMarks AtomMarks(BoundOrigin atom)
    {
        _ = PassPart.Find(PassPart.Parts(this.Library.Layer.Part.Borrows, this.borrows), atom, out var row);
        return row?.Marks ?? LimitMarks.None;
    }

    // An atom as the validator's message names it: its kind, slot and binder's source location.
    private static string Describe(BoundOrigin atom) => $"{atom.Kind} {atom.Slot} at {atom.Binder?.CodeContext.SourceDocument?.Path}:{atom.Binder?.Span}";

    // Whether HIR publishes a call input's operation as a Borrow (HirAdjust.Of's borrow adaptations, or a same-mode Reborrow, which it
    // publishes as an acquisition): NodeBorrows' one test of an operation.
    private static bool PublishesBorrow(in BoundArgumentOperation operation)
        => operation.Kind == ArgumentOperationKind.Reborrow || HirAdjust.Of(operation) is HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow or
            HirAdaptation.BaseBorrow or HirAdaptation.PayloadProjection or HirAdaptation.ReferenceRead;

    // Formation: the borrow facts Binding commits (the facts HIR publishes, BU-27's HIR contract), each read once by its writer into the
    // running part (G147 rulings 19, 25, 36, 37): the node's own facts (OwnBorrows, at BindNode's end), a match's Subject borrow
    // (BindMatch), a match's or for's by-reference bindings (BindingBorrows, by AcquireReferenceBindings), a selected call's operations
    // (CallBorrows, by CompleteArguments), a payload's operation (OperationBorrow, by CompletePayloads), a Closure's written capture
    // entries (CaptureBorrows, by BindConcreteClosure) and the adaptation a consumer records at a node (AdaptationBorrow, by the
    // adaptations column's setter). An address is none (SPEC 5.4). The census reads none of them: it reads the publication itself
    // (ruling 28; Published*).

    // The adaptation recorded at a node, any but a referent read.
    private void AdaptationBorrow(in BoundAdaptation recorded, Dictionary<BoundOrigin, BorrowList> rows)
    {
        if (recorded.Kind != ExpectedAdaptationKind.ReferentRead)
        {
            this.RecordBorrow(rows, recorded.Type);
        }
    }

    // A match's or for's by-reference bindings, acquired as their plan's Patterns bind (AcquireReferenceBindings).
    private void BindingBorrows(BoundMatch? plan, Dictionary<BoundOrigin, BorrowList> rows)
    {
        for (var i = 0; plan is { IsCurrent: true } && i < plan.Positions.Count; i++)
        {
            if (plan.Positions[i] is { Kind: BoundPatternKind.Binding, Acquisition: PatternAcquisition.Borrow, BodySymbol.Type: { } bound })
            {
                this.RecordBorrow(rows, bound);
            }
        }
    }

    // The facts the node's own binding commits.
    private void OwnBorrows(Koto node, Dictionary<BoundOrigin, BorrowList> rows)
    {
        // A written Borrow (`@ref`, `@uniq`, `@objref`, `@objuniq`, a raw Place's) and a Slice view (a slicing index): the node's own Type.
        if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } || this.IsSlicing(node))
        {
            this.RecordBorrow(rows, node.BoundType);
        }

        // SPEC 13.6.1: an object upcast to a view of an operand of another Semantics borrows the operand's payload in the view's mode,
        // under the view's Origin (MirBuilder.Operators.Convert's Borrow; Hir.Adjust's ViewBorrow).
        if (node is ConversionKoto { ConversionBinding: ConversionBinding.ObjectUpcast, BoundType: { IsObjectBorrow: true, Origin: { } origin } view } upcast &&
            upcast.Left.BoundType is { Components: [var payload, ..] } operand && operand.Semantics != view.Semantics)
        {
            this.RecordEntry(rows, origin, payload, view.Semantics == SemanticsKind.ObjUniq);
        }

        // The receiver operation recorded at the node.
        if (this.receiverOperations.TryGetValue(node, out var receiver))
        {
            this.OperationBorrow(rows, receiver);
        }

        // Each call of the node, a user index's two included (the publication's SelectionMode takes one); a by-case value call's Shared
        // receiver acquisition too, which the publication takes before the Exclusive one (PLAN G13; it joins the Exclusive entry).
        for (var i = 0; CallPlan.Of(this, node, i) is { } call; i++)
        {
            this.OperationBorrow(rows, call.SharedReceiver);
            this.CallBorrows(rows, call.ReceiverOperation, call.ArgumentOperations);
        }
    }

    // A Closure's capture entries that borrow or Reborrow (their Adjust): formed by BindConcreteClosure once its written entries are formed,
    // before its body binds (the body states their atoms). Only a written entry borrows or Reborrows: an omitted list captures each binding
    // only by Copy (SPEC 7.7.2; JudgeOmittedCaptures), so the Closure's node has no capture fact of its own.
    private void CaptureBorrows(BoundClosure closure, Dictionary<BoundOrigin, BorrowList> rows)
    {
        for (var i = 0; i < closure.Captures.Count; i++)
        {
            if (closure.Captures[i] is var capture && capture.Adjust is { Acquire: HirAcquire.Reborrow } or { Adaptation: HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow })
            {
                this.RecordBorrow(rows, capture.Environment.Type);
            }
        }
    }

    // A call's receiver and argument operations: a source call's formed once its selection is complete, before an awaiting argument
    // binds (CompleteArguments: the argument's header can state these Borrows' atoms), and every call's at its node from its plan (a
    // synthesized call's at its consumer's, which commits or replaces the plan).
    private void CallBorrows(Dictionary<BoundOrigin, BorrowList> rows, in BoundArgumentOperation receiver, ReadOnlySpan<BoundArgumentOperation> arguments)
    {
        this.OperationBorrow(rows, receiver);
        foreach (ref readonly var argument in arguments)
        {
            this.OperationBorrow(rows, argument);
        }
    }

    // A call input's, receiver's or payload's operation that HIR publishes as a Borrow (PublishesBorrow): its adapted Type.
    private void OperationBorrow(Dictionary<BoundOrigin, BorrowList> rows, in BoundArgumentOperation operation)
    {
        if (PublishesBorrow(operation))
        {
            this.RecordBorrow(rows, operation.AdaptedType);
        }
    }

    // One Borrow Type into `rows`: BorrowReferent's own domain (a borrow layer, a borrow-only pair layer, a Slice, a UniqSlice); nothing
    // for any other Type or an outer Origin naming no Projection or Anchor atom.
    private void RecordBorrow(Dictionary<BoundOrigin, BorrowList> rows, BoundType? type)
    {
        if (type is not null && this.BorrowReferent(type, out var outer, out var exclusive) is { } referent && outer is not null)
        {
            this.RecordEntry(rows, outer, referent, exclusive);
        }
    }

    // One Borrow by its outer Origin, referent and mode: each Projection or Anchor atom it names (a meet's operands).
    private void RecordEntry(Dictionary<BoundOrigin, BorrowList> rows, BoundOrigin outer, BoundType referent, bool exclusive)
    {
        if (outer.Kind != OriginKind.Intersection)
        {
            this.JoinBorrow(rows, outer, referent, exclusive);
            return;
        }

        for (var i = 0; i < outer.Operands.Count; i++)
        {
            this.JoinBorrow(rows, outer.Operands[i], referent, exclusive);
        }
    }

    private void JoinBorrow(Dictionary<BoundOrigin, BorrowList> rows, BoundOrigin atom, BoundType referent, bool exclusive)
    {
        if (atom.Kind is not (OriginKind.Projection or OriginKind.Anchor))
        {
            return;
        }

        // The write's row: one find in `rows`. Only an atom it has no row for can be the layer's: a pass's part makes no row of a library
        // atom (this check runs before it would, and a mark marks only the Origins its own module's body states), so the layer is read
        // only at a new row.
        if (!rows.TryGetValue(atom, out var row))
        {
            var layer = this.Library.Layer.Part.Borrows;
            if (ReferenceEquals(rows, this.borrows) && !ReferenceEquals(rows, layer) && layer.TryGetValue(atom, out var frozen))
            {
                // A library body's node bound again by another pass (a default argument at a call): an identical re-record writes nothing;
                // any other is a write to the layer's part (contract C).
                if (frozen.Mode(referent) is not { } mode || (exclusive && !mode))
                {
                    throw new InvalidOperationException($"A pass wrote the borrows of the library atom {Describe(atom)} (InternalInvariant; contract C).");
                }

                return;
            }

            rows.Add(atom, row = new());
        }

        if (row.Join(referent, exclusive) && ReferenceEquals(rows, this.borrows))
        {
            this.borrowVersion++;
        }
    }

    // A mark's row in the running part (the one find, else a new row).
    private BorrowList BorrowRow(BoundOrigin atom)
    {
        if (!this.borrows.TryGetValue(atom, out var row))
        {
            this.borrows.Add(atom, row = new());
        }

        return row;
    }

    // The census's input (G147 ruling 28): the borrows the HIR publisher itself writes, at its own write positions, into the publication
    // side, which CheckBorrows compares with the formed rows: two independent producers. At a visited node (HirPublisher.Visit): whether it
    // failed (no extras check then: a failed node's facts may stay unpublished; Binding failed it, or HIR publishes it Error, which for a
    // node Binding resolved is a broken contract the publisher records itself, Hir.Broken), a Borrow node's Type or a Slice view's (a
    // slicing index has no Borrow node; an address's Op is no Borrow, SPEC 5.4), an object upcast's view borrow (ViewBorrow), a Closure's
    // capture entries whose Adjust borrows or Reborrows, a match's Subject borrow and the Pattern bindings whose published Adjust borrows;
    // and its limit marks. The forms after the Borrow node's are disjoint, so one dispatch on the node's form reads each (the publisher's
    // visit runs this at every node: the sealed forms first, then MatchKoto, the one with a subclass, TryKoto, whose test is the dearest).
    private void PublishedNode(Koto node, HirOp op)
    {
        this.publishedFailure |= op == HirOp.Error || node.BindingState is BindingState.Unresolved or BindingState.Invalid;
        if (op == HirOp.Borrow)
        {
            this.RecordBorrow(this.publishedBorrows, node.BoundType);
        }

        switch (node)
        {
            case IndexKoto when this.IsSlicing(node):
                this.RecordBorrow(this.publishedBorrows, node.BoundType);
                break;
            case ConversionKoto conversion:
                if (ViewBorrow(conversion) != HirAdaptation.None && conversion is { BoundType: { Origin: { } origin } view, Left.BoundType.Components: [var payload, ..] })
                {
                    this.RecordEntry(this.publishedBorrows, origin, payload, view.Semantics == SemanticsKind.ObjUniq);
                }

                this.MarkLimits(conversion);
                break;
            case FunctionKoto { IsAnonymous: true, ClosureAsCreated: { } closure }:
                for (var i = 0; i < closure.Captures.Count; i++)
                {
                    if (closure.Captures[i] is var capture && capture.Adjust is { Acquire: HirAcquire.Reborrow } or { Adaptation: HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow })
                    {
                        this.RecordBorrow(this.publishedBorrows, capture.Environment.Type);
                    }
                }

                break;
            case ForKoto loop:
                var iteration = BoundMatch.Of(this, loop);
                this.RecordBorrow(this.publishedBorrows, iteration?.SubjectBorrow);
                this.PublishedBindings(iteration);
                break;
            case MatchKoto match:
                var plan = BoundMatch.Of(this, match);
                this.RecordBorrow(this.publishedBorrows, plan?.SubjectBorrow);
                this.PublishedBindings(plan);
                break;
        }
    }

    // The census's input at a match's or for's plan: the Pattern bindings whose published Adjust borrows.
    private void PublishedBindings(BoundMatch? plan)
    {
        for (var i = 0; plan is { IsCurrent: true } && i < plan.Positions.Count; i++)
        {
            if (plan.Positions[i] is { Kind: BoundPatternKind.Binding, BodySymbol.Type: { } bound } position &&
                HirAdjust.Of(position.Acquisition, position.AccessMode).Adaptation is HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow)
            {
                this.RecordBorrow(this.publishedBorrows, bound);
            }
        }
    }

    // The census's input at the publisher's record of the uses (RowUses.Use): a call input, receiver, payload or writer whose operation HIR
    // publishes as a Borrow (PublishesBorrow), its adapted Type; a user index's call the selection does not take (SelectionMode) counts
    // with its inputs, since Binding committed it.
    private void PublishedOperation(in BoundArgumentOperation operation)
    {
        if (PublishesBorrow(operation))
        {
            this.RecordBorrow(this.publishedBorrows, operation.AdaptedType);
        }
    }

    // The census's input at a child the publisher completes by its own facts (RowUses.Use): its recorded adaptation, any but a referent
    // read.
    private void PublishedAdaptation(Koto child)
    {
        if (this.adaptations.TryGetValue(child, out var recorded))
        {
            this.AdaptationBorrow(recorded, this.publishedBorrows);
        }
    }

    // The census's input at the publisher's call loop for a call the selection does not take: its receiver's and arguments' operations.
    private void PublishedCall(CallPlan call)
    {
        this.PublishedOperation(call.ReceiverOperation);
        foreach (ref readonly var argument in call.ArgumentOperations)
        {
            this.PublishedOperation(argument);
        }
    }

    // A published conversion's limit mark, one row per named limit (LimitMarks): Claimed, a claim's Origin (ClaimedOrigin);
    // SharedAddress, an address's whose authority is shared: its Place's path, folded once (PathFold), is reached through shared (SPEC
    // 15.6.2) or passes a conformance selection, through which the address selects `index` (SPEC 5.4).
    private void MarkLimits(ConversionKoto conversion)
    {
        if (ClaimedOrigin(conversion) is { } claimed)
        {
            this.Mark(claimed, LimitMarks.Claimed);
        }
        else if (conversion is { ConversionBinding: ConversionBinding.Address, BoundType.RawOrigin: { } address } &&
            this.PathFold(conversion.Left, []) is { Access: SemanticsKind.Ref } or { Conformance: true })
        {
            this.Mark(address, LimitMarks.SharedAddress);
        }
    }

    // The one writer of the marks: `mark` on each atom `stated` names (each operand of a meet), in the running part; whether a row newly
    // took it (FittedExclusive's fixed point ends on none).
    private bool Mark(BoundOrigin stated, LimitMarks mark)
    {
        var added = false;
        for (var i = 0; i < (stated.Kind == OriginKind.Intersection ? stated.Operands.Count : 1); i++)
        {
            var row = this.BorrowRow(stated.Kind == OriginKind.Intersection ? stated.Operands[i] : stated);
            added |= (row.Marks & mark) == 0;
            row.Marks |= mark;
        }

        return added;
    }

    // SPEC 5.2.1 condition 3, 5.7 Authority (judged where the Origin is bound; Z6 Revision 8): whether a pointer whose raw layer states
    // `origin` may carry write authority: an atom marked FittedExclusive may; one whose members where it is bound (AtomMembers) keep only
    // shared Loans (SharedOnly, the binder reading's rule) may not; a local region with none (a raw local's, `let q = r@follow@raw`) is
    // judged by its finite sources (LocalRegionSources: never a region of its own body, so the recursion ends), may where it has none; a
    // meet may where an operand may; any other unanswered Origin may.
    private bool MayWrite(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (this.MayWrite(origin.Operands[i]))
                {
                    return true;
                }
            }

            return false;
        }

        if ((this.AtomMarks(origin) & LimitMarks.FittedExclusive) != 0)
        {
            return true;
        }

        this.fitMembers.Clear();
        var answered = this.AtomMembers(origin, this.fitMembers) && this.fitMembers.Count > 0;
        if (answered || !IsLocalRegion(origin))
        {
            return !SharedOnly(answered, CollectionsMarshal.AsSpan(this.fitMembers));
        }

        var sources = this.LocalRegionSources(origin);
        for (var i = 0; i < sources.Length; i++)
        {
            if (this.MayWrite(sources[i]))
            {
                return true;
            }
        }

        return sources.Length == 0;
    }

    // Whether a finite source of a region graph row (LocalRegionSources) is marked FittedExclusive.
    private bool SourceMarked(BoundOrigin region)
    {
        foreach (var source in this.LocalRegionSources(region))
        {
            if ((this.AtomMarks(source) & LimitMarks.FittedExclusive) != 0)
            {
                return true;
            }
        }

        return false;
    }

    // The publication's start (PublishPass): its publication side emptied and the running part's marks cleared, so a republication
    // reads none of an earlier one; then the FittedIntoHolder row, formed from the pass's raw fits once, every fit being decided
    // before the publication: each atom a raw fit's target states and, at an invariant position, each its value states; then
    // FittedExclusive's least fixed point over the raw fits and the region graph's rows (each round marks an atom or is the last).
    private void BeginBorrowPublication()
    {
        this.publishedFailure = false;
        foreach (var row in this.publishedBorrows.Values)
        {
            row.Reset();
        }

        foreach (var row in this.borrows.Values)
        {
            row.Marks = LimitMarks.None;
        }

        foreach (var fit in this.rawFits)
        {
            this.Mark(fit.Target, LimitMarks.FittedIntoHolder);
            if (fit.Equality)
            {
                this.Mark(fit.Value, LimitMarks.FittedIntoHolder);
            }
        }

        for (var changed = this.rawFits.Count != 0; changed;)
        {
            changed = false;
            foreach (var fit in this.rawFits)
            {
                changed |= this.MayWrite(fit.Value) && this.Mark(fit.Target, LimitMarks.FittedExclusive);
                changed |= fit.Equality && this.MayWrite(fit.Target) && this.Mark(fit.Value, LimitMarks.FittedExclusive);
            }

            // Each row of the region graph: a local's region and every bounded target (a Projection, an Anchor, a meet).
            foreach (var region in this.localRegions.Keys)
            {
                changed |= (this.AtomMarks(region) & LimitMarks.FittedExclusive) == 0 && this.SourceMarked(region) &&
                    this.Mark(region, LimitMarks.FittedExclusive);
            }
        }
    }

    // The census at the publication's end: every published node's borrow fact is in the rows with at least its mode (a miss), and, where
    // the publication met no failed node, every row's entry is a published node's with exactly its mode (an extra).
    private void CheckBorrows()
    {
        foreach (var (atom, published) in this.publishedBorrows)
        {
            foreach (var member in published.Items)
            {
                if (this.borrows.GetValueOrDefault(atom)?.Mode(member.Referent) is not { } mode || (member.Exclusive && !mode))
                {
                    throw new InvalidOperationException($"The published Borrow of `{member.Referent}` under `{Describe(atom)}` was not formed (InternalInvariant; PLAN G148 (1)).");
                }
            }
        }

        if (this.publishedFailure || this.issues.Count + this.derivedIssues.Count != 0)
        {
            return;
        }

        foreach (var (atom, row) in this.borrows)
        {
            foreach (var member in row.Items)
            {
                if (this.publishedBorrows.GetValueOrDefault(atom)?.Mode(member.Referent) != member.Exclusive)
                {
                    throw new InvalidOperationException($"The formed Borrow of `{member.Referent}` under `{Describe(atom)}` was not published (InternalInvariant; PLAN G148 (1)).");
                }
            }
        }
    }

    // The running pass's rows, emptied in place (ResetPass); the version starts again per run (BeginRun).
    private void ResetBorrows()
    {
        foreach (var row in this.borrows.Values)
        {
            row.Reset();
        }
    }

    // An atom's Borrows in one pass, equal referents joined, and its limit marks; reused across passes (a warm pass allocates nothing).
    internal sealed class BorrowList
    {
        private RawMember[] items = new RawMember[2];

        internal int Count { get; private set; }

        internal LimitMarks Marks { get; set; }

        internal ReadOnlySpan<RawMember> Items => this.items.AsSpan(0, this.Count);

        // Whether a Borrow of `referent` is listed, and its joined mode; null when none is.
        internal bool? Mode(BoundType referent)
        {
            foreach (var member in this.Items)
            {
                if (ReferenceEquals(member.Referent, referent))
                {
                    return member.Exclusive;
                }
            }

            return null;
        }

        // Joins a Borrow of `referent`; whether the list changed.
        internal bool Join(BoundType referent, bool exclusive)
        {
            for (var i = 0; i < this.Count; i++)
            {
                if (ReferenceEquals(this.items[i].Referent, referent))
                {
                    if (this.items[i].Exclusive || !exclusive)
                    {
                        return false;
                    }

                    this.items[i] = this.items[i] with { Exclusive = true };
                    return true;
                }
            }

            if (this.Count == this.items.Length)
            {
                Array.Resize(ref this.items, this.Count * 2);
            }

            this.items[this.Count++] = new(-1, referent, exclusive);
            return true;
        }

        internal void Reset() => (this.Count, this.Marks) = (0, LimitMarks.None);
    }
}
