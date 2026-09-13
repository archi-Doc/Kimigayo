// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace XunitTest;

// Test runtime instrumentation only. Source-generated functions and Static handles
// remain byte-for-byte unchanged; this does not pretend to test Heap construction.
internal static class StringLifetimeAudit
{
    internal static string Instrument(string ir, (string? Symbol, int Length, int Count)[] handles, bool duplicate = false)
    {
        var destroy = new Regex(@"(?m)^(define internal void @__kimi_destroy_string\([^\n]+\nentry:\n)");
        var exit = new Regex(@"(?m)^(define internal void @__kimi_exit\([^\n]+\nentry:\n)");
        Assert.Single(destroy.Matches(ir));
        Assert.Single(exit.Matches(ir));
        const string Hook = "  call void @__test_string_destroy(ptr %text)\n";
        var audited = destroy.Replace(ir, "$1" + Hook + (duplicate ? Hook : string.Empty));
        audited = exit.Replace(audited, "$1  call void @__test_string_exit()\n");
        var functions = new Regex(@"(?ms)^define (?:internal )?(?:void|i1|i8|i16|i32|i64) @__kimi_(?:entry_body|f\d+|start)\(.*?^}");
        Assert.NotEmpty(functions.Matches(ir));
        Assert.Equal(functions.Matches(ir).Select(x => x.Value), functions.Matches(audited).Select(x => x.Value));

        var output = new StringBuilder(audited);
        for (var i = 0; i < handles.Length; i++)
        {
            output.AppendLine($"@__test_string_count{i} = private global i32 0, align 4");
        }

        output.AppendLine("define internal void @__test_string_destroy(ptr %text) {\nentry:");
        output.AppendLine("  %data = load ptr, ptr %text, align 8\n  %lengthPtr = getelementptr %kimi.string, ptr %text, i32 0, i32 1\n  %length = load i64, ptr %lengthPtr, align 8\n  br label %test0");
        for (var i = 0; i < handles.Length; i++)
        {
            var address = handles[i].Symbol is { } symbol ? "@" + symbol : "null";
            output.AppendLine($"test{i}:\n  %address{i} = icmp eq ptr %data, {address}\n  %length{i} = icmp eq i64 %length, {handles[i].Length}\n  %same{i} = and i1 %address{i}, %length{i}\n  br i1 %same{i}, label %hit{i}, label %test{i + 1}");
            output.AppendLine($"hit{i}:\n  %old{i} = load i32, ptr @__test_string_count{i}, align 4\n  %new{i} = add i32 %old{i}, 1\n  store i32 %new{i}, ptr @__test_string_count{i}, align 4\n  ret void");
        }

        output.AppendLine($"test{handles.Length}:\n  call void @ExitProcess(i32 121)\n  unreachable\n}}");
        output.AppendLine("define internal void @__test_string_exit() {\nentry:\n  br label %test0");
        for (var i = 0; i < handles.Length; i++)
        {
            output.AppendLine($"test{i}:\n  %count{i} = load i32, ptr @__test_string_count{i}, align 4\n  %valid{i} = icmp eq i32 %count{i}, {handles[i].Count}\n  br i1 %valid{i}, label %test{i + 1}, label %fail");
        }

        output.AppendLine($"test{handles.Length}:\n  ret void\nfail:\n  call void @ExitProcess(i32 120)\n  unreachable\n}}");
        return output.ToString();
    }
}
