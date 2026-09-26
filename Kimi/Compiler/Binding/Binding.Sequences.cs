// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private bool BindSequenceMember(MemberAccessKoto source, BindingScope scope, out BoundType? result)
    {
        result = null;
        if (source.Right is not IdentifierNameKoto name || name.IdentifierName is not ("indices" or "length" or "isEmpty" or "capacity"))
        {
            return false;
        }

        var receiver = this.BindNode(source.Left, scope);
        if (ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDictionary(receiver) || FormattingTypes.IsSliceBorrow(receiver) || receiver is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Array }] })
        {
            receiver = receiver!.Components[0]; // SPEC 4.6.1: metadata shares access through a reference to the sequence.
        }

        var utf8 = FormattingTypes.IsUtf8Slice(receiver);
        if (!utf8 && receiver?.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary))
        {
            return false; // A ResolvedRange exposes its fields and computed Properties through the library struct (SPEC 4.6.3).
        }

        // SPEC 4.6.1 and 4.7.4: fixed arrays and Array expose length and indices, Slice adds isEmpty, Array adds capacity.
        var valid = name.IdentifierName switch
        {
            "indices" => !utf8 && receiver!.Kind != BoundTypeKind.Dictionary,
            "length" => true,
            "isEmpty" => receiver!.Kind is BoundTypeKind.Slice,
            "capacity" => receiver!.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary,
            _ => false,
        };
        if (!valid)
        {
            result = Fail(source, BindingFailure.MissingName);
            return true;
        }

        result = name.IdentifierName == "indices" ? this.ResolvedRangeType : name.IdentifierName == "isEmpty" ? BoundType.Boolean : BoundType.ISize;
        Complete(name, result);
        Complete(source, result);
        return true;
    }

    private BoundType? BindIteration(ForKoto source, BindingScope scope)
    {
        var iterable = this.BindNode(source.Iterable, scope);
        source.SharedIterable = null;
        source.Mode = SubjectModeOf(source.Iterable, iterable);
        if (iterable is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Slice } or { Kind: BoundTypeKind.Nominal, Symbol.LibraryDeclaration: KimiDeclarationId.ResolvedRange }] })
        {
            // SPEC 14.6.2, 3.4.1: the iteration entry is selected through the reference; the Copy handle or
            // interval is the entry receiver and is read once for the loop. Exclusive enumeration of a
            // Slice lends only the handle, so its elements stay shared.
            this.adaptations[source.Iterable] = new(ExpectedAdaptationKind.ReferentRead, iterable.Components[0]);
            iterable = iterable.Components[0];
        }

        var exclusive = source.Mode == SubjectMode.Exclusive;
        var sequence = iterable?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array && IsBarePlace(source.Iterable) ? iterable :
            ReferenceTypes.IsArray(iterable) || ReferenceTypes.IsDynamicArray(iterable) ? iterable!.Components[0] : null;
        if (sequence is not null)
        {
            // SPEC 14.6.2 subject rule: a bare array Place is shared-borrowed and a shared borrow value of an array is
            // reborrowed; both iterate as the Slice values[..], yielding ref/T during source. values@uniq and an
            // exclusive borrow value lend the array exclusively and yield uniq/T; values@move or a temporary
            // consumes the array instead.
            source.SharedIterable = exclusive
                ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [sequence], origin: this.PlaceOrigin(source.Iterable))
                : this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [sequence.Components[0]], origin: this.PlaceOrigin(source.Iterable));
        }

        var dictionary = ReferenceTypes.IsDictionary(iterable) ? iterable!.Components[0] : iterable?.Kind == BoundTypeKind.Dictionary ? iterable : null;
        if (dictionary is not null && (ReferenceTypes.IsDictionary(iterable) || IsBarePlace(source.Iterable)))
        {
            source.SharedIterable = this.InternType(BoundTypeKind.Semantics, null, exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, [dictionary], origin: iterable!.Origin ?? this.PlaceOrigin(source.Iterable));
        }

        var view = source.SharedIterable ?? iterable;
        var element = view is null ? null : view.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array ? view.Components[0] : view.Kind == BoundTypeKind.Slice
            ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [view.Components[0]], origin: view.Origin) : ReferenceTypes.IsResolvedRange(view) ? BoundType.ISize
            : exclusive && (ReferenceTypes.IsArray(view) || ReferenceTypes.IsDynamicArray(view)) ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Uniq, [view.Components[0].Components[0]], origin: view.Origin) : null;
        if (dictionary is not null)
        {
            // SPEC 14.6.2: shared Dictionary iteration yields (ref/K, ref/V) and exclusive iteration (ref/K, uniq/V),
            // including for Copy components; owned iteration yields a pair of values.
            var key = dictionary.Components[0];
            var value = dictionary.Components[1];
            if (source.SharedIterable is { } shared)
            {
                key = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [key], origin: shared.Origin);
                value = this.InternType(BoundTypeKind.Semantics, null, shared.Semantics, [value], origin: shared.Origin);
            }

            element = this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, [key, value]);
        }

        // SPEC 14.6.2: a Tuple item reached through a reference decomposes into references of the same capability.
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
                slot = this.InternType(BoundTypeKind.Semantics, null, element!.Semantics, [slot], origin: element.Origin);
            }

            name.BoundSymbol!.Type = slot;
            name.BoundSymbol.BindsReference = source.Mode != SubjectMode.ByValue;
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

        if (dictionary is null && !ReferenceTypes.IsResolvedRange(view) && view?.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array) &&
            !(exclusive && (ReferenceTypes.IsArray(view) || ReferenceTypes.IsDynamicArray(view))))
        {
            return Fail(source, BindingFailure.Unsupported);
        }

        return this.FinishResult(source, result);
    }
}
