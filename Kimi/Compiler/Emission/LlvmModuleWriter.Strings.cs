// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static readonly string[] StringFieldTypes = ["ptr", "i64", "i8"];

    private static void StringFieldName(TextWriter output, string prefix, int operation, int field)
    {
        Name(output, prefix, operation);
        output.Write('_');
        WriteNumber(output, field);
    }

    private static void WriteString(TextWriter output, LlvmConstantPool constants, EmissionInstruction instruction, ReadOnlySpan<EmissionOperand> operands)
    {
        var id = instruction.Operation;
        if (instruction.Opcode == EmissionOpcode.StoreLiveFlag)
        {
            output.Write("  store i8 ");
            WriteNumber(output, instruction.Constant);
            Name(output, ", ptr %liveSlot", instruction.Place);
            output.Write(", align 1\n");
            return;
        }

        if (instruction.Opcode == EmissionOpcode.DestroyStringIfLive)
        {
            Name(output, "  %live", id);
            Name(output, " = load i8, ptr %liveSlot", instruction.Place);
            output.Write(", align 1\n");
            Name(output, "  %hasLive", id);
            Name(output, " = icmp ne i8 %live", id);
            output.Write(", 0\n");
            Name(output, "  br i1 %hasLive", id);
            Name(output, ", label %destroy", id);
            output.Write(", label ");
            WriteOperand(output, operands[0]);
            output.Write('\n');
            Name(output, "destroy", id);
            output.Write(":\n");
            WriteCall(output, constants, WindowsLowering.DestroyString, [new(EmissionOperandKind.SlotAddress, instruction.Place), new(EmissionOperandKind.ConstantAddress, instruction.Constant), new(EmissionOperandKind.ConstantLength, instruction.Constant)]);
            output.Write("  br label ");
            WriteOperand(output, operands[0]);
            output.Write('\n');
            Name(output, "b", (int)operands[0].Value);
            output.Write(":\n");
            return;
        }

        if (instruction.Opcode != EmissionOpcode.MoveString || operands.Length != 1 || operands[0].Kind != EmissionOperandKind.SlotAddress)
        {
            throw new InvalidOperationException("Unknown string transfer plan.");
        }

        // Read only the three valid fields, never aggregate padding. Secure all
        // fields before storing; Move transfers responsibility without clearing bits.
        for (var field = 0; field < StringFieldTypes.Length; field++)
        {
            StringFieldName(output, "  %strSource", id, field);
            Name(output, " = getelementptr %kimi.string, ptr %p", (int)operands[0].Value);
            output.Write(", i32 0, i32 ");
            WriteNumber(output, field);
            output.Write('\n');
            StringFieldName(output, "  %strValue", id, field);
            output.Write(" = load ");
            output.Write(StringFieldTypes[field]);
            StringFieldName(output, ", ptr %strSource", id, field);
            WriteAlignment(output, field == 2 ? 1 : WindowsLowering.String.Layout.Alignment);
        }

        for (var field = 0; field < StringFieldTypes.Length; field++)
        {
            StringFieldName(output, "  %strDestination", id, field);
            Name(output, " = getelementptr %kimi.string, ptr %p", instruction.Place);
            output.Write(", i32 0, i32 ");
            WriteNumber(output, field);
            output.Write("\n  store ");
            output.Write(StringFieldTypes[field]);
            StringFieldName(output, " %strValue", id, field);
            StringFieldName(output, ", ptr %strDestination", id, field);
            WriteAlignment(output, field == 2 ? 1 : WindowsLowering.String.Layout.Alignment);
        }
    }
}
