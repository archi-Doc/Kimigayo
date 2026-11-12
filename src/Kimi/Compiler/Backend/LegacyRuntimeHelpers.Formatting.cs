// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The interpolation estimate is this concern's vocabulary.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>The legacy runtime's estimate of an owning interpolation's later writes (SPEC 22.10.5; the old utf8-formatting §5.1 and §6.1;
/// DECISIONS R3-10): their literal bytes plus each value's maximum encoded bytes (<see cref="FormattingTypes.Bound"/>), an unbounded value
/// counted 0, a sum past the maximum object size discarding it. The one rule MIR code generation reads over an interpolation step's
/// operands; the default is the estimate of no bytes and no value.</summary>
/// <param name="Total">The sum, -1 once discarded.</param>
[Transitional("R8", Reason = LegacyRuntimeHelpers.Removal)]
internal readonly record struct FormatEstimate(long Total)
{
    /// <summary>Gets the adapter's hint: the sum, 0 once discarded.</summary>
    internal long Hint => this.Total < 0 ? 0 : this.Total;

    /// <summary>Adds a literal's bytes or a value's maximum (-1 for an unbounded value).</summary>
    /// <param name="amount">The bytes, or -1.</param>
    /// <returns>The estimate with the amount added.</returns>
    internal FormatEstimate Add(long amount)
    {
        var bytes = Math.Max(0, amount);
        return new(this.Total < 0 || this.Total > long.MaxValue - bytes ? -1 : this.Total + bytes);
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

    /// <summary>Writes the formatting runtime: the templates with each marked routine defined once.</summary>
    /// <param name="output">The module text.</param>
    internal static void WriteFormattingRuntime(TextWriter output) => output.Write(Texts.FormattingRuntime);

    // The buffer and formatting runtimes, joined once and expanded by LlvmModuleText's checked one-scan expansion: each routine's
    // definition at its marker. Float formatting is its own part (LlvmModuleText.FormatFloat, R8-S2c).
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

        // No marked routine since R8-W2 c3b (the buffers are the library's); the erased writer's kept runtime defines Text.writer's routine.
        return ExpandRuntime(string.Join('\n', parts), []);
    }

    // Code generation's expanded runtime texts (G350): the CLR initializes this class when a writer (WriteFormattingRuntime) first reads
    // one for a module, never on the check path, whose collector reads the ABIs (Row, IntrinsicRows) but no text; no other member reads
    // them.
    private static class Texts
    {
        // The runtime templates with each marked routine's definition spliced in once.
        internal static readonly string FormattingRuntime = ReadFormattingRuntime();
    }
}
