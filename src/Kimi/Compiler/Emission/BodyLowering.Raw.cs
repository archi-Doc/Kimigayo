// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    // The heap's own alignment (windows-x64-v1); a greater element alignment is reported as unsupported (SPEC 5.6).
    private const long RawAllocationAlignment = 16;

    // SPEC 5.6: Kimi.Raw. allocate checks the count and the byte size and allocates through the runtime, returning a nonnull
    // aligned substitute for zero bytes; release frees an allocate result and ignores null and that substitute; initialize
    // moves its value into the storage and destroys nothing.
    private bool LowerRawOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        var kind = plan.Target.CompilerFunction;
        var inputs = kind == CompilerFunctionKind.RawInitialize ? 2 : 1;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null || plan.DefaultArguments.Length != 0 ||
            plan.ArgumentOperations.Length != inputs || call.ArgumentNodes.Count != inputs || target.Parameters.Count != inputs || plan.ArgumentToParameter.Length != inputs ||
            plan.ArgumentToParameter[0] != 0 || (inputs == 2 && plan.ArgumentToParameter[1] != 1) || plan.TypeArguments.Length != 1 ||
            SignatureType(this, plan.TypeArguments[0]) is not { } elementType || !this.TryGetArrayElement(elementType, out var element))
        {
            return Fail("Raw storage operation has an unsupported argument plan or element Type.", out failure);
        }

        var pointer = kind == CompilerFunctionKind.RawAllocate ? SignatureType(this, plan.ReturnType) : SignatureType(this, plan.ArgumentOperations[0].ParameterType);
        if (pointer is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] } || !ReferenceEquals(pointee, elementType) ||
            !ReferenceEquals(SignatureType(this, call.BoundType), kind == CompilerFunctionKind.RawAllocate ? pointer : BoundType.Unit) ||
            (kind == CompilerFunctionKind.RawAllocate && !ReferenceEquals(SignatureType(this, plan.ArgumentOperations[0].ParameterType), BoundType.ISize)))
        {
            return Fail("Raw storage operation does not match its element Type.", out failure);
        }

        if (kind == CompilerFunctionKind.RawAllocate && element.Value.Layout.Alignment > RawAllocationAlignment)
        {
            return Fail("Raw allocation supports element alignment up to 16 bytes.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (kind == CompilerFunctionKind.RawInitialize)
        {
            return this.LowerRawInitialize(body, function, id, pointer, element, out failure);
        }

        if (!this.ScalarArrayArgument(body, id, 0, kind == CompilerFunctionKind.RawAllocate ? BoundType.ISize : pointer, out var input) ||
            !this.TryGetLocation(call, directory, constants, out var location))
        {
            return Fail("Raw storage operation argument or location is unavailable at the call.", out failure);
        }

        var place = new EmissionOperand(EmissionOperandKind.ConstantAddress, location);
        var length = new EmissionOperand(EmissionOperandKind.ConstantLength, location);
        if (kind == CompilerFunctionKind.RawAllocate)
        {
            function.AddCall(id, WindowsLowering.RawAllocate, [input, new(EmissionOperandKind.Integer, element.Stride), place, length]);
        }
        else
        {
            function.AddCall(id, WindowsLowering.StorageRelease, [input, place, length]);
        }

        return true;
    }

    // SPEC 5.6: the value's responsibility moves into storage that holds no value, so nothing is destroyed first.
    private bool LowerRawInitialize(OwnershipBody body, EmissionFunction function, int id, BoundType pointer, in ArrayElement element, out string? failure)
    {
        failure = null;
        if (!this.ScalarArrayArgument(body, id, 0, pointer, out var destination) || !this.ArrayValueArgument(body, id, 1, element, out var value))
        {
            return Fail("Raw initialization requires a storage pointer and an acquired value.", out failure);
        }

        if (element.IsZeroSized)
        {
            return true; // Zero-sized values retain logical initialization but access no bytes.
        }

        if (element.IsScalar)
        {
            function.AddScalar(EmissionOpcode.StorePointer, id, [value, destination], element.Value.ComputationType, representation: element.Value);
            return true;
        }

        if (element.IsString)
        {
            // Field-wise string Moves take the raw address as an element address; no padding is read.
            function.AddScalar(EmissionOpcode.ElementAddress, id, [destination, new(EmissionOperandKind.Integer, 0)], representation: element.Value);
            destination = new(EmissionOperandKind.ElementAddress, id);
        }

        AddReplacement(function, id, destination, (int)value.Value, element.Value, element.Layout, element.IsString, -1, destroy: false);
        return true;
    }
}
