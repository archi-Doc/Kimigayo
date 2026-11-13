// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument positions (SPEC 10.5.1, 4.3.1, 14.9.2; DECISIONS BU-2 U3, BU-43): the parameter positions that the parts of an argument
// occupy, which the evidence walk (SlotSolution.InferPosition), the defaults (SlotSolution.DefaultPosition) and the whole Type at a bare
// slot (IndependentPositionType) recurse into.
// - Responsibility: a position is the argument parts at one parameter position, a range of positionParts: a call argument or a Case
//   payload; each element of a Tuple or array literal part and each key and value of a Dictionary literal part (AggregateParts), which
//   SPEC 10.5.1 classifies one by one at its component, element, key or value Type;
//   and a control-flow part its call classifies by element (ClassifiedByCall; the owner, 2026-10-26): its one child position is its
//   results at its own parameter Type, the parts of a join that supplies no candidate (Binding.ResultPropagation.Parts.cs), whose
//   child positions are its aggregate parts' by index (SPEC 10.5.1: an array result's too, at every number of sources, each index
//   holding the elements of the sources that have it; the owner, 2026-10-31), while its Type is formed with an array's one element
//   position (14.9.2 step 2, AddPartsAt). AddResultPart forms every part of a child position (Never dropped, the forms that deliver it
//   passed, a waiting join flattened). A position of several parts has one kind (PositionKind), typed first
//   (SPEC 14.9.2 step 2): Typed at its typed parts' common Type (one evidence) whatever else it holds, its literals and the parts
//   awaiting their call beside them taking it (step 3); a position whose typed parts have no common Type waits (Call; U1.6g (a)), and
//   one with no typed part, and so no candidate, waits (Call) when a part awaits its call; then Aggregate when its parts are
//   aggregates, Literal when only literals remain; with no part (Never in every source, or only failed parts) it is Typed with no
//   Type: no evidence, and no Type at a slot that takes the whole (SPEC 10.8, BU-2 U3's D6). A failed part (PartFailed) makes its
//   position Call, Waiting under 10.5.1 (U1.6h site 3; DECISIONS BU-78): it is no evidence and fits every candidate (SPEC 23.1.2), and
//   its siblings are checked at the selected candidate's Type. Every
//   reader (the evidence, the defaults, IndependentPositionType, the fit) takes the kind from it.
// - Outputs: OpenPosition and ClosePosition, a walk's position of one part; ChildPositions and AddChildPosition, a position's (or a
//   part's) child positions and the parameter Type at each; PositionKind.
// - Invariants: a walk removes the parts it appended before it returns, so nested walks share the list; a position's parts end where
//   the list ends when its walk begins.
// - Prohibitions: no fit, evidence or record here; the walks decide what a position's parts are to their candidate.
public sealed partial class Binding
{
    private readonly List<Koto> positionParts = new();

    // SPEC 10.5.1, 10.4 step 1 (the owner's O-250 u; DECISIONS BU-43): the parts of a Tuple, array or Dictionary literal at their child
    // positions, the one representation every aggregate walk reads: preparation (PrepareComposite), the fit (ProbeAggregateArgument), the
    // type without an expectation (HasTypeWithoutExpectation), an unselected call's completion (CompleteUnselectedPart), the waiting walk
    // (TakeWaiting, PartHolder, BoundSince) and the position walks (ChildPositions, AddChildPosition, AddIndexedChild). A Tuple's or
    // array's element i is part i at child position i; a Dictionary's entry j gives part 2j, its key, at child position 0 and part 2j + 1,
    // its value, at 1. A view over the literal's own lists: it allocates nothing. Unit `()` and every other form have no parts (Exist).
    private readonly struct AggregateParts
    {
        private readonly List<Koto>? elements;
        private readonly List<DictionaryLiteralEntry>? entries;

        public AggregateParts(Koto node)
        {
            this.elements = node switch
            {
                TupleLiteralKoto { Elements.Count: > 0 } tuple => tuple.Elements,
                ArrayLiteralKoto array => array.Elements,
                _ => null,
            };
            this.entries = (node as DictionaryLiteralKoto)?.Entries;
        }

        public bool Exist => this.elements is not null || this.entries is not null;

        public int Count => this.elements?.Count ?? (2 * this.entries?.Count) ?? 0;

        // The child positions by index (SPEC 10.5.1; the owner, 2026-10-31): a Tuple's or array's elements, a Dictionary's key and value.
        public int Children => this.elements?.Count ?? (this.entries is null ? 0 : 2);

        // From part `index`, at child position `index`, the step to the next part at that position.
        public int Stride => this.elements?.Count ?? 2;

        // SPEC 12.4.1, 12.4.3, 23.1.2: whether the literal's own Type formation has rules beyond each part's position: a Dictionary
        // literal's entries share one key and one value Type, its literal keys are compared by value, and the Dictionary Type it forms
        // holds its own Constraint on the key Type, whatever the number of its entries (design review 1, B1; implementation review 1,
        // IR-B1). A call that selects no candidate binds such a literal as with no expected Type when its parts alone form that Type,
        // so its binder judges the formation (FormsItsTypeAlone). An array's one element Type (4.3.1) is not bound so: binding an
        // unselected array whole gave eight records dev does not give, Unsupported_Kd among them (G267fd revision 3), so its parts are
        // completed one by one as on dev.
        public bool FormsItsType => this.entries is not null;

        public Koto this[int index]
            => this.elements?[index] ?? (index % 2 == 0 ? this.entries![index / 2].Key : this.entries![index / 2].Value);

        // The child position of part `index` (ChildPattern): an element's own index, a key's 0 and a value's 1.
        public int Position(int index) => this.elements is null ? index % 2 : index;
    }

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

    // The parameter Type at child position `index` of one part `core` whose position's is `pattern`: an aggregate literal part's
    // (ChildPattern), a join's results' the join's own; AddChildPosition's, and the position rows' for their patterns.
    private static BoundType? PartChildPattern(Koto core, int index, BoundType? pattern)
        => new AggregateParts(core).Exist ? ChildPattern(core, index, pattern) : pattern;

    // A walk's position of one argument part; ClosePosition removes it with every part appended after it.
    private int OpenPosition(Koto part)
    {
        var start = this.positionParts.Count;
        this.positionParts.Add(part);
        return start;
    }

    private void ClosePosition(int start) => this.positionParts.RemoveRange(start, this.positionParts.Count - start);

    // The child positions of [start, end), by index (SPEC 10.5.1; the owner, 2026-10-31): of one part, its aggregate literal's
    // elements (bound or not: the late evidence walks a bound one's), or the one position of a join's results its call classifies; of
    // several parts, a join's, those of its aggregate parts (AggregateParts.Children), so that arrays of unequal length have the longest
    // one's.
    private int ChildPositions(int start, int end)
    {
        if (end - start != 1)
        {
            var count = 0;
            for (var i = start; i < end; i++)
            {
                if (this.IsAggregatePart(this.positionParts[i]))
                {
                    count = Math.Max(count, new AggregateParts(this.positionParts[i]).Children);
                }
            }

            return count;
        }

        return this.ChildPositions(this.positionParts[start]);
    }

    // The child positions of one part: its aggregate literal's child positions, or the one position of a join's results.
    private int ChildPositions(Koto part)
    {
        part = KotoHelper.UnwrapParentheses(part);
        var parts = new AggregateParts(part);
        return parts.Exist ? parts.Children : this.ComposedJoin(part) is null ? 0 : 1;
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
        if (new AggregateParts(part).Exist)
        {
            this.AddIndexedChild(part, index);
        }
        else
        {
            this.AddResults(this.ComposedJoin(part)!);
        }

        return PartChildPattern(part, index, pattern);
    }

    // Appends a part's child `index` (AggregateParts), formed by AddResultPart: its element there, none for a Tuple or array without one
    // (Tuple results of other lengths wait under U1.6g (a) and fail their shape per candidate), a Dictionary's keys or values.
    private void AddIndexedChild(Koto part, int index)
    {
        var parts = new AggregateParts(part);
        for (var i = index; i < parts.Count; i += parts.Stride)
        {
            this.AddResultPart(parts[i]);
        }
    }

    // The join of a control-flow part its call completes by its results: a Join (ClassifiedByCall), or a nested join that waits for its
    // position (WaitingResult), walked as a result source's is (AddResultPart); null otherwise.
    private Koto? ComposedJoin(Koto part)
        => this.KindOf(part) == PartKind.Join || this.WaitingResult(PartCore(part)) is not null ? ControlFlowExpression(part) : null;

    // The kind of a position of several parts, a join's, typed first (SPEC 14.9.2 step 2): Typed at the common Type of its typed parts,
    // whatever else it holds, or with no Type when it has no part; Call when its typed parts have no common Type (U1.6g (a)) or, with
    // no typed part, when a part awaits its call
    // (AwaitsCall), then Aggregate when its parts are aggregates, then Literal when only literals remain. A failed part (PartFailed)
    // makes the position Call (U1.6h site 3; SPEC 10.5.1, 23.1.2): it fixes no complete Type, so the position waits for its call.
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
            return PartKind.Call; // U1.6h site 3: Waiting under 10.5.1.
        }

        if (end == start || (type = this.PositionType(start, end, this.ConstraintScope(this.positionParts[start]), out var typed)) is not null)
        {
            return PartKind.Typed;
        }

        // SPEC 10.5.1, 14.9.2 step 2 (U1.6g (a)): typed parts with no common Type fix no Type without the call: the position waits.
        return (waits || typed, literal) switch
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
