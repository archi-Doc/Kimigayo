// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerDictionaryLayout(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || plan.ArgumentToParameter is not [0] || target.Parameters.Count != 1 ||
            SignatureType(this, plan.ArgumentOperations[0].ParameterType) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Dictionary, Components: [var keyType, var valueType] }] } input ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value) ||
            SignatureType(this, plan.ReturnType) is not { Kind: BoundTypeKind.Tuple, Components: [var address, var stride] } result ||
            address is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components: [var pointee] } || !ReferenceEquals(pointee, BoundType.Primitives["u8"]) ||
            !ReferenceEquals(stride, BoundType.ISize) || !ReferenceEquals(SignatureType(this, call.BoundType), result) ||
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

        var helper = this.GetDictionaryHelper(DictionaryHelperKind.Clear, key, value);
        function.AddScalar(EmissionOpcode.Sequence, id, [handle, new(EmissionOperandKind.Integer, helper.Stride)], place: body.Operations[id].Place, op: "DictionaryLayout");
        return true;
    }

    // SPEC 22.1.2.5: borrowStorage over a Dictionary copies the handle's slot buffer, first link and live count into the
    // shared remainder with the concrete slot stride; lendKey and lendValue publish one slot's key or value address.
    // Storage.kimi checks the untaken count and follows the links before lending.
    private bool LowerDictionaryStorageOperation(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var kind = plan.Target.CompilerFunction;
        var borrow = kind is CompilerFunctionKind.StorageBorrowDictionary or CompilerFunctionKind.StorageBorrowDictionaryExclusive;
        var exclusive = kind is CompilerFunctionKind.StorageBorrowDictionaryExclusive or CompilerFunctionKind.StorageLendUniqKey or CompilerFunctionKind.StorageSplitValue;
        var lendsKey = kind is CompilerFunctionKind.StorageLendKey or CompilerFunctionKind.StorageLendUniqKey;
        var inputSemantics = kind is CompilerFunctionKind.StorageBorrowDictionaryExclusive or CompilerFunctionKind.StorageSplitValue ? SemanticsKind.Uniq : SemanticsKind.Ref;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != (borrow ? 1 : 2) || call.ArgumentNodes.Count != plan.ArgumentOperations.Length || target.Parameters.Count != plan.ArgumentOperations.Length ||
            plan.ArgumentToParameter.Length != plan.ArgumentOperations.Length || plan.ArgumentToParameter[0] != 0 || (!borrow && plan.ArgumentToParameter[1] != 1))
        {
            return Fail("Dictionary storage operation has an unsupported argument plan.", out failure);
        }

        var input = SignatureType(this, plan.ArgumentOperations[0].ParameterType);
        if (input is not { Kind: BoundTypeKind.Semantics, Components: [var source] } || input.Semantics != inputSemantics ||
            plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
            source.Components is not [var keyType, var valueType] ||
            (borrow ? source.Kind != BoundTypeKind.Dictionary : source.Symbol?.LibraryDeclaration != (exclusive ? KimiDeclarationId.DictionaryUniqRemainder : KimiDeclarationId.DictionaryRefRemainder)) ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value))
        {
            return Fail("Dictionary storage operation has an unsupported Dictionary or entry Type.", out failure);
        }

        var pointer = borrow ? null : SignatureType(this, plan.ArgumentOperations[1].ParameterType);
        var returnType = SignatureType(this, plan.ReturnType);
        if (returnType is null || !ReferenceEquals(SignatureType(this, call.BoundType), returnType) ||
            (!borrow && (pointer is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components: [var pointee] } || !ReferenceEquals(pointee, BoundType.Primitives["u8"]) ||
                returnType is not { Kind: BoundTypeKind.Semantics, Components: [var lent] } ||
                returnType.Semantics != (kind == CompilerFunctionKind.StorageSplitValue ? SemanticsKind.Uniq : SemanticsKind.Ref) || !ReferenceEquals(lent, lendsKey ? keyType : valueType))))
        {
            return Fail("Dictionary storage operation result does not match its entry Types.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        var helper = this.GetDictionaryHelper(DictionaryHelperKind.Clear, key, value);
        if (!borrow)
        {
            if (!this.ScalarArrayArgument(body, id, 0, input, out _) || !this.ScalarArrayArgument(body, id, 1, pointer!, out var slot))
            {
                return Fail("Dictionary storage lending arguments are unavailable at the call.", out failure);
            }

            var offset = lendsKey ? helper.KeyOffset : helper.ValueOffset;
            function.AddScalar(EmissionOpcode.Sequence, id, [slot, new(EmissionOperandKind.Integer, offset)], op: "DictionaryEntryAddress");
            return true;
        }

        // The remainder record is {storage, stride, link, count}.
        if (this.aggregateLayouts.Get(returnType) is not { IsArray: false } remainder || !SlotTypes.IsResult(returnType) || remainder.Fields.Length != 4 ||
            remainder.Offset(0) != 0 || remainder.Offset(1) != 8 || remainder.Offset(2) != 16 || remainder.Offset(3) != 24 || !this.ValidateSlotCallResult(body, id, out failure))
        {
            return Fail(failure ?? "Dictionary remainder does not have the boundary's shape.", out failure);
        }

        if (!this.ScalarArrayArgument(body, id, 0, input, out var handle))
        {
            return Fail("Dictionary storage borrow is unavailable at the call.", out failure);
        }

        function.AddScalar(EmissionOpcode.Sequence, id, [handle, new(EmissionOperandKind.Integer, helper.Stride)], place: body.Operations[id].Place, op: "DictionaryBorrowStorage");
        return true;
    }

    // SPEC 22.1.2.5: borrowStorage over a fixed array writes the contiguous remainder {storage, position, count} as the
    // borrowed array's first element address, 0 and its concrete length N.
    private bool LowerFixedStorageBorrow(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 || target.Parameters.Count != 1 || plan.ArgumentToParameter is not [0] ||
            SignatureType(this, plan.ArgumentOperations[0].ParameterType) is not { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.FixedArray, Components: [var elementType] } array] } input ||
            plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
            array.LengthExpression is not null || !this.TryGetArrayElement(elementType, out _))
        {
            return Fail("Fixed-array storage borrow needs a concrete borrowed array.", out failure);
        }

        var returnType = SignatureType(this, plan.ReturnType);
        if (returnType is null || !ReferenceEquals(SignatureType(this, call.BoundType), returnType) || this.aggregateLayouts.Get(returnType) is not { IsArray: false } remainder ||
            !SlotTypes.IsResult(returnType) || remainder.Fields.Length != 3 || remainder.Offset(0) != 0 || remainder.Offset(1) != 8 || remainder.Offset(2) != 16)
        {
            return Fail("Fixed-array storage borrow result is not the contiguous remainder.", out failure);
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

        function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, array.Length)], place: body.Operations[id].Place, op: "FixedStorage");
        return true;
    }

    // SPEC 22.1.2.5: ownStorage transfers the acquired Dictionary handle's buffer, first and last links and live count into
    // the owning remainder, which then destroys the unreturned entries and releases the buffer; keyAt and valueAt address
    // one slot's key or value.
    private bool LowerOwnedDictionaryStorage(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
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
        var input = SignatureType(this, plan.ArgumentOperations[0].ParameterType);
        var keyType = owning ? input?.Components is [var ownedKey, _] ? ownedKey : null : SignatureType(this, plan.TypeArguments[0]);
        var valueType = owning ? input?.Components is [_, var ownedValue] ? ownedValue : null : SignatureType(this, plan.TypeArguments[1]);
        if (input is null || keyType is null || valueType is null ||
            (owning ? input.Kind != BoundTypeKind.Dictionary || plan.ArgumentOperations[0].Kind != ArgumentOperationKind.Value
                : plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead)) ||
            !this.TryGetArrayElement(keyType, out var key) || !this.TryGetArrayElement(valueType, out var value))
        {
            return Fail("Owned Dictionary storage operation has an unsupported Dictionary or entry Type.", out failure);
        }

        var returnType = SignatureType(this, plan.ReturnType);
        var pointer = owning ? null : input;
        if (returnType is null || !ReferenceEquals(SignatureType(this, call.BoundType), returnType) ||
            (!owning && (pointer is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components: [var pointee] } || !ReferenceEquals(pointee, BoundType.Primitives["u8"]) ||
                returnType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components: [var addressed] } ||
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

        var helper = this.GetDictionaryHelper(DictionaryHelperKind.Clear, key, value);
        if (!owning)
        {
            if (!this.ScalarArrayArgument(body, id, 0, pointer!, out var slot))
            {
                return Fail("Owned Dictionary storage addressing arguments are unavailable at the call.", out failure);
            }

            var offset = plan.Target.CompilerFunction == CompilerFunctionKind.StorageKeyAt ? helper.KeyOffset : helper.ValueOffset;
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

        if (this.aggregateLayouts.Get(returnType) is not { IsArray: false } owned || !SlotTypes.IsResult(returnType) || owned.Fields.Length != 5 ||
            owned.Offset(0) != 0 || owned.Offset(1) != 8 || owned.Offset(2) != 16 || owned.Offset(3) != 24 || owned.Offset(4) != 32 || !this.ValidateSlotCallResult(body, id, out failure))
        {
            return Fail(failure ?? "Owned Dictionary remainder does not have the boundary's shape.", out failure);
        }

        function.AddScalar(EmissionOpcode.Sequence, id, [new(EmissionOperandKind.SlotAddress, place), new(EmissionOperandKind.Integer, helper.Stride)], place: body.Operations[id].Place, op: "DictionaryOwnStorage");
        return true;
    }
}
