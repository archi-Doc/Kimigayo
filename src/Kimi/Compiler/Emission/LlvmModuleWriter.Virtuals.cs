// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteVirtualCall(TextWriter output, LlvmConstantPool constants, EmissionFunction function, in EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        var operands = function.GetOperands(instruction);
        Name(output, "  %virtual_descriptor", id);
        output.Write(" = load ptr, ptr ");
        WriteOperand(output, operands[instruction.Place]);
        output.Write(", align 8\n");
        Name(output, "  %virtual_table_slot", id);
        output.Write(" = getelementptr i8, ptr ");
        Name(output, "%virtual_descriptor", id);
        output.Write(", i64 24\n");
        Name(output, "  %virtual_table", id);
        output.Write(" = load ptr, ptr ");
        Name(output, "%virtual_table_slot", id);
        output.Write(", align 8\n");
        Name(output, "  %virtual_slot", id);
        output.Write(" = getelementptr ptr, ptr ");
        Name(output, "%virtual_table", id);
        output.Write(", i64 ");
        WriteNumber(output, instruction.Constant);
        output.Write('\n');
        Name(output, "  %virtual_entry", id);
        output.Write(" = load ptr, ptr ");
        Name(output, "%virtual_slot", id);
        output.Write(", align 8\n");
        WriteCall(output, constants, instruction.Callee!, operands, id, function, indirect: true);
    }
}
