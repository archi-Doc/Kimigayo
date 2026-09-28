// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Shared byte transfer, scalar representation and destruction for stored collection values.
internal static partial class LlvmModuleWriter
{
    private static void WriteStoredCopy(TextWriter output, string destination, string source, long size)
    {
        if (size == 0)
        {
            return;
        }

        output.Write("  call void @llvm.memcpy.p0.p0.i64(ptr ");
        output.Write(destination);
        output.Write(", ptr ");
        output.Write(source);
        output.Write(", i64 ");
        WriteNumber(output, size);
        output.Write(", i1 false)\n");
    }

    private static void WriteStoredArgument(TextWriter output, ValueLowering value, bool scalar, string source, string destination, string widened)
    {
        if (!scalar)
        {
            WriteStoredCopy(output, destination, source, value.Layout.Size);
            return;
        }

        if (value.ComputationType != value.Layout.StorageType)
        {
            output.Write("  ");
            output.Write(widened);
            output.Write(" = zext ");
            output.Write(value.ComputationType);
            output.Write(' ');
            output.Write(source);
            output.Write(" to ");
            output.Write(value.Layout.StorageType);
            output.Write('\n');
            source = widened;
        }

        output.Write("  store ");
        output.Write(value.Layout.StorageType);
        output.Write(' ');
        output.Write(source);
        output.Write(", ptr ");
        output.Write(destination);
        output.Write(", align ");
        WriteNumber(output, value.Layout.Alignment);
        output.Write('\n');
    }

    private static void WriteStoredDestruction(TextWriter output, AggregateLayout? layout, bool text, string address)
    {
        if (!text && layout?.NeedsDestruction != true)
        {
            return;
        }

        if (text)
        {
            output.Write("  call void @__kimi_destroy_string");
        }
        else
        {
            Name(output, "  call void @__kimi_drop_aggregate", layout!.Id);
        }

        output.Write("(ptr ");
        output.Write(address);
        output.Write(", ptr %location, i64 %location_length)\n");
    }
}
