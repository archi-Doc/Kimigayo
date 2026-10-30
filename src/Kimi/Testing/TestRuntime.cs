// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the test runtime of a test build (SPEC 17.6, 20.6; IMPL 21.4.2), one text MIR code generation writes (R5 PD2): its routines (Text:
// the selected case, the frames to the runner, a verification's observation, message and Abort, the temporary directory, an observed
// scalar's frame, and the compared values held until the observation that numbers their failure sends them, DECISIONS R5-M136), the entry
// that runs the selected case (WriteEntry), the kind an observed scalar's frame gives its Type (ValueKind), and the phase every frame
// reports (Phase, the runner's numbering) with its save, set and restore around the code a phase covers (WritePhase, RestorePhase).
// Input: the catalog Discover filled; a writer of each case's call, so a code generator calls its case functions its own way; a phase's
// saved value's name.
// Output: LLVM text; a frame kind.
// Invariants: the cases run in the catalog's order; the routines' signatures are RuntimeAbi's; a phase is saved and set together, and its
// saved value restored; warm writes allocate nothing (numbers on the stack, LlvmModuleText.WriteNumber).
// Must not: decide which tests run (the catalog's), write a case's call itself (WriteEntry's caller does), or decide which code a phase
// covers (each code generator's).

using Kimi.Compiler;
using Kimi.Compiler.Backend;

namespace Kimi.Testing;

internal static class TestRuntime
{
    /// <summary>The phase a frame reports, as the runner reads it: the runtime's current phase, which starts as <see cref="Body"/>.</summary>
    internal enum Phase
    {
        Body = 1,
        Cleanup, // Scope Exit and destruction (SPEC 16).
        Initialization, // A static's initializer (SPEC 22.4.3).
        Shutdown, // Static shutdown (SPEC 22.4.3), never in a test case before P42.
    }

    /// <summary>Gets the routines of the test runtime, the part of a test build's runtime its catalog does not change.</summary>
    internal static string Text { get; } = Read();

    /// <summary>Gets the kind an observed scalar's frame gives its Type, as the runner reads it: 1 bool, 2 a signed integer, 3 an
    /// unsigned integer or a char, 4 a floating-point number.</summary>
    /// <param name="type">The scalar's Type.</param>
    /// <returns>The kind.</returns>
    internal static int ValueKind(BoundType type) => type switch
    {
        _ when ReferenceEquals(type, BoundType.Boolean) => 1,
        { IsFloatingPoint: true } => 4,
        _ when ScalarTypes.Signed(type) => 2,
        _ => 3,
    };

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

    /// <summary>Writes the save of the runtime's current phase into the value <paramref name="prefix"/><paramref name="id"/>, then the set of
    /// <paramref name="phase"/>; <see cref="RestorePhase"/> restores the saved value.</summary>
    /// <param name="output">The function's writer.</param>
    /// <param name="prefix">The saved value's name before its number.</param>
    /// <param name="id">The saved value's number.</param>
    /// <param name="phase">The phase set.</param>
    internal static void WritePhase(TextWriter output, string prefix, int id, Phase phase)
    {
        output.Write("  ");
        LlvmModuleText.Name(output, prefix, id);
        output.Write(" = load i32, ptr @__kimi_test_phase\n  store i32 ");
        LlvmModuleText.WriteNumber(output, (int)phase);
        output.Write(", ptr @__kimi_test_phase\n");
    }

    /// <summary>Writes the restore of the phase <see cref="WritePhase"/> saved into the value <paramref name="prefix"/><paramref name="id"/>.</summary>
    /// <param name="output">The function's writer.</param>
    /// <param name="prefix">The saved value's name before its number.</param>
    /// <param name="id">The saved value's number.</param>
    internal static void RestorePhase(TextWriter output, string prefix, int id)
    {
        output.Write("  store i32 ");
        LlvmModuleText.Name(output, prefix, id);
        output.Write(", ptr @__kimi_test_phase\n");
    }

    private static string Read()
    {
        using var stream = typeof(TestRuntime).Assembly.GetManifestResourceStream("Kimi.Testing.TestRuntime.ll")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
