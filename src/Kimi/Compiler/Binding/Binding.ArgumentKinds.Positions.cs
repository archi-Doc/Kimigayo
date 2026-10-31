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
//   child positions are its aggregate parts' by index (SPEC 10.5.1: an array result's too, each index holding the elements of the
//   sources that have it; the owner, 2026-10-31), while its Type is formed with an array's one element position (14.9.2 step 2,
//   AddPartsAt). AddResultPart forms every part of a child position (Never dropped, the
//   forms that deliver it passed, a waiting join flattened). A position of several parts has one kind (PositionKind), typed first
//   (SPEC 14.9.2 step 2): Typed at its typed parts' common Type (one evidence) whatever else it holds, its literals and the parts
//   awaiting their call beside them taking it (step 3), or with no Type when a part failed; only a position with no typed part, and so
//   no candidate, waits (Call) when a part awaits its call; then Aggregate when its parts are aggregates, Literal when only literals
//   remain; with no part (Never in every source) it is Typed with no Type: no evidence, and no Type at a slot that takes the whole
//   (SPEC 10.8, BU-2 U3's D6). Every reader (the evidence, the defaults, IndependentPositionType, the fit) takes the kind from it.
// - Outputs: OpenPosition and ClosePosition, a walk's position of one part; ChildPositions and AddChildPosition, a position's child
//   positions and the parameter Type at each; PositionKind.
// - Invariants: a walk removes the parts it appended before it returns, so nested walks share the list; a position's parts end where
//   the list ends when its walk begins.
// - Prohibitions: no fit, evidence or record here; the walks decide what a position's parts are to their candidate.
public sealed partial class Binding
{
    private readonly List<Koto> positionParts = new();

    // SPEC 3.8, 10.5.1: whether a Tuple literal has the Type's shape, a Tuple of its length: the one Tuple shape rule of the walks, the
    // fit, the late Type, BindTuple and the shape evidence (arrays: ArrayShapeAt).
    private static bool TupleShapeAt(TupleLiteralKoto tuple, BoundType? type)
        => type is { Kind: BoundTypeKind.Tuple } && type.Components.Count == tuple.Elements.Count;

    private static BoundType? ChildPattern(Koto shape, int index, BoundType? pattern) => shape switch
    {
        TupleLiteralKoto tuple => TupleShapeAt(tuple, pattern) ? pattern!.Components[index] : null,
        ArrayLiteralKoto => pattern is { Kind: BoundTypeKind.Array or BoundTypeKind.FixedArray } ? pattern.Components[0] : null,
        _ => pattern is { Kind: BoundTypeKind.Dictionary, Components.Count: 2 } ? pattern.Components[index] : null,
    };

    // SPEC 10.5.1 (the owner, 2026-10-31, reading I): an aggregate literal part's child positions by index, as an argument's elements
    // are classified one by one: a Tuple's or array's elements, a Dictionary's key and value.
    private static int IndexedChildren(Koto part) => part switch
    {
        TupleLiteralKoto tuple => tuple.Elements.Count,
        ArrayLiteralKoto array => array.Elements.Count,
        _ => 2,
    };

    // A walk's position of one argument part; ClosePosition removes it with every part appended after it.
    private int OpenPosition(Koto part)
    {
        var start = this.positionParts.Count;
        this.positionParts.Add(part);
        return start;
    }

    private void ClosePosition(int start) => this.positionParts.RemoveRange(start, this.positionParts.Count - start);

    // The child positions of [start, end), by index (SPEC 10.5.1; the owner, 2026-10-31): of one part, its Tuple or array literal's
    // elements, or the one position of a join's results its call classifies; of several parts, a join's, those of its aggregate parts
    // (IndexedChildren), so that arrays of unequal length have the longest one's.
    private int ChildPositions(int start, int end)
    {
        if (end - start != 1)
        {
            var count = 0;
            for (var i = start; i < end; i++)
            {
                if (this.IsAggregatePart(this.positionParts[i]))
                {
                    count = Math.Max(count, IndexedChildren(this.positionParts[i]));
                }
            }

            return count;
        }

        var part = KotoHelper.UnwrapParentheses(this.positionParts[start]);
        return part is TupleLiteralKoto or ArrayLiteralKoto ? IndexedChildren(part) : this.ComposedJoin(part) is null ? 0 : 1;
    }

    // Appends the parts of child position `index` of [start, end), each part's there (AddIndexedChild), and gives the parameter Type at
    // it: a Tuple element's component, an array element's element Type, a Dictionary key's or value's, a join's results its own; null
    // where the pattern's shape differs.
    private BoundType? AddChildPosition(int start, int end, int index, BoundType? pattern)
    {
        if (end - start != 1)
        {
            for (var i = start; i < end; i++)
            {
                if (this.IsAggregatePart(this.positionParts[i]))
                {
                    this.AddIndexedChild(this.positionParts[i], index);
                }
            }

            return ChildPattern(this.PartsShape(start, end)!, index, pattern);
        }

        var part = KotoHelper.UnwrapParentheses(this.positionParts[start]);
        if (part is TupleLiteralKoto or ArrayLiteralKoto)
        {
            this.AddIndexedChild(part, index);
            return ChildPattern(part, index, pattern);
        }

        this.AddResults(this.ComposedJoin(part)!);
        return pattern;
    }

    // Appends a part's child `index` (IndexedChildren), formed by AddResultPart: its element there, none for an array without one, a
    // Dictionary's keys or values.
    private void AddIndexedChild(Koto part, int index)
    {
        switch (part)
        {
            case TupleLiteralKoto tuple:
                this.AddResultPart(tuple.Elements[index]);
                break;
            case ArrayLiteralKoto array when index < array.Elements.Count:
                this.AddResultPart(array.Elements[index]);
                break;
            case DictionaryLiteralKoto dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    this.AddResultPart(index == 0 ? entry.Key : entry.Value);
                }

                break;
        }
    }

    // The join of a control-flow part its call completes by its results: a Join (ClassifiedByCall), or a nested join that waits for its
    // position (WaitingResult), walked as a result source's is (AddResultPart); null otherwise.
    private Koto? ComposedJoin(Koto part)
        => this.KindOf(part) == PartKind.Join || this.WaitingResult(PartCore(part)) is not null ? ControlFlowExpression(part) : null;

    // The kind of a position of several parts, a join's, typed first (SPEC 14.9.2 step 2): Typed at the common Type of its typed parts,
    // whatever else it holds, or with no Type when a part failed or it has no part; with no typed part, Call when a part awaits its call
    // (AwaitsCall), then Aggregate when its parts are aggregates, then Literal when only literals remain.
    private PartKind PositionKind(int start, int end, out BoundType? type)
    {
        type = null;
        var literal = false;
        var waits = false;
        for (var i = start; i < end; i++)
        {
            var part = this.positionParts[i];
            if (IsUnfittedLiteral(part))
            {
                literal = true;
            }
            else if (!this.IsAggregatePart(part) && this.AwaitsCall(part))
            {
                waits = true;
            }
            else if (!this.IsAggregatePart(part) && this.PartFailed(part))
            {
                return PartKind.Typed; // A failed part gives no Type.
            }
        }

        if (end == start || (type = this.PositionType(start, end, this.ConstraintScope(this.positionParts[start]), out var typed)) is not null || typed)
        {
            return PartKind.Typed;
        }

        return (waits, literal) switch
        {
            (true, _) => PartKind.Call,
            _ when this.PartsShape(start, end) is not null => PartKind.Aggregate,
            (_, true) => PartKind.Literal,
            _ => PartKind.Typed,
        };
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
