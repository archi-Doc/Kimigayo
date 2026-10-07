// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<CallableCandidate> callableCandidateScratch = new();
    private readonly Dictionary<Koto, (int Start, int Count, int Position, AcquisitionMode Mode, AcquisitionMode Other)> callableSelectionFailures = new(ReferenceEqualityComparer.Instance);
    private readonly List<CallableCandidate> callableSelectionParties = [];

    // Whole-contract equality includes fixed Origins and alpha-equivalent per-call binders, not just the storage Type.
    private static bool SameCallableSignature(BoundType a, BoundType b)
        => ReferenceEquals(a, b) || (CallableSignatureFits(a, b, FunctionTypeBinder(a)) && CallableSignatureFits(b, a, FunctionTypeBinder(b)));

    private static bool SameProofPremise(BoundConstraint a, BoundConstraint b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Kind == ConstraintKind.Not && b.Kind == ConstraintKind.Not)
        {
            return SameProofPremise(a.Left!, b.Left!);
        }

        return a.Kind == ConstraintKind.Callable && b.Kind == ConstraintKind.Callable && a.Mask == b.Mask && ReferenceEquals(a.Subject, b.Subject) && SameCallableSignature(a.RequiredType!, b.RequiredType!);
    }

    private static SemanticsKind CallableReceiver(SemanticsMask mask)
        => mask == SemanticsMask.Ref ? SemanticsKind.Ref : mask == SemanticsMask.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;

    private static SemanticsKind WeakestReceiver(SemanticsKind a, SemanticsKind b)
        => a == SemanticsKind.Ref || b == SemanticsKind.Ref ? SemanticsKind.Ref : a == SemanticsKind.Uniq || b == SemanticsKind.Uniq ? SemanticsKind.Uniq : SemanticsKind.Owner;

    private BoundType CallableContractType(BoundType type, BindingScope scope)
        => this.SubstituteIdentityPremises(this.ContractType(type, scope), scope);

    private BoundType? SelectValueCall(InvocationKoto call, BindingScope scope, BoundType type, BoundType? expected)
    {
        var owner = this.CallableContractType(type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type, scope);
        var capacity = 0;
        for (var current = scope; current is not null; current = current.Parent)
        {
            capacity += current.Constraints?.Facts.Count ?? 0;
        }

        var candidates = this.callableCandidateScratch.Rent(capacity);
        var count = 0;
        try
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact.Kind != ConstraintKind.Callable || !ReferenceEquals(this.CallableContractType(fact.Subject!, scope), owner) || !this.AvailableConstraintFact(environment, fact))
                    {
                        continue;
                    }

                    var signature = this.CallableContractType(fact.RequiredType!, scope);
                    var receiver = CallableReceiver(fact.Mask);
                    var index = 0;
                    while (index < count && !SameCallableSignature(candidates[index].Signature, signature))
                    {
                        index++;
                    }

                    if (index == count)
                    {
                        candidates[count++] = new(signature, receiver, fact, current.Owner);
                    }
                    else
                    {
                        candidates[index] = candidates[index] with { Receiver = WeakestReceiver(candidates[index].Receiver, receiver) };
                    }
                }
            }

            var choices = candidates.AsSpan(0, count);
            for (var a = 0; a < count; a++)
            {
                for (var b = 0; b < a; b++)
                {
                    if (candidates[a].Receiver != candidates[b].Receiver)
                    {
                        return this.FailCallableCandidates(call, choices, BindingFailure.ReceiverShapeMismatch);
                    }

                    var left = candidates[a].Signature.Components[0].Components;
                    var right = candidates[b].Signature.Components[0].Components;
                    for (var p = 0; p < Math.Min(left.Count, right.Count); p++)
                    {
                        if (!ReferenceEquals(left[p], right[p]) && this.ParameterShapesConflict(left[p], right[p], this.ShapeOf(left[p], scope.Owner, true), this.ShapeOf(right[p], scope.Owner, true), out var mode, out var other, out _, out _))
                        {
                            return this.FailCallableCandidates(call, choices, BindingFailure.ParameterShapeMismatch, p, mode, other);
                        }
                    }
                }
            }

            if (call.Method is GenericsKoto)
            {
                return this.FailCallableCandidates(call, choices, BindingFailure.NoApplicableCandidate);
            }

            if (!this.PrepareCallArguments(call, scope, callableContext: true))
            {
                return Complete(call, null);
            }

            var stride = call.ArgumentNodes.Count + 1;
            var operations = this.argumentOperationScratch.Rent(count * stride);
            var evaluated = this.candidateScratch.Rent(count);
            try
            {
                var applicable = 0;
                for (var i = 0; i < count; i++)
                {
                    var state = this.ProbeValueCall(call, scope, candidates[i].Signature, expected, operations.AsSpan(i * stride, stride));
                    evaluated[i] = new(null, state, null, 0);
                    if (state is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                    {
                        applicable++;
                    }
                }

                var selected = this.SelectBest(evaluated.AsSpan(0, count), operations, stride);
                if (selected >= 0)
                {
                    return this.BindValueCall(call, scope, candidates[selected].Signature, candidates[selected].Receiver);
                }

                if (applicable != 0)
                {
                    var remaining = 0;
                    for (var i = 0; i < count; i++)
                    {
                        if (evaluated[i].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                        {
                            candidates[remaining++] = candidates[i];
                        }
                    }

                    choices = candidates.AsSpan(0, remaining);
                }

                return this.FailCallableCandidates(call, choices, applicable == 0 ? BindingFailure.NoApplicableCandidate : BindingFailure.Ambiguous);
            }
            finally
            {
                this.argumentOperationScratch.Return(operations, clearArray: true);
                this.candidateScratch.Return(evaluated, clearArray: true);
            }
        }
        finally
        {
            this.callableCandidateScratch.Return(candidates, clearArray: true);
        }
    }

    private CandidateApplicability ProbeValueCall(InvocationKoto call, BindingScope scope, BoundType signature, BoundType? expected, Span<BoundArgumentOperation> operations)
    {
        operations.Clear();
        var parameters = signature.Components[0].Components;
        if (parameters.Count != call.ArgumentNodes.Count)
        {
            return CandidateApplicability.Inapplicable;
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            if (call.GetArgumentLabel(i) is not null)
            {
                return CandidateApplicability.Inapplicable;
            }

            var source = call.ArgumentNodes[i];
            var node = KotoHelper.UnwrapParentheses(source);
            var parameter = parameters[i];
            var quality = ArgumentAdaptation.Literal;
            var kind = ArgumentOperationKind.Value;
            BoundType? adapted = null;
            if (IsWaitingNestedCall(node))
            {
                quality = ArgumentAdaptation.Exact;
            }
            else if (IsAggregateArgument(node) || NeedsEnumContext(node))
            {
                var borrow = parameter is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref };
                var value = borrow ? parameter.Components[0] : parameter;
                var state = this.ProbeAggregateArgument(node, value, scope);
                if (state != CandidateApplicability.Applicable)
                {
                    return state;
                }

                quality = borrow ? ArgumentAdaptation.CrossSemanticsBorrow : ArgumentAdaptation.Literal;
                kind = borrow ? ArgumentOperationKind.Borrow : ArgumentOperationKind.Value;
            }
            else if (node is FunctionKoto { IsAnonymous: true, BoundType: null } closure)
            {
                if (!this.ClosureSignatureFits(closure, parameter))
                {
                    return CandidateApplicability.Inapplicable;
                }

                quality = ArgumentAdaptation.Erasure;
            }
            else if (node is { BoundType: null, BindingState: BindingState.Resolved, BoundSymbol: { Kind: BindingSymbolKind.Function } group })
            {
                if (parameter.Kind != BoundTypeKind.Function || !this.FunctionGroupFits(node, group, parameter, scope))
                {
                    return CandidateApplicability.Inapplicable;
                }

                quality = ArgumentAdaptation.Erasure;
            }
            else if (IsUnfittedLiteral(node))
            {
                var borrow = parameter is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref };
                if (!this.FitsInputLiteral(node, borrow ? parameter.Components[0] : parameter, scope))
                {
                    return CandidateApplicability.Inapplicable;
                }

                quality = borrow ? ArgumentAdaptation.CrossSemanticsBorrow : ArgumentAdaptation.Literal;
                kind = borrow ? ArgumentOperationKind.Borrow : ArgumentOperationKind.Value;
            }
            else if (node.BoundType is { } actual)
            {
                if (this.ErasureSignatureFits(actual, parameter, node))
                {
                    quality = ArgumentAdaptation.Erasure;
                }
                else if (!this.AdaptInput(source, parameter, actual, scope, null, null, out adapted, out quality, out kind, recordPair: false, deferAcquisition: true) ||
                    !this.FitsStructurallyAt(adapted, parameter, call))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }
            else
            {
                return CandidateApplicability.Pending;
            }

            operations[i] = new(source, source.BoundType, parameter, kind, quality, ParameterIndex: i, AdaptedType: adapted);
        }

        var result = signature.Components[1];
        if (PlaceExpected(signature.ResultMode, expected) is { } stored)
        {
            return this.FitsStructurallyAt(result.Components[0], this.ContractType(stored, scope), call)
                ? CandidateApplicability.Applicable : CandidateApplicability.Inapplicable;
        }

        return expected is not null && !this.FitsStructurallyAt(result, expected, call) && this.ExpectedAdaptation(call, result, expected, recordPair: false) is null
            ? CandidateApplicability.Inapplicable : CandidateApplicability.Applicable;
    }

    private BoundType? FailCallableCandidates(InvocationKoto call, ReadOnlySpan<CallableCandidate> candidates, BindingFailure failure, int position = -1, AcquisitionMode mode = default, AcquisitionMode other = default)
    {
        var start = this.callableSelectionParties.Count;
        foreach (var candidate in candidates)
        {
            this.callableSelectionParties.Add(candidate);
        }

        this.callableSelectionFailures[call] = (start, candidates.Length, position, mode, other);
        return this.FailWaitingSelection(call, failure);
    }

    private bool ReportCallableSelection(Koto node, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (!this.callableSelectionFailures.TryGetValue(node, out var failure))
        {
            return false;
        }

        var related = new (string Role, Koto At, string? Label)[failure.Count];
        for (var i = 0; i < failure.Count; i++)
        {
            var candidate = this.callableSelectionParties[failure.Start + i];
            var at = candidate.Context;
            foreach (var syntax in this.nodes)
            {
                if (syntax is IsKoto clause && ReferenceEquals(clause.BoundConstraint, candidate.Fact))
                {
                    at = clause;
                    break;
                }
            }

            related[i] = ("candidate", at, $"Callable<{candidate.Receiver.ToString().ToLowerInvariant()}, {DiagnosticTypeName(candidate.Signature)}>");
        }

        if (code == DiagnosticCode.ParameterShapeMismatch_Kd)
        {
            node.Report(
                requirement,
                code,
                failure.Mode,
                failure.Other,
                evidence: [$"position {failure.Position + 1} of a Callable call", "overlapping Types"],
                related: related,
                note: "All available Callable signatures must agree on acquisition before arguments exclude any candidate (SPEC 8.6, 7.3.1)");
        }
        else
        {
            var note = code == DiagnosticCode.ReceiverShapeMismatch_Kd ? "Distinct Callable signatures require one receiver; each signature uses its weakest declared receiver (SPEC 8.6)"
                : code == DiagnosticCode.AmbiguousBinding_Kd ? "No Callable signature is better than every other applicable signature under the ordinary argument ranking rules (SPEC 10.4)"
                : "No Callable signature applies; candidate checks use positional arguments and any fixed expected result (SPEC 8.6, 10.1)";
            node.Report(requirement, code, evidence: code == DiagnosticCode.ReceiverShapeMismatch_Kd ? null : [failure.Count], related: related, note: note);
        }

        return true;
    }

    private readonly record struct CallableCandidate(BoundType Signature, SemanticsKind Receiver, BoundConstraint Fact, Koto Context);
}
