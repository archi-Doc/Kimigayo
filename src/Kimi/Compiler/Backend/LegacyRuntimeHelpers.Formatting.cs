// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The formatting row is this concern's request vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>The row of one formatting compiler function, readable by kind before any representation exists (X4): the ABI of its
/// routine in the formatting runtime, whose fixed symbol every call names, and its declared needs. No formatting row declares a
/// library callee, since the formatting runtime calls none.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record FormattingHelperRow(CompilerFunctionKind Kind, FunctionAbi Abi, HelperNeeds Needs);

/// <summary>SPEC 22.7, 22.9, 22.10: the UTF-8 buffer, writer, formatting and floating-point runtime and the wrappers over user
/// formatting witnesses (Windows x64 profile).</summary>
internal sealed partial class LegacyRuntimeHelpers
{
    // One row per formatting compiler function, indexed from TextFixed; a new kind adds a row.
    private static readonly FormattingHelperRow[] FormattingRows = CreateFormattingRows();

    // The runtime templates with each row's routine definition spliced in once; read after the rows.
    private static readonly string FormattingRuntime = ReadFormattingRuntime();

    // The writer adapter's write wrapper of each user formatting witness, and the conversions over them, in request order.
    private readonly List<(FunctionAbi Wrapper, FunctionAbi Implementation)> formattingWrites = new();

    private readonly List<(FunctionAbi Wrapper, FunctionAbi Write, bool Fixed)> formattingConversions = new();

    /// <summary>Gets or sets a value indicating whether the module holds the formatting runtime: a formatting row's request
    /// needs it, and the old lowering requests it for its direct calls of formatting routines and for interpolation.</summary>
    internal bool NeedsFormattingRuntime { get; set; }

    /// <summary>Gets the row of a formatting compiler function; a formatting kind without a row is a broken contract.</summary>
    /// <param name="kind">The compiler function.</param>
    /// <returns>The kind's row, or null when the kind is outside the formatting family (TextFixed through BuiltinFormat).</returns>
    internal static FormattingHelperRow? FormattingRow(CompilerFunctionKind kind)
    {
        var index = (uint)(kind - CompilerFunctionKind.TextFixed);
        return index >= (uint)FormattingRows.Length ? null
            : FormattingRows[index] is { } row && row.Kind == kind ? row
            : throw new InvalidOperationException("A formatting compiler function has no row.");
    }

    /// <summary>Registers the writer adapter's write wrapper over a user formatting witness (SPEC 22.10.1): each is written once
    /// per module, in request order, and the needs of WriterWrite's row join the module's.</summary>
    /// <param name="wrapper">The wrapper's ABI.</param>
    /// <param name="implementation">The witness's compiled format.</param>
    internal void RequestFormattingWrite(FunctionAbi wrapper, FunctionAbi implementation)
    {
        this.JoinNeeds(FormattingRow(CompilerFunctionKind.WriterWrite)!.Needs);
        if (!this.formattingWrites.Contains((wrapper, implementation)))
        {
            this.formattingWrites.Add((wrapper, implementation));
        }
    }

    /// <summary>Registers a <c>Text.toString</c> or <c>Text.tryFormat</c> conversion over a write wrapper (SPEC 22.9.2): each is
    /// written once per module, in request order, and the needs of its kind's row join the module's.</summary>
    /// <param name="kind">TextToString, which formats into a heap buffer, or TextTryFormat, into the caller's array.</param>
    /// <param name="wrapper">The conversion's ABI.</param>
    /// <param name="write">The write wrapper it calls.</param>
    internal void RequestFormattingConversion(CompilerFunctionKind kind, FunctionAbi wrapper, FunctionAbi write)
    {
        if (kind is not (CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat))
        {
            throw new InvalidOperationException("A formatting conversion serves Text.toString or Text.tryFormat.");
        }

        this.JoinNeeds(FormattingRow(kind)!.Needs);
        var conversion = (wrapper, write, kind == CompilerFunctionKind.TextTryFormat);
        if (!this.formattingConversions.Contains(conversion))
        {
            this.formattingConversions.Add(conversion);
        }
    }

    /// <summary>Writes the formatting runtime when the module needs it, then each requested write wrapper and conversion.</summary>
    /// <param name="output">The module text.</param>
    internal void WriteFormatting(TextWriter output)
    {
        if (this.NeedsFormattingRuntime)
        {
            output.Write(FormattingRuntime);
        }

        foreach (var (wrapper, implementation) in this.formattingWrites)
        {
            WriteFormattingWrite(output, wrapper, implementation);
        }

        foreach (var (wrapper, write, fixedBuffer) in this.formattingConversions)
        {
            WriteFormattingConversion(output, wrapper, write, fixedBuffer);
        }
    }

    // Each row at its kind's index: every row needs the formatting runtime; WriterWrite, TextToString and TextTryFormat also run
    // the formatted Type's Utf8Format witness, through a wrapper for a user witness. TextWriter's witness is the ErasedWriter
    // intrinsic's and BuiltinFormat's leaves are its shim's, so neither row declares one. Rows sharing a symbol share its routine,
    // which the first of them in kind order defines.
    private static FormattingHelperRow[] CreateFormattingRows()
    {
        const HelperNeeds Runtime = HelperNeeds.FormattingRuntime;
        const HelperNeeds Witness = HelperNeeds.FormattingRuntime | HelperNeeds.FormattingWitness;
        var rows = new FormattingHelperRow[CompilerFunctionKind.BuiltinFormat - CompilerFunctionKind.TextFixed + 1];
        var ret = new AbiParameter("ptr", "ret", AbiParameterKind.ResultSlot);
        var reference = new AbiParameter("ptr", "self", LogicalIndex: 0);
        var owned = new AbiParameter("ptr", "self", AbiParameterKind.OwnedSlot, 0);
        var value = new AbiParameter("ptr", "value", LogicalIndex: 0);
        var builtin = new AbiParameter("i32", "kind", AbiParameterKind.Context);
        var location = RuntimeAbi.Location;
        var length = RuntimeAbi.LocationLength;
        var amount = new AbiParameter("i64", "amount", LogicalIndex: 1);
        var capacity = new AbiParameter("i64", "capacity", AbiParameterKind.Context);
        Add(CompilerFunctionKind.TextFixed, Slotted("__kimi_text_fixed", [ret, reference, capacity]), Runtime);
        Add(CompilerFunctionKind.TextHeap, Slotted("__kimi_text_heap", [ret, new("i64", "capacity", LogicalIndex: 0), location, length]), Runtime);
        Add(
            CompilerFunctionKind.TextWriter,
            Slotted("__kimi_text_writer", [ret, reference, new("i64", "kind", AbiParameterKind.Context), new("ptr", "dispatch", AbiParameterKind.Context)]),
            Runtime);
        Add(CompilerFunctionKind.WriterStatus, Slotted("__kimi_writer_status", [ret, reference]), Runtime);
        Add(
            CompilerFunctionKind.WriterWrite,
            Slotted("__kimi_writer_write_builtin", [ret, reference, new("ptr", "value", LogicalIndex: 1), builtin, location, length]),
            Witness);
        Add(
            CompilerFunctionKind.BuiltinFormat,
            Slotted("__kimi_format_builtin", [ret, value, new("ptr", "self", LogicalIndex: 1), builtin, location, length]),
            Runtime);
        Add(CompilerFunctionKind.TextToString, Slotted("__kimi_text_to_string", [ret, value, builtin, location, length]), Witness);
        Add(
            CompilerFunctionKind.TextTryFormat,
            Slotted("__kimi_text_try_format", [ret, value, new("ptr", "destination", LogicalIndex: 1), capacity, builtin, location, length]),
            Witness);
        Add(CompilerFunctionKind.TextRelease, new("__kimi_text_release", "void", [reference, location, length]), Runtime);
        Add(CompilerFunctionKind.TextUtf8, Slotted("__kimi_text_utf8", [ret, new("ptr", "self", AbiParameterKind.SharedReference, 0)]), Runtime);
        Add(CompilerFunctionKind.TextValidateUtf8, Slotted("__kimi_text_validate_utf8", [ret, owned]), Runtime);
        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferBytes, CompilerFunctionKind.HeapBufferBytes })
        {
            Add(kind, Slotted("__kimi_buffer_bytes", [ret, reference]), Runtime);
        }

        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferText, CompilerFunctionKind.HeapBufferText })
        {
            Add(kind, Slotted("__kimi_buffer_text", [ret, reference]), Runtime);
        }

        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferValidate, CompilerFunctionKind.HeapBufferValidate })
        {
            Add(kind, Slotted("__kimi_buffer_validate", [ret, reference]), Runtime);
        }

        foreach (var kind in new[] { CompilerFunctionKind.FixedBufferClear, CompilerFunctionKind.HeapBufferClear })
        {
            Add(kind, new("__kimi_buffer_clear", "void", [reference]), Runtime);
        }

        Add(CompilerFunctionKind.FixedBufferReserve, Slotted("__kimi_fixed_reserve", [ret, reference, amount, location, length]), Runtime);
        Add(CompilerFunctionKind.HeapBufferReserve, Slotted("__kimi_heap_reserve", [ret, reference, amount, location, length]), Runtime);
        Add(CompilerFunctionKind.FixedBufferIntoText, Slotted("__kimi_buffer_text", [ret, owned]), Runtime);
        Add(CompilerFunctionKind.HeapBufferIntoString, Slotted("__kimi_buffer_into_string", [ret, owned, location, length]), Runtime);
        Add(CompilerFunctionKind.WindowPush, Slotted("__kimi_window_push", [ret, reference, new("i8", "byte", LogicalIndex: 1)]), Runtime);
        Add(CompilerFunctionKind.WindowAppend, Slotted("__kimi_window_append", [ret, reference, new("ptr", "bytes", AbiParameterKind.OwnedSlot, 1)]), Runtime);
        Add(CompilerFunctionKind.WindowLimit, Slotted("__kimi_window_limit", [ret, owned, amount, location, length]), Runtime);
        Add(CompilerFunctionKind.WindowCommit, new("__kimi_window_commit", "i64", [owned]), Runtime);
        Add(CompilerFunctionKind.WriteLineUtf8, new("__kimi_write_line_utf8", "void", [owned, location, length]), Runtime);
        return rows;

        void Add(CompilerFunctionKind kind, FunctionAbi abi, HelperNeeds needs) => rows[kind - CompilerFunctionKind.TextFixed] = new(kind, abi, needs);
    }

    // The buffer and formatting runtimes, then the floating-point formatting and Ryu, each row's routine definition spliced in at
    // its marker (the first row of a shared symbol defines it) and the abort reasons filled in.
    private static string ReadFormattingRuntime()
    {
        string? text = null;
        foreach (var name in new[] { "Utf8BufferRuntime", "Utf8FormatRuntime", "Utf8FloatRuntime", "Utf8FloatRyu" })
        {
            using var resource = typeof(LegacyRuntimeHelpers).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission." + name + ".ll.in")!;
            using var reader = new StreamReader(resource);
            var part = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
            text = text is null ? part : text + "\n" + part;
        }

        foreach (var row in FormattingRows)
        {
            text = text!.Replace("{{" + row.Abi.Name + "}}\n", row.Abi.GetDefinition(exported: false), StringComparison.Ordinal);
        }

        return text!.Replace("{{reason_argument_range}}", Number(AbortReasons.ArgumentRange), StringComparison.Ordinal)
            .Replace("{{reason_format}}", Number(AbortReasons.Format), StringComparison.Ordinal)
            .Replace("{{reason_size}}", Number(AbortReasons.AllocationSize), StringComparison.Ordinal);
    }

    // SPEC 22.10.1: a write that the adapter's recorded failure blocks, and whose returned or nested failure it records.
    private static void WriteFormattingWrite(TextWriter output, FunctionAbi wrapper, FunctionAbi implementation)
    {
        output.Write(wrapper.GetDefinition(exported: false));
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

    // SPEC 22.9.2: toString formats through a heap buffer and Aborts on BufferFull; tryFormat formats into the caller's array and
    // returns its bytes or BufferFull.
    private static void WriteFormattingConversion(TextWriter output, FunctionAbi wrapper, FunctionAbi write, bool fixedBuffer)
    {
        output.Write(wrapper.GetDefinition(exported: false));
        output.Write("entry:\n  %buffer = alloca [32 x i8], align 8\n  %writer = alloca [64 x i8], align 8\n  %result = alloca i32, align 4\n");
        output.Write(fixedBuffer
            ? "  call void @__kimi_text_fixed(ptr %buffer, ptr %destination, i64 %capacity)\n"
            : "  call void @__kimi_text_heap(ptr %buffer, i64 0, ptr %location, i64 %location_length)\n");
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
        output.Write(fixedBuffer
            ? "  store i32 0, ptr %ret, align 4\n  %payload = getelementptr i8, ptr %ret, i64 8\n  call void @__kimi_buffer_bytes(ptr %payload, ptr %buffer)\n"
            : "  call void @__kimi_format_take_string(ptr %ret, ptr %buffer)\n");
        output.Write("  ret void\nfailure:\n");
        if (fixedBuffer)
        {
            output.Write("  store i32 1, ptr %ret, align 4\n  ret void\n}\n");
        }
        else
        {
            output.Write("  call void @__kimi_abort(i32 ");
            WriteNumber(output, AbortReasons.Format);
            output.Write(", ptr %location, i64 %location_length, i64 -2)\n  unreachable\n}\n");
        }
    }
}
