// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.2-10.4, 8.4.8.2: candidate evaluation and selection, shared by a call and by a query that must not publish, the omitted base
// selection of an implicit constructor (SPEC 6.3.6). Each candidate's accessibility is decided and its header bound before the set is
// measured (RentCandidates); RentEvaluation and ReturnEvaluation are the one place the call's scratch is rented and returned, and
// SelectAt the one entry: evaluation records each candidate's applicability and plan facts in that scratch; classification decides
// the selection from them. Neither fails, completes nor records a node, except on a broken contract: BetterCandidate, reached from
// classification through SelectBest and PremisesCannotAffect, records Fail(Source, Internal) when two fits of one argument record
// unequal position counts.
public sealed partial class Binding
{
    private enum CallSelection : byte
    {
        Selected,
        InvalidDeclaration,
        FailedSignature,
        Unproven,
        UnformedSplit,
        IncompleteSignature,
        NoneApplicable,
        Ambiguous,
    }

    // The width condition: a view of a call's Origin scratch formed for a candidate covers the candidate's Origins and input Origins, or
    // the call solves none (both widths 0). The widths are measured after the candidates' headers are bound (RentCandidates), so a
    // shortfall is a broken contract, never a dropped slot.
    private static void RequireOriginWidths(FunctionKoto function, int originSlots, int inputSlots)
    {
        if ((originSlots | inputSlots) != 0 && (function.Origins.Count > originSlots || InputOriginCount(function) > inputSlots))
        {
            throw new InvalidOperationException($"`{function.BoundSymbol?.Name}` has more Origin slots than its call measured (InternalInvariant; R7-P2 A9).");
        }
    }

    // The scratch sizes a candidate set needs, read from the bound headers of its accessible candidates (RentCandidates). Origins are
    // solved when the caller or the enclosing Type can carry them, or when a candidate declares one, returns one or takes one. Receiver:
    // the group has a receiver shape (SPEC 7.3.2), some candidate taking a receiver (ReceiverIndex), so a value-qualified call acquires its
    // Receiver Expression by the Name before selection.
    private static CandidateShape MeasureCandidates(FunctionGroup candidates, bool solveOrigins)
    {
        var shape = new CandidateShape { SolveOrigins = solveOrigins };
        foreach (var candidate in candidates)
        {
            if (candidate.Declaration is FunctionKoto function)
            {
                shape.Count++;
                shape.Receiver |= candidate.ReceiverIndex >= 0;
                shape.Parameters = Math.Max(shape.Parameters, function.Parameters.Count);
                shape.Generics = Math.Max(shape.Generics, CallSlotCount(function));
                shape.Origins = Math.Max(shape.Origins, function.Origins.Count);
                shape.InputOrigins = Math.Max(shape.InputOrigins, InputOriginCount(function));
                shape.SolveOrigins |= function.Origins.Count != 0 || (candidate.Type is { } resultPattern && resultPattern.CarriesOrigin);
                for (var p = 0; !shape.SolveOrigins && p < function.Parameters.Count; p++)
                {
                    shape.SolveOrigins |= function.Parameters[p].Type.BoundType is { } input && input.CarriesOrigin;
                }
            }
        }

        return shape;
    }

    // SPEC 7.7.4, 10.1, 10.2.2: the common Function Type a call's argument is declared at by every candidate a case can still select,
    // each one not Inapplicable (the first one's, for the record: the Owned proof does not read it). Null where one of them declares
    // another Type there, so that some case erases nothing (an applicable `f: F` beside a resting `f: (Z, Box) -> i32`), or none is left.
    private static BoundType? ErasedByEveryCandidate(ref CallEvaluation evaluation, int argument)
    {
        BoundType? erased = null;
        for (var i = 0; i < evaluation.Count; i++)
        {
            if (evaluation.Evaluated[i] is not { State: not CandidateApplicability.Inapplicable, Symbol.Declaration: FunctionKoto function })
            {
                continue;
            }

            if (function.Parameters[evaluation.MappingOf(i)[argument]].Type.BoundType is not { Kind: BoundTypeKind.Function } declared)
            {
                return null;
            }

            erased ??= declared;
        }

        return erased;
    }

    // SPEC 10.1, 10.4, 8.3: whether a selection's classification is the call's own failure, as BindCall records it: no candidate, an
    // ambiguity, or a premise of the candidates not proven (Unproven: Constraints take part in applicability); not UnformedSplit, where
    // BindCall rests (SelectionOnSplit).
    private static bool SelectionFails(CallSelection kind) => kind is CallSelection.NoneApplicable or CallSelection.Ambiguous or CallSelection.Unproven;

    // SPEC 10.4: the candidates of an evaluation that applied (Applicable or Waiting), as bits of their indices (the first 64).
    private static ulong AppliedCandidates(in CallEvaluation evaluation)
    {
        var applied = 0UL;
        for (var i = 0; i < Math.Min(evaluation.Count, 64); i++)
        {
            applied |= evaluation.Evaluated[i].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting ? 1UL << i : 0;
        }

        return applied;
    }

    // SPEC 15.1.5: the absent spelling a NoneApplicable selection names: the Acquisition of the last candidate, in evaluation order,
    // whose trial a bare Place alone kept from applying (TryCandidate), else none.
    private static AcquisitionFailure? LastAcquisition(in CallEvaluation evaluation)
    {
        for (var i = evaluation.Count - 1; i >= 0; i--)
        {
            if (evaluation.Evaluated[i].Acquisition is { } acquisition)
            {
                return acquisition;
            }
        }

        return null;
    }

    // SPEC 7.4, 23.1.2 (rule W; G812 ruling 141): a candidate whose group failed its shape (KeepsSignature's ParameterShapeMismatch or
    // ReceiverShapeMismatch), on which a selection the split leaves open rests: that rest reads the failed group, so it is dependent.
    private static Koto? FailedShapeMember(in CallEvaluation evaluation)
    {
        for (var i = 0; i < evaluation.Count; i++)
        {
            if (evaluation.Evaluated[i].Symbol?.Declaration is FunctionKoto { BindingFailure: BindingFailure.ParameterShapeMismatch or BindingFailure.ReceiverShapeMismatch } failed)
            {
                return failed;
            }
        }

        return null;
    }

    // The callee a call's probe resolved and published its group on (BindCallCore): the method through parentheses, a generic's
    // identifier, with that generic.
    private static Koto CheckedCallee(InvocationKoto call, out GenericsKoto? generic)
    {
        var inner = KotoHelper.UnwrapParentheses(call.Method);
        generic = inner as GenericsKoto;
        return generic?.Identifier ?? inner;
    }

    // A named call's candidate rows (RentCandidates, rented before the set is measured), its Self (its requirements' or a requirement
    // callee's) and the measured shape: what RentEvaluation leaves to its caller, shared by BindCall and ProbeSelection. The rows go
    // into the evaluation RentEvaluation rents (returned with it by ReturnEvaluation), or are returned alone before it is rented.
    private (EvaluatedCandidate[] Evaluated, BoundType? Self, CandidateShape Shape) NamedCandidates(Koto callee, BindingSymbol group, in FunctionGroup candidates, BindingScope scope)
    {
        var self = candidates.Requirements?.Self ?? (callee as RequirementCalleeKoto)?.Self;
        return (this.RentCandidates(candidates, callee, scope), self, MeasureCandidates(candidates, self is not null || group.Scope.Owner is StructKoto or EnumKoto));
    }

    // SPEC 10.1, 10.4, 10.5.1, 8.3 (G812 rulings 122, 123, 126, 139): a named call's own selection at `expected`, judged in full as
    // BindCall judges it, as a query that does not publish (BeginSelectionQuery): the evaluation BindCall makes (NamedCandidates,
    // RentEvaluation, BindExplicitArguments, SelectAt), its classification (ClassifySelection's outcome, which BindCall
    // dispatches on) and winner, the candidates that applied (bits of their indices: an ambiguity's, CompleteAmbiguous) and whether a
    // pending candidate's Unknown premise reads the split (PremiseReadsSplit). Where every Pending candidate of an Unproven selection is
    // unproven only by an Unknown premise, the candidates compared as though applicable (SPEC 10.1, PremisedBest: Premised); whether
    // the final winner (Winner, else Premised) returns an owner value (OwnerResultAt). A callee that names no function group, or
    // explicit arguments it does not choose, select nothing to judge (Selected, -1).
    private ProbedSelection ProbeSelection(InvocationKoto call, BindingScope scope, BoundType? expected)
    {
        var callee = CheckedCallee(call, out var generic);
        if (callee.BoundSymbol is not { Kind: BindingSymbolKind.Function } group)
        {
            return new(CallSelection.Selected, -1, 0, false);
        }

        var candidates = this.GroupOf(callee, group);
        var (evaluated, self, shape) = this.NamedCandidates(callee, group, candidates, scope);
        var query = this.BeginSelectionQuery();
        var evaluation = this.RentEvaluation(call, callee, generic, scope, expected, shape);
        evaluation.Evaluated = evaluated;
        evaluation.Self = self;
        evaluation.Requirements = candidates.Requirements;
        try
        {
            if (!this.BindExplicitArguments(ref evaluation, shape.Count))
            {
                return new(CallSelection.Selected, -1, 0, false);
            }

            var (kind, winner) = this.SelectAt(ref evaluation, candidates);
            int? premised = kind == CallSelection.Unproven && evaluation.FailedPendingSignature is null && evaluation.PremisesOnly
                ? this.PremisedBest(call, evaluation.Evaluated.AsSpan(0, evaluation.Count), evaluation.Operations, evaluation.OperationStride) : null;
            var final = kind == CallSelection.Selected ? winner : premised ?? -1;
            return new(kind, winner, AppliedCandidates(in evaluation), evaluation.PremiseReadsSplit, premised, final >= 0 && this.OwnerResultAt(evaluation, final));
        }
        finally
        {
            this.EndSelectionQuery(query);
            this.ReturnEvaluation(evaluation);
        }
    }

    // A query's entry (a selection that must not publish, SPEC 10.1: nothing of a candidate that is not selected is reported): the
    // state its evaluation touches outside its own scratch, saved here and restored by EndSelectionQuery at its exit whatever the
    // outcome: the obligations its trials add, the deferred acquisition and failed cause SelectAt clears, the requirement reference
    // each candidate sets (null on entry: the arguments are the caller's expressions, as in BindCall), the inference counters, an
    // enclosing trial's facts and the proofs' single-slot facts, which its trials replace (OpenTrial), the walk's open positions, and
    // the waiting parts the enclosing fit met (its own fits start theirs above them: waitingBase). The one owner of that set for every
    // query (ProbeSelection, OpenForm, SelectOmittedBaseConstructor; a part checked against its expected Type,
    // SelectCallAt and FitConstructionAt).
    private SelectionQuery BeginSelectionQuery()
    {
        var query = new SelectionQuery(this.obligations.Count, this.acquisitionFailure, this.failedCause, this.activeRequirementContract, (this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks))
        {
            TrialUnknown = this.trialUnknown,
            TrialClause = this.trialClause,
            TrialPending = this.trialPending,
            TrialRefusal = this.trialRefusal,
            TrialChecked = this.trialChecked,
            FailedUnknown = this.failedUnknown,
            FailedClause = this.failedClause,
            PositionParts = this.positionParts.Count,
            Waiting = (this.waitingBase, this.waitingParts.Count),
        };
        this.activeRequirementContract = null;
        this.waitingBase = this.waitingParts.Count;
        return query;
    }

    private void EndSelectionQuery(in SelectionQuery query)
    {
        for (var i = query.Obligations; i < this.obligations.Count; i++)
        {
            this.obligationSet.Remove(this.obligations[i]);
        }

        this.obligations.RemoveRange(query.Obligations, this.obligations.Count - query.Obligations);
        this.acquisitionFailure = query.Acquisition;
        this.failedCause = query.Cause;
        this.activeRequirementContract = query.Contract;
        (this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks) = query.Counters;
        (this.trialUnknown, this.trialClause, this.trialPending, this.trialRefusal, this.trialChecked) = (query.TrialUnknown, query.TrialClause, query.TrialPending, query.TrialRefusal, query.TrialChecked);
        (this.failedUnknown, this.failedClause) = (query.FailedUnknown, query.FailedClause);
        this.ClosePosition(query.PositionParts);
        this.waitingParts.RemoveRange(query.Waiting.Count, this.waitingParts.Count - query.Waiting.Count);
        this.waitingBase = query.Waiting.Base;
    }

    // SPEC 10.1, 9.4.1: a candidate set's rows, rented before the set is measured. Each candidate's accessibility is decided once, into
    // its row, the one verdict its evaluation reads; a synthesized range construction pins its Kimi target, which source access does not
    // restrict (SPEC 4.6.2). Each accessible candidate's header is bound here, so the set is measured once, after every header fact
    // MeasureCandidates reads is published, also for a call bound before BindDeclarations' header loop reaches a candidate. The caller
    // returns the rows.
    private EvaluatedCandidate[] RentCandidates(in FunctionGroup candidates, Koto callee, BindingScope scope)
    {
        var count = 0;
        foreach (var candidate in candidates)
        {
            count += candidate.Declaration is FunctionKoto ? 1 : 0;
        }

        var rows = this.candidateScratch.Rent(count);
        var index = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Declaration is FunctionKoto)
            {
                var accessible = callee is SyntheticKoto || this.Accessible(candidate, scope, receiverType: this.CallReceiver(callee)?.BoundType);
                ref var row = ref rows[index++]; // A returned row is cleared; the evaluation writes its other fields.
                row.Symbol = candidate;
                row.Accessible = accessible;
                if (accessible)
                {
                    this.BindHeader(candidate);
                }
            }
        }

        return rows;
    }

    // The scratch of one call's candidate evaluation, sized by its measured candidate set and returned by ReturnEvaluation: the one
    // place a selection's scratch is rented, for a call that publishes and for a query that does not. The candidate rows
    // (RentCandidates, rented before the set is measured), the explicit Type arguments, Self and the requirement group are the
    // caller's to fill; ReturnEvaluation returns the rows with the rest.
    private CallEvaluation RentEvaluation(InvocationKoto call, Koto callee, GenericsKoto? generic, BindingScope scope, BoundType? expected, in CandidateShape shape)
    {
        var candidateCount = shape.Count;
        var argumentCount = call.ArgumentNodes.Count;
        var originSlots = shape.SolveOrigins ? shape.Origins : 0;
        var inputSlots = shape.SolveOrigins ? shape.InputOrigins : 0;
        var explicitCount = generic?.TypeArguments.Count ?? 0;
        var evaluation = new CallEvaluation
        {
            Call = call,
            Callee = callee,
            Generic = generic,
            Scope = scope,
            Expected = expected,
            FrameCount = candidateCount,
            OperationStride = argumentCount + 1,
            OriginSlots = originSlots,
            InputSlots = inputSlots,
            Winner = -1,
            PremisesOnly = true,
        };
        evaluation.Frames = this.RentFrames(candidateCount, shape.Generics, call, scope);
        evaluation.ExplicitTypes = this.typeScratch.Rent(explicitCount);
        evaluation.ExplicitLengths = this.lengthScratch.Rent(explicitCount);
        evaluation.Mapping = this.indexScratch.Rent(candidateCount * argumentCount);
        evaluation.Used = this.flagScratch.Rent(Math.Max(1, shape.Parameters));
        evaluation.CompletedWaiting = this.flagScratch.Rent(argumentCount);
        evaluation.WaitingContexts = this.typeScratch.Rent(argumentCount);
        evaluation.CompletedWaiting.AsSpan(0, argumentCount).Clear();
        evaluation.Origins = this.originScratch.Rent(candidateCount * originSlots);
        evaluation.Inputs = this.originScratch.Rent(candidateCount * inputSlots);
        evaluation.Operations = this.argumentOperationScratch.Rent(candidateCount * evaluation.OperationStride);
        evaluation.Rows = this.rowScratch.Rent(candidateCount * argumentCount);
        evaluation.ChildSlices = this.indexScratch.Rent(candidateCount * 2);
        evaluation.ChildBase = this.childRows.Count;
        evaluation.PositionBase = this.positionOperations.Count;
        return evaluation;
    }

    // Returns what RentEvaluation rented and the candidate rows, and ends the evaluation's retained rows (R3) and element positions.
    private void ReturnEvaluation(in CallEvaluation evaluation)
    {
        this.argumentOperationScratch.Return(evaluation.Operations, clearArray: true);
        this.rowScratch.Return(evaluation.Rows, clearArray: true);
        this.indexScratch.Return(evaluation.ChildSlices);
        this.childRows.RemoveRange(evaluation.ChildBase, this.childRows.Count - evaluation.ChildBase);
        this.positionOperations.RemoveRange(evaluation.PositionBase, this.positionOperations.Count - evaluation.PositionBase);
        this.lengthScratch.Return(evaluation.ExplicitLengths, clearArray: true);
        this.candidateScratch.Return(evaluation.Evaluated, clearArray: true);
        this.originScratch.Return(evaluation.Inputs, clearArray: true);
        this.originScratch.Return(evaluation.Origins, clearArray: true);
        this.typeScratch.Return(evaluation.WaitingContexts, clearArray: true);
        this.typeScratch.Return(evaluation.ExplicitTypes, clearArray: true);
        this.flagScratch.Return(evaluation.CompletedWaiting);
        this.flagScratch.Return(evaluation.Used);
        this.indexScratch.Return(evaluation.Mapping);
        this.ReturnFrames(evaluation.Frames, evaluation.FrameCount);
    }

    // SPEC 10.1-10.4: one call's selection over its rented evaluation: every candidate evaluated, then the selection they determine.
    // The entry a publishing call and a query share: it starts from no deferred acquisition, no failed cause and no requirement
    // reference (each trial runs under its candidate's, TryCandidate), and neither fails, completes nor records a node, but for a broken
    // contract's Fail(Source, Internal) in BetterCandidate (reached from ClassifySelection through SelectBest and PremisesCannotAffect);
    // the caller acts on the outcome.
    private (CallSelection Kind, int Winner) SelectAt(ref CallEvaluation evaluation, in FunctionGroup candidates)
    {
        this.BeginResolution();
        this.acquisitionFailure = null;
        this.failedCause = null;
        this.activeRequirementContract = null;
        this.EvaluateCallCandidates(ref evaluation, candidates);
        return this.ClassifySelection(ref evaluation);
    }

    // SPEC 10.5.1, 10.1-10.4 (PLAN G924 AF-3b; design-af3 R1.3, R2.6): a nested call's selection at an expected Type, as a query that
    // publishes nothing (Binding.ArgumentKinds.Checked): the verdict of its probed selection there (ProbeSelection, which reads what the
    // argument probe published: the callee's group, a member's selection, the prepared arguments, the explicit arguments' bound Types).
    // The shape contracts are the publishing call's (BindCallCore): a correlation failure is the nested call's own error. Selected:
    // kept, with its winner's class. NoneApplicable or Ambiguous: the check fails. Unproven (Rd6'): where every Pending candidate is
    // unproven only by an Unknown premise, the candidates compared as though applicable (Premised): kept with the strictly best one's
    // class, or Ambiguous with none; any other Pending candidate (BU-86's environment result, an open signature) keeps it unranked
    // (O-335 (a): never Pending). A failed declaration or signature is the nested call's own error, and explicit arguments the probe
    // failed (Unsupported, Selected with no winner) a failed part, never queried: kept. The raw classification is the verdict's too.
    private CheckedVerdict SelectCallAt(InvocationKoto call, BoundType expected, BindingScope scope)
    {
        var probed = this.ProbeSelection(call, scope, expected);
        var selection = probed is { Kind: CallSelection.Unproven, Premised: { } premised } ? (premised < 0 ? CallSelection.Ambiguous : CallSelection.Selected) : probed.Kind;
        return selection switch
        {
            CallSelection.Selected when probed.Winner >= 0 || probed.Premised >= 0 => new(null, this.CheckedClass(expected, probed.OwnsResult), Selection: probed.Kind),
            CallSelection.NoneApplicable or CallSelection.Ambiguous => new(selection, default, Selection: probed.Kind),
            _ => new(null, ArgumentAdaptation.ExpectedTypeFitting, Selection: probed.Kind),
        };
    }

    // SPEC 3.4.1, 10.2.2 item 2: whether a candidate's result, substituted by its solution, is an owner value (OwnerResult); a bare slot
    // still unbound counts as one (its value is what U fills).
    private bool OwnerResultAt(in CallEvaluation evaluation, int index)
    {
        ref readonly var row = ref evaluation.Evaluated[index];
        var function = (FunctionKoto)row.Symbol!.Declaration;
        var pattern = function.IsConstructor && row.DeclaringType is not null ? this.SelfType(row.DeclaringType.Symbol!) : function.BoundSymbol!.Type;
        return pattern is null || OwnerResult(this.Solution(evaluation, index, function, row.DeclaringType).Type(pattern) ?? pattern);
    }

    // SPEC 10.1, 15.4.4: evaluates every candidate. A fit there is pure (FitOrigin): a candidate that is not selected bounds no
    // omitted Origin, and the selected one's bounds are recorded after selection (JudgeSelectedCall).
    private void EvaluateCallCandidates(ref CallEvaluation evaluation, in FunctionGroup candidates)
    {
        this.candidateTrials++;
        var selection = this.OpenFrame(); // Each trial hands its facts to the selection, which reads them per candidate (r2-6).
        try
        {
            foreach (var candidate in candidates)
            {
                this.EvaluateCallCandidate(ref evaluation, candidate);
            }
        }
        finally
        {
            this.CloseFrame(selection);
            this.candidateTrials--;
        }
    }

    private void EvaluateCallCandidate(ref CallEvaluation evaluation, BindingSymbol candidate)
    {
        if (candidate.Declaration is not FunctionKoto function)
        {
            return;
        }

        var call = evaluation.Call;
        var callee = evaluation.Callee;
        var scope = evaluation.Scope;
        var index = evaluation.Count++;
        var operations = evaluation.OperationsOf(index);
        operations.Clear();
        evaluation.RowsOf(index).Clear(); // F1: each candidate writes its own root rows.
        evaluation.ChildSliceOf(index)[1] = -1; // R3: composed at the candidate's first solve.
        var declaringType = evaluation.Self is null ? this.CallDeclaringType(callee, candidate) : null;
        (this.premiseReadsSplit, this.refusedOnSplit) = (false, false);
        if (this.MeasureCallInference)
        {
            this.inferenceMappings++;
        }

        // The candidate's row, written once, here: RentCandidates wrote its Symbol and Accessible (decided once, and the header bound,
        // before the set was measured), the trial writes its verdict and facts (TryCandidate). An inaccessible candidate is not tried:
        // its row keeps the rent's cleared State (Inapplicable) and the mapping's defaults, the other trial members default.
        ref var row = ref evaluation.Evaluated[index];
        var mapping = evaluation.MappingOf(index); // Written here only; every later pass reads it.
        row.ArgumentMap = MapCallArguments(call, function, this.CallReceiver(evaluation.Generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method)) is not null, mapping, evaluation.Used);
        row.DefaultsUsed = row.ArgumentMap.DefaultsUsed;
        if (row.Accessible)
        {
            if (function.IsConstructor)
            {
                declaringType = row.ArgumentMap.Valid ? this.ConstructorType(call, function, scope, mapping, evaluation.Expected) : null;
            }

            this.TryCandidate(this.Solution(evaluation, index, function, declaringType), evaluation.Expected, operations, ref row);
        }

        if (function.IsConstructor && declaringType is not null)
        {
            declaringType = this.ConstructionType(function, declaringType, evaluation.Frames[index].Types) ?? declaringType;
        }

        // The refusal the trial held (the owner's O-335 (a)): settled only if this candidate is selected (Binding.Calls).
        var state = row.State;
        var checkedPart = this.TakeTrialChecked(call); // SPEC 10.5.1: the reason of an inapplicable candidate only.
        row.DeclaringType = declaringType;
        row.HeldRefusal = this.TakeTrialRefusal(call);
        row.StableConstruction = function.IsConstructor && StableConstructionPremises(state, row.Unsolved, declaringType, operations);
        row.PremiseReadsMark = this.premiseReadsSplit && HoldsMark(evaluation.Frames[index].Types); // DECISIONS BU-240 (G812 ruling 159)
        row.CheckedFailure = state == CandidateApplicability.Inapplicable ? checkedPart : null;
        // The trial's judgment is taken once, in the selection's frame (DECISIONS BU-86); an unformed split (BU-240) only of a candidate that
        // rests on it (Pending; TryCandidate's verdict), as an environment result.
        var judgment = this.TakeTrialPending(call);
        evaluation.Unformed |= state == CandidateApplicability.Pending && judgment is { Kind: PendingJudgmentKind.UnformedSplit };
        evaluation.PremiseReadsSplit |= state == CandidateApplicability.Pending && this.premiseReadsSplit;
        evaluation.RefusedOnSplit |= state == CandidateApplicability.Inapplicable && this.refusedOnSplit;
        evaluation.Pending |= state == CandidateApplicability.Pending;
        if (state == CandidateApplicability.Pending)
        {
            // A failed signature part or explicit Type argument leaves the selection resting on it (SPEC 23.1.2).
            evaluation.FailedPendingSignature ??= IncompleteSignature(function) ?? this.FailedSignaturePart(function) ??
                (judgment is { Kind: PendingJudgmentKind.FailedTypeArgument } failed ? failed.Part : null);

            // SPEC 8.3, 15.6.1: a Callable proof that is Unknown only in its Origin part is explained by its Constraint record.
            evaluation.PendingCount++;
            evaluation.PremisesOnly &= row.PremiseUnknown;
            evaluation.CallableFailure ??= function.TypeConstraints.Count != 0 ? this.CallableOriginFailure(call, function, mapping) : null;
            evaluation.ConstraintFailure ??= row.ConstraintFailure;
            evaluation.PendingJudgment ??= judgment is { Kind: PendingJudgmentKind.EnvironmentResult } judged ? judged : null;
        }

        evaluation.Error |= state == CandidateApplicability.Error;
        evaluation.InvalidDeclaration ??= state == CandidateApplicability.Error ? this.InvalidDeclarationContextCause(function) : null;
        if (state is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
        {
            evaluation.IncompleteSignature ??= state == CandidateApplicability.Inapplicable ? IncompleteSignature(function) : null;
            return;
        }

        evaluation.Applicable++;
        evaluation.Winner = index;
    }

    // The selection the evaluated candidates determine. A failed candidate declaration or signature leaves the selection resting on
    // that failure (SPEC 23.1.2); an Unknown premise defers it only when it can affect it (SPEC 8.3.3). A candidate unproven through an
    // unformed split (DECISIONS BU-240, temporary, PLAN G886: never premise-only, unrankable) makes the selection UnformedSplit: the call is
    // one located Unsupported_Kd, however many are pending (FailUnformedCall).
    private (CallSelection Kind, int Winner) ClassifySelection(ref CallEvaluation evaluation)
    {
        var candidates = evaluation.Evaluated.AsSpan(0, evaluation.Count);
        if (evaluation.Error)
        {
            return (CallSelection.InvalidDeclaration, -1);
        }

        if (evaluation.Pending && (evaluation.FailedPendingSignature is not null || !evaluation.PremisesOnly || evaluation.Applicable == 0 ||
            !this.PremisesCannotAffect(evaluation.Call, candidates, evaluation.Operations, evaluation.OperationStride)))
        {
            return (evaluation.FailedPendingSignature is not null ? CallSelection.FailedSignature
                : evaluation.Unformed ? CallSelection.UnformedSplit : CallSelection.Unproven, -1);
        }

        if (evaluation.RefusedOnSplit && evaluation.Applicable != 0)
        {
            return (CallSelection.UnformedSplit, -1); // SPEC 10.1 (G812 ruling 141, L1): never dropped so that another wins; judged case by case.
        }

        if (evaluation.Applicable == 0)
        {
            return (evaluation.IncompleteSignature is null ? CallSelection.NoneApplicable : CallSelection.IncompleteSignature, -1);
        }

        if (evaluation.Applicable == 1)
        {
            return (CallSelection.Selected, evaluation.Winner);
        }

        var comparable = true;
        foreach (ref readonly var candidate in candidates)
        {
            if (candidate.State == CandidateApplicability.Waiting)
            {
                // F will be this argument's concrete callable Type in every candidate. Its Callable signature is an expectation, not a
                // parameter Type to rank. Select from ordinary inputs/defaults first; only the winner supplies a body context, even when
                // the candidates' signatures differ. Slots acquired in different modes violate the parameter acquisition shape (SPEC
                // 7.3.1), which the declarations report; such candidates select nothing.
                comparable = this.ComparableCallableSlots(evaluation.Call, candidates, evaluation.Operations, evaluation.OperationStride);
                break;
            }
        }

        var winner = comparable ? this.SelectBest(candidates, evaluation.Operations, evaluation.OperationStride) : -1;
        return (winner < 0 ? CallSelection.Ambiguous : CallSelection.Selected, winner);
    }

    // DECISIONS BU-240 (temporary, PLAN G886), SPEC 7.7.4, 23.1.2 (C-359 C2): a call whose selection rests on an unformed split is one located
    // Unsupported_Kd. A Function Item or reference part that every candidate a case can still select declares at a common Function Type
    // (ErasedByEveryCandidate) is erased whichever of them a case selects, so its environment is still judged, once, as a conversion's
    // is (FailErasedOwned; a reference part by its written arguments, FailUnformedOwned): the erasure comes after a selection that
    // the split leaves open, and its Owned proof reads no unformed Type. The selection is first judged in each combination of split
    // cases (SelectionOnSplit, G812 ruling 139): where it fails in every one the call keeps its own record, the canonical run's
    // classification, as BindCall records it with no evaluation to explain it (FailWaitingSelection). Each part awaiting the call is
    // judged and completed as a value call's part on the split is (CompleteNamedPartOnSplit; G812 rulings 134, 136, 143, 156), at the
    // parameter the first evaluation of the candidate the canonical run selects forms from its own columns, as SlotSolution.Type forms
    // a parameter: in each case of the bindings it reads, then in the canonical case, marked open where a split binding decides it
    // (OpenForm); the call rests on the first that fails.
    private BoundType? FailUnformedCall(ref CallEvaluation evaluation, bool fails, ProbedSelection canonical)
    {
        var call = evaluation.Call;
        if (fails)
        {
            return canonical.Kind == CallSelection.Ambiguous ? this.FailAmbiguous(in evaluation, canonical.Applied)
                : this.FailWaitingSelection(call, canonical.Kind == CallSelection.Unproven ? BindingFailure.UnprovenConstraint : BindingFailure.NoApplicableCandidate);
        }

        // The canonical run's winner indexes this evaluation's candidates: both evaluate the call's one group (GroupOf) in RentCandidates'
        // order, so it is below Count and names the same candidate, whose columns are its first evaluation's.
        var shaped = canonical.Kind == CallSelection.Selected ? canonical.Winner : -1;
        Koto? completion = null;
        for (var a = 0; a < call.ArgumentNodes.Count; a++)
        {
            var part = call.ArgumentNodes[a];
            if (shaped >= 0 && evaluation.Evaluated[shaped] is { Symbol.Declaration: FunctionKoto function } && part.BoundType is null &&
                this.KindOf(part) != PartKind.Typed && function.Parameters[evaluation.MappingOf(shaped)[a]].Type.BoundType is { } declared)
            {
                completion ??= this.CompleteNamedPartOnSplit(
                    (call, a, declared, evaluation.Scope),
                    (Evaluation: evaluation, Candidate: shaped),
                    static (binding, s, type) => binding.CallType(type, s.Evaluation.Evaluated[s.Candidate].Symbol!.Declaration, s.Evaluation.Frames[s.Candidate].Types, s.Evaluation.Scope, s.Evaluation.Self, s.Evaluation.OriginsOf(s.Candidate), s.Evaluation.InputsOf(s.Candidate), s.Evaluation.Evaluated[s.Candidate].DeclaringType, s.Evaluation.Frames[s.Candidate].Lengths));
            }

            if (ErasedByEveryCandidate(ref evaluation, a) is not { } erased)
            {
                continue;
            }

            if (part.BoundType is { Kind: BoundTypeKind.FunctionItem } item)
            {
                this.FailErasedOwned(part, item, erased);
            }
            else if (this.KindOf(part) == PartKind.Reference)
            {
                this.FailUnformedOwned(part, erased, null);
            }
        }

        return (FailedShapeMember(in evaluation) ?? completion) is { } failed ? this.CompleteDependent(call, failed) : this.Fail(call, BindingFailure.Unsupported, true);
    }

    // DECISIONS BU-240 with rule W (G812 rulings 143, 156; SPEC 23.1.2): argument `a` of a named call on the split, awaiting the call, at the
    // parameter `form` gives its declared Type in the canonical run's candidate. It is judged in each case of the bindings it reads as
    // a value call's part is (JudgeSplitPart, ArgumentPartFails), so one failing in every case at a waiting part is completed whole at
    // the canonical case's parameter, where each part failing there is its own record (CompleteFailedPart); any other is completed at
    // the open formed parameter (OpenForm, CompleteOnSplit). The part that failed so, else null: the call rests on it.
    private Koto? CompleteNamedPartOnSplit<TState>(
        (InvocationKoto Call, int A, BoundType Declared, BindingScope Scope) part, TState state, Func<Binding, TState, BoundType, BoundType?> form)
    {
        var argument = part.Call.ArgumentNodes[part.A];
        this.PrepareWaitingCases(argument, part.Scope); // A run binds nothing (G812 ruling 123).
        var (fails, waiting, canonical) = this.JudgeSplitPart(
            (part, argument, state, form),
            static (binding, c) =>
            {
                binding.EnterRunPart();
                return binding.EndRunPart(c.part.A, binding.ArgumentPartFails(c.argument, c.part.Declared, c.state, c.form, c.part.Scope, c.part.Call, out _, out _));
            });
        if (fails && waiting is not null)
        {
            this.CompleteFailedPart(argument, canonical!, part.Scope);
            return waiting;
        }

        return this.OpenForm(part.Declared, state, form) is { } parameter && this.CompleteOnSplit(argument, parameter, part.Scope, part.Call) ? argument : null;
    }

    // DECISIONS BU-240 with rule W (G812 ruling 139; temporary with it): a named call's selection judged in each combination of split
    // cases (RestsOnSplitCases), each run a query (ProbeSelection), the whole selection one part: whether it fails in every combination
    // (no candidate, an ambiguity or an unproven premise: where BindCall records the call's own failure; an unproven premise that reads
    // the split rests on it, O-332 (a), G812 ruling 141), and the canonical run's selection (the first run's, each binding at its first
    // admitted Semantics; ruling 130). Where the canonical run selects a candidate whose own trial reads no split (Applicable or Waiting
    // outside the runs), a second search asks whether every combination selects it (Selected): the split then decides nothing of the
    // selection, and an unproven candidate kept in it (SPEC 10.1) is inapplicable in every case (G812 ruling 141, L1).
    private (bool Fails, ProbedSelection Canonical, int Selected) SelectionOnSplit(in CallEvaluation evaluation)
    {
        var enclosing = this.canonicalSelection;
        this.canonicalSelection = null;
        var rests = this.RestsOnSplitCases(this.unformedWrites, (evaluation.Call, evaluation.Scope, evaluation.Expected), static (binding, check) =>
        {
            var selection = binding.ProbeSelection(check.Call, check.Scope, check.Expected);
            binding.canonicalSelection ??= selection;
            return SelectionFails(selection.Kind) && !(selection.Kind == CallSelection.Unproven && selection.PremiseReadsSplit);
        });
        var canonical = this.canonicalSelection ?? new(CallSelection.UnformedSplit, -1, 0, false);
        this.canonicalSelection = enclosing;
        var only = !rests || canonical is not { Kind: CallSelection.Selected, Winner: >= 0 and var winner } ||
            evaluation.Evaluated[winner].State is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting) ||
            this.RestsOnSplitCases(this.unformedWrites, (evaluation.Call, evaluation.Scope, evaluation.Expected, winner), static (binding, check) =>
                binding.ProbeSelection(check.Call, check.Scope, check.Expected) is { Kind: CallSelection.Selected } selection && selection.Winner == check.winner)
            ? -1 : canonical.Winner;
        return (!rests, canonical, only);
    }

    // A named call's selection probed as a query (ProbeSelection): its classification and winner, the candidates that applied,
    // whether a pending candidate's Unknown premise reads the split, the premise-only comparison's winner (-1 none; null where the
    // selection is not Unproven by Unknown premises only) and whether the final winner returns an owner value.
    private readonly record struct ProbedSelection(CallSelection Kind, int Winner, ulong Applied, bool PremiseReadsSplit, int? Premised = null, bool OwnsResult = false);

    private struct CandidateShape
    {
        internal int Count;
        internal int Parameters;
        internal int Generics;
        internal int Origins;
        internal int InputOrigins;
        internal bool SolveOrigins;
        internal bool Receiver;
    }

    // What BeginSelectionQuery saves (see there).
    private readonly record struct SelectionQuery(
        int Obligations, AcquisitionFailure? Acquisition, ICheckCause? Cause, BindingSymbol? Contract, (long, long, long) Counters)
    {
        internal CallableUnknown? TrialUnknown { get; init; }

        internal ClauseFact? TrialClause { get; init; }

        internal PendingJudgment? TrialPending { get; init; }

        internal (Refusal Held, long Epoch)? TrialRefusal { get; init; }

        internal CheckedPartFailure? TrialChecked { get; init; }

        internal CallableUnknown? FailedUnknown { get; init; }

        internal ClauseFact? FailedClause { get; init; }

        internal int PositionParts { get; init; }

        // The enclosing fit's waiting parts: their base and count (waitingBase, waitingParts).
        internal (int Base, int Count) Waiting { get; init; }
    }

    // The inputs and rented scratch of one call's candidate evaluation, and the facts it accumulates for the selection. Each candidate's
    // slots are its own frame (Frames, R7-I2 c), and its argument operations, root rows, child slice, mapping, Origins and input Origins
    // its own stride of the call's columns (OperationsOf, RowsOf, ChildSliceOf, MappingOf, OriginsOf, InputsOf: `Count x stride` arrays
    // rented once per call and never grown, replaced or returned before the selection ends; one accessor each). Only a candidate's first
    // evaluation (EvaluateCallCandidate) writes its mapping (MapCallArguments); the evaluation, the defaults' re-check, the waiting
    // re-trial and the selected call's judgments solve into the candidate's own Origin strides (Solution), and CallType marks its input
    // Origins Static there; the fixed re-check solves into its own scratch and copies the selected candidate's back
    // (ValidateFixedConstruction).
    private struct CallEvaluation
    {
        internal InvocationKoto Call;
        internal Koto Callee;
        internal GenericsKoto? Generic;
        internal BindingScope Scope;
        internal BoundType? Expected;
        internal BoundType? Self;
        internal RequirementGroup? Requirements;
        internal SlotFrame[] Frames;
        internal BoundType?[] ExplicitTypes;
        internal BoundLength?[] ExplicitLengths;
        internal int[] Mapping;
        internal bool[] Used;
        internal BoundOrigin[] Origins;
        internal BoundOrigin[] Inputs;
        internal EvaluatedCandidate[] Evaluated;
        internal BoundArgumentOperation[] Operations;
        internal PositionRow[] Rows;
        internal int[] ChildSlices;
        internal bool[] CompletedWaiting;
        internal BoundType?[] WaitingContexts;
        internal int ChildBase;
        internal int PositionBase;
        internal int FrameCount;
        internal int OperationStride;
        internal int OriginSlots;
        internal int InputSlots;
        internal int Count;
        internal int Applicable;
        internal int Winner;
        internal int PendingCount;
        internal bool Pending;
        internal bool PremisesOnly;
        internal bool Error;
        internal bool Unformed; // DECISIONS BU-240: a candidate rests on a Type formed under an unrepresented split (PendingJudgmentKind.UnformedSplit).
        internal bool PremiseReadsSplit; // DECISIONS BU-240 (G812 ruling 141, L2): a Pending candidate's Unknown premise reads the split.
        internal bool RefusedOnSplit; // DECISIONS BU-240 (G812 ruling 141, L1): a candidate refused only through the split (IsSource).
        internal CallableConstraintFact? CallableFailure;
        internal ReferenceConstraintFailure? ConstraintFailure;
        internal PendingJudgment? PendingJudgment;
        internal Koto? IncompleteSignature;
        internal Koto? FailedPendingSignature;
        internal Koto? InvalidDeclaration;

        // Candidate `index`'s argument operations (stride OperationStride: each argument's, then the receiver's).
        internal readonly Span<BoundArgumentOperation> OperationsOf(int index) => this.Operations.AsSpan(index * this.OperationStride, this.OperationStride);

        // Candidate `index`'s root rows (stride: the call's argument count) and child slice (stride 2: start and count, R3).
        internal readonly Span<PositionRow> RowsOf(int index) => this.Rows.AsSpan(index * this.Call.ArgumentNodes.Count, this.Call.ArgumentNodes.Count);

        internal readonly Span<int> ChildSliceOf(int index) => this.ChildSlices.AsSpan(index * 2, 2);

        // Candidate `index`'s argument-to-parameter mapping (stride: the call's argument count).
        internal readonly Span<int> MappingOf(int index) => this.Mapping.AsSpan(index * this.Call.ArgumentNodes.Count, this.Call.ArgumentNodes.Count);

        // Candidate `index`'s Origin slots and input Origins (strides OriginSlots and InputSlots; empty when the call solves no Origins).
        internal readonly Span<BoundOrigin> OriginsOf(int index) => this.Origins.AsSpan(index * this.OriginSlots, this.OriginSlots);

        internal readonly Span<BoundOrigin> InputsOf(int index) => this.Inputs.AsSpan(index * this.InputSlots, this.InputSlots);

        // SPEC 8.4.1, 8.6.4: the requirement reference candidate `index` is tried and selected under: its requirement group's Contract,
        // else a requirement callee's; the trial's input, read from the call (Solution's view, the selected call's plan).
        internal readonly BindingSymbol? RequirementContractOf(int index) => this.Requirements?.Contracts[index] ?? (this.Callee as RequirementCalleeKoto)?.Contract;
    }
}
