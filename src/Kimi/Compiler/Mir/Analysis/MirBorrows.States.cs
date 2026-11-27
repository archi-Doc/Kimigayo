// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's holder state (A3 5; DECISIONS R4-M160, amending R4-M53's int state): a holder's slot packs two interned MirLoanSets sets, Placed in
// its low 32 bits, what stores into the holder's own Place put there (a whole replace, a direct part (P1), a Call's destination, the
// entry), and Shared in its high 32 bits, what stores through a reference gave it (P10's refs^k targets and every W target); σ(h) is
// Placed ∪ Shared. One store's value reaches every holder it writes as the same Shared id, so a walk reads each distinct Shared id once.
// Input: the slots MirBorrows.Solve carries and MirBorrows.Flow commits. Output: a state's parts and σ(h) as one set (Set), its elements
// in ascending order without forming it, each at MirLoanSets' joined tier (ElementsOf), whether it holds an element by its parts without
// forming it (Holds: Chain and W read σ(h)'s Φ_M so), and the part of a holder's state a gather pass reads at a read's tier (Scanned).
// Two marks by set id, each read and set by Steps at a level: the reads' (steppedBy, by Step's level), by which a Chain closure or a part
// list reads each distinct Shared id once; and W's own (walkedBy, by walk), by which a W walk reads each distinct part, Placed or Shared,
// once, so a read inside a W walk (MirBorrows.Reach's WriteTargets) moves no mark of the walk's.
// Invariants: every reader depends only on σ(h), never on which part holds an element; a gather iterates σ(h) ascending, as one set's
// elements; the transfers work per part (Commit, End, Join), so each part's operation is memoized by MirLoanSets; a part's id read once
// by a walk is a no-op when read again there, because each reader's step is idempotent.
// Must not number elements or keep a set in another form (MirLoanSets), or branch a reader's facts on the part an element is in.
internal sealed partial class MirBorrows
{
    private (long Decided, long Rested)[] steppedBy = []; // By set: the reads' forms that read it (Steps; Content, Chain).
    private long stepping; // The reads' base level, even (level + 1: a set's indirect form; Steps); never reset, so a long (an int wraps at 2^30 walks).
    private (long Decided, long Rested)[] walkedBy = []; // By set: the W walk forms that read it (Steps; MirBorrows.Reach's Read):
    // 2 · walk, plus 1 at Through, at the reached holder's tier (ViaDecided).
    private (int Instance, int Via, int Count)[] scannedBy = []; // By Shared set and read tier (Scanned): its pass instance, first via, vias.
    private int instance; // The gather's pass instances (Pass).

    // A state's parts and a state of parts.
    private static int Placed(ulong state) => (int)(uint)state;

    private static int Shared(ulong state) => (int)(state >> 32);

    private static ulong State(int placed, int shared) => (uint)placed | ((ulong)(uint)shared << 32);

    // Whether a reader reads a part's id at a form of its mark for the first time, marking it: a level, the reads' steppedBy at Step's
    // level plus 0 (as it is) or 1 (its indirect form, which covers 0), W's walkedBy at 2 · walk plus 0 (Named) or 1 (Through, which
    // covers Named), at a tier. Each mark has two fields (DECISIONS R4-M2xx), and a form is the mark it sets: (level, level) decided,
    // (0, level) rested (Form); a form is covered where the mark is at or above it in both fields, so a decided level covers a request at
    // or below it at either tier and a rested one only a rested request: a part takes at most 4 successful Steps per base (two forms ×
    // two tiers).
    private static bool Steps(ref (long Decided, long Rested)[] marks, int part, (long Decided, long Rested) form)
    {
        if (part == 0)
        {
            return false;
        }

        MirDataflow.Grow(ref marks, part + 1);
        ref var mark = ref marks[part];
        if (mark.Decided >= form.Decided && mark.Rested >= form.Rested)
        {
            return false;
        }

        mark = (Math.Max(mark.Decided, form.Decided), Math.Max(mark.Rested, form.Rested));
        return true;
    }

    // A mark's form at a level at the tier the part read was reached at (Steps): decided, which covers either tier, or rested; the tier
    // is the datum each caller holds, the reaching entry's (TieredElement.Rested) or W's clear ViaDecided bit (MirBorrows.Reach's Read).
    private static (long Decided, long Rested) Form(long level, bool rested) => (rested ? 0 : level, level);

    // σ(h) as one set (memoized by MirLoanSets; Placed itself where Shared is empty).
    private int Set(ulong state) => this.sets.Union(Placed(state), Shared(state));

    // The element count of σ(h)'s parts.
    private int Count(ulong state) => this.sets.Count(Placed(state)) + this.sets.Count(Shared(state));

    // σ(h)'s elements in ascending order, each once, without forming the union.
    private StateElements ElementsOf(ulong state) => new(this.sets.Elements(Placed(state)), this.sets.Elements(Shared(state)));

    // Whether σ(h) holds an element, at either tier, answered by its parts without forming the union.
    private bool Holds(ulong state, int element) => this.sets.Contains(Placed(state), element) || this.sets.Contains(Shared(state), element);

    // Starts a read that reads each distinct part's id once per level (steppedBy).
    private void Step() => this.stepping += 2;

    // The part of a holder's state a gather pass instance reads under it (via) at its read key's tier `at` (MirBorrows.CheckSets' TierOf):
    // σ(h), but Placed alone once two distinct vias that are not special read its Shared set in the instance at that tier. A special via,
    // which always reads it, is a reservation holder (a two-phase Loan's mode depends on it) or, where rule W runs, an escape holder (Kept
    // depends on it). Exact: within an instance and a tier the pass, its operand, the drop tier's holder and the tier a candidate is
    // tagged at are fixed, so a candidate's flags, values and roots are; the first via (Tags.Via, a marker's, an own-step entry's) is
    // already tagged; an activation's two holders (ActVia, ActOther) are set once two distinct vias tagged its Loan; Kept, a Loan's mode
    // and the holders a Loan or address names (Reach, under the tier's read key) are the same under every non-special via. A read at
    // Limit and one at the pass's tier tag apart and reach under different keys, so the record is per tier (PLAN G775): a decided read
    // is never cut by Limit reads, whatever the order the gather reads the holders in. A cut read is scanned at `at`, the tier of σ(h)
    // the record is keyed by, never at one of its Placed part alone, so a holder's read tags at one tier whichever via it is.
    private ulong Scanned(ulong held, int via, MirOutcome at)
    {
        var shared = Shared(held);
        if (shared == 0 || this.reserves[via] || (this.wCount > 0 && this.escape[via]))
        {
            return held;
        }

        var key = (2 * shared) + (at.IsLimit ? 1 : 0);
        MirDataflow.Grow(ref this.scannedBy, key + 1);
        ref var record = ref this.scannedBy[key];
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

    /// <summary>The ascending merge of a state's two parts, an element in both once, at the decided tier where either part has it decided
    /// (MirLoanSets' join).</summary>
    private ref struct StateElements(ReadOnlySpan<TieredElement> placed, ReadOnlySpan<TieredElement> shared)
    {
        private ReadOnlySpan<TieredElement> placed = placed;
        private ReadOnlySpan<TieredElement> shared = shared;

        public TieredElement Current { get; private set; }

        public readonly StateElements GetEnumerator() => this;

        public bool MoveNext()
        {
            if (this.placed.IsEmpty && this.shared.IsEmpty)
            {
                return false;
            }

            var fromPlaced = this.shared.IsEmpty || (!this.placed.IsEmpty && this.placed[0].Element <= this.shared[0].Element);
            var fromShared = this.placed.IsEmpty || (!this.shared.IsEmpty && this.shared[0].Element <= this.placed[0].Element);
            this.Current = fromPlaced && (!fromShared || this.placed[0].Word <= this.shared[0].Word) ? this.placed[0] : this.shared[0];
            this.placed = fromPlaced ? this.placed[1..] : this.placed;
            this.shared = fromShared ? this.shared[1..] : this.shared;
            return true;
        }
    }
}
