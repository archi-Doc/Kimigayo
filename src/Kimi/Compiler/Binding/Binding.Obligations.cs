// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding's obligations (DECISIONS BU-85). Responsibility: record each obligation a rule requires (AddObligation), one entry per
// distinct obligation, and judge at the Final pass the obligations still recorded. Inputs: the obligations the Type, Origin, length
// and call rules form while binding. Outputs: Binding.Obligations, read by the Origin judgment, and the failures published at the
// obligations' uses. Invariants: an obligation with an Unstated end is never recorded (a Refuted one fails at its use); a Definition
// obligation still recorded at Final fails its use; any other obligation but an Origin relation with both ends is proven or
// discharged by Final unless it rests on a failed part, and one that is not fails its use as InternalInvariant_Kd. Prohibitions: no
// consumer decides an obligation's verdict from its own reading; it reads Binding's.
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
    // its Language cause (FailTypeRole); an Origin relation with both ends is the Origin judgment's. Any other obligation is proven
    // where it forms or discharged when its local binds, so one left over parts that bound is a broken contract at its use
    // (InternalInvariant_Kd); over a failed part it rests on that failure (rule W, BU-79).
    private void JudgeObligations()
    {
        for (var i = 0; i < this.obligations.Count; i++)
        {
            var obligation = this.obligations[i];
            if (obligation.Deadline == BindingDeadline.Definition)
            {
                this.FailTypeRole(obligation);
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

    private void AddObligation(BindingObligation obligation)
    {
        // Rulings S4, design-final §13 A2: a relation with an Unstated end forms no obligation; a Refuted one is recorded at its use,
        // its Unstated end first.
        if (obligation is { Longer: { } longer, Shorter: { } shorter } && UnstatedRelation(longer, shorter) is { } holds)
        {
            if (!holds || (obligation.Equality && UnstatedRelation(shorter, longer) == false))
            {
                var at = RelationAt(obligation); // As ownership reports a fit's record: at the value that supplies the longer end.
                // The record states the producer's kind: a declared clause, a well-formedness relation or a fit (IsFitObligation).
                var fact = holds ? new OriginRelationFact(at, shorter, this.SuppliedAtUse(shorter, at), longer, Equality: true, obligation.Type, Refuted: true, obligation.Clause, WellFormed: obligation.WellFormed)
                    : new(at, longer, this.SuppliedAtUse(longer, at), shorter, obligation.Equality, obligation.Type, Refuted: true, obligation.Clause, WellFormed: obligation.WellFormed);
                this.FailExplained(ref this.originRelations, at, BindingFailure.OriginRelation, fact);
            }

            return;
        }

        obligation = obligation with { Body = this.formingBody }; // The body Binding formed it in (DECISIONS R4-M101 E1).
        this.AddPendingRelation(obligation); // Judged when its Closure is classified, if it names a pending atom (R4-M126 §2.5).
        if (this.obligationSet.Add(obligation))
        {
            this.obligations.Add(obligation);
            this.originStateVersion++;
            this.RecordLocalRegionBound(obligation);
        }
    }
}
