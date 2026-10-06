// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The records of a node beyond the first, published after it with their own conditions (SPEC 15.6.1: every failed chain).
    private readonly List<(Koto Node, OriginRelationFact Fact)> callRelationScratch = new();
    private readonly List<OriginRelationFact> relationScratch = new();
    private Dictionary<Koto, List<OriginRelationFact>>? callRelations;

    private static bool MentionsOrigin(BoundType type, BoundOrigin origin)
    {
        if (!type.CarriesOrigin)
        {
            return false;
        }

        if (type.Origin is { } outer && MentionsOrigin(outer, origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (MentionsOrigin(type.OriginArguments[i], origin))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (MentionsOrigin(type.Components[i], origin))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MentionsOrigin(BoundOrigin expression, BoundOrigin origin)
    {
        if (ReferenceEquals(expression, origin))
        {
            return true;
        }

        for (var i = 0; i < expression.Operands.Count; i++)
        {
            if (ReferenceEquals(expression.Operands[i], origin))
            {
                return true;
            }
        }

        return false;
    }

    // The input, other than the failing one, whose parameter names the callee's Type parameter `slot` and whose adapted Type carries
    // `value`, the Origin of the slot's solution: the input whose invariant binding fixed it. Receiver first, then the arguments.
    private static Koto? SlotEqualitySource(in CallFit context, BoundType slot, BoundOrigin value)
    {
        for (var n = 0; n <= context.ArgumentCount; n++)
        {
            var i = n == 0 ? context.ArgumentCount : n - 1;
            var operation = context.Operations[i];
            if (operation is { Source: { } source, AdaptedType: { } adapted } && !ReferenceEquals(source, context.At) &&
                operation.ParameterIndex >= 0 && operation.ParameterIndex < context.Selected.Parameters.Count &&
                context.Selected.Parameters[operation.ParameterIndex].Type.BoundType is { } pattern && MentionsSlot(pattern, slot.Symbol!) && MentionsOrigin(adapted, value))
            {
                return source;
            }
        }

        return null;

        static bool MentionsSlot(BoundType type, BindingSymbol symbol)
        {
            if (type.Kind == BoundTypeKind.Parameter && ReferenceEquals(type.Symbol, symbol))
            {
                return true;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (MentionsSlot(type.Components[i], symbol))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // SPEC 15.6.1, 15.6.5, 10.1: applicability uses only the structural part of each fit, so the Origin relations of the selected
    // candidate are judged here, after selection and at their own sources: the receiver's and each argument's fit to its completed
    // parameter Type, then each substituted clause. Refuted and Unknown relations are UnsatisfiedOriginRelation_Kd and
    // UnprovenOriginRelation_Kd records at the value that does not fit; a chain between body Origins, which ownership analysis cannot
    // represent yet, is an obligation that it reports as the located limit at that value. Erasures, anonymous bodies, aggregates and
    // contextual payloads are judged where they are bound. The expected result is judged at its destination.
    private void JudgeSelectedCall(InvocationKoto call, FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, int argumentCount, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType)
    {
        this.callRelationScratch.Clear();
        var receiver = operations[argumentCount];
        if (receiver.Source is not null)
        {
            this.JudgeCallFit(selected, operations, argumentCount, argumentCount);
        }

        for (var i = 0; i < argumentCount; i++)
        {
            this.JudgeCallFit(selected, operations, argumentCount, i);
        }

        if (this.originDeclarations.TryGetValue(selected, out var declaration))
        {
            foreach (var relation in declaration.Relations)
            {
                // SPEC 15.6.1: a substituted clause is located at the first input whose parameter names its longer Origin.
                var at = this.ClauseInput(selected, operations, argumentCount, relation.Longer) ?? call;
                var longer = Substitute(relation.Longer);
                var shorter = Substitute(relation.Shorter);
                this.JudgeCallPosition(at, longer, shorter, relation.Equality, null, relation.Syntax, declared: true);
            }
        }

        this.PublishCallRelations();

        BoundOrigin Substitute(BoundOrigin origin)
        {
            if (declaringType?.Symbol is { } owner)
            {
                origin = this.SubstituteStoredOrigin(origin, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments);
            }

            return this.SubstituteStoredOrigin(origin, selected, origins.AsSpan(0, selected.BoundSymbol?.Schema?.Origins.Count ?? 0), inputs.AsSpan(0, Math.Min(inputs.Length, InputOriginCount(selected))));
        }
    }

    // The records of one call node beyond the first, each with its own condition so that it stays an independent problem.
    private void ReportMoreCallRelations(Koto node, DiagnosticRequirement requirement)
    {
        if (this.callRelations?.TryGetValue(node, out var more) != true)
        {
            return;
        }

        for (var i = 0; i < more!.Count; i++)
        {
            ReportOriginRelation(node, more[i], requirement, OriginRelationCode(more[i]), this.BorrowOriginHint(node), (ushort)(i + 1));
        }
    }

    // The receiver (index == argumentCount) or argument whose adapted Type is fitted to its completed parameter Type.
    private void JudgeCallFit(FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, int argumentCount, int index)
    {
        var operation = operations[index];
        if (operation is not { Source: { } at, AdaptedType: { } adapted, ParameterType: { } parameter } || ReferenceEquals(adapted, parameter) ||
            !adapted.CarriesOrigin || !parameter.CarriesOrigin)
        {
            return;
        }

        var pattern = operation.ParameterIndex >= 0 && operation.ParameterIndex < selected.Parameters.Count ? selected.Parameters[operation.ParameterIndex].Type.BoundType : null;
        var context = new CallFit(selected, operations, argumentCount, at, parameter);
        this.CollectCallRelations(adapted, parameter, pattern, false, context, 0, null);
    }

    // SPEC 15.6.1: the Origin positions of a fit, in the order of the outer borrow layer, the Semantics target, then slots and Type
    // arguments, each with the variance of the relation table; `pattern` follows the uninstantiated parameter while it has the same
    // structure, so that a fresh Origin's equality can be related to the input that fixed it; `slot` is the callee's Type parameter
    // whose solution holds the positions below it, so that an equality with that solution is related to the input that fixed it.
    private void CollectCallRelations(BoundType actual, BoundType expected, BoundType? pattern, bool invariant, in CallFit context, int depth, BoundType? slot)
    {
        if (depth > 64 || actual.Kind == BoundTypeKind.Function || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count)
        {
            return; // SPEC 10.7: a Function Type is a whole contract, compared during applicability.
        }

        if (pattern is { Kind: BoundTypeKind.Parameter, Symbol: { } parameter } && ContainerSlot(context.Selected, parameter) >= 0)
        {
            slot = pattern;
        }

        if (pattern is not null && (pattern.Kind != actual.Kind || pattern.Components.Count != actual.Components.Count || pattern.OriginArguments.Count != actual.OriginArguments.Count))
        {
            pattern = null;
        }

        if (actual.Origin is { } longer && expected.Origin is { } shorter)
        {
            this.JudgeCallPosition(context.At, longer, shorter, invariant, pattern?.Origin, null, context: context, slot: slot);
        }

        var exclusive = IsInvariantLayer(actual, this);
        for (var i = 0; i < actual.Components.Count && actual.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication; i++)
        {
            this.CollectCallRelations(actual.Components[i], expected.Components[i], pattern?.Components[i], invariant || exclusive, context, depth + 1, slot);
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            var variance = actual.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant;
            var both = invariant || variance is OriginVariance.Invariant or OriginVariance.Unused;
            if (both || variance == OriginVariance.Covariant)
            {
                this.JudgeCallPosition(context.At, actual.OriginArguments[i], expected.OriginArguments[i], both, pattern?.OriginArguments[i], null, context: context, slot: slot);
            }
            else
            {
                this.JudgeCallPosition(context.At, expected.OriginArguments[i], actual.OriginArguments[i], false, pattern?.OriginArguments[i], null, context: context, slot: slot);
            }
        }

        for (var i = 0; i < actual.Components.Count && actual.Kind != BoundTypeKind.Semantics; i++)
        {
            var part = pattern?.Components[i];
            if (actual.Kind == BoundTypeKind.Constructed && actual.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count)
            {
                var variance = schema.GenericSlots[i].OriginVariance;
                if (invariant || variance is OriginVariance.Invariant or OriginVariance.Unused)
                {
                    this.CollectCallRelations(actual.Components[i], expected.Components[i], part, true, context, depth + 1, slot);
                }
                else if (variance == OriginVariance.Covariant)
                {
                    this.CollectCallRelations(actual.Components[i], expected.Components[i], part, false, context, depth + 1, slot);
                }
                else
                {
                    this.CollectCallRelations(expected.Components[i], actual.Components[i], null, false, context, depth + 1, slot);
                }

                continue;
            }

            this.CollectCallRelations(actual.Components[i], expected.Components[i], part, invariant, context, depth + 1, slot);
        }
    }

    // SPEC 15.6.5: judges `longer outlives shorter` (and the reverse for `==`) at `at`. A meet at the longer end decomposes, so each
    // failing operand is its own record (SPEC 15.3.6).
    private void JudgeCallPosition(Koto at, BoundOrigin longer, BoundOrigin shorter, bool equality, BoundOrigin? variable, Koto? clause, bool declared = false, in CallFit context = default, BoundType? slot = null)
    {
        if (ReferenceEquals(longer, shorter))
        {
            return;
        }

        if (!equality && longer.Kind == OriginKind.Intersection && !this.ProvesOriginOutlives(longer, shorter, at))
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                this.JudgeCallPosition(at, longer.Operands[i], shorter, false, variable, clause, declared, context, slot);
            }

            return;
        }

        if (IsLocalRegion(longer) || IsLocalRegion(shorter))
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, at, BindingDeadline.BodyOrigins, context.Parameter, longer, shorter, Equality: equality));
            return; // Local bounds are judged after every assignment and selected call has contributed its constraints.
        }

        var forward = this.ProvesOriginOutlives(longer, shorter, at) ? OriginJudgment.Proven : this.JudgeOriginRelation(longer, shorter, at);
        var backward = !equality || this.ProvesOriginOutlives(shorter, longer, at) ? OriginJudgment.Proven : this.JudgeOriginRelation(shorter, longer, at);
        if (forward is OriginJudgment.Refuted or OriginJudgment.Unknown || backward is OriginJudgment.Refuted or OriginJudgment.Unknown)
        {
            // An equality fails as one `==` record; a fresh Origin or a Type slot fixed by another input relates that input (SPEC 15.6.1,
            // Location; SPEC 10.8: the first invariant binding of a slot is its solution).
            var fixedBy = clause is not null || !equality || context.Operations.Length == 0 ? null
                : variable is not null ? this.EqualitySource(context, variable, shorter)
                : slot is not null ? SlotEqualitySource(context, slot, shorter) : null;
            var refuted = forward == OriginJudgment.Refuted || backward == OriginJudgment.Refuted;
            this.callRelationScratch.Add((at, new(at, longer, shorter, equality, declared ? null : context.Parameter, refuted, declared ? clause : null, Substituted: declared, FixedBy: fixedBy)));
            return;
        }

        // A chain between body Origins needs region inference over Loan edges: ownership reports it as the located limit at `at`.
        if (forward == OriginJudgment.Unrepresentable)
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, at, BindingDeadline.BodyOrigins, declared ? null : context.Parameter, longer, shorter));
        }

        if (backward == OriginJudgment.Unrepresentable)
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, at, BindingDeadline.BodyOrigins, declared ? null : context.Parameter, shorter, longer));
        }
    }

    // The input, other than the failing one, whose parameter names the callee's Origin `variable` and whose adapted Type carries the
    // value that solved it: the input whose equality fixed that fresh Origin. Receiver first, then the arguments in source order.
    private Koto? EqualitySource(in CallFit context, BoundOrigin variable, BoundOrigin value)
    {
        if (variable.Kind is not (OriginKind.Input or OriginKind.Parameter) || !ReferenceEquals(variable.Binder, context.Selected))
        {
            return null;
        }

        for (var n = 0; n <= context.ArgumentCount; n++)
        {
            var i = n == 0 ? context.ArgumentCount : n - 1;
            var operation = context.Operations[i];
            if (operation is { Source: { } source, AdaptedType: { } adapted } && !ReferenceEquals(source, context.At) &&
                operation.ParameterIndex >= 0 && operation.ParameterIndex < context.Selected.Parameters.Count &&
                context.Selected.Parameters[operation.ParameterIndex].Type.BoundType is { } pattern && MentionsOrigin(pattern, variable) && MentionsOrigin(adapted, value))
            {
                return source;
            }
        }

        return null;
    }

    // The first input, receiver first, whose parameter Type names `origin`.
    private Koto? ClauseInput(FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, int argumentCount, BoundOrigin origin)
    {
        for (var n = 0; n <= argumentCount; n++)
        {
            var operation = operations[n == 0 ? argumentCount : n - 1];
            if (operation is { Source: { } source, ParameterIndex: >= 0 } && operation.ParameterIndex < selected.Parameters.Count &&
                selected.Parameters[operation.ParameterIndex].Type.BoundType is { } pattern && MentionsOrigin(pattern, origin))
            {
                return source;
            }
        }

        return null;
    }

    // Fails each node with its first record and keeps the others for publication with it.
    private void PublishCallRelations()
    {
        for (var i = 0; i < this.callRelationScratch.Count; i++)
        {
            var (node, fact) = this.callRelationScratch[i];
            var first = true;
            for (var j = 0; j < i; j++)
            {
                first &= !ReferenceEquals(this.callRelationScratch[j].Node, node);
            }

            if (first)
            {
                this.FailExplained(ref this.originRelations, node, BindingFailure.OriginRelation, fact);
            }
            else if (this.originRelations?.TryGetValue(node, out var primary) == true && !primary.Equals(fact))
            {
                this.RecordMoreRelation(node, fact);
            }
        }

        this.callRelationScratch.Clear();
    }

    // A record of `node` after its first, published with it (ReportMoreCallRelations).
    private void RecordMoreRelation(Koto node, OriginRelationFact fact)
    {
        var callRelations = this.callRelations ??= new(ReferenceEqualityComparer.Instance);
        if (!callRelations.TryGetValue(node, out var more))
        {
            callRelations.Add(node, more = new());
        }

        if (!more.Contains(fact))
        {
            more.Add(fact);
        }
    }

    // One fit of a selected call: the callee, its operations, the value and the completed parameter Type.
    private readonly ref struct CallFit(FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, int argumentCount, Koto at, BoundType parameter)
    {
        internal FunctionKoto Selected { get; } = selected;

        internal ReadOnlySpan<BoundArgumentOperation> Operations { get; } = operations;

        internal int ArgumentCount { get; } = argumentCount;

        internal Koto At { get; } = at;

        internal BoundType Parameter { get; } = parameter;
    }
}
