// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// A join's parts (SPEC 14.9.2 step 2; DECISIONS R4-M134, BU-43): where no source supplies a candidate, the common Type is formed part
// by part over the sources other than Never.
// - Responsibility: the parts at one position of a join, a range of positionParts (Binding.ArgumentKinds.Positions.cs): its sources
//   other than Never, through the forms that deliver them (DeliveredOperand) and the selections, `do` and `loop` none of whose sources
//   supplies a candidate (AddResultPart); the child positions of parts that are Tuple, array, fill or Dictionary literals, by element,
//   key and value (AddPartsAt); the common Type of a position's typed parts (PositionType); and the common Type formed part by part
//   (CommonTypeByParts).
// - Inputs: the parts' bound Types, the literals still waiting unbound, the results' recorded sources (ResultContext).
// - Outputs: the formed Type, or none with the conflict that prevents it (PartsConflict).
// - Invariants: a part of Type Never is no part; the walk binds nothing and removes the parts it appended.
// - Prohibitions: no record here; the join (AwaitsWithoutCandidate) and the argument walks decide what the Type is to them.
public sealed partial class Binding
{
    // The scratch of a position's typed Types or literal defaults, used by one SelectCommonType at a time.
    private readonly List<BoundType> partTypes = new();

    // What prevents a Type formed part by part: typed parts (or shapes) that conflict, or literal defaults that do.
    [Flags]
    private enum PartsConflict : byte
    {
        None = 0,
        Typed = 1,
        Defaults = 2,
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
    // a part that awaits its position. A part Binding rejected (Rejected) is no waiting part: the position it is at has no Type formed
    // part by part (BU-9: a literal is bound once).
    private bool IsAggregatePart(Koto part)
        => part is TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto && !Rejected(part) && (part.BoundType is null || this.AwaitsPosition(part));

    // Adds a part at a position: none for Never; the operand of the forms that deliver it; a result target that supplies no candidate
    // through its sources other than Never (nested results propagate the same way).
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

        if (DeliversResult(part) && this.AwaitsPosition(part))
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

    // SPEC 10.5.1 (DECISIONS BU-43): whether a join's results hold an anonymous function that waits for its call.
    private bool HoldsWaitingFunction(Koto join)
    {
        var start = this.positionParts.Count;
        this.AddResults(join);
        var holds = false;
        for (var i = start; !holds && i < this.positionParts.Count; i++)
        {
            holds = this.positionParts[i] is FunctionKoto { IsAnonymous: true, BindingState: BindingState.Unvisited };
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

    // Appends the parts of child position `index` of the aggregate parts in [start, end).
    private void AddPartsAt(int start, int end, int index)
    {
        for (var i = start; i < end; i++)
        {
            switch (this.positionParts[i])
            {
                case TupleLiteralKoto tuple when this.IsAggregatePart(tuple):
                    this.AddResultPart(tuple.Elements[index]);
                    break;
                case ArrayLiteralKoto array when this.IsAggregatePart(array):
                    foreach (var element in array.Elements)
                    {
                        this.AddResultPart(element);
                    }

                    break;
                case DictionaryLiteralKoto dictionary when this.IsAggregatePart(dictionary):
                    foreach (var entry in dictionary.Entries)
                    {
                        this.AddResultPart(index == 0 ? entry.Key : entry.Value);
                    }

                    break;
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
    // length, Dictionaries by key and value); without one, its literals' defaults' (SPEC 10.2); with no part, Never. Null when a
    // conflict prevents it, or a part is still unbound without waiting.
    private BoundType? CommonTypeByParts(int start, int end, BindingScope scope, ref PartsConflict conflict)
    {
        for (var i = start; i < end; i++)
        {
            if (Rejected(this.positionParts[i]))
            {
                return null;
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
                for (var i = 0; i < count; i++)
                {
                    var child = this.positionParts.Count;
                    this.AddPartsAt(start, end, i);
                    var part = this.CommonTypeByParts(child, this.positionParts.Count, scope, ref conflict);
                    this.ClosePosition(child);
                    if (part is null)
                    {
                        return null;
                    }

                    children[i] = part;
                }

                return this.ShapeType(shape, children.AsSpan(0, count));
            }
            finally
            {
                this.typeScratch.Return(children, clearArray: true);
            }
        }

        return this.LiteralDefaults(start, end, scope, ref conflict);
    }

    // SPEC 14.9.2 step 3, 10.2: a position of literals takes their defaults' common Type; with no literal, every part is Never.
    private BoundType? LiteralDefaults(int start, int end, BindingScope scope, ref PartsConflict conflict)
    {
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
        conflict |= common is null ? PartsConflict.Defaults : PartsConflict.None;
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
