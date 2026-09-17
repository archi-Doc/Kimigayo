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
        if (instruction.ScalarOperator is "Read" or "ArrayRead" or "ArrayStorageRead")
        {
            var arrayRead = instruction.ScalarOperator != "Read";
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
                output.Write((long)operands[2].Value);
            }
            else
            {
                Name(output, "%seqend", id);
            }

            output.Write('\n');
            WriteArithmeticFailure(output, constants, instruction, "%invalid");
            Name(output, "  %offset", id);
            output.Write(" = mul i64 ");
            WriteOperand(output, operands[1]);
            output.Write(", ");
            output.Write(instruction.Representation!.Layout.Stride);
            output.Write('\n');
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
            if (instruction.ScalarOperator != "ArrayStorageRead")
            {
                WriteScalar(output, constants, instruction with { Opcode = EmissionOpcode.LoadElement, Place = id, ScalarType = instruction.Representation.ComputationType }, []);
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

        if (instruction.ScalarOperator is "indices" or "Slice")
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
                output.Write(fixedLength);
            }
            else
            {
                Name(output, "%seqend", id);
            }
        }
    }
}
