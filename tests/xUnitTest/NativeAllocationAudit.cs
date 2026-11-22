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
    // The runtime's transfer routine, which generated code calls (DECISIONS R5-M145): the private byte copy (RuntimeAbi.TransferBytes,
    // Storage.transferBytes), through which Array and Dictionary growth copy high-water storage once, at its anchor.
    private const string TransferAnchor = "  call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)\n";

    // Besides LLVM intrinsics, an audited module declares only the runtime's imports and the backend helpers (IMPL 21.4.7; the memory
    // functions among them are the module's own definitions since the Tatara plan's decision 24, so no line declares them); any other
    // declaration in any linkage, such as a second allocator, would escape the counters.
    private static readonly string[] Declarations =
    [
        "GetProcessHeap", "HeapAlloc", "HeapFree", "GetStdHandle", "WriteFile", "GetLastError", "ExitProcess", "VirtualAlloc", "VirtualFree",
        "memcmp", "memcpy", "memmove", "memset", "__chkstk",
    ];

    // The kernel32 functions each platform layer routine may call (the Tatara plan's B7r1 and B7r2; C-468's N2): only the exit ends the
    // process and only the pages routines map and release pages, so a second allocator or exit is caught by its call site. A runtime
    // declaration no row names, such as the process heap's three until B7r3 removes them, may be called nowhere.
    private static readonly (string Routine, string[] Symbols)[] LayerCalls =
    [
        (RuntimeAbi.Exit.Name, ["ExitProcess"]),
        (RuntimeAbi.Write.Name, ["GetStdHandle", "WriteFile", "GetLastError"]),
        (RuntimeAbi.Pages.Name, ["VirtualAlloc"]),
        (RuntimeAbi.ReleasePages.Name, ["VirtualFree", "GetLastError"]),
    ];

    // The runtime text's own definitions, read once from their owner: LlvmModuleText.WriteRuntime's (the layer, the common part, the
    // allocator and the memory part) and the module parts a body requests (the nullable Alloc and the byte copy).
    private static readonly HashSet<string> RuntimeDefinitions = ReadRuntimeDefinitions();

    // Each count lies in its bound at process exit; transferred, when given, bounds the bytes the transfer routine copies.
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
        Assert.True(c.Generate(writer), MinimalEmissionTest.Describe(c));
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

        AssertOsCallsInLayer(ir);
        ir = Interpose(Interpose(Interpose(ir, RuntimeAbi.HeapAllocate.Name, "audit_alloc"), RuntimeAbi.HeapRelease.Name, "audit_free"), "__kimi_exit", "__kimi_audit_exit");
        if (transferred is not null)
        {
            // A bound without the routine would count nothing, and a program whose bodies transfer bytes counts them only through calls
            // of its module's byte copy: a module that writes its transfers in any other form, with or without the routine defined (the
            // storage byte primitives are defined as a pair), fails here instead of passing a bound vacuously.
            var copies = ir.Contains(RuntimeAbi.TransferBytes.Name, StringComparison.Ordinal);
            Assert.True(copies, "A transfer bound needs a routine to count.");
            var calls = ir.Contains("call void @" + RuntimeAbi.TransferBytes.Name + "(", StringComparison.Ordinal);
            Assert.True(calls || !TransfersBytes(source), "A program that transfers bytes needs its module to call the byte copy it counts.");
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
            define internal ptr @audit_alloc(i64 %size) {
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
              %data = call ptr @{{RuntimeAbi.HeapAllocate.Name}}(i64 %size)
              ret ptr %data
            }
            define internal i64 @audit_free(ptr %data) {
            entry:
              %previous = load i64, ptr @audit_frees
              %count = add i64 %previous, 1
              store i64 %count, ptr @audit_frees
              %error = call i64 @{{RuntimeAbi.HeapRelease.Name}}(ptr %data)
              ret i64 %error
            }

            """;
        ScalarEmissionTest.WriteFixture(name, ir, stdout, exit, stderr);
    }

    // IMPL 21.4.2, 21.4.7 (the Tatara plan's B7r1, B7r2): the platform layer owns the runtime's OS calls in a program module. Every
    // mention of a runtime kernel32 function (TargetProfile.Windows.RuntimeDeclarations) but its one declaration in the runtime text's
    // definitions lies inside the layer routine (RuntimeAbi.PlatformRoutines) that LayerCalls lets call it: the process exits only in
    // __kimi_exit, where an audit hooks, and pages are mapped and released only in the pages routines, below the allocator the audit
    // counts. Any other definition is the program's, whose kernel32 calls are its own imports (outside the layer), each sharing the
    // runtime's declaration only with its signature (SPEC 21.2): none can share ExitProcess's, so the program never exits around the hook.
    internal static void AssertOsCallsInLayer(string ir)
    {
        Assert.Equal(RuntimeAbi.PlatformRoutines.Select(static x => x.Name), LayerCalls.Select(static x => x.Routine));
        var offenders = new List<string>();
        var definitions = new Regex(@"(?ms)^define [^\n]*?@([^(\s]+)\(.*?^\}");
        foreach (Match definition in definitions.Matches(ir))
        {
            var name = definition.Groups[1].Value;
            var runtime = RuntimeDefinitions.Contains(name);
            var row = Array.FindIndex(LayerCalls, x => x.Routine == name);
            string[] calls = row < 0 ? [] : LayerCalls[row].Symbols;
            foreach (var (symbol, signature) in TargetProfile.Windows.RuntimeDeclarations)
            {
                var allowed = Array.IndexOf(calls, symbol) >= 0 || (!runtime && signature is not null);
                if (!allowed && Mentions(definition.Value, symbol))
                {
                    offenders.Add(name + " mentions " + symbol);
                }
            }
        }

        var outside = definitions.Replace(ir, string.Empty);
        foreach (var (symbol, _) in TargetProfile.Windows.RuntimeDeclarations)
        {
            var lines = outside.Split('\n').Where(x => Mentions(x, symbol)).ToArray();
            if (lines.Length > 1 || (lines.Length == 1 && !lines[0].StartsWith("declare ", StringComparison.Ordinal)))
            {
                offenders.Add("the module text mentions " + symbol + " outside its declaration");
            }
        }

        Assert.Empty(offenders);

        static bool Mentions(string text, string symbol) => Regex.IsMatch(text, "@" + Regex.Escape(symbol) + @"(?![\w.$])");
    }

    // Replaces the one occurrence of anchor; any other count fails.
    internal static string Instrument(string ir, string anchor, string instrumented)
    {
        Assert.Equal(2, ir.Split(anchor, StringSplitOptions.None).Length);
        return ir.Replace(anchor, instrumented, StringComparison.Ordinal);
    }

    // The names the runtime text defines, as its define lines spell them.
    private static HashSet<string> ReadRuntimeDefinitions()
    {
        var text = new StringWriter();
        LlvmModuleText.WriteRuntime(text, null);
        text.Write(LlvmModuleText.TryAllocate + LlvmModuleText.TransferBytes);
        return [.. Regex.Matches(text.ToString(), @"(?m)^define [^@\n]*@([^(\n]+)\(").Select(static x => x.Groups[1].Value)];
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

            foreach (var block in program.Body(set.Items[item].BodyOrdinal).Blocks)
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
