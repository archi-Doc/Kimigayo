// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

/// <summary>SPEC 22.7, 22.9, 22.10: the UTF-8 buffer, writer and formatting runtime (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
{
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
