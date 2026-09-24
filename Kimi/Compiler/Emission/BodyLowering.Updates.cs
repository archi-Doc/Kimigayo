// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
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
        if (operation.Source is not InvocationKoto { BoundCall: { } plan } || value.Kind != OwnershipValueKind.BorrowedUpdate || value.Count != 2 ||
            plan.Target.CompilerFunction is not (CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap) ||
            (uint)operation.Place >= (uint)body.Places.Count || (uint)operation.Input >= (uint)body.Places.Count ||
            value.Constant < 0 || value.Constant >= body.Places.Count)
        {
            return Fail("Missing complete borrowed update plan.", out failure);
        }

        var swap = plan.Target.CompilerFunction == CompilerFunctionKind.Swap;
        var exchange = plan.Target.CompilerFunction == CompilerFunctionKind.Exchange;
        var first = (int)value.Constant;
        var reference = body.Places[first].Type;
        if (!ReferenceTypes.IsStorage(reference) || (swap && !ReferenceTypes.IsStorage(body.Places[operation.Input].Type)))
        {
            return Fail("Whole update requires supported borrowed storage.", out failure);
        }

        var type = reference.Components[0];
        var receiver = Input(body, id, 0);
        var input = Input(body, id, 1);
        if (reference.Semantics != SemanticsKind.Uniq || !ReferenceTypes.IsStorage(reference) ||
            !ReferenceEquals(body.Places[operation.Place].Type, exchange || swap ? type : BoundType.Unit) || !ReferenceEquals(ValueType(body, receiver), reference) ||
            !ReferenceEquals(swap ? body.Places[operation.Input].Type.Components[0] : body.Places[operation.Input].Type, type) ||
            (swap && body.Places[operation.Input].Type.Semantics != SemanticsKind.Uniq) ||
            (body.IsReachable(id) && (!this.Dominates(receiver, id) ||
                (body.GetInputState(id, first) & PlaceState.MustInit) == 0 || (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0)))
        {
            return Fail("Borrowed update requires matching initialized exclusive targets and acquired inputs.", out failure);
        }

        var layout = exchange || swap ? this.aggregatePlaces[operation.Place] : this.aggregateLayouts.Get(type);
        var representation = layout?.Value ?? WindowsLowering.GetValue(type);
        if (representation is null || (!ScalarTypes.Supports(type) && layout is null && !ReferenceEquals(type, BoundType.Unit) && !ReferenceEquals(type, BoundType.String)))
        {
            return Fail("Borrowed update has no complete value representation.", out failure);
        }

        // Materialize the payload/storage address with the ordinary value-borrow ABI.
        if (swap && ScalarTypes.Supports(type))
        {
            if (input < 0 || (body.IsReachable(id) && !this.Dominates(input, id)))
            {
                return Fail("Second exclusive target does not dominate swap.", out failure);
            }

            function.AddScalar(EmissionOpcode.SwapScalars, id, [this.PhysicalOperand(body, receiver), this.PhysicalOperand(body, input)], representation: representation);
            return true;
        }

        var destination = this.PhysicalOperand(body, receiver);
        if (layout is not null || ReferenceEquals(type, BoundType.String))
        {
            if (exchange || swap)
            {
                Transfer(function, id, layout, [destination], operation.Place, representation);
            }
            else if (layout?.NeedsDestruction != false)
            {
                if (!this.TryGetLocation(operation.Source, directory, constants, out var location))
                {
                    return Fail("Payload content destruction requires a location.", out failure);
                }

                AddOwnedDestruction(function, id, destination, location, layout);
            }

            var incoming = swap ? this.PhysicalOperand(body, input) : new(EmissionOperandKind.SlotAddress, operation.Input);
            Transfer(function, id, layout, [incoming, destination], representation: representation);
            if (swap)
            {
                Transfer(function, id, layout, [new(EmissionOperandKind.SlotAddress, operation.Place), incoming], representation: representation);
            }
        }
        else if (representation.Layout.Size != 0)
        {
            function.AddScalar(EmissionOpcode.ElementAddress, id, [destination, new(EmissionOperandKind.Integer, 0)], representation: representation);
            if (exchange || swap)
            {
                function.AddScalar(EmissionOpcode.LoadElement, id, [], representation.ComputationType, place: id, representation: representation);
            }

            if (input < 0 || (body.IsReachable(id) && !this.Dominates(input, id)))
            {
                return Fail("Borrowed update value does not dominate placement.", out failure);
            }

            function.AddScalar(EmissionOpcode.StoreElement, id, [this.PhysicalOperand(body, input)], representation.ComputationType, place: id, representation: representation);
        }

        this.AddStringFlags(function, operation, id);
        return true;
    }
}
