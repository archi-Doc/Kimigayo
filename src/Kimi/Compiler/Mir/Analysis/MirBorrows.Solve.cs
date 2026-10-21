// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's view loop (A3 5, Carrying, with E8): Run checks a body under the selected view: its Loans, its holders' slots (Holders), endings
// and touches, then holder liveness backward and carrying forward over the dataflow's chains, each holder's set joined by node union, then
// the checks replay and R26-access's pass (MirBorrows.Defaults). A block's locations are carried in order, each planned on the state before
// it (MirBorrows.Flow), checked in the replay, then committed; at a destruction in place the Loans it ends are then removed from every
// holder (`step`: σ_out \ ended, so a call's transfer still reads its destroyed target), and before a Call that moves pending slots their
// inspection Loans (R-a, MirBorrows.Endings).
internal sealed partial class MirBorrows
{
    private int[] slots = []; // Each local's holder slot, -1 for none; E is slot 0.
    private bool[] reserves = []; // By slot: whether a two-phase Loan is issued into it, its reservation holder.
    private int holderCount; // The slots, E's included.
    private int flowCount; // The slots a head stores: E and the flow holders.
    private int chainHead = -1; // The checks replay's chain: its head and first block.
    private int chainFirst;

    /// <summary>Checks a body under the selected view; view 0 of a body no view decides checks it for every view.</summary>
    /// <param name="program">The program, whose drop table it reads and whose problems it adds to.</param>
    /// <param name="body">The body.</param>
    /// <param name="view">The selected view.</param>
    internal void Run(MirProgram program, MirBody body, long view)
    {
        if (view > 0 && !moves.ViewDependent)
        {
            return;
        }

        var all = ulong.MaxValue >> (64 - (int)MirCases.Count(body));
        (this.program, this.body, this.view, this.views) = (program, body, view, moves.ViewDependent ? 1UL << (int)view : all);
        this.sets.Clear();
        (this.loanBase, this.escapedSet) = (Markers + body.Locals.Length, this.sets.Add(0, Escaped));
        this.Loans();
        if (!this.producing)
        {
            return; // No input of a Loan-stratum producer, no problem (A3 6.1, D5; E7's γ, R3 and a slot write need no Loan).
        }

        this.Holders();
        this.Endings();
        this.Touches();
        var words = (this.holderCount + 63) >> 6;
        var state = flow.Prepare((this.flowCount + 63) >> 6, this.flowCount, Math.Max(words, this.holderCount));
        if (state is null)
        {
            var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.StorageLimit);
            program.Problems.Add(new(DiagnosticCode.OwnershipStorageLimit_Kd, requirement, body.Source, body.Ordinal, all)); // R4-M15a.
            return;
        }

        var liveness = new Liveness(this, state);
        flow.Backward(ref liveness);
        liveness.Recording = true;
        flow.ReplayBackward(ref liveness);
        var carrying = new Carrying(this, state);
        flow.Forward(ref carrying);
        (carrying.Checks, this.chainHead) = (true, -1);
        this.Restamp();
        flow.Replay(ref carrying);
        this.DefaultAccesses();
        this.Bytes += this.sets.Bytes;
        this.Work += this.sets.Bytes + this.sets.Scanned;
    }

    // The holders of the view (A3 3): E in slot 0, then the flow holders, whose states cross heads (MirMoveData's flow roots and the roots a
    // direct Loan names, whose states the closure and the Loans' content read anywhere), then the chain-local ones; then each Loan's
    // reservation holder and named holder as slots, E for a direct Loan on a static (storage the body does not own, SPEC 15.6; G136 (i)).
    private void Holders()
    {
        var locals = this.body.Locals;
        MirDataflow.Grow(ref this.slots, locals.Length);
        this.holderCount = 1;
        for (var i = 0; i < locals.Length; i++)
        {
            this.slots[i] = this.Holding(i) ? -2 : -1;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < locals.Length; i++)
            {
                if (this.slots[i] == -2 && (this.named[i] || moves.Find(new(i), out _) < moves.FlowCount) == (pass == 0))
                {
                    this.slots[i] = this.holderCount++;
                }
            }

            this.flowCount = pass == 0 ? this.holderCount : this.flowCount;
        }

        MirDataflow.Grow(ref this.reserves, this.holderCount);
        Array.Clear(this.reserves, 0, this.holderCount);
        for (var l = 0; l < this.loanCount; l++)
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

        this.Work += (2 * locals.Length) + this.loanCount + this.holderCount;
    }

    // σ at the entry: a Loan-capable argument refers to storage the body does not own (Φ_E); the forward solve starts with the view not
    // held (R4-M73).
    private void Initial(ulong[] state)
    {
        state.AsSpan(0, this.holderCount).Clear();
        this.held = false;
        for (var i = 1; i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            if (this.slots[i] >= 0)
            {
                state[this.slots[i]] = (ulong)this.escapedSet;
            }
        }
    }

    // A block's locations: in the checks replay, the live holders after each, its plan on the state before it and its checks first; the
    // terminator's inspection Loans end before its plan (R-a).
    private void Carry(ulong[] state, int block, bool checks)
    {
        if (checks && flow.Head(block) != this.chainHead)
        {
            (this.chainHead, this.chainFirst) = (flow.Head(block), block);
            this.Enter(this.chainHead);
        }

        var count = this.body.Blocks[block].Count;
        for (var i = 0; i <= count; i++)
        {
            var location = this.Location(block, i);
            this.Work++;
            if (i == count)
            {
                this.End(state, this.inspected[block]);
            }

            this.Plan(state, block, i, checks);
            if (checks)
            {
                this.Advance(location);
                this.Check(state, block, i, location);
            }

            this.Commit(state);
            this.End(state, this.ended[location]);
        }
    }

    // Ended Loans removed from every holder (E8, R-a).
    private void End(ulong[] state, int ended)
    {
        if (ended == 0)
        {
            return;
        }

        for (var s = 0; s < this.holderCount; s++)
        {
            state[s] = (ulong)this.sets.Remove((int)state[s], ended);
        }

        this.Work += this.holderCount;
    }

    // The node-union join (R4-M34).
    private bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming)
    {
        var grew = false;
        for (var s = 0; s < target.Length; s++)
        {
            var union = (ulong)this.sets.Union((int)target[s], (int)incoming[s]);
            grew |= union != target[s];
            target[s] = union;
        }

        this.Work += target.Length;
        return grew;
    }

    // The forward problem over holder slots: E and the flow holders stored at heads, each slot a set.
    private struct Carrying(MirBorrows owner, ulong[] state) : IMirFlow
    {
        internal bool Checks { get; set; }

        public readonly void Entry() => owner.Initial(state);

        public readonly void Block(int block) => owner.Carry(state, block, this.Checks);

        public readonly bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming) => owner.Join(target, incoming);
    }
}
