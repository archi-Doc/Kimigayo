// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal enum ObjectCountingStep : byte
{
    None,
    NonAtomic,
    Atomic,
}

// SPEC 3.2, IMPL 21.2.3: payload authority and reference counting are independent properties of the static mode.
internal readonly record struct ObjectHandleMode(SemanticsKind Semantics)
{
    internal LoanRequirement PayloadAuthority => this.Semantics switch
    {
        SemanticsKind.Obj => LoanRequirement.Uniq,
        SemanticsKind.Rc or SemanticsKind.Arc => LoanRequirement.Ref,
        _ => throw new InvalidOperationException("An object handle must have obj, rc or arc Semantics."),
    };

    internal ObjectCountingStep Counting => this.Semantics switch
    {
        SemanticsKind.Obj => ObjectCountingStep.None,
        SemanticsKind.Rc => ObjectCountingStep.NonAtomic,
        SemanticsKind.Arc => ObjectCountingStep.Atomic,
        _ => throw new InvalidOperationException("An object handle must have obj, rc or arc Semantics."),
    };
}

/// <summary>Object handle modes and borrowed views, independently of implementation support.</summary>
internal static class ObjectTypes
{
    internal static ObjectHandleMode? HandleMode(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc, Components.Count: 1 }
            ? new(type.Semantics) : null;

    internal static bool IsBorrow(BoundType? type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.ObjRef or SemanticsKind.ObjUniq, Components.Count: 1 };

    internal static BoundType? ViewTarget(BoundType? type)
    {
        while (ReferenceTypes.IsStorage(type))
        {
            type = type!.Components[0];
        }

        return HandleMode(type) is not null || IsBorrow(type) ? type!.Components[0] : null;
    }

    internal static bool Supports(BoundType source, BoundType target)
    {
        for (var current = source; current is not null; current = AdtDef.Base(current))
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }
}
