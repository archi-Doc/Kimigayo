// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The codegen deviation form of R5 (plan E12, PD10, OQ7; DECISIONS R5-M85): a temporary structure. The stage that makes a target the
// default reduces its Expect to it (G2 for acceptance-time records, G3 for generated output); G4 removes the form with the catalog.
// Responsibility: an expectation that MIR code generation (sel) meets differently from the old generator by a decided item states its
// target and keeps the legacy value beside it, naming the item's one catalog entry.
// Input: the process's code generator, the selector's one reading (Compilation.DefaultCodegen). Output: the value the running code
// generator meets. Invariants: one entry per decided item (CodegenDeviationTest checks the catalog); a deviation's two values differ.
// Must not: branch a test on the pipeline, read KIMI_CODEGEN again or mark a case as expected to fail. The native half (the fixture
// writers' legacy outcome, the .deviation sidecar and compare-fixtures.ps1's acceptance) lands with its first native consumer.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>The value an expectation takes under the process's code generator (R5 PD10).</summary>
internal static class Codegen
{
    /// <summary>Gets a value indicating whether the process generates with the old generator, whose values deviations keep.</summary>
    internal static bool Legacy => Compilation.DefaultCodegen == CompilerCodegen.Legacy;

    /// <summary>Gets the expected value under the process's code generator.</summary>
    /// <typeparam name="T">A type with value equality.</typeparam>
    /// <param name="target">The value under MIR code generation, which stays once its stage makes it the default.</param>
    /// <param name="legacy">The old generator's value.</param>
    /// <param name="deviation">The catalog entry of the decided item.</param>
    /// <returns><paramref name="legacy"/> under the old generator, else <paramref name="target"/>.</returns>
    internal static T Expect<T>(T target, T legacy, Deviation deviation)
    {
        Assert.False(EqualityComparer<T>.Default.Equals(target, legacy), $"The deviation {deviation.Id} keeps an equal legacy value.");
        return Legacy ? legacy : target;
    }
}

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

/// <summary>The deviation catalog: one entry per decided item, which every expectation of that item names (M7 counts the entries).</summary>
internal static class CodegenDeviations
{
    /// <summary>K13a: an instance's operation the backend does not generate (R5-M54) rejects at check under MIR code generation.</summary>
    internal static readonly Deviation InstanceCapabilityAtCheck = new(
        "instance-capability-at-check",
        DeviationClass.D,
        "R5-M41",
        "23.2.2",
        "Under MIR code generation check reports the checked set's records; the old generator accepts the program and refuses it at generation.");
}
