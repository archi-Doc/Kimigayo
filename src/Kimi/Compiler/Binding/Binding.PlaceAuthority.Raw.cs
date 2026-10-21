// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place authority, raw concern (SPEC 5.7; design-final §4): the Origin a Place's path is reached through (PathOrigin), which a
// `during self` layer is read with (ReadHolder) and decides whether a Place is reached through no dereference (HolderPlaceFailure).
public sealed partial class Binding
{
    // SPEC 5.7, 15.6.2 (D11): the Origin and mode of the last borrow or raw pointer that a Place's path dereferences, from the leaf
    // through NextPathStep: a member's, a borrowed element's or an inline part's the innermost borrow, raw or pair layer of the Type
    // it is selected from; a followed borrow's outer Origin and layer; a pair follow's slot with its admitted cases' mode; a raw
    // pointer's Origin and step (none when it states none); a user index's published element; a Slice element's Slice, shared. Owned
    // roots, owned parts, owned Array elements and object payloads continue. True when the path dereferences; false through no
    // dereference, with the Origin of a special receiver at the root (exclusive; SPEC 5.7: the value constructed or destroyed) and
    // none otherwise; null for a pair layer admitting a non-borrow case (U2, located by the caller).
    private bool? PathOrigin(Koto place, out BoundOrigin? origin, out RawStep mode)
    {
        origin = null;
        mode = RawStep.Unstated;
        var source = KotoHelper.UnwrapParentheses(place);
        for (var depth = 0; depth < 64; depth++)
        {
            switch (NextPathStep(source, out var next))
            {
                case PathStep.Follow when next.BoundType is { } followed && this.LayerStep(followed) is not null:
                    return this.Reached(followed, out origin, out mode);
                case PathStep.PairFollow or PathStep.RawFollow when next.BoundType is { } layer:
                    return this.Reached(layer, out origin, out mode);
                case PathStep.UserIndex when ElementAccess.IndexerCall(source, false)?.BoundType is { } element:
                    return this.Reached(element, out origin, out mode);
                case PathStep.SliceElement:
                    origin = (next.BoundType?.Kind == BoundTypeKind.Slice ? next.BoundType : next.BoundType?.Components[0])?.Origin;
                    mode = RawStep.Shared;
                    return true;
                case PathStep.Receiver when source.BoundSymbol is { } receiver:
                    origin = this.ReceiverOrigin(receiver);
                    mode = RawStep.Exclusive;
                    return false;
                case PathStep.Member or PathStep.BorrowedElement or PathStep.InlinePart when this.InnermostLayer(next.BoundType) is { } layer:
                    return this.Reached(layer, out origin, out mode);
                case PathStep.Follow or PathStep.BorrowedElement or PathStep.ArrayElement or PathStep.Member or PathStep.InlinePart:
                    source = KotoHelper.UnwrapParentheses(next);
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    // The innermost borrow, raw or pair layer of a Type's own layers (BelowLayer), through which a part selected from it is reached;
    // null for a Type without one (an owned Type, or object handles only).
    private BoundType? InnermostLayer(BoundType? type)
    {
        BoundType? innermost = null;
        for (; BelowLayer(type) is { } below; type = below)
        {
            innermost = type!.Semantics == SemanticsKind.Raw || this.LayerStep(type) is not null || TryPairLayer(type, out _, out _) ? type : innermost;
        }

        return innermost;
    }

    // The Origin and mode of the layer a path dereferences: a raw pointer's Origin and step, a borrow's or a pair layer's outer Origin
    // and mode; null for a pair layer whose admitted cases mix modes or admit a non-borrow (U2).
    private bool? Reached(BoundType layer, out BoundOrigin? origin, out RawStep mode)
    {
        origin = layer.RawOrigin ?? this.OuterOrigin(layer);
        if (layer.Semantics == SemanticsKind.Raw && layer.Kind == BoundTypeKind.Semantics)
        {
            mode = this.StepOf(layer.RawOrigin) ?? RawStep.Exclusive;
            return true;
        }

        mode = this.LayerStep(layer) ?? RawStep.Unstated;
        return mode == RawStep.Unstated ? null : true;
    }

    // SPEC 5.7, 6.3.4, 16.3.1 (DECISIONS BU-23): the Origin of a constructor's or destructor's receiver `self`, its Place's atom.
    private BoundOrigin ReceiverOrigin(BindingSymbol receiver) => this.OriginAtom(receiver.Declaration, OriginKind.Projection, ReceiverSlot);
}
