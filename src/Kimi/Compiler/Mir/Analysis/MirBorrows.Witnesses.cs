// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problem codes and witness sites (A3 5's A6 table, 6.4): a conflict's code (a Loan found in its reservation holder is a reservation
// conflict, else the check's), a location's subject and source (its statement's or terminator's), a Loan's anchor (its issuing location's
// subject, D14), a holder's declaration, a witness holder's next use in its chain, a Loan problem's subject and facts built from them
// (Facts), and a problem's subject with, inside an inline default copy, its Evaluated fact (Evaluated), which Facts ends with and the
// own-step Warning's record is alone (G673); MirBorrows.Reports records them.
// Must not record a problem or decide which problem a conflict becomes (MirBorrows.Reports).
internal sealed partial class MirBorrows
{
    private ProblemFact[] facts = new ProblemFact[5]; // The facts Facts builds for one problem, then its causes (MirBorrows.Causes' CauseFacts).

    // A conflict's code (A3 5's A6 table, row 1 for every access): a Loan found in its reservation holder, as the access sees it (a call's
    // effect rows see a reservation it activates unique, W6, R4-M92), is a reservation conflict, else the check's code.
    private DiagnosticCode Code(in Candidate candidate, AccessKind kind, DiagnosticCode code)
        => this.Reserved(candidate, kind) ? DiagnosticCode.CallReservationConflict_Kd : code;

    // A location's subject: its statement's or terminator's.
    private SourceRef Subject(MirLocation at) => this.body.Subject(this.Source(at));

    // A Loan's anchor (D14): its issuing location's subject.
    private SourceRef Site(int loan) => this.body.Subject(this.Source(this.loans[loan].Location));

    // A holder's declaration: its local's, a drop slot's its holder's (MirBorrows.Holders' HolderLocal).
    private SourceRef Declaration(int holder) => this.body.Subject(this.body.Locals[this.HolderLocal(holder)].Source);

    // A location's source: its statement's or terminator's.
    private SourceInfo Source(MirLocation at) => this.Source(this.Location(at.Block, at.Index));

    // The source of a location by its index (Location): a statement's, or after every statement a block's terminator's.
    private SourceInfo Source(int location)
        => location < this.body.Statements.Length ? this.body.Statements[location].Source
            : this.body.Blocks[location - this.body.Statements.Length].Terminator.Source;

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
    private (SourceRef Subject, int Count) Facts(SourceRef anchor, int via, MirLocation at, int reservation, ProblemFact? effect)
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

        var count = 0;
        this.facts[count++] = new(ProblemFactKind.Anchor, anchor);
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
