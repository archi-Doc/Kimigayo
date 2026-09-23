// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteSequence(TextWriter output, LlvmConstantPool constants, EmissionFunction function, EmissionInstruction instruction)
    {
        var operands = function.GetOperands(instruction);
        var addressOperand = operands[0];
        var id = instruction.Operation;
        var fixedLength = (long)operands[1].Value;
        var range = instruction.Representation == WindowsLowering.Unit;
        if (instruction.ScalarOperator == "ArrayIterator")
        {
            // The consumed private handle no longer exposes capacity; that word becomes the next-element cursor.
            Name(output, "  %iterator_cursor", id);
            output.Write(" = getelementptr i8, ptr ");
            Address();
            output.Write(", i64 16\n  store i64 0, ptr ");
            Name(output, "%iterator_cursor", id);
            output.Write(", align 8\n");
            return;
        }

        if (instruction.ScalarOperator == "FromEnd")
        {
            Name(output, "  %invalid", id);
            output.Write(" = icmp slt i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", 0\n");
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
            output.Write("  store i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", ptr ");
            Address();
            Name(output, ", align 8\n  %direction", id);
            output.Write(" = getelementptr i8, ptr ");
            Address();
            output.Write(", i64 ");
            WriteNumber(output, operands[2].Value);
            Name(output, "\n  store i8 1, ptr %direction", id);
            output.Write(", align 1\n");
            return;
        }

        if (instruction.ScalarOperator is "Read" or "ArrayRead" or "ArrayStorageRead" or "SliceAddress" or "ArrayAddress")
        {
            // A fixed array's bounds are static; a Slice loads its handle's length.
            var arrayRead = instruction.ScalarOperator is "ArrayRead" or "ArrayStorageRead" or "ArrayAddress";
            if (!arrayRead)
            {
                Name(output, "  %seqbase", id);
                output.Write(" = load ptr, ptr ");
                Address();
                output.Write(", align 8\n");
                Name(output, "  %seqendptr", id);
                output.Write(" = getelementptr i8, ptr ");
                Address();
                output.Write(", i64 8\n");
                Name(output, "  %seqend", id);
                output.Write(" = load i64, ptr ");
                Name(output, "%seqendptr", id);
                output.Write(", align 8\n");
            }

            Name(output, "  %invalid", id);
            output.Write(" = icmp uge i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", ");
            if (arrayRead)
            {
                WriteNumber(output, operands[2].Value);
            }
            else
            {
                Name(output, "%seqend", id);
            }

            output.Write('\n');
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
            Name(output, operands.Length == 5 ? "  %itemoffset" : "  %offset", id);
            output.Write(" = mul i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", ");
            WriteNumber(output, operands.Length == 5 ? (long)operands[3].Value : instruction.Representation!.Layout.Stride);
            output.Write('\n');
            if (operands.Length == 5)
            {
                output.Write($"  %offset{id} = add i64 %itemoffset{id}, {(long)operands[4].Value}\n");
            }

            Name(output, "  %element", id);
            output.Write(" = getelementptr i8, ptr ");
            if (arrayRead)
            {
                Address();
            }
            else
            {
                Name(output, "%seqbase", id);
            }

            Name(output, ", i64 %offset", id);
            output.Write('\n');
            if (instruction.ScalarOperator is "SliceAddress" or "ArrayAddress")
            {
                Name(output, "  %v", id);
                Name(output, " = getelementptr i8, ptr %element", id);
                output.Write(", i64 0\n");
            }
            else if (instruction.ScalarOperator != "ArrayStorageRead")
            {
                WriteScalar(output, constants, instruction with { Opcode = EmissionOpcode.LoadElement, Place = id, ScalarType = instruction.Representation!.ComputationType }, []);
            }

            return;
        }

        if (fixedLength < 0)
        {
            Name(output, "  %seqendptr", id);
            output.Write(" = getelementptr i8, ptr ");
            Address();
            output.Write(", i64 8\n");
            Name(output, "  %seqend", id);
            output.Write(" = load i64, ptr ");
            Name(output, "%seqendptr", id);
            output.Write(", align 8\n");
            if (range)
            {
                Name(output, "  %seqstart", id);
                output.Write(" = load i64, ptr ");
                Address();
                output.Write(", align 8\n");
            }
        }

        if (instruction.ScalarOperator == "SliceRange")
        {
            Name(output, "  %slicestart", id);
            output.Write(" = or i64 0, ");
            WriteOperand(output, operands[2]);
            Name(output, "\n  %slicefinish", id);
            output.Write(" = or i64 0, ");
            if (operands[4].Value != 0)
            {
                End();
            }
            else
            {
                WriteOperand(output, operands[3]);
            }

            Name(output, "\n  %reversed", id);
            Name(output, " = icmp ugt i64 %slicestart", id);
            Name(output, ", %slicefinish", id);
            Name(output, "\n  %pastend", id);
            Name(output, " = icmp ugt i64 %slicefinish", id);
            output.Write(", ");
            End();
            Name(output, "\n  %invalid", id);
            Name(output, " = or i1 %reversed", id);
            Name(output, ", %pastend", id);
            output.Write('\n');
            WriteArithmeticFailure(output, constants, instruction with { Place = (int)operands[5].Value }, "%invalid");
            if (fixedLength < 0)
            {
                Name(output, "  %seqbase", id);
                output.Write(" = load ptr, ptr ");
                Address();
                output.Write(", align 8\n");
            }

            Name(output, "  %sliceoffset", id);
            Name(output, " = mul i64 %slicestart", id);
            output.Write(", ");
            WriteNumber(output, instruction.Representation!.Layout.Stride);
            Name(output, "\n  %slicebase", id);
            output.Write(" = getelementptr i8, ptr ");
            if (fixedLength < 0)
            {
                Name(output, "%seqbase", id);
            }
            else
            {
                Address();
            }

            Name(output, ", i64 %sliceoffset", id);
            Name(output, "\n  %slicelength", id);
            Name(output, " = sub i64 %slicefinish", id);
            Name(output, ", %slicestart", id);
            Name(output, "\n  store ptr %slicebase", id);
            output.Write(", ptr ");
            WriteSlot(output, function, instruction.Place);
            Name(output, ", align 8\n  %seqdest", id);
            output.Write(" = getelementptr i8, ptr ");
            WriteSlot(output, function, instruction.Place);
            Name(output, ", i64 8\n  store i64 %slicelength", id);
            Name(output, ", ptr %seqdest", id);
            output.Write(", align 8\n");
        }
        else if (instruction.ScalarOperator is "indices" or "Slice")
        {
            if (instruction.ScalarOperator == "Slice")
            {
                if (fixedLength < 0)
                {
                    Name(output, "  %seqbase", id);
                    output.Write(" = load ptr, ptr ");
                    Address();
                    output.Write(", align 8\n  store ptr ");
                    Name(output, "%seqbase", id);
                }
                else
                {
                    output.Write("  store ptr ");
                    Address();
                }

                output.Write(", ptr ");
            }
            else
            {
                output.Write("  store i64 0, ptr ");
            }

            WriteSlot(output, function, instruction.Place);
            output.Write(", align 8\n");
            Name(output, "  %seqdest", id);
            output.Write(" = getelementptr i8, ptr ");
            WriteSlot(output, function, instruction.Place);
            output.Write(", i64 8\n  store i64 ");
            End();
            output.Write(", ptr ");
            Name(output, "%seqdest", id);
            output.Write(", align 8\n");
        }
        else if (instruction.ScalarOperator == "capacity")
        {
            // SPEC 4.7.4: an Array handle stores its capacity after the buffer and length.
            Name(output, "  %seqcapptr", id);
            output.Write(" = getelementptr i8, ptr ");
            Address();
            output.Write(", i64 16\n");
            Name(output, "  %v", id);
            output.Write(" = load i64, ptr ");
            Name(output, "%seqcapptr", id);
            output.Write(", align 8\n");
        }
        else
        {
            Name(output, "  %v", id);
            output.Write(instruction.ScalarOperator == "isEmpty" ? " = icmp eq i64 " : " = sub i64 ");
            if (instruction.ScalarOperator == "start")
            {
                Start();
                output.Write(", 0\n");
            }
            else
            {
                End();
                output.Write(", ");
                if (instruction.ScalarOperator == "end")
                {
                    output.Write('0');
                }
                else
                {
                    Start();
                }

                output.Write('\n');
            }
        }

        void Address()
        {
            if (addressOperand.Kind == EmissionOperandKind.SlotAddress)
            {
                WriteSlot(output, function, (int)addressOperand.Value);
            }
            else
            {
                WriteOperand(output, addressOperand);
            }
        }

        void Start()
        {
            if (range)
            {
                Name(output, "%seqstart", id);
            }
            else
            {
                output.Write('0');
            }
        }

        void End()
        {
            if (fixedLength >= 0)
            {
                WriteNumber(output, fixedLength);
            }
            else
            {
                Name(output, "%seqend", id);
            }
        }
    }
}
