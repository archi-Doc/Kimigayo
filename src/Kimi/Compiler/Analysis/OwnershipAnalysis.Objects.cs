// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    // A stored handle is inspected through a protected slot, never acquired as another owner. The ordinary object view
    // and payload borrow keep that slot's Loan, so every containing storage shape has the same lifetime contract.
    private int BorrowStoredObject(Koto source, BoundType type, int reservation)
    {
        var exclusive = type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;
        var slotType = this.compilation.Binding.Reference(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, source.BoundType!, type.Origin);
        var slot = this.BorrowIntermediate(source, slotType, reservation);
        if (slot < 0)
        {
            return -1;
        }

        var viewType = ObjectTypes.IsBorrow(type) ? type : this.compilation.Binding.Reference(exclusive ? SemanticsKind.ObjUniq : SemanticsKind.ObjRef, type.Components[0], type.Origin);
        var view = ReferenceEquals(viewType, type) ? this.BorrowThrough(source, slot, viewType, reservation) : this.BorrowIntermediate(source, viewType, reservation, slot);
        return ReferenceEquals(viewType, type) ? view : this.BorrowThrough(source, view, type, reservation);
    }

    private int RuntimeTypeTest(IsKoto source)
    {
        if (source.BoundRuntimeTest is not { SharedType: { } shared } plan)
        {
            return this.Expression(source.Left); // Never has no result and retains its evaluation/cleanup.
        }

        if (ObjectTypes.HandleMode(plan.OperandType) is null && !ObjectTypes.IsBorrow(plan.OperandType))
        {
            this.Expression(source.Left, PlaceUseKind.Read);
            this.Unsupported(source);
            return -1;
        }

        var receiver = this.BorrowStruct(source.Left, shared);
        if (receiver < 0)
        {
            return -1;
        }

        var result = this.Place(source, BoundType.Boolean, OwnershipPlaceKind.Temporary, true);
        var operation = this.Emit(OwnershipOperationKind.Produce, source, result);
        this.SetValue(operation, OwnershipValueKind.RuntimeTypeTest, [this.Value(receiver)]);
        return this.RegisterTemporary(result);
    }
}
