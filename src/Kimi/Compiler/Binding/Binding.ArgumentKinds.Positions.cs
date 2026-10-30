// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument positions (SPEC 10.5.1, 4.3.1, 14.9.2; DECISIONS BU-2 U3, BU-43): the parameter positions that the parts of an argument
// occupy, which the evidence walk (SlotSolution.InferPosition), the defaults (SlotSolution.DefaultPosition) and the whole Type at a bare
// slot (IndependentPositionType) recurse into.
// - Responsibility: a position is the argument parts at one parameter position, a range of positionParts: a call argument or a Case
//   payload; each element of a Tuple or array literal part, which SPEC 10.5.1 classifies one by one at its component or element Type;
//   and a control-flow part its call classifies by element (ClassifiedByCall; the owner, 2026-10-26): its one child position is its
//   results at its own parameter Type, the parts of a join that supplies no candidate (Binding.ResultPropagation.Parts.cs), whose
//   child positions are formed part by part (SPEC 14.9.2). A position of several parts has one kind (PositionKind): Join when a
//   part waits, Typed at its typed parts' common Type (one evidence), Aggregate when its parts are aggregates, Literal when only
//   literals remain; with no part (Never in every source) it is Typed with no Type: no evidence, and no Type at a slot that takes the
//   whole (SPEC 10.8, BU-2 U3's D6).
// - Outputs: OpenPosition and ClosePosition, a walk's position of one part; ChildPositions and AddChildPosition, a position's child
//   positions and the parameter Type at each; PositionKind.
// - Invariants: a walk removes the parts it appended before it returns, so nested walks share the list; a position's parts end where
//   the list ends when its walk begins.
// - Prohibitions: no fit, evidence or record here; the walks decide what a position's parts are to their candidate.
public sealed partial class Binding
{
    private readonly List<Koto> positionParts = new();

    private static BoundType? ChildPattern(Koto shape, int index, BoundType? pattern) => shape switch
    {
        TupleLiteralKoto tuple => pattern is { Kind: BoundTypeKind.Tuple } && pattern.Components.Count == tuple.Elements.Count
            ? pattern.Components[index] : null,
        ArrayLiteralKoto => pattern is { Kind: BoundTypeKind.Array or BoundTypeKind.FixedArray } ? pattern.Components[0] : null,
        _ => pattern is { Kind: BoundTypeKind.Dictionary, Components.Count: 2 } ? pattern.Components[index] : null,
    };

    // A walk's position of one argument part; ClosePosition removes it with every part appended after it.
    private int OpenPosition(Koto part)
    {
        var start = this.positionParts.Count;
        this.positionParts.Add(part);
        return start;
    }

    private void ClosePosition(int start) => this.positionParts.RemoveRange(start, this.positionParts.Count - start);

    // The child positions of [start, end): of one part, its Tuple or array literal's elements, or the one position of a join's results
    // its call classifies; of several parts, a join's, theirs part by part.
    private int ChildPositions(int start, int end)
    {
        if (end - start != 1)
        {
            return this.PartsShape(start, end) is { } shape ? ShapeChildren(shape) : 0;
        }

        var part = KotoHelper.UnwrapParentheses(this.positionParts[start]);
        return part switch
        {
            TupleLiteralKoto tuple => tuple.Elements.Count,
            ArrayLiteralKoto array => array.Elements.Count,
            _ => this.ComposedJoin(part) is null ? 0 : 1,
        };
    }

    // Appends the parts of child position `index` of [start, end) and gives the parameter Type at it: a Tuple element's component, an
    // array element's element Type, a Dictionary key's or value's, a join's results its own; null where the pattern's shape differs.
    private BoundType? AddChildPosition(int start, int end, int index, BoundType? pattern)
    {
        if (end - start != 1)
        {
            var shape = this.PartsShape(start, end)!;
            this.AddPartsAt(start, end, index);
            return ChildPattern(shape, index, pattern);
        }

        var part = KotoHelper.UnwrapParentheses(this.positionParts[start]);
        switch (part)
        {
            case TupleLiteralKoto tuple:
                this.positionParts.Add(tuple.Elements[index]);
                return ChildPattern(tuple, index, pattern);
            case ArrayLiteralKoto array:
                this.positionParts.Add(array.Elements[index]);
                return ChildPattern(array, index, pattern);
            default:
                this.AddResults(this.ComposedJoin(part)!);
                return pattern;
        }
    }

    // The join of a control-flow part its call classifies by element (ClassifiedByCall), or null.
    private Koto? ComposedJoin(Koto part) => this.KindOf(part) == PartKind.Join ? ControlFlowExpression(part) : null;

    // The kind of a position of several parts, a join's: Join when a part waits for its call, Typed at the common Type of its typed
    // parts (literals of it beside them), Aggregate when its parts are aggregates, Literal when only literals remain; with no part, Typed
    // with no Type.
    private PartKind PositionKind(int start, int end, out BoundType? type)
    {
        type = null;
        var literal = false;
        for (var i = start; i < end; i++)
        {
            var part = this.positionParts[i];
            if (IsUnfittedLiteral(part))
            {
                literal = true;
            }
            else if (!this.IsAggregatePart(part) && part.BindingState != BindingState.Resolved)
            {
                return PartKind.Join;
            }
        }

        if (end == start || (type = this.PositionType(start, end, this.ConstraintScope(this.positionParts[start]), out var typed)) is not null || typed)
        {
            return PartKind.Typed;
        }

        return this.PartsShape(start, end) is not null ? PartKind.Aggregate : literal ? PartKind.Literal : PartKind.Typed;
    }

    // The first literal part of [start, end), whose default a Literal position takes (SPEC 10.5.1, 10.2.1).
    private Koto FirstLiteral(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (IsUnfittedLiteral(this.positionParts[i]))
            {
                return this.positionParts[i];
            }
        }

        throw new InvalidOperationException("A Literal position holds a literal.");
    }
}
