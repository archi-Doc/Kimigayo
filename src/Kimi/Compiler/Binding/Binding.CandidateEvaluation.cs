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
                        operations[(a * stride) + i].Source is { } source && KotoHelper.UnwrapParentheses(source) is FunctionKoto { IsAnonymous: true, BoundType: null })
                    {
                        // ComparableClosureSlots proved matching acquisition. Both slots will receive this one
                        // concrete Closure Type; its Callable constraint signature is not a substituted parameter Type.
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

    private static bool ComparableClosureSlots(InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
    {
        for (var argument = 0; argument < call.ArgumentNodes.Count; argument++)
        {
            if (KotoHelper.UnwrapParentheses(call.ArgumentNodes[argument]) is not FunctionKoto { IsAnonymous: true, BoundType: null })
            {
                continue;
            }

            var first = true;
            SemanticsKind? acquisition = null;
            for (var candidate = 0; candidate < candidates.Length; candidate++)
            {
                if (candidates[candidate].State is not (CandidateApplicability.Applicable or CandidateApplicability.Waiting))
                {
                    continue;
                }

                if (candidates[candidate].State != CandidateApplicability.Waiting)
                {
                    return false;
                }

                var operation = operations[(candidate * stride) + argument];
                var function = (FunctionKoto)candidates[candidate].Symbol.Declaration;
                var pattern = function.Parameters[operation.ParameterIndex].Type.BoundType!;
                var borrowed = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq };
                var slot = borrowed ? pattern.Components[0] : pattern;
                SemanticsKind? mode = borrowed ? pattern.Semantics : null;
                if (slot.Kind != BoundTypeKind.Parameter || ContainerSlot(function, slot.Symbol!) < 0 ||
                    (!first && acquisition != mode))
                {
                    return false;
                }

                acquisition = mode;
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
