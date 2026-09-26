// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>SPEC 4.6.9, 10.2: the Semantics of a shared borrow of a stored value, and whether it reads a stored pointer.</summary>
internal static class SharedReadTypes
{
    internal static SemanticsKind? BorrowSemantics(BoundType stored) => stored.Semantics switch
    {
        SemanticsKind.Owner or SemanticsKind.Uniq => SemanticsKind.Ref,
        SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjUniq => SemanticsKind.ObjRef,
        _ => null,
    };

    // Owner storage borrows take its address. Handles and exclusive references
    // instead load the stored pointer, retaining a shared Loan to the source.
    internal static bool ReadsStoredPointer(BoundType stored, BoundType? result)
        => stored.Kind == BoundTypeKind.Semantics && stored.Semantics != SemanticsKind.Owner &&
            BorrowSemantics(stored) is { } semantics && result is { Kind: BoundTypeKind.Semantics, Origin: not null, Components.Count: 1 } &&
            result.Semantics == semantics && stored.Components.Count == 1 && ReferenceEquals(stored.Components[0], result.Components[0]);
}
