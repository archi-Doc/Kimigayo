// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Binding's obligations (DECISIONS BU-85). Responsibility: record each obligation a rule requires (AddObligation), one entry per
// distinct obligation, and judge at the Final pass the obligations still recorded. Inputs: the obligations the Type, Origin, length
// and call rules form while binding. Outputs: Binding.Obligations, read by the Origin judgment, and the failures published at the
// obligations' uses. Invariants: an obligation with an Unstated end is never recorded (a Refuted one fails at its use); a Definition
// obligation still recorded at Final fails its use; a length obligation is verified unless its use failed or no Function scope
// proves it. Prohibitions: no consumer decides an obligation's verdict from its own reading; it reads Binding's.
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

    // SPEC 8.1.1, 8.1.2, 23.1.2: the Final pass's verdict on the obligations still recorded; a Definition obligation's use fails with
    // its Language cause (FailTypeRole).
    private void JudgeObligations()
    {
        for (var i = 0; i < this.obligations.Count; i++)
        {
            if (this.obligations[i].Deadline == BindingDeadline.Definition)
            {
                this.FailTypeRole(this.obligations[i]);
            }
        }
    }

    private void AddObligation(BindingObligation obligation)
    {
        // Rulings S4, design-final §13 A2: a relation with an Unstated end forms no obligation; a Refuted one is recorded at its use,
        // its Unstated end first.
        if (obligation is { Longer: { } longer, Shorter: { } shorter } && UnstatedRelation(longer, shorter) is { } holds)
        {
            if (!holds || (obligation.Equality && UnstatedRelation(shorter, longer) == false))
            {
                var at = RelationAt(obligation); // As ownership reports a fit's record: at the value that supplies the longer end.
                var fact = holds ? new OriginRelationFact(at, shorter, this.SuppliedAtUse(shorter, at), longer, Equality: true, obligation.Type, Refuted: true)
                    : new(at, longer, this.SuppliedAtUse(longer, at), shorter, obligation.Equality, obligation.Type, Refuted: true);
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
