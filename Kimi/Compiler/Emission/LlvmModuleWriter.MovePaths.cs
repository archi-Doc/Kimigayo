// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WritePartDestruction(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        if (instruction.Opcode == EmissionOpcode.StorePathFlag)
        {
            output.Write("  store i8 ");
            WriteNumber(output, instruction.Constant);
            Name(output, ", ptr %pathSlot", instruction.Place);
            output.Write(", align 1\n");
            return;
        }

        if (instruction.Opcode == EmissionOpcode.EndPartDestruction)
        {
            Name(output, "  br label %b", instruction.Constant);
            output.Write('\n');
            Name(output, "b", instruction.Constant);
            output.Write(":\n");
            return;
        }

        var id = instruction.Continuation;
        var operands = function.GetOperands(instruction);
        // Explicit entry also gives the loop phi a stable predecessor when several
        // remaining ranges are destroyed at the same ownership operation.
        Name(output, "  br label %partEntry", id);
        output.Write('\n');
        Name(output, "partEntry", id);
        output.Write(":\n");
        if (instruction.Place >= 0)
        {
            Name(output, "  %partLive", id);
            Name(output, " = load i8, ptr %pathSlot", instruction.Place);
            output.Write(", align 1\n");
            Name(output, "  %partHasLive", id);
            Name(output, " = icmp ne i8 %partLive", id);
            output.Write(", 0\n");
            Name(output, "  br i1 %partHasLive", id);
            Name(output, ", label %partStart", id);
            Name(output, ", label %partEnd", id);
            output.Write('\n');
        }
        else
        {
            Name(output, "  br label %partStart", id);
            output.Write('\n');
        }

        Name(output, "partStart", id);
        output.Write(":\n");
        Name(output, "  %partBase", id);
        output.Write(" = getelementptr i8, ptr ");
        WriteStorageAddress(output, function, operands[0]);
        output.Write(", i64 ");
        WriteNumber(output, operands[1].Value);
        output.Write('\n');
        if (operands[2].Value == 1)
        {
            WritePartCall(output, constants, instruction, "(ptr %partBase");
            Name(output, "  br label %partEnd", id);
            output.Write('\n');
            Name(output, "partEnd", id);
            output.Write(":\n");
            return;
        }

        Name(output, "  br label %partLoop", id);
        output.Write('\n');
        Name(output, "partLoop", id);
        output.Write(":\n");
        Name(output, "  %partRemaining", id);
        output.Write(" = phi i64 [ ");
        WriteNumber(output, operands[2].Value);
        Name(output, ", %partStart", id);
        Name(output, " ], [ %partIndex", id);
        Name(output, ", %partLoop", id);
        output.Write(" ]\n");
        Name(output, "  %partIndex", id);
        Name(output, " = sub i64 %partRemaining", id);
        output.Write(", 1\n");
        Name(output, "  %partOffset", id);
        Name(output, " = mul i64 %partIndex", id);
        output.Write(", ");
        WriteNumber(output, operands[3].Value);
        output.Write('\n');
        Name(output, "  %partAddress", id);
        Name(output, " = getelementptr i8, ptr %partBase", id);
        Name(output, ", i64 %partOffset", id);
        output.Write('\n');
        WritePartCall(output, constants, instruction, "(ptr %partAddress");
        Name(output, "  %partDone", id);
        Name(output, " = icmp eq i64 %partIndex", id);
        output.Write(", 0\n");
        Name(output, "  br i1 %partDone", id);
        Name(output, ", label %partEnd", id);
        Name(output, ", label %partLoop", id);
        output.Write('\n');
        Name(output, "partEnd", id);
        output.Write(":\n");
    }

    private static void WritePartCall(TextWriter output, LlvmConstantPool constants, EmissionInstruction instruction, string address)
    {
        if (instruction.Aggregate is { } aggregate)
        {
            Name(output, "  call void @__kimi_drop_aggregate", aggregate.Id);
        }
        else
        {
            output.Write("  call void @__kimi_destroy_string");
        }

        Name(output, address, instruction.Continuation);
        output.Write(", ptr @");
        output.Write(constants[instruction.Constant].Name);
        output.Write(", i64 ");
        WriteNumber(output, constants[instruction.Constant].ByteLength);
        output.Write(")\n");
    }
}
