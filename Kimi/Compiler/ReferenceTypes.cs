// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The explicitly implemented reference representation, independent of Origin identity.</summary>
internal static class ReferenceTypes
{
    internal static bool IsStruct(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && StructStorage.IsStruct(type.Components[0]);

    internal static bool IsArray(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.FixedArray;

    internal static bool IsTuple(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.Tuple;

    internal static bool IsEnum(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && EnumStorage.IsEnum(type.Components[0]);

    // SPEC 4.5: a borrowed Array is a reference to its handle storage.
    internal static bool IsDynamicArray(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.Array;

    internal static bool IsDictionary(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.Dictionary;

    internal static bool IsStorage(BoundType? type) => IsStruct(type) || IsArray(type) || IsTuple(type) || IsEnum(type) || IsDynamicArray(type) || IsDictionary(type) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && ReferenceEquals(type.Components[0], BoundType.String)) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            type.Components[0] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq }) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && ReferenceEquals(type.Components[0], BoundType.Unit)) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && (ObjectTypes.IsOwner(type.Components[0]) || ObjectTypes.IsBorrow(type.Components[0]))) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && type.Components[0].Kind is BoundTypeKind.Closure or BoundTypeKind.Parameter) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components.Count: 1 } && ScalarTypes.Supports(type.Components[0])) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 } &&
            (type.Components[0].Kind == BoundTypeKind.Parameter ||
                (ScalarTypes.Supports(type.Components[0]) && type.Origin is { Kind: OriginKind.Input or OriginKind.Projection or OriginKind.Parameter or OriginKind.Intersection })));

    internal static bool IsBorrow(BoundType? type) => IsStorage(type) || ObjectTypes.IsBorrow(type);

    // SPEC 13.4: safe borrows of one scalar Type compare their referent values.
    internal static bool IsScalarBorrow(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && ScalarTypes.Supports(type.Components[0]);

    // SPEC 5.1: a raw pointer is a Copy address value; null is its only literal.
    internal static bool IsPointer(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components.Count: 1 };

    internal static bool IsValue(BoundType? type) => ScalarTypes.Supports(type) || IsBorrow(type) || IsPointer(type);

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

        if (formal.Kind != actual.Kind || formal.Symbol != actual.Symbol || formal.Semantics != actual.Semantics ||
            formal.Length != actual.Length || !ReferenceEquals(formal.LengthExpression, actual.LengthExpression) ||
            formal.Components.Count != actual.Components.Count)
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
