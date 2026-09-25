// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class ElementAccess
{
    // Eligibility only; Lowering must also verify the owner's storage role and initialization.
    internal static bool SupportsBorrowRoot(OwnershipPlace place)
        => place.Kind is OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter or OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result &&
            place.Type.Semantics == SemanticsKind.Owner && (place.Type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Dictionary || StructStorage.IsStruct(place.Type));

    internal static bool SupportsMoveRoot(OwnershipPlace place)
        => place.Kind is OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter && SupportsBorrowRoot(place);

    internal static bool IsSyntax(Koto source) => source is IndexKoto or MemberAccessKoto { Right: NumberLiteralKoto } ||
        (source is MemberAccessKoto { BoundSymbol.Property.IsStored: true, Left.BoundType: { } type } && StructStorage.IsStruct(type));

    // SPEC 15.1.3: literal-only recognition; never use folded values or named constants.
    internal static int StaticSelector(BinaryKoto source)
    {
        if (source is MemberAccessKoto && TryType(source, out _, out var position))
        {
            return position;
        }

        return source is IndexKoto && source.Left.BoundType is { Kind: BoundTypeKind.FixedArray, Length: >= 0 } array &&
            KotoHelper.UnwrapParentheses(source.Right) is NumberLiteralKoto { IsInteger: true } number &&
            number.TryGetIntegerMagnitude(out var magnitude) && magnitude < (ulong)array.Length && magnitude <= int.MaxValue
            ? (int)magnitude : -1;
    }

    internal static KotoKind UpdateOperator(KotoKind kind) => kind switch
    {
        KotoKind.PrefixPlusPlus or KotoKind.PostfixIncrement => KotoKind.Plus,
        KotoKind.PrefixMinusMinus or KotoKind.PostfixDecrement => KotoKind.Minus,
        _ => KotoHelper.CompoundOperation(kind),
    };

    internal static BoundType? DestinationType(Koto source, BoundType? type)
        => ReferenceEquals(type, BoundType.Never) && KotoHelper.UnwrapParentheses(source) is BinaryKoto element &&
            IsSyntax(element) && TryType(element, out var destination, out _) ? destination : type;

    // A transferred operand (x@move) or an Identity acquisition is the operand Place's own value:
    // its temporary keeps the Place's syntax, so both unwrap like a label.
    internal static Koto ValueSource(Koto source)
    {
        while (true)
        {
            source = KotoHelper.UnwrapParentheses(source);
            switch (source)
            {
                case LabeledKoto labeled:
                    source = labeled.Target;
                    break;
                case ConversionKoto { ConversionBinding: ConversionBinding.Transfer or ConversionBinding.Identity } conversion:
                    source = conversion.Left;
                    break;
                default:
                    return source.CodeContext.Compilation.Binding.PropertyCall(source, PropertyAccessorKind.Get) ?? source;
            }
        }
    }

    internal static IdentifierNameKoto? WritableRoot(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (!IsSyntax(source))
        {
            return null;
        }

        while (source is BinaryKoto element && IsSyntax(element))
        {
            if (!TryType(element, out _, out _))
            {
                return null;
            }

            source = KotoHelper.UnwrapParentheses(element.Left);
        }

        // Pattern body bindings have their own local identity; a var pattern
        // permits writes to its acquired value just like a var declaration.
        // Guard candidates remain excluded by their distinct symbol kind.
        if (source is not IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local } symbol } root)
        {
            return null;
        }

        return Binding.IsMutableDeclaration(symbol.Declaration) ? root : null;
    }

    // SPEC 5.2, 12: an inline stored field/Tuple/fixed-array path rooted at *p or p[n].
    // Binding mutability does not decide its write permission.
    internal static bool IsPointerPath(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64 && source is BinaryKoto element && IsSyntax(element) && TryType(element, out _, out _); depth++)
        {
            source = KotoHelper.UnwrapParentheses(element.Left);
            if ((source is DereferenceKoto dereference && ReferenceTypes.IsPointer(dereference.Operand.BoundType)) ||
                (source is IndexKoto index && ReferenceTypes.IsPointer(index.Left.BoundType)))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6: a direct field/Tuple path whose base is a borrowed struct or
    // Tuple reference; nested levels must be inline stored parts. Returns the
    // reference-typed base, or null for other forms.
    internal static Koto? BorrowedPathRoot(MemberAccessKoto field)
    {
        for (var depth = 0; depth < 64; depth++)
        {
            if (Binding.IsGetterResult(field))
            {
                return null;
            }

            if (ReferenceTypes.IsStruct(field.Left.BoundType) || ReferenceTypes.IsTuple(field.Left.BoundType) || ObjectTypes.IsBorrow(field.Left.BoundType))
            {
                return field.Left;
            }

            if (field.Left is not MemberAccessKoto parent || !TryType(field, out _, out _))
            {
                return null;
            }

            field = parent;
        }

        return null;
    }

    // SPEC 15.6: a direct inline field/Tuple/static-array path whose root is an owned local
    // or parameter Name. Returns that Name, or null for other forms.
    internal static IdentifierNameKoto? OwnedPathRoot(BinaryKoto field)
    {
        for (var depth = 0; depth < 64; depth++)
        {
            if (Binding.IsGetterResult(field) || !IsSyntax(field) || StaticSelector(field) < 0)
            {
                return null;
            }

            var receiver = KotoHelper.UnwrapParentheses(field.Left);
            if (receiver is IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter } root)
            {
                return root;
            }

            if (receiver is not BinaryKoto parent)
            {
                return null;
            }

            field = parent;
        }

        return null;
    }

    // The stored position of one path level and the aggregate that contains it.
    internal static int PathSelector(BinaryKoto field, out BoundType? owner, out BoundType? element)
    {
        var left = field.Left.BoundType;
        element = null;
        if (ReferenceTypes.IsTuple(left))
        {
            owner = left!.Components[0];
            return TryBorrowedTupleElement(field, out element, out var index) ? index : -1;
        }

        if (ReferenceTypes.IsStruct(left) || ObjectTypes.IsBorrow(left))
        {
            owner = left!.Components[0];
            for (var i = 0; i < StructStorage.Count(owner); i++)
            {
                if (ReferenceEquals(StructStorage.Field(owner, i).BoundSymbol, field.BoundSymbol))
                {
                    element = StructStorage.FieldType(owner, i);
                    return i;
                }
            }

            return -1;
        }

        owner = left;
        return TryType(field, out element, out _) ? StaticSelector(field) : -1;
    }

    internal static bool TryBorrowedTupleElement(BinaryKoto source, out BoundType? element, out int position)
    {
        element = null;
        position = -1;
        if (ReferenceTypes.IsTuple(source.Left.BoundType) && source is MemberAccessKoto { Right: NumberLiteralKoto number } &&
            number.IsInteger && number.TryGetIntegerMagnitude(out var magnitude) && magnitude < (ulong)source.Left.BoundType!.Components[0].Components.Count)
        {
            position = (int)magnitude;
            element = source.Left.BoundType.Components[0].Components[position];
            return true;
        }

        return false;
    }

    internal static bool TryType(BinaryKoto source, out BoundType? element, out int position)
    {
        element = null;
        position = -1;
        var type = source.Left.BoundType;
        if (ReferenceEquals(type, BoundType.Never) && KotoHelper.UnwrapParentheses(source.Left) is BinaryKoto parent && IsSyntax(parent))
        {
            TryType(parent, out type, out _); // Preserve a nested destination's Type after an abrupt earlier index.
        }

        if (type?.Semantics != SemanticsKind.Owner)
        {
            return false;
        }

        if (source is MemberAccessKoto { BoundSymbol.Property.IsStored: true } && StructStorage.IsStruct(type))
        {
            for (var i = 0; i < StructStorage.Count(type); i++)
            {
                var field = StructStorage.Field(type, i);
                if (ReferenceEquals(field.BoundSymbol, source.BoundSymbol))
                {
                    position = i;
                    element = StructStorage.FieldType(type, i);
                    return element is not null;
                }
            }
        }

        if (source is IndexKoto && type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array && type.Components.Count == 1)
        {
            element = type.Components[0];
            return true;
        }

        if (source is IndexKoto && type.Kind == BoundTypeKind.Dictionary && type.Components.Count == 2)
        {
            element = type.Components[1];
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
