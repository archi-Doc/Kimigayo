// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The explicitly implemented reference representation, independent of Origin identity.</summary>
internal static class ReferenceTypes
{
    internal static bool IsString(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 }
        && ReferenceEquals(type.Components[0], BoundType.String);

    internal static bool IndependentResult(BoundType? type)
    {
        if (ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) || ReferenceEquals(type, BoundType.Never))
        {
            return true;
        }

        if (type is not { Kind: BoundTypeKind.Tuple or BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0 })
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!IndependentResult(type.Components[i]))
            {
                return false;
            }
        }

        return true;
    }
}
