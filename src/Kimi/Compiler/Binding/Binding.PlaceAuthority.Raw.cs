// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place authority, raw concern (SPEC 5.2, 5.2.2, 5.7; design-final §4): what a raw Place is, its root (IsRawRoot) and the walks from a
// Place to that root (IsRawPlace: a Take without state tracking; IsRawRooted: a borrow, an address and place-ness; IsPointerPath: a write
// that binding mutability does not decide), which the Place classification, the conversions, the unsafe-context rule and the HIR
// publisher read; and what a Place's path reaches (PathOrigin), in one walk over NextPathStep:
// the Origin it is reached through, which a `during self` layer is read with (ReadHolder) and a typed path states (BindAddress), and
// the stored Field it is or lies inline in, which the allowlist of a layer reached through no dereference judges (HolderPlaceFailure).
public sealed partial class Binding
{
    // SPEC 5, 5.2, 5.3: the root of a raw Place's path, `*p` or `p[n]` (the node itself, already unwrapped), the one test of it: the raw
    // walks (IsRawPlace, IsRawRooted, IsPointerPath), the Place classification (IsBarePlace, LendsPlace, PlaceOrigin, PlaceOriginSource,
    // Writable, TakeJudgment), its path step (NextPathStep) and the unsafe-context rule (RequiresUnsafe) read it.
    private static bool IsRawRoot(Koto source)
        => source is DereferenceKoto || (source is IndexKoto index && HirTables.TypeOf(index.Left) is { IsPointer: true });

    // The stored Type of a Place selected in place from a stored Field: the Field's declared Type, or its element at `positions` (the
    // leaf's selection first); null without a Field, or where a selection is not of a Tuple or fixed array.
    private static BoundType? StoredPart(MemberAccessKoto? field, ReadOnlySpan<int> positions)
    {
        var stored = field?.BoundSymbol?.Type;
        for (var i = positions.Length - 1; i >= 0 && stored is not null; i--)
        {
            var inline = stored.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray && positions[i] < stored.Components.Count;
            stored = inline ? stored.Components[positions[i]] : null;
        }

        return stored;
    }

    // SPEC 5.2, 4.7.1, 15.1.5: a raw Place, the Place a Take moves without state tracking: *p, p[n], and each part of one the raw
    // storage holds itself (TyCtxt.RawPart: a stored Field, a Tuple element, a fixed-array element at an integer or from-end position).
    // An Array, view, Dictionary or user element, and a part past a stored reference, is a referent, never a raw Place. MIR's move paths
    // read the same TyCtxt.RawPart per step (MirMoveData.Resolve).
    private bool IsRawPlace(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64; depth++)
        {
            if (IsRawRoot(source))
            {
                return true;
            }

            if (source is not BinaryKoto element || !this.IsSyntax(element) || !TyCtxt.RawPart(HirTables.TypeOf(element.Left)) ||
                (element is IndexKoto && KotoHelper.UnwrapParentheses(element.Right) is var key &&
                    (key is RangeKoto || (key is not FromEndIndexKoto && HirTables.TypeOf(element.Right) is not { IsInteger: true }))))
            {
                return false;
            }

            source = KotoHelper.UnwrapParentheses(element.Left);
        }

        return false;
    }

    // SPEC 5.2.2, 4.7.1: a Place rooted at a raw Place: *p or p[n], or a Place reached from one by built-in steps (IsSyntax): a stored
    // Field, a Tuple element and a built-in (no user indexer's) element at an integer position, an Array's included (its receiver is the
    // raw Place's part), each step also past a reference stored in the raw storage (BorrowRawPlace then bounds a borrow by that
    // reference's Origin). A range or from-end position ends it, so a from-end raw Place (IsRawPlace) is not rooted here. Its borrow is a
    // fresh anchor (BorrowRawPlace), its address and its place-ness the raw Place's; only IsRawPlace's Places offer Take.
    private bool IsRawRooted(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64; depth++)
        {
            if (IsRawRoot(source))
            {
                return true;
            }

            if (source is not BinaryKoto element || !this.IsSyntax(element) ||
                (element is IndexKoto && (KotoHelper.UnwrapParentheses(element.Right) is RangeKoto or FromEndIndexKoto ||
                    HirTables.TypeOf(element.Right) is not { IsInteger: true })))
            {
                return false;
            }

            source = KotoHelper.UnwrapParentheses(element.Left);
        }

        return false;
    }

    // SPEC 5.2, 12: a Place reached from *p or p[n] by one or more built-in steps into an owned value (IsInlinePart): a stored
    // Field, a Tuple element, or an element of a fixed array, Array or Dictionary. Binding mutability does not decide its write permission.
    private bool IsPointerPath(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64 && source is BinaryKoto element && this.IsInlinePart(element); depth++)
        {
            source = KotoHelper.UnwrapParentheses(element.Left);
            if (IsRawRoot(source))
            {
                return true;
            }
        }

        return false;
    }

    // The position an inline part step selects in its owner's Type (a Tuple element's, 0 for a fixed-array element); null for any other
    // step (SPEC 3.4.3).
    private int? InlinePosition(PathStep step, Koto source)
        => step is PathStep.Member or PathStep.InlinePart && this.TryType((BinaryKoto)source, out _, out var position) ? Math.Max(position, 0) : null;

    // SPEC 5.7 (Reading a Field, Typed paths): the Origin of the last borrow or raw pointer that a Place's path dereferences, from the
    // leaf through NextPathStep: a member's, a borrowed element's or an inline part's the innermost borrow, raw or pair layer of the Type
    // it is selected from; a followed borrow's outer Origin; a pair follow's slot; a raw pointer's Origin (none when it states none); a
    // user index's published element; a Slice element's Slice. Owned roots, owned parts, owned Array elements and object payloads
    // continue. Dereferences is true when the path dereferences; false through no dereference, with the Origin of a special receiver at
    // the root (SPEC 5.7: the value constructed or destroyed) and none otherwise; null for a pair layer admitting a non-borrow case or a
    // layer that is no borrow (U2, located by the caller). Field and Stored: the stored Field the leaf's inline selections (Tuple and
    // fixed-array elements) start from, and the leaf's stored Type in it.
    private RawPath PathOrigin(Koto place)
    {
        Span<int> positions = stackalloc int[16];
        var selected = 0;
        MemberAccessKoto? field = null;
        var source = KotoHelper.UnwrapParentheses(place);
        for (var depth = 0; depth < 64; depth++)
        {
            var step = this.NextPathStep(source, out var next);
            if (field is null && selected >= 0)
            {
                if (source is MemberAccessKoto { BoundSymbol.Property.IsStored: true } member)
                {
                    field = member;
                }
                else if (this.InlinePosition(step, source) is { } position)
                {
                    if (selected == positions.Length)
                    {
                        var grown = new int[2 * selected];
                        positions.CopyTo(grown);
                        positions = grown;
                    }

                    positions[selected++] = position;
                }
                else
                {
                    selected = -1;
                }
            }

            if (this.Dereference(step, source, next) is { } reached)
            {
                // Read once at the Place (the owner's O-385 (a)): a `let` region of its body as its value (PLAN G492-a (ii)).
                var origin = reached.Origin is { } layer ? this.RegionValueAtUse(layer, place) : null;
                return reached with { Origin = origin, Field = field, Stored = StoredPart(field, positions[..Math.Max(selected, 0)]) };
            }

            source = KotoHelper.UnwrapParentheses(next);
        }

        return new(false, null, field, StoredPart(field, positions[..Math.Max(selected, 0)]));
    }

    // One step of PathOrigin: what a step that ends the walk reaches, null for one the walk continues past.
    private RawPath? Dereference(PathStep step, Koto source, Koto next)
    {
        switch (step)
        {
            case PathStep.Follow when next.BoundType is { Kind: BoundTypeKind.Semantics } followed && IsBorrow(followed.Semantics):
                return this.Dereferenced(followed);
            case PathStep.PairFollow or PathStep.RawFollow when next.BoundType is { } layer:
                return this.Reached(layer);
            case PathStep.UserIndex when this.IndexerCall(source, exclusive: false)?.BoundType is { } element:
                return this.Reached(element);
            case PathStep.SliceElement when (next.BoundType?.Kind == BoundTypeKind.Slice ? next.BoundType : next.BoundType?.Components[0]) is { } slice:
                return this.Dereferenced(slice);
            case PathStep.Receiver when source.BoundSymbol is { } receiver:
                return new(false, this.ReceiverOrigin(receiver));
            case PathStep.Member or PathStep.BorrowedElement or PathStep.InlinePart when this.InnermostLayer(next.BoundType) is { } layer:
                return this.Reached(layer);
            case PathStep.Follow or PathStep.BorrowedElement or PathStep.ArrayElement or PathStep.Member or PathStep.InlinePart:
                return null;
            default:
                return new(false, null);
        }
    }

    // The innermost borrow, raw or pair layer of a Type's own layers (BelowLayer), through which a part selected from it is reached;
    // null for a Type without one (an owned Type, or object handles only).
    private BoundType? InnermostLayer(BoundType? type)
    {
        BoundType? innermost = null;
        for (; BelowLayer(type) is { } below; type = below)
        {
            innermost = type!.IsPointer || IsBorrow(type.Semantics) || TryPairLayer(type, out _, out _) ? type : innermost;
        }

        return innermost;
    }

    // SPEC 5.7 Reading a Field, Typed paths (C-189): the dereference a step past a value of `prefix` makes, the one rule PathOrigin's steps
    // and the Place keys' walk (Binding.Select, PastThrough) read: a raw, borrow or pair layer's (Reached), a Slice's or UniqSlice's (its
    // Origin); null where the path goes on past the value (an owning handle's payload, a collection's element).
    private RawPath? Dereferenced(BoundType prefix)
        => prefix.Kind == BoundTypeKind.Slice ? new(true, prefix.Origin)
            : prefix is { Symbol.LibraryDeclaration: KimiDeclarationId.UniqSlice, OriginArguments: [var slice] } ? new(true, slice)
            : TryPairLayer(prefix, out _, out _) ||
                prefix.IsPointer || (prefix.Kind == BoundTypeKind.Semantics && IsBorrow(prefix.Semantics)) ? this.Reached(prefix) : null;

    // The layer a path dereferences: a raw pointer's Origin, a borrow's outer Origin, or a pair layer's slot; Dereferences is null for a
    // pair layer admitting a non-borrow case or a layer that is no borrow (U2).
    private RawPath Reached(BoundType layer)
    {
        if (layer.IsPointer)
        {
            return new(true, layer.RawOrigin);
        }

        return new(this.HasBorrowSemantics(layer) ? true : null, this.OuterOrigin(layer));
    }

    // SPEC 5.7, 6.3.4, 16.3.1 (DECISIONS BU-23): the Origin of a constructor's or destructor's receiver `self`, its Place's atom.
    private BoundOrigin ReceiverOrigin(BindingSymbol receiver) => this.OriginAtom(receiver.Declaration, OriginKind.Projection, ReceiverSlot);

    // What PathOrigin publishes for a Place (SPEC 5.7): whether its path dereferences (null: U2), the Origin of the last dereference,
    // and the stored Field the Place is or lies inline in, with the Place's stored Type there.
    private readonly record struct RawPath(bool? Dereferences, BoundOrigin? Origin, MemberAccessKoto? Field = null, BoundType? Stored = null);
}
