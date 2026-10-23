// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's values on a state (A3 5's val and issued, with E3): what an rvalue, an operand or a Place carries, a root's state, and a borrow's
// parents and Loan, over the one step walk (MirSteps). Must not decide which holders a set reaches or a call's store writes
// (MirBorrows.Reach).
internal sealed partial class MirBorrows
{
    // val(rv) (A3 5) and the Loans it issues: an operand's, a Ref's, the union of an aggregate's or a value-keeping Cast's operands, an Opaque
    // site's {Φ_E} (R4-M70: the site rests on a recorded error, so a problem only its roots' Loans would give never arises); under the view
    // an Adapt is the rvalue its case selects (none: its operand's value).
    private (int Value, int Own) Value(ulong[] state, in MirRvalue rvalue, int location)
    {
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Use:
            case MirRvalueKind.Adapt:
            case MirRvalueKind.Cast when (MirCastKind)rvalue.Op is MirCastKind.Erasure or MirCastKind.Upcast or MirCastKind.Pointer:
                return (this.Operand(state, rvalue.Start), 0);
            case MirRvalueKind.Ref when (MirRefKind)rvalue.Op != MirRefKind.Raw:
                return this.Issued(state, rvalue.Place, reborrow: false, this.loanAt[location]);
            case MirRvalueKind.Aggregate:
                var value = 0;
                for (var o = rvalue.Start; o < rvalue.Start + rvalue.Count; o++)
                {
                    value = this.sets.Union(value, this.Operand(state, o));
                }

                return (value, 0);
            case MirRvalueKind.Opaque:
                return (this.escapedSet, 0);
            default:
                return (0, 0);
        }
    }

    // An operand's value: a constant's none, a Place's read (a PairAcquire's that Copies; one that Reborrows is its Use's issue, Plan).
    private int Operand(ulong[] state, int index)
        => this.body.Operands[index] is { Kind: not MirOperandKind.Const } operand ? this.Read(state, operand.Place) : 0;

    // A root's state: a holder's, a static's {Φ_E}, none for another local.
    private int Root(ulong[] state, int root)
    {
        if (root < 0)
        {
            return this.escapedSet;
        }

        return this.slots[root] >= 0 ? (int)state[this.slots[root]] : 0;
    }

    // val(X) = content^k(σ(root(X))) over its k borrow Derefs; a raw pointer's referent is {Φ_E}.
    private int Read(ulong[] state, MirPlace place)
    {
        var value = this.Root(state, place.Root);
        var count = this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            value = this.walk[i].Class switch
            {
                MirStepClass.Shared or MirStepClass.Uniq => this.Content(state, value),
                MirStepClass.Raw => this.escapedSet,
                _ => value,
            };
        }

        return value;
    }

    // A borrow's value (A3 5 issued, E3): its Loan with the parents of every borrow layer it follows, from the last shared layer on.
    private (int Value, int Own) Issued(ulong[] state, MirPlace place, bool reborrow, int loan)
    {
        var (layer, parents) = (this.Root(state, place.Root), 0);
        var count = this.placeSteps.Walk(this.body, place, reborrow, ref this.walk);
        for (var i = 0; i < count; i++)
        {
            switch (this.walk[i].Class)
            {
                case MirStepClass.Shared:
                    (parents, layer) = (layer, this.Content(state, layer));
                    break;
                case MirStepClass.Uniq:
                    (parents, layer) = (this.sets.Union(parents, layer), this.Content(state, layer));
                    break;
                case MirStepClass.Raw:
                    (parents, layer) = (this.sets.Union(parents, this.escapedSet), this.escapedSet);
                    break;
            }
        }

        return (parents, loan >= 0 ? this.sets.Add(0, this.loanBase + loan) : 0);
    }
}
