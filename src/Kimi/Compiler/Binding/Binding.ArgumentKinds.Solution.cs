// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A candidate's solution from its call's evaluation: the call's slot arrays, scope, Self and explicit Type arguments.
    private SlotSolution Solution(in CallEvaluation evaluation, FunctionKoto function, BoundType? declaringType)
        => new()
        {
            Binding = this,
            Call = evaluation.Call,
            Binder = function,
            Generic = evaluation.Generic,
            Scope = evaluation.Scope,
            Self = evaluation.Self,
            DeclaringType = declaringType,
            Types = evaluation.Scratch,
            Lengths = evaluation.LengthArguments,
            Origins = evaluation.Origins,
            Inputs = evaluation.Inputs,
        };

    // SPEC 6.5.2, 10.8.1: a Case construction's solution: the enum owns its slots and binds its Origins; it has no Self, declaring Type,
    // explicit Type argument or input Origin, and its Type is never inferred apart from its slots. A Case without payload has no call,
    // and nothing reads it then.
    private SlotSolution Solution(
        InvocationKoto? call,
        EnumKoto declaration,
        BindingScope scope,
        BoundType?[] slots,
        BoundLength?[] lengths,
        BoundOrigin[] origins)
        => new()
        {
            Binding = this,
            Call = call!,
            Binder = declaration,
            Scope = scope,
            Types = slots,
            Lengths = lengths,
            Origins = origins,
            Inputs = [],
            FixedConstruction = true,
        };

    // The SPEC 10.8 solve state of one candidate or one Case construction (DECISIONS BU-2): its Type, length and Origin slots and its
    // input Origins, with what substitution reads (scope, Self, declaring Type), its explicit Type arguments, whether a construction's
    // inferred Type is checked fixed (ValidateFixedConstruction) and the Origin inference its evidence begins. The binder (a function or
    // an enum) binds the Origins; the slots belong to the SlotOwner: the binder, or the structure for a constructor. Invariant: a Case
    // construction supplies data, and no operation of the shared walks (Pattern, Type, the evidence walk, the defaults, the
    // waiting-binds-F rule) reads Function; only a candidate's operations (its Constraints, its call's arguments) do.
    private ref partial struct SlotSolution
    {
        internal Binding Binding;
        internal InvocationKoto Call;
        internal Koto Binder;
        internal GenericsKoto? Generic;
        internal BindingScope Scope;
        internal BoundType? Self;
        internal BoundType? DeclaringType;
        internal BoundType?[] Types;
        internal BoundLength?[] Lengths;
        internal BoundOrigin[] Origins;
        internal BoundOrigin[] Inputs;
        internal bool FixedConstruction;
        internal OriginInference? OriginInference;

        // The slots' owner, whether it is a constructor's structure rather than the binder, and the binder's Origin count.
        internal readonly Koto SlotOwner => CallSlotOwner(this.Binder);

        internal readonly bool Constructor => !ReferenceEquals(this.SlotOwner, this.Binder);

        internal readonly int OriginCount => CallOriginCount(this.Binder);

        // The candidate's function, for the operations of a call.
        internal readonly FunctionKoto Function => (FunctionKoto)this.Binder;

        // The pattern with the solution substituted, or null while it holds an unbound slot.
        internal readonly BoundType? Type(BoundType pattern)
            => this.Binding.CallType(pattern, this.Binder, this.Types, this.Scope, this.Self, this.Origins, this.Inputs, this.DeclaringType, this.Lengths);

        // Whether every own slot is bound.
        internal readonly bool IsComplete()
        {
            var own = CallOwnSlots(this.Function);
            for (var i = 0; i < own.Count; i++)
            {
                if (own[i] is LengthParameterKoto ? this.Lengths[i] is null : this.Types[i] is null)
                {
                    return false;
                }
            }

            return true;
        }

        // SPEC 10.5.1 (D4): whether the result holds a Type or Semantics slot that neither evidence nor a waiting callable fixes with
        // no outer expectation (OpenWaitingSlots), as a construction's does (HoldsOpenSlot); a constructor's result holds all its slots.
        // Origin slots are not counted.
        internal readonly bool ResultHoldsUnsolved(int[] mapping)
        {
            var unsolved = this.Binding.RentSlotSet(CallSlotCount(this.Function));
            try
            {
                this.Binding.UnsolvedSlots(this.Call, this.Function, this.Types, this.Lengths, mapping, ref unsolved, out _);
                var open = new OpenWaitingSlots(ref unsolved, this);
                for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
                {
                    this.Binding.TakeWaiting(this.Call.ArgumentNodes[i], this.Function.Parameters[mapping[i]].Type.BoundType!, this.SlotOwner, open);
                }

                return !unsolved.IsEmpty &&
                    (this.Function.IsConstructor || (this.Function.BoundSymbol?.Type is { } result && MentionsSlots(result, this.SlotOwner, unsolved)));
            }
            finally
            {
                this.Binding.ReturnSlotSet(unsolved);
            }
        }

        // SPEC 15.3.6, 10.8: a Callable proof that fails only because a slot holds the open region of an argument's own per-call Origin,
        // and holds once that per-call Origin stands in for it, is no inapplicability: the argument is reported after selection.
        internal readonly bool PerCallCallables(int[] mapping, Span<BoundArgumentOperation> marks)
        {
            var count = CallOwnSlots(this.Function).Count;
            var restored = this.Binding.typeScratch.Rent(count);
            try
            {
                // SPEC 10.7, 10.8: first instantiate each argument's per-call input at a required input over a fixed Origin; when every
                // Constraint then holds, that instantiation solves the slots and nothing is reported.
                this.Types.AsSpan(0, count).CopyTo(restored);
                if (this.StandIns(mapping, restored.AsSpan(0, count), marks, true) && this.Prove(restored.AsSpan(0, count), false) == ConstraintProof.Proven)
                {
                    restored.AsSpan(0, count).CopyTo(this.Types);
                    this.CompleteOperationTypes(marks);
                    return true;
                }

                this.Types.AsSpan(0, count).CopyTo(restored);
                var marked = this.StandIns(mapping, restored.AsSpan(0, count), marks, false);
                if (!marked || this.Prove(restored.AsSpan(0, count), false) != ConstraintProof.Proven)
                {
                    for (var i = 0; i < this.Call.ArgumentNodes.Count && marked; i++)
                    {
                        marks[i] = marks[i] with { MissingOrigin = false };
                    }

                    return false;
                }

                return true;
            }
            finally
            {
                this.Binding.typeScratch.Return(restored, clearArray: true);
            }
        }

        // Each operation's parameter Type with the solution substituted, where it is complete.
        internal readonly void CompleteOperationTypes(Span<BoundArgumentOperation> operations)
        {
            for (var i = 0; i < operations.Length; i++)
            {
                if (operations[i] is { Source: not null, ParameterIndex: >= 0 } operation &&
                    this.Type(this.Function.Parameters[operation.ParameterIndex].Type.BoundType!) is { } completed)
                {
                    operations[i] = operation with { ParameterType = completed };
                }
            }
        }

        // SPEC 10.8: the Constraints and premises whose judgment needs an unsolved slot are not judged.
        internal readonly ConstraintProof Prove(ReadOnlySpan<BoundType?> slots, bool incomplete, SlotSet unbound = default)
        {
            var binding = this.Binding;
            var function = this.Function;
            var constructor = function.IsConstructor;
            var arguments = constructor ? this.Types.AsSpan(0, CallSlotCount(function)) : slots;
            var outer = binding.BeginProof(); // A candidate's applicability is one conjunction (DECISIONS BU-22).
            var proof = binding.CheckConstraints(
                function.TypeConstraints,
                function,
                arguments,
                this.Scope,
                this.Self,
                constructor ? null : this.DeclaringType,
                this.Lengths,
                incomplete: incomplete || !unbound.IsEmpty,
                skipUnresolved: !unbound.IsEmpty,
                slotOwner: this.SlotOwner);
            if (this.DeclaringType is { } declaringType)
            {
                var closedOwner = constructor ? binding.ConstructionType(function, declaringType, this.Types) : declaringType;
                if (closedOwner is not null)
                {
                    proof = CombineProof(proof, binding.CheckTypeConstraints(closedOwner, this.Scope), true);
                }
                else if (constructor && this.SlotOwner is StructKoto owner)
                {
                    var ownerSlots = this.Types.AsSpan(0, CallSlotCount(function));
                    var ownerProof = binding.CheckConstraints(
                        owner.ConstraintNodes, owner, ownerSlots, this.Scope, incomplete: true, skipUnresolved: !unbound.IsEmpty);
                    proof = CombineProof(proof, ownerProof, true);
                }
            }

            proof = CombineProof(proof, binding.CheckSignatureTypeConstraints(function), true);
            // A substitution must be a valid complete Type independently of whether the function constrains or uses that slot (SPEC 8.1.3).
            for (var i = 0; i < slots.Length; i++)
            {
                proof = CombineProof(proof, this.SlotProof(slots, i, unbound), true);
            }

            proof = CombineProof(proof, binding.ProveMemberConditions(function.BoundSymbol!, this.DeclaringType, this.Scope), true);
            var fact = binding.ProofCause(proof) is null ? binding.ProofFact(proof) : null; // A limit takes precedence.
            var clause = binding.clauseRead is { } kept && kept.Outcome == proof ? kept : (ClauseFact?)null; // G351a: at the trial's verdict.
            proof = binding.EndProof(outer, proof);
            if (fact is not null)
            {
                fact.Epoch = binding.frameEpoch; // The candidate trial's frame, whose consumer explains it (CallableOriginFailure).
            }

            binding.trialUnknown = fact;
            binding.trialClause = clause is { } trial ? trial with { Epoch = binding.frameEpoch } : null;
            return proof;
        }

        // SPEC 10.7, 10.8, 15.3.6: a callable value at a common Function Type that fits only once its per-call input is instantiated at
        // a required input over a fixed Origin, or only through its own per-call Origin, which is reported after selection.
        internal readonly CandidateApplicability FitPerCall(Koto argument, BoundType type, out BoundArgumentOperation operation)
        {
            var binding = this.Binding;
            var node = KotoHelper.UnwrapParentheses(argument);
            operation = default;
            if (!IsWaitingFunctionReference(node) && node.BoundType is not { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem })
            {
                return CandidateApplicability.Inapplicable;
            }

            if (binding.PerCallStandIn(type, argument, type, true) is { } instantiated && this.FitsCallable(node, instantiated))
            {
                binding.InstantiateOpenOrigins(this.Types.AsSpan(0, CallOwnSlots(this.Function).Count), argument, type);
                operation = new(argument, node.BoundType, instantiated, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure);
                return CandidateApplicability.Applicable;
            }

            if (binding.PerCallStandIn(type, argument, type) is { } standIn && this.FitsCallable(node, standIn))
            {
                operation = new(argument, node.BoundType, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure, MissingOrigin: true);
                return CandidateApplicability.Applicable;
            }

            return CandidateApplicability.Inapplicable;
        }

        // Whether the callable value converts to the common Function Type: a function group by one of its functions (SPEC 7.7.4).
        private readonly bool FitsCallable(Koto node, BoundType type)
            => node.BoundType is { } actual
                ? this.Binding.ErasureSignatureFits(actual, type, node, out _) : this.Binding.FunctionGroupFits(node, node.BoundSymbol!, type, this.Scope);

        // Whether the call infers a construction's Type from its arguments (SPEC 10.8.1), and its target.
        private readonly bool InfersConstruction(out Koto? target)
        {
            target = !this.FixedConstruction && this.Call.Method is MemberAccessKoto member && this.Binding.inferredConstructorTargets.Contains(member.Left)
                ? member.Left : null;
            return target is not null;
        }

        private readonly Span<BoundOrigin> InputSpan() => this.Inputs.AsSpan(0, Math.Min(this.Inputs.Length, InputOriginCount(this.Binder)));

        // A slot's own validity: a length is Unknown while unbound; a Type slot is checked when bound and Proven when it is unbound only
        // because it is unsolved.
        private readonly ConstraintProof SlotProof(ReadOnlySpan<BoundType?> slots, int slot, SlotSet unbound)
        {
            if (CallOwnSlots(this.Function)[slot] is LengthParameterKoto)
            {
                return this.Lengths[slot] is not null ? ConstraintProof.Proven : ConstraintProof.Unknown;
            }

            if (slots[slot] is { } argument)
            {
                return this.Binding.CheckTypeConstraints(argument, this.Scope);
            }

            return unbound.Contains(slot) ? ConstraintProof.Proven : ConstraintProof.Unknown;
        }

        // Replaces the open regions of each argument whose Callable signature holds one, marking that argument unless `fixedOnly`.
        private readonly bool StandIns(int[] mapping, Span<BoundType?> slots, Span<BoundArgumentOperation> targets, bool fixedOnly)
        {
            var binding = this.Binding;
            var replaced = false;
            for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
            {
                var source = this.Call.ArgumentNodes[i];
                if (this.CallableSlot(this.Function.Parameters[mapping[i]].Type.BoundType!) is not { } callableSlot || source.BoundType is null ||
                    !binding.TryCallable(callableSlot, binding.ConstraintScope(this.Function), out var requiredSignature, out _) ||
                    binding.CallMemberPattern(requiredSignature, this.Binder, this.DeclaringType) is not { } memberSignature ||
                    this.Type(binding.ContractType(memberSignature, this.Scope, this.Self)) is not { } required ||
                    binding.PerCallStandIn(required, source, required, fixedOnly) is null)
                {
                    continue;
                }

                for (var g = 0; g < slots.Length; g++)
                {
                    if (slots[g] is { } solution)
                    {
                        slots[g] = binding.ReplaceOpenOrigins(solution, source, required, fixedOnly);
                    }
                }

                if (!fixedOnly)
                {
                    targets[i] = targets[i] with { MissingOrigin = true };
                }

                replaced = true;
            }

            return replaced;
        }
    }
}
