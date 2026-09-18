// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerStructBorrow(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (value.Kind == OwnershipValueKind.Address)
        {
            if (operation.Kind != OwnershipOperationKind.Borrow || !ReferenceTypes.IsStorage(ValueType(body, id)) ||
                (uint)operation.Place >= (uint)body.Places.Count || value.Constant != operation.Place ||
                (body.IsReachable(id) && (body.GetInputState(id, operation.Place) & PlaceState.MustInit) == 0))
            {
                return Fail("Borrow address requires initialized storage and a verified reference result.", out failure);
            }

            var type = body.Places[operation.Place].Type;
            var output = ValueType(body, id)!;
            if (ObjectTypes.IsOwner(type))
            {
                var explicitProjection = operation.Source.Parent is ConversionKoto { ConversionBinding: ConversionBinding.PayloadBorrow } conversion &&
                    ReferenceEquals(conversion.Left, operation.Source) && ReferenceEquals(conversion.BoundType, output);
                var memberProjection = operation.Source.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } call } } &&
                    ReferenceEquals(call.Receiver, operation.Source) && call.ReceiverOperation.Kind == ArgumentOperationKind.PayloadProjection &&
                    call.ReceiverOperation.ObjectCompatibility == ConstraintProof.Proven && ReferenceEquals(call.ReceiverOperation.ParameterType, output);
                if (value.Count != 0 || !ReferenceEquals(type.Components[0], output.Components[0]) || !(explicitProjection || memberProjection))
                {
                    return Fail("Object payload address requires a proved complete-payload projection.", out failure);
                }

                function.AddScalar(EmissionOpcode.ObjectPayload, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
                return true;
            }

            if (operation.Source is MemberAccessKoto projected && ReferenceTypes.IsStruct(projected.Left.BoundType))
            {
                var projectedOwner = projected.Left.BoundType!.Components[0];
                var projectedLayout = this.aggregateLayouts.Get(projectedOwner);
                var projectedPosition = -1;
                for (var i = 0; i < StructStorage.Count(projectedOwner); i++)
                {
                    if (ReferenceEquals(StructStorage.Field(projectedOwner, i).BoundSymbol, projected.BoundSymbol))
                    {
                        projectedPosition = i;
                        break;
                    }
                }

                if (projectedLayout is null || projectedPosition < 0 || value.Count != 1 ||
                    !ReferenceEquals(type, projected.Left.BoundType) ||
                    !ReferenceEquals(ValueType(body, Input(body, id, 0)), type) ||
                    !ReferenceEquals(StructStorage.FieldType(projectedOwner, projectedPosition), projected.BoundType) ||
                    !ReferenceEquals(projected.BoundType, output.Components[0]) ||
                    (output.Semantics == SemanticsKind.Uniq && type.Semantics != SemanticsKind.Uniq) ||
                    (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
                {
                    return Fail("Projected borrow lacks a matching stored field and typed receiver.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0)), new(EmissionOperandKind.Integer, projectedLayout.Offset(projectedPosition))]);
                return true;
            }

            if (ReferenceTypes.IsStorage(type))
            {
                if (value.Count != 1 || (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)) ||
                    !ReferenceEquals(type.Components[0], output.Components[0]) || (output.Semantics == SemanticsKind.Uniq && type.Semantics != SemanticsKind.Uniq))
                {
                    return Fail("Reborrow has no matching reference source.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0))]);
            }
            else
            {
                if (value.Count != 0 || !ReferenceEquals(type, output.Components[0]) ||
                    (this.aggregatePlaces[operation.Place] is null && !(ScalarTypes.Supports(type) && body.Places[operation.Place].Kind == OwnershipPlaceKind.Local)))
                {
                    return Fail("Borrow source has no matching aggregate storage.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
            }

            return true;
        }

        var field = value.Kind == OwnershipValueKind.BorrowedField ? operation.Source as MemberAccessKoto
            : (operation.Source as BinaryKoto)?.Left as MemberAccessKoto;
        var receiver = Input(body, id, 0);
        if (field is null || !ReferenceTypes.IsStruct(field.Left.BoundType) ||
            !ReferenceEquals(ValueType(body, receiver), field.Left.BoundType) || !ReferenceTypes.IsValue(field.BoundType) ||
            (body.IsReachable(id) && !this.Dominates(receiver, id)))
        {
            return Fail("Borrowed field access requires a dominating typed receiver.", out failure);
        }

        var owner = field.Left.BoundType!.Components[0];
        var layout = this.aggregateLayouts.Get(owner);
        var position = -1;
        for (var i = 0; i < StructStorage.Count(owner); i++)
        {
            if (ReferenceEquals(StructStorage.Field(owner, i).BoundSymbol, field.BoundSymbol))
            {
                position = i;
                break;
            }
        }

        if (layout is null || position < 0)
        {
            return Fail("Borrowed field has no stored field layout.", out failure);
        }

        var representation = WindowsLowering.GetValue(field.BoundType!)!;
        function.AddScalar(EmissionOpcode.ElementAddress, id, [this.PhysicalOperand(body, receiver), new(EmissionOperandKind.Integer, layout.Offset(position))], representation: representation);
        if (value.Kind == OwnershipValueKind.BorrowedField)
        {
            if (operation.Kind != OwnershipOperationKind.Produce || !ReferenceEquals(ValueType(body, id), field.BoundType))
            {
                return Fail("Borrowed field read has no matching result.", out failure);
            }

            function.AddScalar(EmissionOpcode.LoadElement, id, [], representation.ComputationType, place: id, representation: representation);
        }
        else
        {
            var input = Input(body, id, 1);
            if (operation.Kind != OwnershipOperationKind.WriteBorrowedField || field.Left.BoundType.Semantics != SemanticsKind.Uniq ||
                !ReferenceEquals(ValueType(body, input), field.BoundType) || (body.IsReachable(id) && !this.Dominates(input, id)))
            {
                return Fail("Borrowed field write requires exclusive access and a matching secured value.", out failure);
            }

            function.AddScalar(EmissionOpcode.StoreElement, id, [this.PhysicalOperand(body, input)], representation.ComputationType, place: id, representation: representation);
        }

        return true;
    }
}
