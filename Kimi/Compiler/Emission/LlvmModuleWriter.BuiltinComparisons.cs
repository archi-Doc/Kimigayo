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

        if (instruction.ScalarOperator == "equals")
        {
            if (type is "float" or "double")
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
                Compare("v", "icmp eq");
            }

            return;
        }

        Compare("contractLess", instruction.ScalarOperator == "s" ? "icmp slt" : "icmp ult");
        Compare("contractGreater", instruction.ScalarOperator == "s" ? "icmp sgt" : "icmp ugt");
        Name(output, "  %contractPositive", id);
        Name(output, " = zext i1 %contractGreater", id);
        output.Write(" to i32\n");
        Name(output, "  %contractNegative", id);
        Name(output, " = zext i1 %contractLess", id);
        output.Write(" to i32\n");
        Name(output, "  %v", id);
        Name(output, " = sub i32 %contractPositive", id);
        Name(output, ", %contractNegative", id);
        output.Write('\n');

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
}
