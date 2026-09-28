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

    // SPEC 4.6.6, 4.6.9: an element of a Slice or of a referenced array, including a fixed array reached through a
    // borrowed receiver; no owned root holds it.
    internal static bool IsSharedElement(Koto source) => source is IndexKoto { Right: not RangeKoto } element &&
        (element.Left.BoundType?.Kind == BoundTypeKind.Slice || ReferenceTypes.IsArray(AccessType(element.Left)) || ReferenceTypes.IsDynamicArray(element.Left.BoundType));

    // SPEC 7.1.1: a call of a function that publishes a Place. The call expression designates the referent of the
    // reference the callee returns, with that reference's capabilities and Origin; its Type is the stored Type.
    internal static bool IsPlaceCall(Koto source)
        => KotoHelper.UnwrapParentheses(source) is InvocationKoto { BoundCall.Target.Declaration: FunctionKoto { ReturnType: PlaceResultKoto } };

    // The reference a Place call returns physically (ref/T or uniq/T with the published Origin), or null.
    internal static BoundType? PlaceCallReference(Koto source)
        => KotoHelper.UnwrapParentheses(source) is InvocationKoto { BoundCall: { } plan } && plan.Target.Declaration is FunctionKoto { ReturnType: PlaceResultKoto } &&
            plan.ReturnType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } reference ? reference : null;

    // SPEC 4.6.9: a receiver whose selected Place is reached by a dynamic key or through a reference: a Slice, a dynamic
    // Array, a Dictionary or a borrow.
    internal static bool IsBorrowedReceiver(BoundType? receiver)
        => receiver is { Kind: BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary } || ReferenceTypes.IsBorrow(receiver);

    // SPEC 4.6.9: whether a selection reaches its Place through a borrowed receiver on its path rather than through static
    // selectors of an owner.
    internal static bool IsBorrowedSelection(Koto source) => HasReceiverOnPath(source, owners: true);

    // SPEC 4.6.6, 4.6.9: whether a selection reaches its Place through a Slice or a borrow, so that no owned root holds it.
    internal static bool ReachesThroughBorrow(Koto source) => HasReceiverOnPath(source, owners: false);

    internal static bool IsSyntax(Koto source) => source is IndexKoto index ? !IsUserIndex(index) : source is MemberAccessKoto { Right: NumberLiteralKoto } ||
        (source is MemberAccessKoto { BoundSymbol.Property.IsStored: true, Left.BoundType: { } type } && StructStorage.IsStruct(type));

    // SPEC 4.6.9: receiver[key] resolved through a user Indexable conformance: the Binding synthesized its index call, and
    // its indexUniq call where the use may update or borrow exclusively. Such an expression is a Place call, not an element projection.
    internal static InvocationKoto? IndexerCall(Koto source, bool exclusive) => source.CodeContext.Compilation.Binding.IndexerCall(source, exclusive);

    internal static bool IsUserIndex(Koto source) => KotoHelper.UnwrapParentheses(source) is IndexKoto index && IndexerCall(index, false) is not null;

    // SPEC 4.6.1, 4.6.4: the evaluated key of a built-in selection. An Index or Range key is resolved against the
    // receiver's length by a synthesized call; an isize or ResolvedRange key is applied as written.
    internal static Koto KeySyntax(IndexKoto index) => index.CodeContext.Compilation.Binding.ResolvedKeyCall(index) ?? index.Right;

    // SPEC 4.6.4: a range selection applied through one ResolvedRange value rather than two isize boundaries.
    internal static bool IsResolvedSlice(Koto source) => source is IndexKoto index && index.CodeContext.Compilation.Binding.IsResolvedSlice(index);

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

    /// <summary>
    /// SPEC 3.4.1, 13.4: the Type through which a Place expression read in place, a receiver or a string comparison
    /// operand, is accessed. Through several reference layers, or for a shared element Place, it is the one reference
    /// that Binding recorded as the expression's adaptation.
    /// </summary>
    /// <param name="node">The expression read in place.</param>
    /// <returns>The access Type.</returns>
    internal static BoundType? AccessType(Koto node)
        => node.CodeContext.Compilation.Binding.TryGetAdaptation(node, out var adaptation) && adaptation.Kind is ExpectedAdaptationKind.ReferenceRead or ExpectedAdaptationKind.SharedBorrow
            ? adaptation.Type : node.BoundType;

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

            var receiver = AccessType(field.Left);
            if (ReferenceTypes.IsStruct(receiver) || ReferenceTypes.IsTuple(receiver) || ObjectTypes.IsBorrow(receiver))
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
        var left = AccessType(field.Left);
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
        var receiver = AccessType(source.Left);
        if (ReferenceTypes.IsTuple(receiver) && source is MemberAccessKoto { Right: NumberLiteralKoto number } &&
            number.IsInteger && number.TryGetIntegerMagnitude(out var magnitude) && magnitude < (ulong)receiver!.Components[0].Components.Count)
        {
            position = (int)magnitude;
            element = receiver.Components[0].Components[position];
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

    private static bool HasReceiverOnPath(Koto source, bool owners)
    {
        for (var depth = 0; depth < 64 && KotoHelper.UnwrapParentheses(source) is BinaryKoto selection && selection is IndexKoto { Right: not RangeKoto } or MemberAccessKoto &&
            !Binding.IsGetterResult(selection); depth++)
        {
            var receiver = AccessType(selection.Left);
            if (owners ? IsBorrowedReceiver(receiver) : receiver?.Kind == BoundTypeKind.Slice || ReferenceTypes.IsBorrow(receiver))
            {
                return true;
            }

            source = selection.Left;
        }

        return false;
    }
}
