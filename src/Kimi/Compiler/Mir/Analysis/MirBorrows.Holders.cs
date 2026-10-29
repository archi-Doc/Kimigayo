// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holders of a view (A3 3): the holder table, E in slot 0, the flow holders, whose states cross heads, then the chain-local ones,
// with each Loan's reservation holder and named holder mapped to slots. A local is a holder when its case Type may hold a Loan
// (MirBorrows.Reach's Holding). A holder a kept Drop past the destruction walk's bound drops has a drop slot right after its own, in its
// region, whose state is always empty: drop-liveness, the liveness that only such a Drop gives (MirBorrows.Liveness; DECISIONS R4-M153).
// Must not carry, end or check a Loan (MirBorrows.Solve, MirBorrows.Endings, MirBorrows.Checks).
internal sealed partial class MirBorrows
{
    private readonly List<(int Slot, int Local)> drops = []; // The view's drop slots, each with its holder's local.
    private int[] slots = []; // Each local's holder slot, -1 for none; E is slot 0.
    private int[] dropSlots = []; // By local: its drop slot, -1 for none; set where the view has drop slots.
    private bool[] reserves = []; // By slot: whether a two-phase Loan is issued into it, its reservation holder.
    private int holderCount; // The slots, E's included.
    private int flowCount; // The slots a head stores: E and the flow holders.

    // The holders of the view (A3 3): E in slot 0, then the flow holders, whose states cross heads (MirMoveData's flow roots and the roots a
    // direct Loan or address names, whose states the closure and the Loans' content read anywhere), then the chain-local ones, a holder's
    // drop slot right after it (DroppedDeep); then each Loan's reservation holder and each Loan's and address's named holder as slots, E for
    // a direct one on a static (storage the body does not own, SPEC 15.6; G136 (i)).
    private void Holders()
    {
        var locals = this.body.Locals;
        MirDataflow.Grow(ref this.slots, locals.Length);
        this.holderCount = 1;
        for (var i = 0; i < locals.Length; i++)
        {
            this.slots[i] = this.Holding(i) ? -2 : -1;
        }

        var deep = this.DroppedDeep();
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < locals.Length; i++)
            {
                if (this.slots[i] == -2 && (this.named[i] || moves.Find(new(i), out _) < moves.FlowCount) == (pass == 0))
                {
                    this.slots[i] = this.holderCount++;
                    if (deep && this.dropSlots[i] == -2)
                    {
                        this.dropSlots[i] = this.holderCount++;
                        this.drops.Add((this.dropSlots[i], i));
                    }
                }
            }

            this.flowCount = pass == 0 ? this.holderCount : this.flowCount;
        }

        MirDataflow.Grow(ref this.reserves, this.holderCount);
        Array.Clear(this.reserves, 0, this.holderCount);
        for (var l = 0; l < this.loanCount + this.addressCount; l++)
        {
            ref var loan = ref this.loans[l];
            loan.Holder = loan.Holder >= 0 ? this.slots[loan.Holder] : -1;
            if (loan.Named >= 0)
            {
                loan.Named = this.slots[loan.Named];
            }
            else if (loan.Direct)
            {
                loan.Named = Escaped; // A direct Loan on a static names E.
            }

            if (loan is { Mode: MirRefKind.TwoPhaseUniq, Holder: >= 0 })
            {
                this.reserves[loan.Holder] = true;
            }
        }

        this.Work += (2 * locals.Length) + this.body.Blocks.Length + this.loanCount + this.addressCount + this.holderCount;
    }

    // Whether a kept Drop past the destruction walk's bound (Dropping's Unclassified) drops a holder root, each such root marked -2 in
    // dropSlots for a drop slot; the drop slots' table is reset only for a view that has one (R4-M153).
    private bool DroppedDeep()
    {
        this.drops.Clear();
        var deep = false;
        for (var b = 0; b < this.body.Blocks.Length; b++)
        {
            if (this.body.Blocks[b].Terminator is { Kind: MirTerminatorKind.Drop, Place: { Root: >= 0 and var root } place } && this.slots[root] == -2 &&
                this.Dropping(b, place) == AccessKind.Unclassified)
            {
                if (!deep)
                {
                    MirDataflow.Grow(ref this.dropSlots, this.body.Locals.Length);
                    Array.Fill(this.dropSlots, -1, 0, this.body.Locals.Length);
                    deep = true;
                }

                this.dropSlots[root] = -2;
            }
        }

        return deep;
    }
}
