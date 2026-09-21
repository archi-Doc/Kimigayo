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

    internal static bool IsStorage(BoundType? type) => IsStruct(type) || IsArray(type) || IsTuple(type) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && type.Components[0].Kind is BoundTypeKind.Closure or BoundTypeKind.Parameter) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components.Count: 1 } && ScalarTypes.Supports(type.Components[0])) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 } &&
            (type.Components[0].Kind == BoundTypeKind.Parameter ||
                (ScalarTypes.Supports(type.Components[0]) && type.Origin is { Kind: OriginKind.Input or OriginKind.Projection or OriginKind.Parameter or OriginKind.Intersection })));

    internal static bool IsBorrow(BoundType? type) => IsStorage(type) || ObjectTypes.IsBorrow(type);

    // SPEC 5.1: a raw pointer is a Copy address value; null is its only literal.
    internal static bool IsPointer(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components.Count: 1 };

    internal static bool IsValue(BoundType? type) => ScalarTypes.Supports(type) || IsBorrow(type) || IsPointer(type);

    internal static bool CallTypeMatches(BoundType? formal, BoundType? actual, BoundCall call)
    {
        if (formal is null || actual is null || formal.Kind != actual.Kind || formal.Symbol != actual.Symbol || formal.Semantics != actual.Semantics ||
            formal.Length != actual.Length || !ReferenceEquals(formal.LengthExpression, actual.LengthExpression) ||
            formal.Components.Count != actual.Components.Count || formal.OriginArguments.Count != actual.OriginArguments.Count ||
            !OriginMatches(formal.Origin, actual.Origin))
        {
            return false;
        }

        for (var i = 0; i < formal.OriginArguments.Count; i++)
        {
            if (!OriginMatches(formal.OriginArguments[i], actual.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < formal.Components.Count; i++)
        {
            if (!CallTypeMatches(formal.Components[i], actual.Components[i], call))
            {
                return false;
            }
        }

        return true;

        bool OriginMatches(BoundOrigin? pattern, BoundOrigin? value)
        {
            if (ReferenceEquals(pattern, value))
            {
                return true;
            }

            if (pattern is null)
            {
                return false;
            }

            return ReferenceEquals(call.Target.Declaration.CodeContext.Compilation.Binding.InstantiateStorageOrigin(pattern, call), value);
        }
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
