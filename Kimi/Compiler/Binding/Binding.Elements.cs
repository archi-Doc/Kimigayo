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
            if (ReferenceTypes.IsArray(receiver) && source.Right is not RangeKoto)
            {
                this.RequireType(source.Right, scope, BoundType.ISize);
                return Complete(source, receiver!.Components[0].Components[0]);
            }

            if (receiver?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice && source.Right is RangeKoto range && !range.IsInclusive)
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
                return Complete(source, this.InternType(BoundTypeKind.Slice, null, SemanticsKind.Owner, [receiver.Components[0]], origin: this.PlaceOrigin(source.Left)));
            }

            if (receiver?.Kind == BoundTypeKind.Slice)
            {
                this.RequireType(source.Right, scope, BoundType.ISize);
                return Complete(source, receiver.Components[0]);
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
