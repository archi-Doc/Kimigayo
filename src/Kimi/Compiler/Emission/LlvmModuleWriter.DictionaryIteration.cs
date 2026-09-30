// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteDictionaryIteration(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var operands = function.GetOperands(instruction);
        var address = operands[0];
        var id = instruction.Operation;
        if (instruction.ScalarOperator == "DictionaryLayout")
        {
            output.Write("  store ptr ");
            Address();
            output.Write(", ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n");
            for (var i = 1; i < operands.Length; i++)
            {
                output.Write("  %dlayout");
                WriteNumber(output, id);
                output.Write('_');
                WriteNumber(output, i);
                output.Write(" = getelementptr i8, ptr ");
                WriteSlot(output, function, instruction.Place);
                output.Write(", i64 ");
                WriteNumber(output, i * 8);
                output.Write("\n  store i64 ");
                WriteNumber(output, operands[i].Value);
                output.Write(", ptr %dlayout");
                WriteNumber(output, id);
                output.Write('_');
                WriteNumber(output, i);
                output.Write(", align 8\n");
            }

            return;
        }

        if (instruction.ScalarOperator == "DictionaryEntryAddress")
        {
            // A key or value inside a validated live slot (SPEC 22.1.2.5).
            output.Write("  %v");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr ");
            WriteOperand(output, address);
            output.Write(", i64 ");
            WriteNumber(output, operands[1].Value);
            output.Write("\n");
            return;
        }

        if (instruction.ScalarOperator == "FixedStorage")
        {
            // {storage, position, count} = {first element, 0, N} of the borrowed fixed array.
            output.Write("  store ptr ");
            Address();
            output.Write(", ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n  %fpos");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", i64 8\n  store i64 0, ptr %fpos");
            WriteNumber(output, id);
            output.Write(", align 8\n  %fcount");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", i64 16\n  store i64 ");
            WriteNumber(output, operands[1].Value);
            output.Write(", ptr %fcount");
            WriteNumber(output, id);
            output.Write(", align 8\n");
            return;
        }

        if (instruction.ScalarOperator is "DictionaryBorrowStorage" or "DictionaryOwnStorage")
        {
            // {storage, stride, link, count} = {buffer, slot stride, head, length} of the borrowed handle; the owning
            // remainder also keeps the tail link before its count.
            var owning = instruction.ScalarOperator == "DictionaryOwnStorage";
            output.Write("  %dbuf");
            WriteNumber(output, id);
            output.Write(" = load ptr, ptr ");
            Address();
            output.Write(", align 8\n  %dlenp");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr ");
            Address();
            output.Write(", i64 24\n  %dlen");
            WriteNumber(output, id);
            output.Write(" = load i64, ptr %dlenp");
            WriteNumber(output, id);
            output.Write(", align 8\n  %dheadp");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr ");
            Address();
            output.Write(", i64 32\n  %dhead");
            WriteNumber(output, id);
            output.Write(" = load i64, ptr %dheadp");
            WriteNumber(output, id);
            output.Write(", align 8\n  store ptr %dbuf");
            WriteNumber(output, id);
            output.Write(", ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n");
            Field(8, "i64 ", operands[1].Value);
            Field(16, "i64 %dhead", id);
            if (owning)
            {
                output.Write("  %dtailp");
                WriteNumber(output, id);
                output.Write(" = getelementptr i8, ptr ");
                Address();
                output.Write(", i64 40\n  %dtail");
                WriteNumber(output, id);
                output.Write(" = load i64, ptr %dtailp");
                WriteNumber(output, id);
                output.Write(", align 8\n");
                Field(24, "i64 %dtail", id);
            }

            Field(owning ? 32 : 24, "i64 %dlen", id);
            return;

            void Field(int offset, string prefix, Int128 number)
            {
                output.Write("  %dfield");
                WriteNumber(output, id);
                output.Write('_');
                WriteNumber(output, offset);
                output.Write(" = getelementptr i8, ptr ");
                WriteSlot(output, function, instruction.Place);
                output.Write(", i64 ");
                WriteNumber(output, offset);
                output.Write("\n  store ");
                output.Write(prefix);
                WriteNumber(output, number);
                output.Write(", ptr %dfield");
                WriteNumber(output, id);
                output.Write('_');
                WriteNumber(output, offset);
                output.Write(", align 8\n");
            }
        }

        // DictionaryLocate: the slot of a found key, or Abort for a missing key.
        output.Write("  %invalid");
        WriteNumber(output, id);
        output.Write(" = icmp eq i64 ");
        WriteOperand(output, operands[1]);
        output.Write(", 0\n");
        WriteArithmeticFailure(output, constants, instruction, "%invalid");
        output.Write("  %seqbase");
        WriteNumber(output, id);
        output.Write(" = load ptr, ptr ");
        Address();
        output.Write(", align 8\n  %slotindex");
        WriteNumber(output, id);
        output.Write(" = sub i64 ");
        WriteOperand(output, operands[1]);
        output.Write(", 1\n  %slotoffset");
        WriteNumber(output, id);
        output.Write(" = mul i64 %slotindex");
        WriteNumber(output, id);
        output.Write(", ");
        WriteNumber(output, operands[2].Value);
        output.Write("\n  %slot");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr %seqbase");
        WriteNumber(output, id);
        output.Write(", i64 %slotoffset");
        WriteNumber(output, id);
        output.Write("\n  %element");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr %slot");
        WriteNumber(output, id);
        output.Write(", i64 ");
        WriteNumber(output, operands[3].Value);
        output.Write("\n");

        void Address()
        {
            if (address.Kind == EmissionOperandKind.SlotAddress)
            {
                WriteSlot(output, function, (int)address.Value);
            }
            else
            {
                WriteOperand(output, address);
            }
        }
    }
}
