// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteClosureTableName(TextWriter output, EmissionFunction function, int id)
    {
        output.Write("__kimi_closure_");
        output.Write(function.Abi.Name);
        Name(output, "_", id);
    }

    private static void WriteClosure(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        var operands = function.GetOperands(instruction);
        output.Write("  store i64 0, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", align 8\n");
        for (var i = 0; i < operands.Length; i++)
        {
            var field = instruction.Pattern![i];
            if (field.Representation.Layout.Size == 0)
            {
                continue;
            }

            var boolean = field.Representation.ComputationType == "i1";
            if (boolean)
            {
                Name(output, "  %captureBool", id);
                Name(output, "_", i);
                output.Write(" = zext i1 ");
                WriteOperand(output, operands[i]);
                output.Write(" to i8\n");
            }

            Name(output, "  %captureAddress", id);
            Name(output, "_", i);
            output.Write(" = getelementptr i8, ptr ");
            WriteSlot(output, function, instruction.Place);
            Name(output, ", i64 ", field.Offset);
            output.Write("\n  store ");
            output.Write(field.Representation.Layout.StorageType);
            output.Write(' ');
            if (boolean)
            {
                Name(output, "%captureBool", id);
                Name(output, "_", i);
            }
            else
            {
                WriteOperand(output, operands[i]);
            }

            Name(output, ", ptr %captureAddress", id);
            Name(output, "_", i);
            Name(output, ", align ", field.Representation.Layout.Alignment);
            output.Write('\n');
        }

        Name(output, "  %operationsSlot", id);
        output.Write(" = getelementptr i8, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", i64 8\n  store ptr @");
        WriteClosureTableName(output, function, id);
        Name(output, ", ptr %operationsSlot", id);
        output.Write(", align 8\n");
    }

    private static void WriteValueCall(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        Name(output, "  %environment", id);
        output.Write(" = load i64, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", align 8\n");
        Name(output, "  %operationsSlot", id);
        output.Write(" = getelementptr i8, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", i64 8\n");
        Name(output, "  %operations", id);
        Name(output, " = load ptr, ptr %operationsSlot", id);
        output.Write(", align 8\n");
        Name(output, "  %entry", id);
        Name(output, " = load ptr, ptr %operations", id);
        output.Write(", align 8\n");
        Name(output, "  %contextSlot", id);
        Name(output, " = getelementptr i8, ptr %operations", id);
        output.Write(", i64 8\n");
        Name(output, "  %context", id);
        Name(output, " = load ptr, ptr %contextSlot", id);
        output.Write(", align 8\n");
        var abi = instruction.Callee!;
        if (abi.Result != "void")
        {
            Name(output, "  %v", id);
            output.Write(" = call ");
        }
        else
        {
            output.Write("  call ");
        }

        output.Write(abi.Result);
        Name(output, " %entry", id);
        Name(output, "(i64 %environment", id);
        var operands = function.GetOperands(instruction);
        for (var i = 0; i < operands.Length; i++)
        {
            output.Write(", ");
            output.Write(abi.Parameters[i].Type);
            output.Write(' ');
            WriteOperand(output, operands[i]);
        }

        Name(output, ", ptr %context", id);
        output.Write(")\n");
    }
}
