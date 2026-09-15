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
