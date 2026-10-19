// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problems (A3 5's A6 table, 6.4; DECISIONS R4-M57, R5-M23): a conflict's code (a Loan found in its reservation holder is a reservation
// conflict) and each problem recorded through MirReport under its requirement with its facts: the Loan's or candidate's anchor, its witness
// (a holder's next use in its chain, the location for a value an access takes, a transit value's or E's store), an activation's
// reservation and, inside an inline default copy, its call (R4-M69); a located Unsupported_Kd where the stratum classifies nothing, at the
// source of the pending slot whose default taint every set holding a conflict's Loan carries (R4-M70, until A5-F9), and in a held view at
// every Loan problem's access (P13's interim, R4-M73, until the guard reads each part). Every Loan-problem producer reports through
// Report(code, anchor, via, at, reservation), so the hold is one rule there.
// Must not decide a conflict (MirBorrows.Checks), derive a requirement from a code, or apply a derived-problem rule (MirReport's).
internal sealed partial class MirBorrows
{
    private readonly MirFact[] facts = new MirFact[4];

    // A conflict's code (A3 5's A6 table): a Loan found in its reservation holder is a reservation conflict, else the check's code.
    private DiagnosticCode Code(in Candidate candidate, DiagnosticCode code) => this.Reserved(candidate) ? DiagnosticCode.CallReservationConflict_Kd : code;

    // A conflicting candidate's problem: where only sets with a default taint hold it in the check set, A5's located Unsupported_Kd at the
    // source of the first taint's pending slot, the omitting call (R4-M70, never a Language problem until A5-F9 checks defaults); else its
    // Loan's anchor, and its witness among the sets without a taint (E13 (6)): a holder it was found live in (for an activation, other than
    // its own), else the location, whose operation uses the value that carries it.
    private void Report(DiagnosticCode code, int candidate, int taking, MirLocation at, int reservation)
    {
        ref readonly var found = ref this.candidates[candidate];
        var clean = found.Clean;
        if (!clean.Takes(taking))
        {
            this.Unsupported(this.body.Subject(this.body.Locals[found.Taint - Markers].Source));
            return;
        }

        var live = taking <= Activating ? (clean.Flags & Based) != 0 && Activation(clean.Via) != taking : (clean.Flags & (Based | Stored)) != 0;
        this.Report(code, this.loans[found.Loan].Site, live ? clean.Via : Taken, at, reservation);
    }

    // A Loan problem at a location with its facts (A3 6.4): the anchor of the Loan or guard candidate; its witness, a holder's next use (none
    // when that lies in a later chain), from the location itself for a reservation's check set, the location for a value it takes, or a
    // transit value's or E's store; the reservation's anchor; and the call of the inline default copy the location lies in (R4-M69). In a
    // held view (R4-M73) it is the located Unsupported_Kd at its access, except inside an inline default copy, whose Language problem is its
    // template's and dropped (R4-M69).
    private void Report(DiagnosticCode code, SourceRef anchor, int via, MirLocation at, int reservation)
    {
        var inlined = this.body.Inlining(this.Source(at).Scope);
        if (this.held && inlined < 0)
        {
            this.Unsupported(at);
            return;
        }

        var (subject, from) = (this.Subject(at), reservation >= 0 ? at : at with { Index = at.Index + 1 });
        var witness = via > Escaped ? this.NextUse(via, from) : subject;
        var count = 0;
        this.facts[count++] = new(MirFactKind.Anchor, anchor);
        if (witness is { } site)
        {
            this.facts[count++] = new(via > Escaped || via == Taken ? MirFactKind.Use : MirFactKind.Store, site);
        }

        if (reservation >= 0)
        {
            this.facts[count++] = new(MirFactKind.Reservation, this.loans[reservation].Site);
        }

        if (inlined >= 0)
        {
            this.facts[count++] = new(MirFactKind.Inlined, this.body.Scopes[inlined].Source);
        }

        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict); // The four Loan codes' requirement.
        this.program.Problems.Add(new(code, requirement, subject, this.body.Ordinal, this.views), this.facts.AsSpan(0, count));
    }

    // A located Unsupported_Kd at a location whose access or call the stratum does not classify; a limit record, which depends on the
    // concrete instantiation, so an inline default copy's stays (SPEC 23.1.1, R4-M69).
    private void Unsupported(MirLocation at) => this.Unsupported(this.Subject(at));

    // A located Unsupported_Kd at a subject, with no fact.
    private void Unsupported(SourceRef subject)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
        this.program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, requirement, subject, this.body.Ordinal, this.views));
    }

    // A location's subject: its statement's or terminator's.
    private SourceRef Subject(MirLocation at) => this.body.Subject(this.Source(at));

    // A location's source: its statement's or terminator's.
    private SourceInfo Source(MirLocation at)
    {
        ref readonly var data = ref this.body.Blocks[at.Block];
        return at.Index < data.Count ? this.body.Statements[data.Start + at.Index].Source : data.Terminator.Source;
    }

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
}
