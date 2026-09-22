// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    // A scalar temporary receives a slot only when a borrow materializes it.
    private static bool IsMaterializedScalar(OwnershipBody body, int place)
    {
        if (body.Places[place] is not { Kind: OwnershipPlaceKind.Temporary } temporary || !ScalarTypes.Supports(temporary.Type))
        {
            return false;
        }

        for (var i = 0; i < body.Operations.Count; i++)
        {
            if (body.Operations[i] is { Kind: OwnershipOperationKind.Borrow } borrow && borrow.Place == place)
            {
                return true;
            }
        }

        return false;
    }

    private bool LowerStructBorrow(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (value.Kind == OwnershipValueKind.Address)
        {
            // A string reference result is admitted only by the reference prepass (a borrowed array element).
            if (operation.Kind != OwnershipOperationKind.Borrow || !(ReferenceTypes.IsBorrow(ValueType(body, id)) || ReferenceTypes.IsString(ValueType(body, id))) ||
                (uint)operation.Place >= (uint)body.Places.Count || value.Constant != operation.Place ||
                (body.IsReachable(id) && (body.GetBorrowInputState(id) & PlaceState.MustInit) == 0))
            {
                return Fail("Borrow address requires initialized storage and a verified reference result.", out failure);
            }

            var type = body.Places[operation.Place].Type;
            var output = ValueType(body, id)!;
            if (ObjectTypes.IsBorrow(output))
            {
                if (!(ObjectTypes.IsOwner(type) || ObjectTypes.IsBorrow(type)) || !ReferenceEquals(type.Components[0], output.Components[0]) ||
                    (output.Semantics == SemanticsKind.ObjUniq && type.Semantics is not (SemanticsKind.Obj or SemanticsKind.ObjUniq)))
                {
                    return Fail("Object borrow requires matching view and exclusive authority.", out failure);
                }

                if (ObjectTypes.IsOwner(type) && value.Count == 0)
                {
                    function.AddScalar(EmissionOpcode.ObjectBorrow, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
                    return true;
                }

                if (ObjectTypes.IsBorrow(type) && value.Count == 1 && ReferenceEquals(ValueType(body, Input(body, id, 0)), type) &&
                    (!body.IsReachable(id) || this.Dominates(Input(body, id, 0), id)))
                {
                    function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0))]);
                    return true;
                }

                return Fail("Object borrow requires its initialized owner or parent borrow.", out failure);
            }

            if (ObjectTypes.IsOwner(type) || ObjectTypes.IsBorrow(type))
            {
                var explicitProjection = operation.Source.Parent is ConversionKoto { ConversionBinding: ConversionBinding.PayloadBorrow } conversion &&
                    ReferenceEquals(conversion.Left, operation.Source) && ReferenceEquals(SignatureType(this, conversion.BoundType), output);
                var memberProjection = operation.Source.Parent is MemberAccessKoto { Parent: InvocationKoto { BoundCall: { } call } } &&
                    ReferenceEquals(call.Receiver, operation.Source) && call.ReceiverOperation.Kind == ArgumentOperationKind.PayloadProjection &&
                    call.ReceiverOperation.ObjectCompatibility == ConstraintProof.Proven && ReferenceEquals(call.ReceiverOperation.ParameterType, output);
                if (!ReferenceEquals(type.Components[0], output.Components[0]) || !(explicitProjection || memberProjection) ||
                    (output.Semantics == SemanticsKind.Uniq && type.Semantics == SemanticsKind.ObjRef))
                {
                    return Fail("Object payload address requires a proved complete-payload projection.", out failure);
                }

                if (ObjectTypes.IsOwner(type) && value.Count == 0)
                {
                    function.AddScalar(EmissionOpcode.ObjectPayload, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
                    return true;
                }

                if (ObjectTypes.IsBorrow(type) && value.Count == 1 && ReferenceEquals(ValueType(body, Input(body, id, 0)), type) &&
                    (!body.IsReachable(id) || this.Dominates(Input(body, id, 0), id)))
                {
                    function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0)), new(EmissionOperandKind.Integer, 16)]);
                    return true;
                }

                return Fail("Payload projection lacks its prepared object reference.", out failure);
            }

            if (operation.Source is IndexKoto indexed && ReferenceTypes.IsArray(type))
            {
                // An element of a borrowed fixed array is borrowed in place through a bounds-checked
                // element address; a monomorphized instance sees its substituted element stride.
                var element = type.Components[0].Components[0];
                var array = value.Count == 2 ? Input(body, id, 0) : -1;
                var subscript = value.Count == 2 ? Input(body, id, 1) : -1;
                if ((uint)array >= (uint)id || (uint)subscript >= (uint)id || type.Semantics != SemanticsKind.Ref || operation.LoanMode != LoanRequirement.Ref ||
                    !(ReferenceTypes.IsStorage(output) || ReferenceTypes.IsString(output)) || output.Semantics != SemanticsKind.Ref || !ReferenceEquals(output.Components[0], element) ||
                    !ReferenceEquals(SignatureType(this, indexed.Left.BoundType), type) || !ReferenceEquals(SignatureType(this, indexed.BoundType), element) ||
                    body.Operations[array].Kind != OwnershipOperationKind.Read || body.Operations[array].Place != operation.Place ||
                    !ReferenceEquals(ValueType(body, array), type) || !ReferenceEquals(body.Operations[array].Source, indexed.Left) ||
                    !ReferenceEquals(ValueType(body, subscript), BoundType.ISize) || !ReferenceEquals(body.Operations[subscript].Source, ElementAccess.ValueSource(indexed.Right)) ||
                    (body.IsReachable(id) && (!this.Dominates(array, id) || !this.Dominates(subscript, id))) ||
                    FunctionAbi.GetValue(element, this.aggregateLayouts) is not { } stride ||
                    !this.TryGetLocation(indexed, directory, constants, out var location))
                {
                    return Fail("Indexed borrow requires its borrowed array, an isize index and a concrete element stride.", out failure);
                }

                function.AddScalar(EmissionOpcode.Sequence, id, [this.PhysicalOperand(body, array), this.PhysicalOperand(body, subscript), new(EmissionOperandKind.Integer, type.Components[0].Length)], place: body.Operations.Count + id, location: location, op: "ArrayAddress", check: ArithmeticCheckKind.Bounds, representation: stride);
                return true;
            }

            if (operation.Source is MemberAccessKoto projected && !ReferenceTypes.IsStorage(SignatureType(this, projected.BoundType)) &&
                ElementAccess.BorrowedPathRoot(projected) is { } projectedRoot)
            {
                if (!this.TryBorrowedPathOffset(projected, projectedRoot, out var projectedOffset) || value.Count != 1 ||
                    !ReferenceEquals(type, SignatureType(this, projectedRoot.BoundType)) ||
                    !ReferenceEquals(ValueType(body, Input(body, id, 0)), type) ||
                    !ReferenceEquals(SignatureType(this, projected.BoundType), output.Components[0]) ||
                    (output.Semantics == SemanticsKind.Uniq && type.Semantics != SemanticsKind.Uniq) ||
                    (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
                {
                    return Fail("Projected borrow lacks a matching stored field and typed receiver.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [this.PhysicalOperand(body, Input(body, id, 0)), new(EmissionOperandKind.Integer, projectedOffset)]);
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
            else if (operation.Source is BinaryKoto path && ElementAccess.OwnedPathRoot(path) is { } owner)
            {
                if (value.Count != 0 || this.aggregatePlaces[operation.Place] is null || !ReferenceEquals(SignatureType(this, owner.BoundType), type) ||
                    !ReferenceEquals(SignatureType(this, path.BoundType), output.Components[0]) || !this.TryBorrowedPathOffset(path, owner, out var pathOffset))
                {
                    return Fail("Owned path borrow does not match its stored layout and Types.", out failure);
                }

                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.Integer, pathOffset)]);
            }
            else if (body.Places[operation.Place].Kind == OwnershipPlaceKind.Temporary && ScalarTypes.Supports(type))
            {
                var input = value.Count == 1 ? Input(body, id, 0) : -1;
                if (input < 0 || !ReferenceEquals(ValueType(body, input), type) || !ReferenceEquals(type, output.Components[0]) ||
                    (body.IsReachable(id) && !this.Dominates(input, id)))
                {
                    return Fail("Scalar temporary borrow requires its prepared value.", out failure);
                }

                // Materialize the prepared value once in the temporary's slot, then borrow that slot.
                var slot = WindowsLowering.GetValue(type)!;
                function.AddScalar(EmissionOpcode.StoreScalar, id, [this.PhysicalOperand(body, input)], slot.ComputationType, place: operation.Place, representation: slot);
                function.AddScalar(EmissionOpcode.BorrowAddress, id, [new(EmissionOperandKind.SlotAddress, operation.Place)]);
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
            : operation.Source switch
            {
                BinaryKoto binary => KotoHelper.UnwrapParentheses(binary.Left) as MemberAccessKoto,
                UnaryKoto unary => KotoHelper.UnwrapParentheses(unary.Operand) as MemberAccessKoto,
                _ => null,
            };
        var receiver = Input(body, id, 0);
        var root = field is null ? null : ElementAccess.BorrowedPathRoot(field);
        if (field is null || root is null ||
            (!ReferenceEquals(ValueType(body, receiver), SignatureType(this, root.BoundType)) &&
                !(value.Kind == OwnershipValueKind.BorrowedField && this.PreparedBorrowMatches(body, id, receiver, root))) || !ReferenceTypes.IsValue(SignatureType(this, field.BoundType)) ||
            (body.IsReachable(id) && !this.Dominates(receiver, id)))
        {
            return Fail("Borrowed field access requires a dominating typed receiver.", out failure);
        }

        if (!this.TryBorrowedPathOffset(field, root, out var offset))
        {
            return Fail("Borrowed field path does not match its stored layout and Types.", out failure);
        }

        var representation = WindowsLowering.GetValue(SignatureType(this, field.BoundType)!)!;
        function.AddScalar(EmissionOpcode.ElementAddress, id, [this.PhysicalOperand(body, receiver), new(EmissionOperandKind.Integer, offset)], representation: representation);
        if (value.Kind == OwnershipValueKind.BorrowedField)
        {
            if (operation.Kind != OwnershipOperationKind.Produce || !ReferenceEquals(ValueType(body, id), SignatureType(this, field.BoundType)))
            {
                return Fail("Borrowed field read has no matching result.", out failure);
            }

            function.AddScalar(EmissionOpcode.LoadElement, id, [], representation.ComputationType, place: id, representation: representation);
        }
        else
        {
            var input = Input(body, id, 1);
            if (operation.Kind != OwnershipOperationKind.WriteBorrowedField || SignatureType(this, root.BoundType)!.Semantics != SemanticsKind.Uniq ||
                !ReferenceEquals(ValueType(body, input), SignatureType(this, field.BoundType)) || (body.IsReachable(id) && !this.Dominates(input, id)))
            {
                return Fail("Borrowed field write requires exclusive access and a matching secured value.", out failure);
            }

            function.AddScalar(EmissionOpcode.StoreElement, id, [this.PhysicalOperand(body, input)], representation.ComputationType, place: id, representation: representation);
        }

        return true;
    }

    private bool PreparedBorrowMatches(OwnershipBody body, int read, int receiver, Koto root)
    {
        var place = ValuePlace(body.Operations[receiver]);
        return KotoHelper.UnwrapParentheses(root).BoundSymbol is { } symbol &&
            this.IsPreparedArgument(body, read, symbol, place) &&
            body.Operations[this.elementNextCalls[read]].Source is InvocationKoto { BoundCall: { } call } &&
            ReferenceTypes.CallTypeMatches(SignatureType(this, root.BoundType), ValueType(body, receiver), call);
    }

    // Inline parts are contiguous in their containing layout: sum each
    // validated level's stored offset from the borrowed base.
    private bool TryBorrowedPathOffset(BinaryKoto field, Koto root, out int offset)
    {
        offset = 0;
        for (var level = field; ;)
        {
            var position = ElementAccess.PathSelector(level, out var owner, out var element);
            owner = SignatureType(this, owner);
            element = SignatureType(this, element);
            var layout = owner is null ? null : this.aggregateLayouts.Get(owner);
            if (layout is null || (uint)position >= (uint)layout.Count || !ReferenceEquals(element, SignatureType(this, level.BoundType)))
            {
                return false;
            }

            offset = checked(offset + layout.Offset(position));
            var receiver = KotoHelper.UnwrapParentheses(level.Left);
            if (ReferenceEquals(receiver, KotoHelper.UnwrapParentheses(root)))
            {
                return true;
            }

            if (receiver is not BinaryKoto parent)
            {
                return false;
            }

            level = parent;
        }
    }
}
