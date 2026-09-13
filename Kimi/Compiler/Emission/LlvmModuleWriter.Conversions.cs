// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteConversion(TextWriter output, LlvmConstantPool constants, EmissionInstruction instruction, ReadOnlySpan<EmissionOperand> operands)
    {
        var id = instruction.Operation;
        var sourceType = instruction.Representation!.ComputationType;
        if (instruction.LowerPredicate is { } lower)
        {
            WriteConversionBound(output, "%lower", id, lower, sourceType, operands[0], operands[1]);
        }

        if (instruction.UpperPredicate is { } upper)
        {
            WriteConversionBound(output, "%upper", id, upper, sourceType, operands[0], operands[2]);
        }

        if (instruction.Check == ArithmeticCheckKind.Conversion)
        {
            var condition = instruction.LowerPredicate is null ? "%upper" : "%lower";
            if (instruction.LowerPredicate is not null && instruction.UpperPredicate is not null)
            {
                Name(output, "  %invalid", id);
                Name(output, " = or i1 %lower", id);
                Name(output, ", %upper", id);
                output.Write('\n');
                condition = "%invalid";
            }

            WriteArithmeticFailure(output, constants, instruction, condition);
        }

        if (instruction.ScalarOperator is { } op)
        {
            Name(output, "  %v", id);
            output.Write(" = ");
            output.Write(op);
            output.Write(' ');
            output.Write(sourceType);
            output.Write(' ');
            WriteOperand(output, operands[0]);
            output.Write(" to ");
            output.Write(instruction.ScalarType);
            output.Write('\n');
        }
    }

    private static void WriteConversionBound(TextWriter output, string name, int id, string predicate, string type, EmissionOperand input, EmissionOperand bound)
    {
        output.Write("  ");
        Name(output, name, id);
        output.Write(" = icmp ");
        output.Write(predicate);
        output.Write(' ');
        output.Write(type);
        output.Write(' ');
        WriteOperand(output, input);
        output.Write(", ");
        WriteOperand(output, bound);
        output.Write('\n');
    }
}
