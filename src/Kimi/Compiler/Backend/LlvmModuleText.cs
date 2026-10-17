// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the module frame of the generated LLVM text (SPEC 21.3, 21.4, 22.6; DECISIONS R5-M14): the header with the pooled
// constants, the Windows runtime template with its ABI definitions spliced in and its test variant, the external declarations,
// the overflow and memory intrinsic declarations, the footer's profile attributes, the spelling of external symbols and numbers
// formatted on the stack (or once, as text, for a template filled at startup). Both generators write the frame through it (PD2:
// promoted, never copied).
// Input: a writer, the module's LlvmConstantPool, the test runtime text of a test build, ExternalFunction records, numbers and
// symbols; the runtime ABI records (RuntimeAbi), the abort reasons (AbortReasons) and the target profile (WindowsProfile).
// Output: LLVM text written to the writer; a symbol's spelling after @ and the symbol recovered from that spelling.
// Invariants: the legacy writer's .ll stays byte-identical while it shares this text; warm writes allocate nothing; each runtime ABI
// definition marker is spliced exactly once; the text holds no checkout path.
// Must not take syntax, HIR, Binding internals, EmissionModule or MIR types in its signatures, branch on the generator, or use LINQ.

#pragma warning disable SA1402 // The external declaration record is this writer's input.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Backend;

/// <summary>A foreign function declaration; DllImport selects the import-library form (SPEC 20.4).</summary>
internal readonly record struct ExternalFunction(FunctionAbi Abi, bool DllImport);

/// <summary>Writes the syntax-free module frame of the LLVM text that every generator shares.</summary>
internal static class LlvmModuleText
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

    // The LLVM spelling after @ of an external symbol: the plain identifier, or a quoted
    // name whose UTF-8 bytes other than printable ASCII, " and \ are written as \XX.
    internal static string ExternalName(string symbol)
    {
        var plain = symbol.Length != 0 && !char.IsAsciiDigit(symbol[0]);
        foreach (var c in symbol)
        {
            plain &= char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '$' or '-';
        }

        if (plain)
        {
            return symbol;
        }

        var text = new StringBuilder(symbol.Length + 8).Append('"');
        foreach (var b in Encoding.UTF8.GetBytes(symbol))
        {
            if (b is >= 0x20 and < 0x7F and not (byte)'"' and not (byte)'\\')
            {
                text.Append((char)b);
            }
            else
            {
                text.Append('\\').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return text.Append('"').ToString();
    }

    // Recovers the external symbol from its ExternalName spelling.
    internal static string SymbolFromName(ReadOnlySpan<char> name)
    {
        if (name.Length < 2 || name[0] != '"' || name[^1] != '"')
        {
            return name.ToString();
        }

        name = name[1..^1];
        var bytes = new byte[name.Length];
        var count = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\' && i + 2 < name.Length &&
                byte.TryParse(name.Slice(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var escaped))
            {
                bytes[count++] = escaped;
                i += 2;
            }
            else
            {
                bytes[count++] = (byte)name[i];
            }
        }

        return Encoding.UTF8.GetString(bytes, 0, count);
    }

    // A number's text for a runtime template filled once at startup; writing formats through WriteNumber instead.
    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    // TextWriter.Write(long) formats through a temporary string; format on the stack instead.
    internal static void WriteNumber(TextWriter output, long value)
    {
        Span<char> digits = stackalloc char[20];
        value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        output.Write(digits[..length]);
    }

    internal static void WriteNumber(TextWriter output, Int128 value)
    {
        if (value >= long.MinValue && value <= long.MaxValue)
        {
            WriteNumber(output, (long)value);
            return;
        }

        Span<char> digits = stackalloc char[40];
        value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        output.Write(digits[..length]);
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
        var runtime = AbortReasons.Expand(reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal));
        foreach (var abi in RuntimeAbi.RuntimeDefinitions)
        {
            // Compiler-facing runtime signatures come from the same FunctionAbi records as calls; each is defined once.
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

        return runtime + "\n";
    }

    private static string ReportTestAbort(string runtime, FunctionAbi abort)
    {
        var entry = abort.GetDefinition(exported: false) + "entry:\n";
        return runtime.Replace(entry, entry + "  call void @__kimi_test_aborted()\n", StringComparison.Ordinal);
    }
}
