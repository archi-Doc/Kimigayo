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
                        operations[(a * stride) + i].Source is { } source && (IsWaitingCallable(source) || IsWaitingNestedCall(source)))
                    {
                        // ComparableCallableSlots proved matching acquisition. A waiting argument is completed only for the
                        // selected candidate, so its Callable constraint signature never ranks.
                        continue;
                    }

                    // SPEC 10.8: a parameter Type that holds an unsolved slot (an open position, without a Type) is neither identical
                    // to nor a subtype of another Type.
                    var x = operations[(a * stride) + i].ParameterType;
                    var y = operations[(b * stride) + i].ParameterType;
                    if (ReferenceEquals(x, y) && (x is not null || operations[(a * stride) + i].Source is null))
                    {
                        continue;
                    }

                    // Only existing operation-free Type relations participate here, by their structural part: Origin bindings never
                    // rank candidates (SPEC 10.4 step 2, 15.6.1), also inside Function Types.
                    var xy = x is not null && y is not null && FitsStructuralPart(x, y);
                    var yx = x is not null && y is not null && FitsStructuralPart(y, x);
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

    // SPEC 10.5, 10.8: a waiting argument at F, ref/F or uniq/F, whether its candidate's fixed expected call signature is closed, holds an
    // unsolved slot or is absent (no Callable Constraint on F), compares equal there when the candidates acquire it in one mode.
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
                // SPEC 10.5: a waiting reference, like an anonymous body, never ranks the outer candidates; once their ordinary
                // inputs and defaults select one, its fixed call signature selects the reference, even when the signatures differ.
                if (slot.Kind != BoundTypeKind.Parameter || ContainerSlot(function, slot.Symbol!) < 0 || (!first && acquisition != mode))
                {
                    return false;
                }

                acquisition = mode;
                first = false;
            }
        }

        return true;
    }

    // SPEC 10.2.1, 10.7: whether the remaining candidates erase one argument in some candidate and take it directly in another.
    private static bool ErasureIncomparable(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride, int arguments)
    {
        for (var a = 0; a < arguments; a++)
        {
            var erased = false;
            var direct = false;
            for (var i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                {
                    erased |= operations[(i * stride) + a].Adaptation == ArgumentAdaptation.Erasure;
                    direct |= operations[(i * stride) + a].Adaptation != ArgumentAdaptation.Erasure;
                }
            }

            if (erased && direct)
            {
                return true;
            }
        }

        return false;
    }

    private BoundType? FailWaitingSelection(InvocationKoto call, BindingFailure failure)
    {
        this.MarkWaitingHeaders(call);
        return this.Fail(call, failure, true);
    }

    // SPEC 8.7, 15.6.1: a selection that a single pending Callable proof blocks, explained by that Constraint record.
    private BoundType? FailCallableSelection(InvocationKoto call, CallableConstraintFact fact)
    {
        this.MarkWaitingHeaders(call);
        return this.FailExplained(ref this.callableConstraints, call, BindingFailure.UnprovenConstraint, fact, true);
    }

    // Omitted header Types need this selection's expectation. Keep that dependency explicit without checking
    // the body or turning an independent written-Type error into a consequence of the selection.
    private void MarkWaitingHeaders(InvocationKoto call)
    {
        foreach (var argument in call.ArgumentNodes)
        {
            if (IsWaitingNestedCall(argument))
            {
                this.CompleteDependent(argument, call);
            }
            else if (KotoHelper.UnwrapParentheses(argument) is FunctionKoto { IsAnonymous: true, BoundType: null } closure)
            {
                this.MarkOmittedHeaders(closure, call);
            }
        }
    }

    // ClosureReceiver: the one closure argument whose minimum call receiver is the candidate's only refuted condition (TryCandidate).
    private readonly record struct EvaluatedCandidate(BindingSymbol Symbol, CandidateApplicability State, BoundType? DeclaringType, int DefaultsUsed, ulong Unsolved = 0, ClosureReceiverRefutation? ClosureReceiver = null);

    // SPEC 7.6.3, 8.6: the parameter whose Callable Constraint does not permit its closure argument's minimum call receiver.
    private readonly record struct ClosureReceiverRefutation(int Parameter, SemanticsKind Actual, SemanticsKind Required);
}
