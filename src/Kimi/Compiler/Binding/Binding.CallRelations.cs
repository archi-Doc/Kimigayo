// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call relations (SPEC 15.6.1, 15.6.5, 10.1): the Origin relations of a selected call, formed after selection at their own sources
// (the receiver's and each argument's fit to its completed parameter Type, then each substituted clause and a Within row) as rows of the
// body, which A5 judges (FormBodyRelation; R7-O1: Binding judges no call position and records none), each row with the input that fixed
// its equality (FixedBy); and the bounds its fits give omitted Origins (CollectFitBounds). Files: this one; .Records.cs (the records of
// a node beyond the first).
public sealed partial class Binding
{
    // Whether the Origins and Origin arguments of a Type's parts name `origin`, a meet's operand included (a meet's operands are atoms,
    // Meet); raw layers' Origins are not read. A test of its Origin summary's Nodes (Binding.OriginSummaries).
    private bool MentionsOrigin(BoundType type, BoundOrigin origin) => Array.IndexOf(this.SummaryOf(type).Nodes, origin) >= 0;

    // The input, other than the failing one, whose parameter names the callee's Type parameter `slot` and whose adapted Type carries
    // `value`, the Origin of the slot's solution: the input whose invariant binding fixed it. Receiver first, then the arguments.
    private Koto? SlotEqualitySource(in CallFit context, BoundType slot, BoundOrigin value)
    {
        for (var n = 0; n <= context.ArgumentCount; n++)
        {
            var i = n == 0 ? context.ArgumentCount : n - 1;
            var operation = context.Operations[i];
            if (operation is { Source: { } source, AdaptedType: { } adapted } && !ReferenceEquals(source, context.At) &&
                operation.ParameterIndex >= 0 && operation.ParameterIndex < context.Selected.Parameters.Count &&
                context.Selected.Parameters[operation.ParameterIndex].Type.BoundType is { } pattern && MentionsSlot(pattern, slot.Symbol!) && this.MentionsOrigin(adapted, value))
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
    // candidate are formed here, after selection and at their own sources: the receiver's and each argument's fit to its completed
    // parameter Type, then the well-formedness of each slot the call fills (SPEC 15.6.4 step 3: the receiver's, each argument's and each
    // default's, at the call), then each substituted clause. Each relation the fit's answer does not prove, and each body chain, is a row
    // of the body at the value that does not fit (FormBodyRelation); A5 judges it, a Refuted or Unknown one its record and a chain it
    // cannot represent yet the located limit there, and the input is not failed for it. An aggregate argument's well-formedness is
    // formed by its positions' rows (JudgePositionRows). Erasures, anonymous bodies and join arguments (U2j) are judged where they
    // are bound. The expected result is judged at its destination. `operations` holds the arguments' and, last, the receiver's; the
    // selected candidate's solution holds its Origins and each argument's root row (F1, R4-M164), and the receiver is Evidence.
    private void JudgeSelectedCall(
        InvocationKoto call, in SlotSolution solution, ReadOnlySpan<BoundArgumentOperation> operations, ReadOnlySpan<BoundDefaultArgument> defaults)
    {
        var selected = solution.Function;

        // The selected candidate's solve as its runs retained it (InferenceOf), whose published Marked fact the clauses and the input
        // premises read; the solution rebuilt for this judgment carries none.
        var solved = this.originInferences.GetValueOrDefault((call, selected));
        this.RecordOpenings(call, solution);
        var argumentCount = operations.Length - 1;
        var receiver = operations[argumentCount];
        if (receiver.Source is not null)
        {
            this.JudgeCallFit(selected, operations, argumentCount, argumentCount);
        }

        for (var i = 0; i < argumentCount; i++)
        {
            this.JudgeCallFit(selected, operations, argumentCount, i);
        }

        for (var i = 0; i < operations.Length; i++)
        {
            var childStart = 0;
            var childEnd = 0;
            if (i < argumentCount)
            {
                this.ChildGroup(solution, i, out childStart, out childEnd);
            }

            // R4 (DECISIONS BU-104): an argument whose root has child rows is judged by them, whether or not its operation adapted it.
            if (childStart == childEnd && operations[i] is { Source: { } at, AdaptedType: { } adapted, ParameterType: { } parameter })
            {
                // The parameter's pattern as the candidate reads it, the receiver's too (DECISIONS BU-99).
                var pattern = i < argumentCount ? solution.Rows[i].Pattern : solution.Pattern(selected.Parameters[receiver.ParameterIndex].Type.BoundType!);
                this.RequireInputPremises(at, pattern!, parameter, adapted, i < argumentCount ? solution.Rows[i].Contribution : Contribution.Evidence, solved);
            }
            else if (i < argumentCount && operations[i] is { Source: { } whole, ParameterType: { } type })
            {
                // SPEC 10.2: a composite borrowed at `ref/T` (its cross-semantics borrow) is walked at its value Type T, the operation's
                // SourceType (FitComposite), beside the pattern's referent; a slot's solution stays its contributor's (BU-99).
                var root = solution.Rows[i] with { Type = type };
                if (operations[i] is { Adaptation: ArgumentAdaptation.CrossSemanticsBorrow, SourceType: { } value })
                {
                    root = root with { Pattern = IsBorrow(root.Pattern!.Semantics) ? root.Pattern.Components[0] : root.Pattern, Type = value };
                }

                this.JudgePositionRows(whole, root, solution, solved);
            }
        }

        foreach (var argument in defaults)
        {
            this.RequireInputPremises(call, solution.Pattern(argument.Parameter.Type!)!, argument.ParameterType, null, Contribution.Signature, solved);
        }

        this.JudgeCallClauses(call, selected, operations, solution.Origins, solution.Inputs, solution.DeclaringType, solved);
        this.CollectFitBounds(operations, argumentCount);
    }

    // J2 (PLAN G936): the selected candidate's openings become the region graph's records (RecordOpening), before its fits and
    // clauses are judged; only a candidate with a position without evidence opened anything (HasWaitingPosition, the gate
    // SlotSolution.OpenWaited reads).
    private void RecordOpenings(Koto call, in SlotSolution solution)
    {
        if (this.HasWaitingPosition(solution) && this.originInferences.TryGetValue((call, solution.Function), out var inference))
        {
            foreach (var (region, bound) in inference.Opened)
            {
                this.RecordOpening(region, bound, call);
            }
        }
    }

    // SPEC 15.4.4, 10.7: the bounds that a selected call's fits give the Origins omitted in open initializers at the Function Type
    // positions that CollectCallRelations leaves to applicability. Applicability compares such a position as a whole contract and
    // finds the Origin satisfiable (FitsOriginOutlives); the same fit of each selected input, collected at its source as the call's
    // judgment is, records the value as the Origin's bound.
    private void CollectFitBounds(ReadOnlySpan<BoundArgumentOperation> operations, int argumentCount)
    {
        this.collectingFits = true;
        try
        {
            for (var i = 0; i <= argumentCount; i++)
            {
                if (operations[i] is { Source: { } at, AdaptedType: { } adapted, ParameterType: { } parameter } && adapted.CarriesOrigin)
                {
                    // SPEC 10.1, 10.3, 15.6.1: applicability fitted the structural part alone (FitsStructuralPartAt); a Function Type
                    // position's whole contract and its implementation's own well-formedness are judged here, after selection, at the
                    // argument (DECISIONS R4-M155, landing review B1).
                    var walk = new RelationWalk(this, this, at, OriginPart.Structural);
                    if (!walk.Fits(adapted, parameter))
                    {
                        _ = this.FailMismatch(at, at, adapted, parameter, walk.Failure);
                    }
                }
            }
        }
        finally
        {
            this.collectingFits = false;
        }
    }

    // `operations` holds the arguments' and, last, the receiver's; `solved` is the call's solve, whose published Marked fact a clause over
    // a marked variable rests on (rule W, SPEC 23.1.2).
    private void JudgeCallClauses(InvocationKoto call, FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, BoundType? declaringType, OriginInference? solved)
    {
        var argumentCount = operations.Length - 1;
        var binder = selected.OriginBinder;
        if (this.originDeclarations.TryGetValue(binder, out var declaration))
        {
            foreach (var relation in declaration.Relations)
            {
                if (solved is not null && (solved.ReadsMarked(relation.Longer) || solved.ReadsMarked(relation.Shorter)))
                {
                    continue;
                }

                // SPEC 15.6.1: a substituted clause is located at the first input whose parameter names its longer Origin.
                var at = this.ClauseInput(selected, operations, argumentCount, relation.Longer) ?? call;
                var longer = this.SubstituteClauseOrigin(relation.Longer, selected, origins, inputs, declaringType);
                var shorter = this.SubstituteClauseOrigin(relation.Shorter, selected, origins, inputs, declaringType);
                this.JudgeCallPosition(at, longer, shorter, relation.Equality, null, relation.Syntax, declared: true);
            }
        }

        // SPEC 15.3.3, 15.6.4 step 3 (J2, PLAN G936): a constructor proves nothing of its owner Type's clauses, which are premises
        // inside the Type and obligations of each use, and it carries no result premises (CarriesResultPremises). Its result is the
        // construction Type, whose declared relations go to the result's clause judge (RequireResultClause): a row of the call over a
        // region of the construction, the formation proof's otherwise.
        if (selected.IsConstructor && declaringType is { Symbol: { } owner } &&
            this.originDeclarations.TryGetValue(owner.Declaration, out var ownerDeclaration) && ownerDeclaration.Completed)
        {
            foreach (var relation in ownerDeclaration.Relations)
            {
                var longer = this.SubstituteStoredOrigin(relation.Longer, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments);
                var shorter = this.SubstituteStoredOrigin(relation.Shorter, owner.Declaration, (BoundOrigin[])declaringType.OriginArguments);
                this.RequireResultClause(call, longer, shorter, relation.Equality, relation.Syntax);
            }
        }

        if (this.StatedWithin(selected, operations, out var storage) is { } within && selected.BoundSymbol!.Type?.Origin is { } result)
        {
            // SPEC 5.6, 5.7 (D14): the result of an operation whose catalog row names Within lies within the Origin its argument states.
            var bound = this.SubstituteClauseOrigin(result, selected, origins, inputs, declaringType);
            this.JudgeCallPosition(storage, within, bound, equality: false, variable: null, clause: null);
        }
    }

    // SPEC 15.6.4 step 3: an Origin of a callee's declared relation (an accessor's, its binder's) as a call substitutes it: the callee's
    // own Origins and inputs, as many as the call gives, and the declaring Type's stored slots, in one pass (Binding.CallSubstitution).
    private BoundOrigin SubstituteClauseOrigin(
        BoundOrigin origin,
        FunctionKoto selected,
        ReadOnlySpan<BoundOrigin> origins,
        ReadOnlySpan<BoundOrigin> inputs,
        BoundType? declaringType)
        => this.Substitute(origin, SubstitutionOf(selected.OriginBinder, declaringType, default, default, origins, inputs));

    // The receiver (index == argumentCount) or argument whose adapted Type is fitted to its completed parameter Type; a selected input that
    // borrows a static root is first the located limit (FailStaticBorrow), its relations (SPEC 11.8, a fit to static) waiting for P42,
    // and an implicit exclusive Borrow (a receiver's, SPEC 7.3) of a `during self` layer's Place or an enclosing one is the allowlist's
    // Origin relation problem (JudgeHolderPlace; SPEC 5.7, DECISIONS R4-M110), which leaves the input Resolved and its fit judged.
    private void JudgeCallFit(FunctionKoto selected, ReadOnlySpan<BoundArgumentOperation> operations, int argumentCount, int index)
    {
        var operation = operations[index];
        if (operation is { Source: { } source, ParameterType: { } mode })
        {
            if (this.FailStaticBorrow(source, operation.Kind, mode.Semantics))
            {
                return;
            }

            if (operation.Kind == ArgumentOperationKind.Borrow && IsExclusive(mode.Semantics))
            {
                this.JudgeHolderPlace(source, HolderAccess.ExclusiveBorrow, null, source);
            }
        }

        if (operation is not { Source: { } at, AdaptedType: { } adapted, ParameterType: { } parameter } || ReferenceEquals(adapted, parameter) ||
            ((!adapted.StoresOrigin || !parameter.StoresOrigin) && !adapted.CarriesRawOrigin && !parameter.CarriesRawOrigin))
        {
            return;
        }

        var pattern = operation.ParameterIndex >= 0 && operation.ParameterIndex < selected.Parameters.Count ? selected.Parameters[operation.ParameterIndex].Type.BoundType : null;
        var context = new CallFit(selected, operations, argumentCount, at, parameter);
        this.CollectCallRelations(adapted, parameter, pattern, false, context, null);
    }

    // SPEC 15.6.1: the Origin positions of a fit, in the order of the outer borrow layer, the Semantics target, then slots and Type
    // arguments, each with the variance of the relation table; `pattern` follows the uninstantiated parameter while it has the same
    // structure, so that a fresh Origin's equality can be related to the input that fixed it; `slot` is the callee's Type parameter
    // whose solution holds the positions below it, so that an equality with that solution is related to the input that fixed it.
    private void CollectCallRelations(BoundType actual, BoundType expected, BoundType? pattern, bool invariant, in CallFit context, BoundType? slot)
    {
        if (actual.Kind == BoundTypeKind.Function || actual.Components.Count != expected.Components.Count || actual.OriginArguments.Count != expected.OriginArguments.Count)
        {
            return; // SPEC 10.7: a Function Type is a whole contract, compared during applicability.
        }

        if (ReferenceEquals(actual, expected))
        {
            // An interned Type is a DAG (DECISIONS BU-19): one part relates each of its Origins to itself, which forms no row
            // (FormBodyRelation), so a shared part is never walked again along each of its paths (PLAN G1115).
            return;
        }

        this.termWalks++;
        if (pattern is { Kind: BoundTypeKind.Parameter, Symbol: { } parameter } && ContainerSlot(context.Selected, parameter) >= 0)
        {
            slot = pattern;
        }

        if (pattern is not null && (pattern.Kind != actual.Kind || pattern.Components.Count != actual.Components.Count || pattern.OriginArguments.Count != actual.OriginArguments.Count))
        {
            pattern = null;
        }

        // A call's fit is judged at the call, in no comparison (default).
        if (this.FitOuterOrigins(actual, expected, this, context.At, context.At, out var longer, out var shorter, default) == OuterOriginFit.Relation)
        {
            // SPEC 8.1.2, 15.6.5: a pair layer's slot, `o` included, exists only in its binder's borrow cases.
            this.JudgeCallPosition(context.At, longer, shorter, invariant, pattern is null ? null : this.OuterOrigin(pattern), null, context: context, slot: slot, condition: SlotCondition(actual, this, context.At, default));
        }

        var exclusive = IsInvariantLayer(actual, this, context.At, default);
        for (var i = 0; i < actual.Components.Count && actual.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication; i++)
        {
            this.CollectCallRelations(actual.Components[i], expected.Components[i], pattern?.Components[i], invariant || exclusive, context, slot);
        }

        for (var i = 0; i < actual.OriginArguments.Count; i++)
        {
            var variance = this.OriginArgumentVariance(actual, i, this);
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
                    this.CollectCallRelations(actual.Components[i], expected.Components[i], part, true, context, slot);
                }
                else if (variance == OriginVariance.Covariant)
                {
                    this.CollectCallRelations(actual.Components[i], expected.Components[i], part, false, context, slot);
                }
                else
                {
                    this.CollectCallRelations(expected.Components[i], actual.Components[i], null, false, context, slot);
                }

                continue;
            }

            this.CollectCallRelations(actual.Components[i], expected.Components[i], part, invariant, context, slot);
        }
    }

    // SPEC 15.6.1, 15.6.4 step 3, 15.6.5 (R7-O1 b1a): a selected call's relation `longer outlives shorter` (`==` when `equality`) at `at`,
    // formed as a row of the body (FormBodyRelation), which A5 judges; Binding judges no call position. Its row carries the fit's
    // destination (the completed parameter Type), a declared relation's clause, and, for an `==` whose fresh Origin or Type slot another
    // input fixed, that input (SPEC 15.6.1, Location; SPEC 10.8: the first invariant binding of a slot is its solution).
    private void JudgeCallPosition(Koto at, BoundOrigin longer, BoundOrigin shorter, bool equality, BoundOrigin? variable, Koto? clause, bool declared = false, in CallFit context = default, BoundType? slot = null, bool wellFormed = false, ulong condition = 0)
    {
        var fixedBy = clause is not null || !equality || context.Operations.Length == 0 ? null
            : variable is not null ? this.EqualitySource(context, variable, shorter)
            : slot is not null ? this.SlotEqualitySource(context, slot, shorter) : null;
        this.FormBodyRelation(new(BindingObligationKind.OriginOutlives, at, BindingDeadline.BodyOrigins, declared ? null : context.Parameter, longer, shorter, Equality: equality, Clause: declared ? clause : null, WellFormed: wellFormed, Condition: condition, FixedBy: fixedBy));
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
                context.Selected.Parameters[operation.ParameterIndex].Type.BoundType is { } pattern && this.MentionsOrigin(pattern, variable) && this.MentionsOrigin(adapted, value))
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
                selected.Parameters[operation.ParameterIndex].Type.BoundType is { } pattern && this.MentionsOrigin(pattern, origin))
            {
                return source;
            }
        }

        return null;
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
