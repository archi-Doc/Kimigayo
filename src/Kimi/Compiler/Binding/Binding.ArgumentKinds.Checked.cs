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
//   as the trial's fact (LeaveChecked, CheckedPartFailure); otherwise Waiting with the class its resolution gives (CheckedClass), a
//   Pending premise, a nested declaration or signature failure and a correlation failure included (O-335 (a): a refusal read in a
//   losing candidate's trial never decides the call; the selected candidate's completion binds the part at E and reports them).
// - Invariants: dependentChecks keys a part by its core's syntax identity (SourceRef, argumentParts' key) and E by reference (an
//   interned Type); it lives for one pass (ResetPass) and a query starts only on a miss, so each part is checked once per distinct E;
//   the queries one outermost resolution starts are counted (resolutionWork) and past ResolutionWorkLimit (IMPL A.4) no query runs:
//   the part is kept unchecked and the outermost call is refused (ResolutionWorkLimit_Kd, BindCallCore), never selected.
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
    // fitting) and a borrow value is value-read (Cross-Semantics adaptation).
    private ArgumentAdaptation CheckedClass(BoundType expected, bool ownerResult)
        => ownerResult ? ExpectedTypeClass(ReferenceEquals(this.ExpectedTypeOf(expected), expected) ? null : expected)
            : expected.Kind == BoundTypeKind.Semantics ? ArgumentAdaptation.ExpectedTypeFitting : ArgumentAdaptation.CrossSemanticsBorrow;

    // FitArgument's row for a part that awaits its call as a Call or Case: Inapplicable with the failed check as the trial's fact, else
    // Waiting with its class at the position.
    private CandidateApplicability FitChecked(Koto part, Koto node, PartKind kind, BoundType type, out BoundArgumentOperation operation)
    {
        var verdict = this.CheckedAt(node, kind, type);
        if (verdict.Failure is { } failure)
        {
            operation = default;
            this.LeaveChecked(new(node, type, failure));
            return CandidateApplicability.Inapplicable;
        }

        operation = new(part, null, type, ArgumentOperationKind.Value, verdict.Class);
        return CandidateApplicability.Waiting;
    }

    // The memo: the part's verdict at `type`, queried on a miss as a selection's evaluation (candidateTrials: its fits bound no omitted
    // Origin and a refusal it reads is held), counted toward its resolution's limit. A contextual Case and a call whose probe published
    // a Case are constructions (FitConstructionAt); every other call is selected (SelectCallAt).
    private CheckedVerdict CheckedAt(Koto part, PartKind kind, BoundType type)
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
        var scope = this.ConstraintScope(node);
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

        this.dependentChecks[key] = verdict;
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
    // candidate or is ambiguous (NoneApplicable, Ambiguous), or null with the class its resolution gives (SPEC 10.4 step 1).
    private readonly record struct CheckedVerdict(CallSelection? Failure, ArgumentAdaptation Class);

    // SPEC 10.5.1 (design-af3 R1.5): the part checked against its expected Type that made a candidate inapplicable, that Type and its own
    // resolution's outcome there (NoneApplicable or Ambiguous), diagnostic payload only (RejectedCandidate.CheckedPart).
    private readonly record struct CheckedPartFailure(Koto Part, BoundType Expected, CallSelection Outcome)
    {
        // The epoch of the innermost open frame it is read in (design-r2 Amendment 5).
        internal long Epoch { get; init; }
    }
}
