// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(Koto Source, PropertyAccessorKind Kind), InvocationKoto> propertyCalls = new();
    private readonly Dictionary<Koto, MemberAccessKoto> storageProjections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, MemberAccessKoto> propertyUpdateStorage = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, EvaluatedKoto> propertyUpdateInputs = new(ReferenceEqualityComparer.Instance);

    internal static bool IsGetterResult(Koto node)
        => KotoHelper.UnwrapParentheses(node) is not MemberAccessKoto { IsDirectStorage: true } &&
            KotoHelper.UnwrapParentheses(node).BoundSymbol?.Property is { Getter.IsStandard: false };

    internal MemberAccessKoto? PropertyUpdateStorage(Koto node)
        => KotoHelper.UnwrapParentheses(node) is { BindingState: BindingState.Resolved, BoundSymbol.Property: { } property } target &&
            (!property.Getter.IsStandard || !property.Setter.IsStandard) && this.propertyUpdateStorage.GetValueOrDefault(target) is { BindingState: BindingState.Resolved } storage ? storage : null;

    internal FunctionKoto AccessorFunction(BoundAccessor accessor)
    {
        var function = accessor.ExecutionFunction ??= new(accessor);
        function.RefreshAccessor();
        function.BoundSymbol ??= new(function.Name, BindingSymbolKind.Function, function, accessor.Property.Symbol.Scope);
        function.BoundSymbol.Type = accessor.Result;
        function.BoundSymbol.ReceiverIndex = accessor.Receiver is null ? -1 : 0;
        function.IsRequirement = accessor.Property.Declaration.IsContractRequirement;
        function.BindingState = BindingState.Resolved;
        return function;
    }

    internal InvocationKoto? PropertyCall(Koto node, PropertyAccessorKind kind)
    {
        node = KotoHelper.UnwrapParentheses(node);
        if (node.BindingState != BindingState.Resolved || node.BoundSymbol?.Property is not { } property ||
            (IsSpecialField(node, out var special) && special.IsConstructor))
        {
            return null;
        }

        return !(kind == PropertyAccessorKind.Get ? property.Getter : property.Setter).IsStandard &&
            this.propertyCalls.GetValueOrDefault((node, kind)) is { BindingState: BindingState.Resolved } call ? call : null;
    }

    internal MemberAccessKoto? StorageProjection(Koto node)
        => node.BoundSymbol?.Kind == BindingSymbolKind.Storage && this.storageProjections.GetValueOrDefault(node) is { BindingState: BindingState.Resolved } projection ? projection : null;

    private EvaluatedKoto PropertyUpdateInput(Koto target, Koto update)
    {
        if (!this.propertyUpdateInputs.TryGetValue(target, out var input))
        {
            input = new(update);
            this.propertyUpdateInputs.Add(target, input);
        }

        return input;
    }

    private bool ValidPropertyWritePath(Koto node, BindingScope scope)
    {
        node = KotoHelper.UnwrapParentheses(node);
        while (node is BinaryKoto projection && (projection is MemberAccessKoto || ElementAccess.IsSyntax(projection)))
        {
            node = KotoHelper.UnwrapParentheses(projection.Left);
            if (node.BoundType?.Semantics != SemanticsKind.Owner)
            {
                break; // A reference reaches a separate referent with its own authority.
            }

            if (node.BoundSymbol?.Property is { } parent &&
                (!parent.Getter.IsStandard || !parent.Setter.IsStandard ||
                    !this.Accessible(parent.Symbol, scope, parent.Setter.Access, (node as MemberAccessKoto)?.Left.BoundType)))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 13.7.1, 13.7.2 (DECISIONS BU-50): an update with a custom accessor completes its two sides once, as a read and a write
    // complete theirs: the read Type (the node's) and the destination Type (its computed input's, PropertyUpdateDestination), to which
    // the update's operation fits its result. A standard `get` reads and a standard `set` writes the Field's Place as the body reads it
    // (MemberRead), which also types the storage node; a custom accessor's Type is its call's. A side without a Type leaves the update
    // unresolved, as the read is. The storage is reached through the receiver the target names: a bare static Property names none, so
    // its update is the located Unsupported_Kd until P42 binds a static Property's update (the qualified form is the later phases' limit).
    private BoundType? BindPropertyUpdate(Koto target, BindingScope scope)
    {
        var property = target.BoundSymbol!.Property!;
        var stored = this.MemberRead(target, property.Symbol, scope);
        var result = property.Getter.IsStandard ? stored : this.AccessorUseType(target, property.Getter);
        var destination = property.Setter.IsStandard ? stored : this.AccessorUseType(target, property.Setter);
        if (result is null || destination is null)
        {
            return Complete(target, null);
        }

        if (target is not MemberAccessKoto node)
        {
            return this.Fail(target, BindingFailure.Unsupported);
        }

        Complete(this.propertyUpdateInputs[node], destination);
        var owner = node.Left.BoundType!;
        var referent = owner is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } ? owner.Components[0] : owner;
        var type = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [referent], origin: this.PlaceOrigin(node.Left));
        if (!this.AdaptInput(node.Left, type, owner, scope, null, null, out var adapted, out var quality, out var kind, receiver: true))
        {
            this.FailWrite(node, node);
            return null;
        }

        this.receiverOperations[node] = new(node.Left, owner, adapted, kind, quality, ParameterIndex: 0);
        if (!this.propertyUpdateStorage.TryGetValue(node, out var storage))
        {
            // The stored field is reached through the receiver the update locates once; it takes that borrow's Type.
            var receiver = new EvaluatedKoto(node.Left);
            storage = new(node, receiver, node.Right) { IsDirectStorage = true };
            receiver.Parent = storage;
            this.propertyUpdateStorage.Add(node, storage);
        }

        Complete(storage.Left, adapted);
        storage.BoundSymbol = property.Symbol;
        Complete(storage, stored);
        return Complete(node, result);
    }

    // SPEC 13.7.1: the destination Type of an update with a custom accessor (BindPropertyUpdate), to which its operation fits the result.
    private BoundType? PropertyUpdateDestination(Koto target)
        => this.propertyUpdateInputs.GetValueOrDefault(KotoHelper.UnwrapParentheses(target)) is { BindingState: BindingState.Resolved } input
            ? input.BoundType : null;

    // SPEC 13.7.1: the Type a custom accessor's bound call gives its use at `node`: a `set`'s value input (the destination Type), a
    // `get`'s result (the read Type).
    private BoundType? AccessorUseType(Koto node, BoundAccessor accessor)
        => accessor.Kind != PropertyAccessorKind.Set ? this.propertyCalls[(node, accessor.Kind)].BoundType
            : this.propertyCalls.GetValueOrDefault((node, accessor.Kind))?.BoundCall is { ArgumentOperations.Length: > 0 } call
                ? call.ArgumentOperations[0].ParameterType : null;

    private void BindStorageProjection(Koto node, BoundAccessor accessor)
    {
        if (accessor.SelfSymbol is not { } self)
        {
            return;
        }

        if (!this.storageProjections.TryGetValue(node, out var projection))
        {
            var receiver = new IdentifierNameKoto(node, "self");
            projection = new(node, receiver, accessor.Property.Declaration.NameKoto) { IsDirectStorage = true };
            this.storageProjections.Add(node, projection);
        }

        projection.Left.BoundSymbol = self;
        Complete(projection.Left, self.Type);
        projection.BoundSymbol = accessor.Property.Symbol;
        Complete(projection, accessor.Property.Type);
    }

    private bool BindPropertyCall(Koto node, BoundAccessor accessor, BindingScope scope, Koto? input)
    {
        // Accessors use the same complete declaring Type as methods, including nominal Types with only Origin slots.
        var selection = node is MemberAccessKoto selected && this.memberSelections.TryGetValue(selected, out var memberSelection) ? memberSelection : default;
        var declaringType = selection.DeclaringType;

        var receiver = accessor.Receiver is null ? null : (node as MemberAccessKoto)?.Left;
        var requirement = accessor.Property.Declaration.IsContractRequirement && node is MemberAccessKoto requirementUse &&
            this.requirementGroups.TryGetValue(requirementUse, out var requirementGroup) && requirementGroup.Properties.Count == 1 ? requirementGroup : null;
        var previousContract = this.activeRequirementContract;
        if (requirement is not null)
        {
            this.activeRequirementContract = requirement.PropertyContracts[0];
        }

        try
        {
            return BindCall();
        }
        finally
        {
            this.activeRequirementContract = previousContract;
        }

        bool BindCall()
        {
            var inputType = accessor.Input is { } declaredInput ? Signature(declaredInput) : null;
            // Origin-bearing inputs retain their own execution milestone. An owning or object-form written receiver
            // is the declaration error of SPEC 11.3, not a limit of this path.
            var receiverUnsupported = requirement is null && accessor.Receiver is { } receiverType && (!ReferenceTypes.IsStruct(receiverType) || receiverType.Components[0].Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed) ||
                (receiverType.Components[0].Kind == BoundTypeKind.Constructed && declaringType is null));
            if ((requirement is null && accessor.Declaration?.Body is null) || accessor.Result is not { } declaredResult ||
                Signature(declaredResult) is not { } result ||
                (accessor.Input is not null && inputType is null) || inputType is { CarriesAnyOrigin: true } ||
                receiverUnsupported)
            {
                if (!(receiverUnsupported && receiver?.BoundType is { } shapedActual && this.ReceiverRestsOnAccessorShape(node, accessor, receiver, shapedActual, declaringType, null, scope, false)))
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                }

                return false;
            }

            BoundArgumentOperation receiverOperation = default;
            if (accessor.Receiver is { } declaredReceiver && Signature(declaredReceiver) is { } required)
            {
                if (receiver?.BoundType is not { } actual)
                {
                    this.FailWrite(node, node);
                    return false;
                }

                if (!this.AdaptInput(receiver, required, actual, scope, selection.Path, declaringType, out var adapted, out var quality, out var kind, receiver: true))
                {
                    if (!this.ReceiverRestsOnAccessorShape(node, accessor, receiver, actual, declaringType, null, scope, false))
                    {
                        this.FailWrite(node, node);
                    }

                    return false;
                }

                var proof = selection.Path is null ? ConstraintProof.Proven : ProjectedReceiverProof(accessor.Property.Symbol, accessor);
                if (proof != ConstraintProof.Proven)
                {
                    this.Fail(node, proof == ConstraintProof.Unknown ? BindingFailure.Unsupported : BindingFailure.UnprovenConstraint, proof == ConstraintProof.Unknown);
                    return false;
                }

                if (IsObjectSemantics(actual.Semantics) && required.Semantics != SemanticsKind.Ref)
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                    return false;
                }

                receiverOperation = new(receiver, actual, adapted, kind, quality, selection.Path, 0, proof);
            }

            if (!this.propertyCalls.TryGetValue((node, accessor.Kind), out var call) ||
                call.ArgumentNodes.Count != (input is null ? 0 : 1) + (receiver is null ? 0 : 1) ||
                (input is not null && !ReferenceEquals(call.ArgumentNodes[0], input)) ||
                (receiver is not null && !ReferenceEquals(call.ArgumentNodes[^1], receiver)))
            {
                var method = new IdentifierNameKoto(node, accessor.Property.Symbol.Name + "." + accessor.Kind);
                Koto[] arguments = input is null ? receiver is null ? [] : [receiver] : receiver is null ? [input] : [input, receiver];
                call = new(node, method, arguments) { CallStorage = new() { Evaluated = input is EvaluatedKoto ? 0 : -1 } };
                this.propertyCalls[(node, accessor.Kind)] = call;
            }

            var function = this.AccessorFunction(accessor);
            call.Method.BoundSymbol = function.BoundSymbol;
            Complete(call.Method, null);
            var operations = this.argumentOperationScratch.Rent(call.ArgumentNodes.Count + 1);
            operations[call.ArgumentNodes.Count] = default; // Common call judgment's separate receiver slot.
            Span<int> mapping = stackalloc int[2];
            var count = 0;
            if (input is not null)
            {
                if (input is EvaluatedKoto)
                {
                    Complete(input, inputType); // The update supplies its checked Output, never the original RHS.
                }

                var slot = receiver is null ? 0 : 1;
                mapping[count] = slot;
                operations[count++] = new(input, inputType, inputType, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: slot);
            }

            if (receiver is not null)
            {
                mapping[count] = 0;
                operations[count++] = receiverOperation;
            }

            var originCount = accessor.Declaration!.Origins.Count;
            var inputCount = InputOriginCount(accessor.Binder);
            var origins = this.originScratch.Rent(originCount);
            var inputs = this.originScratch.Rent(inputCount);
            Array.Clear(origins);
            Array.Clear(inputs);
            try
            {
                if (receiver is not null && accessor.Receiver is { } receiverPattern && receiverOperation.ParameterType is { } acquired)
                {
                    var receiverContract = Signature(receiverPattern)!;
                    this.MatchInputOrigins(receiverContract, acquired, accessor.Binder, origins, inputs);
                    operations[count - 1] = receiverOperation with
                    {
                        ParameterType = this.SubstituteStoredOrigins(receiverContract, accessor.Binder, origins.AsSpan(0, originCount), inputs.AsSpan(0, inputCount)),
                        AdaptedType = acquired,
                    };
                }

                result = this.SubstituteStoredOrigins(result, accessor.Binder, origins.AsSpan(0, originCount), inputs.AsSpan(0, inputCount));
                if (this.HasUnsubstitutedOrigin(result, accessor.Binder))
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                    return false;
                }

                call.CallStorage!.Set(function.BoundSymbol!, result, null, mapping[..count], [], conformingType: requirement?.Self, declaringType: declaringType, origins: origins.AsSpan(0, originCount), inputOrigins: inputs.AsSpan(0, inputCount), operations: operations.AsSpan(0, count));
                call.CallStorage.RequirementContract = requirement?.PropertyContracts[0];
                this.JudgeSelectedCall(call, function, operations.AsSpan(0, count + 1), origins, inputs, declaringType, []);
                Complete(call, result);
                return true;
            }
            finally
            {
                this.originScratch.Return(inputs, clearArray: true);
                this.originScratch.Return(origins, clearArray: true);
                this.argumentOperationScratch.Return(operations, clearArray: true);
            }

            BoundType? Signature(BoundType type) => this.MemberType(type, declaringType) is { } member ? this.ContractType(member, scope, requirement?.Self) : null;
        }
    }
}
