// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// Fails closed: an allocation, release, process exit or bounded transfer the rewrite cannot reach fails the test instead of
// escaping the counters.
internal static class NativeAllocationAudit
{
    // Array growth and the Dictionary source's private byte transfer each copy high-water storage once.
    private const string CopyAnchor = "  %used = mul i64 %length, %stride\n";
    private const string TransferAnchor = "  call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)\n";

    internal static void WriteFixture(string name, string source, int allocations, int frees, long bytes, string stdout = "", int failAllocation = 0, long maxTransferredBytes = -1, long minTransferredBytes = 0, Func<string, string>? transformIr = null, int exit = 0, string? stderr = null)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.True(c.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        var ir = writer.ToString();
        ir = transformIr is null ? ir : transformIr(ir);
        ir = Interpose(Interpose(Interpose(ir, "HeapAlloc", "audit_alloc"), "HeapFree", "audit_free"), "__kimi_exit", "__kimi_audit_exit");
        if (maxTransferredBytes >= 0)
        {
            ir = Instrument(ir, CopyAnchor, CopyAnchor + Transferred("%used"));
            ir = Instrument(ir, TransferAnchor, Transferred("%size") + TransferAnchor);
        }

        // Keep the real startup and cleanup path, including explicitly declared main.
        // Audit at process exit instead of assuming an implicit entry-body symbol.
        var start = $$"""
            define internal void @__kimi_audit_exit(i32 %exit) noreturn #0 {
            entry:
              %alloc = load i64, ptr @audit_allocations
              %free = load i64, ptr @audit_frees
              %size = load i64, ptr @audit_bytes
              %transferred = load i64, ptr @audit_transferred
              %a = icmp eq i64 %alloc, {{allocations}}
              %b = icmp eq i64 %free, {{frees}}
              %c = icmp eq i64 %size, {{bytes}}
              %d = icmp ule i64 %transferred, {{maxTransferredBytes}}
              %minimum = icmp uge i64 %transferred, {{minTransferredBytes}}
              %ab = and i1 %a, %b
              %cd = and i1 %c, %d
              %upper = and i1 %ab, %cd
              %ok = and i1 %upper, %minimum
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
        ir += $$"""

            @audit_allocations = private global i64 0
            @audit_frees = private global i64 0
            @audit_bytes = private global i64 0
            @audit_transferred = private global i64 0
            define internal ptr @audit_alloc(ptr %heap, i32 %flags, i64 %size) {
            entry:
              %previous = load i64, ptr @audit_allocations
              %count = add i64 %previous, 1
              store i64 %count, ptr @audit_allocations
              %before = load i64, ptr @audit_bytes
              %total = add i64 %before, %size
              store i64 %total, ptr @audit_bytes
              %fail = icmp eq i64 %count, {{failAllocation}}
              br i1 %fail, label %rejected, label %allocate
            rejected:
              ret ptr null
            allocate:
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
        ScalarEmissionTest.WriteFixture(name, ir, stdout, exit, stderr);
    }

    // Redirects every call of symbol to audit except on its one declaring line; any other mention, such as a non-call use
    // or another spelling, fails.
    private static string Interpose(string ir, string symbol, string audit)
    {
        var lines = ir.Split('\n');
        var declarations = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains(symbol, StringComparison.Ordinal))
            {
                continue;
            }

            if (lines[i].StartsWith("declare ", StringComparison.Ordinal) || lines[i].StartsWith("define ", StringComparison.Ordinal))
            {
                declarations++;
                continue;
            }

            lines[i] = lines[i].Replace("@" + symbol + "(", "@" + audit + "(", StringComparison.Ordinal);
            Assert.DoesNotContain(symbol, lines[i], StringComparison.Ordinal);
        }

        Assert.Equal(1, declarations);
        return string.Join('\n', lines);
    }

    // Replaces the one occurrence of anchor.
    private static string Instrument(string ir, string anchor, string instrumented)
    {
        Assert.Equal(2, ir.Split(anchor, StringSplitOptions.None).Length);
        return ir.Replace(anchor, instrumented, StringComparison.Ordinal);
    }

    // Adds bytes to the transferred total.
    private static string Transferred(string bytes)
        => $"  %before_copy = load i64, ptr @audit_transferred\n  %after_copy = add i64 %before_copy, {bytes}\n" +
            "  store i64 %after_copy, ptr @audit_transferred\n";
}
