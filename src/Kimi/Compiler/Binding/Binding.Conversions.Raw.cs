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
    // borrowed address, which keeps no Loan. The operation's borrow is recorded on the operator. SPEC 5.7 (Typed paths): the pointer
    // states the Origin of the last borrow or raw pointer P is reached through (PathOrigin), formed with that borrow's mode.
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
        if (ElementAccess.IsRawRooted(conversion.Left))
        {
            // SPEC 5.4, 5.2.2: the address of a raw Place or a part of one borrows that Place as a fresh anchor.
            borrowed = this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [stored], origin: BoundOrigin.Static);
        }
        else if (!BorrowsWrittenSlot(stored) ||
            !this.AdaptInput(conversion.Left, FixedPattern.Open(this.types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [stored])), stored, scope, null, null, out borrowed, out _, out _, explicitBorrow: true))
        {
            return this.Fail(conversion, this.BorrowFailure(conversion.Left, scope, exclusive: false));
        }

        var path = this.PathOrigin(conversion.Left);
        if (path.Dereferences is null)
        {
            return this.Fail(conversion, BindingFailure.Unsupported); // U2: a pair follow admitting a non-borrow case.
        }

        Complete(conversion.Right, borrowed);
        conversion.ConversionBinding = ConversionBinding.Address;
        return Complete(conversion, this.types.Pointer(stored, path.Origin));
    }

    // SPEC 5.4, 5.5: a pointer converts to another pointer Type or usize, and usize to a pointer. SPEC 5.7: a pointer keeps its raw
    // Origin unless the target states one, which is a fit of the source's Origin (none for usize) outside an unsafe context and a
    // claim, which records no relation, inside one. Its one case is judged by the adaptation table's Pointer row (CheckAdaptationCase),
    // as a pair binder's raw case is.
    private BoundType? CompletePointerConversion(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target)
    {
        if ((ReferenceTypes.IsPointer(source) && (ReferenceTypes.IsPointer(target) || ReferenceEquals(target, BoundType.USize))) ||
            (ReferenceEquals(source, BoundType.USize) && ReferenceTypes.IsPointer(target)))
        {
            // A Pointer case lends no Borrow, so the judge's null (an unformed Borrow) is a broken contract here, never silent.
            var verdict = this.CheckAdaptationCase(conversion, scope, source, target, ConversionBinding.Pointer, []);
            if (verdict != true)
            {
                return verdict is null ? this.Fail(conversion, BindingFailure.Internal) : Complete(conversion, null);
            }

            conversion.ConversionBinding = ConversionBinding.Pointer;
            if (target.RawOrigin is null && source.RawOrigin is { } kept && ReferenceTypes.IsPointer(target))
            {
                return Complete(conversion, this.types.Pointer(target.Components[0], kept));
            }

            if (target.RawOrigin is not null && !KotoHelper.IsUnsafeContext(conversion))
            {
                _ = this.CheckTypeUse(this.types.Pointer(target.Components[0], source.RawOrigin), target, conversion.Left, out _);
            }

            return Complete(conversion, target);
        }

        return this.Fail(conversion, BindingFailure.TypeMismatch);
    }

    // SPEC 5.2.2: a borrow of a raw Place recovers no owner or Loan. Its Origin is RawAnchor's, which fits every destination, a
    // result included, while its Loan is checked under the anchor. An exclusive borrow is denied only by a safe shared layer inside
    // the pointee: no raw Origin denies an access (SPEC 5.7).
    private BoundType? BorrowRawPlace(ConversionKoto conversion, SemanticsKind semantics, BoundType referent)
    {
        if (semantics == SemanticsKind.Uniq && ReachedThroughShared(conversion.Left))
        {
            return this.Fail(conversion, AccessFailure(conversion.Left, out _));
        }

        var borrowed = this.types.Intern(BoundTypeKind.Semantics, null, semantics, [referent], origin: this.RawAnchor(conversion, conversion.Left));
        Complete(conversion.Right, borrowed);
        conversion.ConversionBinding = ConversionBinding.Borrow;
        return Complete(conversion, borrowed);
    }

    // SPEC 5.2.2, 5.7: the Origin of a borrow, written at `at` or implicit (PlaceOrigin), of `place`, a Place rooted at a raw Place: a
    // fresh anchor, which fits every destination, within the Origin a pointer it is reached through states.
    private BoundOrigin RawAnchor(Koto at, Koto place)
    {
        var anchor = this.OriginAtom(at, OriginKind.Anchor, 0, "anchor");
        return this.PathOrigin(place) is { Dereferences: true, Origin: { } within } ? this.Meet(anchor, within) : anchor;
    }
}
