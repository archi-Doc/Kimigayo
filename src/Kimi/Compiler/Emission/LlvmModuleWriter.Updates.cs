// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteScalarSwap(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var operands = function.GetOperands(instruction);
        var layout = instruction.Representation!.Layout;
        for (var i = 0; i < 2; i++)
        {
            Name(output, i == 0 ? "  %swapFirst" : "  %swapSecond", instruction.Operation);
            output.Write(" = load ");
            output.Write(layout.StorageType);
            output.Write(", ptr ");
            WriteOperand(output, operands[i]);
            output.Write(", align ");
            WriteNumber(output, layout.Alignment);
            output.Write('\n');
        }

        for (var i = 0; i < 2; i++)
        {
            output.Write("  store ");
            output.Write(layout.StorageType);
            Name(output, i == 0 ? " %swapSecond" : " %swapFirst", instruction.Operation);
            output.Write(", ptr ");
            WriteOperand(output, operands[i]);
            output.Write(", align ");
            WriteNumber(output, layout.Alignment);
            output.Write('\n');
        }
    }
}
