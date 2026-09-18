// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteErasureAdapter(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var name = "__kimi_erase_" + function.Abi.Name + "_" + instruction.Operation;
        var entry = instruction.Callee!;
        output.Write('@');
        WriteClosureTableName(output, function, instruction.Operation);
        output.Write($" = private constant {{ ptr, ptr, ptr }} {{ ptr @{name}, ptr null, ptr null }}, align 8\n");
        output.Write($"define internal {entry.Result} @{name}(i64 %environment");
        foreach (var parameter in entry.Parameters)
        {
            if (parameter.Kind is not (AbiParameterKind.Environment or AbiParameterKind.Context))
            {
                output.Write($", {parameter.Type} %{parameter.Name}");
            }
        }

        output.Write(", ptr %context) #0 {\nentry:\n  %storage = alloca i64, align 8\n  store i64 %environment, ptr %storage, align 8\n");
        output.Write(entry.Result == "void" ? "  call void" : $"  %result = call {entry.Result}");
        output.Write($" @{entry.Name}(ptr %storage");
        foreach (var parameter in entry.Parameters)
        {
            if (parameter.Kind is not (AbiParameterKind.Environment or AbiParameterKind.Context))
            {
                output.Write($", {parameter.Type} %{parameter.Name}");
            }
        }

        output.Write(", ptr %context)\n");
        output.Write(entry.NoReturn ? "  unreachable\n}\n" : entry.Result == "void" ? "  ret void\n}\n" : $"  ret {entry.Result} %result\n}}\n");
    }

    private static void WriteErasure(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        output.Write("  store i64 0, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", align 8\n");
        if (instruction.Aggregate!.Value.Layout.Size > 0)
        {
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", ptr ");
            WriteSlot(output, function, (int)function.GetOperands(instruction)[0].Value);
            output.Write($", i64 {instruction.Aggregate.Value.Layout.Size}, i1 false)\n");
        }

        output.Write($"  %operationsSlot{instruction.Operation} = getelementptr i8, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", i64 8\n  store ptr @");
        WriteClosureTableName(output, function, instruction.Operation);
        output.Write($", ptr %operationsSlot{instruction.Operation}, align 8\n");
    }

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
        if (instruction.Aggregate is null)
        {
            output.Write("  store i64 0, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n");
        }

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
            if (operands[i].Kind == EmissionOperandKind.SlotAddress)
            {
                output.Write($"\n  call void @llvm.memcpy.p0.p0.i64(ptr %captureAddress{id}_{i}, ptr ");
                WriteSlot(output, function, (int)operands[i].Value);
                output.Write($", i64 {field.Representation.Layout.Size}, i1 false)\n");
                continue;
            }

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

        if (instruction.Aggregate is not null)
        {
            return;
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
