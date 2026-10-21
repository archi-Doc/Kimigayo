// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problems (A3 5's A6 table, 6.4; DECISIONS R4-M57, R5-M23, R4-M92): each problem recorded through MirReport under its requirement
// with its code and facts, whose codes and sites MirBorrows.Witnesses gives: the Loan's or candidate's anchor, its witness (a holder's next
// use in its chain, the location for a value an access takes, a transit value's or E's store; none for a destruction's own hold, R3), an
// activation's reservation, the static Field an effect met and, inside an inline default copy, its call (R4-M69); a located Unsupported_Kd
// where the stratum classifies nothing, at the source of the pending slot whose default taint every set holding a conflict's Loan carries
// (R4-M70, until A5-F9), and in a held view at every Loan problem's access (P13's interim, R4-M73, until the guard reads each part). Every
// Loan-problem producer reports through Report(code, anchor, via, at, reservation, effect), so the hold is one rule there.
// Must not decide a conflict (MirBorrows.Checks), derive a requirement from a code, or apply a derived-problem rule (MirReport's).
internal sealed partial class MirBorrows
{
    private readonly MirFact[] facts = new MirFact[5];

    // A conflicting candidate's problem: where only sets with a default taint hold it in the check set, A5's located Unsupported_Kd at the
    // source of the first taint's pending slot, the omitting call (R4-M70, never a Language problem until A5-F9 checks defaults); else its
    // Loan's anchor, and its witness among the sets without a taint (E13 (6)): a holder it was found live in (for an activation, other than
    // its own), else the location, whose operation uses the value that carries it; and the static Field an effect met (R4-M92).
    private void Report(DiagnosticCode code, int candidate, int taking, MirLocation at, int reservation, MirFact? effect)
    {
        ref readonly var found = ref this.candidates[candidate];
        var clean = found.Clean;
        if (!clean.Takes(taking))
        {
            this.Unsupported(this.body.Subject(this.body.Locals[found.Taint - Markers].Source));
            return;
        }

        var live = taking <= Activating ? (clean.Flags & Based) != 0 && Activation(clean.Via) != taking : (clean.Flags & (Based | Stored)) != 0;
        this.Report(code, this.loans[found.Loan].Site, live ? clean.Via : Taken, at, reservation, effect);
    }

    // A Loan problem at a location with its facts (A3 6.4): the anchor of the Loan, guard candidate or destruction; its witness, a holder's
    // next use (none when that lies in a later chain), from the location itself for a reservation's check set, the location for a value it
    // takes, a transit value's or E's store, or none (None: a destruction's own hold, which no value carries); the reservation's anchor; the
    // static Field an effect met (R4-M92); and the call of the inline default copy the location lies in (R4-M69). In a held view (R4-M73) it
    // is the located Unsupported_Kd at its access, except inside an inline default copy, whose Language problem is its template's and
    // dropped (R4-M69).
    private void Report(DiagnosticCode code, SourceRef anchor, int via, MirLocation at, int reservation, MirFact? effect)
    {
        var inlined = this.body.Inlining(this.Source(at).Scope);
        if (this.held && inlined < 0)
        {
            this.Unsupported(at);
            return;
        }

        var (subject, from) = (this.Subject(at), reservation >= 0 ? at : at with { Index = at.Index + 1 });
        var witness = via > Escaped ? this.NextUse(via, from) : via == None ? null : subject;
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

        if (effect is { } met)
        {
            this.facts[count++] = met;
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
}
