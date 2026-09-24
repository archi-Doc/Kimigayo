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
            output.Write($"  %v{id} = or i64 0, 0\n");
            return;
        }

        if (instruction.ScalarOperator == "DictionaryStart")
        {
            output.Write($"  %headptr{id} = getelementptr i8, ptr ");
            Address();
            output.Write($", i64 32\n  %v{id} = load i64, ptr %headptr{id}, align 8\n");
            return;
        }

        if (instruction.ScalarOperator == "DictionaryLocate")
        {
            output.Write($"  %invalid{id} = icmp eq i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", 0\n");
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
        }

        output.Write($"  %seqbase{id} = load ptr, ptr ");
        Address();
        output.Write($", align 8\n  %slotindex{id} = sub i64 ");
        WriteOperand(output, operands[1]);
        output.Write($", 1\n  %slotoffset{id} = mul i64 %slotindex{id}, {operands[2].Value}\n  %slot{id} = getelementptr i8, ptr %seqbase{id}, i64 %slotoffset{id}\n");
        if (instruction.ScalarOperator is "DictionaryNext" or "DictionaryTakeNext")
        {
            output.Write($"  %nextptr{id} = getelementptr i8, ptr %slot{id}, i64 8\n  %v{id} = load i64, ptr %nextptr{id}, align 8\n");
            if (instruction.ScalarOperator == "DictionaryTakeNext")
            {
                // Both components have been acquired. Only unyielded entries remain owned by the handle.
                output.Write("  call void @__kimi_dictionary_unlink(ptr ");
                Address();
                output.Write($", i64 {operands[2].Value}, i64 ");
                WriteOperand(output, operands[1]);
                output.Write(")\n");
            }

            return;
        }

        output.Write($"  %element{id} = getelementptr i8, ptr %slot{id}, i64 {operands[3].Value}\n");
        if (instruction.ScalarOperator == "DictionaryLocate")
        {
            return;
        }

        if (instruction.ScalarOperator == "DictionaryAddress")
        {
            output.Write($"  %v{id} = getelementptr i8, ptr %element{id}, i64 0\n");
        }
        else if (instruction.ScalarOperator == "DictionaryRead")
        {
            WriteScalar(output, constants, instruction with { Opcode = EmissionOpcode.LoadElement, Place = id, ScalarType = instruction.Representation!.ComputationType }, []);
        }
        else if (instruction.ScalarOperator == "DictionaryPair")
        {
            output.Write($"  store ptr %element{id}, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write($", align 8\n  %pairvalue{id} = getelementptr i8, ptr %slot{id}, i64 {operands[4].Value}\n  %pairdest{id} = getelementptr i8, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write($", i64 8\n  store ptr %pairvalue{id}, ptr %pairdest{id}, align 8\n");
        }
        else if (instruction.Representation!.Layout.Size > 0)
        {
            output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write($", ptr %element{id}, i64 {instruction.Representation.Layout.Size}, i1 false)\n");
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
