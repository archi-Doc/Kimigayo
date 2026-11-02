// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's check-set candidates (A3 5's check sets and mode, E13 (1), (5)): each Loan an element of a gather's pass finds, once per mode (a
// two-phase Loan is shared only in its reservation holder), tagged with the passes, operands and holders that reached it, and listed by its
// Place's root, so an access scans only its root's; a marker found, the holder it was first seen in (E7's witness); an own-step entry
// found, its tag (MirBorrows.OwnSteps). What the drop tier finds is tagged apart (DECISIONS R4-M153): a candidate's Dropped tags, a
// marker's drop stamp and an own-step entry's drop tag, each with the drop-live holder as via. Which check sets hold a
// candidate is Tags.Takes, either tier's; the passes and the closure are the gather's (MirBorrows.CheckSets).
// Must not compare Places or decide a conflict (MirBorrows.Checks), or read a holder's state itself.
internal sealed partial class MirBorrows
{
    private Candidate[] candidates = [];
    private int[] loanMarks = []; // By Loan and mode (2 × Loan + 1 for a unique one): the check that found it.
    private int[] candidateAt = []; // By Loan and mode: its candidate at that check.
    private int[] rootMarks = []; // By root key: the check that found a candidate on it.
    private int[] rootFirst = []; // By root key: the first and last of its candidates at that check.
    private int[] rootLast = [];
    private int[] markSeen = []; // By marker (γ_H): the check that saw it in the Based or Stored passes.
    private int[] markVia = []; // By marker: the holder that check first saw it in (-1: a transit value).
    private (int Stamp, int Via)[] markDropped = []; // By marker: the check whose drop tier first saw it, and that tier's holder.
    private int candidateCount;

    // A state found in a holder (-1: a transit value, a state of one part) by the pass: its elements, in ascending order.
    private void Scan(ulong found, int via)
    {
        this.Work += this.Count(found);
        foreach (var element in this.ElementsOf(found))
        {
            this.Consider(element, found, via);
        }
    }

    // An element found in a holder (-1: a transit value) by the pass: a Loan once per mode (A3 5's mode: a two-phase Loan is shared only in
    // its reservation holder), its candidate tagged with the pass, whose named holder the closure reads; an address only its named holder;
    // Φ_E makes E live. An activation keeps the first two holders the Based, Seeded and Valued passes find it in. A carrier that keeps a W
    // element (MirBorrows.Causes' Kept) marks its candidate or guard marker kept. The drop tier tags its drop-live holder as the via.
    private void Consider(int element, ulong found, int via)
    {
        var kept = this.Kept(found, element, via);
        var dropped = (this.pass & DropTier) != 0;
        if (element == Escaped)
        {
            this.Reach(Escaped, scan: true);
            return;
        }

        if (element < this.loanBase)
        {
            // A marker never conflicts: the holder a guard-local γ_H is first seen in witnesses err(ended).
            if (this.pass is Based or Stored && this.markSeen[element] != this.stamp)
            {
                (this.markSeen[element], this.markVia[element]) = (this.stamp, via);
            }

            if (dropped && this.markDropped[element].Stamp != this.stamp)
            {
                this.markDropped[element] = (this.stamp, this.dropVia);
            }

            if (this.pass is Based or Stored && kept)
            {
                this.markKept[element] = this.stamp;
            }

            return;
        }

        var index = element - this.loanBase;
        if (index >= this.loanCount)
        {
            // An address never conflicts (D19): it reaches the holder it names; an own-step entry is tagged for its departures.
            if (this.loans[index].OwnStep)
            {
                this.Seen(index, via);
            }

            if (this.loans[index] is { Direct: true, Named: >= 0 } address)
            {
                this.Reach(address.Named, scan: true);
            }

            return;
        }

        ref readonly var loan = ref this.loans[index];
        var unique = loan.Mode == MirRefKind.Uniq || (loan.Mode == MirRefKind.TwoPhaseUniq && via != loan.Holder);
        var key = (2 * index) + (unique ? 1 : 0);
        if (this.loanMarks[key] != this.stamp)
        {
            (this.loanMarks[key], this.candidateAt[key]) = (this.stamp, this.candidateCount);
            MirDataflow.Grow(ref this.candidates, this.candidateCount + 1);
            this.candidates[this.candidateCount] = new(index, unique);
            this.Bucket(loan.Root, this.candidateCount++);
        }

        ref var candidate = ref this.candidates[this.candidateAt[key]];
        candidate.Tag(this.pass, this.passIndex, dropped ? this.dropVia : via);
        candidate.Kept |= kept;
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

    /// <summary>A Loan of the check sets, once per mode: its index, whether its mode in the holders it was found in is unique, its tags, the
    /// drop tier's apart (Dropped), and its root's next candidate.</summary>
    private struct Candidate(int loan, bool unique)
    {
        private Tags tags = new();
        private Tags dropped = new();

        internal readonly int Loan => loan;

        internal readonly bool Unique => unique;

        internal readonly Tags Tags => this.tags;

        internal readonly Tags Dropped => this.dropped;

        internal int Next { get; set; } = None;

        // Whether a carrier keeps it (rule W, MirBorrows.Causes): found in a transit value, a holder other than an escape holder, or an
        // escape holder as no Copy of the borrow, in some pass.
        internal bool Kept { get; set; }

        // Whether a check set holds it (Tags.Takes), in either tier.
        internal readonly bool Takes(int taking) => this.tags.Takes(taking) || this.dropped.Takes(taking);

        // A tag of a pass, a drop-tier pass's into Dropped as its plain pass.
        internal void Tag(byte pass, int index, int via)
        {
            if ((pass & DropTier) != 0)
            {
                this.dropped.Tag((byte)(pass & ~DropTier), index, via);
            }
            else
            {
                this.tags.Tag(pass, index, via);
            }
        }
    }

    /// <summary>What reached a candidate in some sets: the first holder (-1: a transit value), the passes, the first operand whose value
    /// and the last whose root reached it, and the first two holders an activation's passes found it in; none before the first tag.</summary>
    private struct Tags()
    {
        internal int Via { get; private set; } = None;

        internal byte Flags { get; private set; }

        internal int Value { get; private set; } = int.MaxValue;

        internal int Root { get; private set; } = -1;

        internal int ActVia { get; private set; } = None;

        internal int ActOther { get; private set; } = None;

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
            this.Via = this.Via == None ? via : this.Via;
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
