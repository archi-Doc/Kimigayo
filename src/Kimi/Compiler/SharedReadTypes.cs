// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>SPEC 4.7.1, 10.2: the Semantics of a shared borrow of a stored value.</summary>
internal static class SharedReadTypes
{
    internal static SemanticsKind? BorrowSemantics(BoundType stored) => stored.Semantics switch
    {
        SemanticsKind.Owner or SemanticsKind.Uniq => SemanticsKind.Ref,
        SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjUniq => SemanticsKind.ObjRef,
        _ => null,
    };
}
