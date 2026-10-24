// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5's own-step departures (SPEC 5.2.1 condition 3; DECISIONS R4-M116 OQ-1, design B′ §5 on B §5.8): at a location of the checks replay,
// an access that may end an owning value's stay in its holder while a value carrying a pointer formed through that own step may still be
// used. An own-step entry (MirBorrows.Addresses) found at the check is tagged by MirBorrows.Candidates where a pass counts it (Based or
// Stored: live after the access, or stored there into a holder live after it or into E; Valued: an operand's value, which counts for an
// activation, whose call uses its operands after taking the owner) and listed on its root, so an access scans only its root's. An access
// with path S departs an entry with owning path O when S prefixes O step by step (15.6.2, MirBorrows.Checks' Meets) and it is a Move (a
// Take of a raw Place included), a write or a destruction of any class with no borrow or raw Deref of O past S, or an exclusive borrow or
// a call's activation of a reservation across any steps. A departure whose operation and formation lie in one inline default copy is its
// template's (Covered). One record per location: every record at a location has its subject, which MirReport merges.
// Must not issue, end or conflict a Loan (an entry is never a candidate, D19), or record through the hold (MirBorrows.Reports' Departure
// bypasses it: a Warning rejects nothing, PQ-8).
internal sealed partial class MirBorrows
{
    // By address (entry − Loans): the check that found it, the holder its Based or Stored pass first found it in (None: only an operand's
    // value), and the next entry found on its root at that check.
    private (int Stamp, int Via, int Next)[] ownTags = [];
    private (int Stamp, int First)[] ownRoots = []; // By root key: the first own-step entry found on it at that check.
    private int ownEntries; // The own-step entries of the view.

    // Restarts the tags for the view, where it has own-step entries.
    private void RestampOwnSteps(int keys)
    {
        MirDataflow.Grow(ref this.ownTags, this.addressCount);
        MirDataflow.Grow(ref this.ownRoots, keys);
        this.ownTags.AsSpan(0, this.addressCount).Clear();
        this.ownRoots.AsSpan(0, keys).Clear();
        this.Work += this.addressCount + keys;
    }

    // An own-step entry a pass found in a holder (-1: a transit value): tagged at the check in the Based, Stored and Valued passes, listed
    // on its root once.
    private void Seen(int index, int via)
    {
        if (this.pass is not (Based or Stored or Valued))
        {
            return;
        }

        ref var tag = ref this.ownTags[index - this.loanCount];
        if (tag.Stamp != this.stamp)
        {
            ref var root = ref this.ownRoots[this.loans[index].Root];
            tag = (this.stamp, None, root.Stamp == this.stamp ? root.First : None);
            root = (this.stamp, index);
        }

        if (tag.Via == None && this.pass is Based or Stored)
        {
            tag.Via = via;
        }
    }

    // The location's departures: its listed accesses but reads and shared borrows (the effect access follows them), then the reservations
    // it activates, up to the first recorded.
    private void Departures(MirLocation at, int accessed)
    {
        if (this.ownEntries == 0)
        {
            return;
        }

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
    }

    // The first own-step entry found on a root that an access departs and whose departure is not its template's, recorded.
    private bool Depart(MirLocation at, int root, ReadOnlySpan<MirStep> steps, AccessKind kind)
    {
        var scope = this.Source(at).Scope;
        for (var entry = this.ownRoots[root].First; entry != None; entry = this.ownTags[entry - this.loanCount].Next)
        {
            this.Work++;
            var tag = this.ownTags[entry - this.loanCount];
            if ((tag.Via != None || kind is AccessKind.Activate or AccessKind.ActivateInPlace) && this.Departs(steps, kind, entry) &&
                !this.Covered(this.body.Common(scope, this.IssueScope(entry)), -1, -1))
            {
                this.Departure(entry, tag.Via == None ? Taken : tag.Via, at);
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
}
