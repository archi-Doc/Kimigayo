// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

/// <summary>SPEC 22.9.2, 22.9.4: Text.writer's destination kind (Windows x64 profile).</summary>
internal static partial class LegacyRuntimeHelpers
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
