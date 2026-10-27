// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the test runtime of a test build (SPEC 17.6, 20.6; IMPL 21.4.2), one text both code generators write (R5 PD2): its routines (Text:
// the selected case, the frames to the runner, a verification's observation, message and Abort, the temporary directory, an observed
// scalar's frame, and the compared values held until the observation that numbers their failure sends them, DECISIONS R5-M136), the entry
// that runs the selected case (WriteEntry) and the kind an observed scalar's frame gives its Type (ValueKind).
// Input: the catalog Discover filled; a writer of each case's call, so a code generator calls its case functions its own way.
// Output: LLVM text; a frame kind.
// Invariants: the cases run in the catalog's order; the routines' signatures are RuntimeAbi's; warm writes allocate nothing (numbers on
// the stack, LlvmModuleText.WriteNumber).
// Must not: decide which tests run (the catalog's), or write a case's call itself (WriteEntry's caller does).

using System.Globalization;
using Kimi.Compiler;
using Kimi.Compiler.Backend;

namespace Kimi.Testing;

internal static class TestRuntime
{
    /// <summary>Gets the routines of the test runtime, the part of a test build's runtime its catalog does not change.</summary>
    internal static string Text { get; } = Read();

    /// <summary>Gets the kind an observed scalar's frame gives its Type, as the runner reads it: 1 bool, 2 a signed integer, 3 an
    /// unsigned integer or a char, 4 a floating-point number.</summary>
    /// <param name="type">The scalar's Type.</param>
    /// <returns>The kind.</returns>
    internal static int ValueKind(BoundType type) => ReferenceEquals(type, BoundType.Boolean) ? 1 : type.IsFloatingPoint ? 4 : ScalarTypes.Signed(type) ? 2 : 3;

    /// <summary>Writes the entry of a test build (IMPL 21.4.2): the artifact's and the cases' identities and <c>__kimi_start</c>, which runs
    /// the case the runner selects between its begin and completion frames, each case's call written by <paramref name="call"/>.</summary>
    /// <param name="output">The module's writer.</param>
    /// <param name="catalog">The test build's catalog.</param>
    /// <param name="call">The writer of the call of a case, by its index in the catalog.</param>
    internal static void WriteEntry(TextWriter output, TestCatalog catalog, Action<TextWriter, int> call)
    {
        output.Write("\n@__kimi_test_artifact = private constant [67 x i8] c\"");
        output.Write(catalog.ArtifactId);
        output.Write("\"\n");
        var count = catalog.Cases.Count;
        for (var i = 0; i < count; i++)
        {
            output.Write("@__kimi_test_case_");
            LlvmModuleText.WriteNumber(output, i);
            output.Write(" = private constant [67 x i8] c\"");
            output.Write(catalog.Cases[i].CaseId);
            output.Write("\"\n");
        }

        output.Write("define void @__kimi_start() noreturn #0 {\nentry:\n  %case = call i64 @__kimi_test_begin()\n  switch i64 %case, label %invalid [\n");
        for (var i = 0; i < count; i++)
        {
            output.Write("    i64 ");
            LlvmModuleText.WriteNumber(output, i);
            output.Write(", label %case_");
            LlvmModuleText.WriteNumber(output, i);
            output.Write('\n');
        }

        output.Write("  ]\n");
        for (var i = 0; i < count; i++)
        {
            output.Write("case_");
            LlvmModuleText.WriteNumber(output, i);
            output.Write(":\n  call void @__kimi_test_frame(i16 1, i64 0, i32 -1, ptr @__kimi_test_case_");
            LlvmModuleText.WriteNumber(output, i);
            output.Write(", i32 67)\n");
            call(output, i);
            output.Write("  call void @__kimi_test_frame(i16 6, i64 0, i32 -1, ptr null, i32 0)\n  call void @ExitProcess(i32 0)\n  unreachable\n");
        }

        output.Write("invalid:\n  call void @ExitProcess(i32 125)\n  unreachable\n}\n");
    }

    /// <summary>Gets a test build's runtime for the old generator: the routines, then the entry calling each case's function by name.</summary>
    /// <param name="catalog">The test build's catalog.</param>
    /// <param name="names">The LLVM name of each case's function, in catalog case order.</param>
    /// <returns>The text.</returns>
    [Transitional("R6-gen", Reason = "G7 deletes the old generator, its one caller.")]
    internal static string Create(TestCatalog catalog, IReadOnlyList<string> names)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        text.Write(Text);
        WriteEntry(text, catalog, (output, i) =>
        {
            output.Write("  call void @");
            output.Write(names[i]);
            output.Write("()\n");
        });

        return text.ToString();
    }

    private static string Read()
    {
        using var stream = typeof(TestRuntime).Assembly.GetManifestResourceStream("Kimi.Testing.TestRuntime.ll")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
