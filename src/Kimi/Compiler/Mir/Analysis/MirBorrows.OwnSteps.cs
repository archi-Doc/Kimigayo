// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's own-step departures (SPEC 5.2.1 condition 3; DECISIONS R4-M116 OQ-1, design B′ §5 on B §5.8): at a location of the checks replay,
// an access that may end an owning value's stay in its holder while a value carrying a pointer formed through that own step may still be
// used. An own-step entry (MirBorrows.Addresses) found at the check is tagged by MirBorrows.Candidates where a pass counts it (Based or
// Stored: live after the access, or stored there into a holder live after it or into E; Valued: an operand's value, which counts for an
// activation, whose call uses its operands after taking the owner; the drop tier: kept only by a holder live through a Drop past the
// destruction walk's bound, DECISIONS R4-M153) and listed on its root, so an access scans only its root's. An access with path S departs
// an entry with owning path O when S prefixes O step by step (15.6.2, MirBorrows.Checks' Meets) and it is a Move (a Take of a raw Place
// included), a write or a destruction of any class with no borrow or raw Deref of O past S, or an exclusive borrow or a call's activation
// of a reservation across any steps. A departure whose operation and formation lie in one inline default copy is its template's (Covered).
// One record per location: every record at a location has its subject, which MirReport merges. A departure a counted pass gives is the
// location's, else one only the drop tier gives, which rests on that Drop: the Warning, its witness by the one rule (the holder's next use
// in the location's chain, here its drop slot's; none past the chain), and the depth limit at the holder's declaration (DroppedDeparture).
// Before the holders, a view whose only producer input is its own-step entries is a producer only where a use may depart one by the same
// test with no liveness (MayDepart, D5, R4-M141): its other records, a retention Limit's and a storage limit's, then leave it as any view no
// conflict can depend on.
// Must not issue, end or conflict a Loan (an entry is never a candidate, D19).
internal sealed partial class MirBorrows
{
    private const int Listed = -1; // The stamp of MayDepart's lists: every own-step entry on its root, before any check.

    // By address (entry − Loans): the check that found it (Listed: MayDepart's), the holder its Based or Stored pass first found it in
    // (Taken: only an operand's value; None: neither), the next entry found on its root at that check, and the holder the drop tier first
    // found it through (None: none).
    private (int Stamp, int Via, int Next, int Dropped)[] ownTags = [];
    private (int Stamp, int First)[] ownRoots = []; // By root key: the first own-step entry found on it at that check (or listed).
    private int ownEntries; // The own-step entries of the view.
    private (int Entry, int Holder) dropDeparture; // The location's first departure only the drop tier gives, and its drop-live holder.

    // Restarts the tags for the view, where it has own-step entries.
    private void RestampOwnSteps(int keys)
    {
        MirDataflow.Grow(ref this.ownTags, this.addressCount);
        MirDataflow.Grow(ref this.ownRoots, keys);
        this.ownTags.AsSpan(0, this.addressCount).Clear();
        this.ownRoots.AsSpan(0, keys).Clear();
        this.Work += this.addressCount + keys;
    }

    // An own-step entry a pass found in a holder (-1: a transit value): tagged at the check in the Based and Stored passes (with the holder),
    // the Valued pass (Taken; the operand passes follow the Based and Stored ones) and the drop tier (with its drop-live holder), listed on
    // its root once.
    private void Seen(int index, int via)
    {
        var dropped = (this.pass & DropTier) != 0;
        if (!dropped && this.pass is not (Based or Stored or Valued))
        {
            return;
        }

        ref var tag = ref this.ownTags[index - this.loanCount];
        if (tag.Stamp != this.stamp)
        {
            ref var root = ref this.ownRoots[this.loans[index].Root];
            tag = (this.stamp, None, root.Stamp == this.stamp ? root.First : None, None);
            root = (this.stamp, index);
        }

        if (dropped)
        {
            tag.Dropped = tag.Dropped == None ? this.dropVia : tag.Dropped;
        }
        else if (tag.Via == None)
        {
            tag.Via = this.pass == Valued ? Taken : via;
        }
    }

    // The location's departures: its listed accesses but reads and shared borrows (the effect access follows them), then the reservations
    // it activates, up to the first recorded. One departure only the drop tier gives is recorded where no other is (the first met).
    private void Departures(MirLocation at, int accessed)
    {
        if (this.ownEntries == 0)
        {
            return;
        }

        this.dropDeparture = (None, None);

        for (var a = 0; a < accessed; a++)
        {
            var access = this.accesses[a];
            var root = this.Key(access.Place.Root);
            if (access.Kind is AccessKind.Read or AccessKind.SharedBorrow || this.ownRoots[root].Stamp != this.stamp)
            {
                continue;
            }

            var count = this.placeSteps.Walk(this.body, access.Place, access.Reborrow, ref this.accessSteps);
            if (this.Depart(at, root, this.accessSteps.AsSpan(0, count), access.Kind))
            {
                return;
            }
        }

        foreach (var (_, reservation, _) in this.reserving)
        {
            var loan = this.loans[reservation];
            var kind = loan.Destroying ? AccessKind.ActivateInPlace : AccessKind.Activate;
            if (this.ownRoots[loan.Root].Stamp == this.stamp && this.Depart(at, loan.Root, this.loanSteps.AsSpan(loan.Start, loan.Count), kind))
            {
                return;
            }
        }

        if (this.dropDeparture.Entry != None)
        {
            this.DroppedDeparture(this.dropDeparture.Entry, this.dropDeparture.Holder, at);
        }
    }

    // The first own-step entry found on a root that an access departs and whose departure is not its template's, found by a pass that
    // counts for the access (a holder's or transit value's, or for an activation also an operand's), recorded; the location's first entry
    // only the drop tier found that it departs is kept for Departures.
    private bool Depart(MirLocation at, int root, ReadOnlySpan<MirStep> steps, AccessKind kind)
    {
        var scope = this.Source(at).Scope;
        for (var entry = this.ownRoots[root].First; entry != None; entry = this.ownTags[entry - this.loanCount].Next)
        {
            this.Work++;
            var tag = this.ownTags[entry - this.loanCount];
            var counted = tag.Via != None && (tag.Via != Taken || kind is AccessKind.Activate or AccessKind.ActivateInPlace);
            var dropped = !counted && tag.Dropped != None && this.dropDeparture.Entry == None;
            if ((counted || dropped) && this.Departs(steps, kind, entry) && !this.Covered(this.body.Common(scope, this.IssueScope(entry)), -1, -1))
            {
                if (counted)
                {
                    this.Departure(entry, tag.Via, at);
                    return true;
                }

                this.dropDeparture = (entry, tag.Dropped);
            }
        }

        return false;
    }

    // Whether a view whose only producer input is its own-step entries may depart one, the pre-pass's producer term (D5, R4-M141): every
    // use of the view, reached or not, as KindOf classifies it with no liveness, a destruction Accesses leaves unclassified on a root the
    // entries borrow (its located Unsupported_Kd) or an access that departs an entry on its root. Each root's entries are listed under the
    // stamp Listed, which no check uses, so a use tests only its root's.
    private bool MayDepart()
    {
        this.RestampOwnSteps(this.body.Locals.Length + this.body.Statics.Length);
        for (var entry = this.loanCount; entry < this.loanCount + this.addressCount; entry++)
        {
            if (this.loans[entry].OwnStep)
            {
                ref var root = ref this.ownRoots[this.loans[entry].Root];
                this.ownTags[entry - this.loanCount] = (Listed, None, root.Stamp == Listed ? root.First : None, None);
                root = (Listed, entry);
            }
        }

        var visitor = new Departing(this);
        for (var block = 0; block < this.body.Blocks.Length; block++)
        {
            for (var i = 0; i <= this.body.Blocks[block].Count; i++)
            {
                this.Work++;
                visitor.At(new(block, i));
                MirUses.ForEach(cases, new(block, i), ref visitor);
                if (visitor.Found)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether one use may depart an own-step entry listed on its root (MayDepart): an unclassified destruction, or an access other than a read
    // or a shared borrow that departs one (Departs; a reborrow is its plain use, as no Loan exists).
    private bool MayDepart(MirLocation at, MirPlace place, MirAccess access, MirRefKind kind)
    {
        var root = this.Key(place.Root);
        if (this.ownRoots[root].Stamp != Listed || this.KindOf(at, place, access, kind) is not { } known || known is AccessKind.Read or AccessKind.SharedBorrow)
        {
            return false;
        }

        if (known == AccessKind.Unclassified)
        {
            return true;
        }

        var count = this.placeSteps.Walk(this.body, place, reborrow: false, ref this.accessSteps);
        for (var entry = this.ownRoots[root].First; entry != None; entry = this.ownTags[entry - this.loanCount].Next)
        {
            this.Work++;
            if (this.Departs(this.accessSteps.AsSpan(0, count), known, entry))
            {
                return true;
            }
        }

        return false;
    }

    // Whether an access with path S ends the stay of an entry's owning value with path O: S prefixes O, and a Move, write or destruction
    // passes no borrow or raw Deref of O past S (moving or replacing a reference leaves its referent in its holder); an exclusive borrow or
    // an activation departs across any steps.
    private bool Departs(ReadOnlySpan<MirStep> steps, AccessKind kind, int entry)
    {
        ref readonly var owner = ref this.loans[entry];
        if (steps.Length > owner.Count)
        {
            return false;
        }

        for (var i = 0; i < steps.Length; i++)
        {
            if (!Meets(steps[i], this.loanSteps[owner.Start + i]))
            {
                return false;
            }
        }

        if (kind is AccessKind.UniqueBorrow or AccessKind.Activate or AccessKind.ActivateInPlace)
        {
            return true;
        }

        for (var i = steps.Length; i < owner.Count; i++)
        {
            if (this.loanSteps[owner.Start + i].Class is MirStepClass.Shared or MirStepClass.Uniq or MirStepClass.Raw)
            {
                return false;
            }
        }

        return true;
    }

    // The departure test's uses of one location (MayDepart): each Place a use reports, until one may depart.
    private struct Departing(MirBorrows owner) : IMirUses
    {
        private MirLocation at;

        internal bool Found { get; private set; }

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind) => this.Found = this.Found || owner.MayDepart(this.at, place, access, kind);

        internal void At(MirLocation at) => this.at = at;
    }
}
