// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The formatting row and the interpolation estimate are this concern's vocabulary.

using System.Runtime.InteropServices;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>The row of one formatting compiler function, readable by kind before any representation exists (X4): the ABI of its
/// routine in the formatting runtime, whose fixed symbol every call names, and its declared needs, which are what its text needs:
/// the formatting runtime. No formatting row declares a library callee, since the formatting runtime calls none; the formatting
/// witness WriterWrite, TextToString and TextTryFormat run is the need their published library rows state (SPEC 4.9.6).</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record FormattingHelperRow(CompilerFunctionKind Kind, FunctionAbi Abi, HelperNeeds Needs);

/// <summary>The legacy runtime's estimate of an owning interpolation's private buffer (SPEC 22.10.5; the old utf8-formatting §5.1 and §6.1;
/// DECISIONS R3-10): its literal bytes plus each value's maximum encoded bytes (<see cref="LegacyRuntimeHelpers.FormatBound"/>), an
/// unbounded value counted 0 and making the estimate unbounded, a sum past the maximum object size discarding it. The one rule MIR code
/// generation reads over an interpolation step's operands.</summary>
/// <param name="Total">The sum, -1 once discarded.</param>
/// <param name="Bounded">Whether every value added is bounded.</param>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct FormatEstimate(long Total, bool Bounded)
{
    /// <summary>Gets the estimate of no bytes and no value.</summary>
    internal static FormatEstimate Empty => new(0, Bounded: true);

    /// <summary>Gets the capacity of the private heap buffer: the sum when bounded, else 0 (a buffer that starts unallocated).</summary>
    internal long Capacity => this.Unbounded ? 0 : this.Total;

    /// <summary>Gets the adapter's hint: the sum, 0 once discarded.</summary>
    internal long Hint => this.Total < 0 ? 0 : this.Total;

    /// <summary>Gets a value indicating whether the estimate bounds no buffer: a value is unbounded or the sum was discarded.</summary>
    internal bool Unbounded => !this.Bounded || this.Total < 0;

    /// <summary>Adds a literal's bytes or a value's maximum (-1 for an unbounded value).</summary>
    /// <param name="amount">The bytes, or -1.</param>
    /// <returns>The estimate with the amount added.</returns>
    internal FormatEstimate Add(long amount)
    {
        var bytes = Math.Max(0, amount);
        return new(this.Total < 0 || this.Total > long.MaxValue - bytes ? -1 : this.Total + bytes, this.Bounded && amount >= 0);
    }

    /// <summary>Gets whether the buffer is placed on the stack: an interpolation passed directly to <c>Console.writeLine</c>, bounded and
    /// within <see cref="LegacyRuntimeHelpers.FormatStackLimit"/>.</summary>
    /// <param name="direct">Whether the interpolation is the one argument of <c>Console.writeLine</c>.</param>
    /// <returns>Whether it is formatted on the stack.</returns>
    internal bool Stack(bool direct) => direct && this.Bounded && this.Total is >= 0 and <= LegacyRuntimeHelpers.FormatStackLimit;
}

/// <summary>SPEC 22.7, 22.9, 22.10: the UTF-8 buffer, writer, formatting and floating-point runtime and the wrappers over user
/// formatting witnesses (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
{
    /// <summary>The windows-x64 limit of an owning interpolation's stack buffer, its literal bytes and value maxima included: a direct,
    /// bounded interpolation within it is formatted on the stack, after substitution as before (the old utf8-formatting §6.1; STATUS); any
    /// other uses the heap buffer.</summary>
    internal const int FormatStackLimit = 1024;

    /// <summary>The runtime's hint of an internal adapter (<c>__kimi_format_hint</c>, SPEC 22.10.5): the bytes its later writes may still
    /// reserve, which a growth adds.</summary>
    internal static readonly FunctionAbi FormatHint = new("__kimi_format_hint", "void", [new("ptr", "writer"), new("i64", "hint")]);

    /// <summary>The runtime's end of an owning interpolation (<c>__kimi_format_finish</c>, SPEC 22.10.3): the adapter's recorded failure
    /// Aborts with <c>KIMI_E_FORMAT</c>, a pending literal is flushed, and the buffer's bytes become the result string.</summary>
    internal static readonly FunctionAbi FormatFinish = new(
        "__kimi_format_finish",
        "void",
        [new("ptr", "ret"), new("ptr", "buffer"), new("ptr", "writer"), new("ptr", "location"), new("i64", "location_length")]);

    /// <summary>The end of an owning interpolation on the stack (<c>__kimi_format_stack_finish</c>): <see cref="FormatFinish"/>'s, the string
    /// marked static, since its bytes are the frame's.</summary>
    internal static readonly FunctionAbi FormatStackFinish = new("__kimi_format_stack_finish", "void", FormatFinish.Parameters);

    /// <summary>The runtime's test of an adapter (SPEC 22.10.1), true while it has recorded no failure, which a user formatter's write
    /// wrapper calls before and after the witness (MIR code generation; DECISIONS R5-M130).</summary>
    internal static readonly FunctionAbi FormatHealthy = new("__kimi_format_healthy", "i1", [new("ptr", "writer", LogicalIndex: 0)]);

    /// <summary>The runtime's check of an internal adapter after a write (<c>__kimi_format_check</c>, SPEC 22.10.3): a recorded failure
    /// Aborts with <c>KIMI_E_FORMAT</c> at the interpolation's location.</summary>
    internal static readonly FunctionAbi FormatCheck =
        new("__kimi_format_check", "void", [new("ptr", "writer"), new("ptr", "location"), new("i64", "location_length")]);

    /// <summary>The runtime's record of an adapter's failure into a write's result (SPEC 22.10.1): BufferFull, recorded once.</summary>
    internal static readonly FunctionAbi WriterFail =
        Slotted("__kimi_writer_fail", [new("ptr", "ret", AbiParameterKind.ResultSlot), new("ptr", "self", LogicalIndex: 0)]);

    // One row per formatting compiler function, indexed from TextFixed; a new kind adds a row.
    private static readonly FormattingHelperRow[] FormattingRows = CreateFormattingRows();

    // The runtime templates with each row's routine definition spliced in once; read after the rows.
    private static readonly string FormattingRuntime = ReadFormattingRuntime();

    // The maximum encoded bytes of a value by its formatting kind (FormatKind's numbering), the runtime's own __kimi_format_bounds, which
    // __kimi_format_capacity reads (FunctionCodegenTest's FormatBoundsAreTheRuntimesTable ties them): kind 0 is no value, and a string or a
    // view (16, 17) is unbounded.
    private static readonly int[] FormatBounds = [0, 4, 3, 6, 5, 11, 10, 20, 20, 40, 39, 17, 24, 4, 5, 2];

    /// <summary>Gets the runtime's write of an owning interpolation's literal text (<c>__kimi_format_literal</c>, SPEC 22.10.5): WriterWrite's
    /// row ABI under its own name; the text's size is checked at once and its static bytes kept at the adapter until its next reservation
    /// or its end. Initialized after the rows it reads.</summary>
    internal static FunctionAbi FormatLiteral { get; } = FormattingRow(CompilerFunctionKind.WriterWrite)!.Abi.Named("__kimi_format_literal");

    /// <summary>Gets the row of a formatting compiler function; a formatting kind without a row is a broken contract.</summary>
    /// <param name="kind">The compiler function.</param>
    /// <returns>The kind's row, or null when the kind is outside the formatting family (TextFixed through BuiltinFormat).</returns>
    internal static FormattingHelperRow? FormattingRow(CompilerFunctionKind kind)
    {
        var index = (uint)(kind - CompilerFunctionKind.TextFixed);
        if (index >= (uint)FormattingRows.Length)
        {
            return null;
        }

        return FormattingRows[index] is { } row && row.Kind == kind ? row : throw new InvalidOperationException("A formatting compiler function has no row.");
    }

    /// <summary>Gets the formatting runtime's kind of a built-in formattable Type (SPEC 22.10.2), the number its built-in routines take as
    /// their kind Context and switch on (<c>__kimi_format_builtin</c>), the legacy runtime's own numbering, which R8 removes with it: an
    /// integer's by its width and signedness, then f32, f64, char, bool, Unit, string and Utf8Slice. The one table code generation reads.</summary>
    /// <param name="type">A closed Type.</param>
    /// <returns>Its kind, or -1 for a Type without built-in formatting (a user or library formatter's).</returns>
    internal static int FormatKind(BoundType type)
    {
        var width = ScalarTypes.Width(type);
        if (width != 0)
        {
            return width switch { 8 => 0, 16 => 2, 32 => 4, 64 => 6, _ => 8 } + (ScalarTypes.Signed(type) ? 1 : 2);
        }

        return type switch
        {
            _ when ReferenceEquals(type, BoundType.F32) => 11,
            _ when ReferenceEquals(type, BoundType.F64) => 12,
            _ when ReferenceEquals(type, BoundType.Char) => 13,
            _ when ReferenceEquals(type, BoundType.Boolean) => 14,
            _ when ReferenceEquals(type, BoundType.Unit) => 15,
            _ when ReferenceEquals(type, BoundType.String) => 16,
            _ when type.Symbol?.LibraryDeclaration == KimiDeclarationId.Utf8Slice => 17,
            _ => -1,
        };
    }

    /// <summary>Gets the maximum encoded bytes of a value of a Type that SPEC 22.10.5 bounds (the old utf8-formatting §5.1): a primitive
    /// integer's by its width and signedness, a wrapping integer's as its argument's, f32 17, f64 24, char 4, bool 5 and Unit 2.</summary>
    /// <param name="type">A closed Type, or null.</param>
    /// <returns>Its bound, or -1 for an unbounded Type: a string, a view, a user or library formatter's.</returns>
    internal static int FormatBound(BoundType? type)
        => type is not null && FormatKind(type) is var kind and > 0 && kind < FormatBounds.Length ? FormatBounds[kind] : -1;

    /// <summary>Gets the formatting runtime's writer kind of a <c>Text.writer</c> destination Type (SPEC 22.9.2, 22.9.4), the number
    /// <c>__kimi_text_writer</c> takes as its kind Context and <c>__kimi_writer_reserve</c> switches on, the legacy runtime's own numbering,
    /// which R8 removes with it: a FixedBuffer 0 and a HeapBuffer 1, whose buffers the runtime reserves itself, and any other destination 2,
    /// whose <c>reserve</c> witness the runtime calls through the adapter's dispatch word. The one table code generation reads.</summary>
    /// <param name="destination">The closed destination Type.</param>
    /// <returns>Its writer kind.</returns>
    internal static int WriterKind(BoundType destination) => destination.Symbol?.LibraryDeclaration switch
    {
        KimiDeclarationId.FixedBuffer => 0,
        KimiDeclarationId.HeapBuffer => 1,
        _ => 2,
    };

    /// <summary>Writes the formatting runtime: the templates with each row's routine defined once.</summary>
    /// <param name="output">The module text.</param>
    internal static void WriteFormattingRuntime(TextWriter output) => output.Write(FormattingRuntime);

    /// <summary>Writes a <c>Text.toString</c> or <c>Text.tryFormat</c> conversion over a user formatter's write wrapper (SPEC 22.9.2):
    /// toString formats through a heap buffer and Aborts on BufferFull at its call's location; tryFormat formats into the caller's array and
    /// returns its bytes or BufferFull: MIR code generation's wrappers (the old planner's text).</summary>
    /// <param name="output">The module text.</param>
    /// <param name="wrapper">The conversion's ABI, its row's routine less the kind Context: the result slot, the value, tryFormat's
    /// destination and capacity, and the location.</param>
    /// <param name="write">The write wrapper it calls: the result slot, the adapter and the value.</param>
    /// <param name="fixedBuffer">Whether it is tryFormat's, into the caller's array.</param>
    internal static void WriteFormattingConversion(TextWriter output, FunctionAbi wrapper, FunctionAbi write, bool fixedBuffer)
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

    // Each row at its kind's index; every row needs the formatting runtime. Rows sharing a symbol share its routine, which the
    // first of them in kind order defines.
    private static FormattingHelperRow[] CreateFormattingRows()
    {
        const HelperNeeds Runtime = HelperNeeds.FormattingRuntime;
        var rows = new FormattingHelperRow[CompilerFunctionKind.BuiltinFormat - CompilerFunctionKind.TextFixed + 1];
        var ret = new AbiParameter("ptr", "ret", AbiParameterKind.ResultSlot);
        var reference = new AbiParameter("ptr", "self", LogicalIndex: 0);
        var owned = new AbiParameter("ptr", "self", AbiParameterKind.OwnedSlot, 0);
        var value = new AbiParameter("ptr", "value", LogicalIndex: 0);
        var builtin = new AbiParameter("i32", "kind", AbiParameterKind.Context);
        var location = RuntimeAbi.Location;
        var length = RuntimeAbi.LocationLength;
        var amount = new AbiParameter("i64", "amount", LogicalIndex: 1);
        // A capacity Context is the length of the fixed array its logical index's pointer parameter receives (SPEC 22.9.2's N).
        var capacity = new AbiParameter("i64", "capacity", AbiParameterKind.Context, 0);
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
            Runtime);
        Add(
            CompilerFunctionKind.BuiltinFormat,
            Slotted("__kimi_format_builtin", [ret, value, new("ptr", "self", LogicalIndex: 1), builtin, location, length]),
            Runtime);
        Add(CompilerFunctionKind.TextToString, Slotted("__kimi_text_to_string", [ret, value, builtin, location, length]), Runtime);
        Add(
            CompilerFunctionKind.TextTryFormat,
            Slotted(
                "__kimi_text_try_format",
                [ret, value, new("ptr", "destination", LogicalIndex: 1), capacity with { LogicalIndex = 1 }, builtin, location, length]),
            Runtime);
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

    // The buffer and formatting runtimes, then the floating-point formatting and Ryu, expanded by LlvmModuleText's checked splice:
    // each routine's definition at its marker, from the first row of its symbol.
    private static string ReadFormattingRuntime()
    {
        string? text = null;
        foreach (var name in new[] { "Utf8BufferRuntime", "Utf8FormatRuntime", "Utf8FloatRuntime", "Utf8FloatRyu" })
        {
            using var resource = typeof(LegacyRuntimeHelpers).Assembly.GetManifestResourceStream("Kimi.Compiler.Emission." + name + ".ll.in")!;
            using var reader = new StreamReader(resource);
            var part = reader.ReadToEnd();
            text = text is null ? part : text + "\n" + part;
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new List<FunctionAbi>(FormattingRows.Length);
        foreach (var row in FormattingRows)
        {
            if (symbols.Add(row.Abi.Name))
            {
                definitions.Add(row.Abi);
            }
        }

        return ExpandRuntime(text!, CollectionsMarshal.AsSpan(definitions));
    }
}
