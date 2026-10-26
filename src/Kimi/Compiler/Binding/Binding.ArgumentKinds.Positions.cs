// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument positions (SPEC 10.5.1, 4.3.1; DECISIONS BU-2 U3): the parameter positions that the parts of an argument occupy, which the
// evidence walk (SlotSolution.InferPosition), the defaults (SlotSolution.DefaultPosition) and the whole Type at a bare slot
// (IndependentPositionType) recurse into.
// - Responsibility: a position is the argument parts at one parameter position, a range of positionParts: a call argument or a Case
//   payload, and each element of a Tuple or array literal part, which SPEC 10.5.1 classifies one by one at its component or element
//   Type.
// - Outputs: OpenPosition and ClosePosition, a walk's position of one part; ChildPositions, how many child positions a position has;
//   AddChildPosition, which appends one child's parts and gives the parameter Type at it.
// - Invariants: a walk removes the parts it appended before it returns, so nested walks share the list.
// - Prohibitions: no kind, fit or evidence here; the walks decide what a position's parts are to their candidate.
public sealed partial class Binding
{
    private readonly List<Koto> positionParts = new();

    // A walk's position of one argument part; ClosePosition removes it with every part appended after it.
    private int OpenPosition(Koto part)
    {
        var start = this.positionParts.Count;
        this.positionParts.Add(part);
        return start;
    }

    private void ClosePosition(int start) => this.positionParts.RemoveRange(start, this.positionParts.Count - start);

    // The child positions of the position at `start`: one per element of its Tuple or array literal part (a fill has its one element).
    private int ChildPositions(int start)
        => KotoHelper.UnwrapParentheses(this.positionParts[start]) switch
        {
            TupleLiteralKoto tuple => tuple.Elements.Count,
            ArrayLiteralKoto array => array.Elements.Count,
            _ => 0,
        };

    // Appends the parts of child position `index` of the position at `start` and gives the parameter Type at it: a Tuple element's
    // component, an array element's element Type; null where the pattern's shape does not match.
    private BoundType? AddChildPosition(int start, int index, BoundType? pattern)
    {
        if (KotoHelper.UnwrapParentheses(this.positionParts[start]) is TupleLiteralKoto tuple)
        {
            this.positionParts.Add(tuple.Elements[index]);
            return pattern is { Kind: BoundTypeKind.Tuple } && pattern.Components.Count == tuple.Elements.Count ? pattern.Components[index] : null;
        }

        var array = (ArrayLiteralKoto)KotoHelper.UnwrapParentheses(this.positionParts[start]);
        this.positionParts.Add(array.Elements[index]);
        return pattern is { Kind: BoundTypeKind.Array or BoundTypeKind.FixedArray } ? pattern.Components[0] : null;
    }
}
