// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.2-10.4, 8.4.8.2: candidate evaluation and selection, shared by a call and by a query that must not publish, the omitted base
// selection of an implicit constructor (SPEC 6.2.3.6). Evaluation records each candidate's applicability and plan facts in the call's
// rented scratch; classification decides the selection from them. Neither fails, completes nor records a node.
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

    // The scratch sizes a candidate set needs. Origins are solved when the caller or the enclosing Type can carry them, or when a
    // candidate declares one, returns one or takes one.
    private static CandidateShape MeasureCandidates(CallCandidates candidates, bool solveOrigins)
    {
        var shape = new CandidateShape { SolveOrigins = solveOrigins };
        foreach (var candidate in candidates)
        {
            if (candidate.Declaration is FunctionKoto function)
            {
                shape.Count++;
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
        evaluation.BoundStarts[index] = this.candidateBounds.Count;
        operations.Clear();
        var declaringType = evaluation.Self is null ? this.CallDeclaringType(callee, candidate) : null;
        var state = CandidateApplicability.Inapplicable;
        evaluation.Scratch.AsSpan(0, CallSlotCount(function)).Clear();
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
        // SPEC 4.6.3: a synthesized range construction pins its Kimi target, which source access does not restrict.
        var accessible = callee is SyntheticKoto || this.Accessible(candidate, scope, receiverType: this.CallReceiver(callee)?.BoundType);
        if (accessible)
        {
            this.BindHeader(candidate);
            if (function.IsConstructor)
            {
                declaringType = argumentMap.Valid ? this.ConstructorType(call, function, scope, evaluation.Mapping) : null;
            }

            this.activeRequirementContract = evaluation.Requirements?.Contracts[index] ?? (callee as RequirementCalleeKoto)?.Contract;
            state = this.TryCandidate(call, function, evaluation.Generic, scope, evaluation.Scratch, evaluation.LengthArguments, evaluation.ExplicitLengths, evaluation.Mapping, argumentMap, evaluation.Expected, evaluation.Self, evaluation.Origins, evaluation.Inputs, declaringType, operations, out defaultsUsed, out unsolved, out closureReceiver, out independentRejection, out rejectedConstraint, out premiseUnknown);
            this.activeRequirementContract = null;
        }

        if (function.IsConstructor && declaringType is not null)
        {
            declaringType = this.ConstructionType(function, declaringType, evaluation.Scratch) ?? declaringType;
        }

        evaluation.Evaluated[index] = new(candidate, state, declaringType, defaultsUsed, unsolved, closureReceiver, argumentMap, accessible, function.IsConstructor && StableConstructionPremises(state, unsolved, declaringType, operations), independentRejection, rejectedConstraint, premiseUnknown);
        var saved = evaluation.SavedCandidates != 0;
        var argumentCount = call.ArgumentNodes.Count;
        if (saved)
        {
            evaluation.Mapping.AsSpan(0, argumentCount).CopyTo(evaluation.AllMaps.AsSpan(index * argumentCount));
        }

        evaluation.Pending |= state == CandidateApplicability.Pending;
        evaluation.FailedPendingSignature ??= state == CandidateApplicability.Pending ? IncompleteSignature(function) ?? this.FailedSignaturePart(function) : null;
        if (state == CandidateApplicability.Pending)
        {
            // SPEC 8.7, 15.6.1: a Callable proof that is Unknown only in its Origin part is explained by its Constraint record.
            evaluation.PendingCount++;
            evaluation.PremisesOnly &= premiseUnknown;
            evaluation.CallableFailure ??= function.TypeConstraints.Count != 0 ? this.CallableOriginFailure(call, function, evaluation.Scratch, evaluation.LengthArguments, evaluation.Mapping, scope, evaluation.Self, declaringType) : null;
            evaluation.ConstraintFailure ??= this.CandidateConstraintFailure(function, evaluation.Scratch, evaluation.LengthArguments, scope, evaluation.Self, declaringType, ConstraintProof.Unknown);
        }

        evaluation.Error |= state == CandidateApplicability.Error;
        evaluation.InvalidDeclaration ??= state == CandidateApplicability.Error ? InvalidDeclarationContextCause(function) : null;
        if (state is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
        {
            evaluation.IncompleteSignature ??= state == CandidateApplicability.Inapplicable ? IncompleteSignature(function) : null;
            return;
        }

        evaluation.Applicable++;
        evaluation.Winner = index;
        if (saved)
        {
            evaluation.Scratch.AsSpan(0, CallSlotCount(function)).CopyTo(evaluation.AllTypes.AsSpan(index * evaluation.MaxGenerics));
            evaluation.LengthArguments.AsSpan(0, CallSlotCount(function)).CopyTo(evaluation.AllLengths.AsSpan(index * evaluation.MaxGenerics));
            evaluation.Origins.AsSpan(0, evaluation.OriginSlots).CopyTo(evaluation.AllOrigins.AsSpan(index * evaluation.OriginSlots));
            evaluation.Inputs.AsSpan(0, evaluation.InputSlots).CopyTo(evaluation.AllInputs.AsSpan(index * evaluation.InputSlots));
        }
    }

    // The selection the evaluated candidates determine. A failed candidate declaration or signature leaves the selection resting on
    // that failure (SPEC 23.3.6.4); an Unknown premise defers it only when it can affect it (SPEC 8.4.8.2).
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
                comparable = ComparableCallableSlots(evaluation.Call, candidates, evaluation.Operations, evaluation.OperationStride);
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
    }

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
        internal BoundType?[] Scratch;
        internal BoundLength?[] LengthArguments;
        internal BoundLength?[] ExplicitLengths;
        internal int[] Mapping;
        internal bool[] Used;
        internal BoundOrigin[] Origins;
        internal BoundOrigin[] Inputs;
        internal EvaluatedCandidate[] Evaluated;
        internal BoundArgumentOperation[] Operations;
        internal int OperationStride;
        internal int[] BoundStarts;
        internal int SavedCandidates;
        internal BoundType?[] AllTypes;
        internal BoundLength?[] AllLengths;
        internal int[] AllMaps;
        internal BoundOrigin[] AllOrigins;
        internal BoundOrigin[] AllInputs;
        internal int MaxGenerics;
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
        internal Koto? IncompleteSignature;
        internal Koto? FailedPendingSignature;
        internal Koto? InvalidDeclaration;
    }
}
