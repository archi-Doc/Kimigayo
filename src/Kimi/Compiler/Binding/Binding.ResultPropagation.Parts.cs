// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// A join's parts (SPEC 14.9.2 step 2; DECISIONS R4-M134, BU-43): where no source supplies a candidate, the common Type is formed part
// by part over the sources other than Never.
// - Responsibility: the parts at one position of a join, a range of positionParts (Binding.ArgumentKinds.Positions.cs): its sources
//   other than Never, through the forms that deliver them and the selections, `do` and `loop` none of whose sources supplies a
//   candidate (AddResultPart); the parts a child Type of Tuple, array, fill or Dictionary literals is formed from, by element (an
//   array's every element at its one element position), key and value (AddPartsAt); the common Type of a position's typed parts (PositionType); the common Type formed part by part
//   (CommonTypeByParts); and a join's judgement by that formation (JudgeOwn), its root's judgement of its tree (JudgeNested).
// - Inputs: the parts' bound Types, the literals still waiting unbound, the results' recorded sources (ResultContext).
// - Outputs: the formed Type, or none with the conflict that prevents it (PartsConflict); a judged join's outcome (Conflict, Dependent).
// - Invariants: a part of Type Never is no part; a failed part is no part either (the join rests on it); the walk binds nothing and
//   removes the parts it appended.
// - Prohibitions: no record here; the join (AwaitsWithoutCandidate) and the argument walks decide what the Type is to them.
public sealed partial class Binding
{
    // The scratch of a position's typed Types or literal defaults, used by one SelectCommonType at a time.
    private readonly List<BoundType> partTypes = new();

    // What prevents a Type formed part by part: a conflict among typed parts (or shapes), among literal defaults, or a failed part
    // (PartFailed), over which the formation still runs (SPEC 23.1.2: an independent conflict among the others is found).
    [Flags]
    private enum PartsConflict : byte
    {
        None = 0,
        Typed = 1,
        Defaults = 2,
        Failed = 4,
    }

    // The child positions of aggregate parts of one shape: a Tuple's elements, an array's or fill's element, a Dictionary's key and value.
    private static int ShapeChildren(Koto shape) => shape switch
    {
        TupleLiteralKoto tuple => tuple.Elements.Count,
        DictionaryLiteralKoto => 2,
        _ => 1,
    };

    private static bool SameShape(Koto part, Koto shape) => (part, shape) switch
    {
        (TupleLiteralKoto tuple, TupleLiteralKoto other) => tuple.Elements.Count == other.Elements.Count,
        (ArrayLiteralKoto { FillLength: null }, ArrayLiteralKoto { FillLength: null }) => true,
        (ArrayLiteralKoto { FillCount: { } count }, ArrayLiteralKoto { FillCount: { } other }) => ReferenceEquals(count, other),
        (DictionaryLiteralKoto, DictionaryLiteralKoto) => true,
        _ => false,
    };

    // SPEC 14.9.2: a Tuple, array, fill or Dictionary literal part whose Type is formed by its own parts: one waiting unbound or holding
    // a part that awaits its position. A failed part (PartFailed) is no waiting part: the position it is at has no Type formed part by
    // part (BU-9: a literal is bound once).
    private bool IsAggregatePart(Koto part)
        => part is TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto && !this.PartFailed(part) &&
            (part.BoundType is null || this.AwaitsPosition(part));

    // Adds a part at a position: none for Never; the operand of the forms that deliver it; a result target that supplies no candidate
    // through its sources other than Never (nested results propagate the same way), unless it failed (PartFailed): a failed join is one
    // part, no waiting part (BU-9 r1), on whose record the join rests.
    private void AddResultPart(Koto part)
    {
        if (ReferenceEquals(part.BoundType, BoundType.Never))
        {
            return;
        }

        if (DeliveredOperand(part) is { } operand)
        {
            this.AddResultPart(operand);
            return;
        }

        // A join decided Waiting (WaitingResult) holds only deferred sources until it is completed, and a source leaves that state only by
        // its completion, so Waiting implies AwaitsPosition or BoundSince: asked first, the decided fact spares the subtree walk.
        if (DeliversResult(part) && !this.PartFailed(part) && (this.WaitingResult(part) is not null || this.AwaitsPosition(part) || this.Deferred(part)))
        {
            this.AddResults(part);
            return;
        }

        this.positionParts.Add(part);
    }

    // Adds a result target's results, its sources other than Never, as parts at its own position.
    private void AddResults(Koto join)
    {
        var context = this.resultContexts[join];
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (!ReferenceEquals(context.Sources[i], BoundType.Never) && context.SourceNodes[i] is { } source)
            {
                this.AddResultPart(source);
            }
        }
    }

    // SPEC 14.9.2 step 2, 23.1.2 (DECISIONS BU-43): a join's own judgement, by one formation over its own results (CommonTypeByParts),
    // nested joins flattened where they are clean and one failed part each where they are not. A conflict among its typed parts or
    // shapes is its own, found at any position (Conflict), unless under a call's argument probe it waits for its call (ConflictWaits,
    // U1.6g); else a failed part leaves it Dependent on the first (RestOn). A conflict among
    // literal defaults is reported (`defaults`), not judged: a literal result is not fixed before outer constraints (step 3), so it is
    // the root's (AwaitsWithoutCandidate). Idempotent: called at the join's own decision and again by its root (JudgeNested). Returns
    // the Type formed over the parts that are no conflict and no failure, null when none; `shape` is the first aggregate part.
    private BoundType? JudgeOwn(Koto join, ResultContext context, out Koto? shape, out bool defaults)
    {
        var start = this.positionParts.Count;
        this.AddResults(join);
        var conflict = PartsConflict.None;
        Koto? cause = null;
        var common = this.CommonTypeByParts(start, this.positionParts.Count, this.ConstraintScope(join), ref conflict, ref cause);
        shape = this.PartsShape(start, this.positionParts.Count);
        this.ClosePosition(start);
        defaults = (conflict & PartsConflict.Defaults) != 0;
        if ((conflict & PartsConflict.Typed) != 0 && !this.ConflictWaits(join))
        {
            context.Conflict();
        }
        else if (cause is not null)
        {
            context.RestOn(cause);
        }

        return common;
    }

    // SPEC 14.9.2 (DECISIONS BU-43): the root's judgement of its tree before its own formation: each waiting join its waited sources hold,
    // through the forms that deliver them and the parts of Tuple, array and Dictionary literals (JudgeHeld), is judged again after its
    // own (JudgeOwn, post-order), so that a conflict among typed parts, functions a call completed since included, is recorded by the
    // innermost join whose own parts conflict and is one failed part of every enclosing formation.
    private void JudgeNested(Koto join, ResultContext context)
    {
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is null && context.SourceNodes[i] is { } source)
            {
                this.JudgeHeld(source);
            }
        }
    }

    private void JudgeHeld(Koto part)
    {
        if (DeliveredOperand(part) is { } operand)
        {
            this.JudgeHeld(operand);
            return;
        }

        switch (part)
        {
            case TupleLiteralKoto tuple:
                foreach (var element in tuple.Elements)
                {
                    this.JudgeHeld(element);
                }

                break;
            case ArrayLiteralKoto array:
                foreach (var element in array.Elements)
                {
                    this.JudgeHeld(element);
                }

                break;
            case DictionaryLiteralKoto dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    this.JudgeHeld(entry.Key);
                    this.JudgeHeld(entry.Value);
                }

                break;
            default:
                if (this.WaitingResult(part) is { } waiting)
                {
                    this.JudgeNested(part, waiting);
                    this.JudgeOwn(part, waiting, out _, out _);
                }

                break;
        }
    }

    // SPEC 10.5.1 (DECISIONS BU-43): whether a join's results hold a part that awaits its call (AwaitsCall).
    private bool HoldsPartAwaitingCall(Koto join)
    {
        var start = this.positionParts.Count;
        this.AddResults(join);
        var holds = false;
        for (var i = start; !holds && i < this.positionParts.Count; i++)
        {
            holds = this.AwaitsCall(this.positionParts[i]);
        }

        this.ClosePosition(start);
        return holds;
    }

    // The first aggregate part of [start, end), which stands for the position's shape; null when there is none.
    private Koto? PartsShape(int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (this.IsAggregatePart(this.positionParts[i]))
            {
                return this.positionParts[i];
            }
        }

        return null;
    }

    // SPEC 14.9.2 step 2, 12.4.1: appends the parts that child `index` of the aggregate parts in [start, end) forms its Type from: each
    // part's child there (AddIndexedChild), but every element of an array, which has one element Type (ShapeChildren).
    private void AddPartsAt(int start, int end, int index)
    {
        for (var i = start; i < end; i++)
        {
            var part = KotoHelper.UnwrapParentheses(this.positionParts[i]);
            if (part is ArrayLiteralKoto array && this.IsAggregatePart(array))
            {
                foreach (var element in array.Elements)
                {
                    this.AddResultPart(element);
                }
            }
            else if (this.IsAggregatePart(part))
            {
                this.AddIndexedChild(part, index);
            }
        }
    }

    // Whether every aggregate part of [start, end) has the shape of `shape` and no literal sits beside them: Tuples of one length,
    // list literals, fills of one length (SPEC 4.3.3), Dictionaries.
    private bool SameShape(int start, int end, Koto shape)
    {
        for (var i = start; i < end; i++)
        {
            var part = this.positionParts[i];
            if (IsUnfittedLiteral(part) || (this.IsAggregatePart(part) && !SameShape(part, shape)))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 14.9.2 step 2: the common Type of the typed parts of [start, end), literals of it and safe reference layers over it
    // included; `typed` tells whether there is one; null when there is none or they conflict.
    private BoundType? PositionType(int start, int end, BindingScope scope, out bool typed)
    {
        var types = this.partTypes;
        types.Clear();
        for (var i = start; i < end; i++)
        {
            var part = this.positionParts[i];
            if (!IsUnfittedLiteral(part) && !this.IsAggregatePart(part) && this.SuppliedType(part) is { } type)
            {
                types.Add(type);
            }
        }

        typed = types.Count > 0;
        var common = typed ? this.SelectCommonType(types, scope, out _) : null;
        types.Clear();
        return common;
    }

    // SPEC 14.9.2 step 2: the common Type at the position [start, end): its typed parts' (PositionType); without one, its aggregate
    // parts' formed by their child positions (Tuples by element, list literals by element as Array<T>, fills by element at their
    // length, Dictionaries by key and value); without one, its literals' defaults' (SPEC 10.2); with no part, Never. A failed part
    // (PartFailed) is removed from the position, `cause` receiving the first, and the formation runs over the others, every position
    // for its conflicts (SPEC 23.1.2). Null when a conflict or a failed part prevents it, or a part is still unbound without waiting.
    private BoundType? CommonTypeByParts(int start, int end, BindingScope scope, ref PartsConflict conflict, ref Koto? cause)
    {
        for (var i = start; i < end;)
        {
            if (this.PartFailed(this.positionParts[i]))
            {
                conflict |= PartsConflict.Failed;
                cause ??= this.positionParts[i];
                this.positionParts.RemoveAt(i);
                end--;
            }
            else
            {
                i++;
            }
        }

        var common = this.PositionType(start, end, scope, out var typed);
        if (typed)
        {
            conflict |= common is null ? PartsConflict.Typed : PartsConflict.None;
            return common;
        }

        if (this.PartsShape(start, end) is { } shape)
        {
            if (!this.SameShape(start, end, shape))
            {
                conflict |= PartsConflict.Typed;
                return null;
            }

            var count = ShapeChildren(shape);
            var children = this.RentTypes(count);
            try
            {
                var formed = true;
                for (var i = 0; i < count; i++)
                {
                    var child = this.positionParts.Count;
                    this.AddPartsAt(start, end, i);
                    var part = this.CommonTypeByParts(child, this.positionParts.Count, scope, ref conflict, ref cause);
                    this.ClosePosition(child);
                    formed &= part is not null;
                    children[i] = part!;
                }

                return formed ? this.ShapeType(shape, children.AsSpan(0, count)) : null;
            }
            finally
            {
                this.typeScratch.Return(children, clearArray: true);
            }
        }

        common = this.DefaultsType(start, end, scope, out var conflicts);
        conflict |= conflicts ? PartsConflict.Defaults : PartsConflict.None;
        return common;
    }

    // SPEC 14.9.2 step 3, 10.2: the common Type of the literals' defaults of [start, end), Never with no part; `conflicts` tells whether
    // they conflict.
    private BoundType? DefaultsType(int start, int end, BindingScope scope, out bool conflicts)
    {
        conflicts = false;
        var types = this.partTypes;
        types.Clear();
        for (var i = start; i < end; i++)
        {
            if (IsUnfittedLiteral(this.positionParts[i]) && this.LiteralDefault(this.positionParts[i]) is { } literalDefault)
            {
                types.Add(literalDefault);
            }
        }

        if (types.Count == 0)
        {
            return end == start ? BoundType.Never : null;
        }

        var common = this.SelectCommonType(types, scope, out _);
        types.Clear();
        conflicts = common is null;
        return common;
    }

    // The fixed array of a fill's length (SPEC 4.3.3).
    private BoundType FixedArrayType(BoundType element, BoundLength length)
    {
        var count = length.IsConstant ? length.Value : 0;
        return this.types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], count, lengthExpression: length.IsConstant ? null : length);
    }

    // The Type an aggregate shape forms from its child positions' Types: a Tuple, Array<T> for a list literal (SPEC 4.3.1), the fixed
    // array of a fill's length (4.3.3), a Dictionary.
    private BoundType ShapeType(Koto shape, ReadOnlySpan<BoundType> children) => shape switch
    {
        TupleLiteralKoto => this.types.Tuple(children),
        ArrayLiteralKoto { FillCount: { } length } => this.FixedArrayType(children[0], length),
        ArrayLiteralKoto => this.types.Intern(BoundTypeKind.Array, this.Library.DynamicArray, SemanticsKind.Owner, [children[0]]),
        _ => this.types.Intern(BoundTypeKind.Dictionary, this.Library.GetSymbol(KimiDeclarationId.Dictionary), SemanticsKind.Owner, [children[0], children[1]]),
    };
}
