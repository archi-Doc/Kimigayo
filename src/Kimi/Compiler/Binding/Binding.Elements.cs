// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BoundType? BindElement(BinaryKoto source, BindingScope scope)
    {
        var receiver = this.BindNode(source.Left, scope);
        if (source is IndexKoto)
        {
            var dictionary = ReferenceTypes.IsDictionary(receiver) ? receiver!.Components[0] : receiver;
            if (dictionary?.Kind == BoundTypeKind.Dictionary)
            {
                var key = dictionary.Components[0];
                var actual = this.BindNode(source.Right, scope, key);
                // A lookup borrows K itself, including when K is a reference value.
                // An existing ref/K is reborrowed; it is never read into a key snapshot.
                var written = source.Right.BoundType;
                if (written is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && !FitsType(written, key) && FitsType(written.Components[0], key))
                {
                    this.adaptations.Remove(source.Right);
                    actual = written.Components[0];
                }

                if (actual is null)
                {
                    return this.CompleteDependent(source, source.Right); // The key's own failure explains the lookup.
                }

                if (!this.CheckTypeUse(actual, key, source.Right))
                {
                    return this.FailMismatch(source, source.Right, actual, key);
                }

                ((IndexKoto)source).DictionaryKeyReference = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [key], origin: this.PlaceOrigin(source.Right));
                return Complete(source, dictionary.Components[1]);
            }

            if (ReferenceTypes.IsPointer(receiver))
            {
                // SPEC 5.3: p[n] is *(p + n), with a signed offset and no range/from-end form; a zero stride makes it *p.
                // A key that is not an isize, including a range or `^x`, is reported at the key; the access rests on it.
                var offset = this.RequireType(source.Right, scope, BoundType.ISize);
                return offset is not null && FitsType(offset, BoundType.ISize) &&
                    KotoHelper.UnwrapParentheses(source.Right) is not (RangeKoto or FromEndIndexKoto)
                    ? Complete(source, receiver!.Components[0]) : this.CompleteDependent(source, source.Right);
            }

            if (receiver is not null && this.TryBindKeyedSelection((IndexKoto)source, scope, receiver, out var keyed))
            {
                return keyed; // SPEC 4.6.1: a range, ResolvedRange or resolved position key.
            }

            // SPEC 4.6.9: the key of a sequence below is an integer of any Type or a written `^x`, already bound; ownership
            // converts it to an isize element position within the element access's bounds check.
            if ((ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDynamicArray(receiver)) && source.Right is not RangeKoto)
            {
                // SPEC 4.6.9: the element Place keeps its stored complete Type.
                return Complete(source, receiver!.Components[0].Components[0]);
            }

            if (receiver?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array && source.Right is not RangeKoto)
            {
                return Complete(source, receiver.Components[0]); // SPEC 4.6.6: the shared element Place.
            }

            if (this.TryBindIndexer((IndexKoto)source, scope, receiver, out var indexed))
            {
                return indexed; // SPEC 4.6.9: a user Indexable conformance.
            }

            if (receiver is not { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } && !ReferenceEquals(receiver, BoundType.Never))
            {
                this.BindNode(source.Right, scope);
                if (receiver is null)
                {
                    return this.CompleteDependent(source, source.Left); // The receiver's own failure explains the access.
                }

                // SPEC 4.6.9: a sequence reached here, such as through a reference, is an implementation limit; any other
                // receiver, including a range key on an Indexable Type, cannot be indexed by the key.
                var core = receiver;
                while (core is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
                {
                    core = core.Components[0];
                }

                var sequence = core.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary;
                return this.Fail(source, sequence ? BindingFailure.Unsupported : BindingFailure.NotIndexable);
            }

            if (ReferenceEquals(receiver, BoundType.Never))
            {
                this.BindNode(source.Right, scope);
            }

            this.ReceiverElement(source.Left, receiver);
        }
        else
        {
            // A tuple selector is syntax, not a separately evaluated integer operand.
            Complete(source.Right, BoundType.ISize);
            receiver = this.ReceiverThroughLayers(source.Left, receiver) ?? receiver;
            this.ReceiverElement(source.Left, receiver);
            if (this.adaptations.TryGetValue(source.Left, out var selection) && selection.Kind == ExpectedAdaptationKind.SharedBorrow)
            {
                receiver = selection.Type;
            }

            if (ReferenceTypes.IsTuple(receiver))
            {
                return ElementAccess.TryBorrowedTupleElement(source, out var borrowedElement, out _)
                    ? Complete(source, borrowedElement) : this.Fail(source, BindingFailure.TypeMismatch);
            }
        }

        if (ReferenceEquals(receiver, BoundType.Never) || ReferenceEquals(source.Right.BoundType, BoundType.Never))
        {
            return Complete(source, BoundType.Never);
        }

        if (!ElementAccess.TryType(source, out var element, out _))
        {
            return this.Fail(source, receiver?.Kind == BoundTypeKind.Tuple ? BindingFailure.TypeMismatch : BindingFailure.Unsupported);
        }

        return Complete(source, element);
    }
}
