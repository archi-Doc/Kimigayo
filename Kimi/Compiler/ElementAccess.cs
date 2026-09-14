// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class ElementAccess
{
    internal static bool IsSyntax(Koto source) => source is IndexKoto or MemberAccessKoto { Right: NumberLiteralKoto };

    internal static bool TryType(BinaryKoto source, out BoundType? element, out int position)
    {
        element = null;
        position = -1;
        var type = source.Left.BoundType;
        if (type?.Semantics != SemanticsKind.Owner)
        {
            return false;
        }

        if (source is IndexKoto && type.Kind == BoundTypeKind.FixedArray && type.Components.Count == 1)
        {
            element = type.Components[0];
            return true;
        }

        if (source is MemberAccessKoto { Right: NumberLiteralKoto number } && type.Kind == BoundTypeKind.Tuple &&
            number.IsInteger && number.TryGetIntegerMagnitude(out var magnitude) && magnitude < (ulong)type.Components.Count)
        {
            position = (int)magnitude;
            element = type.Components[position];
            return true;
        }

        return false;
    }
}
