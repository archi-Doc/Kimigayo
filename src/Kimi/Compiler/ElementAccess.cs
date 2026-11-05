// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class ElementAccess
{
    // SPEC 4.8.2, 4.7.1: an element of a Slice or of a referenced array, including a fixed array reached through a
    // borrowed receiver; no owned root holds it.
    internal static bool IsSharedElement(Koto source) => source is IndexKoto { Right: not RangeKoto } element &&
        (element.Left.BoundType?.Kind == BoundTypeKind.Slice || ReferenceTypes.IsArray(AccessType(element.Left)) || ReferenceTypes.IsDynamicArray(element.Left.BoundType) || ReferenceTypes.IsDictionary(element.Left.BoundType) ||
            (element.Left.BoundType?.Kind is BoundTypeKind.Array or BoundTypeKind.FixedArray && ReachesThroughBorrow(element.Left)));

    // SPEC 3.4.3: the referent of an exclusive array reference offers element replacement, without requiring a mutable
    // binding for the reference itself. Shared layers on its path are checked separately by Binding.
    // An owned Array or fixed array stored below exclusive references only (`b.items[i]` with `b: uniq/Bank`) offers the same, whose
    // path authority decides it rather than the stored Type (PLAN G59).
    internal static bool IsExclusiveArrayElement(Koto source) => source is IndexKoto { Right: not RangeKoto } element && !IsSlicing(source) &&
        ((AccessType(element.Left, true) is { Semantics: SemanticsKind.Uniq } receiver &&
            (ReferenceTypes.IsDynamicArray(receiver) || ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDictionary(receiver))) ||
        (element.Left.BoundType?.Kind is BoundTypeKind.Array or BoundTypeKind.FixedArray && ReachesThroughBorrow(element.Left) &&
            Binding.PathAuthority(element.Left, []) == SemanticsKind.Uniq));

    // SPEC 4.7.1: a receiver whose selected Place is reached by a dynamic key or through a reference: a Slice, a dynamic
    // Array, a Dictionary or a borrow.
    internal static bool IsBorrowedReceiver(BoundType? receiver)
        => receiver is { Kind: BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary } || ReferenceTypes.IsBorrow(receiver);

    // SPEC 4.8.2, 4.7.1: whether a selection reaches its Place through a Slice or a borrow, so that no owned root holds it.
    internal static bool ReachesThroughBorrow(Koto source) => HasReceiverOnPath(source);

    internal static bool IsSyntax(Koto source) => source is IndexKoto index ? !IsUserIndex(index) && index.CodeContext.Compilation.Binding.ViewRangeCall(index) is null : source is MemberAccessKoto { Right: NumberLiteralKoto } ||
        (source is MemberAccessKoto { BoundSymbol.Property.IsStored: true, Left.BoundType: { } type } && AdtDef.IsStruct(type));

    // SPEC 4.7.1: receiver[key] resolved through a user Indexable conformance: the Binding synthesized its index call, and
    // its indexUniq call where the use may update or borrow exclusively. The expression designates the referent of the reference that
    // call returns, not an element projection.
    internal static InvocationKoto? IndexerCall(Koto source, bool exclusive) => source.CodeContext.Compilation.Binding.IndexerCall(source, exclusive);

    internal static bool IsUserIndex(Koto source) => KotoHelper.UnwrapParentheses(source) is IndexKoto index && IndexerCall(index, false) is not null;

    // SPEC 4.6.3: a range selection applied through one ResolvedRange value rather than two isize boundaries.
    internal static bool IsResolvedSlice(Koto source) => source is IndexKoto index && index.CodeContext.Compilation.Binding.IsResolvedSlice(index);

    // SPEC 4.8.2: a range selection, written with a range or applied through a ResolvedRange key, produces a Slice value,
    // not a Place.
    internal static bool IsSlicing(Koto source) => source is IndexKoto { BoundType.Kind: BoundTypeKind.Slice } slice && (slice.Right is RangeKoto || IsResolvedSlice(slice));

    // SPEC 4.7: a fixed array, Slice or Array, directly or through a reference: the receivers whose keys are positions.
    internal static bool IsSequence(BoundType? type)
        => (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? type.Components[0] : type)?.Kind
            is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array;

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
        if (source is not IdentifierNameKoto { BoundSymbol: { Kind: BindingSymbolKind.Local or BindingSymbolKind.Capture } symbol } root)
        {
            return null;
        }

        return Binding.Reassignable(symbol) ? root : null;
    }

    // SPEC 5.2, 12: *p, p[n], and the stored fields, Tuple elements and integer-indexed fixed-array elements of one are raw
    // Places. Range and from-end forms keep their existing handling.
    internal static bool IsRawPlace(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64; depth++)
        {
            if (source is DereferenceKoto || (source is IndexKoto index && ReferenceTypes.IsPointer(index.Left.BoundType)))
            {
                return true;
            }

            if (source is not BinaryKoto element || !IsSyntax(element) ||
                (element is IndexKoto && (KotoHelper.UnwrapParentheses(element.Right) is RangeKoto or FromEndIndexKoto || element.Right.BoundType is not { IsInteger: true })))
            {
                return false;
            }

            source = KotoHelper.UnwrapParentheses(element.Left);
        }

        return false;
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
    /// SPEC 3.4.3, 13.4: the Type through which a Place expression read in place, a receiver or a string comparison
    /// operand, is accessed. Through several reference layers, or for a shared element Place, it is the one reference
    /// that Binding recorded as the expression's adaptation.
    /// </summary>
    /// <param name="node">The expression read in place.</param>
    /// <param name="exclusive">Whether the selected projection requires exclusive access.</param>
    /// <returns>The access Type.</returns>
    internal static BoundType? AccessType(Koto node, bool exclusive = false)
    {
        var binding = node.CodeContext.Compilation.Binding;
        if (!binding.TryGetAdaptation(node, out var adaptation) || adaptation.Kind is not (ExpectedAdaptationKind.ReferenceRead or ExpectedAdaptationKind.SharedBorrow))
        {
            return node.BoundType;
        }

        // SPEC 3.4.3, 4.7.1: a synthesized shared access does not decide the later projection's capability; the final acquisition
        // does. An exclusive use takes an indexer's bound exclusive entry, or a synthesized access exclusively when the real path,
        // continued through the Place's own layers, grants exclusive access, with the same Origin; a shared layer of the Place's
        // own Type never does (SPEC 15.6.2).
        return exclusive && adaptation.Kind == ExpectedAdaptationKind.SharedBorrow && !Binding.HasSharedLayer(node.BoundType) &&
            (IndexerCall(node, true) is not null || (IsSynthesizedAccess(node) && Binding.ReferentAuthority(node) == SemanticsKind.Uniq))
            ? binding.Types.Reference(SemanticsKind.Uniq, adaptation.Type.Components[0], adaptation.Type.Origin) : adaptation.Type;
    }

    // SPEC 3.4.3: a shared borrow that Binding synthesized to read a Place in place, of the Place's own complete stored Type or of
    // the Type its safe reference layers reach (Binding.ElementRead), is a representation of the selection rather than a reference
    // layer on its path.
    internal static bool IsSynthesizedAccess(Koto node)
        => node.CodeContext.Compilation.Binding.TryGetAdaptation(node, out var adaptation) &&
            adaptation is { Kind: ExpectedAdaptationKind.SharedBorrow, Type.Components: [var read] } && node.BoundType is { } type &&
            (ReferenceEquals(read, type) || ReferenceEquals(read, Binding.ComparisonReferent(type)));

    // SPEC 15.6: a direct field/Tuple path whose base is a borrowed struct or
    // Tuple reference; nested levels must be inline stored parts. Returns the
    // reference-typed base, or null for other forms.
    // The reference whose struct or Tuple referent `reference@follow` selects, or null.
    internal static Koto? FollowedReference(Koto receiver)
        => KotoHelper.UnwrapParentheses(receiver) is ConversionKoto { ConversionBinding: ConversionBinding.Follow } followed &&
            AccessType(followed.Left) is var type && (ReferenceTypes.IsStruct(type) || ReferenceTypes.IsTuple(type)) ? followed.Left : null;

    // SPEC 4.7.1: an owned fixed-array element selected at a runtime index, which has no static Move Path.
    internal static bool IsRuntimeFixedElement(Koto selection)
        => KotoHelper.UnwrapParentheses(selection) is IndexKoto element && IsSyntax(element) && !IsSlicing(element) && !Binding.IsGetterResult(element) &&
            element.Left.BoundType?.Kind == BoundTypeKind.FixedArray && StaticSelector(element) < 0;

    internal static Koto? BorrowedPathRoot(MemberAccessKoto field)
    {
        for (var depth = 0; depth < 64; depth++)
        {
            if (Binding.IsGetterResult(field))
            {
                return null;
            }

            var receiver = AccessType(field.Left);
            if (ReferenceTypes.IsStruct(receiver) || ReferenceTypes.IsTuple(receiver) || receiver is { IsObjectBorrow: true } || receiver?.HandleMode is not null)
            {
                return field.Left;
            }

            // SPEC 13.5.5: an explicitly selected referent (`p@follow.x`) is reached through its reference, as `p.x` is.
            if (FollowedReference(field.Left) is { } reference)
            {
                return reference;
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
            if (receiver is IdentifierNameKoto { BoundSymbol.Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture } root)
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
        if (type?.Semantics != SemanticsKind.Owner)
        {
            return false;
        }

        if (source is MemberAccessKoto { BoundSymbol.Property.IsStored: true } && AdtDef.IsStruct(type))
        {
            return AdtDef.FindField(type, source.BoundSymbol, out element, out position);
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

    private static bool HasReceiverOnPath(Koto source)
    {
        for (var depth = 0; depth < 64 && KotoHelper.UnwrapParentheses(source) is BinaryKoto selection && selection is IndexKoto { Right: not RangeKoto } or MemberAccessKoto &&
            !Binding.IsGetterResult(selection); depth++)
        {
            var receiver = AccessType(selection.Left);
            if (receiver?.Kind == BoundTypeKind.Slice || ReferenceTypes.IsBorrow(receiver))
            {
                return true;
            }

            source = selection.Left;
        }

        return false;
    }
}
