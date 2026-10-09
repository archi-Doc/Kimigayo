// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private static BoundType ArithmeticInputType(InvocationKoto call)
    {
        var input = call.BoundCall!.ArgumentOperations[0];
        return input.AdaptedType ?? call.CodeContext.Compilation.Binding.PreparedBorrowType(input.Source!, input.ParameterType!);
    }

    private int UpdateProperty(Koto source, Koto target, MemberAccessKoto storage, InvocationKoto? arithmetic = null, int loanDepth = -1)
    {
        var binding = this.compilation.Binding;
        var getter = binding.PropertyCall(target, PropertyAccessorKind.Get);
        var setter = binding.PropertyCall(target, PropertyAccessorKind.Set);
        if (!binding.TryGetReceiverOperation(target, out var operation) || operation.Source is null || operation.ParameterType is null ||
            (arithmetic is null && !this.SupportsUpdate(target, target.BoundType, ElementAccess.UpdateOperator(source.Akind))))
        {
            this.Unsupported(source);
            return -1;
        }

        // SPEC 13.7.2: secure the RHS, then keep the one located receiver across the read and write. Getter
        // and setter keep their independent call boundaries and never borrow hidden storage.
        var right = source is BinaryKoto binary ? arithmetic is null ? this.Value(this.Expression(binary.Right))
            : this.PrepareCallArgument(arithmetic, binary.Right, arithmetic.BoundCall!.ArgumentOperations[1]) : 0;
        if (right < 0)
        {
            return -1;
        }

        if (KotoHelper.UnwrapParentheses(operation.Source) is IdentifierNameKoto name && name.BoundType?.Semantics == SemanticsKind.Owner)
        {
            return this.UpdatePropertyPlace(source, target, operation, getter, setter, name, right, arithmetic, loanDepth);
        }

        var receiver = this.BorrowStruct(operation.Source, operation.ParameterType);
        if (receiver < 0)
        {
            return -1;
        }

        var located = ((EvaluatedKoto)storage.Left).Source;
        this.evaluatedOperands[located] = (receiver, -1);
        try
        {
            var previous = getter is null ? arithmetic is null ? this.Value(this.ReadBorrowedField(storage)) : this.BorrowStruct(storage, ArithmeticInputType(arithmetic))
                : this.ReborrowPropertyReceiver(getter, receiver) is >= 0 and var prepared ? this.PropertyGetterValue(target, this.Call(getter, preparedReceiver: prepared), arithmetic) : -1;
            if (source is not BinaryKoto)
            {
                right = this.IncrementOne(source);
            }

            if (previous < 0 || right < 0 || !this.flow!.Nodes[source].CanCompleteNormally)
            {
                return -1;
            }

            var updated = this.ComputePropertyUpdate(source, target, previous, right, arithmetic, loanDepth);
            if (updated < 0)
            {
                return -1;
            }

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
            this.evaluatedOperands.Remove(located);
        }
    }

    // SPEC 9.5.1, 13.7.2: the getter's receiver from the one located receiver. A getter selected through a base path reborrows the whole
    // receiver at its own Type and lends the base prefix, as its call does (PrepareCallArgument), never the receiver as the base.
    private int ReborrowPropertyReceiver(InvocationKoto getter, int receiver)
    {
        var argument = getter.BoundCall!.ArgumentOperations[0];
        if (argument.Kind == ArgumentOperationKind.BaseBorrow)
        {
            return this.BaseBorrowTypes(argument.Source!, argument, out var type, out var whole) && this.ReborrowReceiver(argument.Source!, receiver, whole) is >= 0 and var reference
                ? this.BorrowThrough(getter, reference, type, -1) : -1;
        }

        return this.ReborrowReceiver(argument.Source!, receiver, this.compilation.Binding.PreparedBorrowType(argument.Source!, argument.ParameterType!));
    }

    private int ReborrowReceiver(Koto source, int receiver, BoundType type)
    {
        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var borrow = this.Emit(OwnershipOperationKind.Borrow, source, receiver, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
        this.SetValue(borrow, OwnershipValueKind.Address, [this.Value(receiver)], constant: receiver);
        return this.RegisterTemporary(result);
    }

    // The getter's receiver from a named owned Place, borrowed at the getter's own call; through a base path as above.
    private int BorrowGetterReceiver(InvocationKoto getter, Koto receiver, int place)
    {
        var argument = getter.BoundCall!.ArgumentOperations[0];
        if (argument.Kind != ArgumentOperationKind.BaseBorrow)
        {
            return this.BorrowPropertyPlace(receiver, place, argument.ParameterType!);
        }

        return this.BaseBorrowTypes(argument.Source!, argument, out var type, out var whole) && this.BorrowPropertyPlace(receiver, place, whole) is >= 0 and var reference
            ? this.BorrowThrough(getter, reference, type, -1) : -1;
    }

    private int UpdatePropertyPlace(Koto source, Koto target, BoundArgumentOperation operation, InvocationKoto? getter, InvocationKoto? setter, IdentifierNameKoto receiver, int right, InvocationKoto? arithmetic, int loanDepth)
    {
        // A named owned Place retains its location without lending it for the entire
        // update. Each accessor borrows only at its own call, after securing the RHS.
        var place = this.Local(receiver);
        if (place < 0)
        {
            return -1;
        }

        var previous = getter is null ? arithmetic is null ? this.Value(this.Expression(target, PlaceUseKind.Read)) : this.BorrowStruct(target, ArithmeticInputType(arithmetic))
            : this.BorrowGetterReceiver(getter, receiver, place) is >= 0 and var prepared ? this.PropertyGetterValue(target, this.Call(getter, preparedReceiver: prepared), arithmetic) : -1;
        if (source is not BinaryKoto)
        {
            right = this.IncrementOne(source);
        }

        if (previous < 0 || right < 0 || !this.flow!.Nodes[source].CanCompleteNormally)
        {
            return -1;
        }

        var updated = this.ComputePropertyUpdate(source, target, previous, right, arithmetic, loanDepth);
        if (updated < 0)
        {
            return -1;
        }

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

    private int PropertyGetterValue(Koto target, int value, InvocationKoto? arithmetic)
        => value < 0 ? -1 : arithmetic is null ? this.Value(value) : this.BorrowPropertyPlace(target, value, ArithmeticInputType(arithmetic));

    private int ComputePropertyUpdate(Koto source, Koto target, int previous, int right, InvocationKoto? arithmetic, int loanDepth)
    {
        if (arithmetic is null)
        {
            return this.ComputeUpdate(source, target.BoundType, previous, right, ElementAccess.UpdateOperator(source.Akind));
        }

        var result = this.Call(arithmetic, preparedArguments: [previous, right]);
        this.EndComparisonLoans(loanDepth, source);
        return result;
    }

    private int BorrowPropertyPlace(Koto source, int place, BoundType type)
    {
        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var borrow = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
        var materialized = ScalarTypes.Supports(this.body.Places[place].Type) && this.body.Places[place].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result;
        this.SetValue(borrow, OwnershipValueKind.Address, materialized ? [this.Value(place)] : [], constant: place);
        return this.RegisterTemporary(result);
    }
}
