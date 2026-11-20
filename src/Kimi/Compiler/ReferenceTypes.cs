// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The explicitly implemented reference representation, independent of Origin identity.</summary>
internal static class ReferenceTypes
{
    // SPEC 5.1: a raw pointer is a Copy address value; null is its only literal.
    internal static bool IsPointer(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components.Count: 1 };

    // Physical agreement of a call's formal and actual Types: Kind, Symbol, Semantics, lengths and
    // components. Origins are erased: Binding and ownership analysis verified them, and lowering never
    // distinguishes two storages by Origin (SPEC 21.3.1).
    internal static bool StorageMatches(BoundType? formal, BoundType? actual)
    {
        if (formal is null || actual is null)
        {
            return false;
        }

        if (ReferenceEquals(formal, actual))
        {
            return true;
        }

        // Primitive Types are singletons distinguished by name only, so two different instances never share storage.
        if (formal.Kind == BoundTypeKind.Primitive || formal.Kind != actual.Kind || formal.Symbol != actual.Symbol || formal.Semantics != actual.Semantics ||
            formal.Length != actual.Length || !ReferenceEquals(formal.LengthExpression, actual.LengthExpression) ||
            !formal.LengthArguments.AsSpan().SequenceEqual(actual.LengthArguments) || formal.Components.Count != actual.Components.Count)
        {
            return false;
        }

        for (var i = 0; i < formal.Components.Count; i++)
        {
            if (!StorageMatches(formal.Components[i], actual.Components[i]))
            {
                return false;
            }
        }

        return true;
    }
}
