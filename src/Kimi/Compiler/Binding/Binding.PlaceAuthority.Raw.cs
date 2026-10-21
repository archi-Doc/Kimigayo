// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Place authority, raw concern (SPEC 5.7; design-final §4): the Origin a Place's path is reached through (PathOrigin), which a
// `during self` layer is read with (ReadHolder) and decides whether a Place is reached through no dereference (HolderPlaceFailure).
public sealed partial class Binding
{
    // SPEC 5.7, 15.6.2 (D11): the Origin and mode of the last borrow or raw pointer that a value `holder`'s path dereferences, from the
    // leaf through NextPathStep: the innermost borrow or raw layer of a value's own Type; the outer Origin and layer of a followed
    // borrow; a pair follow's slot with its admitted cases' mode; a raw pointer's Origin and step (none when it states none); a user
    // index's published element; a Slice element's Slice, shared. Owned roots, inline parts, owned Array elements and object payloads
    // continue. True when the path dereferences; false through no dereference, with the Origin of a special receiver at the root
    // (exclusive; SPEC 5.7: the value constructed or destroyed) and none otherwise; null for a pair layer admitting a non-borrow case
    // (U2, located by the caller).
    private bool? PathOrigin(Koto holder, out BoundOrigin? origin, out RawStep mode)
    {
        origin = null;
        mode = RawStep.Unstated;
        var source = KotoHelper.UnwrapParentheses(holder);
        for (var depth = 0; depth < 64; depth++)
        {
            BoundType? innermost = null;
            for (var type = source.BoundType; BelowLayer(type) is { } below; type = below)
            {
                innermost = type!.Semantics == SemanticsKind.Raw || this.LayerStep(type) is not null || TryPairLayer(type, out _, out _) ? type : innermost;
            }

            if (innermost is not null)
            {
                origin = innermost.RawOrigin ?? this.OuterOrigin(innermost);
                return this.Reached(innermost, ref mode);
            }

            switch (NextPathStep(source, out var next))
            {
                case PathStep.Follow when next.BoundType is { } followed && this.LayerStep(followed) is not null:
                    origin = followed.Origin;
                    return this.Reached(followed, ref mode);
                case PathStep.PairFollow when next.BoundType is { } pair:
                    origin = this.OuterOrigin(pair);
                    return this.Reached(pair, ref mode);
                case PathStep.RawFollow when next.BoundType is { } pointer:
                    origin = pointer.RawOrigin;
                    return this.Reached(pointer, ref mode);
                case PathStep.UserIndex when ElementAccess.IndexerCall(source, false)?.BoundType is { } element:
                    origin = element.Origin;
                    return this.Reached(element, ref mode);
                case PathStep.SliceElement:
                    origin = (next.BoundType?.Kind == BoundTypeKind.Slice ? next.BoundType : next.BoundType?.Components[0])?.Origin;
                    mode = RawStep.Shared;
                    return true;
                case PathStep.Receiver when source.BoundSymbol is { } receiver:
                    origin = this.ReceiverOrigin(receiver);
                    mode = RawStep.Exclusive;
                    return false;
                case PathStep.Follow or PathStep.BorrowedElement or PathStep.ArrayElement or PathStep.Member or PathStep.InlinePart:
                    source = KotoHelper.UnwrapParentheses(next);
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    // The mode of the layer a path dereferences: a raw pointer's step, a borrow's or a pair layer's; null for a pair layer whose
    // admitted cases mix modes or admit a non-borrow (U2).
    private bool? Reached(BoundType layer, ref RawStep mode)
    {
        if (layer.Semantics == SemanticsKind.Raw && layer.Kind == BoundTypeKind.Semantics)
        {
            mode = this.StepOf(layer.RawOrigin) ?? RawStep.Exclusive;
            return true;
        }

        if (this.LayerStep(layer) is not { } step)
        {
            return null;
        }

        mode = step;
        return true;
    }

    // SPEC 5.7, 6.3.4, 16.3.1 (DECISIONS BU-23): the Origin of a constructor's or destructor's receiver `self`, its Place's atom.
    private BoundOrigin ReceiverOrigin(BindingSymbol receiver) => this.OriginAtom(receiver.Declaration, OriginKind.Projection, ReceiverSlot);
}
