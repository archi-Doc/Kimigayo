// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The contextual-type fact (design 1's Rule T): for a part that took its expected Type, by its syntax for the pass, the slots of its
    // own pattern that the expected Type supplied (`Owner`'s slots in `Pattern`), or its whole Type (no pattern). Created by the first
    // record.
    private Dictionary<SourceRef, (Koto? Owner, BoundType? Pattern, ulong Slots)>? expectedSlots;

    // The slots still unbound before an expected Type fills them (InferExpectedResult).
    private static ulong UnboundSlots(ReadOnlySpan<BoundType?> slots)
    {
        var unbound = 0UL;
        for (var i = 0; i < Math.Min(slots.Length, 64); i++)
        {
            if (slots[i] is null)
            {
                unbound |= 1UL << i;
            }
        }

        return unbound;
    }

    // The slots among `unbound` that an expected Type then bound. A slot whose Type stores no Origin is well formed by itself (SPEC
    // 15.3.7), so it is never the context's to prove and is not taken.
    private static ulong TakenSlots(ReadOnlySpan<BoundType?> slots, ulong unbound)
    {
        var taken = 0UL;
        for (var i = 0; i < Math.Min(slots.Length, 64); i++)
        {
            if ((unbound & (1UL << i)) != 0 && slots[i] is { StoresOrigin: true })
            {
                taken |= 1UL << i;
            }
        }

        return taken;
    }

    // At a Type parameter of its owner, a pattern position is supplied when the record holds that slot, and is the solution's own
    // otherwise; anywhere else it follows the pattern.
    private static SuppliedPosition SlotPosition(in SuppliedPosition position)
        => position.Pattern is { Kind: BoundTypeKind.Parameter, Symbol: { } symbol } && position.Owner is { } owner
            ? new(null, position.Type, null, null, 0, HoldsSlot(position.Slots, ContainerSlot(owner, symbol))) : position;

    private static bool HoldsSlot(ulong slots, int slot) => slot is >= 0 and < 64 && (slots & (1UL << slot)) != 0;

    // SPEC 10.8.1, 6.5.2 item 2, 4.4.1, 14.9.2, 15.6.1: a position of a part's Type that its expected Type supplied is its context's: the
    // context proves its well-formedness with its final solution (RequireInputPremises at the call whose parameter it fills, otherwise
    // the annotation, result or Field it fits), never the part itself (RequireResultPremises). The one decision, made once where a part
    // takes its expected Type: the selected candidate of a call, a constructor's with an inferred target Type included, the slots its
    // expected result filled (SolveSelected, from its trial's InferExpectedResult; no trial writes); a Case construction, its whole Type
    // unless its qualifier writes it (TakeEnumExpectation); an empty array or Dictionary literal, and a join that takes its position's
    // Type, their whole Type. `slots` empty removes an earlier pass's record. Read through SuppliedPosition only.
    private void TakeExpectedSlots(Koto part, Koto? owner, BoundType? pattern, ulong slots)
    {
        var key = SourceRef.Of(KotoHelper.UnwrapParentheses(part));
        if (slots != 0)
        {
            (this.expectedSlots ??= new())[key] = (owner, pattern, slots);
        }
        else
        {
            this.expectedSlots?.Remove(key);
        }
    }

    // Records that `part` took its whole Type `type` from its expected Type (`taken`), or removes its record. A Type that stores no Origin
    // is well formed by itself and is not recorded.
    private void TakeExpectedType(Koto part, BoundType type, bool taken)
        => this.TakeExpectedSlots(part, null, null, taken && type.StoresOrigin ? ulong.MaxValue : 0);

    // The root position of an argument `part` whose Type is `type` at its input: the part's own Type, or an adaptation's reference layer over
    // it (a borrowed temporary or Place, a reborrow), whose referent is the part's Type; any other adaptation reads through the part's Type,
    // which is then judged wherever any of its positions was supplied.
    private SuppliedPosition ArgumentPosition(Koto part, BoundType? type)
    {
        part = KotoHelper.UnwrapParentheses(part);
        if (type is null || ReferenceEquals(type, part.BoundType))
        {
            return this.PartPosition(part, type);
        }

        return type.Components.Count == 1 && ReferenceEquals(type.Components[0], part.BoundType)
            ? new(part, type, null, null, 0, false) : new(null, type, null, null, 0, this.HasSupplied(part));
    }

    // The position of a call's own Type `pattern`, one of its parameters' written Types: supplied below a slot that the call's expected
    // result filled, which the call's context judges.
    private SuppliedPosition CallPosition(Koto call, BoundType pattern)
        => this.expectedSlots?.TryGetValue(SourceRef.Of(call), out var fact) == true && fact.Pattern is not null
            ? SlotPosition(new(null, null, fact.Owner, pattern, fact.Slots, false)) : default;

    // The position of the call's slot `slot`, a Type argument it infers: supplied when its expected result filled the slot.
    private SuppliedPosition CallPosition(Koto call, int slot)
        => new(null, null, null, null, 0, this.expectedSlots?.TryGetValue(SourceRef.Of(call), out var fact) == true && HoldsSlot(fact.Slots, slot));

    // The position at `index` below `position`. A record's pattern leads to a slot (SlotPosition). An adaptation's layer leads to its part.
    // A Tuple literal's component is its element's. An array literal's element Type and a Dictionary literal's key and value Types are
    // their elements' (SPEC 4.4.1: element evidence, the expected Type filling only the parts it leaves unresolved): supplied wherever an
    // element took any position from its expected Type, a superset that still keeps each position formed when no element took one.
    private SuppliedPosition Component(in SuppliedPosition position, int index)
    {
        var type = position.Type is { } parent && index < parent.Components.Count ? parent.Components[index] : null;
        if (position.Supplied)
        {
            return position with { Type = type };
        }

        if (position.Pattern is { } pattern)
        {
            return index < pattern.Components.Count
                ? SlotPosition(position with { Type = type, Pattern = pattern.Components[index] }) : new(null, type, null, null, 0, false);
        }

        if (position.Part is { } part && !ReferenceEquals(position.Type, part.BoundType))
        {
            return index == 0 ? this.PartPosition(part, type) : new(null, type, null, null, 0, false);
        }

        return position.Part switch
        {
            TupleLiteralKoto tuple when index < tuple.Elements.Count => this.ArgumentPosition(tuple.Elements[index], type),
            ArrayLiteralKoto { FillLength: not null } fill when index == 0 => this.ArgumentPosition(fill.Elements[0], type),
            ArrayLiteralKoto array when index == 0 => new(null, type, null, null, 0, this.AnySupplied(array.Elements)),
            DictionaryLiteralKoto dictionary when index < 2 => new(null, type, null, null, 0, this.AnySupplied(dictionary, index)),
            _ => new(null, type, null, null, 0, false),
        };
    }

    // A part's own position: its record's, its whole Type or its pattern's slots. Else the operand a form delivers with its position's Type
    // (DeliveredOperand: parentheses, a label, an expression body) is the part; a join's Type is its sources' common Type (SPEC 14.9.2
    // step 2), supplied, as an array literal's element Type is, wherever a source took any position from its expected Type; and a literal's
    // positions are its elements'.
    private SuppliedPosition PartPosition(Koto part, BoundType? type)
    {
        if (this.expectedSlots?.TryGetValue(SourceRef.Of(part), out var fact) == true)
        {
            return fact.Pattern is { } pattern ? SlotPosition(new(part, type, fact.Owner, pattern, fact.Slots, false)) : new(null, type, null, null, 0, true);
        }

        if (DeliveredOperand(part) is { } operand)
        {
            return this.PartPosition(operand, type);
        }

        return this.resultContexts.TryGetValue(part, out var join)
            ? new(null, type, null, null, 0, this.AnySupplied(join.SourceNodes)) : new(part, type, null, null, 0, false);
    }

    // Whether a part's record or, through its literal elements, one of theirs holds any position of its Type.
    private bool HasSupplied(Koto part)
    {
        part = KotoHelper.UnwrapParentheses(part);
        if (this.expectedSlots?.ContainsKey(SourceRef.Of(part)) == true)
        {
            return true;
        }

        return part switch
        {
            TupleLiteralKoto tuple => this.AnySupplied(tuple.Elements),
            ArrayLiteralKoto array => this.AnySupplied(array.Elements),
            DictionaryLiteralKoto dictionary => this.AnySupplied(dictionary, 0) || this.AnySupplied(dictionary, 1),
            _ when DeliveredOperand(part) is { } operand => this.HasSupplied(operand),
            _ => this.resultContexts.TryGetValue(part, out var join) && this.AnySupplied(join.SourceNodes),
        };
    }

    // Whether any of `parts` (a literal's elements, a join's sources) has a supplied position.
    private bool AnySupplied(IReadOnlyList<Koto?> parts)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is { } part && this.HasSupplied(part))
            {
                return true;
            }
        }

        return false;
    }

    private bool AnySupplied(DictionaryLiteralKoto dictionary, int component)
    {
        foreach (var entry in dictionary.Entries)
        {
            if (this.HasSupplied(component == 0 ? entry.Key : entry.Value))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.1, 15.6.4 step 3 (design 1's Rule T): one position of a Type that a call judges, read from the contextual-type fact. On an
    // argument's side, the argument's Type there (Type) and whether its expected Type supplied it (Supplied): the call judges a supplied
    // position, and one the argument's own Type kept (Kept) was proven where that Type was formed. On the call's side (CallPosition),
    // whether the call's own expected result supplied it, which the call's context judges with its final solution. The position maps to
    // the part that owns it (Part: a literal, a selected call, an adapted argument) or to a record's pattern (Pattern, Owner's Slots); it
    // decides nothing from a syntax kind.
    private readonly record struct SuppliedPosition(Koto? Part, BoundType? Type, Koto? Owner, BoundType? Pattern, ulong Slots, bool Supplied)
    {
        internal BoundType? Kept => this.Supplied ? null : this.Type;
    }

    // Resets the pass's argument facts: the parts' kinds (Binding.ArgumentKinds) and the slots that took their expected Type.
    private void ResetArgumentFacts()
    {
        this.argumentKinds.Clear();
        this.expectedSlots?.Clear();
    }

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

        // The slots the candidate's expected result filled (InferExpectedResult), which its call records once it is selected.
        internal ulong ExpectedSlots;

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
                UnsolvedSlots(this.Call, this.Function, this.Types, this.Lengths, mapping, ref unsolved, out _);
                var open = new OpenWaitingSlots(ref unsolved, this);
                for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
                {
                    TakeWaiting(this.Call.ArgumentNodes[i], this.Function.Parameters[mapping[i]].Type.BoundType!, this.SlotOwner, open);
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

            return binding.EndProof(outer, CombineProof(proof, binding.ProveMemberConditions(function.BoundSymbol!, this.DeclaringType, this.Scope), true));
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
                ? this.Binding.ErasureSignatureFits(actual, type, node) : this.Binding.FunctionGroupFits(node, node.BoundSymbol!, type, this.Scope);

        // Whether the call infers a construction's Type from its arguments (SPEC 10.8.1), and its target.
        private readonly bool InfersConstruction(out Koto? target)
        {
            target = this.FixedConstruction ? null : this.Binding.InferredConstructorTarget(this.Call.Method);
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
