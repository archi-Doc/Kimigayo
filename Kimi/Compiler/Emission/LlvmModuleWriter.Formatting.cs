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
    }
}
