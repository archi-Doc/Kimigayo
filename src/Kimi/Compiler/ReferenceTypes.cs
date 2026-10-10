// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The explicitly implemented reference representation, independent of Origin identity.</summary>
internal static class ReferenceTypes
{
    internal static bool IsStruct(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && AdtDef.IsStruct(type.Components[0]);

    internal static bool IsArray(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.FixedArray;

    internal static bool IsTuple(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.Tuple;

    internal static bool IsEnum(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && AdtDef.IsEnum(type.Components[0]);

    // SPEC 4.5: a borrowed Array is a reference to its handle storage.
    internal static bool IsDynamicArray(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.Array;

    internal static bool IsDictionary(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.Dictionary;

    // SPEC 4.6.5: a borrowed Slice is a reference to its Copy handle storage; the elements keep their own source Loan.
    internal static bool IsSlice(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && type.Components[0].Kind == BoundTypeKind.Slice;

    internal static bool IsStorage(BoundType? type) => IsStruct(type) || IsArray(type) || IsTuple(type) || IsEnum(type) || IsDynamicArray(type) || IsDictionary(type) || IsSlice(type) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && ReferenceEquals(type.Components[0], BoundType.String)) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            type.Components[0] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq }) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && ReferenceEquals(type.Components[0], BoundType.Unit)) ||
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && (ObjectTypes.HandleMode(type.Components[0]) is not null || ObjectTypes.IsBorrow(type.Components[0]))) ||
        // SPEC 8.4.3, 8.1.1: an associated projection, a pair target or a Semantics application stands for a complete Type like a
        // parameter; each instance checks its substitution.
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && type.Components[0].Kind is BoundTypeKind.Closure or BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) ||
        // Binding verifies Origin obligations; inferred initializer aliases use the same stored reference representation.
        (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && (ScalarTypes.Supports(type.Components[0]) || IsPointer(type.Components[0])));

    internal static bool IsBorrow(BoundType? type) => IsStorage(type) || ObjectTypes.IsBorrow(type);

    // SPEC 4.6.3: the library ResolvedRange struct, which the compiler produces for indices and iterates directly.
    internal static bool IsResolvedRange(BoundType? type) => type is { Kind: BoundTypeKind.Nominal, Symbol.LibraryDeclaration: KimiDeclarationId.ResolvedRange };

    // SPEC 13.4: safe borrows of one scalar Type compare their referent values.
    internal static bool IsScalarBorrow(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
        && ScalarTypes.Supports(type.Components[0]);

    // SPEC 3.4: a safe ref/T or uniq/T, whatever its referent and representation as an argument.
    internal static bool IsReference(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 };

    // SPEC 5.1: a raw pointer is a Copy address value; null is its only literal.
    internal static bool IsPointer(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components.Count: 1 };

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

        // Primitive Types are singletons distinguished by name only, so two different instances never share storage.
        if (formal.Kind == BoundTypeKind.Primitive || formal.Kind != actual.Kind || formal.ResultMode != actual.ResultMode || formal.Symbol != actual.Symbol || formal.Semantics != actual.Semantics ||
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

    internal static bool IsString(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 }
        && ReferenceEquals(type.Components[0], BoundType.String);

    // SPEC 13.4: a string comparison operand is a string or safe reference layers ending in one.
    internal static bool EndsInString(BoundType? type)
    {
        for (var depth = 0; depth < 64 && type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }; depth++)
        {
            type = type.Components[0];
        }

        return ReferenceEquals(type, BoundType.String);
    }

    // A string inspected in place through one safe reference, shared or exclusive.
    internal static bool IsStringReference(BoundType? type) => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
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
