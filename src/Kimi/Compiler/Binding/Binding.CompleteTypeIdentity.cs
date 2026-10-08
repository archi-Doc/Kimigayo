// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundType Left, BoundType Right), bool> completeTypeIdentities = new();

    // Complete Types keep exact free Origins. Function Types quantify their own input Origins;
    // their ordinary bidirectional contract relation compares those binders by position.
    // Memoize pairs so nested constructions over shared graphs do not expand into trees.
    private bool SameCompleteType(BoundType left, BoundType right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        var pair = (left, right);
        if (this.completeTypeIdentities.TryGetValue(pair, out var equal))
        {
            return equal;
        }

        equal = left.Kind != BoundTypeKind.Primitive && left.Kind == right.Kind && left.Symbol == right.Symbol &&
            left.Semantics == right.Semantics && left.ResultMode == right.ResultMode && left.Length == right.Length &&
            ReferenceEquals(left.LengthExpression, right.LengthExpression) && ReferenceEquals(left.ClosureContext, right.ClosureContext) &&
            left.LengthArguments.AsSpan().SequenceEqual(right.LengthArguments) && ReferenceEquals(left.Origin, right.Origin) &&
            ((BoundOrigin[])left.OriginArguments).AsSpan().SequenceEqual((BoundOrigin[])right.OriginArguments) && left.Components.Count == right.Components.Count;
        if (equal && left.Kind == BoundTypeKind.Function)
        {
            equal = FitsType(left, right) && FitsType(right, left);
        }
        else
        {
            for (var i = 0; equal && i < left.Components.Count; i++)
            {
                equal = this.SameCompleteType(left.Components[i], right.Components[i]);
            }
        }

        this.completeTypeIdentities[pair] = equal;
        return equal;
    }
}
