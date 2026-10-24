// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problems (A3 5's A6 table, 6.4; DECISIONS R4-M57, R5-M23, R4-M92, R4-M97, R4-M98): each problem recorded through MirReport under
// its requirement with its code and facts, which MirBorrows.Witnesses builds: the Loan's or candidate's anchor, its witness (a holder's
// next use in its chain, the location for a value an access takes, a transit value's or E's store; none for a destruction's own
// hold, R3), an activation's reservation, the static Field an effect met and, inside an inline default copy, the default's operation
// (Evaluated) with the outermost omitting call as subject (D13); a located Unsupported_Kd where the stratum classifies nothing, and in a
// held view at every Loan problem's subject (P13's interim, R4-M73, until the guard reads each part). Every Loan-problem producer records
// through Record(code, requirement, subject, facts), so the hold is one rule there: a Loan conflict through Report(code, anchor, via, at,
// reservation, effect), A5-F9's template rules (MirBorrows.Defaults) with their parameter.
// Must not decide a conflict (MirBorrows.Checks), derive a requirement from a code, or apply a derived-problem rule (MirReport's).
internal sealed partial class MirBorrows
{
    // A conflicting candidate's problem: its Loan's anchor, and its witness (E13 (6)): a holder it was found live in (for an activation, other
    // than its own), else the location, whose operation uses the value that carries it; and the static Field an effect met (R4-M92).
    private void Report(DiagnosticCode code, int candidate, int taking, MirLocation at, int reservation, MirFact? effect)
    {
        ref readonly var found = ref this.candidates[candidate];
        var tags = found.Tags;
        var live = taking <= Activating ? (tags.Flags & Based) != 0 && Activation(tags.Via) != taking : (tags.Flags & (Based | Stored)) != 0;
        this.Report(code, this.Site(found.Loan), live ? tags.Via : Taken, at, reservation, effect);
    }

    // A Loan problem at a location (A3 6.4): its subject and facts as MirBorrows.Witnesses builds them, recorded under the four Loan codes'
    // requirement.
    private void Report(DiagnosticCode code, SourceRef anchor, int via, MirLocation at, int reservation, MirFact? effect)
    {
        var (subject, count) = this.Facts(anchor, via, at, reservation, effect);
        this.Record(code, OwnershipRequirement.ComparisonLoanConflict, subject, this.facts.AsSpan(0, count));
    }

    // A problem with its facts (D7, R4-M98): in a held view (R4-M73) the located Unsupported_Kd at its subject instead.
    private void Record(DiagnosticCode code, OwnershipRequirement requirement, SourceRef subject, ReadOnlySpan<MirFact> facts)
    {
        if (this.held)
        {
            this.Unsupported(subject);
            return;
        }

        this.program.Problems.Add(new(code, DiagnosticRequirement.Ownership(requirement), subject, this.body.Ordinal, this.views), facts);
    }

    // A located Unsupported_Kd at a location whose access or call the stratum does not classify; a limit record, which depends on the
    // concrete instantiation, so an inline default copy's stays at its operation (SPEC 23.1.1, R4-M69, R4-M71).
    private void Unsupported(MirLocation at) => this.Unsupported(this.Subject(at));

    // A located Unsupported_Kd at a subject, with no fact.
    private void Unsupported(SourceRef subject)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
        this.program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, requirement, subject, this.body.Ordinal, this.views));
    }
}
