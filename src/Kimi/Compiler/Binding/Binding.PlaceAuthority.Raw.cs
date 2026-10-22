// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place authority, raw concern (SPEC 5.7; design-final §4): what a Place's path reaches (PathOrigin), in one walk over NextPathStep:
// the Origin it is reached through, which a `during self` layer is read with (ReadHolder) and a typed path states (BindAddress), and
// the stored Field it is or lies inline in, which the allowlist of a layer reached through no dereference judges (HolderPlaceFailure).
public sealed partial class Binding
{
    // The position an inline part step selects in its owner's Type (a Tuple element's, 0 for a fixed-array element); null for any other
    // step (SPEC 3.4.3).
    private static int? InlinePosition(PathStep step, Koto source)
        => step is PathStep.Member or PathStep.InlinePart && ElementAccess.TryType((BinaryKoto)source, out _, out var position) ? Math.Max(position, 0) : null;

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
            var step = NextPathStep(source, out var next);
            if (field is null && selected >= 0)
            {
                if (source is MemberAccessKoto { BoundSymbol.Property.IsStored: true } member)
                {
                    field = member;
                }
                else if (selected < positions.Length && InlinePosition(step, source) is { } position)
                {
                    positions[selected++] = position;
                }
                else
                {
                    selected = -1;
                }
            }

            if (this.Dereference(step, source, next) is { } reached)
            {
                return reached with { Field = field, Stored = StoredPart(field, positions[..Math.Max(selected, 0)]) };
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
                return this.Reached(followed);
            case PathStep.PairFollow or PathStep.RawFollow when next.BoundType is { } layer:
                return this.Reached(layer);
            case PathStep.UserIndex when ElementAccess.IndexerCall(source, exclusive: false)?.BoundType is { } element:
                return this.Reached(element);
            case PathStep.SliceElement:
                return new(true, (next.BoundType?.Kind == BoundTypeKind.Slice ? next.BoundType : next.BoundType?.Components[0])?.Origin);
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
            innermost = type!.Semantics == SemanticsKind.Raw || IsBorrow(type.Semantics) || TryPairLayer(type, out _, out _) ? type : innermost;
        }

        return innermost;
    }

    // The layer a path dereferences: a raw pointer's Origin, a borrow's outer Origin, or a pair layer's slot; Dereferences is null for a
    // pair layer admitting a non-borrow case or a layer that is no borrow (U2).
    private RawPath Reached(BoundType layer)
    {
        if (layer.Semantics == SemanticsKind.Raw && layer.Kind == BoundTypeKind.Semantics)
        {
            return new(true, layer.RawOrigin);
        }

        var borrow = TryPairLayer(layer, out _, out _)
            ? this.LayerAdmitted(layer) is not SemanticsMask.None and var admitted && (admitted & ~SemanticsMask.Borrow) == 0
            : layer.Kind == BoundTypeKind.Semantics && IsBorrow(layer.Semantics);
        return new(borrow ? true : null, this.OuterOrigin(layer));
    }

    // SPEC 5.7, 6.3.4, 16.3.1 (DECISIONS BU-23): the Origin of a constructor's or destructor's receiver `self`, its Place's atom.
    private BoundOrigin ReceiverOrigin(BindingSymbol receiver) => this.OriginAtom(receiver.Declaration, OriginKind.Projection, ReceiverSlot);

    // What PathOrigin publishes for a Place (SPEC 5.7): whether its path dereferences (null: U2), the Origin of the last dereference,
    // and the stored Field the Place is or lies inline in, with the Place's stored Type there.
    private readonly record struct RawPath(bool? Dereferences, BoundOrigin? Origin, MemberAccessKoto? Field = null, BoundType? Stored = null);
}
