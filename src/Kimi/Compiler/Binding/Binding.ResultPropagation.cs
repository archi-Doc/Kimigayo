// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Result propagation (SPEC 14.9.2 step 3, 3.8; DECISIONS R4-M52, BU-4): a literal takes its position's Type.
// - Responsibility: the Type a Tuple literal takes from its elements' Types at its position's Type (FittedTupleType, α: an element of
//   Type Never takes its position's element Type, every other element keeps its own Type), read where the position is known when the
//   literal is bound (BindTuple).
// - Inputs: the elements' bound Types and the position's Type, if any.
// - Outputs: the interned Tuple Type.
// - Invariants: the elements keep their own Types; only the literal's Type takes its position's element Type for a Never element.
public sealed partial class Binding
{
    // SPEC 3.8: a Never element fits its expected element Type, which the literal's Type takes; `elements` holds the elements' Types and
    // receives the fitted ones.
    private BoundType FittedTupleType(Span<BoundType> elements, BoundType? expected)
    {
        for (var i = 0; i < elements.Length; i++)
        {
            if (ReferenceEquals(elements[i], BoundType.Never) && expected is { Kind: BoundTypeKind.Tuple } && expected.Components.Count == elements.Length)
            {
                elements[i] = expected.Components[i];
            }
        }

        return this.types.Tuple(elements);
    }
}
