// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    // SPEC 22.1.2.5: borrowStorage over a Dictionary copies the handle's slot buffer, first link and live count into the
    // shared remainder with the concrete slot stride; lendKey and lendValue publish one slot's key or value address.
    // Storage.kimi checks the untaken count and follows the links before lending.
    private bool LowerDictionaryStorageOperation(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var borrow = plan.Target.CompilerFunction == CompilerFunctionKind.StorageBorrowDictionary;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != (borrow ? 1 : 2) || call.ArgumentNodes.Count != plan.ArgumentOperations.Length || target.Parameters.Count != plan.ArgumentOperations.Length ||
            plan.ArgumentToParameter.Length != plan.ArgumentOperations.Length || plan.ArgumentToParameter[0] != 0 || (!borrow && plan.ArgumentToParameter[1] != 1))
        {
            return Fail("Dictionary storage operation has an unsupported argument plan.", out failure);
        }

        var input = SignatureType(this, plan.ArgumentOperations[0].ParameterType);
        if (input is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var source] } ||
            plan.ArgumentOperations[0].Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.CopyRead or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
            source.Components is not [var keyType, var valueType] ||
            (borrow ? source.Kind != BoundTypeKind.Dictionary : source.Symbol?.LibraryDeclaration != KimiDeclarationId.DictionaryRefRemainder) ||
            !this.TryGetArrayElement(keyType, out var key, allowEmpty: true) || !this.TryGetArrayElement(valueType, out var value, allowEmpty: true))
        {
            return Fail("Dictionary storage operation has an unsupported Dictionary or entry Type.", out failure);
        }

        var pointer = borrow ? null : SignatureType(this, plan.ArgumentOperations[1].ParameterType);
        var returnType = SignatureType(this, plan.ReturnType);
        if (returnType is null || !ReferenceEquals(SignatureType(this, call.BoundType), returnType) ||
            (!borrow && (pointer is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components: [var pointee] } || !ReferenceEquals(pointee, BoundType.Primitives["u8"]) ||
                returnType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var lent] } ||
                !ReferenceEquals(lent, plan.Target.CompilerFunction == CompilerFunctionKind.StorageLendKey ? keyType : valueType))))
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

            var offset = plan.Target.CompilerFunction == CompilerFunctionKind.StorageLendKey ? helper.KeyOffset : helper.ValueOffset;
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
}
