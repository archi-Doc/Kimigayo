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

    // SPEC 10.4, 8.4.8.2: the candidates the ordinary comparison ranks; with premises, also each candidate whose applicability is unproven
    // only by an Unknown premise after every argument and result check, ranked as the applicable candidate it would be.
    private static bool Ranked(in EvaluatedCandidate candidate, bool premises)
        => candidate.State is CandidateApplicability.Applicable or CandidateApplicability.Waiting || (premises && candidate is { State: CandidateApplicability.Pending, PremiseUnknown: true });

    private int SelectBest(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
        => StrictBestCandidate.Select(candidates.Length, new CandidateOrder(candidates, operations, stride, this, false));

    // SPEC 8.4.8.2: an Unknown premise cannot affect the selection when an applicable candidate is strictly best even with every
    // premise-unproven candidate ranked as applicable; no judgment of those premises then changes the winner. The ordinary comparison
    // decides, so a tie or an incomparable pair still defers.
    private bool PremisesCannotAffect(InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride)
    {
        foreach (ref readonly var candidate in candidates)
        {
            if (candidate.State == CandidateApplicability.Waiting)
            {
                if (!ComparableCallableSlots(call, candidates, operations, stride, true))
                {
                    return false;
                }

                break;
            }
        }

        var best = StrictBestCandidate.Select(candidates.Length, new CandidateOrder(candidates, operations, stride, this, true));
        return best >= 0 && candidates[best].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting;
    }

    private readonly ref struct CandidateOrder : IStrictCandidateOrder
    {
        private readonly ReadOnlySpan<EvaluatedCandidate> candidates;
        private readonly BoundArgumentOperation[] operations;
        private readonly int stride;
        private readonly Binding binding;
        private readonly bool premises;

        internal CandidateOrder(ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride, Binding binding, bool premises)
        {
            this.candidates = candidates;
            this.operations = operations;
            this.stride = stride;
            this.binding = binding;
            this.premises = premises;
        }

        public bool IsEligible(int candidate) => Ranked(this.candidates[candidate], this.premises);

        public bool Better(int left, int right)
        {
            if (this.binding.MeasureCallInference)
            {
                this.binding.inferenceComparisons++;
            }

            return BetterCandidate(this.candidates[left], this.candidates[right], this.operations.AsSpan(left * this.stride, this.stride - 1), this.operations.AsSpan(right * this.stride, this.stride - 1));
        }
    }

    // SPEC 10.4: this pure comparison reads explicit source arguments only; receiver acquisition is common to the group.
    private static bool BetterCandidate(in EvaluatedCandidate a, in EvaluatedCandidate b, ReadOnlySpan<BoundArgumentOperation> left, ReadOnlySpan<BoundArgumentOperation> right)
    {
        var better = false;
        var worse = false;
        for (var i = 0; i < left.Length; i++)
        {
            var x = left[i];
            var y = right[i];
            if (x.Adaptation != y.Adaptation && (x.Adaptation == ArgumentAdaptation.Erasure || y.Adaptation == ArgumentAdaptation.Erasure))
            {
                return false; // An incomparable argument cannot be rescued by another argument or tie-breaker.
            }

            better |= x.Adaptation < y.Adaptation;
            worse |= x.Adaptation > y.Adaptation;
        }

        if (worse || better)
        {
            return better && !worse;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (a.State == CandidateApplicability.Waiting && b.State == CandidateApplicability.Waiting &&
                left[i].Adaptation != ArgumentAdaptation.Erasure && left[i].Source is { } source &&
                (IsWaitingCallable(source) || IsWaitingNestedCall(source)))
            {
                continue; // The checked common acquisition ties here; an unchecked waiting signature never ranks.
            }

            var x = left[i].ParameterType;
            var y = right[i].ParameterType;
            if (ReferenceEquals(x, y) && (x is not null || left[i].Source is null))
            {
                continue;
            }

            // Open slots are neither identical nor subtypes; Origin bindings never rank even inside Function Types.
            var xy = x is not null && y is not null && FitsStructuralPart(x, y);
            var yx = x is not null && y is not null && FitsStructuralPart(y, x);
            better |= xy && !yx;
            worse |= !xy;
        }

        if (worse || better)
        {
            return better && !worse;
        }

        var aGeneric = a.Symbol?.Declaration is FunctionKoto { GenericArguments.Count: > 0 };
        var bGeneric = b.Symbol?.Declaration is FunctionKoto { GenericArguments.Count: > 0 };
        return aGeneric != bGeneric ? !aGeneric : a.DefaultsUsed < b.DefaultsUsed;
    }

    // SPEC 10.5, 10.8: a waiting argument at F, ref/F or uniq/F, whether its candidate's fixed expected call signature is closed, holds an
    // unsolved slot or is absent (no Callable Constraint on F), compares equal there when the candidates acquire it in one mode.
    private static bool ComparableCallableSlots(InvocationKoto call, ReadOnlySpan<EvaluatedCandidate> candidates, BoundArgumentOperation[] operations, int stride, bool premises = false)
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
                if (!Ranked(candidates[candidate], premises))
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

                var function = (FunctionKoto)candidates[candidate].Symbol!.Declaration;
                var pattern = function.Parameters[operation.ParameterIndex].Type.BoundType!;
                var borrowed = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq };
                var slot = borrowed ? pattern.Components[0] : pattern;
                SemanticsKind? mode = borrowed ? pattern.Semantics : null;
                // SPEC 10.5: a waiting reference, like an anonymous body, never ranks the outer candidates; once their ordinary
                // inputs and defaults select one, its fixed call signature selects the reference, even when the signatures differ.
                if (slot.Kind != BoundTypeKind.Parameter || ContainerSlot(CallSlotOwner(function), slot.Symbol!) < 0 || (!first && acquisition != mode))
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

    // Keep a single pending declaration's concrete proof obligation before its candidate scratch is reused. A conformance whose only
    // unproven part waits for OCC-X (SPEC 12.4.4.1) is a located limit: an explicit one reports it at its declaration, from which the
    // use derives, and an inherited one, which has no declaration record, at the use.
    private BoundType? FailPendingConstraint(InvocationKoto call, ReferenceConstraintFailure fact)
    {
        this.MarkWaitingHeaders(call);
        return this.FailUnprovenReference(call, fact, true);
    }

    // A call or Function Item whose unproven clause waits only for OCC-X shares that located limit.
    private BoundType? FailUnprovenReference(Koto use, ReferenceConstraintFailure fact, bool unresolved)
    {
        if (fact.Proof == ConstraintProof.Unknown && this.PendingExclusiveConformance(fact.Constraint, this.ConstraintScope(use)) is { } path)
        {
            if (path.InheritedFrom is null)
            {
                this.partPrerequisites[use] = path.Use;
            }

            return this.Fail(use, BindingFailure.Unsupported, unresolved);
        }

        return this.FailExplained(ref this.referenceConstraints, use, BindingFailure.UnprovenConstraint, fact, unresolved);
    }

    private BoundConformancePath? PendingExclusiveConformance(BoundConstraint constraint, BindingScope scope)
    {
        // SPEC 8.7, 12.4.4.1, 23.3.6.1: an Unknown composite clause is the OCC-X limit only when each of its unproven operands waits only
        // for OCC-X; a decided operand is neutral, and any other unproven operand stays a Proof failure after OCC-X.
        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            var left = this.ProveConstraint(constraint.Left!, scope) == ConstraintProof.Unknown;
            var right = this.ProveConstraint(constraint.Right!, scope) == ConstraintProof.Unknown;
            var leftPath = left ? this.PendingExclusiveConformance(constraint.Left!, scope) : null;
            var rightPath = right ? this.PendingExclusiveConformance(constraint.Right!, scope) : null;
            return (left && leftPath is null) || (right && rightPath is null) ? null : leftPath ?? rightPath;
        }

        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.PendingExclusiveConformance(constraint.Left!, scope);
        }

        if (constraint is not { Kind: ConstraintKind.Contract, Subject.Symbol: { } symbol, Contract: { } contract } || !this.conformances.TryGetValue((symbol, contract), out var identity))
        {
            return null;
        }

        foreach (var path in identity.PathStorage)
        {
            if (path.PendingExclusiveOnly)
            {
                return path;
            }
        }

        return null;
    }

    // The first clause with the outcome. An Unknown clause that waits only for OCC-X yields to another Unknown clause, which stays a Proof
    // failure after OCC-X (SPEC 12.4.4.1, 23.3.6.1), so the report does not depend on the clauses' order.
    private ReferenceConstraintFailure? CandidateConstraintFailure(FunctionKoto function, BoundType?[] slots, BoundLength?[] lengths, BindingScope scope, BoundType? self, BoundType? declaringType, ConstraintProof outcome)
    {
        var pending = (ReferenceConstraintFailure?)null;
        for (var i = 0; i < function.TypeConstraints.Count; i++)
        {
            if (function.TypeConstraints[i] is not IsKoto { BoundConstraint: { HasUnresolved: false } bound } clause)
            {
                continue;
            }

            var substituted = this.SubstituteCandidateConstraint(bound, function, slots, lengths, scope, self, declaringType);
            if (!substituted.HasUnresolved && this.ProveConstraint(substituted, scope) == outcome)
            {
                if (outcome != ConstraintProof.Unknown || this.PendingExclusiveConformance(substituted, scope) is null)
                {
                    return new(clause, substituted, outcome, function.BoundSymbol);
                }

                pending ??= new(clause, substituted, outcome, function.BoundSymbol);
            }
        }

        if (function.BoundSymbol is { ConditionalDeclaration: { } declaration } member)
        {
            declaringType ??= this.SelfType(member.Scope.Owner.BoundSymbol!);
            foreach (IsKoto clause in ((SyntaxFormKoto)declaration.Operands[1]).Operands)
            {
                if (clause.BoundConstraint is not { HasUnresolved: false } bound)
                {
                    continue;
                }

                var substituted = this.ContractConstraint(this.SubstituteConstraint(bound, member.Scope.Owner, (BoundType[])declaringType.Components), scope, declaringType);
                if (!substituted.HasUnresolved && this.ProveConstraint(substituted, scope) == outcome)
                {
                    if (outcome != ConstraintProof.Unknown || this.PendingExclusiveConformance(substituted, scope) is null)
                    {
                        return new(clause, substituted, outcome, member);
                    }

                    pending ??= new(clause, substituted, outcome, member);
                }
            }
        }

        return pending;
    }

    private BoundConstraint SubstituteCandidateConstraint(BoundConstraint constraint, FunctionKoto function, BoundType?[] slots, BoundLength?[] lengths, BindingScope scope, BoundType? self, BoundType? declaringType)
    {
        var substituted = this.SubstituteConstraint(constraint, CallSlotOwner(function), slots.AsSpan(0, CallSlotCount(function)), lengths.AsSpan(0, CallOwnSlots(function).Count), incomplete: true);
        if (!function.IsConstructor && declaringType?.Symbol?.Declaration is { } owner)
        {
            substituted = this.SubstituteConstraint(substituted, owner, (BoundType[])declaringType.Components);
        }

        return this.ContractConstraint(substituted, scope, self);
    }

    // Omitted header Types need this selection's expectation. Keep that dependency explicit without checking
    // the body or turning an independent written-Type error into a consequence of the selection.
    private void MarkWaitingHeaders(InvocationKoto call)
    {
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = call.ArgumentNodes[i];
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

    // A Callable signature has no declaration Symbol or own generic/default parameters. Ordinary candidates always have a Symbol.
    // ClosureReceiver: the one closure argument whose minimum call receiver is the candidate's only refuted condition (TryCandidate).
    private readonly record struct EvaluatedCandidate(BindingSymbol? Symbol, CandidateApplicability State, BoundType? DeclaringType, int DefaultsUsed, bool Unsolved = false, ClosureReceiverRefutation? ClosureReceiver = null, CallArgumentMap ArgumentMap = default, bool Accessible = true, bool StableConstruction = false, bool IndependentRejection = false, ReferenceConstraintFailure? ConstraintFailure = null, bool PremiseUnknown = false);

    // SPEC 7.6.3, 8.6: the parameter whose Callable Constraint does not permit its closure argument's minimum call receiver.
    private readonly record struct ClosureReceiverRefutation(int Parameter, SemanticsKind Actual, SemanticsKind Required);
}
