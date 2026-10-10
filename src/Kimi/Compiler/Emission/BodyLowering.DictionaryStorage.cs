// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    // Generated code addresses a Kimi library record Field by its declared name, at its offset in the record's layout.
    private static EmissionOperand FieldOffset(AggregateLayout layout, BoundType record, string name)
        => new(EmissionOperandKind.Integer, layout.Offset(record, name));

    private bool LowerDictionaryLayout(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, CallPlan plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || plan.ArgumentToParameter is not [0] || target.Parameters.Count != 1 ||
            body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Dictionary, Components: [var keyType, var valueType] }] } input ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value) ||
            body.Resolve(plan.ReturnType, InterpretationContext.Root) is not { Kind: BoundTypeKind.Tuple, Components: [var address, var stride] } result ||
            address is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] } || !ReferenceEquals(pointee, BoundType.Primitives["u8"]) ||
            !ReferenceEquals(stride, BoundType.ISize) || !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), result) ||
            this.aggregateLayouts.Get(result) is not { Fields.Length: 2 } layout || layout.Offset(0) != 0 || layout.Offset(1) != 8)
        {
            return Fail("Dictionary layout projection requires its mutable handle and physical metadata result.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ValidateSlotCallResult(body, id, out failure) || !this.ScalarArrayArgument(body, id, 0, input, out var handle))
        {
            return Fail(failure ?? "Dictionary layout projection has no acquired handle.", out failure);
        }

        var entryLayout = GetDictionaryEntryLayout(key, value);
        function.AddScalar(EmissionOpcode.Sequence, id, [handle, new(EmissionOperandKind.Integer, entryLayout.Stride)], place: body.Operations[id].Place, op: "DictionaryLayout");
        return true;
    }

    // SPEC 22.1.2.5: borrowStorage over a Dictionary copies the handle's slot buffer, first link and live count into the
    // shared or exclusive remainder with the concrete slot stride. Storage.kimi checks the untaken count and follows the
    // links before it borrows an entry's raw Places.
    private bool LowerDictionaryStorageBorrow(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, CallPlan plan, out string? failure)
    {
        failure = null;
        var exclusive = plan.Target.CompilerFunction == CompilerFunctionKind.StorageBorrowDictionaryExclusive;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || target.Parameters.Count != 1 || plan.ArgumentToParameter is not [0])
        {
            return Fail("Dictionary storage borrow has an unsupported argument plan.", out failure);
        }

        var input = body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root);
        if (input is not { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Dictionary, Components: [var keyType, var valueType] }] } ||
            input.Semantics != (exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref) ||
            plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value))
        {
            return Fail("Dictionary storage borrow has an unsupported Dictionary or entry Type.", out failure);
        }

        var returnType = body.Resolve(plan.ReturnType, InterpretationContext.Root);
        if (returnType is null || !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), returnType))
        {
            return Fail("Dictionary storage borrow result does not match its call.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        var entryLayout = GetDictionaryEntryLayout(key, value);
        if (this.aggregateLayouts.Get(returnType) is not { } remainder || !SlotTypes.IsResult(returnType) || !this.ValidateSlotCallResult(body, id, out failure))
        {
            return Fail(failure ?? "Dictionary storage borrow result is not a stored record.", out failure);
        }

        if (!this.ScalarArrayArgument(body, id, 0, input, out var handle))
        {
            return Fail("Dictionary storage borrow is unavailable at the call.", out failure);
        }

        // The remainder record is {storage, stride, link, count}, written at its named Field offsets.
        ReadOnlySpan<EmissionOperand> operands = [handle, new(EmissionOperandKind.Integer, entryLayout.Stride), FieldOffset(remainder, returnType, "storage"), FieldOffset(remainder, returnType, "stride"),
            FieldOffset(remainder, returnType, "link"), FieldOffset(remainder, returnType, "count")];
        function.AddScalar(EmissionOpcode.Sequence, id, operands, place: body.Operations[id].Place, op: "DictionaryBorrowStorage");
        return true;
    }

    // SPEC 22.1.2.5: borrowStorage over a fixed array writes the contiguous remainder {storage, position, count} as the
    // borrowed array's first element address, 0 and its concrete length N.
    private bool LowerFixedStorageBorrow(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, CallPlan plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || target.Parameters.Count != 1 || plan.ArgumentToParameter is not [0] ||
            body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root) is not { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.FixedArray, Components: [var elementType] } array] } input ||
            plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
            array.LengthExpression is not null || !this.TryGetArrayElement(elementType, out _))
        {
            return Fail("Fixed-array storage borrow needs a concrete borrowed array.", out failure);
        }

        var returnType = body.Resolve(plan.ReturnType, InterpretationContext.Root);
        if (returnType is null || !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), returnType) || this.aggregateLayouts.Get(returnType) is not { } remainder ||
            !SlotTypes.IsResult(returnType))
        {
            return Fail("Fixed-array storage borrow result is not a stored record.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ValidateSlotCallResult(body, id, out failure) || !this.ScalarArrayArgument(body, id, 0, input, out var address))
        {
            return Fail(failure ?? "Fixed-array storage borrow is unavailable at the call.", out failure);
        }

        ReadOnlySpan<EmissionOperand> operands = [address, new(EmissionOperandKind.Integer, array.Length), FieldOffset(remainder, returnType, "storage"), FieldOffset(remainder, returnType, "position"),
            FieldOffset(remainder, returnType, "count")];
        function.AddScalar(EmissionOpcode.Sequence, id, operands, place: body.Operations[id].Place, op: "FixedStorage");
        return true;
    }

    // SPEC 22.1.2.5: ownStorage transfers the acquired Dictionary handle's buffer, first and last links and live count into
    // the owning remainder, which then destroys the unreturned entries and releases the buffer; keyAt and valueAt address
    // one slot's key or value.
    private bool LowerOwnedDictionaryStorage(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, CallPlan plan, out string? failure)
    {
        failure = null;
        var owning = plan.Target.CompilerFunction == CompilerFunctionKind.StorageOwnDictionary;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || target.Parameters.Count != 1 ||
            plan.ArgumentToParameter.Length != 1 || plan.ArgumentToParameter[0] != 0 || plan.TypeArguments.Length != 2)
        {
            return Fail("Owned Dictionary storage operation has an unsupported argument plan.", out failure);
        }

        // keyAt and valueAt name K and V explicitly; ownStorage reads them from the Dictionary.
        var input = body.Resolve(plan.ArgumentOperations[0].ParameterType, InterpretationContext.Root);
        var keyType = owning ? input?.Components is [var ownedKey, _] ? ownedKey : null : body.Resolve(plan.TypeArguments[0], InterpretationContext.Root);
        var valueType = owning ? input?.Components is [_, var ownedValue] ? ownedValue : null : body.Resolve(plan.TypeArguments[1], InterpretationContext.Root);
        if (input is null || keyType is null || valueType is null ||
            (owning ? input.Kind != BoundTypeKind.Dictionary || plan.ArgumentOperations[0].Kind != ArgumentOperationKind.Value
                : plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead)) ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value))
        {
            return Fail("Owned Dictionary storage operation has an unsupported Dictionary or entry Type.", out failure);
        }

        var returnType = body.Resolve(plan.ReturnType, InterpretationContext.Root);
        var pointer = owning ? null : input;
        if (returnType is null || !ReferenceEquals(body.Resolve(call.BoundType, body.ContextAt(id)), returnType) ||
            (!owning && (pointer is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] } || !ReferenceEquals(pointee, BoundType.Primitives["u8"]) ||
                returnType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var addressed] } ||
                !ReferenceEquals(addressed, plan.Target.CompilerFunction == CompilerFunctionKind.StorageKeyAt ? keyType : valueType))))
        {
            return Fail("Owned Dictionary storage operation result does not match its entry Types.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        var entryLayout = GetDictionaryEntryLayout(key, value);
        if (!owning)
        {
            if (!this.ScalarArrayArgument(body, id, 0, pointer!, out var slot))
            {
                return Fail("Owned Dictionary storage addressing arguments are unavailable at the call.", out failure);
            }

            var offset = plan.Target.CompilerFunction == CompilerFunctionKind.StorageKeyAt ? entryLayout.KeyOffset : entryLayout.ValueOffset;
            function.AddScalar(EmissionOpcode.Sequence, id, [slot, new(EmissionOperandKind.Integer, offset)], op: "DictionaryEntryAddress");
            return true;
        }

        // The Dictionary's acquired slot is transferred into the remainder {storage, stride, link, tail, count}; the call consumes it.
        var entry = this.parameterArguments[0];
        var place = entry < 0 ? -1 : body.Operations[entry].Place;
        if (place < 0 || !ReferenceTypes.StorageMatches(input, body.Places[place].Type) || !this.IsSlotValue(body.Places[place]) ||
            (body.IsReachable(entry) && (body.GetInputState(entry, place) & PlaceState.MustInit) == 0))
        {
            return Fail("Owned Dictionary storage argument is not an initialized acquired Dictionary.", out failure);
        }

        if (this.aggregateLayouts.Get(returnType) is not { } owned || !SlotTypes.IsResult(returnType) || !this.ValidateSlotCallResult(body, id, out failure))
        {
            return Fail(failure ?? "Owned Dictionary storage result is not a stored record.", out failure);
        }

        ReadOnlySpan<EmissionOperand> operands = [new(EmissionOperandKind.SlotAddress, place), new(EmissionOperandKind.Integer, entryLayout.Stride), FieldOffset(owned, returnType, "storage"),
            FieldOffset(owned, returnType, "stride"), FieldOffset(owned, returnType, "link"), FieldOffset(owned, returnType, "tail"), FieldOffset(owned, returnType, "count")];
        function.AddScalar(EmissionOpcode.Sequence, id, operands, place: body.Operations[id].Place, op: "DictionaryOwnStorage");
        return true;
    }
}
