// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

internal static class NativeAllocationAudit
{
    internal static void WriteFixture(string name, string source, int allocations, int frees, long bytes, string stdout = "")
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.True(c.Emission.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        var ir = writer.ToString()
            .Replace("call ptr @HeapAlloc(", "call ptr @audit_alloc(", StringComparison.Ordinal)
            .Replace("call i32 @HeapFree(", "call i32 @audit_free(", StringComparison.Ordinal)
            .Replace("call void @__kimi_exit(", "call void @__kimi_audit_exit(", StringComparison.Ordinal);
        // Keep the real startup and cleanup path, including explicitly declared main.
        // Audit at process exit instead of assuming an implicit entry-body symbol.
        var start = $$"""
            define internal void @__kimi_audit_exit(i32 %exit) noreturn #0 {
            entry:
              %alloc = load i64, ptr @audit_allocations
              %free = load i64, ptr @audit_frees
              %size = load i64, ptr @audit_bytes
              %a = icmp eq i64 %alloc, {{allocations}}
              %b = icmp eq i64 %free, {{frees}}
              %c = icmp eq i64 %size, {{bytes}}
              %ab = and i1 %a, %b
              %ok = and i1 %ab, %c
              br i1 %ok, label %passed, label %failed
            passed:
              call void @__kimi_exit(i32 %exit)
              unreachable
            failed:
              call void @__kimi_exit(i32 93)
              unreachable
            }
            """;
        ir += "\n" + start + "\n";
        ir += """

            @audit_allocations = private global i64 0
            @audit_frees = private global i64 0
            @audit_bytes = private global i64 0
            define internal ptr @audit_alloc(ptr %heap, i32 %flags, i64 %size) {
            entry:
              %previous = load i64, ptr @audit_allocations
              %count = add i64 %previous, 1
              store i64 %count, ptr @audit_allocations
              %before = load i64, ptr @audit_bytes
              %total = add i64 %before, %size
              store i64 %total, ptr @audit_bytes
              %data = call ptr @HeapAlloc(ptr %heap, i32 %flags, i64 %size)
              ret ptr %data
            }
            define internal i32 @audit_free(ptr %heap, i32 %flags, ptr %data) {
            entry:
              %previous = load i64, ptr @audit_frees
              %count = add i64 %previous, 1
              store i64 %count, ptr @audit_frees
              %ok = call i32 @HeapFree(ptr %heap, i32 %flags, ptr %data)
              ret i32 %ok
            }

            """;
        ScalarEmissionTest.WriteFixture(name, ir, stdout);
    }
}
