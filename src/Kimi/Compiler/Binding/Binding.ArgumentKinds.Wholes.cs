// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// WholeEvidence (SPEC 10.5.1 "a slot that takes the whole literal", 10.8 Solving, 4.3.1 Places; PLAN G924 AF-2, design-bareslot; the
// owner's O-316 (a), O-341 (a), O-346 (a)): the evidence a slot gets from the parts it takes whole.
// - Responsibility: the one meet of every whole at a bare slot (a Tuple, array, fill or Dictionary literal, a mixed range, or a join
//   with such a source, at a position whose pattern is a slot no fixed binding solves) for one candidate's or one Case construction's
//   pass, gathered from every argument and the slot's binding from the other evidence together (order-free; G378), then bound.
// - Inputs: the wholes the evidence walk records (SlotSolution.InferComposite: the slot and the position's parts), the walks'
//   positions (AddPartsAt, AddResultPart: a join's sources flattened, Never dropped), the slot's binding in the frame, and in the late
//   pass each part's own Type (DefaultOf: O-341 (a)'s OwnType, written by DefaultArguments).
// - Outputs: the slot's binding (BindWholes), or the candidate inapplicable where the places disagree; IsWholeEvidence, read by 7.4.4's
//   comparison (ShapeEvidence).
// - Invariants: a whole is a tree of places (its root; a Tuple component; an array's or fill's one element place shared by every
//   element; a Dictionary's key and value places). At a place the typed parts meet as 14.9.2 step 2 finds a common Type, its sentence on
//   safe reference layers over one read Type with or without a literal beside them (SelectCommonType; the owner's O-352 (a)); a part's Type, or the slot's binding, at a place stands at each place below it (4.3.1 Places, ruling 27), and the Types at
//   a place agree in structure and Semantics (10.8): never adapted, since conversions never choose a solution. Aggregates agree in
//   skeleton (a Tuple's arity, a Dictionary, an array literal's length where a fill or an exact fixed array fixes `[N of _]`); an open
//   array kind is Array<E>. A place with no Type is a hole: its literals and waiting parts take their own Types in the late pass, an
//   anonymous function or an empty literal's element never (10.8, 10.2.1); a whole holding a part of Type Never gives nothing (10.8).
//   The slot is bound only where no place is a hole (10.5.1 "only when every part is evidence").
// - Prohibitions: no fit, completion or record here; no reader decides by an earlier argument's binding; no binding of a whole with no
//   expected Type; no second gatherer of a whole's evidence.
public sealed partial class Binding
{
    // The wholes recorded by the evidence walk at a bare slot, per pass (SlotSolution.BindWholes reads and removes those since its mark).
    private readonly List<(BoundType? Slot, Koto Part)> wholes = new();

    // Whether a literal holds a part of Type Never at any depth of its literal parts.
    private static bool HoldsNever(Koto part)
    {
        switch (part)
        {
            case TupleLiteralKoto tuple:
                foreach (var element in tuple.Elements)
                {
                    if (HoldsNeverPart(element))
                    {
                        return true;
                    }
                }

                return false;
            case ArrayLiteralKoto array:
                foreach (var element in array.Elements)
                {
                    if (HoldsNeverPart(element))
                    {
                        return true;
                    }
                }

                return false;
            case DictionaryLiteralKoto dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    if (HoldsNeverPart(entry.Key) || HoldsNeverPart(entry.Value))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static bool HoldsNeverPart(Koto part)
        => ReferenceEquals(part.BoundType, BoundType.Never) || HoldsNever(KotoHelper.UnwrapParentheses(part));

    // One aggregate's skeleton at its place: the shape's family; a list literal's length at a fill's or fixed array's; `known`'s
    // family, as a call-argument literal fits it (ProbeAggregateArgument).
    private static bool SkeletonAt(Koto part, Koto shape, BoundType? known, ArrayLiteralKoto? fill) => part switch
    {
        TupleLiteralKoto tuple => shape is TupleLiteralKoto other && other.Elements.Count == tuple.Elements.Count && (known is null || TupleShapeAt(tuple, known)),
        DictionaryLiteralKoto => shape is DictionaryLiteralKoto && known is null or { Kind: BoundTypeKind.Dictionary },
        ArrayLiteralKoto array => shape is ArrayLiteralKoto &&
            (array.FillCount is not null || fill is null || (fill.FillCount is { IsConstant: true } length && length.Value == array.Elements.Count)) &&
            (known is null || ProbeArrayShape(array, known)),
        _ => false,
    };

    // The array literal's or fill's shape at `known` (SPEC 4.3, 4.3.3): an owned Array, or the fixed array of exactly its length.
    private static bool ProbeArrayShape(ArrayLiteralKoto array, BoundType known)
        => known is { Kind: BoundTypeKind.FixedArray or BoundTypeKind.Array, Semantics: SemanticsKind.Owner } &&
            (array.FillLength is null || known.Kind == BoundTypeKind.FixedArray) &&
            (known.Kind != BoundTypeKind.FixedArray || (array.FillLength is null ? known.LengthExpression is null && known.Length == array.Elements.Count :
                array.FillCount is { } count && (count.IsConstant
                    ? known.LengthExpression is null && known.Length == count.Value
                    : ReferenceEquals(known.LengthExpression, count))));

    // SPEC 7.4.4 through 10.5.1's parts (W6): the whole at `position` is evidence once the defaults applied (ShapeEvidence's comparison).
    private bool IsWholeEvidence(int position)
    {
        var end = this.positionParts.Count;
        var place = end;
        for (var i = position; i < end; i++)
        {
            if (!this.AddWhole(this.positionParts[i]))
            {
                this.ClosePosition(place);
                return false;
            }
        }

        var conflict = false;
        var type = this.PlaceType(place, this.positionParts.Count, null, true, ref conflict);
        this.ClosePosition(place);
        return !conflict && type is not null;
    }

    // Adds a whole's parts at its slot's root place: a join's sources (ComposedJoin), else the part (AddResultPart); false when the whole
    // holds a part of Type Never inside its literals, which gives nothing (SPEC 10.8, 14.9.2 step 2).
    private bool AddWhole(Koto part)
    {
        if (HoldsNever(KotoHelper.UnwrapParentheses(part)))
        {
            return false;
        }

        if (this.ComposedJoin(part) is { } join)
        {
            this.AddResults(join);
        }
        else
        {
            this.AddResultPart(part);
        }

        return true;
    }

    // The Type of the place [start, end) of a slot's wholes (the contract above); `exact` is the Type a part or the slot's binding at a
    // place above stands at here. Null for a hole; `conflict` when the place's Types or skeletons disagree.
    private BoundType? PlaceType(int start, int end, BoundType? exact, bool late, ref bool conflict)
    {
        if (start == end)
        {
            return exact; // A place with no part: unbound unless a Type above stands here (u4m, e1).
        }

        if (end - start == 1 && KotoHelper.UnwrapParentheses(this.positionParts[start]) is RangeKoto range && IsMixedRange(range))
        {
            return exact ?? this.IndependentRangeType(range);
        }

        for (var i = start; i < end; i++)
        {
            if (this.PartFailed(this.positionParts[i]))
            {
                return exact; // SPEC 23.1.2: a failed part is no evidence (rule W); its siblings are checked at the selection.
            }
        }

        var scope = this.ConstraintScope(this.positionParts[start]);
        var common = this.PositionType(start, end, scope, out var typed);
        if ((typed && common is null) || (common is not null && exact is not null && !this.SameBinding(common, exact)))
        {
            conflict = true;
            return null;
        }

        var known = exact ?? common;
        if (this.PartsShape(start, end) is not { } shape)
        {
            return known ?? (late ? this.PlaceOwnType(start, end) : null);
        }

        // SPEC 10.2 row 2, 3.6 (PLAN G924 rule B): at a `ref/U` place the literals are owner temporaries of U (Materialization).
        var formed = known is null ? null : this.Materialization(shape, known, scope) ?? known;
        if (!this.SkeletonsAgree(start, end, shape, formed, out var fill))
        {
            conflict = true;
            return null;
        }

        var count = ShapeChildren(shape);
        var children = this.RentTypes(count);
        try
        {
            var hole = false;
            for (var i = 0; i < count; i++)
            {
                var child = this.positionParts.Count;
                this.AddPartsAt(start, end, i);
                var type = this.PlaceType(child, this.positionParts.Count, formed is null ? null : ExpectedChild(shape, i, formed), late, ref conflict);
                this.ClosePosition(child);
                if (conflict)
                {
                    return null;
                }

                hole |= type is null;
                children[i] = type!;
            }

            return known ?? (hole ? null : this.ShapeType(fill ?? shape, children.AsSpan(0, count)));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
        }
    }

    // SPEC 10.8 Solving: two bindings of one place agree in structure and Semantics, without adaptation; Origins are judged after
    // selection (15.6.1), so they are aside here (SameStructure both ways).
    private bool SameBinding(BoundType a, BoundType b) => ReferenceEquals(a, b) || (this.SameStructure(a, b) && this.SameStructure(b, a));

    // SPEC 10.8, 4.3.1, 4.3.3: whether the aggregate parts of [start, end) agree with `shape` and with `known`: Tuples of one arity,
    // Dictionaries, array literals and fills whose length a fill or a fixed array fixes; no literal beside them. `fill` is the place's
    // first fill, which fixes the kind.
    private bool SkeletonsAgree(int start, int end, Koto shape, BoundType? known, out ArrayLiteralKoto? fill)
    {
        fill = null;
        for (var i = start; i < end; i++)
        {
            var part = KotoHelper.UnwrapParentheses(this.positionParts[i]);
            if (IsUnfittedLiteral(part))
            {
                return false;
            }

            if (part is ArrayLiteralKoto { FillCount: { } } candidate && this.IsAggregatePart(candidate))
            {
                fill ??= candidate;
                if (!ReferenceEquals(fill.FillCount, candidate.FillCount))
                {
                    return false;
                }
            }
        }

        for (var i = start; i < end; i++)
        {
            var part = KotoHelper.UnwrapParentheses(this.positionParts[i]);
            if (this.IsAggregatePart(part) && !SkeletonAt(part, shape, known, fill))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.2.1 (O-341 (a)): a hole's own Type in the late pass: the common Type of its parts' own Types (DefaultOf), none when a part
    // has none (an anonymous function, an empty literal's element).
    private BoundType? PlaceOwnType(int start, int end)
    {
        var types = this.partTypes;
        types.Clear();
        for (var i = start; i < end; i++)
        {
            if (this.DefaultOf(KotoHelper.UnwrapParentheses(this.positionParts[i])) is not { } own)
            {
                types.Clear();
                return null;
            }

            types.Add(own);
        }

        var common = this.SelectCommonType(types, this.ConstraintScope(this.positionParts[start]), out _);
        types.Clear();
        return common;
    }

    // SPEC 4.6.2: a mixed range at a bare slot: without an expected range, a literal-only boundary takes the integer Type of the typed
    // one; its default (rule 3) applies only to the selected candidate (SPEC 10.2.1).
    private BoundType? IndependentRangeType(RangeKoto range)
    {
        var start = this.IndependentRangeBoundary(range.Start, this.StartType);
        var last = this.IndependentRangeBoundary(range.End, this.EndType);
        start ??= this.DefaultOf(range.Start!) ?? this.RangeTyping(range.Start!, last);
        last ??= this.DefaultOf(range.End!) ?? this.RangeTyping(range.End!, start);
        if (start is null || last is null)
        {
            return null;
        }

        var symbol = range.IsInclusive ? this.Library.ClosedRange : this.Library.Range;
        return this.types.Intern(BoundTypeKind.Constructed, symbol, SemanticsKind.Owner, [start, last]);
    }

    private ref partial struct SlotSolution
    {
        // SPEC 10.5.1, 10.8 Solving: at the end of a pass, each slot's wholes recorded since `mark` and the slot's binding from the other
        // evidence meet once (PlaceType) and bind the slot where no place is a hole; false when they disagree (the candidate is
        // inapplicable). Removes the wholes it read.
        internal readonly bool BindWholes(int mark, bool late)
        {
            var binding = this.Binding;
            var wholes = binding.wholes;
            var bound = true;
            for (var i = mark; bound && i < wholes.Count; i++)
            {
                if (wholes[i].Slot is not { } slot)
                {
                    continue;
                }

                var place = binding.positionParts.Count;
                var exact = binding.SubstituteType(slot, this.SlotOwner, this.Types, this.Lengths);
                for (var j = i; j < wholes.Count; j++)
                {
                    if (ReferenceEquals(wholes[j].Slot, slot))
                    {
                        _ = binding.AddWhole(wholes[j].Part);
                        wholes[j] = default;
                    }
                }

                var conflict = false;
                var type = binding.PlaceType(place, binding.positionParts.Count, exact, late, ref conflict);
                binding.ClosePosition(place);
                bound = !conflict && (type is null || binding.Infer(slot, type, this.Solve, this.Frame, lengths: true));
            }

            wholes.RemoveRange(mark, wholes.Count - mark);
            return bound;
        }
    }
}
