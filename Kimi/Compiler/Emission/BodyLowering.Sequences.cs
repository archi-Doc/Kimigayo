// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerSequence(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (value.Constant < 0 || value.Constant >= body.Sequences.Count || operation.Kind != OwnershipOperationKind.Produce)
        {
            return Fail("Missing sequence value plan.", out failure);
        }

        var plan = body.Sequences[(int)value.Constant];
        if (plan.Operation != id || (uint)plan.Receiver >= (uint)body.Places.Count ||
            (body.IsReachable(id) && (body.GetInputState(id, plan.Receiver) & PlaceState.MustInit) == 0))
        {
            return Fail("Sequence metadata requires initialized receiver storage.", out failure);
        }

        var receiver = body.Places[plan.Receiver].Type;
        var syntaxReceiver = operation.Source switch
        {
            BinaryKoto binary => ElementAccess.ValueSource(binary.Left),
            ForKoto loop when plan.Kind is SequenceOperation.Start or SequenceOperation.End or SequenceOperation.ArrayRead => ElementAccess.ValueSource(loop.Iterable),
            _ => null,
        };
        var receiverPlace = body.Places[plan.Receiver];
        if (syntaxReceiver is null || plan.Projection < -1 ||
            (plan.Projection < 0 && !ReferenceEquals(ElementAccess.ValueSource(receiverPlace.Source), syntaxReceiver) &&
                !(syntaxReceiver.BoundSymbol is { } symbol && body.SymbolPlaces.TryGetValue(symbol, out var local) && local == plan.Receiver)))
        {
            return Fail("Sequence receiver does not match its evaluated source.", out failure);
        }

        var address = new EmissionOperand(EmissionOperandKind.SlotAddress, plan.Receiver);
        if (plan.Projection >= 0)
        {
            if ((uint)plan.Projection >= (uint)body.Projections.Count)
            {
                return Fail("Missing sequence receiver projection.", out failure);
            }

            var projection = body.Projections[plan.Projection];
            if (projection.Root != plan.Receiver || projection.Operation >= id || !body.HasComparisonLoan(id, projection.Loan) ||
                !ReferenceEquals(body.Operations[projection.Operation].Source, syntaxReceiver) ||
                (body.IsReachable(id) && !this.Dominates(projection.Operation, id)))
            {
                return Fail("Sequence receiver projection is not available.", out failure);
            }

            receiver = body.Operations[projection.Operation].Source.BoundType!;
            address = new(EmissionOperandKind.ElementAddress, projection.Operation);
        }

        if (plan.Kind is SequenceOperation.Read or SequenceOperation.ArrayRead)
        {
            var arrayRead = plan.Kind == SequenceOperation.ArrayRead;
            if (arrayRead && receiver.Length == 0)
            {
                address = new(EmissionOperandKind.NullAddress, 0);
            }

            var validSource = arrayRead ? operation.Source is ForKoto { Iterable.BoundType.Kind: BoundTypeKind.FixedArray } :
                operation.Source is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Slice };
            if (receiver.Kind != (arrayRead ? BoundTypeKind.FixedArray : BoundTypeKind.Slice) || !validSource ||
                !ReferenceEquals(ValueType(body, id), receiver.Components[0]) || !ScalarTypes.Supports(ValueType(body, id)) ||
                (uint)plan.Index >= (uint)id || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
                (body.IsReachable(id) && !this.Dominates(plan.Index, id)) || !this.TryGetLocation(operation.Source, directory, constants, out var location))
            {
                return Fail("Slice read requires a protected handle and an isize index.", out failure);
            }

            function.AddScalar(EmissionOpcode.Sequence, id, [address, this.PhysicalOperand(body, plan.Index), new(EmissionOperandKind.Integer, arrayRead ? receiver.Length : -1)], place: body.Operations.Count + id, location: location, op: arrayRead ? "ArrayRead" : "Read", check: ArithmeticCheckKind.Bounds, representation: WindowsLowering.GetValue(ValueType(body, id)!));
            return true;
        }

        if (plan.Kind == SequenceOperation.Slice)
        {
            if (receiver.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice) || operation.Source is not IndexKoto { Right: RangeKoto { IsFull: true } } source ||
                source.BoundType is not { Kind: BoundTypeKind.Slice, Origin: not null } slice ||
                !ReferenceEquals(slice, ValueType(body, id)) || !ReferenceEquals(slice.Components[0], receiver.Components[0]))
            {
                return Fail("Slice construction requires a full sequence and its backing Origin.", out failure);
            }

            if (receiver.Kind == BoundTypeKind.FixedArray && this.aggregateLayouts.Get(receiver)!.Value.Layout.Size == 0)
            {
                address = new(EmissionOperandKind.NullAddress, 0);
            }

            function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1)], place: operation.Place, op: "Slice");
            return true;
        }

        var name = plan.Kind switch
        {
            SequenceOperation.Indices => "indices",
            SequenceOperation.Start => "start",
            SequenceOperation.End => "end",
            SequenceOperation.IsEmpty => "isEmpty",
            SequenceOperation.Length => "length",
            _ => null,
        };
        if (name is null || (operation.Source is not ForKoto && operation.Source is not MemberAccessKoto { Right: IdentifierNameKoto }) ||
            (operation.Source is MemberAccessKoto { Right: IdentifierNameKoto member } && member.IdentifierName != name) ||
            receiver.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.ResolvedRange or BoundTypeKind.Slice) ||
            (plan.Kind == SequenceOperation.Indices ? operation.Source is not MemberAccessKoto { Right: IdentifierNameKoto { IdentifierName: "indices" } } ||
                receiver.Kind == BoundTypeKind.ResolvedRange || !ReferenceEquals(ValueType(body, id), BoundType.ResolvedRange) :
                plan.Kind == SequenceOperation.IsEmpty ? !ReferenceEquals(ValueType(body, id), BoundType.Boolean) : !ReferenceEquals(ValueType(body, id), BoundType.ISize)))
        {
            return Fail("Sequence operation does not match its source and result Type.", out failure);
        }

        function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, receiver.Kind == BoundTypeKind.FixedArray ? receiver.Length : -1)], place: operation.Place, op: name, representation: receiver.Kind == BoundTypeKind.ResolvedRange ? WindowsLowering.Unit : null);
        return true;
    }
}
