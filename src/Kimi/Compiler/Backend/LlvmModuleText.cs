// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the module frame of the generated LLVM text (SPEC 21.3, 21.4, 22.6; DECISIONS R5-M14), one per target row (Frame, built once per
// row of the target table on first use): the header with the pooled constants (the row's float marker where it has one), the runtime,
// composed of the row's platform layer (its RuntimeLayer's template; windows-x64's WindowsPlatform.ll.in owns every kernel32 call of the
// runtime in a program module, linux-x64's LinuxPlatform.ll.in every system call, x86_64-unknown-none's NonePlatform.ll.in Limine's boot
// data and the platform's routines over the program's adapters, which ModuleCodegen's frame defines), the common part (Runtime.ll.in) and
// Kimi's allocator (HeapRuntime.ll.in)
// and expanded in one scan, with the layer's ABI definitions spliced in and the row's constants written, and its test variant (none for
// a layer naming no test runtime, where a test build is a broken contract), each with the row's runtime declarations it declares (its
// imports, which the generation result publishes for the link; LinkAmbiguity), the memory part every module defines after it
// (MemoryRuntime.ll.in: the runtime's memcmp, memcpy, memmove and memset, TargetProfile.MemorySymbols), the erased writer's kept runtime
// (ErasedWriterRuntime.ll.in: the ErasedReserve intrinsic's routine, a module part on request), the float formatting part
// (Utf8FloatRuntime.ll.in and Utf8FloatRyu.ll.in: the FormatFloat intrinsic's routine and Ryu, a module part on request), the 128-bit
// integer formatting part (FormatInteger128Runtime.ll.in: the FormatInteger128 intrinsic's routine and its division by ten, a module part
// on request), the runtime routines a module defines only where a body calls them (TryAllocate, the nullable Alloc; TransferBytes, the byte
// copy), the checked one-scan expansion every runtime template takes (ExpandRuntime; TestRuntime's text too),
// the external declarations, the overflow and memory intrinsic declarations, the footer (the row's profile attributes with its
// FunctionAttributes, #0; #1, #0 plus "no-builtins", for the memory part; the PIC Level module flag of a pic row, none for a static
// one), the spelling of external symbols, numbered names, numbers formatted on the stack and the bits of
// a floating constant's hexadecimal spelling. Code generation writes the frame through it (PD2: promoted, never copied; Name came from the old writer with ObjectRuntime, R5-M69; Hexadecimal from
// FunctionCodegen, replacing the old writer's hardware widening, PLAN G157 (19)).
// Input: a writer, a target row (TargetProfile) and its frame, the module's LlvmConstantPool, the test runtime text of a test build,
// ExternalFunction records, numbers, a floating constant's IEEE bits, name prefixes and symbols; the runtime ABI records, constants and
// layer records (RuntimeAbi) and the abort reasons (AbortReasons).
// Output: LLVM text written to the writer; a row's frame; a symbol's spelling after @; an expanded
// runtime template; a floating constant's hexadecimal spelling bits.
// Invariants: warm writes allocate nothing; a row's frame is built once, on its first use, so a process builds no other row's text; a
// template is expanded in one ordered scan into one builder, a row's constants given to the row's runtime only (a target-free
// template naming one fails); each runtime ABI definition marker of a template is spliced exactly once, every `{{` opens a marker and
// none is left unexpanded, or the template fails; a row's relocation model is pic or static, or its footer fails; the text holds no
// checkout path.
// Must not take syntax, HIR, Binding internals or MIR types in its signatures, or use LINQ.
// Files: the module frame and the runtime templates (this file); the spellings of symbols, numbers, names and floating constants
// (LlvmModuleText.Spelling.cs).

#pragma warning disable SA1402 // The external declaration record is this writer's input.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Backend;

/// <summary>A foreign function declaration: its C signature named by the symbol's spelling (ExternalName), the external symbol itself,
/// which the generation result publishes for the link (R9-B1), DllImport, the import-library form, which only the IR writer reads
/// (WriteExternals; SPEC 20.4, 22.6), and Library, the import's providing library, which the link reads (Binding's
/// LibraryImport.Library; IMPL 21.2; NamedProvider).</summary>
internal readonly record struct ExternalFunction(FunctionAbi Abi, string Symbol, bool DllImport, string Library);

/// <summary>Writes the syntax-free module frame of the LLVM text.</summary>
internal static partial class LlvmModuleText
{
    // Fixed profile vocabulary: no per-module tracking or warm declaration construction.
    // The aggregate helpers copy bytes through these intrinsics.
    internal const string MemoryDeclarations =
        "declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)\n" +
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

    // IMPL 21.4.7 (the Tatara plan's decision 24): the runtime's C memory functions, which every module defines after the runtime template
    // (WriteRuntime) and generated code may call: memcmp, memcpy, memmove and memset (TargetProfile.MemorySymbols), external so that code
    // generation's libcalls bind to them after the IR passes, each under #1.
    internal static readonly string MemoryRuntime = ExpandRuntime(Template("MemoryRuntime.ll.in"), [], []);

    // SPEC 21.4.3, 22.2: the nullable form of Alloc (RuntimeAbi.TryAllocateBytes), defined where a body calls it: null wherever Alloc
    // would Abort (a size above isize's range, a failed allocation), the allocator's block (RuntimeAbi.HeapAllocate) otherwise, a fresh
    // start; no location to report.
    internal static readonly string TryAllocate = ExpandRuntime(
        """
        {{__kimi_try_allocate}}
        entry:
          %invalid = icmp ugt i64 %bytes, 9223372036854775807
          br i1 %invalid, label %failed, label %heap
        heap:
          %memory = call ptr @__kimi_heap_allocate(i64 %bytes)
          ret ptr %memory
        failed:
          ret ptr null
        }

        """,
        [RuntimeAbi.TryAllocateBytes],
        []);

    // SPEC 22.2: the byte copy (RuntimeAbi.TransferBytes), defined where a body calls it: its ranges never overlap (the declaration's
    // contract), so llvm.memcpy (DECISIONS R5-M18, R5-M92); it moves no responsibility and has no location to report. Code
    // generation calls this one routine, which the native audits count (DECISIONS R5-M145).
    internal static readonly string TransferBytes = ExpandRuntime(
        """
        {{__kimi_transfer_bytes}}
        entry:
          call void @llvm.memcpy.p0.p0.i64(ptr %destination, ptr %source, i64 %size, i1 false)
          ret void
        }

        """,
        [RuntimeAbi.TransferBytes],
        []);

    // The frames of the target table's rows, each built on its first use (Frame), in TargetProfile.AllRows' order.
    private static readonly Lazy<ModuleFrame>[] Frames = Array.ConvertAll(TargetProfile.AllRows, static row => new Lazy<ModuleFrame>(() => new(row)));

    /// <summary>Gets a target row's module frame, built once, on the row's first use.</summary>
    /// <param name="row">A row of the target table; another is a broken contract.</param>
    /// <returns>The row's frame.</returns>
    internal static ModuleFrame Frame(TargetProfile row)
    {
        var index = Array.IndexOf(TargetProfile.AllRows, row);
        return index >= 0 ? Frames[index].Value : throw new ArgumentException("The row must be a row of the target table.", nameof(row));
    }

    // The module header, then the definitions of the module's pooled constants.
    internal static void WriteHeader(TextWriter output, ModuleFrame frame, LlvmConstantPool constants)
    {
        output.Write(frame.Header);
        for (var i = 0; i < constants.Count; i++)
        {
            output.Write(constants[i].Definition);
        }
    }

    // The frame's runtime, in its test variant when a test build supplies its test runtime text, then the memory part, then that text. A
    // test build on a row whose layer names no test runtime is a broken contract (B7b8 refuses such a test before any build).
    internal static void WriteRuntime(TextWriter output, ModuleFrame frame, string? testRuntime)
    {
        output.Write(testRuntime is null
            ? frame.Runtime
            : frame.TestRuntime ?? throw new InvalidOperationException("A test build's target row must have a test runtime."));
        output.Write(MemoryRuntime);
        output.Write(testRuntime);
    }

    // SPEC 22.6: one declaration per foreign symbol. A kernel32 import that the runtime WriteRuntime wrote (with the same frame and
    // testRuntime) already declares shares that declaration; Binding proved their physical Types equal (SPEC 21.2). The runtime's test
    // variant declares what the runtime declares (it adds calls only).
    internal static void WriteExternals(TextWriter output, ModuleFrame frame, ReadOnlySpan<ExternalFunction> externals, string? testRuntime)
    {
        foreach (var external in externals)
        {
            var abi = external.Abi;
            if (Declares(frame.Runtime, abi.Name) || (testRuntime is not null && Declares(testRuntime, abi.Name)))
            {
                continue;
            }

            output.Write(external.DllImport ? "declare dllimport " : "declare ");
            WriteDeclaration(output, abi);
        }
    }

    // The one writer of a function's declaration after its `declare` and linkage words: its result, its name and its parameter Types,
    // without names or attributes (an external's every call writes its own, AbiParameter.Attributes).
    internal static void WriteDeclaration(TextWriter output, FunctionAbi abi)
    {
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

    // A runtime template (a row's runtime, the memory part, the erased writer, the formatting parts and the test runtime) expanded in one
    // ordered scan into one builder sized for the template and its definitions: `\r\n` becomes `\n`, each definition is spliced in at
    // its `{{name}}` marker line and every
    // other marker takes its owner's text, an abort reason's (AbortReasons.Expand) or an ABI constant's (RuntimeAbi.Expand: a
    // target-free one or one of the given row constants, which only a row's runtime is given), whose names are disjoint.
    // Compiler-facing runtime signatures come from the same FunctionAbi records as calls and each is defined once, so a missing or
    // repeated definition marker, or a marker no owner expands, is a broken template. Every `{{` opens a marker, so a `{` right before
    // one (`{{{name}}`) fails too.
    internal static string ExpandRuntime(string template, ReadOnlySpan<FunctionAbi> definitions, ReadOnlySpan<(string Name, string Text)> constants)
    {
        var length = template.Length;
        foreach (var abi in definitions)
        {
            length += abi.GetDefinition(exported: false).Length;
        }

        var runtime = new StringBuilder(length);
        var spliced = new bool[definitions.Length];
        var rest = template.AsSpan();
        for (var next = rest.IndexOfAny('{', '\r'); next >= 0; next = rest.IndexOfAny('{', '\r'))
        {
            runtime.Append(rest[..next]);
            rest = rest[next..];
            if (rest.StartsWith("{{", StringComparison.Ordinal))
            {
                rest = Splice(runtime, rest, definitions, constants, spliced);
                continue;
            }

            var newline = rest.StartsWith("\r\n", StringComparison.Ordinal);
            runtime.Append(newline ? '\n' : rest[0]);
            rest = rest[(newline ? 2 : 1)..];
        }

        return Array.IndexOf(spliced, false) >= 0
            ? throw new InvalidDataException("The runtime template must contain each shared ABI definition marker exactly once.")
            : runtime.Append(rest).ToString();
    }

    /// <summary>Gets a platform layer's runtime template: the layer template (windows-x64's WindowsPlatform.ll.in: the definitions of
    /// RuntimeAbi.PlatformRoutines and every kernel32 call of the runtime in a program module), the common part (Runtime.ll.in), then
    /// Kimi's allocator (HeapRuntime.ll.in), read as one text.</summary>
    /// <param name="layer">The layer's record.</param>
    /// <returns>The template.</returns>
    internal static string RuntimeTemplate(RuntimeLayer layer) => Template(layer.Template) + "\n" + Template("Runtime.ll.in") + "\n" + Template("HeapRuntime.ll.in");

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

    /// <summary>Gets the erased writer's kept runtime (ErasedWriterRuntime.ll.in), expanded once.</summary>
    internal static string ErasedWriterRuntime => ErasedWriter.Text;

    /// <summary>Gets the float formatting part (Utf8FloatRuntime.ll.in, then Utf8FloatRyu.ll.in), the FormatFloat intrinsic's routine
    /// with Ryu, expanded once, on first use.</summary>
    internal static string FormatFloatRuntime => FormatFloat.Text;

    /// <summary>Gets the 128-bit integer formatting part (FormatInteger128Runtime.ll.in), the FormatInteger128 intrinsic's routine with
    /// its division by ten, expanded once, on first use.</summary>
    internal static string FormatInteger128Runtime => FormatInteger128.Text;

    // A runtime template's text: the embedded resource of the file beside this one.
    private static string Template(string file)
    {
        using var stream = typeof(LlvmModuleText).Assembly.GetManifestResourceStream("Kimi.Compiler.Backend." + file)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Writes the marker that begins the rest of a template and returns the text after it: a definition's marker line (the marker
    // and its newline) takes the definition the first time; any other marker its owner's text. A repeated definition marker,
    // an unknown marker or one without its end is left unexpanded, so the template fails.
    private static ReadOnlySpan<char> Splice(
        StringBuilder runtime, ReadOnlySpan<char> rest, ReadOnlySpan<FunctionAbi> definitions, ReadOnlySpan<(string Name, string Text)> constants, bool[] spliced)
    {
        var end = rest.IndexOf("}}", StringComparison.Ordinal);
        ReadOnlySpan<char> name = end < 0 ? [] : rest[2..end];
        var after = rest[(end + 2)..];
        var line = after.StartsWith("\n", StringComparison.Ordinal) ? 1 : 0;
        if (after.StartsWith("\r\n", StringComparison.Ordinal))
        {
            line = 2;
        }

        for (var i = 0; i < definitions.Length; i++)
        {
            if (line != 0 && !spliced[i] && name.SequenceEqual(definitions[i].Name))
            {
                spliced[i] = true;
                runtime.Append(definitions[i].GetDefinition(exported: false));
                return after[line..];
            }
        }

        return AbortReasons.Expand(name, runtime) || RuntimeAbi.Expand(name, constants, runtime)
            ? after
            : throw new InvalidDataException("The runtime template has an unexpanded marker.");
    }

    private static string ReportTestAbort(string runtime, FunctionAbi abort)
    {
        var entry = abort.GetDefinition(exported: false) + "entry:\n";
        return runtime.Replace(entry, entry + "  call void @__kimi_test_aborted()\n", StringComparison.Ordinal);
    }

    /// <summary>A target row's module frame (the Tatara plan's B7b2a): its header, runtime, the runtime's test variant and footer, built
    /// once per row (Frame).</summary>
    internal sealed class ModuleFrame
    {
        internal ModuleFrame(TargetProfile row)
        {
            this.Layer = RuntimeAbi.Layer(row);
            this.Header = "; Kimigayo checked pre-optimization IR (" + row.Name + ")\ntarget triple = \"" + row.Target + "\"\n" +
                "target datalayout = \"" + row.DataLayout + "\"\n" + RuntimeAbi.String.Layout.StorageType + " = type { ptr, i64, i8 }\n" +
                (row.FloatMarker is { } marker ? "@" + marker + " = global i32 0, align 4\n" : string.Empty);
            this.Runtime = ExpandRuntime(RuntimeTemplate(this.Layer), this.Layer.Definitions, RuntimeAbi.RowConstants(row)) + "\n";
            this.TestRuntime = this.Layer.TestRuntime is null
                ? null
                : ReportTestAbort(ReportTestAbort(this.Runtime, RuntimeAbi.Abort), RuntimeAbi.AbortMessage);
            this.Footer = FooterText(row);
            this.RuntimeImports = Imports(row, this.Runtime, null);
            this.TestRuntimeImports = this.TestRuntime is null ? null : Imports(row, this.TestRuntime, Testing.TestRuntime.Text);
        }

        /// <summary>Gets the row's platform layer, whose start form the module's entry takes (ModuleCodegen's start).</summary>
        internal RuntimeLayer Layer { get; }

        /// <summary>Gets the header: target information, shared Types, and exactly one strong definition of the row's float marker (SPEC
        /// 21.4.7), where the row has one.</summary>
        internal string Header { get; }

        /// <summary>Gets the runtime: the row's platform layer, the common part and the allocator, one text expanded in one scan with the
        /// layer's definitions and the row's constants.</summary>
        internal string Runtime { get; }

        /// <summary>Gets a test build's runtime: each Abort definition first reports the aborted test to the test runtime; null for a layer
        /// naming no test runtime.</summary>
        internal string? TestRuntime { get; }

        /// <summary>Gets the imports the runtime declares (LinkAmbiguity, T's ruling 32): the row's runtime declarations whose symbol it
        /// declares, in the row's order (TargetProfile.RuntimeDeclarations), each with its providing supply (NamedProvider).</summary>
        internal (string Symbol, string Library)[] RuntimeImports { get; }

        /// <summary>Gets the imports a test build's runtime declares: those of <see cref="TestRuntime"/> and of the test runtime's routines
        /// (Testing's TestRuntime.Text), in the row's order, each with its providing supply; null for a layer naming no test
        /// runtime.</summary>
        internal (string Symbol, string Library)[]? TestRuntimeImports { get; }

        /// <summary>Gets the footer: every generated definition carries the row's profile attributes (SPEC 21.3.1) with its
        /// FunctionAttributes, #0; the memory part's definitions take #1, #0 plus "no-builtins", so no pass turns their loops into calls of
        /// themselves (the Tatara plan's decision 24); a pic row's module states its PIC Level, a static row's none, as clang writes them.</summary>
        internal string Footer { get; }

        // The footer's text; a relocation model other than pic and static is a broken row.
        private static string FooterText(TargetProfile row)
        {
            var profile = "uwtable(" + row.UnwindTables + ") \"target-cpu\"=\"" + row.Cpu + "\" \"target-features\"=\"" + row.Features +
                "\" \"denormal-fp-math\"=\"ieee,ieee\"";
            foreach (var attribute in row.FunctionAttributes)
            {
                profile += " " + attribute;
            }

            var flags = row.RelocationModel switch
            {
                "pic" => "!llvm.module.flags = !{!0}\n!0 = !{i32 8, !\"PIC Level\", i32 2}\n",
                "static" => string.Empty,
                _ => throw new InvalidDataException("The target row's relocation model must be pic or static."),
            };
            return "attributes #0 = { " + profile + " }\nattributes #1 = { " + profile + " \"no-builtins\" }\n" + flags;
        }

        // The row's runtime declarations a runtime declares, or the test runtime's routines do, in the row's order, each with the supply
        // TargetProfile names as its provider: Core's contract, that exactly one supply lists each runtime symbol, so a missing one is a
        // broken contract, never a default.
        private static (string Symbol, string Library)[] Imports(TargetProfile row, string runtime, string? routines)
        {
            var imports = new List<(string Symbol, string Library)>();
            foreach (var (symbol, _) in row.RuntimeDeclarations)
            {
                if (Declares(runtime, symbol) || (routines is not null && Declares(routines, symbol)))
                {
                    _ = TargetProfile.TryGetRuntimeDeclaration(symbol, out _, out var provider);
                    imports.Add((symbol, provider ?? throw new InvalidOperationException($"No supply of the target row provides the runtime import '{symbol}'.")));
                }
            }

            return [.. imports];
        }
    }

    // The kept runtime's definition and text, initialized on first use: the ErasedReserve row's routine (IntrinsicRows,
    // RuntimeAbi.ErasedReserve); then the template expanded with it.
    private static class ErasedWriter
    {
        internal static readonly FunctionAbi[] Definitions = [IntrinsicRows.Of(IntrinsicId.ErasedReserve).Routine!];

        internal static readonly string Text = ExpandRuntime(Template("ErasedWriterRuntime.ll.in"), Definitions, []);
    }

    // The float formatting part, initialized on first use (a module without a float write never reads it): the FormatFloat routine's
    // template with its definition spliced from RuntimeAbi.FormatFloat, then Ryu, which has no marker.
    private static class FormatFloat
    {
        internal static readonly string Text = ExpandRuntime(Template("Utf8FloatRuntime.ll.in") + "\n" + Template("Utf8FloatRyu.ll.in"), [RuntimeAbi.FormatFloat], []);
    }

    // The 128-bit integer formatting part, initialized on first use (a module without an i128 or u128 write never reads it): the
    // FormatInteger128 routine's template with its definition spliced from RuntimeAbi.FormatInteger128.
    private static class FormatInteger128
    {
        internal static readonly string Text = ExpandRuntime(Template("FormatInteger128Runtime.ll.in"), [RuntimeAbi.FormatInteger128], []);
    }
}
