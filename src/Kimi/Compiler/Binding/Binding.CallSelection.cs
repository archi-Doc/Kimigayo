// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.2-10.4, 8.4.8.2: candidate evaluation and selection, shared by a call and by a query that must not publish, the omitted base
// selection of an implicit constructor (SPEC 6.3.6). Evaluation records each candidate's applicability and plan facts in the call's
// rented scratch; classification decides the selection from them. Neither fails, completes nor records a node.
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

    // The scratch sizes a candidate set needs. Origins are solved when the caller or the enclosing Type can carry them, or when a
    // candidate declares one, returns one or takes one. Receiver: the group has a receiver shape (SPEC 7.3.2), some candidate taking a
    // receiver (ReceiverIndex), so a value-qualified call acquires its Receiver Expression by the Name before selection.
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
        var arguments = evaluation.Call.ArgumentNodes.Count;
        BoundType? erased = null;
        for (var i = 0; i < evaluation.Count; i++)
        {
            if (evaluation.Evaluated[i] is not { State: not CandidateApplicability.Inapplicable, Symbol.Declaration: FunctionKoto function })
            {
                continue;
            }

            var parameter = evaluation.SavedCandidates != 0 ? evaluation.AllMaps[(i * arguments) + argument] : evaluation.Mapping[argument];
            if (function.Parameters[parameter].Type.BoundType is not { Kind: BoundTypeKind.Function } declared)
            {
                return null;
            }

            erased ??= declared;
        }

        return erased;
    }

    // SPEC 10.1, 10.4, 8.3: whether a selection's classification is the call's own failure, as BindCall records it: no candidate, an
    // ambiguity, or a premise of the candidates not proven (Unproven: Constraints take part in applicability); not UnformedSplit, where
    // BindCall rests (WaitingFails, SelectionOnSplit).
    private static bool SelectionFails(CallSelection kind) => kind is CallSelection.NoneApplicable or CallSelection.Ambiguous or CallSelection.Unproven;

    // SPEC 10.1-10.4: a call's selection at its evaluation's expected Type: every candidate evaluated, from no acquisition or cause fact
    // of an earlier selection, and the selection classified. The one evaluation and classification of a named call (BindCall), of an
    // omitted base clause and of a part that awaits its call; a query that must not publish brackets it (BeginSelectionQuery).
    private (CallSelection Kind, int Winner) EvaluateSelection(ref CallEvaluation evaluation, in FunctionGroup candidates)
    {
        this.acquisitionFailure = null;
        this.failedCause = null;
        this.EvaluateCallCandidates(ref evaluation, candidates);
        return this.ClassifySelection(ref evaluation);
    }

    // A named call's evaluation at `expected` over its group's candidates (their requirements and Self; its explicit arguments are
    // the Generics its method names), and the shape its scratch takes (RentEvaluation).
    private (CallEvaluation Evaluation, CandidateShape Shape) NewEvaluation(
        InvocationKoto call, Koto callee, BindingSymbol group, in FunctionGroup candidates, BindingScope scope, BoundType? expected)
    {
        var self = candidates.Requirements?.Self ?? (callee as RequirementCalleeKoto)?.Self;
        var evaluation = new CallEvaluation
        {
            Call = call,
            Callee = callee,
            Generic = KotoHelper.UnwrapParentheses(call.Method) as GenericsKoto,
            Scope = scope,
            Expected = expected,
            Self = self,
            Requirements = candidates.Requirements,
            Winner = -1,
            PremisesOnly = true,
        };
        return (evaluation, MeasureCandidates(candidates, self is not null || group.Scope.Owner is StructKoto or EnumKoto));
    }

    // SPEC 10.1, 10.4, 10.5.1, 8.3 (G812 rulings 122, 123, 126, 139): a named call's own selection at `expected`, judged in full as
    // BindCall judges it, as a query that does not publish (BeginSelectionQuery): the evaluation BindCall makes (NewEvaluation,
    // RentEvaluation, BindExplicitArguments, EvaluateSelection), its classification (ClassifySelection's outcome, which BindCall
    // dispatches on) and winner. A callee that names no function group, or explicit arguments it does not choose, select nothing to
    // judge (Selected, -1).
    private (CallSelection Kind, int Winner) ProbeSelection(InvocationKoto call, BindingScope scope, BoundType? expected)
    {
        var method = KotoHelper.UnwrapParentheses(call.Method);
        var callee = method is GenericsKoto { Identifier: { } identifier } ? identifier : method;
        if (callee.BoundSymbol is not { Kind: BindingSymbolKind.Function } group)
        {
            return (CallSelection.Selected, -1);
        }

        var candidates = this.GroupOf(callee, group);
        var (evaluation, shape) = this.NewEvaluation(call, callee, group, candidates, scope, expected);
        var query = this.BeginSelectionQuery();
        this.RentEvaluation(ref evaluation, shape);
        try
        {
            return this.BindExplicitArguments(ref evaluation, candidates) ? this.EvaluateSelection(ref evaluation, candidates) : (CallSelection.Selected, -1);
        }
        finally
        {
            this.EndSelectionQuery(query);
            this.ReturnEvaluation(evaluation);
        }
    }

    // The state a selection's evaluation touches beside its scratch: the obligations its trials add, the acquisition and cause facts,
    // the active requirement contract and the inference counters; a query that must not publish saves it and restores it after.
    private SelectionQuery BeginSelectionQuery()
        => new(this.obligations.Count, this.acquisitionFailure, this.failedCause, this.activeRequirementContract, (this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks));

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
    }

    // The scratch of a named call's evaluation (BindCall), sized by its candidates' shape, with the bases of the rows and classes its
    // trials retain; ReturnEvaluation returns it in the reverse order.
    private void RentEvaluation(ref CallEvaluation evaluation, in CandidateShape shape)
    {
        var call = evaluation.Call;
        var arguments = call.ArgumentNodes.Count;
        var explicitCount = evaluation.Generic?.TypeArguments.Count ?? 0;
        var saved = shape.Count > 1 ? shape.Count : 0;
        evaluation.MaxGenerics = shape.Generics;
        evaluation.OriginSlots = shape.SolveOrigins ? shape.Origins : 0;
        evaluation.InputSlots = shape.SolveOrigins ? shape.InputOrigins : 0;
        evaluation.SavedCandidates = saved;
        evaluation.Frame = this.infer.Rent(Math.Max(1, shape.Generics), call);
        evaluation.ExplicitTypes = this.typeScratch.Rent(explicitCount);
        evaluation.ExplicitLengths = this.lengthScratch.Rent(explicitCount);
        evaluation.Mapping = this.indexScratch.Rent(Math.Max(1, arguments));
        evaluation.Used = this.flagScratch.Rent(Math.Max(1, shape.Parameters));
        evaluation.CompletedWaiting = this.flagScratch.Rent(arguments);
        evaluation.WaitingContexts = this.typeScratch.Rent(arguments);
        evaluation.CompletedWaiting.AsSpan(0, arguments).Clear();
        evaluation.Origins = this.originScratch.Rent(evaluation.OriginSlots);
        evaluation.Inputs = this.originScratch.Rent(evaluation.InputSlots);
        evaluation.Evaluated = this.candidateScratch.Rent(shape.Count);
        evaluation.AllTypes = this.typeScratch.Rent(saved * shape.Generics);
        evaluation.AllSelectors = this.typeScratch.Rent(saved * shape.Generics);
        evaluation.AllLengths = this.lengthScratch.Rent(saved * shape.Generics);
        evaluation.AllMaps = this.indexScratch.Rent(saved * arguments);
        evaluation.AllOrigins = this.originScratch.Rent(saved * evaluation.OriginSlots);
        evaluation.AllInputs = this.originScratch.Rent(saved * evaluation.InputSlots);
        evaluation.OperationStride = arguments + 1;
        evaluation.Operations = this.argumentOperationScratch.Rent(shape.Count * evaluation.OperationStride);
        evaluation.Rows = this.rowScratch.Rent(shape.Count * arguments);
        evaluation.ChildSlices = this.indexScratch.Rent(shape.Count * 2);
        evaluation.ChildBase = this.childRows.Count;
        evaluation.ClassBase = this.positionClasses.Count;
    }

    private void ReturnEvaluation(in CallEvaluation evaluation)
    {
        this.argumentOperationScratch.Return(evaluation.Operations, clearArray: true);
        this.rowScratch.Return(evaluation.Rows, clearArray: true);
        this.indexScratch.Return(evaluation.ChildSlices);
        this.childRows.RemoveRange(evaluation.ChildBase, this.childRows.Count - evaluation.ChildBase); // R3: the evaluation's retained rows end with it.
        this.positionClasses.RemoveRange(evaluation.ClassBase, this.positionClasses.Count - evaluation.ClassBase);
        this.originScratch.Return(evaluation.AllInputs, clearArray: true);
        this.originScratch.Return(evaluation.AllOrigins, clearArray: true);
        this.indexScratch.Return(evaluation.AllMaps);
        this.typeScratch.Return(evaluation.AllSelectors, clearArray: true);
        this.typeScratch.Return(evaluation.AllTypes, clearArray: true);
        this.lengthScratch.Return(evaluation.AllLengths, clearArray: true);
        this.lengthScratch.Return(evaluation.ExplicitLengths, clearArray: true);
        this.candidateScratch.Return(evaluation.Evaluated, clearArray: true);
        this.originScratch.Return(evaluation.Inputs, clearArray: true);
        this.originScratch.Return(evaluation.Origins, clearArray: true);
        this.typeScratch.Return(evaluation.WaitingContexts, clearArray: true);
        this.typeScratch.Return(evaluation.ExplicitTypes, clearArray: true);
        this.flagScratch.Return(evaluation.CompletedWaiting);
        this.flagScratch.Return(evaluation.Used);
        this.indexScratch.Return(evaluation.Mapping);
        this.infer.Return(evaluation.Frame);
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
        var operations = evaluation.Operations.AsSpan(index * evaluation.OperationStride, evaluation.OperationStride);
        operations.Clear();
        evaluation.Rows.AsSpan(index * call.ArgumentNodes.Count, call.ArgumentNodes.Count).Clear(); // F1: each candidate writes its own root rows.
        evaluation.ChildSlices[(index * 2) + 1] = -1; // R3: composed at the candidate's first solve.
        var declaringType = evaluation.Self is null ? this.CallDeclaringType(callee, candidate) : null;
        var state = CandidateApplicability.Inapplicable;
        evaluation.Frame.ClearTypes(CallSlotCount(function));
        if (this.MeasureCallInference)
        {
            this.inferenceMappings++;
        }

        var argumentMap = MapCallArguments(call, function, this.CallReceiver(evaluation.Generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method)) is not null, evaluation.Mapping, evaluation.Used);
        var defaultsUsed = argumentMap.DefaultsUsed;
        ClosureReceiverRefutation? closureReceiver = null;
        var unsolved = false;
        var independentRejection = false;
        var premiseUnknown = false;
        ReferenceConstraintFailure? rejectedConstraint = null;
        // SPEC 4.6.2: a synthesized range construction pins its Kimi target, which source access does not restrict.
        var accessible = callee is SyntheticKoto || this.Accessible(candidate, scope, receiverType: this.CallReceiver(callee)?.BoundType);
        if (accessible)
        {
            this.BindHeader(candidate);
            if (function.IsConstructor)
            {
                declaringType = argumentMap.Valid ? this.ConstructorType(call, function, scope, evaluation.Mapping, evaluation.Expected) : null;
            }

            this.activeRequirementContract = evaluation.Requirements?.Contracts[index] ?? (callee as RequirementCalleeKoto)?.Contract;
            state = this.TryCandidate(
                this.Solution(evaluation, index, function, declaringType),
                evaluation.Mapping,
                argumentMap,
                evaluation.Expected,
                operations,
                out defaultsUsed,
                out unsolved,
                out closureReceiver,
                out independentRejection,
                out rejectedConstraint,
                out premiseUnknown);
            this.activeRequirementContract = null;
        }

        if (function.IsConstructor && declaringType is not null)
        {
            declaringType = this.ConstructionType(function, declaringType, evaluation.Frame.Types) ?? declaringType;
        }

        evaluation.Evaluated[index] = new(candidate, state, declaringType, defaultsUsed, unsolved, closureReceiver, argumentMap, accessible, function.IsConstructor && StableConstructionPremises(state, unsolved, declaringType, operations), independentRejection, rejectedConstraint, premiseUnknown);
        var saved = evaluation.SavedCandidates != 0;
        var argumentCount = call.ArgumentNodes.Count;
        if (saved)
        {
            evaluation.Mapping.AsSpan(0, argumentCount).CopyTo(evaluation.AllMaps.AsSpan(index * argumentCount));
        }

        // The trial's judgment is taken once, in the selection's frame (DECISIONS BU-86); an unformed split (D3) only of a candidate that
        // rests on it (Pending; TryCandidate's verdict), as an environment result.
        var judgment = this.TakeTrialPending(call);
        evaluation.Unformed |= state == CandidateApplicability.Pending && judgment is { Kind: PendingJudgmentKind.UnformedSplit };
        evaluation.Pending |= state == CandidateApplicability.Pending;
        if (state == CandidateApplicability.Pending)
        {
            // A failed signature part or explicit Type argument leaves the selection resting on it (SPEC 23.1.2).
            evaluation.FailedPendingSignature ??= IncompleteSignature(function) ?? this.FailedSignaturePart(function) ??
                (judgment is { Kind: PendingJudgmentKind.FailedTypeArgument } failed ? failed.Part : null);

            // SPEC 8.3, 15.6.1: a Callable proof that is Unknown only in its Origin part is explained by its Constraint record.
            evaluation.PendingCount++;
            evaluation.PremisesOnly &= premiseUnknown;
            evaluation.CallableFailure ??= function.TypeConstraints.Count != 0 ? this.CallableOriginFailure(call, function, evaluation.Mapping) : null;
            evaluation.ConstraintFailure ??= rejectedConstraint;
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
        if (saved)
        {
            evaluation.Frame.Types[..CallSlotCount(function)].CopyTo(evaluation.AllTypes.AsSpan(index * evaluation.MaxGenerics));
            evaluation.Frame.Selectors[..CallSlotCount(function)].CopyTo(evaluation.AllSelectors.AsSpan(index * evaluation.MaxGenerics));
            evaluation.Frame.Lengths[..CallSlotCount(function)].CopyTo(evaluation.AllLengths.AsSpan(index * evaluation.MaxGenerics));
            evaluation.Origins.AsSpan(0, evaluation.OriginSlots).CopyTo(evaluation.AllOrigins.AsSpan(index * evaluation.OriginSlots));
            evaluation.Inputs.AsSpan(0, evaluation.InputSlots).CopyTo(evaluation.AllInputs.AsSpan(index * evaluation.InputSlots));
        }
    }

    // The selection the evaluated candidates determine. A failed candidate declaration or signature leaves the selection resting on
    // that failure (SPEC 23.1.2); an Unknown premise defers it only when it can affect it (SPEC 8.3.3). A candidate unproven through an
    // unformed split (DECISIONS D3, temporary, PLAN G886: never premise-only, unrankable) makes the selection UnformedSplit: the call is
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

    // DECISIONS D3 (temporary, PLAN G886), SPEC 7.7.4, 23.1.2 (C-359 C2): a call whose selection rests on an unformed split is one located
    // Unsupported_Kd. A Function Item or reference part that every candidate a case can still select declares at a common Function Type
    // (ErasedByEveryCandidate) is erased whichever of them a case selects, so its environment is still judged, once, as a conversion's
    // is (FailErasedOwned; a reference part by its written arguments, FailUnformedOwned): the erasure comes after a selection that
    // the split leaves open, and its Owned proof reads no unformed Type. The selection is first judged in each combination of split
    // cases (SelectionOnSplit, G812 ruling 139): where it fails in every one the call keeps its own record, the canonical run's
    // classification, as BindCall records it with no evaluation to explain it (FailWaitingSelection). Each anonymous function awaiting
    // the call in a part's tree is completed as a value call's part on the split completes it (CompleteSplitPart, G812 rulings 134,
    // 136), at the Type the trial of the candidate the canonical run selects (SelectedCandidate) forms for its position in the
    // canonical case, as SlotSolution.Type forms a parameter.
    private BoundType? FailUnformedCall(ref CallEvaluation evaluation)
    {
        var call = evaluation.Call;
        var (fails, canonical) = this.SelectionOnSplit(in evaluation);
        if (fails)
        {
            var failure = canonical.Kind == CallSelection.Ambiguous ? BindingFailure.Ambiguous
                : canonical.Kind == CallSelection.Unproven ? BindingFailure.UnprovenConstraint : BindingFailure.NoApplicableCandidate;
            return this.FailWaitingSelection(call, failure);
        }

        var shaped = canonical.Kind == CallSelection.Selected ? this.SelectedCandidate(ref evaluation, canonical.Winner) : -1;
        for (var a = 0; a < call.ArgumentNodes.Count; a++)
        {
            var part = call.ArgumentNodes[a];
            if (shaped >= 0 && evaluation.Evaluated[shaped] is { Symbol.Declaration: FunctionKoto function } candidate &&
                function.Parameters[evaluation.Mapping[a]].Type.BoundType is { } declared)
            {
                var trial = (function, evaluation.Frame, evaluation.Scope, evaluation.Self, evaluation.Origins, evaluation.Inputs, candidate.DeclaringType);
                _ = this.CompleteSplitPart(
                    part,
                    declared,
                    trial,
                    static (binding, s, type) => binding.CallType(type, s.function, s.Frame.Types, s.Scope, s.Self, s.Origins, s.Inputs, s.DeclaringType, s.Frame.Lengths),
                    evaluation.Scope);
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

        return this.Fail(call, BindingFailure.Unsupported, true);
    }

    // DECISIONS D3 with rule W (G812 ruling 139; temporary with it): a named call's selection judged in each combination of split
    // cases (RestsOnSplitCases), each run a query (ProbeSelection), the whole selection one part: whether it fails in every combination
    // (no candidate, an ambiguity or an unproven premise: where BindCall records the call's own failure), and the canonical run's
    // classification and winner (the first run's, each binding at its first admitted Semantics; ruling 130).
    private (bool Fails, (CallSelection Kind, int Winner) Canonical) SelectionOnSplit(in CallEvaluation evaluation)
    {
        var enclosing = this.canonicalSelection;
        this.canonicalSelection = null;
        var rests = this.RestsOnSplitCases(this.unformedWrites, (evaluation.Call, evaluation.Scope, evaluation.Expected), static (binding, check) =>
        {
            var selection = binding.ProbeSelection(check.Call, check.Scope, check.Expected);
            binding.canonicalSelection ??= selection;
            return SelectionFails(selection.Kind);
        });
        var canonical = this.canonicalSelection ?? (CallSelection.UnformedSplit, -1);
        this.canonicalSelection = enclosing;
        return (!rests, canonical);
    }

    // DECISIONS D3 (G812 rulings 134, 139): candidate `index` (the canonical run's winner), whose trial's frame and mapping the
    // evaluation then holds: the last trial's, so a candidate evaluated before another is evaluated again alone, as a query that
    // publishes nothing (BeginSelectionQuery); the evaluation's counts stay its selection's. -1 for none.
    private int SelectedCandidate(ref CallEvaluation evaluation, int index)
    {
        if (index < 0 || index == evaluation.Count - 1)
        {
            return index;
        }

        var (query, saved, selection) = (this.BeginSelectionQuery(), evaluation, this.OpenFrame());
        this.candidateTrials++;
        try
        {
            evaluation.Count = index;
            this.EvaluateCallCandidate(ref evaluation, saved.Evaluated[index].Symbol!);
            return index;
        }
        finally
        {
            this.candidateTrials--;
            this.CloseFrame(selection);
            this.EndSelectionQuery(query);
            evaluation = saved; // The rented frame, mapping and rows keep the trial's contents.
        }
    }

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
        int Obligations, AcquisitionFailure? Acquisition, ICheckCause? Cause, BindingSymbol? Contract, (long, long, long) Counters);

    // The inputs and rented scratch of one call's candidate evaluation, and the facts it accumulates for the selection. The saved
    // arrays retain each applicable candidate's slots and mapping only when several candidates are compared.
    private struct CallEvaluation
    {
        internal InvocationKoto Call;
        internal Koto Callee;
        internal GenericsKoto? Generic;
        internal BindingScope Scope;
        internal BoundType? Expected;
        internal BoundType? Self;
        internal RequirementGroup? Requirements;
        internal SlotFrame Frame;
        internal BoundType?[] ExplicitTypes;
        internal BoundLength?[] ExplicitLengths;
        internal int[] Mapping;
        internal bool[] Used;
        internal bool[] CompletedWaiting;
        internal BoundType?[] WaitingContexts;
        internal BoundOrigin[] Origins;
        internal BoundOrigin[] Inputs;
        internal EvaluatedCandidate[] Evaluated;
        internal BoundArgumentOperation[] Operations;
        internal PositionRow[] Rows;
        internal int[] ChildSlices;
        internal int OperationStride;
        internal int SavedCandidates;
        internal BoundType?[] AllTypes;
        internal BoundType?[] AllSelectors;
        internal BoundLength?[] AllLengths;
        internal int[] AllMaps;
        internal BoundOrigin[] AllOrigins;
        internal BoundOrigin[] AllInputs;
        internal int MaxGenerics;
        internal int OriginSlots;
        internal int InputSlots;
        internal int ChildBase;
        internal int ClassBase;
        internal int Count;
        internal int Applicable;
        internal int Winner;
        internal int PendingCount;
        internal bool Pending;
        internal bool PremisesOnly;
        internal bool Error;
        internal bool Unformed; // DECISIONS D3: a candidate rests on a Type formed under an unrepresented split (PendingJudgmentKind.UnformedSplit).
        internal CallableConstraintFact? CallableFailure;
        internal ReferenceConstraintFailure? ConstraintFailure;
        internal PendingJudgment? PendingJudgment;
        internal Koto? IncompleteSignature;
        internal Koto? FailedPendingSignature;
        internal Koto? InvalidDeclaration;
    }
}
