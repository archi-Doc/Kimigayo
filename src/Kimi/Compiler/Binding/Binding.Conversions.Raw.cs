// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The conversions of raw pointers and raw Places (SPEC 5.2.2, 5.4, 5.5): pointer casts, Typed Null Formation, the @raw address and
// a borrow of a raw Place; until PLAN U8, the gate on a view of a nonnull Option's storage as another Type (CheckNonnullView, G821).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, (ulong Version, bool May)> nonnullHeld = new(); // MayHoldNonnull's answers, per pass (G821).
    private readonly HashSet<BoundType> nonnullSeen = new(); // MayHold's visited parts, one walk at a time (G821).

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

    // PLAN G821 (the owner's O-237 (a); a temporary gate, removed when U8 lands): until the Backend lays out IMPL 21.1.3's nonnull Option,
    // a view between raw pointer Types whose pointees are not the same Type (SPEC 5.4: a same-Type conversion is an ordinary Copy) is a
    // located Unsupported_Kd where either pointee may hold a nonnull Option (MayHoldNonnull): an access through the view may read or write
    // that Option's storage as another Type (SPEC 5.2: at any byte within the operand's provenance, by a displacement, a Field or a pun),
    // which the tagged layout miscompiles. The view, not each access, is judged: the adaptation table's Pointer row (CheckAdaptationCase)
    // is where a pointee Type is chosen, for a written conversion and a pair binder's raw case alike. The record is at the written
    // pointee Type (the conversion's Right), so the conversion stays bound and its own checks, its operand's and its uses' run (SPEC
    // 23.1.2).
    private void CheckNonnullView(ConversionKoto conversion, BoundType source, BoundType target)
    {
        if (ReferenceTypes.IsPointer(source) && ReferenceTypes.IsPointer(target) && !this.SameStructure(source.Components[0], target.Components[0]) &&
            (this.MayHoldNonnull(source.Components[0]) || this.MayHoldNonnull(target.Components[0])))
        {
            this.Fail(conversion.Right, BindingFailure.Unsupported);
        }
    }

    // Whether a value of the Type may hold a nonnull Option (TyCtxt.NonnullOption yes or undecided: a pair payload whose admitted set
    // meets IMPL's R list is nonnull in some case, never read as none; the owner's O-252 (a)): at itself, at a part Holdings states at
    // its own address (Inline: a Field, the base, an element, a Case payload, an environment binding) or past a raw layer it holds (a
    // Raw entry: SPEC 5.2, a punned raw pointer has the provenance of the raw pointer whose bytes it reads, so a view of the layer
    // reaches its pointee; a borrow's or an object handle's bytes were written by no raw pointer and give none, and a collection's
    // handle component, whose `data` a raw write wrote, is an own or shared step whose layout is implementation-defined for any source
    // but the Kimi Kotonoha's, IMPL 21.1.3), at any depth; or where a part is undecided (an Unsupported row, Binding's Limit), never
    // read as none (ruling 6).
    // Adds nothing: a declaration on an inline cycle (its InvalidInlineLayout_Kd is the record; its parts are not walked), an Abstract
    // part (the named limit: Binding binds a generic body once), a part Binding did not bind (its own record).
    // Holdings is one level by contract, and this temporary fact (removed at U8) does not enter a canonical walk (StorageHeight, which
    // reaches raw pointees, publishes only its height verdict), so the walk keeps a visited set (each distinct part once per walk, a
    // raw layer's cycle closed) and a memo per pass of each walked pointee's answer (nonnullHeld, stamped by storageVersion, as
    // comparisonPlans is); it ends at Binding's own limits (an expanding instance is a Limit). Linear in the distinct parts; allocates
    // nothing warm; retentionHeld is read as a stack.
    private bool MayHoldNonnull(BoundType type)
    {
        if (this.nonnullHeld.TryGetValue(type, out var known) && known.Version == this.storageVersion)
        {
            return known.May;
        }

        this.nonnullSeen.Clear();
        var may = this.MayHold(type);
        this.nonnullHeld[type] = (this.storageVersion, may);
        return may;
    }

    // MayHoldNonnull's walk from one part.
    private bool MayHold(BoundType type)
    {
        if (!this.nonnullSeen.Add(type) || type.Symbol?.Adt is { InlineCycle: true })
        {
            return false;
        }

        if (this.types.NonnullOption(type) != false)
        {
            return true;
        }

        var start = this.retentionHeld.Count;
        var may = !this.Holdings(type, null, null, this.retentionHeld);
        for (var i = start; !may && i < this.retentionHeld.Count; i++)
        {
            var entry = this.retentionHeld[i];
            may = entry.Step == HoldStep.Unsupported ||
                ((entry.Step == HoldStep.Inline || (entry.Flags & HoldFlags.Raw) != 0) && entry.Part is { } part && this.MayHold(part));
        }

        this.retentionHeld.RemoveRange(start, this.retentionHeld.Count - start);
        return may;
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
