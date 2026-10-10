// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>PLAN G33, SPEC 22.1.2.5: a consumed fixed array moves into the inline storage of its owning remainder, and
/// inlineBase publishes that storage's element address for the Kimigayo remainder operations.</summary>
internal sealed partial class BodyLowering
{
    private bool LowerFixedStorageOwn(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, CallPlan plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || plan.ArgumentToParameter.Length != 1 || target.Parameters.Count != 1 ||
            plan.ArgumentOperations[0].Kind != ArgumentOperationKind.Value ||
            body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root) is not { Kind: BoundTypeKind.FixedArray, LengthExpression: null } array ||
            !this.TryGetArrayElement(array, out var whole))
        {
            return Fail("Fixed-array ownStorage has an unsupported argument plan or array Type.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        var returnType = body.Resolve(plan.ReturnType, InterpretationContext.Root);
        if (returnType is null || !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), returnType) || this.aggregateLayouts.Get(returnType) is not { } remainder ||
            !SlotTypes.IsResult(returnType) || !this.ValidateSlotCallResult(body, id, out failure))
        {
            return Fail(failure ?? "Fixed-array ownStorage result is not a stored record.", out failure);
        }

        // The whole array's layout keys the helper; the remainder record gives the named offsets and the storage size.
        var helper = this.GetArrayHelper(ArrayHelperKind.OwnFixedStorage, whole, remainder: remainder, fields: GetRemainderFields(remainder, returnType, false));
        this.callOperands.Clear();
        if (helper.Fields.StorageSize != 0)
        {
            // A zero-sized array has no slot; otherwise its acquired slot is transferred into the remainder.
            var entry = this.parameterArguments[0];
            var place = entry < 0 ? -1 : body.Operations[entry].Place;
            if (place < 0 || !ReferenceTypes.StorageMatches(array, body.Places[place].Type) || !this.IsSlotValue(body.Places[place]) ||
                (body.IsReachable(entry) && (body.GetInputState(entry, place) & PlaceState.MustInit) == 0))
            {
                return Fail("Fixed-array ownStorage argument is not an initialized acquired array.", out failure);
            }

            this.callOperands.Add(new(EmissionOperandKind.SlotAddress, place));
        }

        this.callOperands.Add(new(EmissionOperandKind.Integer, array.Length));
        this.callOperands.Add(new(EmissionOperandKind.SlotAddress, body.Operations[id].Place));
        function.AddCall(id, helper.Abi, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }
}
