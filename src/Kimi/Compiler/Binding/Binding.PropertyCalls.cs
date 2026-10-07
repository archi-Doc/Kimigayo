// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(Koto Source, PropertyAccessorKind Kind), InvocationKoto> propertyCalls = new();
    private readonly Dictionary<Koto, MemberAccessKoto> storageProjections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, MemberAccessKoto> propertyUpdateStorage = new(ReferenceEqualityComparer.Instance);

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

    private bool BindPropertyUpdate(MemberAccessKoto node, BindingScope scope)
    {
        var property = node.BoundSymbol!.Property!;
        var getterResult = this.propertyCalls.GetValueOrDefault((node, PropertyAccessorKind.Get))?.BoundType ?? property.Getter.Result;
        var setterInput = this.PropertySetterInput(node) ?? property.Setter.Input;
        if (getterResult is not { } result || setterInput is not { } input ||
            !this.FitsTypeAt(result, input, node))
        {
            this.Fail(node, BindingFailure.TypeMismatch);
            return false;
        }

        var owner = node.Left.BoundType!;
        var referent = owner is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } ? owner.Components[0] : owner;
        var type = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [referent], origin: this.PlaceOrigin(node.Left));
        if (!this.AdaptInput(node.Left, type, owner, scope, null, null, out var adapted, out var quality, out var kind, receiver: true))
        {
            this.FailWrite(node, node);
            return false;
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
        Complete(storage, property.Type);
        return true;
    }

    private BoundType? PropertySetterInput(Koto node)
        => this.propertyCalls.GetValueOrDefault((node, PropertyAccessorKind.Set))?.BoundCall is { ArgumentOperations.Length: > 0 } call
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
        var declaringType = node is MemberAccessKoto selected && this.memberSelections.TryGetValue(selected, out var memberSelection) &&
            memberSelection.DeclaringType is { } declaring ? declaring : null;

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
            // Origin-bearing inputs and inherited/object receiver projections retain their own execution
            // milestones. An owning or object-form written receiver is the declaration error of SPEC 11.2, not a limit of this path.
            var receiverUnsupported = requirement is null && accessor.Receiver is { } receiverType && (!ReferenceTypes.IsStruct(receiverType) || receiverType.Components[0].Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed) ||
                (receiverType.Components[0].Kind == BoundTypeKind.Constructed && declaringType is null));
            if ((requirement is null && accessor.Declaration?.Body is null) || accessor.Result is not { } declaredResult ||
                Signature(declaredResult) is not { } result ||
                (accessor.Input is not null && inputType is null) || inputType is { CarriesOrigin: true } ||
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
                if (receiver?.BoundType is not { } actual || (node is MemberAccessKoto member && this.memberSelections.TryGetValue(member, out var selection) && selection.Path is not null))
                {
                    this.FailWrite(node, node);
                    return false;
                }

                if (!this.AdaptInput(receiver, required, actual, scope, null, null, out var adapted, out var quality, out var kind, receiver: true))
                {
                    if (!this.ReceiverRestsOnAccessorShape(node, accessor, receiver, actual, declaringType, null, scope, false))
                    {
                        this.FailWrite(node, node);
                    }

                    return false;
                }

                receiverOperation = new(receiver, actual, adapted, kind, quality, ParameterIndex: 0);
            }

            if (!this.propertyCalls.TryGetValue((node, accessor.Kind), out var call) ||
                call.ArgumentNodes.Count != (input is null ? 0 : 1) + (receiver is null ? 0 : 1) ||
                (input is not null && !ReferenceEquals(call.ArgumentNodes[0], input)) ||
                (receiver is not null && !ReferenceEquals(call.ArgumentNodes[^1], receiver)))
            {
                var method = new IdentifierNameKoto(node, accessor.Property.Symbol.Name + "." + accessor.Kind);
                Koto[] arguments = input is null ? receiver is null ? [] : [receiver] : receiver is null ? [input] : [input, receiver];
                call = new(node, method, arguments) { CallStorage = new() };
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
                if (HasUnsubstitutedOrigin(result, accessor.Binder))
                {
                    this.Fail(node, BindingFailure.Unsupported, true);
                    return false;
                }

                call.CallStorage!.Set(function.BoundSymbol!, result, null, mapping[..count], [], conformingType: requirement?.Self, declaringType: declaringType, origins: origins.AsSpan(0, originCount), inputOrigins: inputs.AsSpan(0, inputCount), operations: operations.AsSpan(0, count));
                call.CallStorage.RequirementContract = requirement?.PropertyContracts[0];
                this.JudgeSelectedCall(call, function, operations, count, origins, inputs, declaringType);
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
