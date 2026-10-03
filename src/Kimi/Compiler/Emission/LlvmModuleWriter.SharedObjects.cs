// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
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
              %control = getelementptr i8, ptr %header, i64 8
              %old = load i64, ptr %control, align 8
              %next = sub i64 %old, 2
              store i64 %next, ptr %control, align 8
              %final = icmp eq i64 %old, 2
              br i1 %final, label %destroy, label %done
            destroy:
              call void @__kimi_drop_object(ptr %slot, ptr %location, i64 %length)
              br label %done
            done:
              ret void
            }

            """);
    }
}
