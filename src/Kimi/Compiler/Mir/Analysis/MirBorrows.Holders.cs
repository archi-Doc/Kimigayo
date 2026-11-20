// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holders of a view (A3 3): the holder table, E in slot 0, the flow holders, whose states cross heads, then the chain-local ones,
// with each Loan's reservation holder and named holder mapped to slots. A local is a holder when its case Type may hold a Loan, it is
// exposed (Holding), or a store of the body gives it a value read from a holder (Flowing), the least such set; a local that holds only
// calls' results and Case payloads of them (Returned) gives none from a Case payload that holds no Loan (MirBorrows.Values' Unheld)
// but what a call's write source stored in it, which its write slot, in the flow region, carries (WriteSlots).
// A holder a kept Drop past the destruction walk's bound drops has a drop slot right after its own, in its region, whose state is
// always empty: drop-liveness, the liveness that only such a Drop gives (MirBorrows.Liveness; DECISIONS R4-M153).
// Must not carry, end or check a Loan (MirBorrows.Solve, MirBorrows.Endings, MirBorrows.Checks).
internal sealed partial class MirBorrows
{
    private readonly List<(int Slot, int Local)> drops = []; // The view's drop slots, each with its holder's local.
    private readonly List<int> flowQueue = []; // Flowing's worklist: the holders whose edges are still to follow.
    private readonly List<int> pointed = []; // Written's worklist: the locals whose referring stores are still to follow.
    private readonly List<int> sourceSlots = []; // A call's source slots (SourceSlots).
    private int[] flowFirst = []; // By local: its first edge in flowEdges, -1 for none (Flowing).
    private int[] flowEdges = []; // Flowing's edges, two entries each: the local a store gives the value, and its source's next edge.
    private int[] mixFirst = []; // By local: its first edge in mixEdges, -1 for none (Mixing).
    private int[] mixEdges = []; // Mixing's edges, two entries each: the local a whole Move or Copy gives the value, and its source's next edge.
    private bool[] mixed = []; // By local: whether it may hold a value that is neither a call's result nor a Case payload of one (Mixing).
    private bool[] written = []; // By local: whether a call's write source may store in it (Mixing, Writes).
    private int[] writeSlots = []; // By local: its write slot, -1 for none; set where writeCount > 0.
    private int[] slotWrites = []; // By holder slot: its local's write slot, -1 for none; set where writeCount > 0.
    private int writeCount; // The view's write slots, -1 before the slots are numbered.
    private bool mixedKnown; // Whether mixed is the view's (Returned); Run resets it per view.
    private bool mixedWrites; // Whether Mixing met a call's write it counted before Flowing decided the holders (Carries).
    private bool flowed; // Whether Flowing has decided the view's holders (Carries).
    private int[] pointedBy = []; // By local: the Written walk that followed it.
    private int pointedWalks; // The Written walks.
    private int[] slots = []; // Each local's holder slot, -1 for none; E is slot 0.
    private int[] dropSlots = []; // By local: its drop slot, -1 for none; set where the view has drop slots.
    private bool[] reserves = []; // By slot: whether a two-phase Loan is issued into it, its reservation holder.
    private int holderCount; // The slots, E's included.
    private int flowCount; // The slots a head stores: E and the flow holders.

    // An edge of a linked adjacency (first by source local; two entries per edge: its target and the source's next edge); the edges after it.
    private static int Linked(ref int[] edges, int[] first, (int From, int To) edge, int count)
    {
        MirDataflow.Grow(ref edges, 2 * (count + 1));
        (edges[2 * count], edges[(2 * count) + 1]) = (edge.To, first[edge.From]);
        first[edge.From] = count;
        return count + 1;
    }

    // Whether an output's store may put the input at a source slot (SourceSlots) in a Loan-free Case payload, a write source: the
    // output's storage may hold it.
    private static bool Sourced(OutputRetention written, int slot) => written.Own.Holds(slot) || written.All.Holds(slot);

    // The holders of the view (A3 3): E in slot 0, then the flow holders, whose states cross heads (MirMoveData's flow roots and the roots a
    // direct Loan or address names, whose states the closure and the Loans' content read anywhere), then the chain-local ones, a holder's
    // drop slot right after it (DroppedDeep); then each Loan's reservation holder and each Loan's and address's named holder as slots, E for
    // a direct one on a static (storage the body does not own, SPEC 15.6; G136 (i)).
    private void Holders()
    {
        var locals = this.body.Locals;
        MirDataflow.Grow(ref this.slots, locals.Length);
        (this.holderCount, this.flowed, this.writeCount) = (1, false, -1);
        for (var i = 0; i < locals.Length; i++)
        {
            this.slots[i] = this.Holding(i) ? -2 : -1;
        }

        this.Unfollow();
        this.Flowing();
        this.flowed = true;
        if (this.mixedKnown ? this.mixedWrites : this.WriteSourced())
        {
            this.Mixing(); // With the holders (Carries), before the write slots are numbered: none runs after where a call writes.
        }

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

            this.writeCount = pass == 0 ? this.WriteSlots() : this.writeCount;
            this.flowCount = pass == 0 ? this.holderCount : this.flowCount;
        }

        if (this.writeCount > 0)
        {
            MirDataflow.Grow(ref this.slotWrites, this.holderCount);
            Array.Fill(this.slotWrites, -1, 0, this.holderCount);
            for (var i = 0; i < locals.Length; i++)
            {
                if (this.slots[i] >= 0)
                {
                    this.slotWrites[this.slots[i]] = this.writeSlots[i];
                }
            }
        }

        this.Unfollow(); // Sources' answers read the slots.
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
    // operands' (MirBorrows.Values' ValueOperands, val's one rule), or a returning Call's destination, whose operands are
    // the inputs its result record retains (Binding's retention, 15.6.4: decided per argument; MirBorrows.CallRows' Planned, a call
    // receiver input at the callee operand), one of the operands a Place rooted at a holder whose read carries what the holder carries
    // (none through a Case payload the Case bound empties or a reference the referent bound empties, MirBorrows.Values' Unheld). The least such set over Holding's: an edge per
    // such store and operand from the operand's root to the destination, then a worklist from the holders, each edge once, linear in the
    // body. A local no such store reaches carries nothing at every point, so it has no slot.
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
                    for (var o = rvalue.Start; o < rvalue.Start + this.ValueOperands(rvalue); o++)
                    {
                        edges = this.Flows(this.body.Operands[o], to, edges);
                    }
                }
            }

            if (data.Terminator is { Kind: MirTerminatorKind.Call, Target: >= 0, Place.Root: >= 0 and var into } call && this.slots[into] == -1 &&
                !this.Through(call.Place))
            {
                _ = this.Planned(call);
                for (var p = 0; p <= call.Count; p++)
                {
                    if (this.resultKinds[p] != RetentionKind.None)
                    {
                        edges = this.Flows(p < call.Count ? this.body.Operands[call.Start + p] : call.Operand, into, edges);
                    }
                }
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

    // Flowing's edge from an operand's root to the local a store gives its value (none from a constant, a static or a Place whose read
    // carries nothing, Unheld); the edges after it.
    private int Flows(in MirOperand operand, int to, int edges)
        => operand.Kind == MirOperandKind.Const || operand.Place.Root < 0 || this.Unheld(operand.Place)
            ? edges : Linked(ref this.flowEdges, this.flowFirst, (operand.Place.Root, to), edges);

    // Whether a local holds only calls' results and Case payloads of them (SPEC 15.6 Holders' Case-payload sentence; the owner's O-383
    // (b)): every value the body stores in it is one (Mixing, computed once per view at the first Place that asks, so a body without
    // such a Place pays nothing).
    private bool Returned(int local)
    {
        if (!this.mixedKnown)
        {
            this.Mixing();
        }

        return !this.mixed[local];
    }

    // The locals that may hold a value that is neither a call's result nor a Case payload of one nor a reference held as a part of one,
    // the least such set: an argument (the caller's value), a local an Assign stores into (through no borrow or raw Deref, Through) other
    // than whole by a Move or Copy of another local's whole value, of a Case payload of it, or of a reference it holds as a part (Whole),
    // a local a Call returns into a part of, a Call's whole destination whose result keeps an input's own value that may carry a Loan in
    // a Loan-free part (KeepsSourced), and a local such a Move or Copy gives the value of one of them: a call's result stays one while it
    // is Moved or Copied whole from local to local, a Case payload of it is bounded as it is (the owner's O-383 (b); a Field or element of
    // the result that is no reference stays the holder's part, O-364 (a)), and a reference Copied out of a Field, Tuple element or Case
    // payload of it is what the owner's O-437 (b) means by "a reference the call returns as a part of its result" (the referent bound,
    // MirBorrows.Values' Sources). A pointer the body formed on such a local does not make it one of them otherwise:
    // a raw write is SPEC 15.6 Addresses', and a store of the body through a reference to it is not read here (a known limit: the row
    // rvCaseRefWriteBad). A call's write may store a value a Loan-free Case payload may hold and that may carry a Loan in a local
    // (Writes): the local stays a call's result, written, as is a local such a Move or Copy gives its value, and its write slot carries
    // what the writes store (WriteSlots; a view with a write source runs Mixing before the slots are numbered, WriteSourced). An edge
    // per such Move or Copy, then a worklist from the others, each edge once, linear in the body.
    private void Mixing()
    {
        var locals = this.body.Locals;
        MirDataflow.Grow(ref this.mixed, locals.Length);
        MirDataflow.Grow(ref this.written, locals.Length);
        MirDataflow.Grow(ref this.mixFirst, locals.Length);
        Array.Fill(this.mixFirst, -1, 0, locals.Length);
        Array.Clear(this.written, 0, locals.Length);
        for (var i = 0; i < locals.Length; i++)
        {
            this.mixed[i] = locals[i].Kind == MirLocalKind.Arg;
        }

        var (edges, lost) = (0, false);
        this.mixedWrites = false;
        for (var b = 0; b < this.body.Blocks.Length; b++)
        {
            ref readonly var data = ref this.body.Blocks[b];
            for (var s = data.Start; s < data.Start + data.Count; s++)
            {
                ref readonly var statement = ref this.body.Statements[s];
                if (statement is { Kind: MirStatementKind.Assign, Place.Root: >= 0 and var to } && !this.mixed[to] && !this.Through(statement.Place))
                {
                    var from = statement.Place.Projection == 0 ? this.Whole(statement.Rvalue) : -1;
                    edges = from >= 0 ? Linked(ref this.mixEdges, this.mixFirst, (from, to), edges) : edges;
                    this.mixed[to] = from < 0;
                }
            }

            if (data.Terminator is { Kind: MirTerminatorKind.Call, Target: >= 0, Place: { Root: >= 0 and var into } place } call && !this.Through(place))
            {
                this.mixed[into] |= place.Projection != 0 || this.KeepsSourced(call);
            }

            lost |= data.Terminator.Kind == MirTerminatorKind.Call && this.Writes(data.Terminator);
        }

        var writes = false;
        for (var i = 0; i < locals.Length; i++)
        {
            this.written[i] |= lost && this.Exposed(i);
            writes |= this.written[i];
        }

        this.Spread(this.mixed);
        if (writes)
        {
            this.Spread(this.written);
        }

        this.mixedKnown = true;
        this.Work += locals.Length + this.body.Statements.Length + this.body.Blocks.Length + edges;
    }

    // Spreads a flag by local over Mixing's edges, each edge once (a written local's mixed targets are mixed already).
    private void Spread(bool[] flag)
    {
        this.flowQueue.Clear();
        for (var i = 0; i < this.body.Locals.Length; i++)
        {
            if (flag[i] && this.mixFirst[i] >= 0)
            {
                this.flowQueue.Add(i);
            }
        }

        while (this.flowQueue.Count > 0)
        {
            var from = this.flowQueue[^1];
            this.flowQueue.RemoveAt(this.flowQueue.Count - 1);
            for (var e = this.mixFirst[from]; e >= 0; e = this.mixEdges[(2 * e) + 1])
            {
                var to = this.mixEdges[2 * e];
                if (!flag[to])
                {
                    flag[to] = true;
                    this.flowQueue.Add(to);
                }
            }
        }
    }

    // The write slots (A3 3): a local a call's write source may store in (written) that holds only calls' results takes one in the flow
    // region, which the call's write source joins (MirBorrows.Calls' Keep) and a whole store replaces (Rewrite), so a
    // Case payload the Case bound empties, or a referent the referent bound empties, carries what the writes stored (MirBorrows.Values' Read); the count.
    private int WriteSlots()
    {
        var (locals, count) = (this.body.Locals.Length, 0);
        MirDataflow.Grow(ref this.writeSlots, locals);
        for (var i = 0; i < locals; i++)
        {
            this.writeSlots[i] = this.mixedKnown && this.written[i] && !this.mixed[i] ? this.holderCount++ : -1;
            count += this.writeSlots[i] >= 0 ? 1 : 0;
        }

        return count;
    }

    // A call's write sources into the locals its arguments refer to (SPEC 15.6.4 by its published retention, MirBorrows.CallRows'
    // Retaining; the Middle's decisions on the review's caseWriteR2Bad): through each output whose record holds a source slot (Sourced)
    // whose input may carry a Loan (Carries), the locals the output's argument may refer to are written (Written). Whether a written
    // argument was lost (Written).
    private bool Writes(in MirTerminator call)
    {
        if (this.Broken(call) || this.Retaining(call) is not { IsLimit: false } retention || retention.Writes.IsEmpty)
        {
            return false;
        }

        var (receiver, lost) = (this.Receiver(call), false);
        this.SourceSlots(call, retention.Result.Kinds.Length, receiver);
        foreach (var written in retention.Writes)
        {
            foreach (var slot in this.sourceSlots)
            {
                this.Work++;
                if (Sourced(written, slot) && this.Carries(this.Input(call, Position(call.Callee, receiver, slot)).Place.Root))
                {
                    lost |= this.Written(this.Input(call, Position(call.Callee, receiver, written.Output)));
                    break;
                }
            }
        }

        return lost;
    }

    // Whether a call's result keeps by Value a source slot's input (a Loan-free input, SourceSlots) that may carry a Loan (Carries): its
    // own value then rides in the result, in a part whose Type holds no Loan, with the Loans of its holder, which the Case bound cannot
    // tell from a payload that carries nothing (SPEC 15.6 Holders' Case-payload sentence, 15.6.4: an Origin-free input's own value at a
    // result position that can hold it; the owner's O-383 (b) read per result, not per Case: the per-Case record, D-733, is the exact form
    // that will replace it, and caseOwnElseOk its known limit). Mixing then leaves the call's whole destination unbounded.
    private bool KeepsSourced(in MirTerminator call)
    {
        if (this.Broken(call) || this.Retaining(call) is not { IsLimit: false } retention)
        {
            return false;
        }

        var receiver = this.Receiver(call);
        var kinds = retention.Result.Kinds;
        this.SourceSlots(call, kinds.Length, receiver);
        foreach (var slot in this.sourceSlots)
        {
            this.Work++;
            if ((kinds[slot] & RetentionKind.Value) != 0 && this.Carries(this.Input(call, Position(call.Callee, receiver, slot)).Place.Root))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a call of the view has a write source (an output whose record holds a source slot, Sourced), so Mixing decides the written
    // locals before the slots are numbered (Holders); a view without one keeps Mixing lazy. Each block, each call's retention lookup,
    // each input SourceSlots tests and each output's source slot count as Work.
    private bool WriteSourced()
    {
        this.Work += this.body.Blocks.Length;
        for (var b = 0; b < this.body.Blocks.Length; b++)
        {
            if (this.body.Blocks[b].Terminator is not { Kind: MirTerminatorKind.Call } call)
            {
                continue;
            }

            this.Work++;
            if (this.Broken(call) || this.Retaining(call) is not { IsLimit: false } retention || retention.Writes.IsEmpty)
            {
                continue;
            }

            this.SourceSlots(call, retention.Result.Kinds.Length, this.Receiver(call));
            foreach (var written in retention.Writes)
            {
                foreach (var slot in this.sourceSlots)
                {
                    this.Work++;
                    if (Sourced(written, slot))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // A call's source slots, found once per call (the landing hunk's Revision 2: one scan per output was N outputs x N inputs where
    // each output's record retains an input): the input slots whose input is no constant and whose Type holds no Loan, into
    // sourceSlots, the input at Position (a composed drop's receiver at receiver 0). Each output's record is then asked at these slots
    // alone (Sourced: Writes, WriteSourced, MirBorrows.Calls' Keep). Each input tested counts as Work. An input whose Type holds a Loan
    // is no write source: a Loan-free Case payload is bounded by its Case's Type (the owner's O-383 (b)): it carries no Loan whatever
    // reached it: a part an input holds by value, a value read through a borrowed input, or reference bits written through a raw
    // pointer (SPEC 15.6.4), as for a call's result (MirBorrows.Values' Sources; the rows rvResPartBad, rvwPartBad, xchgFieldD,
    // cpyIntoD). Written reaches the output argument's first referent only; a deeper target makes the program invalid by 15.6.4 already.
    private void SourceSlots(in MirTerminator call, int slots, int receiver)
    {
        this.sourceSlots.Clear();
        for (var slot = 0; slot < slots; slot++)
        {
            this.Work++;
            var input = this.Input(call, Position(call.Callee, receiver, slot));
            if (input.Kind != MirOperandKind.Const && !this.Holds(cases.TypeOf(input), out _))
            {
                this.sourceSlots.Add(slot);
            }
        }
    }

    // Whether a written input's root may carry a Loan: a static's {Φ_E}, a holder (Flowing's least set), and any local before Flowing
    // has decided the holders, which Holders then has Mixing count again (mixedWrites).
    private bool Carries(int root)
    {
        this.mixedWrites |= !this.flowed;
        return root < 0 || !this.flowed || this.slots[root] != -1;
    }

    // Writes the locals a written argument may refer to: following its local's referring stores (MirBorrows.Loans' Referring), a Ref's
    // Place's root where the Place passes no Deref, else the local whose Deref is its first step, and the local a Reborrow, Move or
    // Copy reads; each local once per walk. Whether the argument was lost: a local not Referring, a Place past a deeper Deref, or an
    // argument that is no whole local's value, so every exposed local is written instead (Mixing).
    private bool Written(in MirOperand argument)
    {
        if (argument is not { Kind: MirOperandKind.Copy or MirOperandKind.Move, Place: { Projection: 0 } whole })
        {
            return argument.Kind != MirOperandKind.Const;
        }

        MirDataflow.Grow(ref this.pointedBy, this.body.Locals.Length);
        this.pointedWalks++;
        this.pointed.Clear();
        this.pointed.Add(whole.Root);
        while (this.pointed.Count > 0)
        {
            var local = this.pointed[^1];
            this.pointed.RemoveAt(this.pointed.Count - 1);
            if (local < 0 || this.pointedBy[local] == this.pointedWalks)
            {
                continue;
            }

            this.pointedBy[local] = this.pointedWalks;
            if (!this.Referring(local, out var store))
            {
                return true;
            }

            for (; store >= 0; store = this.referStores[(2 * store) + 1])
            {
                var assigned = this.body.Statements[this.referStores[2 * store]].Rvalue;
                var rvalue = assigned.Kind == MirRvalueKind.Adapt ? cases.Adapt(assigned, out _) : assigned;
                var place = rvalue.Kind == MirRvalueKind.Ref ? rvalue.Place : new MirPlace(this.body.Operands[rvalue.Start].Place.Root, 0);
                var deref = this.FirstDeref(place);
                this.Work++;
                if (deref < 0)
                {
                    return true;
                }

                if (rvalue.Kind == MirRvalueKind.Ref && deref == 0 && place.Root >= 0)
                {
                    this.written[place.Root] = true;
                }
                else
                {
                    this.pointed.Add(place.Root);
                }
            }
        }

        return false;
    }

    // A whole store into a local with a write slot, the start or end of its storage included, replaces the slot (SPEC 15.6 Holders: a
    // holder completely replaced no longer carries the Loans of its old value; the review's B2): cleared, then joined with the write slot
    // of the local whose value, Case payload or reference part a Move or Copy stores (Whole), if any, so the value moved carries what the writes
    // stored in its source and nothing older; a call's result stored has none (MirBorrows.Flow's Assign and storage statements,
    // MirBorrows.Calls' Call). A slot grows only by the writes and Moves between two such stores.
    private void Rewrite(ulong[] state, int to, int from)
    {
        if (this.writeSlots[to] >= 0)
        {
            this.AddSlot(this.writeSlots[to], 0, 0, Cleared, issue: false);
            if (from >= 0 && this.writeSlots[from] >= 0)
            {
                this.AddSlot(this.writeSlots[to], this.Set(state[this.writeSlots[from]]), 0, Joined, issue: false);
            }
        }
    }

    // Of a Place's steps, whether a Deref is its first and none other: 1 for that, 0 for no Deref, -1 for a deeper one.
    private int FirstDeref(MirPlace place)
    {
        var first = 0;
        for (var at = place.Projection; at != 0; at = this.body.Projections[at].Prefix)
        {
            if (this.body.Projections[at].Kind == MirProjectionKind.Deref)
            {
                if (this.body.Projections[at].Prefix != 0)
                {
                    return -1;
                }

                first = 1;
            }
        }

        return first;
    }

    // The local whose whole value, a Case payload of whose value at any depth (through payload steps alone, MirBorrows.Values' Payload),
    // or a reference it holds as a part (through Field and payload steps alone: a Field, a Tuple element or a Case payload; no Deref or
    // Index) an rvalue is by a Move or Copy (an Adapt's case-selected one), or -1.
    private int Whole(in MirRvalue assigned)
    {
        var rvalue = assigned.Kind == MirRvalueKind.Adapt ? cases.Adapt(assigned, out _) : assigned;
        if (rvalue.Kind != MirRvalueKind.Use ||
            this.body.Operands[rvalue.Start] is not { Kind: MirOperandKind.Copy or MirOperandKind.Move, Place: { Root: >= 0 } from })
        {
            return -1;
        }

        var (at, reference) = (from.Projection, cases.TypeOf(from).SafeReferent is not null);
        while (at != 0 && (this.Payload(at) || (reference && this.body.Projections[at].Kind == MirProjectionKind.Field)))
        {
            at = this.body.Projections[at].Prefix;
        }

        return at == 0 ? from.Root : -1;
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
