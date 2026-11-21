// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's passed fact (SPEC 15.6.4, af6410d81: a slot a call or direct static access reaches is proved initialized there exactly when every
// path to it passes a Borrow of that slot; 22.4.3, 22.4.4; DECISIONS Z7b): per block of the view, the statics a Borrow of the body passes on
// every path to the block's terminator.
// Input: the view's Loans (MirBorrows.Loans' Issue: a Direct Loan whose Place is a static's, its storage, a part of it or the payload it
// owns, by block, on a static with one slot that A4 numbers an initialization for, MirEffects.Initialization) and MirDataflow's edges and
// phases (the selected view's CaseSwitch side, SPEC 14.10.3's P1 and P2).
// Output: Passed(block), the static bits clear on every path, valid for the Run, where the effect rule can run (an effect root or a Drop of
// a mutable static); over the analyses' byte limit (R4-M15a) one OwnershipStorageLimit_Kd at the body, table Loans (the stratum whose
// arena it is), and the body is unverified.
// Invariants: gen-only over a union of "not passed" (entry all set, a block's Borrows clear their statics' bits), so the least fixed point
// is the meet over paths; one bit per static with one slot, whose Field is its slot.
// Must not read liveness, a callee's or a Closure's Borrows, an address, an ensure or a read as a proof, a static with several slots
// (P42's per-access key, R4-M103), or syntax.
internal sealed partial class MirBorrows
{
    private int[] passBit = []; // By static: its bit, -1 for none.
    private int[] passStatics = []; // By bit: its static.
    private int[] passStarts = []; // By block: its first entry in passBits, and the end after the last block's.
    private int[] passBits = []; // The bits each block's Borrows clear, block by block.
    private ulong[] passed = []; // By block: the state before its terminator, passWords words.
    private int passCount; // The statics with a bit.
    private int passBitCount;
    private int passWords;

    // The state before a block's terminator: a static's bit is clear where every path of the view to it passes a Borrow of the static.
    private ReadOnlySpan<ulong> Passed(int block) => this.passed.AsSpan(block * this.passWords, this.passWords);

    // A Direct Loan of a static (Issue): its block's entry where the static has one slot that A4 ensures, numbered at its first.
    private void PassBit(int d)
    {
        if (this.passBit[d] < 0 && effects.Initialization(this.body.Statics[d].Symbol) >= 0)
        {
            MirDataflow.Grow(ref this.passStatics, this.passCount + 1);
            this.passStatics[this.passCount] = d;
            this.passBit[d] = this.passCount++;
        }

        if (this.passBit[d] >= 0)
        {
            MirDataflow.Grow(ref this.passBits, this.passBitCount + 1);
            this.passBits[this.passBitCount++] = this.passBit[d];
        }
    }

    // Solves the passed fact where a static has a bit and the effect rule can run; false where its arena and store are over the byte limit,
    // after recording it in the views the Run covers.
    private bool Passes(ulong views)
    {
        this.passWords = (this.passCount + 63) >> 6;
        if (this.passCount == 0 || (this.effectRoots.Count == 0 && !this.dropsStatic))
        {
            return true;
        }

        var blocks = this.body.Blocks.Length;
        var state = flow.Prepare(0, this.passWords, this.passWords, out var words);
        words += (long)blocks * this.passWords;
        if (state is null || !MirDataflow.Within(words))
        {
            MirDataflow.StorageLimit(this.program.Problems, this.body, views, MirStorageTable.Loans, words); // R4-M15a.
            return false;
        }

        MirDataflow.Grow(ref this.passed, blocks * this.passWords);
        var passing = new Passing(this, state);
        flow.Forward(ref passing);
        flow.Replay(ref passing);
        return true;
    }

    // The passed fact's transfer: a block clears the bits of its Borrows and keeps the state before its terminator (the replay's last).
    private readonly struct Passing(MirBorrows owner, ulong[] state) : IMirFlow
    {
        public void Entry() => state.AsSpan(0, owner.passWords).Fill(ulong.MaxValue);

        public void Block(int block)
        {
            var (from, to) = (owner.passStarts[block], owner.passStarts[block + 1]);
            foreach (var bit in owner.passBits.AsSpan(from, to - from))
            {
                state[bit >> 6] &= ~(1UL << bit);
            }

            state.AsSpan(0, owner.passWords).CopyTo(owner.passed.AsSpan(block * owner.passWords));
            owner.Work += to - from + owner.passWords;
        }

        public bool Join(Span<ulong> target, ReadOnlySpan<ulong> incoming) => MirDataflow.Union(target, incoming);
    }
}
