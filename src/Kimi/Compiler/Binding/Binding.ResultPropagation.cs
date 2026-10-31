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
//   bound once, at the decided Type (BindWaiting from BindWaitedSources, RequireAtPosition, DecideWaiting) or, with nothing decided, at
//   its defaults (AwaitsWithoutCandidate, DefaultWaitingPart); (3) the late fit of a bound expression that awaits its position
//   (AwaitsPosition: a Tuple, array or dictionary literal with a part of Type Never or a part that awaits; a result target every value
//   source of which awaits or has Type Never; through parentheses, labels and expression bodies), which is no evidence
//   beside one that is (SPEC 14.9.2 step 2, 4.3, 10.8) and takes the Type that a join, its literal's element evidence or its selected
//   call decides (TryPropagate); (4) where no source supplies a candidate, the join's Type formed part by part
//   (Binding.ResultPropagation.Parts.cs) or, for a call's control-flow argument, the wait for the call that classifies it
//   (AwaitsWithoutCandidate; DECISIONS BU-43), whose late fit takes a waiting literal and a waiting list or fill literal by the shape
//   it constructs, writing nothing; an anonymous function among its results waits for the call, which completes it (TakeWaiting) and
//   then decides the join part by part, the sources holding it waiting until then (Completed, DecideTaken); (5) the join's one
//   outcome (ResultContext.Outcome, DECISIONS BU-43), written once where it becomes true: Waiting; Conflict, its own record;
//   Dependent, resting on its first failed part or on the join it waited for (Cause), with no record (SPEC 23.1.2); Decided. A part's
//   failure is read from who decides it (PartFailed). A join tree (a join holding nested joins through its sources, the forms that
//   deliver them and Tuple, array and Dictionary literals, none supplying a candidate) is judged once, at its root, each conflict
//   with one owner: a conflict among a join's own typed parts (bound parts, functions its call completed, shapes), fixed when the
//   join decides, is that join's own record, found by its own formation and by no other (JudgeOwn), the innermost such join recording
//   it and every enclosing join resting on it as on a failed part; a conflict among literal defaults, which are not fixed before outer
//   constraints (SPEC 14.9.2 step 3), is the root's own record, found over every position of the flattened tree where no typed part
//   decides (AwaitsWithoutCandidate). A failed part stops no formation: the others are still judged. Every waited source is bound
//   exactly once, by the root's pass (BindWaitedSources), and a nested join bound there honours the outcome of the join it waited for
//   (DecideWaiting); no join decides by itself while an enclosing join still decides, and one bound with no Type under no failed join
//   is a root.
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
            var shaped = TupleShapeAt(tuple, expected);
            for (var i = 0; i < tuple.Elements.Count; i++)
            {
                var element = shaped ? expected!.Components[i] : null;
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
    // that waits unbound, the Type of a part that awaits its call (Call: AwaitsCall, whatever its kind; SPEC 10.5.1; DECISIONS BU-43), or
    // several; a literal bound once, or a part its call completed, is decided.
    [Flags]
    private enum Awaiting : byte
    {
        None = 0,
        Never = 1,
        Literal = 2,
        Call = 4,
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
            _ when this.AwaitsCall(node) => Awaiting.Call, // SPEC 10.5.1: a part its call completes takes the Type there.
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

    // A result target awaits when it delivers a value and each of its value sources awaits, has Type Never, or waited and holds only
    // what its call completed since (Completed), which awaits nothing but the join's decision.
    private Awaiting SourcesAwait(ResultContext context)
    {
        var awaiting = Awaiting.None;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.SourceNodes[i] is not { } source)
            {
                return Awaiting.None;
            }

            if (context.Sources[i] is null)
            {
                var deferred = this.AwaitsFor(source); // Once per source: Deferred takes it rather than walking the source again.
                if (!this.Deferred(source, deferred))
                {
                    return Awaiting.None;
                }

                awaiting |= deferred;
            }
            else if (!ReferenceEquals(context.Sources[i], BoundType.Never))
            {
                var awaits = this.AwaitsFor(source);
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
    // is decided later (DecidedLater): it is no evidence, and it is bound once, at that Type or at its default (BindWaiting).
    private bool WaitsForPosition(Koto node, BoundType? expected)
        => expected is null && node.BindingState == BindingState.Unvisited && IsUnfittedLiteral(node) && this.DecidedLater(node);

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

    // A part that waited for its position's Type: unbound, not failed (PartFailed), and awaiting its position, holding what its call
    // completed since (Completed), or a call the probe deferred (Call, SPEC 10.5.1), which it waits to take with the rest of its join
    // (SPEC 14.9.2 steps 2-3; DECISIONS BU-43).
    private bool Deferred(Koto source) => this.Deferred(source, null);

    // The same with the source's Awaiting (AwaitsFor) when the caller already has it, so the source is not walked twice.
    private bool Deferred(Koto source, Awaiting? awaits)
        => !this.PartFailed(source) && (this.AwaitsCall(source) || (source.BindingState is BindingState.Unvisited or BindingState.Unresolved &&
            ((awaits ?? this.AwaitsFor(source)) != Awaiting.None || this.Completed(source))));

    // A result target that waits for its position's Type (AwaitsWithoutCandidate).
    private ResultContext? WaitingResult(Koto node)
        => DeliversResult(node) && this.resultContexts.TryGetValue(node, out var context) && context.Outcome == JoinOutcome.Waiting ? context : null;

    // A result target that waited for its position and is unresolved since, which its position decides (DecideWaiting): one still
    // waiting, or one its root judged (JudgeNested: Conflict, Dependent) and binds by its pass (BindWaitedSources).
    private ResultContext? WaitedResult(Koto node)
        => DeliversResult(node) && node.BindingState == BindingState.Unresolved && this.resultContexts.TryGetValue(node, out var context) &&
            context.Outcome is JoinOutcome.Waiting or JoinOutcome.Conflict or JoinOutcome.Dependent ? context : null;

    // Whether a waited source holds a join that waited (WaitedResult), through the forms that deliver it and the parts of Tuple, array
    // and Dictionary literals: binding the source binds that join at its outcome.
    private bool HoldsWaitedResult(Koto part)
    {
        if (DeliveredOperand(part) is { } operand)
        {
            return this.HoldsWaitedResult(operand);
        }

        var holds = false;
        switch (part)
        {
            case TupleLiteralKoto tuple:
                foreach (var element in tuple.Elements)
                {
                    holds |= this.HoldsWaitedResult(element);
                }

                return holds;
            case ArrayLiteralKoto array:
                foreach (var element in array.Elements)
                {
                    holds |= this.HoldsWaitedResult(element);
                }

                return holds;
            case DictionaryLiteralKoto dictionary:
                foreach (var entry in dictionary.Entries)
                {
                    holds |= this.HoldsWaitedResult(entry.Key) | this.HoldsWaitedResult(entry.Value);
                }

                return holds;
            default:
                return this.WaitedResult(part) is not null;
        }
    }

    // SPEC 23.1.2 (DECISIONS BU-43): whether a part's own binding failed, read from who decides it: a delivering form's operand; a
    // result target by its published outcome (JoinOutcome.Conflict or Dependent) or its own failure; a Tuple, array, fill or dictionary
    // literal by its own failure only, its parts being judged at their positions; any other part unbound, not a part waiting for its
    // call (IsWaitingPart), with a cause Binding recorded (HasFailureCauses: a failure, a recovery or a failed prerequisite).
    private bool PartFailed(Koto part)
    {
        if (DeliveredOperand(part) is { } operand)
        {
            return this.PartFailed(operand);
        }

        if (this.resultContexts.TryGetValue(part, out var context))
        {
            return part.BindingFailure != BindingFailure.None || context.Outcome is JoinOutcome.Conflict or JoinOutcome.Dependent;
        }

        if (part is TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto)
        {
            return part.BindingFailure != BindingFailure.None || IsRecovery(part, out _);
        }

        return part.BoundType is null && !this.IsWaitingPart(part) && this.HasFailureCauses(part);
    }

    // SPEC 10.5.1 (DECISIONS BU-43): whether a source that waited was completed since by its call (TakeWaiting), through the forms that
    // made its anonymous functions wait (CallableTarget): bound, a form that delivers one, an aggregate each of whose parts at least one
    // (CallableParts) was, or a waiting result target each of whose waited sources was.
    private bool Completed(Koto source)
    {
        if (DeliveredOperand(source) is { } operand)
        {
            return this.Completed(operand);
        }

        if (this.WaitingResult(source) is not { } context)
        {
            return source.BoundType is not null || (CallableParts(source) is { Count: > 0 } parts && this.AllCompleted(parts));
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

    private bool AllCompleted(List<Koto> parts)
    {
        foreach (var part in parts)
        {
            if (!this.Completed(part))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.5.1 (DECISIONS BU-43): a call's waiting join that awaited its position for the anonymous functions among its results, which
    // its call completed (TakeWaiting), decides again: with nothing else awaiting, at once with no expectation, as a join none of whose
    // sources supplies a candidate (the sources holding them are Deferred, so its Type is formed part by part and they take it), its
    // call then fitting it as a bound argument; otherwise under its call's argument probe, where its one formation fails it at once
    // when those functions, now typed parts, conflict, and else it waits for its call again. False when it fails, on which the call
    // rests.
    private bool DecideTaken(Koto argument)
        => ControlFlowExpression(argument) is not { } join || this.WaitingResult(join) is null ||
            (this.AwaitsPosition(join)
                ? this.BindIndependentArgument(argument, this.ConstraintScope(join)) is not null || this.WaitingResult(join) is not null
                : this.BindNode(join, this.ConstraintScope(join), null) is not null);

    // SPEC 14.9.2 (DECISIONS BU-43): a result target that waited for its position is decided when its position binds it, honouring
    // the outcome it has: judged by its root as a Conflict, it binds its waited sources at the Type its position decided (none without
    // one) and fails at itself, its own record; as Dependent, it binds them so and rests on its cause (SPEC 23.1.2: their own records
    // are independent of it). Still Waiting, bound with no Type by the pass of the join it waited for (PositionTarget) once that join
    // failed, it rests on that join's record or cause (SPEC 23.1.2: the root judged its parts).
    // Otherwise its sources take the Type the position supplies, as a Target Result Type fixed by an outer expectation, or, without
    // one, it decides as a root (DecideResult); one its call bound meanwhile supplies the Type it took, and one holding what its call
    // completed is Deferred (Completed).
    private BoundType? DecideWaiting(Koto node, ResultContext context, BoundType? expected)
    {
        if (context.Outcome == JoinOutcome.Waiting && expected is null && this.PositionTarget(node, out _) is { } position &&
            this.resultContexts.TryGetValue(position, out var target) && target.Outcome is JoinOutcome.Conflict or JoinOutcome.Dependent)
        {
            context.RestOn(target.Outcome == JoinOutcome.Conflict ? position : target.Cause!);
        }

        if (context.Outcome is JoinOutcome.Conflict or JoinOutcome.Dependent)
        {
            this.BindWaitedSources(node, context, ResultExpectation(node, expected));
            return context.Outcome == JoinOutcome.Conflict ? this.Fail(node, BindingFailure.TypeMismatch) : this.CompleteDependent(node, context.Cause!);
        }

        context.Outcome = JoinOutcome.Undecided;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.Sources[i] is null && context.SourceNodes[i] is { BoundType: not null } source)
            {
                context.Sources[i] = this.BindWaiting(source, null);
            }
        }

        if ((context.Expected = ResultExpectation(node, expected)) is { } type)
        {
            this.BindWaitedSources(node, context, type);
        }

        return this.DecideResult(node, context);
    }

    // SPEC 14.9.2 step 2 (DECISIONS BU-43): a join none of whose value sources supplies a candidate. Its tree is judged first
    // (JudgeNested), then its own formation (JudgeOwn): a conflict among its typed parts fails it at once (Conflict, its own record, on
    // which an outer join rests), and a failed part leaves it Dependent on that part's record (SPEC 23.1.2). Clean, it waits for its
    // position's Type (true) when a source waited for its Type and that Type is decided later (DecidedLater), or when it is a call's
    // control-flow argument whose results are not Dictionaries: its call classifies it (ClassifiedByCall, SPEC 10.5.1) and completes
    // it at the selected candidate's Type. Otherwise it is the root of its tree: a conflict among the literal defaults of the flattened
    // tree is its own record (step 3); else the formed Type is the join's one candidate, at which its waited sources are bound
    // (DecideResult, BindWaitedSources); failed or forming nothing, it binds them with no Type.
    private bool AwaitsWithoutCandidate(Koto node, ResultContext context, List<BoundType> types)
    {
        var waited = false;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            waited |= context.Sources[i] is null && context.SourceNodes[i] is { } source && this.Deferred(source);
        }

        // A join its position decides later is judged with its root's tree, once: the root's JudgeNested judges the held tree in
        // post-order, an inner conflict still the innermost join's record (DecideWaiting's Conflict row); it judges only itself here.
        var later = waited && DeliversResult(node) && this.DecidedLater(node);
        if (!later)
        {
            this.JudgeNested(node, context);
        }

        var common = this.JudgeOwn(node, context, out var shape, out var defaults);
        if (context.Outcome == JoinOutcome.Undecided && (later || (shape is not DictionaryLiteralKoto && this.ClassifiedByCall(node, shape))))
        {
            context.Outcome = JoinOutcome.Waiting;
            return true;
        }

        if (defaults)
        {
            context.Conflict();
        }

        types.Clear(); // A nested result decided above reused the list.
        if (common is not null && context.Outcome == JoinOutcome.Undecided)
        {
            types.Add(common);
        }
        else
        {
            this.BindWaitedSources(node, context, null);
        }

        return false;
    }

    // SPEC 14.9.2 step 3: binds a source that waited at `type` (null: its defaults). Returns the Type the source supplies, null when it fails.
    private BoundType? BindWaiting(Koto source, BoundType? type)
        => this.BindNode(source, this.ConstraintScope(source), type) is null ? null : this.SuppliedType(source);

    // SPEC 14.9.2 step 2: the candidates of a join, Never supplying none: a source that awaits its position supplies none and is checked
    // by its late Type (BindWaitedSources), with or without a fixed Target Result Type; a source that waited unbound supplies none either;
    // a failed source leaves the join Dependent on it (SPEC 23.1.2), and any other unbound source makes the join pending. Returns
    // whether a source awaits.
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
                if (!deferred && context.SourceNodes[i] is { } failed && this.PartFailed(failed))
                {
                    context.RestOn(failed);
                }
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

    // SPEC 14.9.2 steps 3 and 4, 23.1.2 (DECISIONS BU-43): the one pass that binds a join's waited sources, each once. At `type`, the
    // Target Result Type the join or its position decided (a failed join's too): a source that waited unbound is bound at it, which
    // checks its literals, and fits it; a bound one that awaits, or does not fit as it is, takes it when its late Type fits (U1.6g (b)),
    // a bound one that does not await failing at itself otherwise (the join resting on it). With no Type, the join having failed
    // (Conflict, Dependent) or formed nothing: a waited source holding a literal takes its defaults, and one holding a join that waited
    // (HoldsWaitedResult) is bound so that the join honours its outcome (DecideWaiting); under a failed join, a source holding neither
    // rests on the join's record or cause (CompleteDependent). The first misfit is returned.
    private BoundType? BindWaitedSources(Koto node, ResultContext context, BoundType? type)
    {
        BoundType? failed = null;
        var rests = context.Outcome is JoinOutcome.Conflict or JoinOutcome.Dependent;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.SourceNodes[i] is not { } source || ReferenceEquals(context.Sources[i], BoundType.Never))
            {
                continue;
            }

            var waited = context.Sources[i] is null;
            var held = waited && this.HoldsWaitedResult(source);
            var awaits = !waited && type is not null && this.AwaitsPosition(source);
            if (waited ? !held && !this.Deferred(source) : type is null || (!awaits && this.FitsTypeAt(context.Sources[i]!, type, node)))
            {
                continue;
            }

            bool fits;
            if (!waited)
            {
                // U1.6g (b), SPEC 7.7.4, 23.1.2: a bound part that does not fit takes the Type through its late adaptation (an Item by
                // erasure); one that cannot is its own record, at the part, on which the join rests, as a waited part's misfit is.
                fits = this.TryPropagate(source, type!, node);
                if (!fits && !awaits && !rests)
                {
                    this.FailMismatch(source, source, context.Sources[i]!, type!);
                    context.RestOn(source);
                }
            }
            else if (type is not null)
            {
                fits = this.BindWaiting(source, type) is { } bound && this.FitsTypeAt(bound, type, node);
            }
            else if (!rests || held || this.SuppliesDefault(source))
            {
                fits = this.BindWaiting(source, null) is not null;
            }
            else
            {
                this.CompleteDependent(source, context.Outcome == JoinOutcome.Conflict ? node : context.Cause!);
                continue;
            }

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

        if (!this.AwaitsCall(node) && !this.AwaitsPosition(node) && !this.Deferred(node))
        {
            return this.LateAdaptation(node, target); // A bound part; one holding what its call completed is formed from its parts below.
        }

        if (DeliveredOperand(node) is { } operand)
        {
            return this.LateWrite(node, this.LateType(operand, target));
        }

        var kind = this.KindOf(node);
        var late = node switch
        {
            TupleLiteralKoto tuple => this.LateTupleType(tuple, target),
            ArrayLiteralKoto array => this.LateArrayType(array, target),
            DictionaryLiteralKoto dictionary => this.LateDictionaryType(dictionary, target),
            _ when IsUnfittedLiteral(node) => this.FitsInputLiteral(node, target, this.ConstraintScope(node)) ? target : null, // A waiting literal.
            _ when kind is PartKind.Function or PartKind.Case => // One waiting for its call, fitted as an argument by its header
                this.FitArgument(node, target, this.ConstraintScope(node), node, out _) == CandidateApplicability.Inapplicable ? null : target,
            _ when kind is PartKind.Call or PartKind.Reference => target, // Completed by its call at this position's Type (SPEC 10.1).
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
        if (!TupleShapeAt(tuple, target))
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
        if (!this.ArrayShapeAt(array, target))
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

    // SPEC 4.3: whether an array literal has the target's shape: its own fixed array's, or the one it constructs there (ConstructsAt).
    private bool ArrayShapeAt(ArrayLiteralKoto array, BoundType target)
        => target.Components.Count == 1 &&
            (array.BoundType is { Kind: BoundTypeKind.FixedArray } own ? SameArrayShape(own, target) : this.ConstructsAt(array, target));

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
