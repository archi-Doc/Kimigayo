// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin-atom borrows (SPEC 5.7 Origins: "the outer Origin of a borrow value (an input's or, in a body, a local's) [belongs] to that
// borrow"; 15.6.5: a finite Origin local to a body, a Borrow's; contract of Binding.RawOrigins.cs; originpub design Revision 3, G147
// rulings 18 and 19; DECISIONS draft of PLAN G148 (1), Z6).
// - Responsibility: for each Projection or Anchor atom, the Borrows Binding commits with it as their outer Origin (each such operand of a
//   meet), each as RawMember(-1, referent, exclusive) by BorrowReferent of its Type, equal referents joining their modes, in commit order;
//   and for any atom a published claim's Type states, its Claimed mark (5.7 Fits and claims).
// - Inputs: the borrow facts Binding commits for a node, read by the one enumeration NodeBorrows (its written Borrow, Slice view or object
//   view, its recorded adaptation, its receiver operation, its calls' operations, its payload operations, its Subject and by-reference
//   bindings, its capture entries), at each writer of those facts, outside candidate trials and collected fits (FitConjunct's rule):
//   BindNode's end for the facts the node's own binding writes, an adaptation's record at the node, and Check's acquisitions of a
//   match's or a user iteration's bindings (AcquireBinding's callers); and the claims the HIR publisher publishes (MarkClaim).
// - Outputs: Borrows(atom, members, out claimed), read by the borrows' readers through OriginMembers, and RetentionState, the key field of
//   TyCtxt's retention memo: the region and borrow versions until the pass's last borrow writer (Check) has run, Complete afterwards.
// - Invariants: an atom's forming Borrow is committed, so formed, before any Type stating the atom can be read (a typed path, a Field
//   read or a substitution states the atom only from a Type that carries it), except a Pattern binding's Borrow, whose acquisition
//   Check decides after the bodies bind: while they bind, its atom's row lacks it; the rows only grow within a pass, each new entry or changed
//   mode moving borrowVersion (a mark does not: no versioned reader reads marks); a library body's rows are the layer's part, written only
//   by the library pass, an identical re-record by another pass writing nothing; at the publication the census (CheckBorrows) reads the
//   same enumeration over every published node: misses always, extras and modes exactly where the publication met no failed node, a
//   difference the internal-invariant form.
// - Rule (SPEC 5.4): an address `P@raw` is no Borrow ("checks like an immediately ending @ref ... and leaves no Loan"): NodeBorrows reads
//   no borrow fact of it.
// - Prohibitions: no entry for another atom kind, none in a candidate trial or a collected fit, no mode from syntax, no default entry, no
//   second list of borrow facts; RegionVersion is not moved; no reader but OriginMembers, the validator and tests.
public sealed partial class Binding
{
    // RetentionState's value once the pass's borrow writers have run: outside every sum of the two versions within a run.
    internal const int RetentionComplete = int.MaxValue;

    // The publication side of CheckBorrows (the published borrow positions), reused.
    private readonly Dictionary<BoundOrigin, BorrowList> publishedBorrows = new(ReferenceEqualityComparer.Instance);

    // Whether the publication met a failed node (Binding's Unresolved or Invalid, or published Error), whose Borrows HIR may not publish:
    // no extras check then.
    private bool publishedFailure;

    // The running pass's part of the rows (PassPart.Borrows, EnterPass).
    private Dictionary<BoundOrigin, BorrowList> borrows;
    private int borrowVersion;

    // Whether the running pass's borrow writers have all run (RunPass after Check, whose acquisitions are the last; StartPass clears
    // it): true between Binds.
    private bool borrowsComplete;

    // The key field of TyCtxt's retention memo (DECISIONS G570, as the publication amends it): until the rows are complete, the region
    // graph's and the borrows' versions, which only rise within a run, so distinct states have distinct sums; Complete afterwards, which
    // no mid-binding answer has.
    internal int RetentionState => this.borrowsComplete ? RetentionComplete : this.regionVersion + this.borrowVersion;

    // The program part's rows as tests read them (TyCtxtTest), every pass's rows being their own part's.
    internal IEnumerable<KeyValuePair<BoundOrigin, BorrowList>> ProgramBorrows => this.programPart.Borrows;

    // SPEC 5.7 Fits and claims: a claim, `E@(raw/U during o)` in an unsafe context, a pointer conversion whose written target states an
    // Origin; that Origin, null for any other node. The one classification of a claim (the publication's marks read it).
    internal static BoundOrigin? ClaimedOrigin(Koto node)
        => node is ConversionKoto { ConversionBinding: ConversionBinding.Pointer, Right.BoundType: { } target } conversion &&
            ReferenceTypes.IsPointer(target) && target.RawOrigin is { } stated && KotoHelper.IsUnsafeContext(conversion) ? stated : null;

    // The Borrows of `atom`, appended to `members`, and its Claimed mark; false where the atom has no entry (its reader's limit, never a
    // default).
    internal bool Borrows(BoundOrigin atom, List<RawMember> members, out bool claimed)
    {
        _ = PassPart.Find(PassPart.Parts(this.Library.Layer.Part.Borrows, this.borrows), atom, out var row);
        claimed = row?.Claimed ?? false;
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

    // An atom as the validator's message names it: its kind, slot and binder's source location.
    private static string Describe(BoundOrigin atom) => $"{atom.Kind} {atom.Slot} at {atom.Binder?.CodeContext.SourceDocument?.Path}:{atom.Binder?.Span}";

    // Whether HIR publishes a call input's operation as a Borrow (HirAdjust.Of's borrow adaptations, or a same-mode Reborrow, which it
    // publishes as an acquisition): NodeBorrows' one test of an operation.
    private static bool PublishesBorrow(in BoundArgumentOperation operation)
        => operation.Kind == ArgumentOperationKind.Reborrow || HirAdjust.Of(operation) is HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow or
            HirAdaptation.BaseBorrow or HirAdaptation.PayloadProjection or HirAdaptation.ReferenceRead;

    // Revision 3's formation (ruling 19): a node's committed borrow facts into the running part, outside candidate trials and collected
    // fits (their facts are the selected candidate's, read when its node is bound), at each writer: BindNode's end, an adaptation's
    // record and Check's binding acquisitions. Idempotent: a re-formed entry joins.
    private void FormNodeBorrows(Koto node)
    {
        if (this.candidateTrials == 0 && !this.collectingFits)
        {
            this.NodeBorrows(node, this.borrows);
        }
    }

    // The one enumeration of the borrow facts Binding commits for `node` (the facts HIR publishes, BU-27's HIR contract), into `rows`:
    // formation (FormNodeBorrows) and the census (PublishedNode) read it alike. An address is none (SPEC 5.4).
    private void NodeBorrows(Koto node, Dictionary<BoundOrigin, BorrowList> rows)
    {
        // A written Borrow (`@ref`, `@uniq`, `@objref`, `@objuniq`, a raw Place's) and a Slice view (a slicing index): the node's own Type.
        if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } || (node is IndexKoto && ElementAccess.IsSlicing(this, node)))
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

        // The adaptation recorded at the node (any but a referent read) and its receiver operation.
        if (this.adaptations.TryGetValue(node, out var recorded) && recorded.Kind != ExpectedAdaptationKind.ReferentRead)
        {
            this.RecordBorrow(rows, recorded.Type);
        }

        if (this.receiverOperations.TryGetValue(node, out var receiver))
        {
            this.OperationBorrow(rows, receiver);
        }

        // Each call of the node, a user index's two included (the publication's SelectionMode takes one), and an enum construction's
        // payload operations.
        for (var i = 0; CallPlan.Of(this, node, i) is { } call; i++)
        {
            this.OperationBorrow(rows, call.ReceiverOperation);
            foreach (ref readonly var argument in call.ArgumentOperations)
            {
                this.OperationBorrow(rows, argument);
            }
        }

        if (this.TryGetEnumConstruction(node, out var construction))
        {
            foreach (ref readonly var operation in construction!.PayloadOperations)
            {
                this.OperationBorrow(rows, operation);
            }
        }

        // A match's Subject borrow, and a match's or for's by-reference bindings.
        var plan = node switch { MatchKoto match => BoundMatch.Of(this, match), ForKoto loop => BoundMatch.Of(this, loop), _ => null };
        this.RecordBorrow(rows, plan?.SubjectBorrow);
        for (var i = 0; plan is { IsCurrent: true } && i < plan.Positions.Count; i++)
        {
            if (plan.Positions[i] is { Kind: BoundPatternKind.Binding, Acquisition: PatternAcquisition.Borrow, BodySymbol.Type: { } bound })
            {
                this.RecordBorrow(rows, bound);
            }
        }

        // A Closure's capture entries that borrow or Reborrow (their Adjust).
        if (node is FunctionKoto { IsAnonymous: true, ClosureAsCreated: { } closure })
        {
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                if (closure.Captures[i] is var capture && capture.Adjust is { Acquire: HirAcquire.Reborrow } or { Adaptation: HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow })
                {
                    this.RecordBorrow(rows, capture.Environment.Type);
                }
            }
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

        if (this.BorrowRow(rows, atom).Join(referent, exclusive) && ReferenceEquals(rows, this.borrows))
        {
            this.borrowVersion++;
        }
    }

    // A write's row (the parts' one find, else a new row in `rows`); a row of the layer's part written by another pass is a broken
    // contract (contract C).
    private BorrowList BorrowRow(Dictionary<BoundOrigin, BorrowList> rows, BoundOrigin atom)
    {
        if (!rows.TryGetValue(atom, out var row))
        {
            rows.Add(atom, row = new());
        }

        return row;
    }

    // The census at one published node (CheckBorrows): the one enumeration into the publication side, the node's claim mark, and whether
    // the node failed (no extras check then: a failed node's facts may stay unpublished): Binding failed it, or HIR publishes it Error,
    // which for a node Binding resolved is a broken contract the publisher records itself (Hir.Broken).
    private void PublishedNode(Koto node, HirOp op)
    {
        this.publishedFailure |= op == HirOp.Error || node.BindingState is BindingState.Unresolved or BindingState.Invalid;
        this.NodeBorrows(node, this.publishedBorrows);
        this.MarkClaim(node);
    }

    // A published claim: its Origin's atoms, as its Type states them (each operand of a meet), marked Claimed in the running part.
    private void MarkClaim(Koto node)
    {
        if (ClaimedOrigin(node) is not { } claimed)
        {
            return;
        }

        for (var i = 0; i < (claimed.Kind == OriginKind.Intersection ? claimed.Operands.Count : 1); i++)
        {
            this.BorrowRow(this.borrows, claimed.Kind == OriginKind.Intersection ? claimed.Operands[i] : claimed).Claimed = true;
        }
    }

    // The publication's start (PublishPass): its publication side emptied and the running part's marks cleared, so a republication
    // reads none of an earlier one.
    private void BeginBorrowPublication()
    {
        this.publishedFailure = false;
        foreach (var row in this.publishedBorrows.Values)
        {
            row.Reset();
        }

        foreach (var row in this.borrows.Values)
        {
            row.Claimed = false;
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

    // An atom's Borrows in one pass, equal referents joined, and its Claimed mark; reused across passes (a warm pass allocates nothing).
    internal sealed class BorrowList
    {
        private RawMember[] items = new RawMember[2];

        internal int Count { get; private set; }

        internal bool Claimed { get; set; }

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

        internal void Reset() => (this.Count, this.Claimed) = (0, false);
    }
}
