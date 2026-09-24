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
        if (ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDictionary(receiver) || FormattingTypes.IsSliceBorrow(receiver) || receiver is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Array }] })
        {
            receiver = receiver!.Components[0]; // SPEC 4.6.1: metadata shares access through a reference to the sequence.
        }

        var utf8 = FormattingTypes.IsUtf8Slice(receiver);
        if (!utf8 && receiver?.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.ResolvedRange or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary))
        {
            return false;
        }

        // SPEC 4.6.1 and 4.7.4: fixed arrays and Array expose length and indices, Slice adds isEmpty, Array adds capacity.
        var range = receiver!.Kind == BoundTypeKind.ResolvedRange;
        var valid = name.IdentifierName switch
        {
            "indices" => !range && !utf8 && receiver.Kind != BoundTypeKind.Dictionary,
            "length" => true,
            "isEmpty" => receiver.Kind is BoundTypeKind.Slice or BoundTypeKind.ResolvedRange,
            "capacity" => receiver.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary,
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
        var iterable = this.RejectExclusiveSubject(source.Iterable, this.BindNode(source.Iterable, scope));
        source.SharedIterable = null;
        if (iterable is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Slice or BoundTypeKind.ResolvedRange }] })
        {
            // SPEC 14.6.2: a borrowed Slice or ResolvedRange is read as its Copy value and iterated as that value.
            iterable = this.ReadReferent(source.Iterable, iterable);
        }

        var sequence = iterable?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array && IsBarePlace(source.Iterable) ? iterable :
            ReferenceTypes.IsArray(iterable) || ReferenceTypes.IsDynamicArray(iterable) ? iterable!.Components[0] : null;
        if (sequence is not null)
        {
            // SPEC 14.6.2 subject rule: a bare array Place is shared-borrowed and a borrow value of an array is
            // shared-reborrowed; both iterate as the Slice values[..], yielding ref/T during source. values@move
            // or a temporary consumes the array instead.
            source.SharedIterable = this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [sequence.Components[0]], origin: this.PlaceOrigin(source.Iterable));
        }

        var dictionary = ReferenceTypes.IsDictionary(iterable) ? iterable!.Components[0] : iterable?.Kind == BoundTypeKind.Dictionary ? iterable : null;
        if (dictionary is not null && (ReferenceTypes.IsDictionary(iterable) || IsBarePlace(source.Iterable)))
        {
            source.SharedIterable = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [dictionary], origin: iterable!.Origin ?? this.PlaceOrigin(source.Iterable));
        }

        var view = source.SharedIterable ?? iterable;
        var element = view is null ? null : view.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array ? view.Components[0] : view.Kind == BoundTypeKind.Slice
            ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [view.Components[0]], origin: view.Origin) : view.Kind == BoundTypeKind.ResolvedRange ? BoundType.ISize : null;
        if (dictionary is not null)
        {
            // SPEC 14.6.2: shared Dictionary iteration yields a pair of references,
            // including for Copy components; owned iteration yields a pair of values.
            var key = dictionary.Components[0];
            var value = dictionary.Components[1];
            if (source.SharedIterable is { } shared)
            {
                key = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [key], origin: shared.Origin);
                value = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [value], origin: shared.Origin);
            }

            element = this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, [key, value]);
        }

        var sharedTuple = source.IsTupleBinding && ReferenceTypes.IsTuple(element);
        var tuple = sharedTuple ? element!.Components[0] : element;
        var result = this.BeginResult(source, scope, BoundType.Unit);
        var duplicate = false;
        for (var i = 0; i < source.Bindings.Count; i++)
        {
            var name = source.Bindings[i];
            duplicate |= name.BoundSymbol!.Next is not null;
            var slot = source.IsTupleBinding && tuple?.Kind == BoundTypeKind.Tuple && i < tuple.Components.Count ? tuple.Components[i] : element;
            if (sharedTuple && slot is not null)
            {
                slot = this.SharedReadType(slot, element!.Origin!, name);
            }

            name.BoundSymbol!.Type = slot;
            Complete(name, slot);
        }

        this.BindNode(source.Body, scope);
        if (duplicate)
        {
            return Fail(source, BindingFailure.Duplicate);
        }

        if (iterable is null)
        {
            return Complete(source, null);
        }

        if (source.IsTupleBinding && (tuple?.Kind != BoundTypeKind.Tuple || tuple.Components.Count != source.Bindings.Count))
        {
            return Fail(source, BindingFailure.TypeMismatch);
        }

        if (dictionary is null && view?.Kind is not (BoundTypeKind.ResolvedRange or BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array))
        {
            return Fail(source, BindingFailure.Unsupported);
        }

        return this.FinishResult(source, result);
    }
}
