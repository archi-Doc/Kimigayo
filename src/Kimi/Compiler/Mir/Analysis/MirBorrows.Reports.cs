// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

// A5's problems (A3 5's A6 table, 6.4; DECISIONS R4-M57, R5-M23, R4-M92, R4-M97, R4-M98, R4-M148): each problem recorded through MirReport under
// its requirement with its code and facts, which MirBorrows.Witnesses builds: the Loan's or candidate's anchor, its witness (a holder's
// next use in its chain, the location for a value an access takes, a transit value's or E's store; none for a destruction's own
// hold, R3), an activation's reservation, the static Field an effect met and, inside an inline default copy, the default's operation
// (Evaluated) with the outermost omitting call as subject (D13); a located Unsupported_Kd where the stratum classifies nothing, and in a
// view PD13 holds (MirBorrows.CallRows' held; R4-M126 (iii), R4-M148) at every rejection's subject; OwnershipDepthLimit_Kd where a check
// rests on a part past a Type walk's bound alone (R4-M153); InternalInvariant_Kd at a Value call
// whose declared signature does not number its operands (G136). Every rejection but a located limit record is recorded through
// Record(code, requirement, subject, facts), so the hold is one rule there: a Loan conflict through Report(code, anchor, via, at,
// reservation, effect), A5-F9's template rules (MirBorrows.Defaults) with their parameter. The own-step Warning (OwnStepDeparture_Kd,
// MirBorrows.OwnSteps), which is no Loan problem and rejects nothing, is recorded through Departure under its own requirement, never
// through the hold (R4-M116 PQ-8).
// Must not decide a conflict (MirBorrows.Checks), derive a requirement from a code, or apply a derived-problem rule (MirReport's).
internal sealed partial class MirBorrows
{
    // A conflicting candidate's problem: its Loan's anchor, and its witness (E13 (6)): a holder it was found live in (for an activation, other
    // than its own), else the location, whose operation uses the value that carries it; and the static Field an effect met (R4-M92). One
    // the check set holds only through a holder live only for a past destruction rests on the destruction walk's bound: the depth record at
    // that holder's first past destruction instead (R4-M153).
    private void Report(DiagnosticCode code, int candidate, int taking, MirLocation at, int reservation, MirFact? effect)
    {
        ref readonly var found = ref this.candidates[candidate];
        var tags = found.Tags;
        if (tags.Rests(taking))
        {
            this.PastLimit(tags.PastVia);
            return;
        }

        var live = taking <= Activating ? (tags.Flags & Based) != 0 && Activation(tags.Via) != taking : (tags.Flags & (Based | Stored)) != 0;
        this.Report(code, this.Site(found.Loan), live ? tags.Via : Taken, at, reservation, effect);
    }

    // A Loan problem at a location (A3 6.4): its subject and facts as MirBorrows.Witnesses builds them, recorded under the four Loan codes'
    // requirement.
    private void Report(DiagnosticCode code, SourceRef anchor, int via, MirLocation at, int reservation, MirFact? effect)
    {
        var (subject, count) = this.Facts(anchor, via, at, reservation, effect);
        this.Record(code, OwnershipRequirement.ComparisonLoanConflict, subject, this.facts.AsSpan(0, this.CauseFacts(count)));
    }

    // A rejection with its facts (D7, R4-M98): in a view PD13 holds (R4-M126 (iii), R4-M148) the located Unsupported_Kd at its subject
    // instead.
    private void Record(DiagnosticCode code, OwnershipRequirement requirement, SourceRef subject, ReadOnlySpan<MirFact> facts)
    {
        if (this.held)
        {
            this.Unsupported(subject);
            return;
        }

        this.AddProblem(code, requirement, subject, facts);
    }

    // The own-step Warning at a location (SPEC 5.2.1 condition 3, MirBorrows.OwnSteps): its subject and facts as MirBorrows.Witnesses builds
    // them for the entry's first formation and the witness, under its own requirement, never through the hold (PQ-8).
    private void Departure(int entry, int via, MirLocation at)
    {
        var (subject, count) = this.Facts(this.Site(entry), via, at, -1, null);
        this.AddProblem(DiagnosticCode.OwnStepDeparture_Kd, OwnershipRequirement.OwnStepDeparture, subject, this.facts.AsSpan(0, count));
    }

    // A problem with its facts in the body's views.
    private void AddProblem(DiagnosticCode code, OwnershipRequirement requirement, SourceRef subject, ReadOnlySpan<MirFact> facts)
        => this.program.Problems.Add(new(code, DiagnosticRequirement.Ownership(requirement), subject, this.body.Ordinal, this.views), facts);

    // The depth record of a check that rests only on a holder live for a destruction past the destruction walk's bound (R4-M153): at that
    // holder's first past destruction.
    private void PastLimit(int holder) => this.DepthLimit(this.body.Subject(this.Source(this.pastAt[holder])), MirTypes.Bound);

    // A broken contract at a location (G136, R4-M148: a Value call's declared signature does not number its operands): InternalInvariant_Kd
    // at it, which no hold takes.
    private void Internal(MirLocation at)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);
        this.program.Problems.Add(new(DiagnosticCode.InternalInvariant_Kd, requirement, this.Subject(at), this.body.Ordinal, this.views));
    }

    // A located Unsupported_Kd at a location whose access or call the stratum does not classify; a limit record, which depends on the
    // concrete instantiation, so an inline default copy's stays at its operation (SPEC 23.1.1, R4-M69, R4-M71).
    private void Unsupported(MirLocation at) => this.Unsupported(this.Subject(at));

    // A located Unsupported_Kd at a subject, with no fact, a conflict's causes (rule W) dropped with it.
    private void Unsupported(SourceRef subject)
    {
        this.causes.Clear();
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
        this.program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, requirement, subject, this.body.Ordinal, this.views));
    }

    // The depth record at a subject (SPEC 23.1.1, IMPL 21.3.2; R4-M153), a limit record as Unsupported_Kd is, kept inside an inline copy:
    // OwnershipDepthLimit_Kd, with the bound a MirTypes walk stopped at as its Limit fact, none for a call's CallRetention.Limit, whose
    // bound Binding does not publish; a conflict's causes (rule W) dropped with it.
    private void DepthLimit(SourceRef subject, int? bound)
    {
        this.causes.Clear();
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.DepthLimit);
        var problem = new MirProblem(DiagnosticCode.OwnershipDepthLimit_Kd, requirement, subject, this.body.Ordinal, this.views);
        this.program.Problems.Add(problem, bound is { } limit ? [new(MirFactKind.Limit, subject, limit)] : []);
    }
}
