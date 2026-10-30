// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

// Fails closed: an allocation, release, process exit or bounded transfer the rewrite cannot reach fails the test instead of
// escaping the counters.
internal static class NativeAllocationAudit
{
    // The runtime's two transfer routines, which both generators call (DECISIONS R5-M145): Array growth (RuntimeAbi.ArrayGrow) and the
    // Dictionary source's private byte copy (RuntimeAbi.TransferBytes, Storage.transferBytes) each copy high-water storage once, at their
    // anchor.
    private const string CopyAnchor = "  %used = mul i64 %length, %stride\n";
    private const string TransferAnchor = "  call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)\n";

    // Besides LLVM intrinsics, an audited module declares only the runtime's imports and the backend helpers (IMPL 21.4.7);
    // any other declaration in any linkage, such as a second allocator, would escape the counters.
    private static readonly string[] Declarations =
    [
        "GetProcessHeap", "HeapAlloc", "HeapFree", "GetStdHandle", "WriteFile", "GetLastError", "ExitProcess",
        "memcmp", "memcpy", "memmove", "memset", "__chkstk",
    ];

    // Each count lies in its bound at process exit; transferred, when given, bounds the bytes the transfer routines copy.
    internal static void WriteFixture(
        string name,
        string source,
        Bound allocations,
        Bound frees,
        Bound bytes,
        string stdout = "",
        int failAllocation = 0,
        Bound? transferred = null,
        Func<string, string>? transformIr = null,
        int exit = 0,
        string? stderr = null)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var writer = new StringWriter();
        Assert.True(c.WriteIr(writer, out var error), MinimalEmissionTest.Describe(c, error));
        var ir = writer.ToString();
        ir = transformIr is null ? ir : transformIr(ir);
        foreach (Match declaration in Regex.Matches(ir, @"(?m)^declare\b[^\n]*"))
        {
            var symbol = Regex.Match(declaration.Value, @"@([^(]*)\(").Groups[1].Value;
            if (!symbol.StartsWith("llvm.", StringComparison.Ordinal))
            {
                Assert.Contains(symbol, Declarations);
            }
        }

        AssertExitsThroughHook(ir);
        ir = Interpose(Interpose(Interpose(ir, "HeapAlloc", "audit_alloc"), "HeapFree", "audit_free"), "__kimi_exit", "__kimi_audit_exit");
        if (transferred is not null)
        {
            // A bound with neither routine would count nothing, and a program whose bodies transfer bytes counts them only through calls
            // of its module's byte copy: a module that writes its transfers in any other form, with or without the routine defined (the
            // storage byte primitives are defined as a pair), fails here instead of passing a bound vacuously.
            var grows = ir.Contains(RuntimeAbi.ArrayGrow.Name, StringComparison.Ordinal);
            var copies = ir.Contains(RuntimeAbi.TransferBytes.Name, StringComparison.Ordinal);
            Assert.True(grows || copies, "A transfer bound needs a routine to count.");
            var calls = ir.Contains("call void @" + RuntimeAbi.TransferBytes.Name + "(", StringComparison.Ordinal);
            Assert.True(calls || !TransfersBytes(source), "A program that transfers bytes needs its module to call the byte copy it counts.");
            ir = CountTransfers(ir, RuntimeAbi.ArrayGrow.Name, CopyAnchor, CopyAnchor + Transferred("%used"));
            ir = CountTransfers(ir, RuntimeAbi.TransferBytes.Name, TransferAnchor, Transferred("%size") + TransferAnchor);
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
            {{Within("alloc", allocations)}}
            {{Within("free", frees)}}
            {{Within("size", bytes)}}
            {{Within("transferred", transferred ?? Bound.Any)}}
              %counts = and i1 %alloc_ok, %free_ok
              %amounts = and i1 %size_ok, %transferred_ok
              %ok = and i1 %counts, %amounts
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

    // The process exits only through __kimi_exit (IMPL 21.4.2), where an audit hooks: ExitProcess may appear only on its
    // one declaration and inside that definition.
    internal static void AssertExitsThroughHook(string ir)
    {
        var exit = Assert.Single(Regex.Matches(ir, @"(?ms)^define [^\n]*@__kimi_exit\(.*?^\}"));
        var rest = ir.Remove(exit.Index, exit.Length);
        var declaration = Assert.Single(Regex.Matches(rest, @"(?m)^declare [^\n]*@ExitProcess\([^\n]*$"));
        Assert.DoesNotContain("ExitProcess", rest.Remove(declaration.Index, declaration.Length), StringComparison.Ordinal);
    }

    // Replaces the one occurrence of anchor; any other count fails.
    internal static string Instrument(string ir, string anchor, string instrumented)
    {
        Assert.Equal(2, ir.Split(anchor, StringSplitOptions.None).Length);
        return ir.Replace(anchor, instrumented, StringComparison.Ordinal);
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

    // Counts the copies at routine's anchor. The routine is mentioned in some spelling exactly when its anchor is present,
    // so neither a changed body nor a renamed routine with an unchanged body escapes.
    private static string CountTransfers(string ir, string routine, string anchor, string instrumented)
    {
        var mentioned = ir.Contains(routine, StringComparison.Ordinal);
        Assert.Equal(mentioned, ir.Contains(anchor, StringComparison.Ordinal));
        return mentioned ? Instrument(ir, anchor, instrumented) : ir;
    }

    // SPEC 22.2 (DECISIONS R5-M145): whether a body the program reaches calls Storage.transferBytes, read from its MIR and the instances its
    // startup reaches, the program analyzed as FunctionCodegenTest's form analyzes it.
    private static bool TransfersBytes(string source)
    {
        var c = FunctionCodegenTest.GeneratedDefinitions(source).C;
        var program = c.BuildMir();
        var set = c.Instances!;
        for (var item = 0; item < set.Items.Length; item++)
        {
            if (set.Items[item].Key.Kind != MonoKind.Body)
            {
                continue;
            }

            foreach (var block in program.Bodies[set.Items[item].BodyOrdinal].Blocks)
            {
                if (block.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.LibraryDeclaration: KimiDeclarationId.StorageTransferBytes })
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Adds bytes to the transferred total.
    private static string Transferred(string bytes)
        => $"  %before_copy = load i64, ptr @audit_transferred\n  %after_copy = add i64 %before_copy, {bytes}\n" +
            "  store i64 %after_copy, ptr @audit_transferred\n";

    // Sets %<counter>_ok when the counter lies in bound.
    private static string Within(string counter, Bound bound)
        => $"  %{counter}_low = icmp uge i64 %{counter}, {bound.Min}\n  %{counter}_high = icmp ule i64 %{counter}, {bound.Max}\n" +
            $"  %{counter}_ok = and i1 %{counter}_low, %{counter}_high";

    // An inclusive range of a count; a number converts to the range of exactly that count.
    internal readonly record struct Bound(long Min, long Max)
    {
        // Any count, for an amount a test does not claim.
        internal static readonly Bound Any = new(0, long.MaxValue);

        public static implicit operator Bound(long count) => new(count, count);
    }
}
