// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private const string StringComparisons = """
        declare i32 @memcmp(ptr, ptr, i64)
        define internal i32 @__kimi_string_equal(ptr %a, i64 %an, ptr %b, i64 %bn) #0 {
        entry:
          %sameLength = icmp eq i64 %an, %bn
          br i1 %sameLength, label %same, label %different
        same:
          %empty = icmp eq i64 %an, 0
          br i1 %empty, label %equal, label %bytes
        bytes:
          %comparison = call i32 @memcmp(ptr %a, ptr %b, i64 %an)
          ret i32 %comparison
        equal:
          ret i32 0
        different:
          ret i32 1
        }
        define internal i32 @__kimi_string_compare(ptr %a, i64 %an, ptr %b, i64 %bn) #0 {
        entry:
          %shorter = icmp ult i64 %an, %bn
          %count = select i1 %shorter, i64 %an, i64 %bn
          %empty = icmp eq i64 %count, 0
          br i1 %empty, label %lengths, label %bytes
        bytes:
          %comparison = call i32 @memcmp(ptr %a, ptr %b, i64 %count)
          %equal = icmp eq i32 %comparison, 0
          br i1 %equal, label %lengths, label %different
        different:
          ret i32 %comparison
        lengths:
          %sameLength = icmp eq i64 %an, %bn
          %direction = select i1 %shorter, i32 -1, i32 1
          %result = select i1 %sameLength, i32 0, i32 %direction
          ret i32 %result
        }
        """ + "\n";

    private static void WriteStringAddress(TextWriter output, EmissionFunction function, EmissionOperand operand)
    {
        if (operand.Kind == EmissionOperandKind.SlotAddress)
        {
            WriteSlot(output, function, (int)operand.Value);
        }
        else
        {
            WriteOperand(output, operand);
        }
    }

    private static void WriteStringComparison(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        var operands = function.GetOperands(instruction);
        // Inspection needs only data and byte length, never releaseKind or aggregate padding.
        for (var side = 0; side < 2; side++)
        {
            StringFieldName(output, "  %compareLengthPtr", id, side);
            output.Write(" = getelementptr %kimi.string, ptr ");
            WriteStringAddress(output, function, operands[side]);
            output.Write(", i32 0, i32 1\n");
            StringFieldName(output, "  %compareData", id, side);
            output.Write(" = load ptr, ptr ");
            WriteStringAddress(output, function, operands[side]);
            output.Write(", align 8\n");
            StringFieldName(output, "  %compareLength", id, side);
            output.Write(" = load i64, ptr ");
            StringFieldName(output, "%compareLengthPtr", id, side);
            output.Write(", align 8\n");
        }

        Name(output, "  %stringOrder", id);
        output.Write(instruction.Opcode == EmissionOpcode.StringEquals ? " = call i32 @__kimi_string_equal(" : " = call i32 @__kimi_string_compare(");
        for (var side = 0; side < 2; side++)
        {
            output.Write(side == 0 ? "ptr " : ", ptr ");
            StringFieldName(output, "%compareData", id, side);
            output.Write(", i64 ");
            StringFieldName(output, "%compareLength", id, side);
        }

        output.Write(")\n");
        Name(output, "  %v", id);
        output.Write(" = icmp ");
        output.Write(instruction.ScalarOperator);
        Name(output, " i32 %stringOrder", id);
        output.Write(", 0\n");
    }

    private static void WriteStringPattern(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        Name(output, "  %patternLengthPtr", id);
        output.Write(" = getelementptr %kimi.string, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", i32 0, i32 1\n");
        Name(output, "  %patternLength", id);
        Name(output, " = load i64, ptr %patternLengthPtr", id);
        output.Write(", align 8\n");
        if (instruction.Constant < 0)
        {
            Name(output, "  %v", id);
            Name(output, " = icmp eq i64 %patternLength", id);
            output.Write(", 0\n");
            return;
        }

        Name(output, "  %patternData", id);
        output.Write(" = load ptr, ptr ");
        WriteSlot(output, function, instruction.Place);
        output.Write(", align 8\n");
        Name(output, "  %patternOrder", id);
        Name(output, " = call i32 @__kimi_string_equal(ptr %patternData", id);
        Name(output, ", i64 %patternLength", id);
        output.Write(", ptr @");
        output.Write(constants[instruction.Constant].Name);
        output.Write(", i64 ");
        WriteNumber(output, constants[instruction.Constant].ByteLength);
        output.Write(")\n");
        Name(output, "  %v", id);
        Name(output, " = icmp eq i32 %patternOrder", id);
        output.Write(", 0\n");
    }
}
