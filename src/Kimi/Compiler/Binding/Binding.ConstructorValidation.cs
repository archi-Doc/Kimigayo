// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, CallInferenceFailure>? callInferenceFailures;

    // Test/measurement switch for the full reference path. It never changes language behavior.
    internal bool UseConstructorReferenceCheck { get; set; }

    // A failed typed-input obligation with no slot, projection, length or Origin premise is
    // independent of inference mode. Other rejections must be revisited, even after a partial fit.
    private static bool IndependentInputPattern(BoundType type)
    {
        if (type.CarriesOrigin || type.LengthExpression is not null || type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection)
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!IndependentInputPattern(type.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StableConstructionPremises(CandidateApplicability state, bool unsolved, BoundType? construction, ReadOnlySpan<BoundArgumentOperation> operations)
    {
        if (state != CandidateApplicability.Applicable || unsolved || construction is null || construction.CarriesOrigin)
        {
            return false;
        }

        foreach (var operation in operations)
        {
            if (operation.Source is null)
            {
                continue;
            }

            // A complete, exact input has no literal/default or waiting-context dependency.
            // Its checked acquisition and constraints remain valid at the same fixed bindings.
            if (operation.Source.BoundType is not { } actual || operation.ParameterType is not { } required ||
                operation.Adaptation != ArgumentAdaptation.Exact || !ReferenceEquals(actual, required))
            {
                return false;
            }
        }

        return true;
    }

    private enum ConstructionCheck : byte
    {
        ChangedSelection,
        WaitingContext,
        UnprovenCandidate,
        AcquisitionCorrelation,
    }

    private readonly record struct CallInferenceFailure(FunctionKoto Selected, FunctionKoto? Other, BoundType? Construction, ConstructionCheck Check, int Slot = -1, BoundType? LeftBinding = null, BoundType? RightBinding = null);

    private static bool ContainsUnspellableType(BoundType type)
    {
        if (type.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (ContainsUnspellableType(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The reference check restores the original declaration group, including inference-rejected
    // candidates. Syntax mappings are unchanged; only binding-dependent applicability is rerun.
    private bool ValidateFixedConstruction(InvocationKoto call, BindingScope scope, BoundType? expected, BoundType construction, ReadOnlySpan<EvaluatedCandidate> original, int selected, int[] allMaps, int[] mapping, BoundType?[] arguments, BoundLength?[] lengths, BoundOrigin[] origins, BoundOrigin[] inputs, BoundOrigin[] allOrigins, BoundOrigin[] allInputs, int originStride, int inputStride, BoundArgumentOperation[] operations, int stride, bool[] completedWaiting, BoundType?[] waitingContexts)
    {
        var count = original.Length;
        var argumentCount = call.ArgumentNodes.Count;
        var checkedCandidates = this.candidateScratch.Rent(count);
        var checkedOperations = this.argumentOperationScratch.Rent(count * stride);
        var selectedFunction = (FunctionKoto)original[selected].Symbol!.Declaration;
        FunctionKoto? unproven = null;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var previous = original[i];
                var function = (FunctionKoto)previous.Symbol!.Declaration;
                var candidateOperations = checkedOperations.AsSpan(i * stride, stride);
                candidateOperations.Clear();
                if (!previous.ArgumentMap.Valid || !previous.Accessible || (!this.UseConstructorReferenceCheck && previous.IndependentRejection))
                {
                    if (this.MeasureCallInference)
                    {
                        this.inferenceFixedReuses++;
                    }

                    checkedCandidates[i] = previous with { State = CandidateApplicability.Inapplicable };
                    continue;
                }

                if (!this.UseConstructorReferenceCheck && previous.StableConstruction && ReferenceEquals(previous.DeclaringType, construction))
                {
                    if (this.MeasureCallInference)
                    {
                        this.inferenceFixedReuses++;
                    }

                    checkedCandidates[i] = previous;
                    operations.AsSpan(i * stride, stride).CopyTo(candidateOperations);
                    continue;
                }

                if (count > 1)
                {
                    allMaps.AsSpan(i * argumentCount, argumentCount).CopyTo(mapping);
                }

                if (this.MeasureCallInference)
                {
                    this.inferenceFixedChecks++;
                }

                var state = this.TryCandidate(call, function, null, scope, arguments, lengths, [], mapping, previous.ArgumentMap, expected, null, origins, inputs, construction, candidateOperations, out var defaults, out var unbound, out var receiver, out _, out _, out var premiseUnknown, fixedConstruction: true);
                checkedCandidates[i] = new(previous.Symbol, state, construction, defaults, unbound, receiver, previous.ArgumentMap, PremiseUnknown: premiseUnknown);
                if ((state == CandidateApplicability.Pending && !premiseUnknown) || state == CandidateApplicability.Error || unbound)
                {
                    return Reject(function, ConstructionCheck.UnprovenCandidate);
                }

                unproven ??= premiseUnknown ? function : null;

                if (state is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                {
                    for (var a = 0; a < argumentCount; a++)
                    {
                        if (!completedWaiting[a])
                        {
                            continue;
                        }

                        var pattern = function.Parameters[mapping[a]].Type.BoundType!;
                        var contextPattern = WasWaitingNestedCall(call.ArgumentNodes[a]) ? pattern : this.ExpectedCallSignature(function, pattern);
                        var context = contextPattern is null ? null : this.CallType(contextPattern, function, arguments, scope, null, origins, inputs, construction, lengths);
                        var usedContext = waitingContexts[a];
                        if (!ReferenceEquals(context, usedContext) && (context is null || usedContext is null || !FitsType(context, usedContext) || !FitsType(usedContext, context)))
                        {
                            return Reject(function, ConstructionCheck.WaitingContext);
                        }
                    }
                }

                if (state == CandidateApplicability.Waiting)
                {
                    return Reject(function, ConstructionCheck.WaitingContext);
                }

                if (count > 1 && i == selected)
                {
                    origins.AsSpan(0, originStride).CopyTo(allOrigins.AsSpan(i * originStride));
                    inputs.AsSpan(0, inputStride).CopyTo(allInputs.AsSpan(i * inputStride));
                }
            }

            // SPEC 8.4.8.2: as at the first selection, an Unknown premise rejects the fixed construction only when it can affect it.
            if (unproven is not null && !this.PremisesCannotAffect(call, checkedCandidates.AsSpan(0, count), checkedOperations, stride))
            {
                return Reject(unproven, ConstructionCheck.UnprovenCandidate);
            }

            var winner = this.SelectBest(checkedCandidates.AsSpan(0, count), checkedOperations, stride);
            if (winner != selected)
            {
                var other = winner;
                for (var i = 0; other < 0 && i < count; i++)
                {
                    if (i != selected && checkedCandidates[i].State == CandidateApplicability.Applicable)
                    {
                        other = i;
                    }
                }

                return Reject(other < 0 ? null : (FunctionKoto)original[other].Symbol!.Declaration, ConstructionCheck.ChangedSelection);
            }

            checkedOperations.AsSpan(selected * stride, stride).CopyTo(operations.AsSpan(selected * stride, stride));
            this.InitializeCallSlots(call, selectedFunction, construction, arguments, fixedConstruction: true);
            if (count > 1)
            {
                allMaps.AsSpan(selected * argumentCount, argumentCount).CopyTo(mapping);
                allOrigins.AsSpan(selected * originStride, originStride).CopyTo(origins);
                allInputs.AsSpan(selected * inputStride, inputStride).CopyTo(inputs);
            }

            return true;
        }
        finally
        {
            this.argumentOperationScratch.Return(checkedOperations, clearArray: true);
            this.candidateScratch.Return(checkedCandidates, clearArray: true);
        }

        bool Reject(FunctionKoto? other, ConstructionCheck check)
        {
            this.FailExplained(ref this.callInferenceFailures, call, BindingFailure.ConstructorSelectionChanged, new CallInferenceFailure(selectedFunction, other, construction, check), true);
            return false;
        }
    }

    private void ReportCallInferenceFailure(Koto node, DiagnosticRequirement requirement, DiagnosticCode code, CallInferenceFailure fact)
    {
        var correlation = fact.Check == ConstructionCheck.AcquisitionCorrelation;
        (string Role, Koto At, string? Label)[] related = fact.Other is { } other && !ReferenceEquals(fact.Selected, other)
            ? [("declaration", fact.Selected, correlation ? "Group contract" : "Tentative selection"), ("declaration", other, correlation ? "Corresponding declaration" : "Fixed-binding check")]
            : [("declaration", fact.Selected, "Tentative selection")];
        var note = fact.Check switch
        {
            ConstructionCheck.WaitingContext => "The fixed-binding check needs a different or additional waiting-argument context; the argument is never analyzed again to select another constructor (SPEC 10.8.1)",
            ConstructionCheck.UnprovenCandidate => "The original constructor group cannot be fully judged with the inferred construction Type fixed (SPEC 10.8.1)",
            ConstructionCheck.AcquisitionCorrelation => "The group contract is checked before applicability filtering; independent inputs and fixed bindings do not establish the required shared acquisition (SPEC 7.3.1)",
            _ => "The original constructor group, including inference-rejected candidates, no longer has the same unique Best Candidate when the construction Type is fixed (SPEC 10.8.1)",
        };
        object[] evidence = correlation
            ? fact.LeftBinding is { } left && fact.RightBinding is { } right
                ? [CallOwnSlots(fact.Selected)[fact.Slot].Identifier, DiagnosticTypeName(left), DiagnosticTypeName(right)]
                : [CallOwnSlots(fact.Selected)[fact.Slot].Identifier]
            : [DiagnosticTypeName(fact.Construction!)];
        node.Report(requirement, code, evidence: evidence, related: related, note: note);
    }
}
