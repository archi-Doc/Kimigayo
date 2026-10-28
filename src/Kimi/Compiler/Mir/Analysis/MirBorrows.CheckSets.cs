// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A5's check sets (A3 5, E5 and E13 (1), (5)): every access at a location is checked on the closure of the states before it of the holders
// live after it that it does not overwrite and of E, with the values it takes. One gather per location builds every access's set: each
// Loan it finds, once per mode, is tagged with the sources that reach it, through the closure (a live direct Loan naming T makes σ(T)
// live), in passes: the live holders (Based); the location's stores into a holder live after it or made live by the closure, or into E,
// and E5's seeds (Stored); in a view with a past use (MirBorrows.Liveness), the holders live only for a destruction past the destruction
// walk's bound, and the stores into the holders they make live, each tagged with that holder (Past, R4-M153: a check that only they meet
// rests on the bound); a borrow's R2 parents (Parented); an observing destruction's value (Observed); an activation holder's seed
// (Seeded); then each operand's value in operand order, so a Loan keeps the first operand that reaches it, and each operand's root in
// reverse, so it keeps the last. A pass scans a holder's state once and a value it takes once per holder it is found under (a later take
// tags nothing more; Took finds it by scanning the values taken under that holder at the location, so a take costs that list's length),
// and the location's entries are listed by holder, so a holder's lookup reads only its own entries (E13 (5)); each access then scans its
// root's Loans. The elements a pass finds become candidates in MirBorrows.Candidates.
internal sealed partial class MirBorrows
{
    private const byte Based = 1;
    private const byte Stored = 2;
    private const byte Parented = 4;
    private const byte Observed = 8;
    private const byte Seeded = 16;
    private const byte Valued = 32;
    private const byte Rooted = 64;
    private const byte Past = 128;
    private const int None = int.MinValue; // No via, operand or candidate.
    private const int Taken = -2; // A witness: the access's own location, whose operation uses the value that carries the Loan.

    // An access's check set (A3 E13 (1)): operand i's (from 0) its earlier and own operands' values and its later operands' roots; a
    // destination write's, a store's and an access that takes no value transit and E5's seed (TakesStores); a borrow's its R2 parents and
    // E5's seed (TakesParents); an observing destruction's its value (TakesValue); an activation's in a holder (Activation) the operands'
    // values and its holder's seed, its holder left out.
    private const int TakesStores = -1;
    private const int TakesParents = -2;
    private const int TakesValue = -3;
    private const int Activating = -4;

    private readonly List<int> pending = []; // Slots reached in the Based or Stored passes whose stores are still to read.
    private readonly List<int> scanning = []; // Slots whose states are still to read in the pass.
    private readonly List<MirOperand> operating = []; // The location's operands, in operand order.
    private (int Value, byte Pass, int Next)[] taken = []; // The values the check's passes took, listed by holder (Took).
    private int[] reached = []; // By slot: the check that reached it.
    private byte[] slotFlags = []; // By slot: the passes that read its state at the check.
    private int[] slotValue = []; // By slot: the first operand whose value read its state.
    private int[] slotRoot = []; // By slot: the last operand whose root read its state.
    private int[] listed = []; // By slot + 1 (0: no holder): the check whose entry and taken lists it heads.
    private int[] entryFirst = []; // By slot + 1: the first of the location's entries into its holder at that check (None: none).
    private int[] takenFirst = []; // By slot + 1: the last value a pass took under its holder at that check (None: none).
    private int[] pastRoots = []; // By slot, in a view with a past use: the holder live only for a past use whose closure reached it first.
    private int[] entryNext = []; // By entry: the next entry into its holder.
    private int takenCount;
    private int stamp;
    private byte pass;
    private int passIndex; // A Valued or Rooted pass's operand.
    private int pastRoot; // The Past pass's holder live only for a past use, whose closure or stores it reads.

    // The check set of an activation in a holder.
    private static int Activation(int holder) => Activating - holder;

    // Restarts the checks' stamps for the view: those of its slots, Loans, roots, markers and own-step entries are cleared, so a view's cost
    // is its own (markVia, candidateAt and the root, entry and taken lists are read only where their stamps hold the check's).
    private void Restamp()
    {
        this.stamp = 0;
        var keys = this.body.Locals.Length + this.body.Statics.Length;
        MirDataflow.Grow(ref this.reached, this.holderCount);
        MirDataflow.Grow(ref this.slotFlags, this.holderCount);
        MirDataflow.Grow(ref this.slotValue, this.holderCount);
        MirDataflow.Grow(ref this.slotRoot, this.holderCount);
        MirDataflow.Grow(ref this.loanMarks, 2 * this.loanCount);
        MirDataflow.Grow(ref this.candidateAt, 2 * this.loanCount);
        MirDataflow.Grow(ref this.rootMarks, keys);
        MirDataflow.Grow(ref this.rootFirst, keys);
        MirDataflow.Grow(ref this.rootLast, keys);
        MirDataflow.Grow(ref this.markSeen, this.loanBase);
        MirDataflow.Grow(ref this.markVia, this.loanBase);
        MirDataflow.Grow(ref this.markKept, this.loanBase);
        MirDataflow.Grow(ref this.listed, this.holderCount + 1);
        MirDataflow.Grow(ref this.entryFirst, this.holderCount + 1);
        MirDataflow.Grow(ref this.takenFirst, this.holderCount + 1);
        if (this.pastUses > 0)
        {
            MirDataflow.Grow(ref this.markPast, this.loanBase);
            this.markPast.AsSpan(0, this.loanBase).Clear();
        }

        this.reached.AsSpan(0, this.holderCount).Clear();
        this.loanMarks.AsSpan(0, 2 * this.loanCount).Clear();
        this.rootMarks.AsSpan(0, keys).Clear();
        this.markSeen.AsSpan(0, this.loanBase).Clear();
        this.markKept.AsSpan(0, this.loanBase).Clear();
        this.listed.AsSpan(0, this.holderCount + 1).Clear();
        this.Work += (2 * this.holderCount) + 1 + (2 * this.loanCount) + keys + (2 * this.loanBase);
        if (this.ownEntries > 0)
        {
            this.RestampOwnSteps(keys);
        }
    }

    // The gather of a location's check sets: its entries listed by holder, the Based pass from E and the live holders (an overwritten one
    // is reached for its stores but not read), the Stored pass from the stores into the holders those passes reach, then the passes of the
    // location's accesses and activations, and the operands' values and roots where an access or an activation takes them.
    private void Gather(ulong[] state, MirLocation at, int stores)
    {
        this.stamp++;
        (this.candidateCount, this.takenCount) = (0, 0);
        MirDataflow.Grow(ref this.entryNext, stores);
        for (var e = stores - 1; e >= 0; e--)
        {
            var key = this.Lists(this.entries[e].Slot);
            (this.entryNext[e], this.entryFirst[key]) = (this.entryFirst[key], e);
        }

        this.Work += stores;
        this.BasePass(state);
        this.Pass(Stored);
        this.Stores(state);
        if (this.pastUses > 0)
        {
            this.PastPass(state);
            this.Stores(state);
        }

        this.TakenValues(state, at);
    }

    // The stores of the holders the Based, Stored and Past passes reached, each value read in the pass (Past: under the holder that made it
    // live).
    private void Stores(ulong[] state)
    {
        while (this.pending.Count > 0)
        {
            var slot = this.pending[^1];
            this.pending.RemoveAt(this.pending.Count - 1);
            this.pastRoot = this.pass == Past ? this.pastRoots[slot] : this.pastRoot;
            for (var e = this.entryFirst[this.Lists(slot)]; e != None; e = this.entryNext[e])
            {
                this.Work++;
                if (this.entries[e].Op != Cleared)
                {
                    this.StoredValue(this.entries[e]);
                }
            }

            this.Drain(state);
        }
    }

    // The Based pass from E and the holders live for a use (in a view with a past use, not only for one; an overwritten one is reached for
    // its stores but not read), each holder's state read and closed through the holders its direct Loans name; also Entry(B)'s gather
    // (MirBorrows.Defaults), where no entry is listed yet.
    private void BasePass(ulong[] state)
    {
        this.Pass(Based);
        this.Reach(Escaped, scan: true);
        this.Work += (this.holderCount + 63) >> 6;
        for (var word = 0; word < (this.holderCount + 63) >> 6; word++)
        {
            for (var bits = this.pastUses > 0 ? this.used[word] : this.live[word]; bits != 0; bits &= bits - 1)
            {
                var slot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                this.Reach(slot, scan: !this.Overwritten(slot));
            }
        }

        this.Drain(state);
    }

    // The Past pass (R4-M153): each holder live only for a destruction past the destruction walk's bound, read as the Based pass reads a
    // live one and closed through the holders its direct Loans name, what it reaches tagged with it.
    private void PastPass(ulong[] state)
    {
        this.Pass(Past);
        MirDataflow.Grow(ref this.pastRoots, this.holderCount);
        this.Work += (this.holderCount + 63) >> 6;
        for (var word = 0; word < (this.holderCount + 63) >> 6; word++)
        {
            for (var bits = this.live[word] & ~this.used[word]; bits != 0; bits &= bits - 1)
            {
                this.pastRoot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                this.Reach(this.pastRoot, scan: !this.Overwritten(this.pastRoot));
                this.Drain(state);
            }
        }
    }

    // The passes of what the location's accesses and activations take (A3 E13 (1)): a borrow's R2 parents and an observing destruction's
    // value under their root's holder, each activation holder's seed (E5), and where an operand or an activation takes them, the
    // operands' values under their roots' holders in operand order (a Loan keeps the first) and their roots in reverse (it keeps the last).
    private void TakenValues(ulong[] state, MirLocation at)
    {
        var operands = false;
        for (var a = 0; a < this.accessCount; a++)
        {
            var access = this.accesses[a];
            operands |= access.Taking >= 0;
            if (access.Taking is TakesParents or TakesValue)
            {
                this.Pass(access.Taking == TakesParents ? Parented : Observed);
                var value = access.Taking == TakesParents ? this.Issued(state, access.Place, access.Reborrow, -1).Value : this.Read(state, access.Place);
                this.Value(state, value, access.Place.Root);
            }
        }

        this.Pass(Seeded);
        foreach (var (local, _, _) in this.reserving)
        {
            this.Seed((int)state[this.slots[local]]);
            this.Drain(state);
        }

        if (!operands && this.reserving.Count == 0)
        {
            return;
        }

        this.Operating(at);
        for (var j = 0; j < this.operating.Count; j++)
        {
            this.Pass(Valued, j);
            if (this.operating[j] is { Kind: not MirOperandKind.Const } operand)
            {
                this.Value(state, this.Read(state, operand.Place), operand.Place.Root);
            }
        }

        for (var j = this.operating.Count - 1; operands && j >= 0; j--)
        {
            this.Pass(Rooted, j);
            if (this.operating[j] is { Kind: not MirOperandKind.Const, Place.Root: var root } && (root < 0 ? Escaped : this.slots[root]) is var slot and >= 0)
            {
                this.Reach(slot, scan: true);
                this.Drain(state);
            }
        }
    }

    // A location's operands in operand order (MirUses's): an rvalue's, but a Reborrowing PairAcquire's, which is its Use's borrow; a
    // SwitchInt's operand; a Call's Value callee, then its arguments.
    private void Operating(MirLocation at)
    {
        this.operating.Clear();
        ref readonly var data = ref this.body.Blocks[at.Block];
        if (at.Index < data.Count)
        {
            ref readonly var statement = ref this.body.Statements[data.Start + at.Index];
            if (statement.Kind == MirStatementKind.Assign && this.Reborrowing(statement) == MirRefKind.Raw)
            {
                var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
                this.operating.AddRange(this.body.Operands.Slice(rvalue.Start, rvalue.Count));
            }

            return;
        }

        var terminator = data.Terminator;
        if (terminator.Kind == MirTerminatorKind.SwitchInt || terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value })
        {
            this.operating.Add(terminator.Operand);
        }

        if (terminator.Kind == MirTerminatorKind.Call)
        {
            this.operating.AddRange(this.body.Operands.Slice(terminator.Start, terminator.Count));
        }
    }

    // A value a pass takes, under its root's holder (-1 for a static's or another local's): its Loans, whose closure the pass reads.
    private void Value(ulong[] state, int value, int root)
    {
        var via = root >= 0 ? this.slots[root] : -1;
        if (this.Took(value, via))
        {
            return;
        }

        this.Scan(value, via);
        this.Drain(state);
    }

    // Whether the pass took a value under a holder (-1: none) before at the check, else records it: a later take tags its Loans no
    // further (a Valued pass keeps the least operand, the other passes' tags do not depend on it), so the pass reads it once.
    private bool Took(int value, int via)
    {
        var key = this.Lists(via);
        for (var t = this.takenFirst[key]; t != None; t = this.taken[t].Next)
        {
            this.Work++;
            if (this.taken[t].Value == value && this.taken[t].Pass == this.pass)
            {
                return true;
            }
        }

        MirDataflow.Grow(ref this.taken, this.takenCount + 1);
        this.taken[this.takenCount] = (value, this.pass, this.takenFirst[key]);
        this.takenFirst[key] = this.takenCount++;
        return false;
    }

    // The key of a holder's entry and taken lists at the check (slot -1: no holder), started empty.
    private int Lists(int slot)
    {
        if (this.listed[slot + 1] != this.stamp)
        {
            (this.listed[slot + 1], this.entryFirst[slot + 1], this.takenFirst[slot + 1]) = (this.stamp, None, None);
        }

        return slot + 1;
    }

    private void Pass(byte pass, int index = 0) => (this.pass, this.passIndex) = (pass, index);

    // A holder reached at the check: in the Based, Stored and Past passes its stores count (a Past one's under the holder that made it live),
    // and its state is read where the pass reads it first (a Based read stands for every pass, a Valued one for the later operands and a
    // Rooted one for the earlier).
    private void Reach(int slot, bool scan)
    {
        if (this.reached[slot] != this.stamp)
        {
            (this.reached[slot], this.slotFlags[slot], this.slotValue[slot], this.slotRoot[slot]) = (this.stamp, 0, int.MaxValue, -1);
            if (this.pass is Based or Stored or Past)
            {
                this.pending.Add(slot);
            }

            if (this.pass == Past)
            {
                this.pastRoots[slot] = this.pastRoot;
            }
        }

        var reads = scan && (this.slotFlags[slot] & Based) == 0 && this.pass switch
        {
            Valued => this.slotValue[slot] > this.passIndex,
            Rooted => this.slotRoot[slot] < this.passIndex,
            _ => (this.slotFlags[slot] & this.pass) == 0,
        };
        if (reads)
        {
            this.slotFlags[slot] |= this.pass is Valued or Rooted ? (byte)0 : this.pass;
            this.slotValue[slot] = this.pass == Valued ? this.passIndex : this.slotValue[slot];
            this.slotRoot[slot] = this.pass == Rooted ? this.passIndex : this.slotRoot[slot];
            this.scanning.Add(slot);
        }
    }

    // Reads the pass's holder states, each element found in its holder.
    private void Drain(ulong[] state)
    {
        while (this.scanning.Count > 0)
        {
            var slot = this.scanning[^1];
            this.scanning.RemoveAt(this.scanning.Count - 1);
            this.Scan((int)state[slot], slot);
            this.Work++;
        }
    }

    private bool Overwritten(int slot)
    {
        for (var e = this.entryFirst[this.Lists(slot)]; e != None; e = this.entryNext[e])
        {
            this.Work++;
            if (this.entries[e].Op == Replaced)
            {
                return true;
            }
        }

        return false;
    }

    // A stored value at the check (transit): a flow's Loans are candidates; the holders a borrow's parents and every issued Loan name seed the
    // closure (E5).
    private void StoredValue(in Entry entry)
    {
        if (!entry.Issue && !this.Took(entry.Value, -1))
        {
            this.Scan(entry.Value, -1);
        }

        this.Seed(entry.Issue ? this.sets.Union(entry.Value, entry.Own) : entry.Own);
    }

    // E5's seed: the holders a set's direct Loans name, and E where it carries Φ_E, are read.
    private void Seed(int set)
    {
        this.Work += this.sets.Count(set);
        foreach (var element in this.sets.Elements(set))
        {
            if (element == Escaped || (element >= this.loanBase && this.loans[element - this.loanBase] is { Direct: true, Named: >= 0 }))
            {
                this.Reach(element == Escaped ? Escaped : this.loans[element - this.loanBase].Named, scan: true);
            }
        }
    }
}
