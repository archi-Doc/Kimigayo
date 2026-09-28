// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteDictionaryIteration(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var operands = function.GetOperands(instruction);
        var address = operands[0];
        var id = instruction.Operation;
        if (instruction.ScalarOperator == "DictionaryEnd")
        {
            output.Write("  %v");
            WriteNumber(output, id);
            output.Write(" = or i64 0, 0\n");
            return;
        }

        if (instruction.ScalarOperator == "DictionaryStart")
        {
            output.Write("  %headptr");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr ");
            Address();
            output.Write(", i64 32\n  %v");
            WriteNumber(output, id);
            output.Write(" = load i64, ptr %headptr");
            WriteNumber(output, id);
            output.Write(", align 8\n");
            return;
        }

        if (instruction.ScalarOperator == "DictionaryLocate")
        {
            output.Write("  %invalid");
            WriteNumber(output, id);
            output.Write(" = icmp eq i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", 0\n");
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
        }

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
        output.Write("\n");
        if (instruction.ScalarOperator is "DictionaryNext" or "DictionaryTakeNext")
        {
            output.Write("  %nextptr");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr %slot");
            WriteNumber(output, id);
            output.Write(", i64 8\n  %v");
            WriteNumber(output, id);
            output.Write(" = load i64, ptr %nextptr");
            WriteNumber(output, id);
            output.Write(", align 8\n");
            if (instruction.ScalarOperator == "DictionaryTakeNext")
            {
                // Both components have been acquired. Only unyielded entries remain owned by the handle.
                output.Write("  call void @__kimi_dictionary_unlink(ptr ");
                Address();
                output.Write(", i64 ");
                WriteNumber(output, operands[2].Value);
                output.Write(", i64 ");
                WriteOperand(output, operands[1]);
                output.Write(")\n");
            }

            return;
        }

        output.Write("  %element");
        WriteNumber(output, id);
        output.Write(" = getelementptr i8, ptr %slot");
        WriteNumber(output, id);
        output.Write(", i64 ");
        WriteNumber(output, operands[3].Value);
        output.Write("\n");
        if (instruction.ScalarOperator == "DictionaryLocate")
        {
            return;
        }

        if (instruction.ScalarOperator == "DictionaryAddress")
        {
            output.Write("  %v");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr %element");
            WriteNumber(output, id);
            output.Write(", i64 0\n");
        }
        else if (instruction.ScalarOperator == "DictionaryRead")
        {
            WriteScalar(output, constants, instruction with { Opcode = EmissionOpcode.LoadElement, Place = id, ScalarType = instruction.Representation!.ComputationType }, []);
        }
        else if (instruction.ScalarOperator == "DictionaryPair")
        {
            output.Write("  store ptr %element");
            WriteNumber(output, id);
            output.Write(", ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n  %pairvalue");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr %slot");
            WriteNumber(output, id);
            output.Write(", i64 ");
            WriteNumber(output, operands[4].Value);
            output.Write("\n  %pairdest");
            WriteNumber(output, id);
            output.Write(" = getelementptr i8, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", i64 8\n  store ptr %pairvalue");
            WriteNumber(output, id);
            output.Write(", ptr %pairdest");
            WriteNumber(output, id);
            output.Write(", align 8\n");
        }
        else if (instruction.Representation!.Layout.Size > 0)
        {
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", ptr %element");
            WriteNumber(output, id);
            output.Write(", i64 ");
            WriteNumber(output, instruction.Representation.Layout.Size);
            output.Write(", i1 false)\n");
        }

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
