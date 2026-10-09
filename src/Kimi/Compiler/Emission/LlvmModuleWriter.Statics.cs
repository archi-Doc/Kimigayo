// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteStatics(EmissionModule module, TextWriter output)
    {
        foreach (var slot in module.Statics)
        {
            var type = slot.Initializer.Result;
            var storage = type == "i1" ? "i8" : type;
            output.Write('@');
            output.Write(slot.StateName);
            output.Write(" = internal global i8 0\n@");
            output.Write(slot.ValueName);
            output.Write(" = internal global ");
            output.Write(storage);
            output.Write(" zeroinitializer\n");
            output.Write(slot.Getter.GetDefinition(false));
            output.Write("entry:\n  %state = load i8, ptr @");
            output.Write(slot.StateName);
            output.Write("\n  switch i8 %state, label %cycle [i8 0, label %initialize i8 2, label %ready]\ninitialize:\n  store i8 1, ptr @");
            output.Write(slot.StateName);
            output.Write("\n  %initial = call ");
            output.Write(type);
            output.Write(" @");
            output.Write(slot.Initializer.Name);
            output.Write("()\n");
            if (type == "i1")
            {
                output.Write("  %initial.byte = zext i1 %initial to i8\n");
            }

            output.Write("  store ");
            output.Write(storage);
            output.Write(type == "i1" ? " %initial.byte, ptr @" : " %initial, ptr @");
            output.Write(slot.ValueName);
            output.Write("\n  store i8 2, ptr @");
            output.Write(slot.StateName);
            output.Write("\n  br label %ready\nready:\n  %stored = load ");
            output.Write(storage);
            output.Write(", ptr @");
            output.Write(slot.ValueName);
            if (type == "i1")
            {
                output.Write("\n  %value = trunc i8 %stored to i1");
            }

            output.Write("\n  ret ");
            output.Write(type);
            output.Write(type == "i1" ? " %value" : " %stored");
            output.Write("\ncycle:\n  call void @__kimi_abort(i32 ");
            WriteNumber(output, WindowsLowering.StaticCycleReason);
            output.Write(", ptr %location, i64 %location_length, i64 -2)\n  unreachable\n}\n");
        }
    }
}
