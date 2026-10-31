// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holder state (A3 5; DECISIONS R4-M160, amending R4-M53's int state): a holder's slot packs two interned MirLoanSets sets, Placed in
// its low 32 bits, what stores into the holder's own Place put there (a whole replace, a direct part (P1), a Call's destination, the
// entry), and Shared in its high 32 bits, what stores through a reference gave it (P10's refs^k targets and every W target); σ(h) is
// Placed ∪ Shared. One store's value reaches every holder it writes as the same Shared id, so a walk reads each distinct Shared id once.
// Input: the slots MirBorrows.Solve carries and MirBorrows.Flow commits. Output: a state's parts and σ(h) as one set (Set), its elements
// in ascending order without forming it (ElementsOf), and the per-walk mark by which a W walk, a Chain closure or a part list reads each
// distinct Shared id once (Step, Steps).
// Invariants: every reader depends only on σ(h), never on which part holds an element; a gather iterates σ(h) ascending, as one set's
// elements; the transfers work per part (Commit, End, Join), so each part's operation is memoized by MirLoanSets; a Shared id read once
// by a walk is a no-op when read again there, because each reader's step is idempotent.
// Must not number elements or keep a set in another form (MirLoanSets), or branch a reader's facts on the part an element is in.
internal sealed partial class MirBorrows
{
    private int[] steppedBy = []; // By Shared set: the walk level that read it (Steps).
    private int stepping; // The current walk's base level, even; level + 1 reads a set's indirect form (Steps).

    // A state's parts and a state of parts.
    private static int Placed(ulong state) => (int)(uint)state;

    private static int Shared(ulong state) => (int)(state >> 32);

    private static ulong State(int placed, int shared) => (uint)placed | ((ulong)(uint)shared << 32);

    // σ(h) as one set (memoized by MirLoanSets; Placed itself where Shared is empty).
    private int Set(ulong state) => this.sets.Union(Placed(state), Shared(state));

    // The element count of σ(h)'s parts.
    private int Count(ulong state) => this.sets.Count(Placed(state)) + this.sets.Count(Shared(state));

    // σ(h)'s elements in ascending order, each once, without forming the union.
    private StateElements ElementsOf(ulong state) => new(this.sets.Elements(Placed(state)), this.sets.Elements(Shared(state)));

    // Starts a walk that reads each distinct Shared id once per level.
    private void Step() => this.stepping += 2;

    // Whether the walk reads a Shared id at a level (0: as it is, 1: its indirect form, which covers level 0) for the first time, marking it.
    private bool Steps(int shared, int level)
    {
        if (shared == 0)
        {
            return false;
        }

        MirDataflow.Grow(ref this.steppedBy, shared + 1);
        if (this.steppedBy[shared] >= this.stepping + level)
        {
            return false;
        }

        this.steppedBy[shared] = this.stepping + level;
        return true;
    }

    /// <summary>The ascending merge of a state's two parts, an element in both once.</summary>
    private ref struct StateElements(ReadOnlySpan<int> placed, ReadOnlySpan<int> shared)
    {
        private ReadOnlySpan<int> placed = placed;
        private ReadOnlySpan<int> shared = shared;

        public int Current { get; private set; }

        public readonly StateElements GetEnumerator() => this;

        public bool MoveNext()
        {
            if (this.shared.IsEmpty || (!this.placed.IsEmpty && this.placed[0] <= this.shared[0]))
            {
                if (this.placed.IsEmpty)
                {
                    return false;
                }

                this.Current = this.placed[0];
                this.placed = this.placed[1..];
                this.shared = !this.shared.IsEmpty && this.shared[0] == this.Current ? this.shared[1..] : this.shared;
                return true;
            }

            this.Current = this.shared[0];
            this.shared = this.shared[1..];
            return true;
        }
    }
}
