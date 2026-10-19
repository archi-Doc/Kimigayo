// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // Decodes a Convert instruction for LlvmScalarText: its conversion, and the form its check and bounds select. A conversion
    // check reports its own location even in a body that forwards its caller's (SPEC 22.5.1 asks for the caller's; no
    // forwarding library body contains one).
    private static void WriteConversion(
        TextWriter output, LlvmConstantPool constants, in EmissionInstruction instruction, ReadOnlySpan<EmissionOperand> operands)
    {
        Span<char> input = stackalloc char[OperandLength];
        Span<char> result = stackalloc char[OperandLength];
        Span<char> lower = stackalloc char[OperandLength];
        Span<char> upper = stackalloc char[OperandLength];
        var bounded = instruction.LowerPredicate is not null || instruction.UpperPredicate is not null;
        var conversion = new ScalarConversion(
            instruction.ScalarOperator,
            instruction.Representation!.ComputationType,
            instruction.ScalarType!,
            Spell(operands[0], input),
            Spell(new(EmissionOperandKind.Value, instruction.Operation), result),
            new(
                instruction.LowerPredicate,
                bounded ? Spell(operands[1], lower) : default,
                instruction.UpperPredicate,
                bounded ? Spell(operands[2], upper) : default));
        Span<char> names = stackalloc char[CheckLength];
        switch (instruction.Check)
        {
            case ArithmeticCheckKind.None when bounded:
                // SPEC 4.7.1: an unchecked conversion with bounds is a position.
                LlvmScalarText.WritePositionConversion(output, conversion, instruction.Operation);
                return;
            case ArithmeticCheckKind.None:
                LlvmScalarText.WriteConversion(output, conversion);
                return;
            case ArithmeticCheckKind.Conversion:
                LlvmScalarText.WriteCheckedConversion(output, conversion, CheckOf(constants, instruction, callerLocation: false, names));
                return;
            case ArithmeticCheckKind.FloatingConversion:
                LlvmScalarText.WriteFloatingNarrowing(output, conversion, CheckOf(constants, instruction, callerLocation: false, names));
                return;
            default:
                throw new InvalidOperationException("Unknown conversion check.");
        }
    }
}
