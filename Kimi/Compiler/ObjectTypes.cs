// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The implemented unique object-handle representation.</summary>
internal static class ObjectTypes
{
    internal static bool IsOwner(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Obj, Components.Count: 1 };

    internal static bool IsBorrow(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.ObjRef or SemanticsKind.ObjUniq, Components.Count: 1 };

    internal static bool Supports(BoundType source, BoundType target)
    {
        for (var current = source; current is not null; current = current.StoredBase)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }
}
