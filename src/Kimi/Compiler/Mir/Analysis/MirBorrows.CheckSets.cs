// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A5's check sets (A3 5, E5 and E13 (1), (5)): every access at a location is checked on the closure of the states before it of the holders
// live after it that it does not overwrite and of E, with the values it takes. One gather per location builds every access's set: each
// Loan it finds, once per mode, is tagged with the sources that reach it, through the closure (a live direct Loan naming T makes σ(T)
// live), in passes: the live holders (Based); the location's stores into a holder live after it or made live by the closure, or into E,
// and E5's seeds (Stored); a borrow's R2 parents (Parented); an observing destruction's value (Observed); an activation holder's seed
// (Seeded); then each operand's value in operand order, so a Loan keeps the first operand that reaches it, and each operand's root in
// reverse, so it keeps the last. A pass scans a holder's state once and a value it takes once per holder it is found under (a later take
// tags nothing more; Took finds it by scanning the values taken under that holder at the location, so a take costs that list's length),
// and the location's entries are listed by holder, so a holder's lookup reads only its own entries (E13 (5)); each access then scans its
// root's Loans.
internal sealed partial class MirBorrows
{
    private const byte Based = 1;
    private const byte Stored = 2;
    private const byte Parented = 4;
    private const byte Observed = 8;
    private const byte Seeded = 16;
    private const byte Valued = 32;
    private const byte Rooted = 64;
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
    private Candidate[] candidates = [];
    private int[] reached = []; // By slot: the check that reached it.
    private byte[] slotFlags = []; // By slot: the passes that read its state at the check.
    private int[] slotValue = []; // By slot: the first operand whose value read its state.
    private int[] slotRoot = []; // By slot: the last operand whose root read its state.
    private int[] loanMarks = []; // By Loan and mode (2 × Loan + 1 for a unique one): the check that found it.
    private int[] candidateAt = []; // By Loan and mode: its candidate at that check.
    private int[] rootMarks = []; // By root key: the check that found a candidate on it.
    private int[] rootFirst = []; // By root key: the first and last of its candidates at that check.
    private int[] rootLast = [];
    private int[] markSeen = []; // By marker (τ_s, γ_H): the check that saw it in the Based or Stored passes.
    private int[] markVia = []; // By marker: the holder that check first saw it in (-1: a transit value).
    private int[] listed = []; // By slot + 1 (0: no holder): the check whose entry and taken lists it heads.
    private int[] entryFirst = []; // By slot + 1: the first of the location's entries into its holder at that check (None: none).
    private int[] takenFirst = []; // By slot + 1: the last value a pass took under its holder at that check (None: none).
    private int[] entryNext = []; // By entry: the next entry into its holder.
    private int candidateCount;
    private int takenCount;
    private int stamp;
    private byte pass;
    private int passIndex; // A Valued or Rooted pass's operand.

    // The check set of an activation in a holder.
    private static int Activation(int holder) => Activating - holder;

    // Restarts the checks' stamps for the view: those of its slots, Loans, roots and markers are cleared, so a view's cost is its own
    // (markVia, candidateAt and the root, entry and taken lists are read only where their stamps hold the check's).
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
        MirDataflow.Grow(ref this.listed, this.holderCount + 1);
        MirDataflow.Grow(ref this.entryFirst, this.holderCount + 1);
        MirDataflow.Grow(ref this.takenFirst, this.holderCount + 1);
        this.reached.AsSpan(0, this.holderCount).Clear();
        this.loanMarks.AsSpan(0, 2 * this.loanCount).Clear();
        this.rootMarks.AsSpan(0, keys).Clear();
        this.markSeen.AsSpan(0, this.loanBase).Clear();
        this.listed.AsSpan(0, this.holderCount + 1).Clear();
        this.Work += (2 * this.holderCount) + 1 + (2 * this.loanCount) + keys + this.loanBase;
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
        this.Pass(Based);
        this.Reach(Escaped, scan: true);
        this.Work += (this.holderCount + 63) >> 6;
        for (var word = 0; word < (this.holderCount + 63) >> 6; word++)
        {
            for (var bits = this.live[word]; bits != 0; bits &= bits - 1)
            {
                var slot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                this.Reach(slot, scan: !this.Overwritten(slot));
            }
        }

        this.Drain(state);
        this.Pass(Stored);
        while (this.pending.Count > 0)
        {
            var slot = this.pending[^1];
            this.pending.RemoveAt(this.pending.Count - 1);
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

        this.TakenValues(state, at);
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

        this.Work += this.sets.Count(value);
        foreach (var element in this.sets.Elements(value))
        {
            this.Consider(element, via);
        }

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

    // A holder reached at the check: in the Based and Stored passes its stores count, and its state is read where the pass reads it first
    // (a Based read stands for every pass, a Valued one for the later operands and a Rooted one for the earlier).
    private void Reach(int slot, bool scan)
    {
        if (this.reached[slot] != this.stamp)
        {
            (this.reached[slot], this.slotFlags[slot], this.slotValue[slot], this.slotRoot[slot]) = (this.stamp, 0, int.MaxValue, -1);
            if (this.pass is Based or Stored)
            {
                this.pending.Add(slot);
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
            foreach (var element in this.sets.Elements((int)state[slot]))
            {
                this.Consider(element, slot);
            }

            this.Work += this.sets.Count((int)state[slot]) + 1;
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
            this.Work += this.sets.Count(entry.Value);
            foreach (var element in this.sets.Elements(entry.Value))
            {
                this.Consider(element, -1);
            }
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

    // An element found in a holder (-1: a transit value) by the pass: a Loan once per mode (A3 5's mode: a two-phase Loan is shared only in
    // its reservation holder), its candidate tagged with the pass, whose named holder the closure reads; Φ_E makes E live. An activation
    // keeps the first two holders the Based, Seeded and Valued passes find it in.
    private void Consider(int element, int via)
    {
        if (element == Escaped)
        {
            this.Reach(Escaped, scan: true);
            return;
        }

        if (element < this.loanBase)
        {
            // A marker never conflicts; the holder it is first seen in witnesses err(ended) for a guard-local γ_H.
            if (this.pass is Based or Stored && this.markSeen[element] != this.stamp)
            {
                (this.markSeen[element], this.markVia[element]) = (this.stamp, via);
            }

            return;
        }

        var index = element - this.loanBase;
        ref readonly var loan = ref this.loans[index];
        var unique = loan.Mode == MirRefKind.Uniq || (loan.Mode == MirRefKind.TwoPhaseUniq && via != loan.Holder);
        var key = (2 * index) + (unique ? 1 : 0);
        if (this.loanMarks[key] != this.stamp)
        {
            (this.loanMarks[key], this.candidateAt[key]) = (this.stamp, this.candidateCount);
            MirDataflow.Grow(ref this.candidates, this.candidateCount + 1);
            this.candidates[this.candidateCount] = new(index, unique, via);
            this.Bucket(loan.Root, this.candidateCount++);
        }

        ref var candidate = ref this.candidates[this.candidateAt[key]];
        candidate.Tag(this.pass, this.passIndex, via);
        if (loan is { Direct: true, Named: >= 0 })
        {
            this.Reach(loan.Named, scan: true);
        }
    }

    // Appends a candidate to its root's list.
    private void Bucket(int root, int candidate)
    {
        if (this.rootMarks[root] != this.stamp)
        {
            (this.rootMarks[root], this.rootFirst[root]) = (this.stamp, candidate);
        }
        else
        {
            this.candidates[this.rootLast[root]].Next = candidate;
        }

        this.rootLast[root] = candidate;
    }

    // The first of a root's candidates at the check (None: none).
    private int First(int root) => this.rootMarks[root] == this.stamp ? this.rootFirst[root] : None;

    /// <summary>A Loan of the check sets, once per mode: its index, whether its mode in the holders it was found in is unique, the first
    /// holder (-1: a transit value), the passes that found it, the first operand whose value and the last whose root reached it, the first
    /// two holders an activation's passes found it in, and its root's next candidate.</summary>
    private struct Candidate(int loan, bool unique, int via)
    {
        internal readonly int Loan => loan;

        internal readonly bool Unique => unique;

        internal readonly int Via => via;

        internal byte Flags { get; private set; }

        internal int Value { get; private set; } = int.MaxValue;

        internal int Root { get; private set; } = -1;

        internal int ActVia { get; private set; } = None;

        internal int ActOther { get; private set; } = None;

        internal int Next { get; set; } = None;

        // Whether a check set holds it (A3 E13 (1)): an activation's in a holder, where the Based, Seeded or Valued passes found it in another
        // holder; another's, a Based one; an operand's, a Valued one from an operand up to it or a Rooted one from a later one; the others,
        // by their passes.
        internal readonly bool Takes(int taking) => taking <= Activating ? (this.ActVia != None && Activation(this.ActVia) != taking) || this.ActOther != None
            : (this.Flags & Based) != 0 || taking switch
            {
                >= 0 => this.Value <= taking || this.Root > taking,
                TakesStores => (this.Flags & Stored) != 0,
                TakesParents => (this.Flags & (Stored | Parented)) != 0,
                _ => (this.Flags & Observed) != 0,
            };

        internal void Tag(byte pass, int index, int via)
        {
            this.Flags |= pass is Valued or Rooted ? (byte)0 : pass;
            this.Value = pass == Valued ? Math.Min(this.Value, index) : this.Value;
            this.Root = pass == Rooted ? Math.Max(this.Root, index) : this.Root;
            if (pass is Based or Seeded or Valued && this.ActVia == None)
            {
                this.ActVia = via;
            }
            else if (pass is Based or Seeded or Valued && this.ActOther == None && via != this.ActVia)
            {
                this.ActOther = via;
            }
        }
    }
}
