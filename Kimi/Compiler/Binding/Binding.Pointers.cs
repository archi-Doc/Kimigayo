// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Prove only zero storage from logical Types (SPEC 21.1). Unknown/generic or
    // recursive layouts remain subject to concrete layout validation at generation.
    private bool HasZeroStride(BoundType type, int depth = 0)
    {
        if (ReferenceEquals(type, BoundType.Unit))
        {
            return true;
        }

        if (depth >= 64 || type.Semantics != SemanticsKind.Owner)
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.FixedArray)
        {
            return (type.LengthExpression is null && type.Length == 0) || this.HasZeroStride(type.Components[0], depth + 1);
        }

        if (type.Kind == BoundTypeKind.Tuple)
        {
            foreach (var element in type.Components)
            {
                if (!this.HasZeroStride(element, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        if (type.Symbol?.Declaration is StructKoto declaration && this.storageShapes.TryGetValue(declaration, out var shape))
        {
            // StorageShape includes direct bases, as well as stored fields. Resolve
            // them in this instantiation, not the generic declaration's context.
            foreach (var syntax in shape.Types)
            {
                if (this.StoredType(syntax, type) is not { } field || !this.HasZeroStride(field, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }
}
