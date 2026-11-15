// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holders of a view (A3 3): the holder table, E in slot 0, the flow holders, whose states cross heads, then the chain-local ones,
// with each Loan's reservation holder and named holder mapped to slots. A local is a holder when its case Type may hold a Loan, it is
// exposed (Holding), or a store of the body gives it a value read from a holder (Flowing), the least such set. A holder a kept Drop past
// the destruction walk's bound drops has a drop slot right after its own, in its region, whose state is always empty: drop-liveness, the
// liveness that only such a Drop gives (MirBorrows.Liveness; DECISIONS R4-M153).
// Must not carry, end or check a Loan (MirBorrows.Solve, MirBorrows.Endings, MirBorrows.Checks).
internal sealed partial class MirBorrows
{
    private readonly List<(int Slot, int Local)> drops = []; // The view's drop slots, each with its holder's local.
    private readonly List<int> flowQueue = []; // Flowing's worklist: the holders whose edges are still to follow.
    private int[] flowFirst = []; // By local: its first edge in flowEdges, -1 for none (Flowing).
    private int[] flowEdges = []; // Flowing's edges, two entries each: the local a store gives the value, and its source's next edge.
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

        this.Flowing();
        var deep = this.DroppedDeep();
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < locals.Length; i++)
            {
                if (this.slots[i] == -2 && ((this.named[i] & NamedHolder) != 0 || moves.Find(new(i), out _) < moves.FlowCount) == (pass == 0))
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

    // Whether a local is a holder (SPEC 15.6 Holders: a holder carries the Loans of the values stored in every part of it): it is exposed
    // (MirBorrows.Loans' Exposed: a pointer the body formed may write its bytes, so what a write through it stores there, a punned one over
    // its Scalars included, SPEC 5.2, is carried by it, SPEC 15.6 Addresses, 5.2.1 Retained dependencies, 15.6.4), or its case Type may
    // hold a Loan (Holds): Flowing's seed.
    private bool Holding(int local) => this.Exposed(local) || this.Holds(cases.Type(this.body.Locals[local].Type), out _);

    // The holders closed over the body's value flow (SPEC 15.6 Holders: "A value read, Copied or Moved from a part carries every Loan the
    // holder carries"; 15.6 Loans: every aggregate, Closure or storage that holds one; a read through a reference carries the Loans of what
    // it reaches; O-230 Q1 (a), O-359 (a), O-364 (a): a Scalar part's Copy too): a local is one, whatever its Type, when a store of the body
    // gives it a value read from a holder: an Assign into its own Place (through no borrow or raw Deref, Through) whose rvalue's value is its
    // operands' (Value: Keeps, or an aggregate, a Closure's environment included), or a returning Call's destination (MirBorrows.Calls'
    // inputs, the callee operand of a Value callee included), one of the operands a Place rooted at a holder. The least such set over
    // Holding's: an edge per such store and operand from the operand's root to the destination, then a worklist from the holders, each edge
    // once, linear in the body. A local no such store reaches carries nothing at every point, so it has no slot.
    private void Flowing()
    {
        var locals = this.body.Locals.Length;
        MirDataflow.Grow(ref this.flowFirst, locals);
        Array.Fill(this.flowFirst, -1, 0, locals);
        var edges = 0;
        for (var b = 0; b < this.body.Blocks.Length; b++)
        {
            ref readonly var data = ref this.body.Blocks[b];
            for (var s = data.Start; s < data.Start + data.Count; s++)
            {
                ref readonly var statement = ref this.body.Statements[s];
                if (statement is { Kind: MirStatementKind.Assign, Place.Root: >= 0 and var to } && this.slots[to] == -1 && !this.Through(statement.Place))
                {
                    var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                    var end = rvalue.Start + (this.Keeps(rvalue) ? 1 : rvalue.Kind == MirRvalueKind.Aggregate ? rvalue.Count : 0);
                    for (var o = rvalue.Start; o < end; o++)
                    {
                        edges = this.Flows(this.body.Operands[o], to, edges);
                    }
                }
            }

            if (data.Terminator is { Kind: MirTerminatorKind.Call, Target: >= 0, Place.Root: >= 0 and var into } call && this.slots[into] == -1 &&
                !this.Through(call.Place))
            {
                for (var o = call.Start; o < call.Start + call.Count; o++)
                {
                    edges = this.Flows(this.body.Operands[o], into, edges);
                }

                edges = call.Callee.Kind == MirCalleeKind.Value ? this.Flows(call.Operand, into, edges) : edges;
            }
        }

        this.flowQueue.Clear();
        for (var i = 0; i < locals; i++)
        {
            if (this.slots[i] == -2 && this.flowFirst[i] >= 0)
            {
                this.flowQueue.Add(i);
            }
        }

        while (this.flowQueue.Count > 0)
        {
            var from = this.flowQueue[^1];
            this.flowQueue.RemoveAt(this.flowQueue.Count - 1);
            for (var e = this.flowFirst[from]; e >= 0; e = this.flowEdges[(2 * e) + 1])
            {
                var to = this.flowEdges[2 * e];
                if (this.slots[to] == -1)
                {
                    this.slots[to] = -2;
                    this.flowQueue.Add(to);
                }
            }
        }

        this.Work += this.body.Statements.Length + this.body.Blocks.Length + edges;
    }

    // Flowing's edge from an operand's root to the local a store gives its value (none from a constant or a static); the edges after it.
    private int Flows(in MirOperand operand, int to, int edges)
    {
        if (operand.Kind == MirOperandKind.Const || operand.Place.Root < 0)
        {
            return edges;
        }

        MirDataflow.Grow(ref this.flowEdges, 2 * (edges + 1));
        (this.flowEdges[2 * edges], this.flowEdges[(2 * edges) + 1]) = (to, this.flowFirst[operand.Place.Root]);
        this.flowFirst[operand.Place.Root] = edges;
        return edges + 1;
    }

    // A holder slot's local, or a drop slot's holder's (the drop list keeps it).
    private int HolderLocal(int slot)
    {
        foreach (var (drop, local) in this.drops)
        {
            if (drop == slot)
            {
                return local;
            }
        }

        return Array.IndexOf(this.slots, slot, 0, this.body.Locals.Length);
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
