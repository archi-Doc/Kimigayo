// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Checked parts (SPEC 10.5.1, 10.1, 10.4 step 1, 10.6; PLAN G924 AF-3b, design-af3 Revision 2, the owner's O-335 (a) and O-351 (a)):
// a nested call, construction or contextual Case that awaits its call is checked against the expected Type at its position under
// each remaining candidate, once per distinct expected Type.
// - Responsibility: FitArgument's row "checked against the expected Type" (FitChecked) and its memo (dependentChecks): whether such a
//   part checks at a candidate's closed Type E, and its class there; the count of the queries one resolution starts (SPEC 10.6).
// - Inputs: the part (prepared by its call's probe: its callee group, its arguments, its explicit Type arguments, its Case), E and the
//   part's scope. Its verdict at E is its own resolution's, as a query that publishes nothing: a call's selection (SelectCallAt,
//   Binding.CallSelection) and a Case construction's fit (FitConstructionAt, Binding.Enums).
// - Outputs: Inapplicable where the part's resolution at E has no applicable candidate or is ambiguous (Rd1), with the failed check
//   as the trial's fact (LeaveChecked, CheckedPartFailure) and its components (argumentMisfit); otherwise Waiting with the class its
//   resolution gives (CheckedClass), a Pending premise, a nested declaration or signature failure and a correlation failure included
//   (O-335 (a): a refusal read in a losing candidate's trial never decides the call; the selected candidate's completion binds the
//   part at E and reports them), the part kept (waitingParts) with its selection there (Unproven: an obligation, SPEC 10.1).
// - Readers (ArgFlow ruling 146, one judge): a candidate trial reads Inapplicable and the failed check (SPEC 10.5.1, R1.7); a value
//   call's run on BU-240's split, whose Item is the selected candidate, reads the same failure and the kept parts' Unproven as the
//   run's failure at that part (RunFitFails); neither judges the part again.
// - Invariants: dependentChecks keys a part by its core's syntax identity (SourceRef, argumentParts' key) and E by reference (an
//   interned Type); it lives for one pass (ResetPass) and a query starts only on a miss, so each part is checked once per distinct E;
//   the queries one outermost resolution starts are counted (resolutionWork) and past ResolutionWorkLimit (IMPL A.4) no query runs:
//   the part is kept unchecked and the outermost call is refused (ResolutionWorkLimit_Kd, BindCallCore), never selected. A verdict is
//   a function of (part, E) only: one whose query read a split layer (ReadSplitSince: a binding of the open combination, or a
//   formation that formed none) is its run's, which E does not hold, and is not kept (DECISIONS BU-240, removed with its (R); ArgFlow
//   ruling 152 C1), so a later run at the same E queries again. SPEC 10.4 step 1's exception (an unbound slot takes the argument
//   whole) and step 2's MetOpenSlot read the candidates' declared patterns and the pair at the comparison, never a stored verdict, so
//   the key stays (part, E) at an open E too (Binding.OpenSlots: no owner is carried into a query).
// - Prohibitions: no binding, record or node write; no reader decides a checked part's fit but through FitArgument.
public sealed partial class Binding
{
    // IMPL A.4 (the owner's O-351 (a)): the distinct (part, expected Type) queries one outermost call's resolution may start.
    internal const int ResolutionWorkLimit = 4096;

    private readonly Dictionary<(SourceRef Part, BoundType Expected), CheckedVerdict> dependentChecks = new();

    // The queries the current outermost resolution started (memo hits not counted), and whether one was refused past the limit.
    private int resolutionWork;
    private bool resolutionLimited;

    // SPEC 10.4 step 1, 10.2.2 (design-af3 R1.2, ArgFlow ruling 63): the class of a part checked at `expected` by whether its resolution
    // there returns an owner value: at `ref/U` an owner value is the one temporary of U, borrowed (Cross-Semantics adaptation), and a
    // returned `ref/T` is passed as itself (Expected-Type fitting, 10.2.2 item 2); by value an owner value fits directly (Expected-Type
    // fitting) and a borrow value is value-read (Cross-Semantics adaptation), but at a bare atom, an unbound `T`, which takes a returned
    // value or a returned borrow whole (Expected-Type fitting, 10.2.2 item 2; Binding.OpenSlots). The one class source of a checked
    // part at a closed and at an open position.
    private ArgumentAdaptation CheckedClass(BoundType expected, bool ownerResult)
        => ownerResult ? ExpectedTypeClass(ReferenceEquals(this.ExpectedTypeOf(expected), expected) ? null : expected)
            : expected.Kind is BoundTypeKind.Semantics or BoundTypeKind.OpenSlot ? ArgumentAdaptation.ExpectedTypeFitting : ArgumentAdaptation.CrossSemanticsBorrow;

    // FitArgument's row for a part that awaits its call as a Call or Case: Inapplicable with the failed check as the trial's fact, else
    // Waiting with its class at the position.
    private CandidateApplicability FitChecked(Koto part, Koto node, PartKind kind, BoundType type, BindingScope scope, out BoundArgumentOperation operation)
    {
        var verdict = this.CheckedAt(node, kind, type, scope);
        if (verdict.Failure is { } failure)
        {
            operation = default;
            this.argumentMisfit = verdict.Misfit; // The enclosing arms add their components (ComponentPath.Within).
            this.LeaveChecked(new(node, type, failure));
            return CandidateApplicability.Inapplicable;
        }

        operation = new(part, null, type, ArgumentOperationKind.Value, verdict.Class);
        this.waitingParts.Add(new(part, type, default, verdict.Selection, verdict.Misfit));
        return CandidateApplicability.Waiting;
    }

    // The memo: the part's verdict at `type`, queried on a miss as a selection's evaluation (candidateTrials: its fits bound no omitted
    // Origin and a refusal it reads is held), counted toward its resolution's limit. A contextual Case and a call whose probe published
    // a Case are constructions (FitConstructionAt); every other call is selected (SelectCallAt). `scope` is the scope its call's
    // arguments are bound in, the one its probe bound the part in.
    private CheckedVerdict CheckedAt(Koto part, PartKind kind, BoundType type, BindingScope scope)
    {
        var node = PartCore(part);
        var key = (SourceRef.Of(node), type);
        if (this.dependentChecks.TryGetValue(key, out var kept))
        {
            return kept;
        }

        this.BeginResolution();
        if (++this.resolutionWork > ResolutionWorkLimit)
        {
            this.resolutionLimited = true;
            return new(null, ArgumentAdaptation.ExpectedTypeFitting); // SPEC 10.6: decides nothing; the outermost call is refused.
        }

        if (this.MeasureCallInference)
        {
            this.inferenceQueries++;
        }

        CheckedVerdict verdict;
        var (stamp, mark) = (this.splitReads, this.unformedWrites);
        this.candidateTrials++;
        try
        {
            verdict = kind == PartKind.Call && node is InvocationKoto call && CheckedCallee(call, out _).BoundSymbol?.EnumCase is null
                ? this.SelectCallAt(call, type, scope) : this.FitConstructionAt(node, type, scope);
        }
        finally
        {
            this.candidateTrials--;
        }

        if (!this.ReadSplitSince(stamp, mark))
        {
            this.dependentChecks[key] = verdict;
        }

        return verdict;
    }

    // SPEC 10.6: an outermost resolution (no evaluation open) starts its count afresh.
    private void BeginResolution()
    {
        if (this.candidateTrials == 0)
        {
            this.resolutionWork = 0;
            this.resolutionLimited = false;
        }
    }

    // SPEC 10.5.1: the outcome of a part's check against an expected Type: Failure, the part's own resolution there has no applicable
    // candidate or is ambiguous (NoneApplicable, Ambiguous), or null with the class its resolution gives (SPEC 10.4 step 1); for a
    // qualified Case construction, the enum Type its qualifier fixes there (Owner), which its publisher must reach too
    // (BindEnumConstruction). Selection is the resolution's own classification before Rd6' compares unproven candidates (SelectCallAt):
    // Unproven where the selected candidate's obligation is unproven (SPEC 10.1), a construction's where a part below it is; Misfit the
    // components from the part down to its failure or to that unproven part (ComponentPath: AtShape where the Case does not resolve or
    // take its arguments at the Type; a payload's within its slot of the enum; none where the failure is the part's own).
    private readonly record struct CheckedVerdict(
        CallSelection? Failure, ArgumentAdaptation Class, BoundType? Owner = null, CallSelection Selection = CallSelection.Selected, ComponentPath Misfit = default);

    // SPEC 10.5.1 (design-af3 R1.5): the part checked against its expected Type that made a candidate inapplicable, that Type and its own
    // resolution's outcome there (NoneApplicable or Ambiguous), diagnostic payload only (the record's Checked fact, CheckedFact).
    private readonly record struct CheckedPartFailure(Koto Part, BoundType Expected, CallSelection Outcome)
    {
        // The epoch of the innermost open frame it is read in (design-r2 Amendment 5).
        internal long Epoch { get; init; }
    }
}
