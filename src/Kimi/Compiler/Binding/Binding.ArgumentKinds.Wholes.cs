// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// WholeEvidence (SPEC 10.5.1 "a slot that takes the whole literal", 10.8 Solving, 4.3.1; PLAN G924 AF-2, design-bareslot): the evidence a
// slot gets from the parts it takes whole.
// - Responsibility: the Type of the parts at a position whose pattern is a bare slot (a Tuple, a fill, a mixed range, a join or a join
//   position, from its child positions), bound from it only when every part is evidence; with the late pass, a Literal part gives its
//   own Type (DefaultOf) once DefaultArguments recorded it (SPEC 10.2.1, O-341 (a)). Read by the evidence walk at a bare slot
//   (SlotSolution.InferComposite) and by 7.4.4's comparison (ShapeEvidence, IsWholeEvidence).
// - Inputs: a position of positionParts (Binding.ArgumentKinds.Positions.cs: PositionKind, AddPartsAt, AddChildPosition with no
//   pattern), each part's bound Type and recorded Default.
// - Outputs: the whole's Type, or null where a part is no evidence; IsWholeEvidence(position).
// - Invariants: a part of Type Never, also a position Never in every source, gives none (SPEC 10.8; DECISIONS BU-2 U3's D6); the walks
//   remove the positions they append.
// - Prohibitions: no fit, binding or record here; no second reader of a whole's evidence.
public sealed partial class Binding
{
    // SPEC 7.4.4 through 10.5.1's parts: the whole at `position` is evidence once the defaults applied (ShapeEvidence's comparison).
    private bool IsWholeEvidence(int position) => this.IndependentPositionType(position, fitLiterals: true) is not null;

    // The independent Type of the parts at a position (Binding.ArgumentKinds.Positions.cs): a Tuple's, a fill's, a join's or a join
    // position's from its children; with `fitLiterals`, a Literal part gives its default once DefaultArguments applied it. A part of
    // Type Never, also a position Never in every source, gives none (SPEC 10.8; DECISIONS BU-2 U3's D6).
    private BoundType? IndependentPositionType(int position, bool fitLiterals)
    {
        var end = this.positionParts.Count;
        if (end - position != 1)
        {
            return this.PositionKind(position, end, out var type) switch
            {
                PartKind.Typed => type,
                PartKind.Literal => fitLiterals ? this.DefaultOf(this.FirstLiteral(position, end)) : null,
                PartKind.Aggregate => this.IndependentShapeType(position, end, this.PartsShape(position, end)!, fitLiterals),
                _ => null,
            };
        }

        var source = KotoHelper.UnwrapParentheses(this.positionParts[position]);
        if (source.BoundType is { } actual && !this.AwaitsPosition(source))
        {
            return ReferenceEquals(actual, BoundType.Never) ? null : actual; // SPEC 10.8: a Never part supplies no evidence.
        }

        if (source is RangeKoto range && IsMixedRange(range))
        {
            // SPEC 4.6.2: without an expected range, a literal-only boundary takes the integer Type of the typed one; its default (rule 3)
            // applies only to the selected candidate (SPEC 10.2.1).
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

        if (source is TupleLiteralKoto or ArrayLiteralKoto { FillCount: not null })
        {
            return this.IndependentShapeType(position, end, source, fitLiterals);
        }

        if (this.ComposedJoin(source) is not null)
        {
            return this.IndependentChildType(position, end, 0, fitLiterals);
        }

        return fitLiterals ? this.DefaultOf(source) : null;
    }

    // The independent Type an aggregate shape forms from its child positions' (ShapeType), as a join's Type is formed (SPEC 14.9.2
    // step 2: an array's one element position, AddPartsAt), none when one has none.
    private BoundType? IndependentShapeType(int start, int end, Koto shape, bool fitLiterals)
    {
        var count = ShapeChildren(shape);
        var children = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var position = this.positionParts.Count;
                this.AddPartsAt(start, end, i);
                var child = this.IndependentPositionType(position, fitLiterals);
                this.ClosePosition(position);
                if (child is null)
                {
                    return null;
                }

                children[i] = child;
            }

            return this.ShapeType(shape, children.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(children, clearArray: true);
        }
    }

    // The independent Type of child position `index` of [start, end).
    private BoundType? IndependentChildType(int start, int end, int index, bool fitLiterals)
    {
        var child = this.positionParts.Count;
        this.AddChildPosition(start, end, index, null);
        var type = this.IndependentPositionType(child, fitLiterals);
        this.ClosePosition(child);
        return type;
    }
}
