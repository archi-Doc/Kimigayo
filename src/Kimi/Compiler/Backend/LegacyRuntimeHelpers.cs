// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the legacy runtime helpers of code generation until Kimigayo library code replaces them at R8 (plan PD2, P1a-P2b; DECISIONS
// R5-M16): Text.writer's destination kind, the one helper left. It writes no runtime text since R8-W2 c4 and maps no compiler function
// to a runtime routine since R8-W2 d (writeLine's row was the last: every compiler function is an intrinsic or a shim).
// Input: a Text.writer destination. Output: its writer kind, the kind Context of Text.writer's routine and the number
// Utf8Writer.reserve's kind switch reads (WriterKind; SPEC 22.9.2, 22.9.4, Windows x64 profile).
// Invariants: reads allocate nothing.
// Must not hold module state, take syntax or MIR types, or a BoundType but a writer's destination for its kind, or use LINQ.

namespace Kimi.Compiler.Backend;

/// <summary>The legacy runtime helpers: Text.writer's destination kind.</summary>
[Transitional("R8", Reason = "R8 replaces the legacy runtime helpers with Kimigayo library code.")]
internal static class LegacyRuntimeHelpers
{
    /// <summary>Gets the writer kind of a <c>Text.writer</c> destination Type (SPEC 22.9.2, 22.9.4), the number <c>__kimi_text_writer</c>
    /// takes as its kind Context and <c>Utf8Writer.reserve</c>'s kind switch reads, the legacy numbering, which R8 removes with it: a
    /// FixedBuffer 0 and a HeapBuffer 1, whose buffers the reservation's kind arms reserve themselves, and any other destination 2, whose
    /// <c>reserve</c> witness the reservation calls through the adapter's dispatch word. The one table code generation reads.</summary>
    /// <param name="destination">The closed destination Type.</param>
    /// <returns>Its writer kind.</returns>
    internal static int WriterKind(BoundType destination) => destination.Symbol?.LibraryDeclaration switch
    {
        KimiDeclarationId.FixedBuffer => 0,
        KimiDeclarationId.HeapBuffer => 1,
        _ => 2,
    };
}
