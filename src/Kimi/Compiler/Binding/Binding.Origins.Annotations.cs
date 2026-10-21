// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin of a layer in a Type annotation (SPEC 15.3.1, 15.4.2, 15.4.4): its Type position (TypeContext), a written Origin
// (BindOrigin, BindOriginName) or an omitted one (OmittedOrigin), and a binder's inputs, whose atoms the omitted Origins of its
// signature are.
public sealed partial class Binding
{
    private static int InputCount(Koto owner) => owner is FunctionKoto f ? f.Parameters.Count : owner is PropertyAccessorKoto ? 2 : owner is FunctionTypeKoto t ? t.Parameters is TupleTypeKoto tuple ? tuple.ElementNodes.Count : 1 : 0;

    private static int InputOriginCount(Koto owner) => InputCount(owner) + (owner.BoundSymbol?.AggregateInputOrigins?.Count ?? 0);

    private static Koto? InputType(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].Type : owner is PropertyAccessorKoto a ? index == 0 ? a.ReceiverType : a.ValueType : owner is FunctionTypeKoto t ? t.Parameters is TupleTypeKoto tuple ? tuple.ElementNodes[index] : t.Parameters : null;

    private static string InputName(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].InternalName : index == 0 ? "self" : "value";

    private BoundType? BoundInputType(Koto owner, int index)
        => ReferenceEquals(owner, this.closureHeader) && InputType(owner, index) is SyntaxFormKoto { Akind: KotoKind.InferredType }
            ? this.closureHeaderInputs!.Components[index]
            : owner is PropertyAccessorKoto accessor ? index == 0 ? Accessor(accessor).Receiver : Accessor(accessor).Input : InputType(owner, index)?.BoundType;

    private TypeBindingContext TypeContext(Koto syntax, BindingScope scope)
    {
        for (var current = syntax; current.Parent is { } parent; current = parent)
        {
            if (parent is PropertyAccessorKoto accessor)
            {
                if (ReferenceEquals(accessor.ReturnType, current))
                {
                    return new(TypePosition.Result, accessor);
                }

                return ReferenceEquals(accessor.ReceiverType, current) || ReferenceEquals(accessor.ValueType, current)
                    ? new(TypePosition.Parameter, accessor, ReferenceEquals(accessor.ReceiverType, current) ? 0 : 1, true)
                    : new(TypePosition.Explicit, accessor);
            }

            if (parent is FunctionKoto function)
            {
                if (ReferenceEquals(function.ReturnType, current))
                {
                    return new(TypePosition.Result, function);
                }

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (ReferenceEquals(function.Parameters[i].Type, current))
                    {
                        return new(TypePosition.Parameter, function, i, true);
                    }
                }

                return new(TypePosition.Explicit, function);
            }

            if (parent is VariableKoto variable)
            {
                if (variable is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement } property && property.GetAccessor(PropertyAccessorKind.Get) is { } getter)
                {
                    return new(TypePosition.Result, getter);
                }

                return new(variable is PropertyKoto ? scope.Owner is GroupKoto ? TypePosition.StaticStorage : TypePosition.InstanceStorage : TypePosition.Local, variable);
            }
        }

        return new(TypePosition.Explicit, syntax);
    }

    private BoundOrigin? BindOriginName(string name, Koto use, BindingScope scope)
    {
        if (name == "static")
        {
            return BoundOrigin.Static;
        }

        if (this.OriginCandidate(name, use, scope, out var scalar, out var carrier, out var repeated))
        {
            if (repeated is not null)
            {
                this.RestOnRepeatedSet(use, repeated);
                return null;
            }

            if (scalar is not null)
            {
                return scalar;
            }

            if (carrier is { Origin: { } origin } && IsBorrow(carrier.Semantics))
            {
                return origin;
            }

            this.Fail(use, BindingFailure.InvalidOrigin);
            return null;
        }

        this.Fail(use, BindingFailure.MissingOrigin, true);
        return null;
    }

    private BoundOrigin? BindOrigin(Koto syntax, BindingScope scope)
    {
        if (IsRecovery(syntax, out _))
        {
            this.Fail(syntax, BindingFailure.InvalidOrigin);
            return null;
        }

        BoundOrigin? result = null;
        if (syntax is IdentifierNameKoto name)
        {
            result = this.BindOriginName(name.IdentifierName, syntax, scope);
        }
        else if (syntax is ParenthesizedKoto parentheses)
        {
            result = this.BindOrigin(parentheses.Operand, scope);
        }
        else if (syntax is BinaryKoto binary && binary.Akind == KotoKind.And)
        {
            var left = this.BindOrigin(binary.Left, scope);
            var right = this.BindOrigin(binary.Right, scope);
            if (left is not null && right is not null)
            {
                result = this.Meet(left, right);
            }
        }
        else if (syntax is MemberAccessKoto member && member.Left is IdentifierNameKoto input && member.Right is IdentifierNameKoto target)
        {
            if (this.OriginCandidate(input.IdentifierName, syntax, scope, out _, out var type, out var repeated))
            {
                while (type is { Kind: BoundTypeKind.Semantics } && IsBorrow(type.Semantics))
                {
                    type = type.Components[0];
                }

                var schema = type?.Symbol?.Schema;
                var declared = false;
                for (var i = 0; schema is not null && i < schema.Origins.Count; i++)
                {
                    if (schema.Origins[i].Name == target.IdentifierName)
                    {
                        declared = true;
                        result = type!.Kind == BoundTypeKind.Slice ? type.Origin : i < type.OriginArguments.Count ? type.OriginArguments[i] : null;
                        break;
                    }
                }

                if (repeated is not null && (declared || schema is null))
                {
                    // The slot of a repeated set is unknown; a slot name its Type does not declare is still its own problem.
                    this.RestOnRepeatedSet(syntax, repeated);
                    result = null;
                }
                else if (result is not null)
                {
                    input.BindingState = target.BindingState = BindingState.Resolved;
                    target.BoundOrigin = result;
                }
                else if (type?.Symbol?.Schema is not null)
                {
                    this.RecordAbsentSlot(syntax, type);
                }
            }
        }

        if (result is null)
        {
            this.Fail(syntax, BindingFailure.InvalidOrigin);
        }
        else
        {
            result = this.OriginAtUse(result, syntax);
            syntax.BindingState = BindingState.Resolved;
            syntax.BoundOrigin = result;
            if (!OriginVisible(result, syntax))
            {
                this.Fail(syntax, BindingFailure.InvalidOrigin);
            }
        }

        return result;
    }

    private BoundOrigin? OmittedOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement = LoanRequirement.Ref, int aggregateSlot = -1, BindingSymbol? borrowCondition = null)
    {
        if (this.inheritedOriginTypes.TryGetValue(use, out var inherited))
        {
            var origin = aggregateSlot < 0 ? inherited.Origin : aggregateSlot < inherited.OriginArguments.Count ? inherited.OriginArguments[aggregateSlot] : null;
            if (origin is not null)
            {
                return origin;
            }
        }

        if (this.PendingOrigin(use, scope, context, requirement, aggregateSlot, borrowCondition) is { } pending)
        {
            return pending;
        }

        if (context.Position == TypePosition.StaticStorage && requirement != LoanRequirement.Uniq)
        {
            // SPEC 15.4.2: after explicit relations, shared layers and none/ref aggregate slots default to static.
            // Owned and the static-source rules remain separate obligations; this never grants an exclusive loan.
            return BoundOrigin.Static;
        }

        if (aggregateSlot >= 0 && context.Position == TypePosition.Parameter &&
            context.Owner is FunctionKoto { IsAnonymous: false } or PropertyAccessorKoto)
        {
            var slots = context.Owner.BoundSymbol!.AggregateInputOrigins ??= new();
            foreach (var slot in slots)
            {
                if (ReferenceEquals(slot.Occurrence, use) && slot.TargetSlot == aggregateSlot)
                {
                    return slot;
                }
            }

            var origin = new BoundOrigin(OriginKind.Input, context.Owner, InputCount(context.Owner) + slots.Count)
            {
                InputIndex = context.Slot,
                Occurrence = use,
                TargetSlot = aggregateSlot,
            };
            slots.Add(origin);
            return origin;
        }

        if (context.Position == TypePosition.Parameter && context.Direct)
        {
            var origin = this.OriginAtom(context.Owner, OriginKind.Input, context.Slot);
            origin.BorrowCondition = null;
            return origin;
        }

        if (context.Position == TypePosition.Parameter && (context.Owner is FunctionKoto { IsAnonymous: true } ||
            (context.Owner is FunctionTypeKoto && OriginOwner(context.Owner) is VariableKoto { InitializerKoto: not null, TypeKoto: { } annotation } && IsWithin(context.Owner, annotation))))
        {
            // SPEC 15.3.4: nested anonymous/local Function input slots are body regions fixed once, never per-call inputs.
            return this.OpenOrigin(use, aggregateSlot, localInput: true);
        }

        if (context.Position == TypePosition.Local && context.Owner is VariableKoto { InitializerKoto: not null })
        {
            var origin = this.OriginAtom(use, OriginKind.Inference, aggregateSlot);
            this.AddObligation(new(BindingObligationKind.OriginInference, use, BindingDeadline.BodyOrigins, Longer: origin));
            return origin;
        }

        if (context.Position == TypePosition.Result)
        {
            BoundOrigin? meet = null;
            var guaranteedBorrow = false;
            var inputCount = InputCount(context.Owner);
            for (var i = 0; i < inputCount; i++)
            {
                var type = this.BoundInputType(context.Owner, i);
                if (type?.Origin is not { } input || (!IsBorrow(type.Semantics) && type.Kind != BoundTypeKind.SemanticsApplication))
                {
                    continue;
                }

                meet = meet is null ? input : this.Meet(meet, input);
                guaranteedBorrow |= IsBorrow(type.Semantics) || (borrowCondition is not null && ReferenceEquals(type.Symbol, borrowCondition)) ||
                    (type.Symbol?.WholeType is { } whole && this.HasSemanticsRole(whole, SemanticsMask.ValueBorrow | SemanticsMask.ObjRef | SemanticsMask.ObjUniq, scope));
            }

            if (meet is not null)
            {
                if (!guaranteedBorrow && requirement == LoanRequirement.Uniq)
                {
                    this.Fail(use, BindingFailure.MissingOrigin);
                    return null;
                }

                return !guaranteedBorrow && aggregateSlot >= 0 && OwnedWithoutConditionalBorrows() is not ConstraintProof.Proven and var unowned
                    ? this.MissingOrigin(use, unowned) : meet;
            }

            if (requirement != LoanRequirement.Uniq)
            {
                if (aggregateSlot >= 0)
                {
                    this.failedCause = null;
                    for (var i = 0; i < inputCount; i++)
                    {
                        if (this.BoundInputType(context.Owner, i) is { } input && this.ProveOwned(input, use) is not ConstraintProof.Proven and var proof)
                        {
                            return this.MissingOrigin(use, proof);
                        }
                    }
                }

                return BoundOrigin.Static;
            }
        }

        if (context.CallQualifier)
        {
            // SPEC 15.4.4: the call infers the slot; Member reports the qualifier when Binding cannot (RejectedOriginQualifier).
            return this.OriginAtom(use, OriginKind.Inference, aggregateSlot);
        }

        this.Fail(use, BindingFailure.MissingOrigin);
        return null;

        ConstraintProof OwnedWithoutConditionalBorrows()
        {
            this.failedCause = null;
            var count = InputCount(context.Owner);
            for (var i = 0; i < count; i++)
            {
                var input = this.BoundInputType(context.Owner, i);
                if (input?.Kind == BoundTypeKind.SemanticsApplication)
                {
                    input = input.Components[0];
                }

                if (input is not null && this.ProveOwned(input, use) is not ConstraintProof.Proven and var proof)
                {
                    return proof;
                }
            }

            return ConstraintProof.Proven;
        }
    }

    // SPEC 15.4.2, 23.1.2: a result Origin needs its inputs Owned; one that is not is a missing Origin, unless its Error rests on a cause.
    private BoundOrigin? MissingOrigin(Koto use, ConstraintProof unowned)
    {
        if (unowned == ConstraintProof.Error && this.TakeFailedCause() is { } cause)
        {
            this.CompleteDependent(use, cause);
        }
        else
        {
            this.Fail(use, BindingFailure.MissingOrigin);
        }

        return null;
    }
}
