// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteElementAddress(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var operands = function.GetOperands(instruction);
        var id = instruction.Operation;
        if (instruction.Check == ArithmeticCheckKind.Bounds)
        {
            Name(output, "  %invalid", id);
            output.Write(" = icmp uge i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", ");
            WriteOperand(output, operands[2]);
            output.Write('\n');
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
        }

        // A zero-byte element needs no physical address; its checks and logical value remain.
        if (instruction.Representation!.Layout.Size == 0)
        {
            return;
        }

        if (instruction.Check == ArithmeticCheckKind.Bounds)
        {
            Name(output, "  %offset", id);
            output.Write(" = mul i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", ");
            WriteOperand(output, operands[3]);
            output.Write('\n');
        }

        Name(output, "  %element", id);
        output.Write(" = getelementptr i8, ptr ");
        if (operands[0].Kind == EmissionOperandKind.SlotAddress)
        {
            WriteSlot(output, function, (int)operands[0].Value);
        }
        else
        {
            WriteOperand(output, operands[0]);
        }

        output.Write(", i64 ");
        if (instruction.Check == ArithmeticCheckKind.Bounds)
        {
            Name(output, "%offset", id);
        }
        else
        {
            WriteOperand(output, operands[1]);
        }

        output.Write('\n');
    }
}
