// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument positions (SPEC 10.5.1, 4.3.1, 14.9.2; DECISIONS BU-2 U3, BU-43): the parameter positions that the parts of an argument
// occupy, which the evidence walk (SlotSolution.InferPosition), the defaults (SlotSolution.DefaultPosition) and the whole Type at a bare
// slot (IndependentPositionType) recurse into.
// - Responsibility: a position is the argument parts at one parameter position, a range of positionParts: a call argument or a Case
//   payload; each element of a Tuple or array literal part, which SPEC 10.5.1 classifies one by one at its component or element Type;
//   and a control-flow part its call classifies by element (RecordArgumentJoin; the owner, 2026-10-26): its one child position is its
//   results at its own parameter Type, the parts of a join that supplies no candidate (Binding.ResultPropagation.Parts.cs), whose
//   child positions are its aggregate parts' by index (SPEC 10.5.1: an array result's too, at every number of sources, each index
//   holding the elements of the sources that have it; the owner, 2026-10-31), while its Type is formed with an array's one element
//   position (14.9.2 step 2, AddPartsAt). AddResultPart forms every part of a child position (Never dropped, the forms that deliver it
//   passed, a waiting join flattened). A position of several parts has one kind (PositionKind), typed first
//   (SPEC 14.9.2 step 2): Typed at its typed parts' common Type (one evidence) whatever else it holds, its literals and the parts
//   awaiting their call beside them taking it (step 3); argument sources with no common kind (revision B, DECISIONS BU-80) are Join,
//   each source an argument at the position: typed parts with no common Type, parts awaiting their call beside a literal or an
//   aggregate, and any position with a failed part (PartFailed), which fits every candidate and is no evidence (SPEC 23.1.2, rule W);
//   parts that all await their call wait (Call); then Aggregate when its parts are aggregates, Literal when only literals remain; with
//   no part (Never in every source) it is Typed with no Type: no evidence, and no Type at a slot that takes the whole (SPEC 10.8, BU-2
//   U3's D6). Every reader (the evidence, the defaults, IndependentPositionType, the fit, step 3) takes the kind from it.
// - Outputs: OpenPosition and ClosePosition, a walk's position of one part; ChildPositions and AddChildPosition, a position's child
//   positions and the parameter Type at each; ChildAt, an aggregate part's parts at a child position, which the held-part walk
//   (AddHeldParts) shares; PositionKind.
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
        TupleLiteralKoto tuple => TupleShapeAt(tuple, pattern) && index < tuple.Elements.Count ? pattern!.Components[index] : null,
        ArrayLiteralKoto => pattern is { Kind: BoundTypeKind.Array or BoundTypeKind.FixedArray } ? pattern.Components[0] : null,
        _ => pattern is { Kind: BoundTypeKind.Dictionary, Components.Count: 2 } ? pattern.Components[index] : null,
    };

    // SPEC 10.5.1 (the owner, 2026-10-31, reading I): an aggregate literal part's child positions by index, as an argument's elements
    // are classified one by one: a Tuple's or array's elements, a Dictionary's key and value.
    private static int IndexedChildren(Koto part) => part switch
    {
        TupleLiteralKoto tuple => tuple.Elements.Count,
        ArrayLiteralKoto array => array.Elements.Count,
        DictionaryLiteralKoto => 2,
        _ => throw new InvalidOperationException("An aggregate part is a Tuple, array or Dictionary literal."),
    };

    // The k-th part at child position `index` of an aggregate literal part, null past the last: a Tuple's or array's element there (a
    // fill's one value at its element position), each Dictionary entry's key (0) or value (1). The one child selection of the walks
    // into argument sources (AddIndexedChild) and into the parts a source holds (AppendHeldParts).
    private static Koto? ChildAt(Koto part, int index, int k) => part switch
    {
        TupleLiteralKoto tuple => k == 0 && index < tuple.Elements.Count ? tuple.Elements[index] : null,
        ArrayLiteralKoto array => k == 0 && index < array.Elements.Count ? array.Elements[index] : null,
        DictionaryLiteralKoto dictionary => k < dictionary.Entries.Count ? (index == 0 ? dictionary.Entries[k].Key : dictionary.Entries[k].Value) : null,
        _ => null,
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
    // elements (bound or not: the late evidence walks a bound one's), or the one position of a join's results its call classifies; of
    // several parts, a join's, those of its aggregate parts (IndexedChildren), so that arrays of unequal length have the longest one's.
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

    // Appends a part's child `index` (IndexedChildren, ChildAt), formed by AddResultPart: its element there, none for a Tuple or array
    // without one (Tuple results of other lengths wait under U1.6g (a) and fail their shape per candidate), a Dictionary's keys or values.
    private void AddIndexedChild(Koto part, int index)
    {
        for (var k = 0; ChildAt(part, index, k) is { } child; k++)
        {
            this.AddResultPart(child);
        }
    }

    // The join of a control-flow part its call completes by its results: a Join (RecordArgumentJoin), or a nested join that waits for its
    // position (WaitingResult), walked as a result source's is (AddResultPart); null otherwise.
    private Koto? ComposedJoin(Koto part)
        => this.KindOf(part) == PartKind.Join || this.WaitingResult(PartCore(part)) is not null ? ControlFlowExpression(part) : null;

    // The kind of a position of several parts, a join's, typed first (SPEC 14.9.2 step 2): Typed at the common Type of its typed parts,
    // whatever else it holds, or with no Type when it has no part; Join (revision B, BU-80; SPEC 10.5.1) for sources fitted one by one:
    // a failed part among them (rule W), typed parts with no common Type, or parts awaiting their call (AwaitsCall) beside a literal or
    // an aggregate, or the aggregate sources of a join that are typed whole (FormedByElement); Call when its parts all await their call;
    // then Aggregate when its parts are aggregates, then Literal.
    private PartKind PositionKind(int start, int end, out BoundType? type)
    {
        type = null;
        var literal = false;
        var waits = false;
        var failed = false;
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
            else if (this.PartFailed(part))
            {
                failed = true;
            }
        }

        if (failed)
        {
            return PartKind.Join; // SPEC 23.1.2 (rule W, BU-79): a wildcard among sources fitted one by one.
        }

        if (end == start || (type = this.PositionType(start, end, this.ConstraintScope(this.positionParts[start]), out var typed)) is not null)
        {
            return PartKind.Typed;
        }

        // SPEC 10.5.1 (revision B, BU-80): argument sources with no common kind are fitted one by one, each at the position: typed parts
        // with no common Type, parts that await their call beside a literal or an aggregate, and aggregates that are typed whole (an array
        // literal source gives Array<T>, 14.9.2 step 2). Parts that all await their call wait.
        var shape = this.PartsShape(start, end);
        return typed || (waits && (literal || shape is not null)) ? PartKind.Join : waits ? PartKind.Call :
            shape is not null ? (end - start > 1 && !FormedByElement(shape) ? PartKind.Join : PartKind.Aggregate) : literal ? PartKind.Literal : PartKind.Typed;
    }

    // The first typed part of [start, end), as which a Typed position's common Type is evidence, a typed argument's (InferParts).
    private Koto FirstTyped(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var part = this.positionParts[i];
            if (!IsUnfittedLiteral(part) && !this.IsAggregatePart(part) && this.SuppliedType(part) is not null)
            {
                return part;
            }
        }

        throw new InvalidOperationException("A Typed position with a Type holds a typed part.");
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
