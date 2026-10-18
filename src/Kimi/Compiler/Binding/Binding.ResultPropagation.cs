// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Result propagation (SPEC 14.9.2 step 3, 3.8, 12.3; DECISIONS R4-M52, BU-4): a literal takes its position's Type.
// - Responsibility: (1) α, the Type a Tuple literal takes from its elements' Types at its position's Type (FittedTupleType: an element
//   of Type Never takes its position's element Type, every other element keeps its own Type) where the position is known when the
//   literal is bound (BindTuple); (2) the late fit, where it is decided only after the expression is bound: an expression that awaits
//   its position (AwaitsPosition: a literal-only expression, SPEC 12.3; a Tuple or array literal with a part of Type Never or a part
//   that awaits; a result target every value source of which awaits or has Type Never; through parentheses, labels, expression bodies,
//   @move and @copy) is no evidence beside one that is (SPEC 14.9.2 step 2, 4.3, 10.8), and takes the Type that a join, its literal's
//   element evidence or its selected call decides (TryPropagate); (3) the literal deferral of a join (SPEC 14.9.2 step 3: a literal
//   result is not fixed before outer constraints): while a join collects its sources without a Target Result Type, a numeric
//   literal-only source, directly, through the forms that deliver it or as a part of a Tuple or array literal source, is not bound
//   (WaitsForJoin, DefersToJoin), and a join all of whose sources await and one of which waited, itself such a source, waits too
//   (AwaitsWithoutCandidate); the join's decision (DecideResult) binds each waiting source once (BindWaiting), at the decided Type
//   (PropagateSources) or, without a candidate, at its default, the defaulted sources then supplying the candidates
//   (AwaitsWithoutCandidate), and a waiting join is decided when its position binds it (DecideWaiting).
// - Inputs: the bound expression, its parts' Types, the value sources a join recorded (ResultContext.SourceNodes) and whether it still
//   collects them (ResultContext.Collecting), the target Type.
// - Outputs: LateTypeOf, the Type the expression takes at the target, computed from its parts: a literal leaf takes its position's
//   Type when it fits it (FitsInputLiteral), an array literal its target's element Type only when every element fits it, any other
//   part keeps its own Type; TryPropagate writes that Type into the expression and every part and wrapper the computation passed
//   when it fits the target at the use, and writes nothing otherwise; a waiting source bound at its join's Type.
// - Invariants: a typed part is never re-typed or re-bound (SPEC 14.9.2: a typed binding is never reinferred); only literal leaves,
//   the literals and wrappers around them and nested result targets are written; a bound expression holds no waiting part.
// - Prohibitions: no inference or record here; a waiting source is bound only by BindNode at its decided Type, which reports its own
//   misfit at the literal; the reader reports a misfit of the late fit.
public sealed partial class Binding
{
    // The writes of the last late Type computation, which TryPropagate applies.
    private readonly List<(Koto Node, BoundType Type)> lateWrites = new();

    // SPEC 14.9.1: a result target whose value is its result, a selection, `do` or `loop`; a function's value is itself, not its result.
    private static bool DeliversResult(Koto node) => node is IfKoto or MatchKoto or DoKoto or LoopKoto;

    // The operand a value-preserving form delivers as it is: parentheses, a label, an expression body, @move and @copy, read from the
    // syntax so that it holds before they are bound.
    private static Koto? DeliveredOperand(Koto node) => node switch
    {
        ParenthesizedKoto parentheses => parentheses.Operand,
        LabeledKoto label => label.Target,
        CodeBlockKoto { IsExpressionBody: true } block => block.Items[0],
        ConversionKoto acquisition when
            ConversionTargetSyntax(acquisition) is TypeSemanticsKoto { Type: null, Identifier: Constants.MoveOperation or Constants.CopyOperation }
            => acquisition.Left,
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

    // SPEC 14.9.2 step 3: whether an expression, bound or waiting, takes its position's Type once that is decided.
    private bool AwaitsPosition(Koto node)
    {
        if (DeliveredOperand(node) is { } operand)
        {
            return this.AwaitsPosition(operand);
        }

        return node switch
        {
            TupleLiteralKoto tuple => this.HoldsAwaitingPart(tuple.Elements),
            ArrayLiteralKoto array => this.HoldsAwaitingPart(array.Elements),
            _ when DeliversResult(node) => this.resultContexts.TryGetValue(node, out var context) && this.SourcesAwait(context),
            _ => NumericLiteralDefault(node) is not null,
        };
    }

    private bool HoldsAwaitingPart(List<Koto> parts)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            if (ReferenceEquals(parts[i].BoundType, BoundType.Never) || this.AwaitsPosition(parts[i]))
            {
                return true;
            }
        }

        return false;
    }

    // A result target awaits when it delivers a value and each of its value sources awaits or has Type Never.
    private bool SourcesAwait(ResultContext context)
    {
        var awaits = false;
        for (var i = 0; i < context.Sources.Count; i++)
        {
            if (context.SourceNodes[i] is not { } source)
            {
                return false;
            }

            if (!ReferenceEquals(context.Sources[i], BoundType.Never))
            {
                if (context.Sources[i] is null ? !this.Deferred(source) : !this.AwaitsPosition(source))
                {
                    return false;
                }

                awaits = true;
            }
        }

        return awaits;
    }

    // SPEC 14.9.2 step 3, 12.3: a numeric literal-only expression bound without an expected Type waits, unbound, for the Type of the join
    // whose value source it is, directly or as a part of a Tuple or array literal source, while that join collects its sources without
    // a Target Result Type: it is no evidence, and it is bound once that Type, or its default, is decided (PropagateSources).
    private bool WaitsForJoin(Koto node, BoundType? expected)
        => expected is null && node.BindingState == BindingState.Unvisited && NumericLiteralDefault(node) is not null && this.DefersToJoin(node);

    // Whether the node is a value source of a join that collects its sources without a Target Result Type (SPEC 14.9.1, 14.9.2): directly,
    // through the forms that deliver it (DeliveredOperand), or as a part of a Tuple or array literal source.
    private bool DefersToJoin(Koto node)
    {
        var source = node;
        while (source.Parent is { } parent &&
            (ReferenceEquals(DeliveredOperand(parent), source) || parent is TupleLiteralKoto or ArrayLiteralKoto { FillLength: null }))
        {
            source = parent;
        }

        return ResultTarget(source) is { } target && this.resultContexts.TryGetValue(target, out var context) && context.Collecting && context.Expected is null;
    }

    // A value source that waited for its join's Type: unbound, without a failure of its own, and awaiting its position.
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
    // join delivers its result as a source of a join that collects, it waits for that join's Type (true); otherwise the waiting sources
    // take their defaults (SPEC 10.2) and the sources' own Types compete.
    private bool AwaitsWithoutCandidate(Koto node, ResultContext context, List<BoundType> types)
    {
        var nested = DeliversResult(node) && this.DefersToJoin(node);
        var defaulted = this.flagScratch.Rent(context.Sources.Count);
        try
        {
            var any = false;
            for (var i = 0; i < context.Sources.Count; i++)
            {
                defaulted[i] = context.Sources[i] is null && context.SourceNodes[i] is { } source && this.Deferred(source);
                if (defaulted[i] && nested)
                {
                    return context.Waiting = true;
                }

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

    // SPEC 14.9.2 step 3: binds a source that waited at `type` (null: its defaults): its delivered operand first, since @move and @copy
    // bind theirs without an expected Type, then the forms around it. Returns the Type the source supplies, null when it fails.
    private BoundType? BindWaiting(Koto source, BoundType? type)
    {
        var operand = source;
        while (DeliveredOperand(operand) is { } inner)
        {
            operand = inner;
        }

        if (!ReferenceEquals(operand, source))
        {
            this.BindNode(operand, this.ConstraintScope(operand), type);
        }

        return this.BindNode(source, this.ConstraintScope(source), type) is null ? null : this.SuppliedType(source);
    }

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

    private BoundType? RequireAtPosition(Koto part, BindingScope scope, BoundType type)
    {
        this.PropagateBound(part, type);
        return this.RequireType(part, scope, type);
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

        var late = DeliveredOperand(node) is { } operand ? this.LateType(operand, target) : node switch
        {
            TupleLiteralKoto tuple => this.LateTupleType(tuple, target),
            ArrayLiteralKoto array => this.LateArrayType(array, target),
            _ when DeliversResult(node) => this.LateResultType(node, target),
            _ => this.FitsInputLiteral(node, target, this.ConstraintScope(node)) ? this.LateLiteralType(node, target) : null,
        };

        if (late is not null)
        {
            this.lateWrites.Add((node, late));
            if (node is ConversionKoto acquisition)
            {
                this.lateWrites.Add((acquisition.Right, late)); // @move's and @copy's target name the operand's Type.
            }
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
            if (this.LateType(array.Elements[i], target.Components[0]) is not { } element || !this.FitsTypeAt(element, target.Components[0], array.Elements[i]))
            {
                return null;
            }
        }

        return target;
    }

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

    // SPEC 12.3: the operands of a literal-only expression take the Type it fits with it, but a shift count, which is typed on its own.
    private BoundType LateLiteralType(Koto node, BoundType target)
    {
        if (node is UnaryKoto or BinaryKoto)
        {
            this.LateLiteralOperand(node is BinaryKoto binary ? binary.Left : ((UnaryKoto)node).Operand, target);
            if (node is BinaryKoto { Akind: not (KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan) } arithmetic)
            {
                this.LateLiteralOperand(arithmetic.Right, target);
            }
        }

        return target;
    }

    private void LateLiteralOperand(Koto operand, BoundType target)
    {
        this.lateWrites.Add((operand, target));
        this.LateLiteralType(operand, target);
    }
}
