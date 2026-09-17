// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WritePatternName(TextWriter output, EmissionFunction function, int operation)
    {
        output.Write("__kimi_pattern_");
        output.Write(function.Abi.Name);
        Name(output, "_", operation);
    }

    private static void WriteCompositePatternHelper(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        output.Write("define internal i1 @");
        WritePatternName(output, function, instruction.Operation);
        output.Write("(ptr %subject) #0 {\nentry:\n");
        var steps = instruction.Pattern!;
        output.Write(steps.Length == 0 ? "  ret i1 true\n}\n" : "  br label %test0\n");
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            Name(output, "test", i);
            output.Write(":\n");
            Name(output, "  %address", i);
            Name(output, " = getelementptr i8, ptr %subject, i64 ", step.Offset);
            output.Write('\n');
            Name(output, "  %value", i);
            output.Write(" = load ");
            output.Write(step.Representation.Layout.StorageType);
            Name(output, ", ptr %address", i);
            Name(output, ", align ", step.Representation.Layout.Alignment);
            output.Write('\n');
            Name(output, "  %equal", i);
            output.Write(" = icmp eq ");
            output.Write(step.Representation.Layout.StorageType);
            Name(output, " %value", i);
            output.Write(", ");
            WriteNumber(output, step.Expected);
            output.Write('\n');
            Name(output, "  br i1 %equal", i);
            if (i + 1 < steps.Length)
            {
                Name(output, ", label %test", i + 1);
            }
            else
            {
                output.Write(", label %accepted");
            }

            output.Write(", label %rejected\n");
        }

        if (steps.Length != 0)
        {
            output.Write("accepted:\n  ret i1 true\nrejected:\n  ret i1 false\n}\n");
        }
    }

    private static void WritePatternRead(TextWriter output, EmissionFunction function, EmissionInstruction instruction)
    {
        var id = instruction.Operation;
        var operands = function.GetOperands(instruction);
        var representation = instruction.Representation!;
        Name(output, "  %patternAddress", id);
        output.Write(" = getelementptr i8, ptr ");
        WriteStorageAddress(output, function, operands[0]);
        output.Write(", i64 ");
        WriteNumber(output, operands[1].Value);
        output.Write('\n');
        Name(output, representation.ComputationType == "i1" ? "  %patternStorage" : "  %v", id);
        output.Write(" = load ");
        output.Write(representation.Layout.StorageType);
        Name(output, ", ptr %patternAddress", id);
        Name(output, ", align ", representation.Layout.Alignment);
        output.Write('\n');
        if (representation.ComputationType == "i1")
        {
            Name(output, "  %v", id);
            Name(output, " = trunc i8 %patternStorage", id);
            output.Write(" to i1\n");
        }
    }
}
