// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    private static void WriteFormattingWrappers(EmissionModule module, TextWriter output)
    {
        foreach (var (wrapper, implementation) in module.FormattingWrites)
        {
            output.Write(wrapper.GetDefinition(false));
            output.Write("""
                entry:
                  %failed = getelementptr i8, ptr %self, i64 56
                  %before = load i8, ptr %failed
                  %blocked = icmp ne i8 %before, 0
                  br i1 %blocked, label %error, label %format
                format:
                  call void @
                """);
            output.Write(implementation.Name);
            output.Write("""
                (ptr %ret, ptr %value, ptr %self)
                  %tag = load i32, ptr %ret
                  %after = load i8, ptr %failed
                  %returned_error = icmp ne i32 %tag, 0
                  %nested_error = icmp ne i8 %after, 0
                  %any_error = or i1 %returned_error, %nested_error
                  br i1 %any_error, label %error, label %done
                error:
                  call void @__kimi_writer_fail(ptr %ret, ptr %self)
                  br label %done
                done:
                  ret void
                }

                """);
        }

        foreach (var (wrapper, write, fixedBuffer) in module.FormattingConversions)
        {
            output.Write(wrapper.GetDefinition(false));
            output.Write("entry:\n  %buffer = alloca [32 x i8], align 8\n  %writer = alloca [64 x i8], align 8\n  %result = alloca i32, align 4\n");
            output.Write(fixedBuffer ? "  call void @__kimi_text_fixed(ptr %buffer, ptr %destination, i64 %capacity)\n" : "  call void @__kimi_text_heap(ptr %buffer, i64 0, ptr %location, i64 %location_length)\n");
            output.Write("  call void @__kimi_text_writer(ptr %writer, ptr %buffer, i64 ");
            output.Write(fixedBuffer ? "0" : "1");
            output.Write(", ptr null)\n  call void @");
            output.Write(write.Name);
            output.Write("""
                (ptr %result, ptr %writer, ptr %value)
                  %tag = load i32, ptr %result, align 4
                  %ok = icmp eq i32 %tag, 0
                  br i1 %ok, label %success, label %failure
                success:

                """);
            output.Write(fixedBuffer ? "  store i32 0, ptr %ret, align 4\n  %payload = getelementptr i8, ptr %ret, i64 8\n  call void @__kimi_buffer_bytes(ptr %payload, ptr %buffer)\n" : "  call void @__kimi_format_take_string(ptr %ret, ptr %buffer)\n");
            output.Write("  ret void\nfailure:\n");
            if (fixedBuffer)
            {
                output.Write("  store i32 1, ptr %ret, align 4\n  ret void\n}\n");
            }
            else
            {
                output.Write("  call void @__kimi_abort(i32 ");
                WriteNumber(output, WindowsLowering.FormatReason);
                output.Write(", ptr %location, i64 %location_length, i64 -2)\n  unreachable\n}\n");
            }
        }
    }
}
