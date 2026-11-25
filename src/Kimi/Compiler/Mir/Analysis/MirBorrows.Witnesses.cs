// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problem codes and witness sites (A3 5's A6 table, 6.4): a conflict's code (a Loan found in its reservation holder is a reservation
// conflict, else the check's), a location's subject and source (its statement's or terminator's), a Loan's anchor (its issuing location's
// subject, D14), a holder's declaration, a witness holder's next use in its chain, E's store of what a check found in E (EStore, along
// the edges the forward solve carried, MirDataflow.Carries), a Loan problem's subject and facts built from them
// (Facts), and a problem's subject with, inside an inline default copy, its Evaluated fact (Evaluated), which Facts ends with and the
// own-step Warning's record is alone (G673); MirBorrows.Reports records them.
// Must not record a problem or decide which problem a conflict becomes (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    private const int Unstored = -3; // StoreIn: no location of the range stores or ends the element.

    private readonly List<int> searching = []; // EStore's blocks whose predecessors are still to read.
    private ProblemFact[] facts = new ProblemFact[5]; // The facts Facts builds for one problem, then its causes (MirBorrows.Causes' CauseFacts).
    private bool[] searched = []; // By block: whether EStore read it.

    // A conflict's code (A3 5's A6 table, row 1 for every access): a Loan found in its reservation holder, as the access sees it (a call's
    // effect rows see a reservation it activates unique, W6, R4-M92), is a reservation conflict, else the check's code.
    private DiagnosticCode Code(in Candidate candidate, AccessKind kind, DiagnosticCode code)
        => this.Reserved(candidate, kind) ? DiagnosticCode.CallReservationConflict_Kd : code;

    // A location's subject: its statement's or terminator's.
    private SourceRef Subject(MirLocation at) => this.body.Subject(this.Source(at));

    // A Loan's anchor (D14): its issuing location's subject.
    private SourceRef Site(int loan) => this.body.Subject(this.Source(this.loans[loan].Location));

    // A holder's declaration: its local's (MirBorrows.Holders' HolderLocal).
    private SourceRef Declaration(int holder) => this.body.Subject(this.body.Locals[this.HolderLocal(holder)].Source);

    // A location's source: its statement's or terminator's.
    private SourceInfo Source(MirLocation at) => this.Source(this.Location(at.Block, at.Index));

    // The source of a location by its index (Location): a statement's, or after every statement a block's terminator's.
    private SourceInfo Source(int location)
        => location < this.body.Statements.Length ? this.body.Statements[location].Source
            : this.body.Blocks[location - this.body.Statements.Length].Terminator.Source;

    // PLAN G651 U2b R5 (D-C4): a location's node site, opened by each of A5's location loops, the innermost site of a refused read there.
    private (Binding.AfterRunOwner Kind, SourceRef Subject) OpenAt(int location) => types.OpenSite(this.Source(location).Syntax);

    // The witness holder's first use at or after a location in its chain (A3 6.4), or null where it is used only in a later chain.
    private SourceRef? NextUse(int slot, MirLocation from)
    {
        var (block, first) = (from.Block, from.Index);
        for (var visited = 0; visited < this.body.Blocks.Length; visited++)
        {
            ref readonly var data = ref this.body.Blocks[block];
            for (var i = first; i <= data.Count; i++)
            {
                var (start, count) = this.touched[this.Location(block, i)];
                for (var t = start; t < start + count; t++)
                {
                    if (this.touches[t].Slot == slot && (this.touches[t].Flags & Uses) != 0)
                    {
                        return this.Subject(new(block, i));
                    }
                }
            }

            var terminator = data.Terminator;
            var next = MirUses.Successors(terminator) == 1 && terminator.CheckTarget < 0 ? MirUses.Successor(this.body, terminator, 0) : -1;
            if (next < 0 || next == this.chainFirst || flow.Head(next) != flow.Head(block))
            {
                return null;
            }

            (block, first) = (next, 0);
        }

        return null;
    }

    // A Loan problem's subject and facts at a location (A3 6.4): the anchor of the Loan, guard candidate or destruction; its witness, a
    // holder's next use, from the location itself for a reservation's check set, or, where that lies in a later chain, the holder's
    // declaration, the value retaining the Loan, as a Store (none for local 0, whose next use is the caller's, R4-M156); the location for a
    // value it takes, a transit value's or E's store, or none (None: a destruction's own hold, which no value carries); the reservation's
    // anchor; the static Field an effect met (R4-M92); and last its subject and, inside an inline default copy, the default's operation
    // (Evaluated).
    private (SourceRef Subject, int Count) Facts(int found, int via, MirLocation at, int reservation, ProblemFact? effect)
    {
        var (operation, from) = (this.Subject(at), reservation >= 0 ? at : at with { Index = at.Index + 1 });
        SourceRef? witness = via == None ? null : operation;
        var kind = via > Escaped || via == Taken ? ProblemFactKind.Use : ProblemFactKind.Store;
        if (via > Escaped)
        {
            witness = this.NextUse(via, from);
            if (witness is null && this.HolderLocal(via) != 0)
            {
                (witness, kind) = (this.Declaration(via), ProblemFactKind.Store);
            }
        }
        else if (via == Escaped)
        {
            var store = this.EStore(found, at);
            if (store < 0)
            {
                return (operation, 0);
            }

            witness = this.body.Subject(this.Source(store));
        }

        var count = 0;
        this.facts[count++] = new(ProblemFactKind.Anchor, this.Anchor(found, at));
        if (witness is { } site)
        {
            this.facts[count++] = new(kind, site);
        }

        if (reservation >= 0)
        {
            this.facts[count++] = new(ProblemFactKind.Reservation, this.Site(reservation));
        }

        if (effect is { } met)
        {
            this.facts[count++] = met;
        }

        return this.Evaluated(at, count);
    }

    // What a check found's anchor (D14): a Loan's issuing location's subject, a guard marker's candidate's declaration, else (None: a
    // destruction's own hold, R3) the location's.
    private SourceRef Anchor(int found, MirLocation at)
        => found >= this.loanBase ? this.Site(found - this.loanBase) : found >= Markers ? this.body.Subject(this.body.Locals[found - Markers].Source) : this.Subject(at);

    // E's store of what a check found in E (DIAGNOSTICS' `Store`): the nearest location, backward from the state before the access along
    // the edges the forward solve carries a state on (MirDataflow.Carries), whose entries put the element into E (MirBorrows.Flow's
    // eStores), never past one that ends it (a Loan's ending; a marker none), so its store carries it on a path to the access, a loop's
    // back edge to the access's own location included; -1 for none, a broken contract (E's state changes only through entries, its entry
    // state holds no Loan or guard marker, and the transfer is monotone in E's stores, MirBorrows.Flow's eStores). Carries reads the
    // selected view's CaseSwitch sides, and the solve ran under the run's view; the effect rule's views loop selects each view of a run
    // that no view decides while it records (MirBorrows.Effects), so the search selects the run's view and the recording one after it.
    private int EStore(int found, MirLocation at)
    {
        var recording = BitOperations.PopCount(this.views) == 1 ? BitOperations.TrailingZeroCount(this.views) : this.view;
        if (recording != this.view)
        {
            cases.Select(this.body, this.view);
        }

        var store = this.Nearest(found, at);
        if (recording != this.view)
        {
            cases.Select(this.body, recording);
        }

        return store;
    }

    // EStore's search under the run's view.
    private int Nearest(int found, MirLocation at)
    {
        var blocks = this.body.Blocks.Length;
        MirDataflow.Grow(ref this.searched, blocks);
        Array.Clear(this.searched, 0, blocks);
        this.searching.Clear();
        var store = this.StoreIn(found, at.Block, at.Index - 1, 0);
        if (store != Unstored)
        {
            return store;
        }

        this.searching.Add(at.Block);
        for (var q = 0; q < this.searching.Count; q++)
        {
            var into = this.searching[q];
            foreach (var from in flow.Predecessors(into))
            {
                this.Work++;
                if (this.searched[from] || !flow.Carries(from, into))
                {
                    continue;
                }

                this.searched[from] = true;
                store = this.StoreIn(found, from, this.body.Blocks[from].Count, from == at.Block ? at.Index : 0);
                if (store >= 0)
                {
                    return store;
                }

                if (store == Unstored && from != at.Block)
                {
                    this.searching.Add(from);
                }
            }
        }

        return -1;
    }

    // The nearest location of a block, from index `last` down to `first`, whose entries put an element into E, or -1 (stopped) where a
    // location ends it first: after its own entries (a destruction in place, E8) or, at a terminator, before them (R-a); else Unstored.
    private int StoreIn(int found, int block, int last, int first)
    {
        for (var i = last; i >= first; i--)
        {
            var location = this.Location(block, i);
            this.Work++;
            if (this.sets.Contains(this.ended[location], found))
            {
                return -1;
            }

            if (this.sets.Contains(this.eStores[location].Sure, found) || this.sets.Contains(this.eStores[location].Unsure, found))
            {
                return location;
            }

            if (i == this.body.Blocks[block].Count && this.sets.Contains(this.inspected[block], found))
            {
                return -1;
            }
        }

        return Unstored;
    }

    // A problem's subject at a location, after the count facts built (D13): inside an inline default copy the outermost omitting call, with
    // the default's operation as an Evaluated fact; else the location's subject.
    private (SourceRef Subject, int Count) Evaluated(MirLocation at, int count)
    {
        var operation = this.Subject(at);
        var call = this.body.OmittingCall(this.Source(at).Scope);
        if (call is not null)
        {
            this.facts[count++] = new(ProblemFactKind.Evaluated, operation);
        }

        return (call ?? operation, count);
    }
}
