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
                var borrowedElement = receiver!.Components[0].Components[0];
                return Complete(source, ReferenceTypes.IsDynamicArray(receiver) ? this.SequenceReadType(source, borrowedElement) : borrowedElement);
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
                return Complete(source, this.SequenceReadType(source, receiver.Components[0]));
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

    private BoundType SequenceReadType(BinaryKoto source, BoundType element)
    {
        // SPEC 4.6.6: a concrete Non-Copy struct is read by shared storage borrow.
        // Chained projections, assignment and explicit acquisition instead retain the Place.
        var target = (Koto)source;
        while (target.Parent is ParenthesizedKoto parentheses)
        {
            target = parentheses;
        }

        var place = target.Parent is ConversionKoto || (source.Left.BoundType?.Kind == BoundTypeKind.Array && target.Parent is MemberAccessKoto member && ReferenceEquals(member.Left, target)) ||
            (target.Parent is BinaryKoto assignment && ReferenceEquals(assignment.Left, target) && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals);
        return !place && StructStorage.IsStruct(element) && this.ProveCopy(element, source) == ConstraintProof.Refuted
            ? this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [element], origin: this.PlaceOrigin(source.Left))
            : element;
    }
}
