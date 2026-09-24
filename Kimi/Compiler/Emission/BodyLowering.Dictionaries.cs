// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool dictionaryRuntimeUsed;

    private static long AlignDictionary(long size, int alignment) => (size + alignment - 1) & -(long)alignment;

    private bool LowerDictionaryOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.CompilerFunction != CompilerFunctionKind.DictionaryReserve ||
            plan.Target.Declaration is not FunctionKoto target || plan.Receiver is null || call.AttributeChain is not null ||
            plan.ReceiverOperation.Kind is not (ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != call.ArgumentNodes.Count || plan.ArgumentToParameter.Length != call.ArgumentNodes.Count ||
            call.ArgumentNodes.Count + 1 != target.Parameters.Count || target.BoundSymbol?.ReceiverIndex != 0 ||
            plan.ReceiverOperation.ParameterType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Dictionary, Components.Count: 2 } dictionary] } receiverType ||
            !this.TryGetArrayElement(dictionary.Components[0], out var key) || !this.TryGetArrayElement(dictionary.Components[1], out var value))
        {
            return Fail("Dictionary operation requires a supported receiver, acquired arguments and entry layout.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, receiverType, out var handle) ||
            !this.ScalarArrayArgument(body, id, 1, BoundType.ISize, out var additional) ||
            !ReferenceEquals(SignatureType(this, call.BoundType), BoundType.Unit) ||
            !this.TryGetLocation(call, directory, constants, out var location))
        {
            return Fail("Dictionary reserve requires its acquired receiver and additional count.", out failure);
        }

        var alignment = Math.Max(8, Math.Max(key.Value.Layout.Alignment, value.Value.Layout.Alignment));
        var keyOffset = AlignDictionary(16, key.Value.Layout.Alignment);
        var valueOffset = AlignDictionary(keyOffset + key.Value.Layout.Size, value.Value.Layout.Alignment);
        var stride = AlignDictionary(valueOffset + value.Value.Layout.Size, alignment);
        this.callOperands.Clear();
        this.callOperands.Add(handle);
        this.callOperands.Add(new(EmissionOperandKind.Integer, stride));
        this.callOperands.Add(additional);
        this.callOperands.Add(new(EmissionOperandKind.ConstantAddress, location));
        this.callOperands.Add(new(EmissionOperandKind.ConstantLength, location));
        this.dictionaryRuntimeUsed = true;
        this.arrayRuntimeUsed = true;
        function.AddCall(id, WindowsLowering.DictionaryReserve, CollectionsMarshal.AsSpan(this.callOperands));
        return true;
    }
}
