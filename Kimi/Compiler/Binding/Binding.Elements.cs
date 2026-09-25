// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 4.6.6, 4.6.9: an element expression designates the element Place with its stored complete Type; a bare
    // read Copies a proven-Copy element, @ref borrows the slot and a fixed expected ref/T borrows it implicitly
    // (SPEC 10.2). A member or index selected below an element of a Slice or borrowed array reaches it through
    // an implicit shared borrow of that element (SPEC 3.4.1); an owned array element is projected in place.
    private BoundType? ElementPlaceType(BinaryKoto source, BoundType element)
    {
        var target = (Koto)source;
        while (target.Parent is ParenthesizedKoto parentheses)
        {
            target = parentheses;
        }

        var chain = (target.Parent is MemberAccessKoto member && ReferenceEquals(member.Left, target)) || (target.Parent is IndexKoto index && ReferenceEquals(index.Left, target));
        if (!chain || source.Left.BoundType?.Kind is BoundTypeKind.Array or BoundTypeKind.FixedArray)
        {
            return element;
        }

        var proof = this.ProveCopy(element, source);
        if (proof == ConstraintProof.Proven)
        {
            return element;
        }

        if (proof == ConstraintProof.Refuted && SharedReadTypes.BorrowSemantics(element) is { } semantics)
        {
            var origin = this.PlaceOrigin(source.Left);
            var referent = element.Semantics == SemanticsKind.Owner ? element : element.Components[0];
            return this.InternType(BoundTypeKind.Semantics, null, semantics, [referent], origin: element.Origin is { } dependency ? this.Meet(origin, dependency) : origin);
        }

        return Fail(source, BindingFailure.Unsupported);
    }

    private BoundType? BindElement(BinaryKoto source, BindingScope scope)
    {
        var receiver = this.BindNode(source.Left, scope);
        if (source is IndexKoto)
        {
            if (receiver?.Kind == BoundTypeKind.Dictionary)
            {
                var key = receiver.Components[0];
                var actual = this.BindNode(source.Right, scope, key);
                // A lookup borrows K itself, including when K is a reference value.
                // An existing ref/K is reborrowed; it is never read into a key snapshot.
                var written = source.Right.BoundType;
                if (written is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } && !FitsType(written, key) && FitsType(written.Components[0], key))
                {
                    this.adaptations.Remove(source.Right);
                    actual = written.Components[0];
                }

                if (actual is null || !this.CheckTypeUse(actual, key, source.Right))
                {
                    return Fail(source, BindingFailure.TypeMismatch);
                }

                ((IndexKoto)source).DictionaryKeyReference = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [key], origin: this.PlaceOrigin(source.Right));
                return Complete(source, receiver.Components[1]);
            }

            if (ReferenceTypes.IsPointer(receiver))
            {
                // SPEC 5.3: p[n] is *(p + n), with a signed offset and no range/from-end form.
                var offset = this.RequireType(source.Right, scope, BoundType.ISize);
                return offset is not null && FitsType(offset, BoundType.ISize) &&
                    KotoHelper.UnwrapParentheses(source.Right) is not (RangeKoto or FromEndIndexKoto) &&
                    !this.HasZeroStride(receiver!.Components[0])
                    ? Complete(source, receiver!.Components[0]) : Fail(source, BindingFailure.TypeMismatch);
            }

            if ((ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDynamicArray(receiver)) && source.Right is not RangeKoto)
            {
                this.RequireType(source.Right, scope, BoundType.ISize);
                return Complete(source, this.ElementPlaceType(source, receiver!.Components[0].Components[0]));
            }

            var sequence = ReferenceTypes.IsDynamicArray(receiver) ? receiver!.Components[0] : receiver;
            if (sequence?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array && source.Right is RangeKoto range && !range.IsInclusive)
            {
                if (range.Start is { } start)
                {
                    this.RequireType(start, scope, BoundType.ISize);
                }

                if (range.End is { } end)
                {
                    this.RequireType(end, scope, BoundType.ISize);
                }

                Complete(range, BoundType.Range);
                return Complete(source, this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [sequence.Components[0]], origin: this.PlaceOrigin(source.Left)));
            }

            if (receiver?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array && source.Right is not RangeKoto)
            {
                this.RequireType(source.Right, scope, BoundType.ISize);
                return Complete(source, this.ElementPlaceType(source, receiver.Components[0]));
            }

            if (receiver is not { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } && !ReferenceEquals(receiver, BoundType.Never))
            {
                this.BindNode(source.Right, scope);
                return Fail(source, BindingFailure.Unsupported);
            }

            this.RequireType(source.Right, scope, BoundType.ISize);
        }
        else
        {
            // A tuple selector is syntax, not a separately evaluated integer operand.
            Complete(source.Right, BoundType.ISize);
            if (ReferenceTypes.IsTuple(receiver))
            {
                return ElementAccess.TryBorrowedTupleElement(source, out var borrowedElement, out _)
                    ? Complete(source, borrowedElement) : Fail(source, BindingFailure.TypeMismatch);
            }
        }

        if (ReferenceEquals(receiver, BoundType.Never) || ReferenceEquals(source.Right.BoundType, BoundType.Never))
        {
            return Complete(source, BoundType.Never);
        }

        if (!ElementAccess.TryType(source, out var element, out _))
        {
            return Fail(source, receiver?.Kind == BoundTypeKind.Tuple ? BindingFailure.TypeMismatch : BindingFailure.Unsupported);
        }

        return Complete(source, element);
    }
}
