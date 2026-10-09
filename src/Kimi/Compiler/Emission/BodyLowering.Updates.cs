// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    internal bool LowerCompilerUpdate(EmissionFunction function, CompilerFunctionKind kind, BoundType[] inputs)
    {
        if (inputs.Length != 2 || inputs[0] is not { Semantics: SemanticsKind.Uniq, Components: [var type] })
        {
            return false;
        }

        var layout = this.aggregateLayouts.GetStored(type);
        var representation = layout?.Value ?? WindowsLowering.GetValue(type);
        if (representation is null)
        {
            return false;
        }

        var swap = kind == CompilerFunctionKind.Swap;
        var exchange = kind == CompilerFunctionKind.Exchange;
        if (representation.Layout.Size != 0 && (exchange || swap) && (layout is not null || ReferenceEquals(type, BoundType.String)))
        {
            function.SlotAddresses.Add(new(exchange ? EmissionOperandKind.ReturnAddress : EmissionOperandKind.SlotAddress, 0));
            if (swap)
            {
                function.Slots.Add(new(0, representation));
            }
        }

        AddWholeUpdate(function, 0, type, layout, representation, new(EmissionOperandKind.Argument, 0), new(EmissionOperandKind.Argument, 1), 0, swap, exchange, -1);
        return true;
    }

    private static void AddWholeUpdate(EmissionFunction function, int id, BoundType type, AggregateLayout? layout, ValueLowering representation, EmissionOperand destination, EmissionOperand incoming, int result, bool swap, bool exchange, int location)
    {
        if (representation.Layout.Size == 0)
        {
            // Zero-sized values have no ABI argument or bytes to transfer, but replacing a live value still runs drop.
            if (!swap && !exchange && layout?.NeedsDestruction == true)
            {
                AddOwnedDestruction(function, id, destination, location, layout);
            }

            return;
        }

        if (swap && ReferenceTypes.IsValue(type))
        {
            function.AddScalar(EmissionOpcode.SwapScalars, id, [destination, incoming], representation: representation);
        }
        else if (layout is not null || ReferenceEquals(type, BoundType.String))
        {
            if (exchange || swap)
            {
                Transfer(function, id, layout, [destination], result, representation);
            }
            else if (layout?.NeedsDestruction != false)
            {
                AddOwnedDestruction(function, id, destination, location, layout);
            }

            Transfer(function, id, layout, [incoming, destination], representation: representation);
            if (swap)
            {
                Transfer(function, id, layout, [new(EmissionOperandKind.SlotAddress, result), incoming], representation: representation);
            }
        }
        else if (representation.Layout.Size != 0)
        {
            function.AddScalar(EmissionOpcode.ElementAddress, id, [destination, new(EmissionOperandKind.Integer, 0)], representation: representation);
            if (exchange)
            {
                function.AddScalar(EmissionOpcode.LoadElement, id, [], representation.ComputationType, place: id, representation: representation);
            }

            function.AddScalar(EmissionOpcode.StoreElement, id, [incoming], representation.ComputationType, place: id, representation: representation);
        }
    }

    private static void Transfer(EmissionFunction function, int id, AggregateLayout? layout, ReadOnlySpan<EmissionOperand> operands, int place = -1, ValueLowering? representation = null)
    {
        var start = function.Operands.Count;
        function.Operands.AddRange(operands);
        function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, Place: place, OperandStart: start, OperandCount: operands.Length, Aggregate: layout, Representation: representation ?? layout!.Value));
    }

    private bool LowerBorrowedUpdate(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (!operation.IsWholeUpdate || value.Kind != OwnershipValueKind.BorrowedUpdate || value.Count != 2 ||
            (uint)operation.Place >= (uint)body.Places.Count || (uint)operation.Input >= (uint)body.Places.Count ||
            value.Constant < 0 || value.Constant >= body.Places.Count)
        {
            return Fail("Missing complete borrowed update plan.", out failure);
        }

        var swap = operation.Kind == OwnershipOperationKind.SwapBorrowed;
        var exchange = operation.Kind == OwnershipOperationKind.ExchangeBorrowed;
        var first = (int)value.Constant;
        var reference = body.Places[first].Type;
        if (!ReferenceTypes.IsStorage(reference) || (swap && !ReferenceTypes.IsStorage(body.Places[operation.Input].Type)))
        {
            return Fail("Whole update requires supported borrowed storage.", out failure);
        }

        var type = reference.Components[0];
        var receiver = Input(body, id, 0);
        var input = Input(body, id, 1);
        var binding = body.Function.CodeContext.Compilation.Binding;
        var inputType = swap ? body.Places[operation.Input].Type.Components[0] : body.Places[operation.Input].Type;
        if (reference.Semantics != SemanticsKind.Uniq || !ReferenceTypes.IsStorage(reference) ||
            !binding.FitsVerifiedTypeAt(body.Places[operation.Place].Type, exchange || swap ? type : BoundType.Unit, operation.Source) ||
            ValueType(body, receiver) is not { } receiverType || !binding.FitsVerifiedTypeAt(receiverType, reference, operation.Source) ||
            !binding.FitsVerifiedTypeAt(inputType, type, operation.Source) || (swap && !binding.FitsVerifiedTypeAt(type, inputType, operation.Source)) ||
            (swap && body.Places[operation.Input].Type.Semantics != SemanticsKind.Uniq) ||
            (body.IsReachable(id) && (!this.Dominates(receiver, id) ||
                (body.GetInputState(id, first) & PlaceState.MustInit) == 0 || (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0)))
        {
            return Fail("Borrowed update requires matching initialized exclusive targets and acquired inputs.", out failure);
        }

        var layout = exchange || swap ? this.aggregatePlaces[operation.Place] : this.aggregateLayouts.GetStored(type);
        var representation = layout?.Value ?? WindowsLowering.GetValue(type);
        if (representation is null || (!ReferenceTypes.IsValue(type) && layout is null && !ReferenceEquals(type, BoundType.Unit) && !ReferenceEquals(type, BoundType.String)))
        {
            return Fail("Borrowed update has no complete value representation.", out failure);
        }

        if ((swap || (layout is null && representation.Layout.Size != 0)) &&
            (input < 0 || (body.IsReachable(id) && !this.Dominates(input, id))))
        {
            return Fail("Borrowed update value does not dominate placement.", out failure);
        }

        var location = -1;
        if (!exchange && !swap && (layout is not null || ReferenceEquals(type, BoundType.String)) && layout?.NeedsDestruction != false &&
            !this.TryGetLocation(operation.Source, directory, constants, out location))
        {
            return Fail("Payload content destruction requires a location.", out failure);
        }

        var incoming = representation.Layout.Size == 0 ? default : swap ? this.PhysicalOperand(body, input) : layout is not null || ReferenceEquals(type, BoundType.String)
            ? new(EmissionOperandKind.SlotAddress, operation.Input) : ReferenceTypes.IsString(type) ? this.ReferenceOperand(body, input) : this.PhysicalOperand(body, input);
        AddWholeUpdate(function, id, type, layout, representation, this.PhysicalOperand(body, receiver), incoming, operation.Place, swap, exchange, location);

        this.AddStringFlags(function, operation, id);
        return true;
    }
}
