// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(Koto Source, PropertyAccessorKind Kind), InvocationKoto> propertyCalls = new();
    private readonly Dictionary<Koto, MemberAccessKoto> storageProjections = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, MemberAccessKoto> propertyUpdateStorage = new(ReferenceEqualityComparer.Instance);

    internal static bool IsGetterResult(Koto node)
        => KotoHelper.UnwrapParentheses(node).BoundSymbol?.Property is { Getter.IsStandard: false };

    internal MemberAccessKoto? PropertyUpdateStorage(Koto node)
        => this.propertyUpdateStorage.GetValueOrDefault(KotoHelper.UnwrapParentheses(node));

    internal FunctionKoto AccessorFunction(BoundAccessor accessor)
    {
        var function = accessor.ExecutionFunction ??= new(accessor);
        function.RefreshAccessor();
        function.BoundSymbol ??= new(function.Name, BindingSymbolKind.Function, function, accessor.Property.Symbol.Scope);
        function.BoundSymbol.Type = accessor.Result;
        function.BoundSymbol.ReceiverIndex = accessor.Receiver is null ? -1 : 0;
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

        return (kind == PropertyAccessorKind.Get ? property.Getter : property.Setter).IsStandard
            ? null : this.propertyCalls.GetValueOrDefault((node, kind));
    }

    internal MemberAccessKoto? StorageProjection(Koto node)
        => node.BoundSymbol?.Kind == BindingSymbolKind.Storage ? this.storageProjections.GetValueOrDefault(node) : null;

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
        if (property.Getter.Result is not { } result || property.Setter.Input is not { } input ||
            !this.FitsTypeAt(result, input, node))
        {
            Fail(node, BindingFailure.TypeMismatch);
            return false;
        }

        var owner = node.Left.BoundType!;
        var referent = ReferenceTypes.IsStruct(owner) ? owner.Components[0] : owner;
        var type = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [referent], origin: this.PlaceOrigin(node.Left));
        if (!this.AdaptInput(node.Left, type, owner, scope, null, null, out var adapted, out var quality, out var kind, receiver: true))
        {
            Fail(node, BindingFailure.InvalidAssignment);
            return false;
        }

        this.receiverOperations[node] = new(node.Left, owner, adapted, kind, quality, ParameterIndex: 0);
        if (!this.propertyUpdateStorage.TryGetValue(node, out var storage))
        {
            storage = new(node, new IdentifierNameKoto(node.Left, "self"), node.Right);
            this.propertyUpdateStorage.Add(node, storage);
        }

        Complete(storage.Left, adapted);
        storage.BoundSymbol = property.Symbol;
        Complete(storage, property.Type);
        return true;
    }

    private void BindStorageProjection(Koto node, BoundAccessor accessor)
    {
        if (accessor.SelfSymbol is not { } self)
        {
            return;
        }

        if (!this.storageProjections.TryGetValue(node, out var projection))
        {
            var receiver = new IdentifierNameKoto(node, "self");
            projection = new(node, receiver, accessor.Property.Declaration.NameKoto);
            this.storageProjections.Add(node, projection);
        }

        projection.Left.BoundSymbol = self;
        Complete(projection.Left, self.Type);
        projection.BoundSymbol = accessor.Property.Symbol;
        Complete(projection, accessor.Property.Type);
    }

    private bool BindPropertyCall(Koto node, BoundAccessor accessor, BindingScope scope, Koto? input)
    {
        // Ownership-bearing results/inputs, requirement dispatch and inherited/object
        // receiver projections retain their own execution milestones.
        if (accessor.Declaration?.Body is null || accessor.Result is not { CarriesOrigin: false } result ||
            this.ProveCopy(result, node) != ConstraintProof.Proven ||
            (accessor.Input is { } value && (value.CarriesOrigin || this.ProveCopy(value, node) != ConstraintProof.Proven)) ||
            (accessor.Receiver is { } receiverType && (!ReferenceTypes.IsStruct(receiverType) || receiverType.Components[0].Kind != BoundTypeKind.Nominal)))
        {
            Fail(node, BindingFailure.Unsupported, true);
            return false;
        }

        var receiver = accessor.Receiver is null ? null : (node as MemberAccessKoto)?.Left;
        BoundArgumentOperation receiverOperation = default;
        if (accessor.Receiver is { } required)
        {
            if (receiver?.BoundType is not { } actual ||
                (node is MemberAccessKoto member && this.memberSelections.TryGetValue(member, out var selection) && selection.Path is not null) ||
                !this.AdaptInput(receiver, required, actual, scope, null, null, out var adapted, out var quality, out var kind, receiver: true))
            {
                Fail(node, BindingFailure.InvalidAssignment);
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
        var operations = this.argumentOperationScratch.Rent(call.ArgumentNodes.Count);
        Span<int> mapping = stackalloc int[2];
        var count = 0;
        if (input is not null)
        {
            var slot = receiver is null ? 0 : 1;
            mapping[count] = slot;
            operations[count++] = new(input, accessor.Input, accessor.Input, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: slot);
        }

        if (receiver is not null)
        {
            mapping[count] = 0;
            operations[count++] = receiverOperation;
        }

        call.CallStorage!.Set(function.BoundSymbol!, result, null, mapping[..count], [], operations: operations.AsSpan(0, count));
        this.argumentOperationScratch.Return(operations, clearArray: true);
        Complete(call, result);
        return true;
    }
}
