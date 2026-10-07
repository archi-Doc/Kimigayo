// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int ArithmeticUpdate(BinaryKoto source, InvocationKoto call)
    {
        if (call.BoundCall is not { ArgumentOperations.Length: 2 } plan)
        {
            this.Unsupported(source);
            return -1;
        }

        var target = this.SelectedPlace(KotoHelper.UnwrapParentheses(source.Left));
        if (this.compilation.Binding.PropertyUpdateStorage(target) is { } property)
        {
            var propertyDepth = this.comparisonDepth++;
            try
            {
                return this.UpdateProperty(source, target, property, call, propertyDepth);
            }
            finally
            {
                this.EndComparisonLoans(propertyDepth, source);
                this.comparisonDepth = propertyDepth;
            }
        }

        var destination = target is IdentifierNameKoto ? this.Local(target) : -1;
        var inline = target is BinaryKoto path && ElementAccess.OwnedPathRoot(path) is not null && !Binding.IsGetterResult(path);
        var raw = ElementAccess.IsRawPlace(target);
        var leftType = plan.ArgumentOperations[0].AdaptedType ?? this.compilation.Binding.PreparedBorrowType(source.Left, plan.ArgumentOperations[0].ParameterType!);
        var depth = this.comparisonDepth++;
        var right = this.PrepareCallArgument(call, source.Right, plan.ArgumentOperations[1]);
        var address = -1;
        var projection = -1;
        Koto storage = target;
        if (destination < 0 && !inline)
        {
            if (!raw && target is BinaryKoto element && ElementAccess.IsSyntax(element) && !ElementAccess.ReachesThroughBorrow(element) && ElementAccess.WritableRoot(element) is not null)
            {
                projection = this.LocateElement(element);
            }
            else if (raw)
            {
                address = this.PointerAddress(target);
            }
            else if (target is ConversionKoto followed && this.FollowsReference(followed) && this.Concrete(followed.Left.BoundType)?.Semantics == SemanticsKind.Uniq)
            {
                storage = followed.Left;
                address = this.ReadsStoredReference(followed) ? this.StoredReference(KotoHelper.UnwrapParentheses(storage), SemanticsKind.Uniq)
                    : this.Expression(storage, PlaceUseKind.Read);
            }
            else if (target is InvocationKoto place && ElementAccess.PlaceCallReference(place)?.Semantics == SemanticsKind.Uniq)
            {
                address = this.PlaceCallReference(place);
            }
            else
            {
                this.Unsupported(source);
            }
        }

        var left = destination >= 0 || inline ? this.PrepareCallArgument(call, source.Left, plan.ArgumentOperations[0])
            : projection >= 0 ? this.BorrowElementAddress((BinaryKoto)target, projection, leftType)
            : address < 0 ? -1 : raw ? this.BorrowRawAddress(target, address, leftType) : this.BorrowThrough(target, address, leftType, -1);
        // Preparation is RHS-first; the retained requirement still receives operands in their original positions.
        var updated = this.Call(call, preparedArguments: [left, right]);
        this.EndComparisonLoans(depth, source);
        this.comparisonDepth = depth;
        if (updated < 0)
        {
            return -1;
        }

        if (destination >= 0)
        {
            this.Emit(OwnershipOperationKind.Write, source, destination, updated);
        }
        else
        {
            // A static inline path has no receiver or key evaluation. Its exclusive access starts only at replacement.
            if (inline)
            {
                address = this.BorrowStruct(target, this.compilation.Binding.Reference(SemanticsKind.Uniq, target.BoundType!, leftType.Origin));
            }
            else if (projection >= 0)
            {
                address = this.BorrowElementAddress((BinaryKoto)target, projection, this.compilation.Binding.Reference(SemanticsKind.Uniq, target.BoundType!, leftType.Origin));
            }

            this.StorePointer(storage, raw ? address : this.Value(address), updated);
        }

        return this.Temporary(source);
    }
}
