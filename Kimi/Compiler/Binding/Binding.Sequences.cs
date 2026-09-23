// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private bool BindSequenceMember(MemberAccessKoto source, BindingScope scope, out BoundType? result)
    {
        result = null;
        if (source.Right is not IdentifierNameKoto name || name.IdentifierName is not ("indices" or "length" or "isEmpty" or "capacity" or "start" or "end"))
        {
            return false;
        }

        var receiver = this.BindNode(source.Left, scope);
        if (ReferenceTypes.IsArray(receiver) || receiver is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Array }] })
        {
            receiver = receiver!.Components[0]; // SPEC 4.6.1: metadata shares access through a reference to the sequence.
        }

        if (receiver?.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.ResolvedRange or BoundTypeKind.Slice or BoundTypeKind.Array))
        {
            return false;
        }

        // SPEC 4.6.1 and 4.7.4: fixed arrays and Array expose length and indices, Slice adds isEmpty, Array adds capacity.
        var range = receiver.Kind == BoundTypeKind.ResolvedRange;
        var valid = name.IdentifierName switch
        {
            "indices" => !range,
            "length" => true,
            "isEmpty" => receiver.Kind is BoundTypeKind.Slice or BoundTypeKind.ResolvedRange,
            "capacity" => receiver.Kind == BoundTypeKind.Array,
            _ => range,
        };
        if (!valid)
        {
            result = Fail(source, BindingFailure.MissingName);
            return true;
        }

        result = name.IdentifierName == "indices" ? BoundType.ResolvedRange : name.IdentifierName == "isEmpty" ? BoundType.Boolean : BoundType.ISize;
        Complete(name, result);
        Complete(source, result);
        return true;
    }

    private BoundType? BindIteration(ForKoto source, BindingScope scope)
    {
        var iterable = this.BindNode(source.Iterable, scope);
        source.SharedIterable = null;
        if (iterable?.Kind == BoundTypeKind.FixedArray && IsBarePlace(source.Iterable))
        {
            // SPEC 14.6.2 subject rule: a bare fixed-array Place is shared-borrowed and iterated as the
            // Slice values[..], yielding ref{source}/T; values@move or a temporary consumes the array.
            source.SharedIterable = this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [iterable.Components[0]], origin: this.PlaceOrigin(source.Iterable));
        }

        var view = source.SharedIterable ?? iterable;
        var element = view?.Kind == BoundTypeKind.FixedArray ? view.Components[0] : view?.Kind == BoundTypeKind.Slice
            ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [view.Components[0]], origin: view.Origin) : BoundType.ISize;
        var result = this.BeginResult(source, scope, BoundType.Unit);
        var duplicate = false;
        for (var i = 0; i < source.Bindings.Count; i++)
        {
            var name = source.Bindings[i];
            duplicate |= name.BoundSymbol!.Next is not null;
            var slot = source.IsTupleBinding && element.Kind == BoundTypeKind.Tuple && i < element.Components.Count ? element.Components[i] : element;
            name.BoundSymbol!.Type = slot;
            Complete(name, slot);
        }

        this.BindNode(source.Body, scope);
        if (duplicate)
        {
            return Fail(source, BindingFailure.Duplicate);
        }

        if (source.IsTupleBinding && (element.Kind != BoundTypeKind.Tuple || element.Components.Count != source.Bindings.Count))
        {
            return Fail(source, BindingFailure.TypeMismatch);
        }

        if (iterable?.Kind is not (BoundTypeKind.ResolvedRange or BoundTypeKind.FixedArray or BoundTypeKind.Slice))
        {
            return Fail(source, BindingFailure.Unsupported);
        }

        return this.FinishResult(source, result);
    }
}
