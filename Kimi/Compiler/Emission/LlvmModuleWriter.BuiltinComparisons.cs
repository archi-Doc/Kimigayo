// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteBuiltinComparison(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        var type = instruction.ScalarType;
        if (type == "void")
        {
            Name(output, "  %v", id);
            output.Write(" = icmp eq i1 0, 0\n");
            return;
        }

        var operands = function.GetOperands(instruction);
        for (var side = 0; side < 2; side++)
        {
            StringFieldName(output, "  %contractValue", id, side);
            output.Write(" = load ");
            output.Write(type);
            output.Write(", ptr ");
            WriteStorageAddress(output, function, operands[side]);
            output.Write(", align ");
            WriteNumber(output, instruction.Representation!.Layout.Alignment);
            output.Write('\n');
        }

        if (instruction.ScalarOperator is "equals" or "ieee")
        {
            if (type is "float" or "double" && instruction.ScalarOperator == "equals")
            {
                Compare("contractEqual", "fcmp oeq");
                for (var side = 0; side < 2; side++)
                {
                    StringFieldName(output, "  %contractNaN", id, side);
                    output.Write(" = fcmp uno ");
                    output.Write(type);
                    StringFieldName(output, " %contractValue", id, side);
                    StringFieldName(output, ", %contractValue", id, side);
                    output.Write('\n');
                }

                Name(output, "  %contractBothNaN", id);
                StringFieldName(output, " = and i1 %contractNaN", id, 0);
                StringFieldName(output, ", %contractNaN", id, 1);
                output.Write('\n');
                Name(output, "  %v", id);
                Name(output, " = or i1 %contractEqual", id);
                Name(output, ", %contractBothNaN", id);
                output.Write('\n');
            }
            else
            {
                Compare("v", type is "float" or "double" ? "fcmp oeq" : "icmp eq");
            }

            return;
        }

        var floating = instruction.ScalarOperator == "float";
        Compare("contractLess", floating ? "fcmp olt" : instruction.ScalarOperator == "s" ? "icmp slt" : "icmp ult");
        Compare("contractGreater", floating ? "fcmp ogt" : instruction.ScalarOperator == "s" ? "icmp sgt" : "icmp ugt");
        Name(output, "  %contractPositive", id);
        Name(output, " = zext i1 %contractGreater", id);
        output.Write(" to i32\n");
        Name(output, "  %contractNegative", id);
        Name(output, " = zext i1 %contractLess", id);
        output.Write(" to i32\n");
        Name(output, floating ? "  %contractOrder" : "  %v", id);
        Name(output, " = sub i32 %contractPositive", id);
        Name(output, ", %contractNegative", id);
        output.Write('\n');

        if (floating)
        {
            Compare("contractUnordered", "fcmp uno");
            Name(output, "  %contractUnorderedBit", id);
            Name(output, " = zext i1 %contractUnordered", id);
            output.Write(" to i32\n");
            Name(output, "  %contractUnorderedCode", id);
            Name(output, " = shl i32 %contractUnorderedBit", id);
            output.Write(", 1\n");
            Name(output, "  %v", id);
            Name(output, " = or i32 %contractUnorderedCode", id);
            Name(output, ", %contractOrder", id);
            output.Write('\n');
        }

        void Compare(string name, string operation)
        {
            output.Write("  %");
            Name(output, name, id);
            output.Write(" = ");
            output.Write(operation);
            output.Write(' ');
            output.Write(type);
            StringFieldName(output, " %contractValue", id, 0);
            StringFieldName(output, ", %contractValue", id, 1);
            output.Write('\n');
        }
    }

    private static void WriteTupleRelation(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        var input = function.GetOperands(instruction)[0];
        WriteEquality(output, "%tupleUnordered", id, "i32", input, 2);
        Name(output, "  %tupleOrdered", id);
        Name(output, " = xor i1 %tupleUnordered", id);
        output.Write(", true\n");
        Name(output, "  %tupleRelation", id);
        output.Write(" = icmp ");
        output.Write(instruction.ScalarOperator);
        output.Write(" i32 ");
        WriteOperand(output, input);
        output.Write(", 0\n");
        Name(output, "  %v", id);
        Name(output, " = and i1 %tupleOrdered", id);
        Name(output, ", %tupleRelation", id);
        output.Write('\n');
    }
}
