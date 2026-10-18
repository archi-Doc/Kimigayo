// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problems (A3 5's A6 table, 6.4; DECISIONS R4-M57, R5-M23): a conflict's code (a Loan found in its reservation holder is a reservation
// conflict), err(ended) at a guard candidate's EndBorrow (E7), and each problem recorded through MirReport under its requirement with its
// facts: the Loan's or candidate's anchor, its witness (a holder's next use in its chain, the location for a value an access takes, a
// transit value's or E's store) and an activation's reservation; a located Unsupported_Kd where the stratum classifies nothing.
// Must not decide a conflict (MirBorrows.Checks), derive a requirement from a code, or apply a derived-problem rule (MirReport's).
internal sealed partial class MirBorrows
{
    private readonly MirFact[] facts = new MirFact[3];

    // A conflict's code (A3 5's A6 table): a Loan found in its reservation holder is a reservation conflict, else the check's code.
    private DiagnosticCode Code(in Candidate candidate, DiagnosticCode code) => this.Reserved(candidate) ? DiagnosticCode.CallReservationConflict_Kd : code;

    // err(ended) (A3 E7, SPEC 14.8.3): at a guard candidate's EndBorrow its guard-local Origin ends, so a value live after it that carries its
    // γ (CS(ℓ), transit included) is a borrow that escapes the guard: ComparisonLoanConflict_Kd at the cleanup (DIAGNOSTICS, escaped
    // borrows), anchored at the candidate.
    private void Ended(MirLocation at, int candidate)
    {
        var mark = this.guardBase + candidate;
        if (this.markSeen[mark] == this.stamp)
        {
            var anchor = this.body.Subject(this.body.Locals[candidate].Source);
            this.Report(DiagnosticCode.ComparisonLoanConflict_Kd, anchor, this.markVia[mark], at, -1);
        }
    }

    // A conflicting candidate's problem: its Loan's anchor, and its witness for the check set (E13 (6)): a holder it was found live in (for
    // an activation, other than its own), else the location, whose operation uses the value that carries it.
    private void Report(DiagnosticCode code, int candidate, int taking, MirLocation at, int reservation)
    {
        ref readonly var found = ref this.candidates[candidate];
        var live = taking <= Activating ? (found.Flags & Based) != 0 && Activation(found.Via) != taking : (found.Flags & (Based | Stored)) != 0;
        this.Report(code, this.loans[found.Loan].Site, live ? found.Via : Taken, at, reservation);
    }

    // A Loan problem at a location with its facts (A3 6.4): the anchor of the Loan or guard candidate; its witness, a holder's next use (none
    // when that lies in a later chain), from the location itself for a reservation's check set, the location for a value it takes, or a
    // transit value's or E's store; and the reservation's anchor.
    private void Report(DiagnosticCode code, SourceRef anchor, int via, MirLocation at, int reservation)
    {
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

        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict); // The four Loan codes' requirement.
        this.program.Problems.Add(new(code, requirement, subject, this.views), this.facts.AsSpan(0, count));
    }

    // A located Unsupported_Kd at a location whose access or call the stratum does not classify.
    private void Unsupported(MirLocation at)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
        this.program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, requirement, this.Subject(at), this.views));
    }

    // A location's subject: its statement's or terminator's.
    private SourceRef Subject(MirLocation at)
    {
        ref readonly var data = ref this.body.Blocks[at.Block];
        return this.body.Subject(at.Index < data.Count ? this.body.Statements[data.Start + at.Index].Source : data.Terminator.Source);
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
