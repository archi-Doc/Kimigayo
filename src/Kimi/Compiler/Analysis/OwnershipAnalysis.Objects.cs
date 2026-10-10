// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int ReadObjectPayload(ConversionKoto source)
    {
        if (this.Resolve(source.BoundType, this.Active) is not { } payload)
        {
            this.Internal(source);
            return -1;
        }

        // SPEC 13.5.5.1: selection lends the complete payload; the ordinary Copy read retains its internal Origins.
        var type = this.compilation.Binding.PayloadReadReference(source, payload);
        var reference = this.BorrowStruct(source, type);
        if (reference < 0)
        {
            return -1;
        }

        if (this.compilation.Binding.ProveCopy(payload, source) != ConstraintProof.Proven)
        {
            this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
            return this.Temporary(source);
        }

        return this.LoadThrough(source, reference, 1, type);
    }

    // A stored handle is inspected through a protected slot, never acquired as another owner. The ordinary object view
    // and payload borrow keep that slot's Loan, so every containing storage shape has the same lifetime contract.
    private int BorrowStoredObject(Koto source, BoundType stored, BoundType type, int reservation)
    {
        var exclusive = type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;
        var slotType = this.compilation.Binding.Reference(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, stored, type.Origin);
        var slot = this.BorrowIntermediate(source, slotType, reservation);
        if (slot < 0)
        {
            return -1;
        }

        var viewType = type.IsObjectBorrow ? type : this.compilation.Binding.Reference(exclusive ? SemanticsKind.ObjUniq : SemanticsKind.ObjRef, type.Components[0], type.Origin);
        var view = ReferenceEquals(viewType, type) ? this.BorrowThrough(source, slot, viewType, reservation) : this.BorrowIntermediate(source, viewType, reservation, slot);
        return ReferenceEquals(viewType, type) ? view : this.BorrowThrough(source, view, type, reservation);
    }

    private int RuntimeTypeTest(IsKoto source)
    {
        if (source.BoundRuntimeTest is not { SharedType: { } shared } plan)
        {
            return this.Expression(source.Left); // Never has no result and retains its evaluation/cleanup.
        }

        if (plan.OperandType.HandleMode is null && !plan.OperandType.IsObjectBorrow)
        {
            this.Expression(source.Left, PlaceUseKind.Read);
            this.Internal(source);
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
