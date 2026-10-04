// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly ScratchBuffers<EvaluatedCandidate> candidateScratch = new();

    private enum CandidateApplicability : byte
    {
        Inapplicable,
        Applicable,
        Waiting,
        Pending,
        Error,
    }

    private static int SelectBest(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
    {
        // SPEC 10.4: the receiver acquisition is common to the group (one receiver shape per Name, SPEC 7.3),
        // so Best Candidate compares the explicit arguments only; the receiver occupies the last slot.
        var compared = stride - 1;
        for (var a = 0; a < candidates.Length; a++)
        {
            if (candidates[a].State is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
            {
                continue;
            }

            var fa = (FunctionKoto)candidates[a].Symbol.Declaration;
            var dominates = true;
            for (var b = 0; b < candidates.Length; b++)
            {
                if (a == b || candidates[b].State is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
                {
                    continue;
                }

                var fb = (FunctionKoto)candidates[b].Symbol.Declaration;
                var better = false;
                var worse = false;
                for (var i = 0; i < compared; i++)
                {
                    var x = operations[(a * stride) + i];
                    var y = operations[(b * stride) + i];
                    if (x.Adaptation != y.Adaptation && (x.Adaptation == ArgumentAdaptation.Erasure || y.Adaptation == ArgumentAdaptation.Erasure))
                    {
                        // Incomparability at one argument cannot be rescued by another argument or a later tie-breaker.
                        worse = true;
                        break;
                    }

                    better |= x.Adaptation < y.Adaptation;
                    worse |= x.Adaptation > y.Adaptation;
                }

                if (worse)
                {
                    dominates = false;
                    break;
                }

                if (better)
                {
                    continue;
                }

                for (var i = 0; i < compared; i++)
                {
                    if (candidates[a].State == CandidateApplicability.Waiting && candidates[b].State == CandidateApplicability.Waiting &&
                        operations[(a * stride) + i].Adaptation != ArgumentAdaptation.Erasure &&
                        operations[(a * stride) + i].Source is { } source && IsWaitingCallable(source))
                    {
                        // ComparableCallableSlots proved matching acquisition and a common reference context when needed.
                        // Both slots receive the same concrete callable Type, not their Callable constraint signature.
                        continue;
                    }

                    var x = operations[(a * stride) + i].ParameterType;
                    var y = operations[(b * stride) + i].ParameterType;
                    if (ReferenceEquals(x, y))
                    {
                        continue;
                    }

                    // Only existing operation-free Type relations participate here.
                    var xy = x is not null && y is not null && FitsType(x, y);
                    var yx = x is not null && y is not null && FitsType(y, x);
                    better |= xy && !yx;
                    worse |= !xy;
                }

                if (worse)
                {
                    dominates = false;
                    break;
                }

                if (better)
                {
                    continue;
                }

                var aGeneric = fa.GenericArguments.Count != 0;
                var bGeneric = fb.GenericArguments.Count != 0;
                if (!(aGeneric != bGeneric ? !aGeneric : candidates[a].DefaultsUsed < candidates[b].DefaultsUsed))
                {
                    dominates = false;
                    break;
                }
            }

            if (dominates)
            {
                return a;
            }
        }

        return -1;
    }

    private static bool ComparableCallableSlots(InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
    {
        for (var argument = 0; argument < call.ArgumentNodes.Count; argument++)
        {
            if (!IsWaitingCallable(call.ArgumentNodes[argument]))
            {
                continue;
            }

            var first = true;
            SemanticsKind? acquisition = null;
            BoundType? referenceContext = null;
            for (var candidate = 0; candidate < candidates.Length; candidate++)
            {
                if (candidates[candidate].State is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
                {
                    continue;
                }

                var operation = operations[(candidate * stride) + argument];
                if (operation.Adaptation == ArgumentAdaptation.Erasure)
                {
                    continue; // The comparison retains this distinct operation; it never ties with a concrete slot.
                }

                if (candidates[candidate].State != CandidateApplicability.Waiting)
                {
                    return false;
                }

                var function = (FunctionKoto)candidates[candidate].Symbol.Declaration;
                var pattern = function.Parameters[operation.ParameterIndex].Type.BoundType!;
                var borrowed = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq };
                var slot = borrowed ? pattern.Components[0] : pattern;
                SemanticsKind? mode = borrowed ? pattern.Semantics : null;
                if (slot.Kind != BoundTypeKind.Parameter || ContainerSlot(function, slot.Symbol!) < 0 ||
                    (!first && (acquisition != mode || (IsWaitingFunctionReference(call.ArgumentNodes[argument]) && !ReferenceEquals(referenceContext, operation.ParameterType)))))
                {
                    return false;
                }

                acquisition = mode;
                referenceContext = operation.ParameterType;
                first = false;
            }
        }

        return true;
    }

    private BoundType? FailWaitingSelection(InvocationKoto call, BindingFailure failure)
    {
        // Omitted header Types need this selection's expectation. Keep that dependency explicit without checking
        // the body or turning an independent written-Type error into a consequence of the selection.
        foreach (var argument in call.ArgumentNodes)
        {
            if (KotoHelper.UnwrapParentheses(argument) is FunctionKoto { IsAnonymous: true, BoundType: null } closure)
            {
                foreach (var parameter in closure.Parameters)
                {
                    if (parameter.Type is SyntaxFormKoto { Akind: KotoKind.InferredType } inferred)
                    {
                        this.CompleteDependent(inferred, call);
                    }
                }
            }
        }

        return this.Fail(call, failure, true);
    }

    private readonly record struct EvaluatedCandidate(BindingSymbol Symbol, CandidateApplicability State, BoundType? DeclaringType, int DefaultsUsed);
}
