// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The codegen deviation form of R5 (plan E12, PD10, OQ7; DECISIONS R5-M85, R5-M106): a temporary structure. The stage that makes a target
// the default reduces its Expect or LegacyNative to it: G2 for acceptance-time records (DECISIONS R5-M156), G3 for generated output, so
// the catalog is empty from G3 and CodegenDeviationTest checks the form on synthetic samples (DECISIONS R5-M158); G4 deletes the form with
// the catalog, the .deviation sidecar and compare-fixtures.ps1's deviating count.
// Responsibility: an expectation that MIR code generation meets differently from the old generator by a decided item states its target
// and keeps the legacy value beside it, naming the item's one catalog entry; a native fixture's outcome so (LegacyNative, which
// ScalarEmissionTest's fixture writers take), whose .deviation sidecar both runs write byte-equal (Sidecar).
// Input: the process's code generator, the selector's one reading (Compilation.DefaultCodegen). Output: the value the running code
// generator meets. Invariants: one entry per decided item (CodegenDeviationTest checks the catalog); a deviation's two values differ, by content.
// Must not: branch a test on the pipeline, read KIMI_CODEGEN again or mark a case as expected to fail.

using System.Collections;
using System.Text.Json;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>The value an expectation takes under the process's code generator (R5 PD10).</summary>
internal static class Codegen
{
    /// <summary>Gets a value indicating whether the process generates with the old generator, whose values deviations keep.</summary>
    internal static bool Legacy => Compilation.DefaultCodegen == CompilerCodegen.Legacy;

    /// <summary>Gets the expected value under the process's code generator.</summary>
    /// <typeparam name="T">A type with value equality, or an array, which is compared by its elements.</typeparam>
    /// <param name="target">The value under MIR code generation, which stays once its stage makes it the default.</param>
    /// <param name="legacy">The old generator's value.</param>
    /// <param name="deviation">The catalog entry of the decided item.</param>
    /// <returns><paramref name="legacy"/> under the old generator, else <paramref name="target"/>.</returns>
    internal static T Expect<T>(T target, T legacy, Deviation deviation)
    {
        // One structural comparison for every T: an array's elements (an audit's order), else the value's own equality (DECISIONS R5-M145).
        Assert.False(StructuralComparisons.StructuralEqualityComparer.Equals(target, legacy), $"The deviation {deviation.Id} keeps an equal legacy value.");
        return Legacy ? legacy : target;
    }

    /// <summary>Gets a deviating fixture's <c>.deviation</c> sidecar, the same bytes in every run (DECISIONS R5-M85, R5-M106): UTF-8 without a
    /// byte order mark, LF line ends, its catalog entry and both outcomes, keys in a fixed order.</summary>
    /// <param name="legacy">The old generator's outcome and the entry.</param>
    /// <param name="stdout">The target's standard output.</param>
    /// <param name="exit">The target's exit code.</param>
    /// <param name="stderr">The target's standard error.</param>
    /// <returns>The sidecar's bytes.</returns>
    internal static byte[] Sidecar(LegacyNative legacy, string stdout, int exit, string stderr)
    {
        var deviation = legacy.Deviation;
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteString("id", deviation.Id);
            json.WriteString("class", deviation.Class.ToString().ToLowerInvariant());
            json.WriteString("item", deviation.Item);
            json.WriteString("clause", deviation.Clause);
            json.WriteString("reason", deviation.Reason);
            Outcome(json, "legacy", legacy.Stdout, legacy.Exit, legacy.Stderr);
            Outcome(json, "target", stdout, exit, stderr);
            json.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();

        static void Outcome(Utf8JsonWriter json, string side, string stdout, int exit, string stderr)
        {
            json.WriteStartObject(side);
            json.WriteString("stdout", stdout);
            json.WriteNumber("exit", exit);
            json.WriteString("stderr", stderr);
            json.WriteEndObject();
        }
    }
}

/// <summary>The old generator's native outcome of a fixture that MIR code generation meets differently by a decided item (DECISIONS R5-M85,
/// R5-M106); ScalarEmissionTest's fixture writers take it beside the target's.</summary>
/// <param name="Deviation">The catalog entry of the decided item.</param>
/// <param name="Stdout">The old generator's standard output.</param>
/// <param name="Exit">Its exit code.</param>
/// <param name="Stderr">Its standard error.</param>
internal sealed record LegacyNative(Deviation Deviation, string Stdout, int Exit, string Stderr);

/// <summary>A decided difference of MIR code generation from the old generator (DECISIONS R5-M6, R5-M85).</summary>
/// <param name="Id">The entry's kebab-case name.</param>
/// <param name="Class">Its class (OQ7).</param>
/// <param name="Item">The DECISIONS entry that decides it.</param>
/// <param name="Clause">The SPEC sections, separated by spaces.</param>
/// <param name="Reason">Why the values differ.</param>
internal sealed record Deviation(string Id, DeviationClass Class, string Item, string Clause, string Reason);

/// <summary>The classes of a deviation (OQ7, DECISIONS R5-M6).</summary>
internal enum DeviationClass
{
    /// <summary>(a) The old generator's value is wrong.</summary>
    A,

    /// <summary>(d) An intended change.</summary>
    D,

    /// <summary>(e) A form not ported under D2 (PD27).</summary>
    E,
}

/// <summary>The deviation catalog: one entry per decided item, which every expectation of that item names (M7 counts the entries). Empty
/// from G3, which reduced constant-temporaries (R5-M145), inline-env (R5-M104), receiver-layers (R5-M114) and static-cycle (R5-M106) to
/// their targets (DECISIONS R5-M158).</summary>
internal static class CodegenDeviations
{
}
