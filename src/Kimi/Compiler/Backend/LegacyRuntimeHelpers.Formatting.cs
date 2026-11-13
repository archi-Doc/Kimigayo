// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The formatting row and the interpolation estimate are this concern's vocabulary.

using System.Runtime.InteropServices;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>The row of one formatting compiler function, readable by kind before any representation exists (X4): the ABI of its
/// routine in the formatting runtime, whose fixed symbol every call names, and its declared needs, which are what its text needs:
/// the formatting runtime. No formatting row declares a library callee, since the formatting runtime calls none.</summary>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal sealed record FormattingHelperRow(CompilerFunctionKind Kind, FunctionAbi Abi, HelperNeeds Needs);

/// <summary>The legacy runtime's estimate of an owning interpolation's private buffer (SPEC 22.10.5; the old utf8-formatting §5.1 and §6.1;
/// DECISIONS R3-10): its literal bytes plus each value's maximum encoded bytes (<see cref="FormattingTypes.Bound"/>), an
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
}

/// <summary>SPEC 22.7, 22.9, 22.10: the UTF-8 buffer, writer and formatting runtime (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
{
    /// <summary>The runtime's hint of an internal adapter (<c>__kimi_format_hint</c>, SPEC 22.10.5): the bytes its later writes may still
    /// reserve, which a growth adds.</summary>
    internal static readonly FunctionAbi FormatHint = new("__kimi_format_hint", "void", [new("ptr", "writer"), new("i64", "hint")]);

    /// <summary>The runtime's end of an owning interpolation (<c>__kimi_format_finish</c>, SPEC 22.10.3): the adapter's recorded failure
    /// Aborts with <c>KIMI_E_FORMAT</c>, a pending literal is flushed, and the buffer's bytes become the result string.</summary>
    internal static readonly FunctionAbi FormatFinish = new(
        "__kimi_format_finish",
        "void",
        [new("ptr", "ret"), new("ptr", "buffer"), new("ptr", "writer"), new("ptr", "location"), new("i64", "location_length")]);

    /// <summary>The runtime's check of an internal adapter after a write (<c>__kimi_format_check</c>, SPEC 22.10.3): a recorded failure
    /// Aborts with <c>KIMI_E_FORMAT</c> at the interpolation's location.</summary>
    internal static readonly FunctionAbi FormatCheck =
        new("__kimi_format_check", "void", [new("ptr", "writer"), new("ptr", "location"), new("i64", "location_length")]);

    /// <summary>The runtime's built-in write (<c>__kimi_writer_write_builtin</c>, SPEC 22.10.1): the adapter's frame around the built-in
    /// formatting of a value of a kind (FormatKind), which an owning interpolation's literal text (SegmentText) calls. Temporary (DECISIONS
    /// R8-M18): removed when SegmentText and FormatLiteral leave the runtime (R8-W2).</summary>
    internal static readonly FunctionAbi WriteBuiltin = Slotted(
        "__kimi_writer_write_builtin",
        [
            new("ptr", "ret", AbiParameterKind.ResultSlot), new("ptr", "self", LogicalIndex: 0), new("ptr", "value", LogicalIndex: 1),
            new("i32", "kind", AbiParameterKind.Context), RuntimeAbi.Location, RuntimeAbi.LocationLength,
        ]);

    /// <summary>The runtime's FixedBuffer over bytes (<c>__kimi_text_fixed</c>): the HeapBuffer's construction in FormatHeap's routine (a
    /// direct line's region is Text.fixed's body since R8-W2 a2). Text.fixed is a body; this is a second statement of its construction,
    /// temporary until W2 (FormatBuffer).</summary>
    internal static readonly FunctionAbi FormatFixed = Slotted(
        "__kimi_text_fixed",
        [new("ptr", "ret", AbiParameterKind.ResultSlot), new("ptr", "self", LogicalIndex: 0), new("i64", "capacity", AbiParameterKind.Context)]);

    /// <summary>The runtime's HeapBuffer of a capacity (<c>__kimi_text_heap</c>): an owning interpolation's heap buffer (FormatBuffer's
    /// routine). Text.heap is a body; this is a second statement of its construction and allocation, temporary until W2
    /// (FormatBuffer).</summary>
    internal static readonly FunctionAbi FormatHeap = Slotted(
        "__kimi_text_heap",
        [new("ptr", "ret", AbiParameterKind.ResultSlot), new("i64", "capacity", LogicalIndex: 0), RuntimeAbi.Location, RuntimeAbi.LocationLength]);

    // One row per formatting compiler function, indexed from TextWriter; a new kind adds a row.
    private static readonly FormattingHelperRow[] FormattingRows = CreateFormattingRows();

    /// <summary>Gets the runtime's write of an owning interpolation's literal text (<c>__kimi_format_literal</c>, SPEC 22.10.5): the built-in
    /// write's ABI (WriteBuiltin) under its own name; the text's size is checked at once and its static bytes kept at the adapter until its
    /// next reservation or its end. Initialized after WriteBuiltin.</summary>
    internal static FunctionAbi FormatLiteral { get; } = WriteBuiltin.Named("__kimi_format_literal");

    /// <summary>Gets the row of a formatting compiler function; a formatting kind without a row is a broken contract.</summary>
    /// <param name="kind">The compiler function.</param>
    /// <returns>The kind's row, or null when the kind is outside the formatting family (TextWriter through BuiltinFormat).</returns>
    internal static FormattingHelperRow? FormattingRow(CompilerFunctionKind kind)
    {
        var index = (uint)(kind - CompilerFunctionKind.TextWriter);
        if (index >= (uint)FormattingRows.Length)
        {
            return null;
        }

        return FormattingRows[index] is { } row && row.Kind == kind ? row : throw new InvalidOperationException("A formatting compiler function has no row.");
    }

    /// <summary>Gets the formatting runtime's kind of a built-in formattable Type (SPEC 22.10.2), the number its built-in routines take as
    /// their kind Context and switch on (<c>__kimi_format_builtin</c>), the legacy runtime's own numbering, which R8 removes with it: an
    /// integer's by its width and signedness, then f32, f64, char, bool, Unit and string. The one table code generation reads. The
    /// integers, f32, f64, char, bool, Unit, string and the wrapping integers format through the witness table's bodies (R8-S2a,
    /// R8-S2b1, R8-S2b2, R8-S2c, R8-S2d1), so the routine has no arm but string's 16, which serves only SegmentText's literal text
    /// (until R8-W2). The other numbers stay while
    /// FormattingTypes.IsBuiltin answers them, which FormatKindsAreTheRuntimesNumbering ties to this table (until R8-S2d2). A view
    /// declares its conformance (R8-S2d1), so it has none.</summary>
    /// <param name="type">A closed Type.</param>
    /// <returns>Its kind, or -1 for a Type without built-in formatting (a user or library formatter's).</returns>
    internal static int FormatKind(BoundType type)
    {
        var width = type.Width;
        if (width != 0)
        {
            return width switch { 8 => 0, 16 => 2, 32 => 4, 64 => 6, _ => 8 } + (type.IsSigned ? 1 : 2);
        }

        return type switch
        {
            _ when ReferenceEquals(type, BoundType.F32) => 11,
            _ when ReferenceEquals(type, BoundType.F64) => 12,
            _ when ReferenceEquals(type, BoundType.Char) => 13,
            _ when ReferenceEquals(type, BoundType.Boolean) => 14,
            _ when ReferenceEquals(type, BoundType.Unit) => 15,
            _ when ReferenceEquals(type, BoundType.String) => 16,
            _ => -1,
        };
    }

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
    internal static void WriteFormattingRuntime(TextWriter output) => output.Write(Texts.FormattingRuntime);

    // Each row at its kind's index; every row needs the formatting runtime. Rows sharing a symbol share its routine, which the
    // first of them in kind order defines.
    private static FormattingHelperRow[] CreateFormattingRows()
    {
        const HelperNeeds Runtime = HelperNeeds.FormattingRuntime;
        var rows = new FormattingHelperRow[CompilerFunctionKind.BuiltinFormat - CompilerFunctionKind.TextWriter + 1];
        var ret = new AbiParameter("ptr", "ret", AbiParameterKind.ResultSlot);
        var reference = new AbiParameter("ptr", "self", LogicalIndex: 0);
        var value = new AbiParameter("ptr", "value", LogicalIndex: 0);
        var builtin = new AbiParameter("i32", "kind", AbiParameterKind.Context);
        var location = RuntimeAbi.Location;
        var length = RuntimeAbi.LocationLength;
        Add(
            CompilerFunctionKind.TextWriter,
            Slotted("__kimi_text_writer", [ret, reference, new("i64", "kind", AbiParameterKind.Context), new("ptr", "dispatch", AbiParameterKind.Context)]),
            Runtime);
        Add(
            CompilerFunctionKind.BuiltinFormat,
            Slotted("__kimi_format_builtin", [ret, value, new("ptr", "self", LogicalIndex: 1), builtin, location, length]),
            Runtime);
        return rows;

        void Add(CompilerFunctionKind kind, FunctionAbi abi, HelperNeeds needs) => rows[kind - CompilerFunctionKind.TextWriter] = new(kind, abi, needs);
    }

    // The buffer and formatting runtimes, joined once and expanded by LlvmModuleText's checked one-scan expansion: each routine's
    // definition at its marker, from the first row of its symbol. Float formatting is its own part (LlvmModuleText.FormatFloat, R8-S2c).
    private static string ReadFormattingRuntime()
    {
        var names = new[] { "Utf8BufferRuntime", "Utf8FormatRuntime" };
        var parts = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            using var resource = typeof(LegacyRuntimeHelpers).Assembly.GetManifestResourceStream("Kimi.Compiler.Backend." + names[i] + ".ll.in")!;
            using var reader = new StreamReader(resource);
            parts[i] = reader.ReadToEnd();
        }

        // A routine the erased writer's kept runtime defines is not this text's; the row-less built-in write, fixed and heap buffers are.
        var symbols = new HashSet<string>(StringComparer.Ordinal) { WriteBuiltin.Name, FormatFixed.Name, FormatHeap.Name };
        foreach (var kept in LlvmModuleText.ErasedWriterDefinitions)
        {
            symbols.Add(kept.Name);
        }

        var definitions = new List<FunctionAbi>(FormattingRows.Length + 3) { WriteBuiltin, FormatFixed, FormatHeap };
        foreach (var row in FormattingRows)
        {
            if (symbols.Add(row.Abi.Name))
            {
                definitions.Add(row.Abi);
            }
        }

        return ExpandRuntime(string.Join('\n', parts), CollectionsMarshal.AsSpan(definitions));
    }

    // A signature that writes the caller's result slot.
    private static FunctionAbi Slotted(string name, AbiParameter[] parameters)
        => new(name, RuntimeAbi.Unit.ComputationType, parameters) { ResultSlot = true };

    // Code generation's expanded runtime texts (G350): the CLR initializes this class when a writer (WriteFormattingRuntime) first reads
    // one for a module, never on the check path, whose collector reads the rows and ABIs (Row, IntrinsicRows) but no text; no other
    // member reads them.
    private static class Texts
    {
        // The runtime templates with each row's routine definition spliced in once.
        internal static readonly string FormattingRuntime = ReadFormattingRuntime();
    }
}
