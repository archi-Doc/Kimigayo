// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A5's values on a state (A3 5's val and issued, with E3; SPEC 15.6 Addresses, 5.2.2, 5.7): what an rvalue, an operand or a Place carries,
// a root's state, and a borrow's parents and Loan, over the one step walk of MirSteps' steps (Prefix), which a store's destination also
// reads (MirBorrows.Flow), and the value a Deref goes through (PointerOf); a raw Ref issues its address, a `during self` layer's read
// carries what `@raw` of its storage carries (Layered), each formed by the untracked rule (MirBorrows.Addresses). Must not decide which
// holders a set reaches or a call's store writes (MirBorrows.Reach).
internal sealed partial class MirBorrows
{
    // val(rv) (A3 5) and the Loans it issues: an operand's, a Ref's (a raw one's its parents and address, formed), the union of an aggregate's
    // operands, a value-keeping Cast's operand's (between raw pointer Types, 5.4; none to usize, 5.5), a displacement's pointer's (5.3), an
    // integer's pointer {Φ_E} (D8), an Opaque site's {Φ_E} (R4-M70: the site rests on a recorded error, so a problem only its roots' Loans
    // would give never arises); under the view an Adapt is the rvalue its case selects (none: its operand's value).
    private (int Value, int Own) Value(ulong[] state, in MirRvalue rvalue, int location)
    {
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Use:
            case MirRvalueKind.Adapt:
            case MirRvalueKind.Cast when (MirCastKind)rvalue.Op is MirCastKind.Erasure or MirCastKind.Upcast ||
                ((MirCastKind)rvalue.Op == MirCastKind.Pointer && rvalue.Type?.Semantics == SemanticsKind.Raw):
            case MirRvalueKind.BinaryOp when (MirBinaryOp)rvalue.Op is MirBinaryOp.Add or MirBinaryOp.Sub &&
                this.TypeOf(rvalue.Start)?.Semantics == SemanticsKind.Raw:
                return (this.Operand(state, rvalue.Start), 0);
            case MirRvalueKind.Cast when (MirCastKind)rvalue.Op == MirCastKind.IntToPointer:
                return (this.escapedSet, 0);
            case MirRvalueKind.Ref:
                var (parents, issued) = this.Issued(state, rvalue.Place, reborrow: false, this.loanAt[location]);
                return (MirRefKind)rvalue.Op == MirRefKind.Raw ? (this.Formed(this.sets.Union(parents, issued)), 0) : (parents, issued);
            case MirRvalueKind.Aggregate:
                var value = 0;
                for (var o = rvalue.Start; o < rvalue.Start + rvalue.Count; o++)
                {
                    value = this.sets.Union(value, this.Operand(state, o));
                }

                return (this.Indirect(value), 0); // Rule W: an aggregate holding a borrow is not the borrow.
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

        return this.slots[root] >= 0 ? this.Set(state[this.slots[root]]) : 0;
    }

    // val(X) = content^k(σ(root(X))) over its k Derefs: the step walk's layer over all its steps, read as Layered reads it.
    private int Read(ulong[] state, MirPlace place)
    {
        var (layer, parents) = this.Prefix(state, place.Root, this.placeSteps.Walk(this.body, place, reborrow: false, ref this.walk));
        return this.Layered(place.Root, place.Projection, layer, parents);
    }

    // A borrow's value (A3 5 issued, E3): its Loan with the parents of every borrow layer it follows, from the last shared layer on (the
    // step walk's parents).
    private (int Value, int Own) Issued(ulong[] state, MirPlace place, bool reborrow, int loan)
    {
        var parents = this.Prefix(state, place.Root, this.placeSteps.Walk(this.body, place, reborrow, ref this.walk)).Parents;
        return (parents, loan >= 0 ? this.sets.Add(0, this.loanBase + loan) : 0);
    }

    // The one step walk (A3 5, E3) over a Place's first end steps in the walk buffer: val of that prefix (its layer) and the parents a
    // borrow of it follows, from the last shared layer on the values its Derefs go through. Read, Issued and Store's prefix read it.
    private (int Layer, int Parents) Prefix(ulong[] state, int root, int end)
    {
        var (layer, parents) = (this.Root(state, root), 0);
        for (var i = 0; i < end; i++)
        {
            var pointer = this.PointerOf(root, layer, parents, i);
            (parents, layer) = this.walk[i].Class switch
            {
                MirStepClass.Shared => (pointer, this.Content(state, pointer)),
                MirStepClass.Raw => (pointer, this.Indirect(this.Content(state, pointer))), // A fresh anchor (5.2.2); no reference (rule W).
                MirStepClass.Uniq => (this.sets.Union(parents, pointer), this.Content(state, pointer)),
                _ => (parents, this.Indirect(layer)), // Rule W: a part of a holder is no Copy of a borrow it holds.
            };
        }

        return (layer, parents);
    }

    // The value a Deref step of the walk buffer goes through, on the layer and parents of the prefix it applies to: a borrow's, that layer; a
    // raw pointer's, the prefix's read (Layered): the addresses and Loans it carries.
    private int PointerOf(int root, int layer, int parents, int step)
        => this.walk[step].Class == MirStepClass.Raw ? this.Layered(root, this.walk[step].Prefix, layer, parents) : layer;

    // A Place's read on its layer and parents (SPEC 5.7 Reading a Field; B §5.4): a `during self` layer's adds what `@raw` of the layer's
    // storage carries, the parents and its own-step entry, formed (the untracked rule tests what it forms, as at a raw Ref); another
    // Place's is its layer.
    private int Layered(int root, int projection, int layer, int parents)
    {
        var entry = this.OwnStepAt(root, projection);
        return entry < 0 ? layer : this.sets.Union(layer, this.Indirect(this.Formed(this.sets.Add(parents, this.loanBase + entry))));
    }
}
