// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteErasureAdapter(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var entry = instruction.Callee!;
        var heap = instruction.Aggregate is { } environment && (environment.NeedsDestruction || environment.Value.Layout.Size > 8);
        output.Write('@');
        WriteClosureTableName(output, function, instruction.Operation);
        output.Write(" = private constant { ptr, ptr, ptr } { ptr @");
        WriteErasureAdapterName(output, function, instruction.Operation);
        output.Write(", ptr null, ptr ");
        if (heap)
        {
            output.Write('@');
            WriteErasureAdapterName(output, function, instruction.Operation);
            output.Write("_drop");
        }
        else
        {
            output.Write("null");
        }

        output.Write(" }, align 8\ndefine internal ");
        output.Write(entry.Result);
        output.Write(" @");
        WriteErasureAdapterName(output, function, instruction.Operation);
        output.Write("(i64 %environment");
        foreach (var parameter in entry.Parameters)
        {
            if (parameter.Kind is not (AbiParameterKind.Environment or AbiParameterKind.Context or AbiParameterKind.Location or AbiParameterKind.LocationLength))
            {
                output.Write(", ");
                output.Write(parameter.Type);
                output.Write(" %");
                output.Write(parameter.Name);
            }
        }

        output.Write(", ptr %location, i64 %location_length, ptr %context) #0 {\nentry:\n");
        output.Write(heap ? "  %storage = inttoptr i64 %environment to ptr\n" : "  %storage = alloca i64, align 8\n  store i64 %environment, ptr %storage, align 8\n");
        output.Write(entry.Result == "void" ? "  call " : "  %result = call ");
        output.Write(entry.Result);
        output.Write(" @");
        output.Write(entry.Name);
        output.Write('(');
        for (var i = 0; i < entry.Parameters.Length; i++)
        {
            if (i != 0)
            {
                output.Write(", ");
            }

            var parameter = entry.Parameters[i];
            output.Write(parameter.Type);
            output.Write(" %");
            output.Write(parameter.Kind == AbiParameterKind.Environment ? "storage" : parameter.Name);
        }

        output.Write(")\n");
        if (entry.NoReturn)
        {
            output.Write("  unreachable\n}\n");
        }
        else
        {
            output.Write("  ret ");
            output.Write(entry.Result);
            output.Write(entry.Result == "void" ? "\n}\n" : " %result\n}\n");
        }

        if (heap)
        {
            output.Write("define internal void @");
            WriteErasureAdapterName(output, function, instruction.Operation);
            output.Write("_drop(i64 %environment, ptr %context, ptr %location, i64 %length) #0 {\nentry:\n  %storage = inttoptr i64 %environment to ptr\n");
            if (instruction.Aggregate!.NeedsDestruction)
            {
                Name(output, "  call void @__kimi_drop_aggregate", instruction.Aggregate.Id);
                output.Write("(ptr %storage, ptr %location, i64 %length)\n");
            }

            output.Write("  call void @__kimi_free(ptr %storage, ptr %location, i64 %length)\n  ret void\n}\n");
        }
    }

    private static void WriteErasureAdapterName(TextWriter output, EmissionFunction function, int operation)
    {
        output.Write("__kimi_erase_");
        output.Write(function.Abi.Name);
        Name(output, "_", operation);
    }

    private static void WriteErasure(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var heap = instruction.Aggregate is { } layout && (layout.NeedsDestruction || layout.Value.Layout.Size > 8);
        if (heap)
        {
            Name(output, "  %heapEnvironment", instruction.Operation);
            output.Write(" = call ptr @__kimi_alloc(i64 ");
            WriteNumber(output, instruction.Aggregate!.Value.Layout.Size);
            output.Write(", ptr @");
            output.Write(constants[instruction.Constant].Name);
            output.Write(", i64 ");
            WriteNumber(output, constants[instruction.Constant].ByteLength);
            output.Write(")\n  store ptr ");
            Name(output, "%heapEnvironment", instruction.Operation);
            output.Write(", ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n");
        }
        else
        {
            output.Write("  store i64 0, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n");
        }

        if (instruction.Aggregate is { } environment && environment.Value.Layout.Size > 0)
        {
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
            if (heap)
            {
                Name(output, "%heapEnvironment", instruction.Operation);
            }
            else
            {
                WriteSlot(output, function, instruction.Place);
            }

            output.Write(", ptr ");
            WriteSlot(output, function, (int)function.GetOperands(instruction)[0].Value);
            output.Write(", i64 ");
            WriteNumber(output, instruction.Aggregate.Value.Layout.Size);
            output.Write(", i1 false)\n");
        }

        Name(output, "  %operationsSlot", instruction.Operation);
        output.Write(" = getelementptr i8, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", i64 8\n  store ptr @");
        WriteClosureTableName(output, function, instruction.Operation);
        Name(output, ", ptr %operationsSlot", instruction.Operation);
        output.Write(", align 8\n");
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
            var field = function.PatternSteps[instruction.PatternStart + i];
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
                Name(output, "\n  call void @llvm.memcpy.p0.p0.i64(ptr %captureAddress", id);
                Name(output, "_", i);
                output.Write(", ptr ");
                WriteSlot(output, function, (int)operands[i].Value);
                output.Write(", i64 ");
                WriteNumber(output, field.Representation.Layout.Size);
                output.Write(", i1 false)\n");
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

    // The value is in Place's slot, or at the address of a leading operand beyond the ABI parameters when a reference holds it.
    private static void WriteValueCall(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        var abi = instruction.Callee!;
        var operands = function.GetOperands(instruction);
        var first = operands.Length - abi.Parameters.Length;
        Name(output, "  %environment", id);
        output.Write(" = load i64, ptr ");
        WriteValueAddress(output, function, instruction, operands, first);
        output.Write(", align 8\n");
        Name(output, "  %operationsSlot", id);
        output.Write(" = getelementptr i8, ptr ");
        WriteValueAddress(output, function, instruction, operands, first);
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
        for (var i = first; i < operands.Length; i++)
        {
            output.Write(", ");
            output.Write(abi.Parameters[i - first].Type);
            output.Write(' ');
            if (operands[i].Kind == EmissionOperandKind.ConstantAddress)
            {
                output.Write('@');
                output.Write(constants[(int)operands[i].Value].Name);
            }
            else if (operands[i].Kind == EmissionOperandKind.ConstantLength)
            {
                WriteNumber(output, constants[(int)operands[i].Value].ByteLength);
            }
            else
            {
                WriteStorageAddress(output, function, operands[i]);
            }
        }

        Name(output, ", ptr %context", id);
        output.Write(")\n");
    }

    private static void WriteValueAddress(TextWriter output, EmissionFunction function, EmissionInstruction instruction, ReadOnlySpan<EmissionOperand> operands, int first)
    {
        if (first > 0)
        {
            WriteOperand(output, operands[0]);
        }
        else
        {
            WriteSlot(output, function, instruction.Place);
        }
    }
}
