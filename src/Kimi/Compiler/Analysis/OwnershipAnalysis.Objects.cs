// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int RuntimeTypeTest(IsKoto source)
    {
        if (source.BoundRuntimeTest is not { SharedType: { } shared } plan)
        {
            return this.Expression(source.Left); // Never has no result and retains its evaluation/cleanup.
        }

        if (!ObjectTypes.IsOwner(plan.OperandType) && !ObjectTypes.IsBorrow(plan.OperandType))
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
