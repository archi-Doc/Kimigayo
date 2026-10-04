// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteSealedObjectDrop(TextWriter output, ObjectHandleMode mode, int payloadDrop)
    {
        output.Write("  %header = load ptr, ptr %slot, align 8\n");
        if (mode.Counting == ObjectCountingStep.NonAtomic)
        {
            WriteRcReleaseTransition(output);
        }

        if (payloadDrop != -1)
        {
            output.Write("  %payload = getelementptr i8, ptr %header, i64 16\n");
            if (payloadDrop == -2)
            {
                output.Write("  call void @__kimi_destroy_string");
            }
            else
            {
                Name(output, "  call void @__kimi_drop_aggregate", payloadDrop);
            }

            output.Write("(ptr %payload, ptr %location, i64 %length)\n");
        }

        // Every current factory uses this allocator. Destruction may Abort; free follows only its normal return.
        output.Write("  call void @__kimi_free(ptr %header, ptr %location, i64 %length)\n");
        if (mode.Counting == ObjectCountingStep.NonAtomic)
        {
            output.Write("  br label %done\ndone:\n");
        }
    }

    private static void WriteRcReleaseTransition(TextWriter output)
        => output.Write("  %control = getelementptr i8, ptr %header, i64 8\n  %old = load i64, ptr %control, align 8\n  %next = sub i64 %old, 2\n  store i64 %next, ptr %control, align 8\n  %final = icmp eq i64 %old, 2\n  br i1 %final, label %destroy, label %done\ndestroy:\n");

    // IMPL 21.2.3: rc remains inline until Weak operations become executable. Clone changes only the count;
    // final release stores zero before destruction, and never accesses the header after finalization.
    private static void WriteRcObjects(TextWriter output)
    {
        output.Write("""
            define internal void @__kimi_clone_rc(ptr %ret, ptr %value, ptr %location, i64 %length) #0 {
            entry:
              %header = load ptr, ptr %value, align 8
              %control = getelementptr i8, ptr %header, i64 8
              %old = load i64, ptr %control, align 8
              %maximum = icmp eq i64 %old, -2
              br i1 %maximum, label %overflow, label %retain
            retain:
              %next = add i64 %old, 2
              store i64 %next, ptr %control, align 8
              store ptr %header, ptr %ret, align 8
              ret void
            overflow:
              call void @__kimi_abort(i32
            """);
        output.Write(' ');
        WriteNumber(output, WindowsLowering.ReferenceCountReason);
        output.Write("""
            , ptr %location, i64 %length, i64 -2)
              unreachable
            }
            define internal void @__kimi_drop_rc(ptr %slot, ptr %location, i64 %length) #0 {
            entry:
              %header = load ptr, ptr %slot, align 8

            """);
        WriteRcReleaseTransition(output);
        output.Write("""
              call void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length)
              br label %done
            done:
              ret void
            }

            """);
    }
}
