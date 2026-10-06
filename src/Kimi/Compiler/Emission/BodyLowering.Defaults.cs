// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerDefaultCall(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var evaluation = body.DefaultEvaluations![(int)body.Values[id].Constant];
        if (this.Defaults?.Get(body, evaluation, this.aggregateLayouts, out failure) is not { } entry)
        {
            return Fail(failure ?? "Default evaluator has no verified signature.", out failure);
        }

        var result = body.Operations[id].Place;
        if (!Binding.SameModuloOpenOrigins(body.Places[result].Type, entry.Result) ||
            (SlotTypes.IsResult(entry.Result) && !this.ValidateSlotCallResult(body, id, out failure)))
        {
            return Fail(failure ?? "Default evaluator result does not match its prepared slot.", out failure);
        }

        this.callOperands.Clear();
        foreach (var parameter in entry.Abi.Parameters)
        {
            if (parameter.Kind == AbiParameterKind.ResultSlot)
            {
                this.callOperands.Add(new(EmissionOperandKind.SlotAddress, result));
                continue;
            }

            var input = body.DefaultInputs![evaluation.Start + parameter.LogicalIndex];
            var type = body.Places[input.Place].Type;
            if (!Binding.SameModuloOpenOrigins(type, entry.Parameters[parameter.LogicalIndex]) ||
                (body.IsReachable(id) && ((body.GetInputState(id, input.Place) & PlaceState.MustInit) == 0 || !this.Dominates(input.Read, id))))
            {
                return Fail("Default evaluator input is not the initialized prepared value of its parameter.", out failure);
            }

            if (parameter.Kind == AbiParameterKind.PreparedSlot)
            {
                this.callOperands.Add(new(EmissionOperandKind.SlotAddress, input.Place));
            }
            else
            {
                return Fail("Default evaluator input must preserve the prepared slot address.", out failure);
            }
        }

        function.AddCall(id, entry.Abi, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }
}
