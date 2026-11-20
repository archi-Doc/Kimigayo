// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.2-10.4, 8.4.8.2: candidate evaluation and selection, shared by a call and by a query that must not publish, the omitted base
// selection of an implicit constructor (SPEC 6.3.6). Each candidate's accessibility is decided and its header bound before the set is
// measured (RentCandidates); evaluation records each candidate's applicability and plan facts in the call's rented scratch;
// classification decides the selection from them. Neither fails, completes nor records a node.
public sealed partial class Binding
{
    private enum CallSelection : byte
    {
        Selected,
        InvalidDeclaration,
        FailedSignature,
        Unproven,
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
        var accessible = evaluation.Evaluated[index].Accessible; // Decided once, and the header bound, before the set was measured.
        if (accessible)
        {
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
            declaringType = this.ConstructionType(function, declaringType, evaluation.Frames[index].Types) ?? declaringType;
        }

        // The refusal the trial held (the owner's O-335 (a)): settled only if this candidate is selected (Binding.Calls).
        var held = this.TakeTrialRefusal(call);
        ref var row = ref evaluation.Evaluated[index]; // Its Symbol and Accessible are RentCandidates'.
        row.State = state;
        row.DeclaringType = declaringType;
        row.DefaultsUsed = defaultsUsed;
        row.Unsolved = unsolved;
        row.ClosureReceiver = closureReceiver;
        row.ArgumentMap = argumentMap;
        row.StableConstruction = function.IsConstructor && StableConstructionPremises(state, unsolved, declaringType, operations);
        row.IndependentRejection = independentRejection;
        row.ConstraintFailure = rejectedConstraint;
        row.PremiseUnknown = premiseUnknown;
        row.HeldRefusal = held;
        var saved = evaluation.SavedCandidates != 0;
        var argumentCount = call.ArgumentNodes.Count;
        if (saved)
        {
            evaluation.Mapping.AsSpan(0, argumentCount).CopyTo(evaluation.AllMaps.AsSpan(index * argumentCount));
        }

        evaluation.Pending |= state == CandidateApplicability.Pending;
        if (state == CandidateApplicability.Pending)
        {
            // A failed signature part or explicit Type argument leaves the selection resting on it (SPEC 23.1.2); the trial's judgment
            // before its proof is taken once, in the selection's frame (DECISIONS BU-86).
            var judgment = this.TakeTrialPending(call);
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
            evaluation.Origins.AsSpan(0, evaluation.OriginSlots).CopyTo(evaluation.AllOrigins.AsSpan(index * evaluation.OriginSlots));
            evaluation.Inputs.AsSpan(0, evaluation.InputSlots).CopyTo(evaluation.AllInputs.AsSpan(index * evaluation.InputSlots));
        }
    }

    // The selection the evaluated candidates determine. A failed candidate declaration or signature leaves the selection resting on
    // that failure (SPEC 23.1.2); an Unknown premise defers it only when it can affect it (SPEC 8.3.3).
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
            return (evaluation.FailedPendingSignature is null ? CallSelection.Unproven : CallSelection.FailedSignature, -1);
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

    // The inputs and rented scratch of one call's candidate evaluation, and the facts it accumulates for the selection. Each candidate's
    // slots are its own frame (Frames, R7-I2 c); the saved arrays retain each applicable candidate's mapping, Origins and input Origins
    // only when several candidates are compared.
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
        internal int OperationStride;
        internal int SavedCandidates;
        internal int[] AllMaps;
        internal BoundOrigin[] AllOrigins;
        internal BoundOrigin[] AllInputs;
        internal int OriginSlots;
        internal int InputSlots;
        internal int Count;
        internal int Applicable;
        internal int Winner;
        internal int PendingCount;
        internal bool Pending;
        internal bool PremisesOnly;
        internal bool Error;
        internal CallableConstraintFact? CallableFailure;
        internal ReferenceConstraintFailure? ConstraintFailure;
        internal PendingJudgment? PendingJudgment;
        internal Koto? IncompleteSignature;
        internal Koto? FailedPendingSignature;
        internal Koto? InvalidDeclaration;
    }
}
