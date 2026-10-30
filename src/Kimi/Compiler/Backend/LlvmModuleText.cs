// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the module frame of the generated LLVM text (SPEC 21.3, 21.4, 22.6; DECISIONS R5-M14): the header with the pooled
// constants, the Windows runtime template with its ABI definitions spliced in and its test variant, the runtime routines a module
// defines only where a body calls them (TryAllocate, the nullable Alloc; TransferBytes, the byte copy), the checked splice every runtime
// template takes (ExpandRuntime; LegacyRuntimeHelpers' formatting runtime too), the external declarations, the overflow and memory
// intrinsic declarations, the footer's profile attributes, the spelling of external symbols, numbered names,
// numbers formatted on the stack (or once, as text, for a template filled at startup) and the bits of a floating constant's
// hexadecimal spelling. Code generation writes the frame through it (PD2: promoted, never copied; Name came from the old writer with
// ObjectRuntime, R5-M69; Hexadecimal from FunctionCodegen, replacing the old writer's hardware widening, PLAN G157 (19)).
// Input: a writer, the module's LlvmConstantPool, the test runtime text of a test build, ExternalFunction records, numbers, a
// floating constant's IEEE bits, name prefixes and symbols; the runtime ABI records (RuntimeAbi), the abort reasons (AbortReasons)
// and the target profile (WindowsProfile).
// Output: LLVM text written to the writer; a symbol's spelling after @ and the symbol recovered from that spelling; an expanded
// runtime template; a floating constant's hexadecimal spelling bits.
// Invariants: warm writes allocate nothing; each runtime ABI
// definition marker of a template is spliced exactly once and none is left unexpanded, or the template fails; the text holds no
// checkout path.
// Must not take syntax, HIR, Binding internals or MIR types in its signatures, or use LINQ.
// Files: the module frame and the runtime templates (this file); the spellings of symbols, numbers, names and floating constants
// (LlvmModuleText.Spelling.cs).

#pragma warning disable SA1402 // The external declaration record is this writer's input.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Backend;

/// <summary>A foreign function declaration; DllImport selects the import-library form (SPEC 20.4).</summary>
internal readonly record struct ExternalFunction(FunctionAbi Abi, bool DllImport);

/// <summary>Writes the syntax-free module frame of the LLVM text.</summary>
internal static partial class LlvmModuleText
{
    // Every generated definition carries the same profile attributes (SPEC 21.3.1).
    internal const string Footer =
        "attributes #0 = { uwtable(" + WindowsProfile.UnwindTables + ") \"target-cpu\"=\"" + WindowsProfile.Cpu + "\" \"target-features\"=\"" +
        WindowsProfile.Features + "\" \"denormal-fp-math\"=\"ieee,ieee\" }\n!llvm.module.flags = !{!0}\n!0 = !{i32 8, !\"PIC Level\", i32 2}\n";

    // Fixed profile vocabulary: no per-module tracking or warm declaration construction.
    // The runtime Array capacity routines and the aggregate helpers copy bytes through these intrinsics.
    internal const string MemoryDeclarations =
        "declare void @llvm.memcpy.p0.p0.i64(ptr noalias nocapture writeonly, ptr noalias nocapture readonly, i64, i1 immarg)\n" +
        "declare void @llvm.memmove.p0.p0.i64(ptr nocapture writeonly, ptr nocapture readonly, i64, i1 immarg)\n";

    internal const string OverflowDeclarations = """
        declare { i8, i1 } @llvm.sadd.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.ssub.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.smul.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.uadd.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.usub.with.overflow.i8(i8, i8)
        declare { i8, i1 } @llvm.umul.with.overflow.i8(i8, i8)
        declare { i16, i1 } @llvm.sadd.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.ssub.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.smul.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.uadd.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.usub.with.overflow.i16(i16, i16)
        declare { i16, i1 } @llvm.umul.with.overflow.i16(i16, i16)
        declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.ssub.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.smul.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.uadd.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.usub.with.overflow.i32(i32, i32)
        declare { i32, i1 } @llvm.umul.with.overflow.i32(i32, i32)
        declare { i64, i1 } @llvm.sadd.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.ssub.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.smul.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.uadd.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.usub.with.overflow.i64(i64, i64)
        declare { i64, i1 } @llvm.umul.with.overflow.i64(i64, i64)
        """ + "\n";

    // The i128 forms, written only by a module whose checked arithmetic uses them.
    internal const string WideOverflowDeclarations = """
        declare { i128, i1 } @llvm.sadd.with.overflow.i128(i128, i128)
        declare { i128, i1 } @llvm.ssub.with.overflow.i128(i128, i128)
        declare { i128, i1 } @llvm.smul.with.overflow.i128(i128, i128)
        declare { i128, i1 } @llvm.uadd.with.overflow.i128(i128, i128)
        declare { i128, i1 } @llvm.usub.with.overflow.i128(i128, i128)
        declare { i128, i1 } @llvm.umul.with.overflow.i128(i128, i128)
        """ + "\n";

    // SPEC 21.4.3, 22.2: the nullable form of Alloc (RuntimeAbi.TryAllocateBytes), defined where a body calls it: null wherever Alloc
    // would Abort (a size above isize's range, no process heap, a failed allocation); a zero-byte request allocates one byte, so the
    // result is a fresh start; no location to report.
    internal static readonly string TryAllocate = ExpandRuntime(
        """
        {{__kimi_try_allocate}}
        entry:
          %invalid = icmp ugt i64 %bytes, 9223372036854775807
          br i1 %invalid, label %failed, label %heap
        heap:
          %process_heap = call ptr @GetProcessHeap()
          %no_heap = icmp eq ptr %process_heap, null
          br i1 %no_heap, label %failed, label %try
        try:
          %zero = icmp eq i64 %bytes, 0
          %actual = select i1 %zero, i64 1, i64 %bytes
          %memory = call ptr @HeapAlloc(ptr %process_heap, i32 0, i64 %actual)
          ret ptr %memory
        failed:
          ret ptr null
        }

        """,
        [RuntimeAbi.TryAllocateBytes]);

    // SPEC 22.2: the byte copy (RuntimeAbi.TransferBytes), defined where a body calls it: its ranges never overlap (the declaration's
    // contract), so llvm.memcpy (DECISIONS R5-M18, R5-M92); it moves no responsibility and has no location to report. Both generators
    // call it, so the native audits count one routine (DECISIONS R5-M145).
    internal static readonly string TransferBytes = ExpandRuntime(
        """
        {{__kimi_transfer_bytes}}
        entry:
          call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)
          ret void
        }

        """,
        [RuntimeAbi.TransferBytes]);

    // Target information, shared Types, and exactly one strong _fltused definition (SPEC 21.4.7).
    private static readonly string Header =
        "; Kimigayo checked pre-optimization IR (" + WindowsProfile.Name + ")\ntarget triple = \"" + WindowsProfile.Target + "\"\n" +
        "target datalayout = \"" + WindowsProfile.DataLayout + "\"\n" +
        RuntimeAbi.String.Layout.StorageType + " = type { ptr, i64, i8 }\n@" + WindowsProfile.FloatMarker + " = global i32 0, align 4\n";

    private static readonly string Runtime = ReadRuntime();

    // A test build's runtime: each Abort definition first reports the aborted test to the test runtime.
    private static readonly string TestRuntimeBase = ReportTestAbort(ReportTestAbort(Runtime, RuntimeAbi.Abort), RuntimeAbi.AbortMessage);

    // The module header, then the definitions of the module's pooled constants.
    internal static void WriteHeader(TextWriter output, LlvmConstantPool constants)
    {
        output.Write(Header);
        for (var i = 0; i < constants.Count; i++)
        {
            output.Write(constants[i].Definition);
        }
    }

    // The runtime template, in its test variant when a test build supplies its test runtime text, followed by that text.
    internal static void WriteRuntime(TextWriter output, string? testRuntime)
    {
        output.Write(testRuntime is null ? Runtime : TestRuntimeBase);
        output.Write(testRuntime);
    }

    // SPEC 22.6: one declaration per foreign symbol. A kernel32 import that the runtime WriteRuntime wrote (with the same
    // testRuntime) already declares shares that declaration; Binding proved their physical Types equal (SPEC 21.2).
    internal static void WriteExternals(TextWriter output, ReadOnlySpan<ExternalFunction> externals, string? testRuntime)
    {
        var runtime = testRuntime is null ? Runtime : TestRuntimeBase;
        foreach (var external in externals)
        {
            var abi = external.Abi;
            if (Declares(runtime, abi.Name) || (testRuntime is not null && Declares(testRuntime, abi.Name)))
            {
                continue;
            }

            output.Write(external.DllImport ? "declare dllimport " : "declare ");
            output.Write(abi.Result);
            output.Write(" @");
            output.Write(abi.Name);
            output.Write('(');
            for (var i = 0; i < abi.Parameters.Length; i++)
            {
                if (i != 0)
                {
                    output.Write(", ");
                }

                output.Write(abi.Parameters[i].Type);
            }

            output.Write(")\n");
        }
    }

    // A runtime template (the Windows runtime, the formatting runtime) with its abort reason markers expanded and each definition
    // spliced in at its `{{name}}` marker. Compiler-facing runtime signatures come from the same FunctionAbi records as calls and
    // each is defined once, so a missing or repeated marker, or a marker left unexpanded, is a broken template.
    internal static string ExpandRuntime(string template, ReadOnlySpan<FunctionAbi> definitions)
    {
        var runtime = AbortReasons.Expand(template.Replace("\r\n", "\n", StringComparison.Ordinal));
        foreach (var abi in definitions)
        {
            var marker = "{{" + abi.Name + "}}\n";
            var index = runtime.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0 || runtime.IndexOf(marker, index + marker.Length, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException("The runtime template must contain each shared ABI definition marker exactly once.");
            }

            runtime = runtime.Replace(marker, abi.GetDefinition(exported: false), StringComparison.Ordinal);
        }

        if (runtime.Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The runtime template has an unexpanded ABI definition.");
        }

        return runtime;
    }

    // Whether the runtime text declares the symbol: `@name(` on a line that begins with `declare `.
    private static bool Declares(string runtime, string name)
    {
        for (var index = runtime.IndexOf(name, StringComparison.Ordinal); index >= 0; index = runtime.IndexOf(name, index + 1, StringComparison.Ordinal))
        {
            var end = index + name.Length;
            var symbol = index != 0 && runtime[index - 1] == '@' && end < runtime.Length && runtime[end] == '(';
            if (symbol && runtime.AsSpan(runtime.LastIndexOf('\n', index) + 1).StartsWith("declare ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadRuntime()
    {
        using var stream = typeof(LlvmModuleText).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission.WindowsRuntime.ll.in")!;
        using var reader = new StreamReader(stream);
        return ExpandRuntime(reader.ReadToEnd(), RuntimeAbi.RuntimeDefinitions) + "\n";
    }

    private static string ReportTestAbort(string runtime, FunctionAbi abort)
    {
        var entry = abort.GetDefinition(exported: false) + "entry:\n";
        return runtime.Replace(entry, entry + "  call void @__kimi_test_aborted()\n", StringComparison.Ordinal);
    }
}
