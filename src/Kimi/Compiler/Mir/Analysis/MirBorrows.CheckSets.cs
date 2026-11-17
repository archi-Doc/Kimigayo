// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A5's check sets (A3 5, E5 and E13 (1), (5)): every access at a location is checked on the closure of the states before it of the holders
// live after it that it does not overwrite and of E, with the values it takes. One gather per location builds every access's set: each
// Loan it finds, once per mode, is tagged with the sources that reach it, through the closure (a live direct Loan naming T makes σ(T)
// live), in passes: the live holders (Based); the location's stores into a holder live after it or made live by the closure, or into E,
// and E5's seeds (Stored); a borrow's R2 parents (Parented); an observing destruction's value (Observed); an activation holder's seed
// (Seeded); then each operand's value in operand order, so a Loan keeps the first operand that reaches it, and each operand's root in
// reverse, so it keeps the last. A read has a tier (MirBorrows.Candidates' ReadTier): its read key's, Limit where the state read carries
// Φ_U. The closure keeps it (R4-M198's Φ_U, PLAN G775), as the dataflow's union keeps Φ_U through Content and Chain: a holder a read at
// Limit reaches has its own read key (slot + holderCount), under which its state and, in the Stored pass, its stores are read at that
// tier, and a reach at the pass's tier reads the holder again. Every memo of a gather read is keyed by its read key or tier: a pass
// scans a holder's state once per read key (its Shared set until two vias read it at that tier, MirBorrows.States' Scanned), reads its
// stores once per read key (pending), and takes a value once per holder it is found under and tier (a later take tags nothing more;
// Took finds it by scanning the values taken under that holder at the location, so a take costs that list's length); the location's
// entries are listed by holder, so a holder's lookup reads only its own entries (E13 (5)); each access then scans its root's Loans.
// After every pass, the drop tier (DECISIONS R4-M153): from each holder live after the location only through its drop slot
// (MirBorrows.Liveness), its Based and Stored passes again, marked DropTier, with that holder as their via; a read key an earlier pass
// read is not read again, so a holder the passes read only at its Limit key is read again at Deep (more Work, no other record: a Limit
// tag outranks a Deep one). The elements a pass finds become candidates in MirBorrows.Candidates.
internal sealed partial class MirBorrows
{
    private const byte Based = 1;
    private const byte Stored = 2;
    private const byte Parented = 4;
    private const byte Observed = 8;
    private const byte Seeded = 16;
    private const byte Valued = 32;
    private const byte Rooted = 64;
    private const byte DropTier = 128; // A Based or Stored pass of the drop tier, from a holder live only through its drop slot.
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

    private readonly List<int> pending = []; // Read keys reached in the Based or Stored passes whose stores are still to read.
    private readonly List<int> scanning = []; // Read keys whose states are still to read in the pass.
    private readonly List<MirOperand> operating = []; // The location's operands, in operand order.
    private (int Value, byte Pass, MirOutcome Tier, int Next)[] taken = []; // The values the check's passes took, listed by holder (Took).
    private int[] reached = []; // By read key (a slot; slot + holderCount at the Limit tier): the check that reached it.
    private byte[] slotFlags = []; // By read key: the passes that read its state at the check.
    private int[] slotValue = []; // By read key: the first operand whose value read its state.
    private int[] slotRoot = []; // By read key: the last operand whose root read its state.
    private int[] listed = []; // By slot + 1 (0: no holder): the check whose entry and taken lists it heads.
    private int[] entryFirst = []; // By slot + 1: the first of the location's entries into its holder at that check (None: none).
    private int[] takenFirst = []; // By slot + 1: the last value a pass took under its holder at that check (None: none).
    private int[] entryNext = []; // By entry: the next entry into its holder.
    private int takenCount;
    private int stamp;
    private byte pass;
    private int passIndex; // A Valued or Rooted pass's operand.
    private int dropVia; // A drop-tier pass's holder, the via of what it finds.

    // The check set of an activation in a holder.
    private static int Activation(int holder) => Activating - holder;

    // Restarts the checks' stamps for the view: those of its slots, Loans, roots, markers and own-step entries are cleared, so a view's cost
    // is its own (markVia, candidateAt and the root, entry and taken lists are read only where their stamps hold the check's).
    private void Restamp()
    {
        this.stamp = 0;
        var keys = this.body.Locals.Length + this.body.Statics.Length;
        MirDataflow.Grow(ref this.reached, 2 * this.holderCount);
        MirDataflow.Grow(ref this.slotFlags, 2 * this.holderCount);
        MirDataflow.Grow(ref this.slotValue, 2 * this.holderCount);
        MirDataflow.Grow(ref this.slotRoot, 2 * this.holderCount);
        MirDataflow.Grow(ref this.loanMarks, 2 * this.loanCount);
        MirDataflow.Grow(ref this.candidateAt, 2 * this.loanCount);
        MirDataflow.Grow(ref this.rootMarks, keys);
        MirDataflow.Grow(ref this.rootFirst, keys);
        MirDataflow.Grow(ref this.rootLast, keys);
        MirDataflow.Grow(ref this.markSeen, this.loanBase);
        MirDataflow.Grow(ref this.markVia, this.loanBase);
        MirDataflow.Grow(ref this.markKept, this.loanBase);
        MirDataflow.Grow(ref this.markRested, this.loanBase);
        MirDataflow.Grow(ref this.listed, this.holderCount + 1);
        MirDataflow.Grow(ref this.entryFirst, this.holderCount + 1);
        MirDataflow.Grow(ref this.takenFirst, this.holderCount + 1);
        this.reached.AsSpan(0, 2 * this.holderCount).Clear();
        this.loanMarks.AsSpan(0, 2 * this.loanCount).Clear();
        this.rootMarks.AsSpan(0, keys).Clear();
        this.markSeen.AsSpan(0, this.loanBase).Clear();
        this.markKept.AsSpan(0, this.loanBase).Clear();
        this.markRested.AsSpan(0, this.loanBase).Clear();
        this.listed.AsSpan(0, this.holderCount + 1).Clear();
        this.Work += (3 * this.holderCount) + 1 + (2 * this.loanCount) + keys + (2 * this.loanBase);
        if (this.ownEntries > 0)
        {
            this.RestampOwnSteps(keys);
        }

        if (this.drops.Count > 0)
        {
            MirDataflow.Grow(ref this.markDropped, this.loanBase);
            this.markDropped.AsSpan(0, this.loanBase).Clear();
            this.Work += this.loanBase;
        }
    }

    // The gather of a location's check sets: its entries listed by holder, the Based pass from E and the live holders (an overwritten one
    // is reached for its stores but not read), the Stored pass from the stores into the holders those passes reach, then the passes of the
    // location's accesses and activations, and the operands' values and roots where an access or an activation takes them; last the drop
    // tier.
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
        this.StoredPass(state, Stored);
        this.TakenValues(state, at);
        this.DropPasses(state);
    }

    // A Stored pass (plain or of the drop tier): the stores into each holder an earlier pass of the gather reached and has not yet read
    // them for, at its read key's tier.
    private void StoredPass(ulong[] state, byte pass)
    {
        this.Pass(pass);
        while (this.pending.Count > 0)
        {
            var key = this.pending[^1];
            this.pending.RemoveAt(this.pending.Count - 1);
            for (var e = this.entryFirst[this.Lists(this.SlotOf(key))]; e != None; e = this.entryNext[e])
            {
                this.Work++;
                if (this.entries[e].Op != Cleared)
                {
                    this.StoredValue(this.entries[e], this.TierOf(key));
                }
            }

            this.Drain(state);
        }
    }

    // The drop tier (DECISIONS R4-M153): from each holder live after the location only through its drop slot, the Based pass (overwritten,
    // it is reached for its stores but not read) and the Stored pass, marked DropTier, with that holder as their via.
    private void DropPasses(ulong[] state)
    {
        foreach (var (drop, local) in this.drops)
        {
            var holder = this.slots[local];
            this.Work++;
            if ((this.live[drop >> 6] & (1UL << drop)) == 0 || (this.live[holder >> 6] & (1UL << holder)) != 0)
            {
                continue;
            }

            this.dropVia = holder;
            this.Pass(Based | DropTier);
            this.Reach(holder, scan: !this.Overwritten(holder));
            this.Drain(state);
            this.StoredPass(state, Stored | DropTier);
        }
    }

    // The Based pass from E and the live holders (an overwritten one is reached for its stores but not read), each holder's state read and
    // closed through the holders its direct Loans name; also Entry(B)'s gather (MirBorrows.Defaults), where no entry is listed yet.
    private void BasePass(ulong[] state)
    {
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
            this.Seed(this.Set(state[this.slots[local]]), this.PassTier);
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
        if (this.Took(value, via, this.PassTier))
        {
            return;
        }

        this.Scan(State(value, 0), via, this.ReadTier(State(value, 0), this.PassTier));
        this.Drain(state);
    }

    // Whether the pass took a value under a holder (-1: none) at a tier before at the check, else records it: a later take tags its Loans
    // no further (a Valued pass keeps the least operand, the other passes' tags do not depend on it), so the pass reads it once per tier.
    private bool Took(int value, int via, MirOutcome at)
    {
        var key = this.Lists(via);
        for (var t = this.takenFirst[key]; t != None; t = this.taken[t].Next)
        {
            this.Work++;
            if (this.taken[t].Value == value && this.taken[t].Pass == this.pass && this.taken[t].Tier.IsLimit == at.IsLimit)
            {
                return true;
            }
        }

        MirDataflow.Grow(ref this.taken, this.takenCount + 1);
        this.taken[this.takenCount] = (value, this.pass, at, this.takenFirst[key]);
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

    // Starts a pass instance, at the pass's tier until a read fixes its own (a reach outside a read takes the pass's read key).
    private void Pass(byte pass, int index = 0)
    {
        (this.pass, this.passIndex, this.instance) = (pass, index, this.instance + 1);
        this.tier = this.PassTier;
    }

    // A holder's read key at the reaching read's tier (R4-M198's undecided class, PLAN G775): reached by a read at the Limit tier (through
    // a set carrying Φ_U, MirBorrows.Puns), it rests on the undecided write: slot + holderCount; else the slot.
    private int KeyOf(int slot) => this.tier.IsLimit ? slot + this.holderCount : slot;

    // A read key's holder.
    private int SlotOf(int key) => key < this.holderCount ? key : key - this.holderCount;

    // A read key's tier: Limit for one reached only through a set carrying Φ_U, else the pass's.
    private MirOutcome TierOf(int key) => key < this.holderCount ? this.PassTier : MirOutcome.Limit;

    // A holder reached at the check, under its read key (KeyOf): in the Based and Stored passes, the drop tier's included, its stores count
    // at the key's tier, and its state is read at that tier where the pass reads the key first (a Based read stands for every pass, a
    // Valued one for the later operands and a Rooted one for the earlier); a Limit key only where the pass has not read the slot's own
    // key, and a reach at the pass's tier later in the pass reads it again. The drop tier's reach at Deep reads the slot's own key, so it
    // may read again a holder the passes read at Limit: more Work, no other record (a Limit tag outranks a Deep one, Candidate.Outcome).
    private void Reach(int slot, bool scan)
    {
        var key = this.KeyOf(slot);
        if (this.Starts(key) && (this.pass & ~DropTier) is Based or Stored)
        {
            this.pending.Add(key);
        }

        if (scan && this.Reads(key) && (key == slot || this.Reads(slot)))
        {
            this.slotFlags[key] |= this.pass is Valued or Rooted ? (byte)0 : this.pass;
            this.slotValue[key] = this.pass == Valued ? this.passIndex : this.slotValue[key];
            this.slotRoot[key] = this.pass == Rooted ? this.passIndex : this.slotRoot[key];
            this.scanning.Add(key);
        }
    }

    // Starts a read key's marks at the check; whether it was not reached there before.
    private bool Starts(int key)
    {
        if (this.reached[key] == this.stamp)
        {
            return false;
        }

        (this.reached[key], this.slotFlags[key], this.slotValue[key], this.slotRoot[key]) = (this.stamp, 0, int.MaxValue, -1);
        return true;
    }

    // Whether the pass reads a read key's state first at the check (a key not reached there yet: yes).
    private bool Reads(int key) => this.reached[key] != this.stamp || ((this.slotFlags[key] & Based) == 0 && this.pass switch
    {
        Valued => this.slotValue[key] > this.passIndex,
        Rooted => this.slotRoot[key] < this.passIndex,
        _ => (this.slotFlags[key] & this.pass) == 0,
    });

    // Reads the pass's holder states, each element found in its holder at its read key's tier.
    private void Drain(ulong[] state)
    {
        while (this.scanning.Count > 0)
        {
            var key = this.scanning[^1];
            this.scanning.RemoveAt(this.scanning.Count - 1);
            var slot = this.SlotOf(key);
            var at = this.ReadTier(state[slot], this.TierOf(key)); // Of σ(h): the tier Scanned keys and its cut is read at.
            this.Scan(this.Scanned(state[slot], slot, at), slot, at);
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

    // A stored value at the check (transit), at its holder's read key's tier `at`: a flow's Loans are candidates; the holders a borrow's
    // parents and every issued Loan name seed the closure (E5).
    private void StoredValue(in Entry entry, MirOutcome at)
    {
        if (!entry.Issue && !this.Took(entry.Value, -1, at))
        {
            this.Scan(State(entry.Value, 0), -1, this.ReadTier(State(entry.Value, 0), at));
        }

        this.Seed(entry.Issue ? this.sets.Union(entry.Value, entry.Own) : entry.Own, at);
    }

    // E5's seed: the holders a set's direct Loans name, and E where it carries Φ_E, are read, at the read's tier (MirBorrows.Candidates'
    // ReadTier) of the set at its holder's tier `at`.
    private void Seed(int set, MirOutcome at)
    {
        this.tier = this.ReadTier(State(set, 0), at);
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
