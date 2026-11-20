// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin-atom borrows (SPEC 5.7 Origins: "the outer Origin of a borrow value (an input's or, in a body, a local's) [belongs] to that
// borrow"; 15.6.5: a finite Origin local to a body, a Borrow's; contract of Binding.RawOrigins.cs; DECISIONS draft of PLAN G148 (1), Z6).
// - Responsibility: for each Projection or Anchor atom, the Borrows Binding forms with it as their outer Origin (each such operand of a
//   meet), each as RawMember(-1, referent, exclusive) by BorrowReferent of its Type, equal referents joining their modes, in formation
//   order; and for any atom a published claim's Type states, its Claimed mark (5.7 Fits and claims).
// - Inputs: the Types FormBorrow receives at BU-27's naming sites outside candidate trials and collected fits (FitConjunct's rule), a
//   selected call's operations and position operations after selection (RecordSelectedBorrows, beside RecordOpenings), and the claims the
//   HIR publisher publishes (MarkClaims).
// - Outputs: Borrows(atom, members, out claimed), read by the borrows' readers through OriginMembers, and RetentionState, the key field of
//   TyCtxt's retention memo: the region and borrow versions while the pass's bodies bind, Complete once they are bound.
// - Invariants: an atom's forming Borrow is recorded before any Type stating the atom can be read (a Borrow's Type is formed before any
//   node, adaptation or Type carries it; a typed path, a Field read or a substitution states the atom only from a Type that carries it);
//   the rows only grow within a pass, each new entry or changed mode moving borrowVersion (a mark does not: no versioned reader reads
//   marks); a library body's rows are the layer's part, written only by the library pass; at the publication the rows equal the
//   published borrow positions (CheckBorrows): misses always, extras and modes exactly in a body without a Binding failure, a difference
//   the internal-invariant form.
// - Prohibitions: no entry for another atom kind, none in a candidate trial or a collected fit, no mode from syntax, no default entry;
//   RegionVersion is not moved; no reader but OriginMembers, the validator and tests.
public sealed partial class Binding
{
    // RetentionState's value once the pass's bodies are bound: outside every sum of the two versions within a run.
    internal const int RetentionComplete = int.MaxValue;

    // The publication side of CheckBorrows (the published borrow positions), reused.
    private readonly Dictionary<BoundOrigin, BorrowList> publishedBorrows = new(ReferenceEqualityComparer.Instance);

    // Whether the publication met a node Binding failed (Unresolved or Invalid), whose Borrows HIR may not publish: no extras check then.
    private bool publishedFailure;

    // The running pass's part of the rows (PassPart.Borrows, EnterPass).
    private Dictionary<BoundOrigin, BorrowList> borrows;
    private int borrowVersion;

    // Whether the running pass's bodies are bound (RunPass after BindBodies; StartPass clears it): true between Binds.
    private bool bodiesBound;

    // The key field of TyCtxt's retention memo (DECISIONS G570, as the publication amends it): while the pass's bodies bind, the region
    // graph's and the borrows' versions, which only rise within a run, so distinct states have distinct sums; Complete afterwards, which
    // no mid-binding answer has.
    internal int RetentionState => this.bodiesBound ? RetentionComplete : this.regionVersion + this.borrowVersion;

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
    // publishes as an acquisition): the one test of both sides, formation (RecordSelectedBorrows) and publication (CheckBorrows).
    private static bool PublishesBorrow(in BoundArgumentOperation operation)
        => operation.Kind == ArgumentOperationKind.Reborrow || HirAdjust.Of(operation) is HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow or
            HirAdaptation.BaseBorrow or HirAdaptation.PayloadProjection or HirAdaptation.ReferenceRead;

    // A naming site's Borrow Type, recorded under the atoms its outer Origin names unless a candidate trial or a collected fit forms it
    // (their Borrows are the selected candidate's, RecordSelectedBorrows).
    private BoundType FormBorrow(BoundType formed)
    {
        if (this.candidateTrials == 0 && !this.collectingFits)
        {
            this.RecordBorrow(this.borrows, formed);
        }

        return formed;
    }

    // SPEC 10.1, 15.6.1: the selected candidate's Borrows, formed in its trial: each operation's adapted Type, and each position's.
    private void RecordSelectedBorrows(ReadOnlySpan<BoundArgumentOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (PublishesBorrow(operation) && operation.AdaptedType is { } adapted)
            {
                _ = this.FormBorrow(adapted);
            }
        }
    }

    // One Borrow Type into `rows`: BorrowReferent's own domain (a borrow layer, a borrow-only pair layer, a Slice, a UniqSlice); nothing
    // for any other Type or an outer Origin naming no Projection or Anchor atom.
    private void RecordBorrow(Dictionary<BoundOrigin, BorrowList> rows, BoundType? type)
    {
        if (type is null || this.BorrowReferent(type, out var outer, out var exclusive) is not { } referent || outer is null)
        {
            return;
        }

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

    // The publication side's positions at a node (CheckBorrows): a Borrow node's Type, a Slice view's (a slicing index has no Borrow node),
    // a Closure's capture borrows (its entries' Types where their Adjust borrows or Reborrows); and a claim's mark.
    private void PublishedNode(Koto node, HirOp op)
    {
        this.publishedFailure |= node.BindingState is BindingState.Unresolved or BindingState.Invalid;
        if (op == HirOp.Borrow || (node is IndexKoto && ElementAccess.IsSlicing(this, node)))
        {
            // An address (`@raw`) borrows its operand as its target's Type records (Conversions.Raw), its own Type being the pointer.
            this.PublishedBorrow(node is ConversionKoto { ConversionBinding: ConversionBinding.Address } address ? address.Right.BoundType : node.BoundType);
        }

        if (node is FunctionKoto { IsAnonymous: true, ClosureAsCreated: { } closure })
        {
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                if (closure.Captures[i] is var capture && capture.Adjust is { Acquire: HirAcquire.Reborrow } or { Adaptation: HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow })
                {
                    this.PublishedBorrow(capture.Environment.Type);
                }
            }
        }

        this.MarkClaim(node);
    }

    // A call input's operation that HIR publishes as a Borrow (PublishesBorrow): its adapted Type.
    private void PublishedOperation(in BoundArgumentOperation operation)
    {
        if (PublishesBorrow(operation))
        {
            this.PublishedBorrow(operation.AdaptedType);
        }
    }

    // A child's recorded adaptation that borrows (any but a referent read): its Type.
    private void PublishedAdaptation(Koto child)
    {
        if (this.adaptations.TryGetValue(child, out var recorded) && recorded.Kind != ExpectedAdaptationKind.ReferentRead)
        {
            this.PublishedBorrow(recorded.Type);
        }
    }

    // A match's Subject borrow.
    private void PublishedSubject(BoundType? borrow) => this.PublishedBorrow(borrow);

    // The publication side: one published borrow position's Type (CheckBorrows).
    private void PublishedBorrow(BoundType? type) => this.RecordBorrow(this.publishedBorrows, type);

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

    // I4 at the publication's end: every published borrow position is in the rows with at least its mode (a miss), and, where the pass
    // published no Binding failure, every row's entry is published with exactly its mode (an extra).
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
