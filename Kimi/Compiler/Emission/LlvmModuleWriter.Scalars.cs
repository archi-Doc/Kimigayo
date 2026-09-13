// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void Name(TextWriter output, string prefix, int id)
    {
        output.Write(prefix);
        WriteNumber(output, id);
    }

    private static void WriteOperand(TextWriter output, EmissionOperand operand)
    {
        if (operand.Kind == EmissionOperandKind.Argument)
        {
            output.Write("%a");
        }

        if (operand.Kind == EmissionOperandKind.Value)
        {
            output.Write("%v");
        }

        if (operand.Kind == EmissionOperandKind.Block)
        {
            output.Write("%b");
        }

        WriteNumber(output, operand.Value);
    }

    private static void WriteScalar(TextWriter output, LlvmConstantPool constants, EmissionInstruction instruction, ReadOnlySpan<EmissionOperand> operands)
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
            case EmissionOpcode.LoadScalar:
                Name(output, type == "i1" ? "  %storage" : "  %v", id);
                output.Write(" = load ");
                output.Write(instruction.Representation!.Layout.StorageType);
                output.Write(", ptr %p");
                WriteNumber(output, instruction.Place);
                WriteAlignment(output, instruction.Representation.Layout.Alignment);
                if (type == "i1")
                {
                    Name(output, "  %v", id);
                    Name(output, " = trunc i8 %storage", id);
                    output.Write(" to i1\n");
                }

                return;
            case EmissionOpcode.StoreScalar:
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
                    WriteOperand(output, operands[0]);
                }

                Name(output, ", ptr %p", instruction.Place);
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
                break;
            case EmissionOpcode.Convert:
                WriteConversion(output, constants, instruction, operands);
                return;
            default:
                throw new InvalidOperationException("Unknown physical opcode.");
        }

        var op = instruction.ScalarOperator!;
        if (instruction.Check == ArithmeticCheckKind.Overflow)
        {
            Name(output, "  %checked", id);
            output.Write(" = call { ");
            output.Write(type);
            output.Write(", i1 } @llvm.");
            output.Write(op);
            output.Write(".with.overflow.");
            output.Write(type);
            output.Write('(');
            output.Write(type);
            output.Write(' ');
            WriteOperand(output, operands[0]);
            output.Write(", ");
            output.Write(type);
            output.Write(' ');
            WriteOperand(output, operands[1]);
            output.Write(")\n");
            Name(output, "  %v", id);
            WriteCheckedValue(output, type!, id);
            output.Write(", 0\n");
            Name(output, "  %overflow", id);
            WriteCheckedValue(output, type!, id);
            output.Write(", 1\n");
            WriteArithmeticFailure(output, constants, instruction, "%overflow");
            return;
        }

        if (instruction.Check == ArithmeticCheckKind.Division)
        {
            WriteEquality(output, "%zero", id, type!, operands[1], 0);
            WriteEquality(output, "%minimum", id, type!, operands[0], long.MinValue >> (64 - (instruction.Representation!.Layout.Size * 8)));
            WriteEquality(output, "%minusone", id, type!, operands[1], -1);
            Name(output, "  %overflow", id);
            Name(output, " = and i1 %minimum", id);
            Name(output, ", %minusone", id);
            output.Write('\n');
            Name(output, "  %invalid", id);
            Name(output, " = or i1 %zero", id);
            Name(output, ", %overflow", id);
            output.Write('\n');
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
        }
        else if (instruction.Check == ArithmeticCheckKind.UnsignedDivision)
        {
            WriteEquality(output, "%zero", id, type!, operands[1], 0);
            WriteArithmeticFailure(output, constants, instruction, "%zero");
        }
        else if (instruction.Check == ArithmeticCheckKind.Shift)
        {
            Name(output, "  %invalid", id);
            output.Write(" = icmp uge ");
            output.Write(instruction.CountRepresentation!.ComputationType);
            output.Write(' ');
            WriteOperand(output, operands[1]);
            output.Write(", ");
            WriteNumber(output, instruction.Representation!.Layout.Size * 8);
            output.Write('\n');
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
            if (instruction.CountRepresentation.Layout.Size != instruction.Representation.Layout.Size)
            {
                // Only successful, nonnegative counts are converted to the left operand's width.
                Name(output, "  %shift", id);
                output.Write(instruction.CountRepresentation.Layout.Size < instruction.Representation.Layout.Size ? " = zext " : " = trunc ");
                output.Write(instruction.CountRepresentation.ComputationType);
                output.Write(' ');
                WriteOperand(output, operands[1]);
                output.Write(" to ");
                output.Write(type);
                output.Write('\n');
            }
        }
        else if (instruction.Check != ArithmeticCheckKind.None)
        {
            throw new InvalidOperationException("Unknown arithmetic check.");
        }

        Name(output, "  %v", id);
        output.Write(" = ");
        if (instruction.IsComparison)
        {
            output.Write("icmp ");
        }

        output.Write(op);
        output.Write(' ');
        output.Write(type);
        output.Write(' ');
        WriteOperand(output, operands[0]);
        output.Write(", ");
        if (instruction.Check == ArithmeticCheckKind.Shift && instruction.CountRepresentation!.Layout.Size != instruction.Representation!.Layout.Size)
        {
            Name(output, "%shift", id);
        }
        else
        {
            WriteOperand(output, operands[1]);
        }

        output.Write('\n');
    }

    private static void WriteAlignment(TextWriter output, int alignment)
    {
        output.Write(", align ");
        WriteNumber(output, alignment);
        output.Write('\n');
    }

    private static void WriteCheckedValue(TextWriter output, string type, int id)
    {
        output.Write(" = extractvalue { ");
        output.Write(type);
        Name(output, ", i1 } %checked", id);
    }

    private static void WriteEquality(TextWriter output, string name, int id, string type, EmissionOperand operand, long constant)
    {
        output.Write("  ");
        Name(output, name, id);
        output.Write(" = icmp eq ");
        output.Write(type);
        output.Write(' ');
        WriteOperand(output, operand);
        output.Write(", ");
        WriteNumber(output, constant);
        output.Write('\n');
    }

    private static void WriteArithmeticFailure(TextWriter output, LlvmConstantPool constants, EmissionInstruction instruction, string condition)
    {
        var id = instruction.Operation;
        output.Write("  br i1 ");
        Name(output, condition, id);
        Name(output, ", label %abort", id);
        Name(output, ", label %b", instruction.Place);
        output.Write('\n');
        Name(output, "abort", id);
        output.Write(":\n");
        var reasonId = instruction.Check switch
        {
            ArithmeticCheckKind.Overflow or ArithmeticCheckKind.Division => WindowsLowering.IntegerOverflowReason,
            ArithmeticCheckKind.UnsignedDivision => WindowsLowering.IntegerDivisionZeroReason,
            ArithmeticCheckKind.Shift => WindowsLowering.IntegerShiftCountReason,
            ArithmeticCheckKind.Conversion => WindowsLowering.IntegerConversionReason,
            _ => throw new InvalidOperationException("Unknown arithmetic failure reason."),
        };
        var reason = new EmissionOperand(EmissionOperandKind.Integer, reasonId);
        if (instruction.Check == ArithmeticCheckKind.Division)
        {
            // The synthetic success-label ID is outside the ownership value ID range.
            // Labels use %b, so its %v name can hold this failure-only ABI argument.
            Name(output, "  %v", instruction.Place);
            Name(output, " = select i1 %zero", id);
            output.Write(", i32 ");
            WriteNumber(output, WindowsLowering.IntegerDivisionZeroReason);
            output.Write(", i32 ");
            WriteNumber(output, WindowsLowering.IntegerOverflowReason);
            output.Write('\n');
            reason = new(EmissionOperandKind.Value, instruction.Place);
        }

        WriteCall(output, constants, WindowsLowering.Abort, [reason, new(EmissionOperandKind.ConstantAddress, instruction.Constant), new(EmissionOperandKind.ConstantLength, instruction.Constant), new(EmissionOperandKind.Integer, -2)]);
        output.Write("  unreachable\n");
        Name(output, "b", instruction.Place);
        output.Write(":\n");
    }
}
