// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly Dictionary<Koto, int> propertyReceivers = new(ReferenceEqualityComparer.Instance);

    private int UpdateProperty(Koto source, Koto target, MemberAccessKoto storage)
    {
        var binding = this.compilation.Binding;
        var getter = binding.PropertyCall(target, PropertyAccessorKind.Get);
        var setter = binding.PropertyCall(target, PropertyAccessorKind.Set);
        if (!binding.TryGetReceiverOperation(target, out var operation) || operation.Source is null || operation.ParameterType is null)
        {
            this.Unsupported(source);
            return -1;
        }

        if (KotoHelper.UnwrapParentheses(operation.Source) is IdentifierNameKoto name && name.BoundType?.Semantics == SemanticsKind.Owner)
        {
            return this.UpdatePropertyPlace(source, target, storage, operation, getter, setter, name);
        }

        // SPEC 13.7.2: secure the RHS, then keep the one located receiver across the read and write. Getter
        // and setter keep their independent call boundaries and never borrow hidden storage.
        var right = source is BinaryKoto binary ? this.Value(this.Expression(binary.Right)) : 0;
        if (right < 0)
        {
            return -1;
        }

        var receiver = this.BorrowStruct(operation.Source, operation.ParameterType);
        if (receiver < 0)
        {
            return -1;
        }

        this.propertyReceivers[storage.Left] = receiver;
        try
        {
            var previous = getter is null ? this.Value(this.ReadBorrowedField(storage))
                : this.Value(this.Call(getter, preparedReceiver: this.ReborrowPropertyReceiver(getter, receiver)));
            if (source is not BinaryKoto)
            {
                right = this.IncrementOne(source);
            }

            if (previous < 0 || right < 0 || !this.flow!.Nodes[source].CanCompleteNormally)
            {
                return -1;
            }

            var updated = this.ComputeUpdate(source, target.BoundType, previous, right, ElementAccess.UpdateOperator(source.Akind));
            if (setter is not null)
            {
                this.Call(setter, updated, receiver);
            }
            else
            {
                var write = this.Emit(OwnershipOperationKind.WriteBorrowedField, source, receiver, updated);
                this.SetValue(write, OwnershipValueKind.BorrowedFieldWrite, [this.Value(receiver), this.Value(updated)]);
            }

            return this.UpdateResult(source, previous, updated);
        }
        finally
        {
            this.propertyReceivers.Remove(storage.Left);
        }
    }

    private int ReborrowPropertyReceiver(InvocationKoto getter, int receiver)
    {
        var argument = getter.BoundCall!.ArgumentOperations[0];
        var type = this.compilation.Binding.PreparedBorrowType(argument.Source!, argument.ParameterType!);
        var result = this.Place(argument.Source!, type, OwnershipPlaceKind.Temporary, false);
        var borrow = this.Emit(OwnershipOperationKind.Borrow, argument.Source!, receiver, result, loanMode: type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
        this.SetValue(borrow, OwnershipValueKind.Address, [this.Value(receiver)], constant: receiver);
        return this.RegisterTemporary(result);
    }

    private int UpdatePropertyPlace(Koto source, Koto target, MemberAccessKoto storage, BoundArgumentOperation operation, InvocationKoto? getter, InvocationKoto? setter, IdentifierNameKoto receiver)
    {
        // A named owned Place retains its location without lending it for the entire
        // update. Each accessor borrows only at its own call; RHS may inspect the owner.
        var place = this.Local(receiver);
        if (place < 0)
        {
            return -1;
        }

        var previous = getter is null ? this.Value(this.Expression(target, PlaceUseKind.Read))
            : this.Value(this.Call(getter, preparedReceiver: this.BorrowPropertyPlace(receiver, place, getter.BoundCall!.ArgumentOperations[0].ParameterType!)));
        var right = source is BinaryKoto binary ? this.Value(this.Expression(binary.Right)) : this.IncrementOne(source);
        if (previous < 0 || right < 0 || !this.flow!.Nodes[source].CanCompleteNormally)
        {
            return -1;
        }

        var updated = this.ComputeUpdate(source, target.BoundType, previous, right, ElementAccess.UpdateOperator(source.Akind));
        var exclusive = this.BorrowPropertyPlace(receiver, place, operation.ParameterType!);
        if (setter is not null)
        {
            this.Call(setter, updated, exclusive);
        }
        else
        {
            var write = this.Emit(OwnershipOperationKind.WriteBorrowedField, source, exclusive, updated);
            this.SetValue(write, OwnershipValueKind.BorrowedFieldWrite, [this.Value(exclusive), this.Value(updated)]);
        }

        return this.UpdateResult(source, previous, updated);
    }

    private int BorrowPropertyPlace(Koto source, int place, BoundType type)
    {
        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var borrow = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
        this.SetValue(borrow, OwnershipValueKind.Address, [], constant: place);
        return this.RegisterTemporary(result);
    }
}
