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
//   call decides (TryPropagate).
// - Inputs: the bound expression, its parts' Types, the value sources a join recorded (ResultContext.SourceNodes) and whether it still
//   collects them (ResultContext.Collecting), the literal collecting its element evidence (collectingElements), the target Type.
// - Outputs: LateTypeOf, the Type the expression takes at the target, computed from its parts: an array or dictionary literal its
//   target's element, key and value Types only when every part fits them, any other part keeps its own Type; TryPropagate writes that
//   Type into the expression and every awaiting part and wrapper the computation passed when it fits the target at the use, and writes
//   nothing otherwise; a waiting part bound at its position's Type.
// - Invariants: a literal is bound once, unfitted until then; a bound literal is decided and never awaits, so no bound literal, and
//   no node other than an awaiting aggregate, result target or wrapper, is written (SPEC 14.9.2: a typed binding is never reinferred);
//   a bound expression holds no waiting part.
// - Prohibitions: no inference or record here; a waiting part is bound only by BindNode at its decided Type or default, which reports
//   its own misfit at the literal; the reader reports a misfit of the late fit.
public sealed partial class Binding
{
    // The writes of the last late Type computation, which TryPropagate applies.
    private readonly List<(Koto Node, BoundType Type)> lateWrites = new();

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
    // that waits unbound, or both; a literal bound once is decided.
    [Flags]
    private enum Awaiting : byte
    {
        None = 0,
        Never = 1,
        Literal = 2,
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
    // is decided later (DecidedLater): it is no evidence, and it is bound once, at that Type or at its default (BindWaiting).
    private bool WaitsForPosition(Koto node, BoundType? expected)
        => expected is null && node.BindingState == BindingState.Unvisited && IsUnfittedLiteral(node) && this.DecidedLater(node);

    // Whether the Type of the position an expression supplies is decided after the expression is reached. The one walk passes the forms
    // that deliver it (DeliveredOperand) and the Tuple, array and dictionary literals it is a part of; the Type is decided by an array or
    // dictionary literal that collects its element evidence (collectingElements, SPEC 4.3), by the call whose argument probe a join it
    // supplies is under (WaitsForCall, SPEC 10.5.1), or by that join while it collects its sources without a Target Result Type (SPEC
    // 14.9.2).
    private bool DecidedLater(Koto node)
    {
        var source = node;
        while (source.Parent is { } parent &&
            (ReferenceEquals(DeliveredOperand(parent), source) || parent is TupleLiteralKoto or ArrayLiteralKoto { FillLength: null } or DictionaryLiteralKoto))
        {
            if (ReferenceEquals(parent, this.collectingElements))
            {
                return true;
            }

            source = parent;
        }

        if (ResultTarget(source) is not { } target)
        {
            return false;
        }

        return this.WaitsForCall(target) || (this.resultContexts.TryGetValue(target, out var context) && context.Collecting && context.Expected is null);
    }

    // A part that waited for its position's Type: unbound, without a failure of its own, and awaiting its position.
    private bool Deferred(Koto source)
        => source.BindingState is BindingState.Unvisited or BindingState.Unresolved && source.BindingFailure == BindingFailure.None &&
            this.AwaitsPosition(source);

    // A result target that waits for its position's Type (AwaitsWithoutCandidate).
    private ResultContext? WaitingResult(Koto node)
        => DeliversResult(node) && this.resultContexts.TryGetValue(node, out var context) && context.Waiting ? context : null;

    // SPEC 14.9.2: a result target that waited for its position is decided when its position binds it: its sources take the Type the
    // position supplies, as a Target Result Type fixed by an outer expectation, or, without one, their defaults.
    private BoundType? DecideWaiting(Koto node, ResultContext context, BoundType? expected)
    {
        context.Waiting = false;
        if ((context.Expected = ResultExpectation(node, expected)) is { } type)
        {
            this.PropagateSources(node, context, type);
        }

        return this.DecideResult(node, context);
    }

    // SPEC 14.9.2 step 3: a join whose every value source awaits has no candidate of its own. When a source waited for its Type and the
    // join's own Type is decided later (DecidedLater), it waits for that Type (true); otherwise the waiting sources holding literals take
    // their defaults (SPEC 10.2), which supply the candidates, those holding only Never parts then taking the decided Type; with no
    // literal, the sources' own Types compete.
    private bool AwaitsWithoutCandidate(Koto node, ResultContext context, List<BoundType> types)
    {
        var nested = DeliversResult(node) && this.DecidedLater(node);
        var defaulted = this.flagScratch.Rent(context.Sources.Count);
        try
        {
            var literal = false;
            for (var i = 0; i < context.Sources.Count; i++)
            {
                defaulted[i] = context.Sources[i] is null && context.SourceNodes[i] is { } source && this.Deferred(source);
                if (defaulted[i] && nested)
                {
                    return context.Waiting = true;
                }

                literal |= defaulted[i] && this.SuppliesDefault(context.SourceNodes[i]!);
            }

            var any = false;
            for (var i = 0; i < context.Sources.Count; i++)
            {
                // A source holding only Never parts takes the Type the defaulted literals supply (PropagateSources); without one, its own.
                defaulted[i] = defaulted[i] && (!literal || this.SuppliesDefault(context.SourceNodes[i]!));
                if (defaulted[i])
                {
                    context.Sources[i] = this.BindWaiting(context.SourceNodes[i]!, null);
                    context.Pending |= context.Sources[i] is null;
                    any = true;
                }
            }

            types.Clear(); // A nested result decided above reused the list.
            for (var i = 0; i < context.Sources.Count; i++)
            {
                if (context.Sources[i] is { } source && !ReferenceEquals(source, BoundType.Never) && (defaulted[i] || !any))
                {
                    types.Add(source);
                }
            }

            return false;
        }
        finally
        {
            this.flagScratch.Return(defaulted);
        }
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

        this.lateWrites.Clear();
        return true;
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
            return this.SuppliedType(node);
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
            _ => DeliversResult(node) ? this.LateResultType(node, target) : null, // A bound expression that awaits holds no waiting literal.
        };

        return this.LateWrite(node, late);
    }

    // Records the write of a late Type to the node.
    private BoundType? LateWrite(Koto node, BoundType? late)
    {
        if (late is not null)
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

    // SPEC 4.3: an array literal of the target's shape takes the target's element Type when every element fits it.
    private BoundType? LateArrayType(ArrayLiteralKoto array, BoundType target)
    {
        if (array.BoundType is not { } own || own.Kind != target.Kind || !ReferenceEquals(own.Symbol, target.Symbol) || target.Components.Count != 1)
        {
            return null;
        }

        if (own.Length != target.Length || !ReferenceEquals(own.LengthExpression, target.LengthExpression))
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

    // SPEC 12.4.3: a dictionary literal takes the target's key and value Types when every key and value fits them.
    private BoundType? LateDictionaryType(DictionaryLiteralKoto dictionary, BoundType target)
    {
        if (dictionary.BoundType?.Kind != BoundTypeKind.Dictionary || target.Kind != BoundTypeKind.Dictionary || target.Components.Count != 2)
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
