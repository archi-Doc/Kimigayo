// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Result propagation (SPEC 14.9.2 step 3, 3.8, 4.3, 12.3; DECISIONS R4-M52, BU-4, BU-9): a literal takes its position's Type.
// - Responsibility: (1) α, the Type a Tuple literal takes from its elements' Types at its position's Type (FittedTupleType: an element
//   of Type Never takes its position's element Type, every other element keeps its own Type) where the position is known when the
//   literal is bound (BindTuple); (2) the literal deferral (SPEC 14.9.2 step 3: a literal result is not fixed before outer
//   constraints): an unfitted literal reached without an expected Type while its position's Type is decided later (DecidedLater: the
//   one walk through the forms that deliver it and the Tuple, array and dictionary literals it is a part of, to an array or dictionary
//   literal collecting its element evidence, a join collecting its sources without a Target Result Type, or a call deciding a
//   control-flow argument) waits unbound (WaitsForPosition), as does an aggregate holding it, an array or dictionary literal no part of
//   which establishes its element Type there, and a join all of whose sources await, one unbound (AwaitsWithoutCandidate); each is
//   bound once, at the decided Type (BindWaiting from PropagateSources, RequireAtPosition, DecideWaiting) or, with nothing decided, at
//   its defaults (AwaitsWithoutCandidate, DefaultWaitingPart); (3) the late fit of a bound expression that awaits its position
//   (AwaitsPosition: a Tuple, array or dictionary literal with a part of Type Never or a part that awaits; a result target every value
//   source of which awaits or has Type Never; through parentheses, labels and expression bodies), which is no evidence
//   beside one that is (SPEC 14.9.2 step 2, 4.3, 10.8) and takes the Type that a join, its literal's element evidence or its selected
//   call decides (TryPropagate); (4) where no source supplies a candidate, the join's Type formed part by part
//   (Binding.ResultPropagation.Parts.cs) or, for a call's control-flow argument, the wait for the call that classifies it
//   (AwaitsWithoutCandidate; DECISIONS BU-43), whose late fit takes a waiting literal and a waiting list or fill literal by the shape
//   it constructs, writing nothing; an anonymous function among its results waits for the call, which completes it (TakeWaiting) and
//   then decides the join part by part, the sources holding it waiting until then (Completed, DecideTaken).
// - Inputs: the bound expression, its parts' Types, the value sources a join recorded (ResultContext.SourceNodes) and whether it still
//   collects them (ResultContext.Collecting), the literal collecting its element evidence (collectingElements), the target Type.
// - Outputs: LateTypeOf, the Type the expression takes at the target, computed from its parts: an array or dictionary literal its
//   target's element, key and value Types only when every part fits them, a bound part that does not await its own Type or the one
//   adaptation a Tuple argument's part takes there (LateAdaptation); TryPropagate writes that Type into the expression and every
//   awaiting part and wrapper the computation passed, and those adaptations, when it fits the target at the use, and writes nothing
//   otherwise; a waiting part bound at its position's Type.
// - Invariants: a literal is bound once, unfitted until then; a bound literal is decided and never awaits, so no bound literal, and
//   no node other than an awaiting aggregate, result target or wrapper, is written (SPEC 14.9.2: a typed binding is never reinferred);
//   a bound expression holds no waiting part.
// - Prohibitions: no inference or record here; a waiting part is bound only by BindNode at its decided Type or default, which reports
//   its own misfit at the literal; the reader reports a misfit of the late fit.
public sealed partial class Binding
{
    // The writes of the last late Type computation, which TryPropagate applies: an aggregate's or result target's Type, and a bound
    // part's adaptation to the Type at its position (its erasure or its one implicit operation, SPEC 10.2).
    private readonly List<(Koto Node, BoundType Type)> lateWrites = new();
    private readonly List<(Koto Node, BoundType Type)> lateAdaptations = new();

    // The array or dictionary literal whose element evidence is being collected, which decides its parts' Type (DecidedLater).
    private Koto? collectingElements;

    // SPEC 14.9.1: a result target whose value is its result, a selection, `do` or `loop`; a function's value is itself, not its result.
    private static bool DeliversResult(Koto node) => node is IfKoto or MatchKoto or DoKoto or LoopKoto;

    // The operand a form delivers with its position's Type: parentheses, a label, an expression body, read from the syntax so that it
    // holds before they are bound. An @move or @copy operand takes no Type from its position (SPEC 12.3.1, 13.5.2; DECISIONS BU-9).
    private static Koto? DeliveredOperand(Koto node) => node switch
    {
        ParenthesizedKoto parentheses => parentheses.Operand,
        LabeledKoto label => label.Target,
        CodeBlockKoto { IsExpressionBody: true } block => block.Items[0],
        _ => null,
    };

    private static bool SameArrayShape(BoundType own, BoundType target)
        => own.Kind == target.Kind && ReferenceEquals(own.Symbol, target.Symbol) && own.Length == target.Length &&
            ReferenceEquals(own.LengthExpression, target.LengthExpression);

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

    // SPEC 3.8: a Tuple literal binds its elements at its position's element Types, a bound element that awaits taking its Type first,
    // and takes α's Type (FittedTupleType).
    private BoundType? BindTuple(TupleLiteralKoto tuple, BindingScope scope, BoundType? expected)
    {
        var buffer = this.RentTypes(tuple.Elements.Count);
        try
        {
            var complete = true;
            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var element = expected is { Kind: BoundTypeKind.Tuple } && expected.Components.Count == tuple.Elements.Count ? expected.Components[i] : null;
                this.PropagateBound(tuple.Elements[i], element);
                var type = this.BindNode(tuple.Elements[i], scope, element);
                buffer[i] = type!;
                complete &= type is not null;
            }

            return Complete(tuple, complete ? this.FittedTupleType(buffer.AsSpan(0, tuple.Elements.Count), expected) : null);
        }
        finally
        {
            this.typeScratch.Return(buffer, clearArray: true);
        }
    }

    // What an expression awaits its position for (SPEC 14.9.2 step 3): the Type of a part of Type Never, the Type or default of a literal
    // that waits unbound, the Type of an anonymous function that waits unbound for its call (SPEC 10.5.1; DECISIONS BU-43), or several;
    // a literal or anonymous function bound once is decided.
    [Flags]
    private enum Awaiting : byte
    {
        None = 0,
        Never = 1,
        Literal = 2,
        Callable = 4,
    }

    // SPEC 14.9.2 step 3: whether an expression, bound or waiting, takes its position's Type once that is decided.
    private bool AwaitsPosition(Koto node) => this.AwaitsFor(node) != Awaiting.None;

    // Whether a part that waits holds a literal, whose default it takes when no position decides its Type; one holding only Never parts
    // has no default and takes the Type the others supply (AwaitsWithoutCandidate, DefaultWaitingPart).
    private bool SuppliesDefault(Koto part) => (this.AwaitsFor(part) & Awaiting.Literal) != 0;

    private Awaiting AwaitsFor(Koto node)
    {
        if (DeliveredOperand(node) is { } operand)
        {
            return this.AwaitsFor(operand);
        }

        return node switch
        {
            TupleLiteralKoto tuple => this.PartsAwait(tuple.Elements),
            ArrayLiteralKoto array => this.PartsAwait(array.Elements),
            DictionaryLiteralKoto dictionary => this.EntriesAwait(dictionary.Entries),
            _ when DeliversResult(node) => this.resultContexts.TryGetValue(node, out var context) ? this.SourcesAwait(context) : Awaiting.None,
            FunctionKoto { IsAnonymous: true, BindingState: BindingState.Unvisited } => Awaiting.Callable,
            _ => node.BindingState == BindingState.Unvisited && IsUnfittedLiteral(node) ? Awaiting.Literal : Awaiting.None,
        };
    }

    private Awaiting PartsAwait(List<Koto> parts)
    {
        var awaiting = Awaiting.None;
        for (var i = 0; i < parts.Count; i++)
        {
            awaiting |= this.PartAwaits(parts[i]);
        }

        return awaiting;
    }

    private Awaiting EntriesAwait(List<DictionaryLiteralEntry> entries)
    {
        var awaiting = Awaiting.None;
        for (var i = 0; i < entries.Count; i++)
        {
            awaiting |= this.PartAwaits(entries[i].Key) | this.PartAwaits(entries[i].Value);
        }

        return awaiting;
    }

    private Awaiting PartAwaits(Koto part) => ReferenceEquals(part.BoundType, BoundType.Never) ? Awaiting.Never : this.AwaitsFor(part);

    // A result target awaits when it delivers a value and each of its value sources awaits or has Type Never.
    private Awaiting SourcesAwait(ResultContext context)
    {
        var awaiting = Awaiting.None;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.SourceNodes[i] is not { } source)
            {
                return Awaiting.None;
            }

            if (!ReferenceEquals(context.Sources[i], BoundType.Never))
            {
                var awaits = context.Sources[i] is null && !this.Deferred(source) ? Awaiting.None : this.AwaitsFor(source);
                if (awaits == Awaiting.None)
                {
                    return Awaiting.None;
                }

                awaiting |= awaits;
            }
        }

        return awaiting;
    }

    // SPEC 14.9.2 step 3, 4.3, 10.5.1, 12.3: an unfitted literal reached without an expected Type waits, unbound, while its position's Type
    // is decided later (DecidedLater): it is no evidence, and it is bound once, at that Type or at its default (BindWaiting). An anonymous
    // function that is a result of a call's control-flow argument waits for that call (SPEC 10.5.1: such an argument is Waiting;
    // DECISIONS BU-43), which completes it as it completes a waiting argument (TakeWaiting).
    private bool WaitsForPosition(Koto node, BoundType? expected)
        => expected is null && node.BindingState == BindingState.Unvisited &&
            (IsUnfittedLiteral(node) ? this.DecidedLater(node) :
                node is FunctionKoto { IsAnonymous: true } && this.PositionTarget(node, out _) is { } target && this.UnderArgumentProbe(target));

    // The result target to which the position an expression supplies belongs, or null. The one walk passes the forms that deliver it
    // (DeliveredOperand) and the Tuple, array (a fill's included, DECISIONS BU-43) and dictionary literals it is a part of; `collecting`
    // tells whether one of them collects its element evidence (collectingElements, SPEC 4.3).
    private Koto? PositionTarget(Koto node, out bool collecting)
    {
        collecting = false;
        var source = node;
        while (source.Parent is { } parent &&
            (ReferenceEquals(DeliveredOperand(parent), source) || parent is TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto))
        {
            collecting |= ReferenceEquals(parent, this.collectingElements);
            source = parent;
        }

        return ResultTarget(source);
    }

    // Whether the Type of the position an expression supplies is decided after the expression is reached: by an array or dictionary
    // literal that collects its element evidence (SPEC 4.3), by the call whose argument probe a join it supplies is under (WaitsForCall,
    // SPEC 10.5.1), or by that join while it collects its sources without a Target Result Type (SPEC 14.9.2).
    private bool DecidedLater(Koto node)
        => this.PositionTarget(node, out var collecting) is { } target
            ? collecting || this.WaitsForCall(target) ||
                (this.resultContexts.TryGetValue(target, out var context) && context.Collecting && context.Expected is null)
            : collecting;

    // A part that waited for its position's Type: unbound, without a failure of its own, and awaiting its position or holding what its
    // call completed since (Completed), which it waits to take with the rest of its join (SPEC 14.9.2 steps 2-3; DECISIONS BU-43).
    private bool Deferred(Koto source)
        => source.BindingState is BindingState.Unvisited or BindingState.Unresolved && source.BindingFailure == BindingFailure.None &&
            (this.AwaitsPosition(source) || this.Completed(source));

    // A result target that waits for its position's Type (AwaitsWithoutCandidate).
    private ResultContext? WaitingResult(Koto node)
        => DeliversResult(node) && this.resultContexts.TryGetValue(node, out var context) && context.Waiting ? context : null;

    // SPEC 10.5.1 (DECISIONS BU-43): whether a source that waited was completed since by its call (TakeWaiting), through the forms that
    // made its anonymous functions wait (PositionTarget): bound, a form that delivers one, a waiting Tuple, array or dictionary literal
    // (IsAggregatePart) each of whose parts was, at least one, or a waiting result target each of whose waited sources was.
    private bool Completed(Koto source)
    {
        if (DeliveredOperand(source) is { } operand)
        {
            return this.Completed(operand);
        }

        if (this.WaitingResult(source) is not { } context)
        {
            return source.BoundType is not null || (this.IsAggregatePart(source) && this.PartsCompleted(source));
        }

        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is null && context.SourceNodes[i] is { } waited && !this.Completed(waited))
            {
                return false;
            }
        }

        return true;
    }

    private bool PartsCompleted(Koto aggregate)
    {
        if (aggregate is DictionaryLiteralKoto dictionary)
        {
            foreach (var entry in dictionary.Entries)
            {
                if (!this.Completed(entry.Key) || !this.Completed(entry.Value))
                {
                    return false;
                }
            }

            return dictionary.Entries.Count > 0;
        }

        var parts = aggregate is TupleLiteralKoto tuple ? tuple.Elements : ((ArrayLiteralKoto)aggregate).Elements;
        foreach (var part in parts)
        {
            if (!this.Completed(part))
            {
                return false;
            }
        }

        return parts.Count > 0;
    }

    // SPEC 10.5.1 (DECISIONS BU-43): a call's waiting join that awaited its position for the anonymous functions among its results, which
    // its call completed (TakeWaiting), is decided at once with no expectation, as a join none of whose sources supplies a candidate:
    // the sources holding them are Deferred, so its Type is formed part by part and they take it; its call then fits it as a bound
    // argument. False when that decision fails, on which the call rests.
    private bool DecideTaken(Koto argument)
        => ControlFlowExpression(argument) is not { } join || this.WaitingResult(join) is null || this.AwaitsPosition(join) ||
            this.BindNode(join, this.ConstraintScope(join), null) is not null;

    // SPEC 14.9.2: a result target that waited for its position is decided when its position binds it: its sources take the Type the
    // position supplies, as a Target Result Type fixed by an outer expectation, or, without one, their defaults; one its call bound
    // meanwhile supplies the Type it took, and one holding what its call completed is Deferred (Completed).
    private BoundType? DecideWaiting(Koto node, ResultContext context, BoundType? expected)
    {
        context.Waiting = false;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is null && context.SourceNodes[i] is { BoundType: not null } source)
            {
                context.Sources[i] = this.BindWaiting(source, null);
            }
        }

        if ((context.Expected = ResultExpectation(node, expected)) is { } type)
        {
            this.PropagateSources(node, context, type);
        }

        return this.DecideResult(node, context);
    }

    // SPEC 14.9.2 step 2 (DECISIONS BU-43): a join none of whose value sources supplies a candidate. It waits for its position's Type
    // (true) when a source waited for its Type and that Type is decided later (DecidedLater), or when it is a call's control-flow
    // argument whose results are not Dictionaries: its call classifies it (ClassifiedByCall, SPEC 10.5.1) and completes it at the
    // selected candidate's Type, a conflict among its typed parts failing it at once. Otherwise its common Type is formed part by part
    // (CommonTypeByParts) and is the join's one candidate; with none, its waiting sources take their defaults.
    private bool AwaitsWithoutCandidate(Koto node, ResultContext context, List<BoundType> types)
    {
        var nested = DeliversResult(node) && this.DecidedLater(node);
        var start = this.positionParts.Count;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (nested && context.Sources[i] is null && context.SourceNodes[i] is { } waited && this.Deferred(waited))
            {
                this.ClosePosition(start);
                return context.Waiting = true;
            }

            if (!ReferenceEquals(context.Sources[i], BoundType.Never) && context.SourceNodes[i] is { } source)
            {
                this.AddResultPart(source);
            }
        }

        var conflict = PartsConflict.None;
        var common = this.CommonTypeByParts(start, this.positionParts.Count, this.ConstraintScope(node), ref conflict);
        var shape = this.PartsShape(start, this.positionParts.Count);
        this.ClosePosition(start);
        if ((conflict & PartsConflict.Typed) == 0 && shape is not DictionaryLiteralKoto && this.ClassifiedByCall(node, shape))
        {
            return context.Waiting = true;
        }

        context.Invalid |= conflict != PartsConflict.None;
        for (var i = 0; common is null && i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is null && context.SourceNodes[i] is { } source && this.Deferred(source))
            {
                context.Sources[i] = this.BindWaiting(source, null);
                context.Pending |= context.Sources[i] is null;
            }
        }

        types.Clear(); // A nested result decided above reused the list.
        if (common is not null)
        {
            types.Add(common);
        }

        return false;
    }

    // SPEC 14.9.2 step 3: binds a source that waited at `type` (null: its defaults). Returns the Type the source supplies, null when it fails.
    private BoundType? BindWaiting(Koto source, BoundType? type)
        => this.BindNode(source, this.ConstraintScope(source), type) is null ? null : this.SuppliedType(source);

    // SPEC 14.9.2 step 2: the candidates of a join, Never supplying none: a source that awaits its position supplies none and is checked
    // by its late Type (PropagateSources), with or without a fixed Target Result Type; a source that waited unbound supplies none either,
    // and any other unbound source makes the join pending. Returns whether a source awaits.
    private bool ResultCandidates(ResultContext context, List<BoundType> types)
    {
        var awaiting = false;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is not { } source)
            {
                var deferred = context.SourceNodes[i] is { } node && this.Deferred(node);
                awaiting |= deferred;
                context.Pending |= !deferred;
            }
            else if (!ReferenceEquals(source, BoundType.Never))
            {
                var awaits = context.SourceNodes[i] is { } node && this.AwaitsPosition(node);
                awaiting |= awaits;
                if (!awaits)
                {
                    types.Add(source);
                }
            }
        }

        return awaiting;
    }

    // SPEC 14.9.2 steps 3 and 4: each source that awaits its position takes the decided Target Result Type: one that waited unbound is
    // bound at it, which checks its literals, and fits it; a bound one takes it when its late Type fits. The first misfit is returned.
    private BoundType? PropagateSources(Koto node, ResultContext context, BoundType common)
    {
        BoundType? failed = null;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.SourceNodes[i] is not { } source || ReferenceEquals(context.Sources[i], BoundType.Never))
            {
                continue;
            }

            var waited = context.Sources[i] is null;
            if (waited ? !this.Deferred(source) : !this.AwaitsPosition(source))
            {
                continue;
            }

            var fits = waited ? this.BindWaiting(source, common) is { } bound && this.FitsTypeAt(bound, common, node) :
                this.TryPropagate(source, common, node);
            context.Sources[i] = this.SuppliedType(source);
            context.Pending |= context.Sources[i] is null;
            failed ??= fits ? null : context.Sources[i];
        }

        return failed;
    }

    // SPEC 14.9.2 step 3: the Type an expression that awaits takes at `target`; null when it cannot take it.
    private BoundType? LateTypeOf(Koto node, BoundType target)
    {
        this.lateWrites.Clear();
        this.lateAdaptations.Clear();
        return this.LateType(node, target);
    }

    // SPEC 14.9.2 step 3: an expression that awaits its position takes `target` when its late Type fits it at `use`.
    private bool TryPropagate(Koto node, BoundType target, Koto use)
    {
        if (this.LateTypeOf(node, target) is not { } late || !this.FitsTypeAt(late, target, use))
        {
            return false;
        }

        foreach (var (part, type) in this.lateWrites)
        {
            Complete(part, type);
        }

        foreach (var (part, type) in this.lateAdaptations)
        {
            this.AdaptAtPosition(part, part.BoundType!, type);
        }

        this.lateWrites.Clear();
        this.lateAdaptations.Clear();
        return true;
    }

    // SPEC 10.2, 7.7.4: a bound part takes the Type at its position as a Tuple argument's part does: a Closure or Function Item by its
    // erasure, judged now as at a selected call (ErasePart), any other part by its one implicit operation (ExpectedAdaptation).
    private void AdaptAtPosition(Koto part, BoundType actual, BoundType type)
    {
        if (this.ErasureSignatureFits(actual, type, part))
        {
            this.ErasePart(part, actual, type);
        }
        else if (this.ExpectedAdaptation(part, actual, type) is { } adaptation)
        {
            this.adaptations[part] = adaptation;
        }
    }

    // A bound part that awaits its position takes `type` when it fits it, before its consumer checks it; its Type, null when unbound.
    private BoundType? PropagateBound(Koto part, BoundType? type)
    {
        if (type is not null && part.BoundType is not null && this.AwaitsPosition(part))
        {
            this.TryPropagate(part, type, part);
        }

        return part.BoundType;
    }

    // A part at its position's Type: one that waited is bound at it (BindWaiting), a bound one that awaits takes it (PropagateBound).
    private BoundType? RequireAtPosition(Koto part, BindingScope scope, BoundType type)
    {
        if (this.Deferred(part))
        {
            this.BindWaiting(part, type);
        }

        this.PropagateBound(part, type);
        return this.RequireType(part, scope, type);
    }

    // SPEC 4.3, 14.9.2 step 3: a part holding literals that waited for an element Type no element established takes its defaults, which
    // supply the default element Type as an awaiting element's own Type does (an unfitted literal part supplied its default already).
    private void DefaultWaitingPart(Koto part, ref BoundType? literalDefault)
    {
        if (!IsUnfittedLiteral(part) && this.Deferred(part) && this.SuppliesDefault(part) && this.BindWaiting(part, null) is { } type &&
            !ReferenceEquals(type, BoundType.Never))
        {
            literalDefault = literalDefault is null ? type : this.CommonOriginType(literalDefault, type) ?? literalDefault;
        }
    }

    // The Type at `target` and the writes that give it; a part that does not await keeps its own Type and is not written.
    private BoundType? LateType(Koto node, BoundType target)
    {
        if (ReferenceEquals(node.BoundType, BoundType.Never))
        {
            return target; // SPEC 3.8: a Never expression fits; the literal around it takes its position's Type.
        }

        if (!this.AwaitsPosition(node))
        {
            return this.LateAdaptation(node, target);
        }

        if (DeliveredOperand(node) is { } operand)
        {
            return this.LateWrite(node, this.LateType(operand, target));
        }

        var late = node switch
        {
            TupleLiteralKoto tuple => this.LateTupleType(tuple, target),
            ArrayLiteralKoto array => this.LateArrayType(array, target),
            DictionaryLiteralKoto dictionary => this.LateDictionaryType(dictionary, target),
            _ when IsUnfittedLiteral(node) => this.FitsInputLiteral(node, target, this.ConstraintScope(node)) ? target : null, // A waiting literal.
            FunctionKoto { IsAnonymous: true } closure => this.ClosureSignatureFits(closure, target) ? target : null, // One waiting for its call.
            _ => DeliversResult(node) ? this.LateResultType(node, target) : null,
        };

        return this.LateWrite(node, late);
    }

    // SPEC 14.9.2 step 3, 10.2 (DECISIONS BU-43): a bound part that does not await keeps its own Type where that fits the target, and
    // otherwise takes the target as a Tuple argument's part does, by its erasure's signature or its one implicit operation, written
    // when the whole fits (AdaptAtPosition).
    private BoundType? LateAdaptation(Koto node, BoundType target)
    {
        if (this.SuppliedType(node) is not { } actual || node.ErasedFunctionType is not null || this.adaptations.ContainsKey(node) ||
            Compatible(actual, target))
        {
            return this.SuppliedType(node);
        }

        var adapted = this.ErasureSignatureFits(actual, target, node) ? target : this.ExpectedAdaptation(node, actual, target, recordPair: false)?.Type;
        if (adapted is null)
        {
            return actual;
        }

        this.lateAdaptations.Add((node, target));
        return adapted;
    }

    // Records the write of a late Type to a bound node; a waiting one, which a call's waiting join holds (ClassifiedByCall), is bound at
    // its join's decision instead.
    private BoundType? LateWrite(Koto node, BoundType? late)
    {
        if (late is not null && node.BoundType is not null)
        {
            this.lateWrites.Add((node, late));
        }

        return late;
    }

    private BoundType? LateTupleType(TupleLiteralKoto tuple, BoundType target)
    {
        if (target.Kind != BoundTypeKind.Tuple || target.Components.Count != tuple.Elements.Count)
        {
            return null;
        }

        var parts = this.RentTypes(tuple.Elements.Count);
        try
        {
            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                if (this.LateType(tuple.Elements[i], target.Components[i]) is not { } part)
                {
                    return null;
                }

                parts[i] = part;
            }

            return this.types.Tuple(parts.AsSpan(0, tuple.Elements.Count));
        }
        finally
        {
            this.typeScratch.Return(parts, clearArray: true);
        }
    }

    // SPEC 4.3: an array literal of the target's shape takes the target's element Type when every element fits it. One waiting unbound,
    // or a list literal its elements typed as Array<T>, has the shape it constructs at the target: a list literal an Array or a fixed
    // array of its count, a fill a fixed array of its length (SPEC 4.3.1, 4.3.3; DECISIONS BU-43).
    private BoundType? LateArrayType(ArrayLiteralKoto array, BoundType target)
    {
        if (target.Components.Count != 1 ||
            (array.BoundType is { Kind: BoundTypeKind.FixedArray } own ? !SameArrayShape(own, target) : !this.ConstructsAt(array, target)))
        {
            return null;
        }

        for (var i = 0; i < array.Elements.Count; i++)
        {
            if (!this.LateFits(array.Elements[i], target.Components[0]))
            {
                return null;
            }
        }

        return target;
    }

    // The shape a waiting array literal constructs at the target (SPEC 4.3.1, 4.3.3).
    private bool ConstructsAt(ArrayLiteralKoto array, BoundType target)
    {
        if (target.Kind != BoundTypeKind.FixedArray)
        {
            return array.FillLength is null && target.Kind == BoundTypeKind.Array && ReferenceEquals(target.Symbol, this.Library.DynamicArray);
        }

        return array.FillCount is not { } count
            ? array.FillLength is null && target.LengthExpression is null && target.Length == array.Elements.Count
            : count.IsConstant ? target.LengthExpression is null && target.Length == count.Value : ReferenceEquals(target.LengthExpression, count);
    }

    // SPEC 12.4.3: a dictionary literal takes the target's key and value Types when every key and value fits them, also one waiting.
    private BoundType? LateDictionaryType(DictionaryLiteralKoto dictionary, BoundType target)
    {
        if (dictionary.BoundType is { Kind: not BoundTypeKind.Dictionary } || target.Kind != BoundTypeKind.Dictionary || target.Components.Count != 2)
        {
            return null;
        }

        for (var i = 0; i < dictionary.Entries.Count; i++)
        {
            if (!this.LateFits(dictionary.Entries[i].Key, target.Components[0]) || !this.LateFits(dictionary.Entries[i].Value, target.Components[1]))
            {
                return null;
            }
        }

        return target;
    }

    private bool LateFits(Koto part, BoundType target) => this.LateType(part, target) is { } late && this.FitsTypeAt(late, target, part);

    // SPEC 14.9.2: nested results propagate the same way; each value source of an awaiting result target takes the target Type.
    private BoundType? LateResultType(Koto node, BoundType target)
    {
        var context = this.resultContexts[node];
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (!ReferenceEquals(context.Sources[i], BoundType.Never) &&
                (this.LateType(context.SourceNodes[i]!, target) is not { } source || !this.FitsTypeAt(source, target, node)))
            {
                return null;
            }
        }

        return target;
    }
}
