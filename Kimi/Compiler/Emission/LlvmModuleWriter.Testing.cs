// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteTestSnapshot(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var operand = function.GetOperands(instruction)[0];
        var width = instruction.ScalarType == "i1" ? 1 : instruction.Representation!.Layout.Size * 8;
        var floating = instruction.ScalarType is "float" or "double";
        if (floating)
        {
            Name("  ", ".bits");
            output.Write(" = bitcast ");
            output.Write(instruction.ScalarType);
            output.Write(' ');
            WriteOperand(output, operand);
            output.Write(" to i");
            WriteNumber(output, width);
            output.Write('\n');
        }

        if (width != 128)
        {
            Name("  ", ".wide");
            output.Write(" = zext i");
            WriteNumber(output, width);
            output.Write(' ');
            if (floating)
            {
                Name(string.Empty, ".bits");
            }
            else
            {
                WriteOperand(output, operand);
            }

            output.Write(" to i128\n");
        }

        output.Write("  call void @__kimi_test_scalar(i64 %v");
        WriteNumber(output, instruction.Operation);
        output.Write(", i32 ");
        WriteNumber(output, instruction.Place);
        output.Write(", i16 ");
        WriteNumber(output, instruction.Constant);
        output.Write(", i16 ");
        WriteNumber(output, width);
        output.Write(", i128 ");
        if (width == 128)
        {
            WriteOperand(output, operand);
        }
        else
        {
            Name(string.Empty, ".wide");
        }

        output.Write(")\n");

        void Name(string prefix, string suffix)
        {
            output.Write(prefix);
            output.Write("%test");
            WriteNumber(output, instruction.Operation);
            output.Write('_');
            WriteNumber(output, instruction.Place);
            output.Write(suffix);
        }
    }
}
