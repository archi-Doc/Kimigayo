// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The conversions of raw pointers and raw Places (SPEC 5.2.2, 5.4, 5.5): pointer casts, Typed Null Formation, the @raw address and
// a borrow of a raw Place.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 5.4: the expected Type of a pointer cast's input: an integer literal is first fitted to usize, and null@raw/U is Typed Null
    // Formation, a null of the target Type (SPEC 5.1); none for any other input or target.
    private static BoundType? PointerInput(BoundType? target, bool literal, Koto operand)
        => !ReferenceTypes.IsPointer(target) ? null : literal ? BoundType.USize : operand is NullLiteralKoto ? target : null;

    // SPEC 5.4: P@raw is the address of the written slot. It performs the checks of an immediately ending @ref and converts the
    // borrowed address, which keeps no Loan. The operation's borrow is recorded on the operator.
    private BoundType? BindAddress(ConversionKoto conversion, BindingScope scope)
    {
        var stored = this.BindNode(conversion.Left, scope);
        if (stored is null)
        {
            return Complete(conversion, null);
        }

        if (IsNeverValue(conversion.Left, stored))
        {
            conversion.ConversionBinding = ConversionBinding.Abrupt;
            return Complete(conversion, stored);
        }

        BoundType borrowed;
        if (ElementAccess.IsRawPlace(conversion.Left))
        {
            // SPEC 5.4, 5.2.2: the address of a raw Place or a part of one borrows that Place as a fresh anchor.
            borrowed = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [stored], origin: BoundOrigin.Static);
        }
        else if (!BorrowsWrittenSlot(stored) ||
            !this.AdaptInput(conversion.Left, this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [stored]), stored, scope, null, null, out borrowed, out _, out _, explicitBorrow: true))
        {
            return this.Fail(conversion, this.BorrowFailure(conversion.Left, scope, exclusive: false));
        }

        Complete(conversion.Right, borrowed);
        conversion.ConversionBinding = ConversionBinding.Address;
        return Complete(conversion, this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Raw, [stored]));
    }

    // SPEC 5.4, 5.5: a pointer converts to another pointer Type or usize, and usize to a pointer. SPEC 5.7: a pointer keeps its raw
    // Origin unless the target states one, which is a fit of the source's Origin (none for usize) outside an unsafe context and a
    // claim, which records no relation, inside one.
    private BoundType? CompletePointerConversion(ConversionKoto conversion, BoundType source, BoundType target)
    {
        if ((ReferenceTypes.IsPointer(source) && (ReferenceTypes.IsPointer(target) || ReferenceEquals(target, BoundType.USize))) ||
            (ReferenceEquals(source, BoundType.USize) && ReferenceTypes.IsPointer(target)))
        {
            conversion.ConversionBinding = ConversionBinding.Pointer;
            if (target.RawOrigin is null && source.RawOrigin is { } kept && ReferenceTypes.IsPointer(target))
            {
                return Complete(conversion, this.types.Pointer(target.Components[0], kept));
            }

            if (target.RawOrigin is not null && !KotoHelper.IsUnsafeContext(conversion))
            {
                _ = this.CheckTypeUse(this.types.Pointer(target.Components[0], source.RawOrigin), target, conversion.Left);
            }

            return Complete(conversion, target);
        }

        return this.Fail(conversion, BindingFailure.TypeMismatch);
    }

    // SPEC 5.2.2: a borrow of a raw Place recovers no owner or Loan. Its referent is a fresh anchor, so the result Origin has no
    // upper bound and is fitted to the expected Type, result or storage destination: a returned borrow takes the declared result's
    // Origin, and any other one is the anchor, which fits every destination while its Loan is checked under the anchor.
    private BoundType? BorrowRawPlace(ConversionKoto conversion, SemanticsKind semantics, BoundType referent)
    {
        var origin = ResultFunction(conversion)?.BoundSymbol?.Type is { Kind: BoundTypeKind.Semantics, Components: [var declared], Origin: { } result } declaredResult &&
            declaredResult.Semantics == semantics && ReferenceEquals(declared, referent) ? result : this.OriginAtom(conversion, OriginKind.Anchor, 0, "anchor");
        var borrowed = this.types.Intern(BoundTypeKind.Semantics, null, semantics, [referent], origin: origin);
        Complete(conversion.Right, borrowed);
        conversion.ConversionBinding = ConversionBinding.Borrow;
        return Complete(conversion, borrowed);
    }
}
