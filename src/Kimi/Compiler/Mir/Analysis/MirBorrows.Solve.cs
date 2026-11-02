// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's view loop (A3 5, Carrying, with E8): Run checks a body under the selected view: its Loans, addresses, its holders' slots (Holders), endings,
// touches and the copies' entries, then holder liveness backward and carrying forward over the dataflow's chains, each holder's set joined
// by node union, then the checks replay and R26-access's pass (MirBorrows.Defaults). A block's locations are carried in order, each planned
// on the state before it (MirBorrows.Flow), checked in the replay, then committed; at a destruction in place the Loans it ends are then
// removed from every holder (`step`: σ_out \ ended, so a call's transfer still reads its destroyed target), and before a Call that moves
// pending slots their inspection Loans (R-a, MirBorrows.Endings); in the replay, a copy's Entry(B) is gathered before its entry's plan.
internal sealed partial class MirBorrows
{
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
        (this.program, this.body, this.view, this.views, this.held) = (program, body, view, moves.ViewDependent ? 1UL << (int)view : all, false);
        this.sets.Clear();
        (this.loanBase, this.escapedSet) = (Markers + body.Locals.Length, this.sets.Add(0, Escaped));
        this.Loans();
        this.Addresses();
        if (!this.producing)
        {
            // No input of a Loan-stratum producer, no problem (A3 6.1, D5; E7's γ, R3, a slot write, PQ-10 and an own-step entry an access may
            // depart need no Loan, R4-M141).
            return;
        }

        this.Caused(); // Rule W's elements (MirBorrows.Causes), before any set holds a Loan.
        this.Holders();
        this.Escapes();
        this.Endings();
        this.Touches();
        this.Entries();
        var words = (this.holderCount + 63) >> 6;
        var state = flow.Prepare((this.flowCount + 63) >> 6, this.flowCount, Math.Max(words, this.holderCount), out var arena);
        if (state is null)
        {
            MirDataflow.StorageLimit(program.Problems, body, all, MirStorageTable.Loans, arena); // R4-M15a.
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

    // σ at the entry: a Loan-capable argument, and what storage the body does not own holds, refer to such storage (Φ_E; SPEC 15.6), so
    // every value read through E carries Φ_E (R4-M120, B1).
    private void Initial(ulong[] state)
    {
        state.AsSpan(0, this.holderCount).Clear();
        state[Escaped] = State(this.escapedSet, 0);
        for (var i = 1; i < this.body.Locals.Length && this.body.Locals[i].Kind == MirLocalKind.Arg; i++)
        {
            if (this.slots[i] >= 0)
            {
                state[this.slots[i]] = State(this.escapedSet, 0);
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
            if (checks && this.copying && this.entering[location])
            {
                this.Entering(state, location);
            }

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

    // Ended Loans removed from every holder (E8, R-a), with their indirect markers (rule W).
    private void End(ulong[] state, int ended)
    {
        if (ended == 0)
        {
            return;
        }

        ended = this.Indirect(ended);

        for (var s = 0; s < this.holderCount; s++)
        {
            state[s] = State(this.sets.Remove(Placed(state[s]), ended), this.sets.Remove(Shared(state[s]), ended));
        }

        this.Work += this.holderCount;
    }

    // The node-union join (R4-M34).
    private bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming)
    {
        var grew = false;
        for (var s = 0; s < target.Length; s++)
        {
            var union = State(this.sets.Union(Placed(target[s]), Placed(incoming[s])), this.sets.Union(Shared(target[s]), Shared(incoming[s])));
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
