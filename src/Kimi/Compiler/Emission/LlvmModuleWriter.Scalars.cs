// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Backend;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // The longest operand spelling: %element and an Int128.
    private const int OperandLength = 48;

    private static void Name(TextWriter output, string prefix, int id)
    {
        output.Write(prefix);
        WriteNumber(output, id);
    }

    private static void WriteOperand(TextWriter output, EmissionOperand operand)
        => output.Write(Spell(operand, stackalloc char[OperandLength]));

    // An operand's LLVM spelling, formatted into text, which holds OperandLength characters.
    private static ReadOnlySpan<char> Spell(EmissionOperand operand, Span<char> text)
    {
        var prefix = operand.Kind switch
        {
            EmissionOperandKind.CallerLocation => "%location",
            EmissionOperandKind.CallerLocationLength => "%location_length",
            EmissionOperandKind.NullAddress => "null",
            EmissionOperandKind.Float32 or EmissionOperandKind.Float64 => "0x",
            EmissionOperandKind.ElementAddress => "%element",
            EmissionOperandKind.Argument => "%a",
            EmissionOperandKind.Value => "%v",
            EmissionOperandKind.Block => "%b",
            _ => string.Empty,
        };
        prefix.CopyTo(text);
        var digits = text[prefix.Length..];
        var length = 0;
        if (operand.Kind is EmissionOperandKind.Float32 or EmissionOperandKind.Float64)
        {
            // LLVM's hexadecimal float spelling uses the exactly extended double bits.
            var bits = operand.Kind == EmissionOperandKind.Float32
                ? BitConverter.DoubleToUInt64Bits(BitConverter.UInt32BitsToSingle((uint)operand.Value)) : unchecked((ulong)operand.Value);
            bits.TryFormat(digits, out length, "X16", CultureInfo.InvariantCulture);
        }
        else if (operand.Kind is not (EmissionOperandKind.CallerLocation or EmissionOperandKind.CallerLocationLength or EmissionOperandKind.NullAddress))
        {
            operand.Value.TryFormat(digits, out length, default, CultureInfo.InvariantCulture);
        }

        return text[..(prefix.Length + length)];
    }

    private static void WriteScalar(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction, ReadOnlySpan<EmissionOperand> operands)
    {
        var id = instruction.Operation;
        var type = instruction.ScalarType;
        switch (instruction.Opcode)
        {
            case EmissionOpcode.ReturnScalar:
                output.Write("  ret ");
                output.Write(type);
                output.Write(' ');
                WriteOperand(output, operands[0]);
                output.Write('\n');
                return;
            case EmissionOpcode.Label:
                Name(output, "b", id);
                output.Write(":\n");
                return;
            case EmissionOpcode.Branch:
                output.Write("  br label ");
                WriteOperand(output, operands[0]);
                output.Write('\n');
                return;
            case EmissionOpcode.ConditionalBranch:
                output.Write("  br i1 ");
                WriteOperand(output, operands[0]);
                output.Write(", label ");
                WriteOperand(output, operands[1]);
                output.Write(", label ");
                WriteOperand(output, operands[2]);
                output.Write('\n');
                return;
            case EmissionOpcode.LoadElement:
            case EmissionOpcode.LoadScalar:
            case EmissionOpcode.LoadPointer:
                Name(output, type == "i1" ? "  %storage" : "  %v", id);
                output.Write(" = load ");
                output.Write(instruction.Representation!.Layout.StorageType);
                if (instruction.Opcode == EmissionOpcode.LoadPointer)
                {
                    output.Write(", ptr ");
                    WriteOperand(output, operands[0]);
                }
                else
                {
                    output.Write(", ptr ");
                    if (instruction.Opcode == EmissionOpcode.LoadElement)
                    {
                        Name(output, "%element", instruction.Place);
                    }
                    else
                    {
                        WriteSlot(output, function, instruction.Place);
                    }
                }

                WriteAlignment(output, instruction.Representation.Layout.Alignment);
                if (type == "i1")
                {
                    Name(output, "  %v", id);
                    Name(output, " = trunc i8 %storage", id);
                    output.Write(" to i1\n");
                }

                return;
            case EmissionOpcode.StoreScalar:
            case EmissionOpcode.StoreElement:
            case EmissionOpcode.StorePointer:
                if (type == "i1")
                {
                    Name(output, "  %storage", id);
                    output.Write(" = zext i1 ");
                    WriteOperand(output, operands[0]);
                    output.Write(" to i8\n  store i8 ");
                    Name(output, "%storage", id);
                }
                else
                {
                    output.Write("  store ");
                    output.Write(type);
                    output.Write(' ');
                    WriteStorageAddress(output, function, operands[0]);
                }

                if (instruction.Opcode == EmissionOpcode.StorePointer)
                {
                    output.Write(", ptr ");
                    WriteOperand(output, operands[1]);
                }
                else
                {
                    output.Write(", ptr ");
                    if (instruction.Opcode == EmissionOpcode.StoreElement)
                    {
                        Name(output, "%element", instruction.Place);
                    }
                    else
                    {
                        WriteSlot(output, function, instruction.Place);
                    }
                }

                WriteAlignment(output, instruction.Representation!.Layout.Alignment);
                return;
            case EmissionOpcode.Phi:
                Name(output, "  %v", id);
                output.Write(" = phi ");
                output.Write(type);
                for (var i = 0; i < operands.Length; i += 2)
                {
                    output.Write(i == 0 ? " [ " : ", [ ");
                    WriteOperand(output, operands[i]);
                    output.Write(", ");
                    WriteOperand(output, operands[i + 1]);
                    output.Write(" ]");
                }

                output.Write('\n');
                return;
            case EmissionOpcode.Scalar:
                WriteArithmetic(output, constants, function, instruction, operands);
                return;
            case EmissionOpcode.PointerOffset:
                // SPEC 5.3: count * signed stride bytes; out-of-allocation results are the program's undefined
                // behavior, so no inbounds, nsw or other attribute is claimed.
                Name(output, "  %offset", id);
                output.Write(" = mul i64 ");
                WriteOperand(output, operands[1]);
                output.Write(", ");
                WriteNumber(output, instruction.Constant);
                output.Write('\n');
                Name(output, "  %v", id);
                output.Write(" = getelementptr i8, ptr ");
                WriteOperand(output, operands[0]);
                Name(output, ", i64 %offset", id);
                output.Write('\n');
                return;
            case EmissionOpcode.Convert:
                WriteConversion(output, constants, instruction, operands);
                return;
            default:
                throw new InvalidOperationException("Unknown physical opcode.");
        }
    }

    // Decodes a Scalar instruction for LlvmScalarText: its operation, and the check its kind selects.
    private static void WriteArithmetic(
        TextWriter output, LlvmConstantPool constants, EmissionFunction function, in EmissionInstruction instruction, ReadOnlySpan<EmissionOperand> operands)
    {
        Span<char> left = stackalloc char[OperandLength];
        Span<char> right = stackalloc char[OperandLength];
        Span<char> result = stackalloc char[OperandLength];
        var operation = new ScalarOperation(
            instruction.ScalarOperator!,
            instruction.ScalarType!,
            Spell(operands[0], left),
            operands.Length > 1 ? Spell(operands[1], right) : default,
            Spell(new(EmissionOperandKind.Value, instruction.Operation), result),
            instruction.IsComparison);
        if (instruction.Check == ArithmeticCheckKind.None)
        {
            LlvmScalarText.WriteOperation(output, operation);
            return;
        }

        Span<char> success = stackalloc char[OperandLength];
        var check = CheckOf(constants, instruction, function.Abi.CallerLocation, success);
        switch (instruction.Check)
        {
            case ArithmeticCheckKind.Overflow:
                LlvmScalarText.WriteOverflowChecked(output, operation, check);
                return;
            case ArithmeticCheckKind.Division:
                // The synthetic success-label ID is outside the ownership value ID range, so its %v name can hold the reason.
                Span<char> reason = stackalloc char[OperandLength];
                LlvmScalarText.WriteSignedQuotient(output, operation, check, Spell(new(EmissionOperandKind.Value, instruction.Place), reason));
                return;
            case ArithmeticCheckKind.DivisionZero:
                LlvmScalarText.WriteZeroChecked(output, operation, check);
                return;
            case ArithmeticCheckKind.WrappingDivision:
                LlvmScalarText.WriteWrappingQuotient(output, operation, check);
                return;
            case ArithmeticCheckKind.Shift:
                LlvmScalarText.WriteShift(output, operation, check, instruction.CountRepresentation!.ComputationType);
                return;
            default:
                throw new InvalidOperationException("Unknown arithmetic check.");
        }
    }

    private static void WriteAlignment(TextWriter output, int alignment)
    {
        output.Write(", align ");
        WriteNumber(output, alignment);
        output.Write('\n');
    }

    // Decodes the failure block of a conversion or bounds check for LlvmScalarText.
    private static void WriteArithmeticFailure(TextWriter output, LlvmConstantPool constants, EmissionInstruction instruction, string condition, bool callerLocation = false)
    {
        var reason = instruction.Check switch
        {
            ArithmeticCheckKind.Conversion => AbortReasons.IntegerConversion,
            ArithmeticCheckKind.FloatingConversion => AbortReasons.FloatingConversion,
            ArithmeticCheckKind.Bounds => AbortReasons.IndexBounds,
            ArithmeticCheckKind.Argument => AbortReasons.ArgumentRange,
            _ => throw new InvalidOperationException("Unknown arithmetic failure reason."),
        };
        Span<char> success = stackalloc char[OperandLength];
        LlvmScalarText.WriteFailure(output, condition, reason, CheckOf(constants, instruction, callerLocation, success));
    }

    // A check's names: its operation ID and the success label at the instruction's Place; its location constant, or the caller's.
    private static ScalarCheck CheckOf(LlvmConstantPool constants, in EmissionInstruction instruction, bool callerLocation, Span<char> success)
        => new(
            instruction.Operation, Spell(new(EmissionOperandKind.Block, instruction.Place), success), callerLocation ? null : constants[instruction.Constant]);
}
