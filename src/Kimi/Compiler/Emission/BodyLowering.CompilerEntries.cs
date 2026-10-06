// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    // A value-call adapter receives the same acquired inputs as a direct Array operation. Reuse its per-element
    // helpers and capacity primitives; only their private argument order differs from the logical function ABI.
    internal FunctionAbi? CompilerArrayEntry(CompilerFunctionKind kind, BoundType[] inputs, BoundType result, List<EmissionOperand> operands)
    {
        if (inputs.Length == 0 || inputs[0].Components is not [{ Kind: BoundTypeKind.Array, Components: [var item] }] ||
            !this.TryGetArrayElement(item, out var element))
        {
            return null;
        }

        operands.Add(new(EmissionOperandKind.Argument, 0));
        this.arrayRuntimeUsed = true;
        FunctionAbi entry;
        switch (kind)
        {
            case CompilerFunctionKind.ArrayReserve:
            case CompilerFunctionKind.ArrayShrinkToFit:
                entry = kind == CompilerFunctionKind.ArrayReserve ? WindowsLowering.ArrayReserve : WindowsLowering.ArrayShrink;
                operands.Add(new(EmissionOperandKind.Integer, element.Stride));
                if (kind == CompilerFunctionKind.ArrayReserve)
                {
                    operands.Add(new(EmissionOperandKind.Argument, 1));
                }

                break;
            case CompilerFunctionKind.ArrayAppend:
            case CompilerFunctionKind.ArrayInsert:
                var insert = kind == CompilerFunctionKind.ArrayInsert;
                entry = this.GetArrayHelper(insert ? ArrayHelperKind.Insert : ArrayHelperKind.Append, element).Abi;
                if (insert)
                {
                    operands.Add(new(EmissionOperandKind.Argument, 1));
                }

                if (!element.IsZeroSized)
                {
                    operands.Add(new(EmissionOperandKind.Argument, insert ? 2 : 1));
                }

                break;
            case CompilerFunctionKind.ArrayPop:
                if (this.aggregateLayouts.Get(result) is not { Cases.Length: 2 } option)
                {
                    return null;
                }

                entry = this.GetArrayHelper(ArrayHelperKind.Pop, element, option).Abi;
                operands.Add(new(EmissionOperandKind.ReturnAddress, 0));
                break;
            case CompilerFunctionKind.ArrayRemove:
                entry = this.GetArrayHelper(ArrayHelperKind.Remove, element).Abi;
                operands.Add(new(EmissionOperandKind.Argument, 1));
                if (!element.IsScalar && !element.IsZeroSized)
                {
                    operands.Add(new(EmissionOperandKind.ReturnAddress, 0));
                }

                break;
            case CompilerFunctionKind.ArraySwap:
                entry = this.GetArrayHelper(ArrayHelperKind.Swap, element).Abi;
                operands.Add(new(EmissionOperandKind.Argument, 1));
                operands.Add(new(EmissionOperandKind.Argument, 2));
                break;
            case CompilerFunctionKind.ArrayClear:
                entry = this.GetArrayHelper(ArrayHelperKind.Clear, element).Abi;
                break;
            default:
                return null;
        }

        if (kind != CompilerFunctionKind.ArrayPop)
        {
            operands.Add(new(EmissionOperandKind.CallerLocation, 0));
            operands.Add(new(EmissionOperandKind.CallerLocationLength, 0));
        }

        return entry;
    }
}
