// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding's obligations (DECISIONS BU-85). Responsibility: record each obligation a rule requires (AddObligation), one entry per
// distinct obligation, and judge at the Final pass the obligations still recorded. Inputs: the obligations the Type, Origin, length,
// call and erasure rules form while binding. Outputs: Binding.Obligations, read by the Origin judgment, and the failures published at
// the obligations' uses. Invariants: an obligation with an Unstated end is never recorded (a Refuted one is a problem of its use, which
// it leaves Resolved: RecordResolvedOrigin); each is stamped with the body Binding formed it in, but an Origin relation formed in a
// body with an end foreign to it (ForeignEnd) is its nested signature's declaration row (R7-O3 U-O3); a Definition obligation still
// recorded at Final fails its use; an Owned obligation is judged at Final by its discharge: an erasure's Owned condition deferred while
// a body is bound (Judgment), its failure the erasure's Language record at its use (JudgeErasedOwned), or a result slot's default
// deferred at its last resolution (ResultDefault), its failure the slot's missing Origin (JudgeDeferredDefault); any other obligation
// but an Origin relation with both ends is proven or discharged by Final unless it rests on a failed part, and one that is not fails
// its use as InternalInvariant_Kd. Prohibitions: no consumer decides an obligation's verdict from its own reading; it reads Binding's.
public sealed partial class Binding
{
    internal bool IsVerifiedLengthObligation(BindingObligation obligation)
    {
        if (obligation is not { Kind: BindingObligationKind.TypeFormation, Deadline: BindingDeadline.Instantiation, Length: { } length } ||
            obligation.Use.BindingState != BindingState.Resolved || obligation.Use.BoundType is not { IsInteger: true })
        {
            return false;
        }

        var function = this.ConstraintScope(obligation.Use).Function;
        return (function is not null && IsSignatureLength(obligation.Use, function)) || this.ProveLength(length, function);
    }

    // Whether the local whose Type or initializer holds `use` bound both, so that BindVariableCore discharged the inferences of the
    // Origins its Type omits (ResolveInitializerOrigins, InferLocalOrigins; SPEC 15.3.6, 15.4.4).
    private static bool LocalBound(Koto use)
    {
        for (var current = use.Parent; current is not null; current = current.Parent)
        {
            if (current is VariableKoto local)
            {
                return local.InitializerKoto?.BindingState == BindingState.Resolved && local.TypeKoto?.BindingState is null or BindingState.Resolved;
            }
        }

        return false;
    }

    // SPEC 8.1.1, 8.1.2, 23.1.2: the Final pass's verdict on the obligations still recorded. A Definition obligation's use fails with
    // its Language cause (FailTypeRole); an Owned obligation is judged here by its discharge (an erasure's, JudgeErasedOwned, SPEC
    // 15.2.3, 8.3.3; a result slot's default, JudgeDeferredDefault, SPEC 15.4.4); an Origin relation with both ends is the Origin
    // judgment's. Any other obligation is proven where it forms or discharged when its local binds, so one left over parts that bound
    // is a broken contract at its use (InternalInvariant_Kd); over a failed part it rests on that failure (rule W, BU-79).
    private void JudgeObligations()
    {
        for (var i = 0; i < this.obligations.Count; i++)
        {
            var obligation = this.obligations[i];
            if (obligation.Deadline == BindingDeadline.Definition)
            {
                this.FailTypeRole(obligation);
            }
            else if (obligation is { Kind: BindingObligationKind.Owned, Discharge: OriginDischarge.ResultDefault })
            {
                this.JudgeDeferredDefault(obligation);
            }
            else if (obligation.Kind == BindingObligationKind.Owned)
            {
                this.JudgeErasedOwned(obligation);
            }
            else if (this.IsUnjudged(obligation))
            {
                this.Fail(obligation.Use, BindingFailure.Internal);
            }
        }
    }

    // DECISIONS BU-85: a length not proven at its resolved use, an Origin inference unresolved although its local bound, or any form
    // other than an Origin relation with both ends.
    private bool IsUnjudged(in BindingObligation obligation) => obligation switch
    {
        { Discharge: OriginDischarge.CallBorrows } or { Kind: BindingObligationKind.OriginOutlives, Longer: not null, Shorter: not null } => false,
        { Length: not null } => obligation.Use.BindingState == BindingState.Resolved && !this.IsVerifiedLengthObligation(obligation),
        { Kind: BindingObligationKind.OriginInference } => LocalBound(obligation.Use) && !this.IsVerifiedOriginObligation(obligation),
        _ => true,
    };

    // SPEC 15.6.1, 15.6.5 (R7-O1; DECISIONS draft "A body's Origin relations are judged by A5 alone"): the one rule for a relation formed
    // in an executable body: Binding judges none; it is a row of the body's C1-F4 view, which A5 judges, unless it is a fixed-fixed
    // relation the fit's answer proves both ways. In order: (1) a meet at the longer end of a relation that is no body chain (every operand
    // fixed) and does not fit as a whole decomposes, each operand its own relation (SPEC 15.3.6); a body chain's meet stays one row, whose
    // node and edge per operand A5's MeetNode rule gives (2a0-D D15); (2) the fit's answer at the use, both directions for `==`, by
    // FitConjunct: outside candidate trials and collected fits FitOriginOutlives, which bounds an open initializer's omitted Origin
    // (SPEC 15.4.4), inside them the pure FitsOriginOutlives; (3) a body chain (an end not fixed in the body, an Unbound end included,
    // or a region chain) is the row whatever (2) said, since its obligation bounds the region graph (RecordLocalRegionBound), its ends
    // read as the fit reads them, a `let` region of the use's body as its value (RegionValueAtUse; PLAN G492-a (ii)); (4) a fixed-fixed
    // relation (2) proves needs no row; (5) any other is the row, through AddObligation, whose Unstated rule is SPEC 5.7's. Before
    // all of these, a relation that holds by identity in each direction it requires (HoldsByIdentity) is no row, a chain's included.
    private void FormBodyRelation(in BindingObligation row)
    {
        var (at, longer, shorter, equality, condition) = (row.Use, row.Longer!, row.Shorter!, row.Equality, row.Condition);
        if (HoldsByIdentity(longer, shorter) && (!equality || HoldsByIdentity(shorter, longer)))
        {
            return;
        }

        var chain = BodyChain(this.RegionValueAtUse(longer, at), this.RegionValueAtUse(shorter, at), this.formingBody);
        if (!chain && !equality && longer.Kind == OriginKind.Intersection && !this.FitConjunct(this.OriginAtUse(longer, at), this.OriginAtUse(shorter, at), at, default, condition))
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                this.FormBodyRelation(row with { Longer = longer.Operands[i] });
            }

            return;
        }

        var forward = this.FitConjunct(this.OriginAtUse(longer, at), this.OriginAtUse(shorter, at), at, default, condition);
        var backward = !equality || this.FitConjunct(this.OriginAtUse(shorter, at), this.OriginAtUse(longer, at), at, default, condition);
        if (chain || !forward || !backward)
        {
            this.AddObligation(row);
        }
    }

    private void AddObligation(BindingObligation obligation)
    {
        // Rulings S4, design-final §13 A2: a relation with an Unstated end forms no obligation; a Refuted one is a problem of its use,
        // its Unstated end first, that leaves the use Resolved (R7-O1 B4; SPEC 15.6.1: a failed relation adds no constraint, so the
        // use's Loans reach MIR; 23.1.2: its other records are independent).
        if (obligation is { Longer: { } longer, Shorter: { } shorter } && UnstatedRelation(longer, shorter) is { } holds)
        {
            if (!holds || (obligation.Equality && UnstatedRelation(shorter, longer) == false))
            {
                var at = obligation.Site ?? obligation.Use; // Where A5 reports the row's record (H11b D5).
                // The record states the producer's kind: a declared clause, a well-formedness relation or a fit.
                var fact = holds ? new OriginRelationFact(at, shorter, this.SuppliedAtUse(shorter, at), longer, Equality: true, obligation.Type, Refuted: true, obligation.Clause, WellFormed: obligation.WellFormed)
                    : new(at, longer, this.SuppliedAtUse(longer, at), shorter, obligation.Equality, obligation.Type, Refuted: true, obligation.Clause, WellFormed: obligation.WellFormed);
                this.RecordResolvedOrigin(at, fact);
            }

            return;
        }

        // The body Binding formed it in (DECISIONS R4-M101 E1), except a relation formed in a body with an end the view's row 1 classes
        // foreign to it (a nested signature's per-call Origin): that signature's declaration row (SPEC 15.3.4; R7-O3 U-O3), stamped with
        // the declaration judge's frame, where it bounds no local region (BoundableIn), for JudgeDeclarationRows to judge at its use.
        obligation = obligation with { Body = this.formingBody.IsBody && this.ForeignEnd(obligation) ? ExecutableBody.Declarations : this.formingBody };
        if (this.obligationSet.Add(new(obligation)))
        {
            this.obligations.Add(obligation);
            this.originStateVersion++;
            this.RecordLocalRegionBound(obligation);
        }
    }

    // The obligation list's dedup key (R7-N5 B8, the Front's C-521 form (d); DECISIONS BU-220): the obligation with each node it names
    // as its SourceRef (B7's per-pass memo form), equal exactly when the obligations are, since every such node is numbered when its
    // obligation forms (the probe: none without an id). The set is its pass part's, emptied each pass (ResetPass), so no key outlives a
    // numbering, and a library pass's set keys no library node.
    internal readonly record struct ObligationKey(BindingObligationKind Kind, SourceRef Use, BindingDeadline Deadline, BoundType? Type, BoundOrigin? Longer, BoundOrigin? Shorter, BoundLength? Length, bool Equality, SourceRef Clause, bool WellFormed, OriginDischarge Discharge, ulong Condition, SourceRef FixedBy, SourceRef Site, ExecutableBody Body, bool Compared)
    {
        internal ObligationKey(in BindingObligation obligation)
            : this(obligation.Kind, SourceRef.Written(obligation.Use), obligation.Deadline, obligation.Type, obligation.Longer, obligation.Shorter, obligation.Length, obligation.Equality, SourceRef.Written(obligation.Clause), obligation.WellFormed, obligation.Discharge, obligation.Condition, SourceRef.Written(obligation.FixedBy), SourceRef.Written(obligation.Site), obligation.Body, obligation.Compared)
        {
            Debug.Assert(
                Numbered(obligation.Use, this.Use) && Numbered(obligation.Clause, this.Clause) && Numbered(obligation.FixedBy, this.FixedBy) && Numbered(obligation.Site, this.Site),
                "Every node an obligation key names is numbered: an unnumbered one would key as no node (SourceRef.Written's default).");
        }

        private static bool Numbered(Koto? node, SourceRef id) => node is null || id != default;
    }

    // Whether an Origin relation has an end that ForeignDeclarationAtom classes foreign to the forming body, a meet by its operands as
    // ClassifyMeet classes them (a Closure's result defaulting to the meet of its borrowed inputs, SPEC 15.4.3 rule 1; meets are flat).
    // A call's argument fit relates the call's images and regions of its callee's Origins, never the callee's own atoms (a composite's
    // value Type formed at the trial's parameter is completed at the call's, CompleteOperationTypes), so no call forms such a row.
    private bool ForeignEnd(in BindingObligation obligation)
    {
        if (obligation is not { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: { } shorter })
        {
            return false;
        }

        return Foreign(longer) || Foreign(shorter);

        bool Foreign(BoundOrigin end)
        {
            for (var i = 0; i < end.Operands.Count; i++)
            {
                if (this.ForeignDeclarationAtom(end.Operands[i], this.formingBody))
                {
                    return true;
                }
            }

            return this.ForeignDeclarationAtom(end, this.formingBody);
        }
    }
}
