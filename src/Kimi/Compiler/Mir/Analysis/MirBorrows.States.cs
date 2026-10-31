// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holder state (A3 5; DECISIONS R4-M160, amending R4-M53's int state): a holder's slot packs two interned MirLoanSets sets, Placed in
// its low 32 bits, what stores into the holder's own Place put there (a whole replace, a direct part (P1), a Call's destination, the
// entry), and Shared in its high 32 bits, what stores through a reference gave it (P10's refs^k targets and every W target); σ(h) is
// Placed ∪ Shared. One store's value reaches every holder it writes as the same Shared id, so a walk reads each distinct Shared id once.
// Input: the slots MirBorrows.Solve carries and MirBorrows.Flow commits. Output: a state's parts and σ(h) as one set (Set), its elements
// in ascending order without forming it (ElementsOf), the per-walk mark by which a W walk, a Chain closure or a part list reads each
// distinct Shared id once (Step, Steps), and the part of a holder's state a gather pass reads (Scanned).
// Invariants: every reader depends only on σ(h), never on which part holds an element; a gather iterates σ(h) ascending, as one set's
// elements; the transfers work per part (Commit, End, Join), so each part's operation is memoized by MirLoanSets; a Shared id read once
// by a walk is a no-op when read again there, because each reader's step is idempotent.
// Must not number elements or keep a set in another form (MirLoanSets), or branch a reader's facts on the part an element is in.
internal sealed partial class MirBorrows
{
    private long[] steppedBy = []; // By Shared set: the walk level that read it (Steps).
    private long stepping; // The walk's base level, even (level + 1: a set's indirect form; Steps); never reset, so a long (an int wraps at 2^30 walks).
    private (int Instance, int Via, int Count)[] scannedBy = []; // By Shared set: the pass instance that read it, its first via, the vias.
    private int instance; // The gather's pass instances (Pass).

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

    // The part of a holder's state a gather pass instance reads under it (via): σ(h), but Placed alone once two distinct vias that are
    // not special read its Shared set in the instance. A special via, which always reads it, is a reservation holder (a two-phase Loan's
    // mode depends on it) or, where rule W runs, an escape holder (Kept depends on it). Exact: within an instance the pass, its operand and
    // the drop tier's holder are fixed, so a candidate's flags, values and roots are; the first via (Tags.Via, a marker's, an own-step
    // entry's) is already tagged; an activation's two holders (ActVia, ActOther) are set once two distinct vias tagged its Loan; Kept,
    // a Loan's mode and the holders a Loan or address names (Reach) are the same under every non-special via.
    private ulong Scanned(ulong held, int via)
    {
        var shared = Shared(held);
        if (shared == 0 || this.reserves[via] || (this.wCount > 0 && this.escape[via]))
        {
            return held;
        }

        MirDataflow.Grow(ref this.scannedBy, shared + 1);
        ref var record = ref this.scannedBy[shared];
        if (record.Instance != this.instance)
        {
            record = (this.instance, via, 1);
            return held;
        }

        if (record.Count < 2)
        {
            record.Count += record.Via != via ? 1 : 0;
            return held;
        }

        return State(Placed(held), 0);
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
