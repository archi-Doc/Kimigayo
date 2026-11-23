// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The outcome of an omitted base clause's selection (SPEC 6.3.6).</summary>
internal enum OmittedBaseOutcome : byte
{
    /// <summary>A base constructor is selected.</summary>
    Selected,

    /// <summary>The direct base declares no constructor.</summary>
    NoBaseConstructor,

    /// <summary>No base constructor applies with no arguments.</summary>
    NoneApplicable,

    /// <summary>More than one base constructor remains best.</summary>
    Ambiguous,

    /// <summary>An Unknown premise of a candidate can affect the selection (SPEC 8.3.3).</summary>
    Unproven,

    /// <summary>The selection rests on another failure, such as an invalid declaration.</summary>
    Dependent,
}

/// <summary>An omitted base clause's selection: the selected constructor, or the premise clause or failure an unselected outcome
/// rests on.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Winner">The selected base constructor.</param>
/// <param name="Cause">The Unknown premise clause of <see cref="OmittedBaseOutcome.Unproven"/>, or the failure of
/// <see cref="OmittedBaseOutcome.Dependent"/>.</param>
internal readonly record struct OmittedBaseSelection(OmittedBaseOutcome Outcome, FunctionKoto? Winner = null, Koto? Cause = null);

public sealed partial class Binding
{
    private readonly ImplicitConstructorDecisionRows implicitConstructorDecisions;
    private readonly List<FunctionKoto> judgedConstructors = new();
    private readonly List<FunctionKoto> withdrawnConstructors = new();

    // SPEC 6.3.6: the omitted base clause's ordinary selection over the direct base's constructors with no arguments, made from the
    // derived constructor's scope with the evaluation and classification of a call (Binding.CallSelection), without publishing. The
    // obligations and per-call state that evaluation touches are restored; its fits bound no omitted Origin (EvaluateCallCandidates).
    // The constructor's base initializer is the retained zero-argument call of its omitted base clause.
    internal OmittedBaseSelection SelectOmittedBaseConstructor(FunctionKoto constructor)
    {
        var call = constructor.BaseInitializer!;
        if (this.BaseConstructorGroup(constructor, out _) is not { } group)
        {
            return new(OmittedBaseOutcome.NoBaseConstructor);
        }

        var candidates = FunctionGroup.Of(group);
        var evaluated = this.RentCandidates(candidates, call.Method, this.scopes[constructor]);
        var shape = MeasureCandidates(candidates, true);
        var query = this.BeginSelectionQuery();
        var evaluation = this.RentEvaluation(call, call.Method, null, this.scopes[constructor], null, shape); // The call has no argument.
        evaluation.Evaluated = evaluated;
        try
        {
            var (selection, winner) = this.SelectAt(ref evaluation, candidates);

            // A winner whose own slots stay unsolved is selected as by a written base call, whose binding then reports them.
            return selection switch
            {
                // SPEC 6.3.6: an omitted base selection's Unknown premise means no implicit constructor, not 10.1's obligation.
                CallSelection.Selected when evaluated[winner].PremiseUnknown =>
                    new(OmittedBaseOutcome.Unproven, Cause: evaluated[winner].ConstraintFailure?.Clause),
                CallSelection.Selected => new(OmittedBaseOutcome.Selected, (FunctionKoto)evaluated[winner].Symbol!.Declaration),
                CallSelection.NoneApplicable => new(OmittedBaseOutcome.NoneApplicable),
                CallSelection.Ambiguous => new(OmittedBaseOutcome.Ambiguous),
                CallSelection.Unproven or CallSelection.UnformedSplit => new(OmittedBaseOutcome.Unproven, Cause: evaluation.PendingCount != 1 ? null : evaluation.CallableFailure?.Clause ?? evaluation.ConstraintFailure?.Clause),
                CallSelection.InvalidDeclaration => new(OmittedBaseOutcome.Dependent, Cause: evaluation.InvalidDeclaration ?? this.TakeFailedCause().Use),
                CallSelection.FailedSignature => new(OmittedBaseOutcome.Dependent, Cause: evaluation.FailedPendingSignature),
                _ => new(OmittedBaseOutcome.Dependent, Cause: evaluation.IncompleteSignature),
            };
        }
        finally
        {
            this.EndSelectionQuery(query);
            this.ReturnEvaluation(evaluation);
        }
    }

    // SPEC 6.3.6: decides each pending synthesized constructor of a derived structure once the declarations are bound, base first.
    // A selected base constructor makes it a member of the structure's `init` group; any other outcome withdraws it from the pass.
    private void CompleteImplicitConstructors()
    {
        this.judgedConstructors.Clear();
        this.withdrawnConstructors.Clear();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is StructKoto { ImplicitConstructorPending: true } structure)
            {
                this.CompleteImplicitConstructor(structure);
            }
        }

        if (this.withdrawnConstructors.Count != 0)
        {
            this.PurgeWithdrawnConstructors();
        }
    }

    private OmittedBaseSelection CompleteImplicitConstructor(StructKoto structure)
    {
        if (this.implicitConstructorDecisions.TryGetValue(structure, out var decided))
        {
            return decided;
        }

        // An invalid base graph, including a cycle, or base clause Constraints that are not Proven leave the decision resting on the
        // base clause, whose own check reports them.
        var clause = structure.BaseClause!;
        var constructor = structure.SynthesizedConstructor!;
        var selection = new OmittedBaseSelection(OmittedBaseOutcome.Dependent, Cause: clause);
        this.implicitConstructorDecisions.Add(structure, selection);
        if (this.inheritanceStates.TryGetValue(structure.BoundSymbol!, out var state) && state == 2 && clause.BoundType is { } baseType &&
            this.CheckTypeConstraints(baseType, this.scopes[structure], structure) == ConstraintProof.Proven)
        {
            // A base synthesized constructor that does not exist for a reason other than absence leaves this one resting on that reason.
            if (baseType.Symbol?.Declaration is StructKoto parent && (parent.ImplicitConstructorPending || this.implicitConstructorDecisions.ContainsKey(parent)) &&
                this.CompleteImplicitConstructor(parent) is { Outcome: OmittedBaseOutcome.Unproven or OmittedBaseOutcome.Dependent } inherited)
            {
                selection = inherited;
            }
            else
            {
                selection = this.SelectOmittedBaseConstructor(constructor);
                this.judgedConstructors.Add(constructor);
            }
        }

        this.implicitConstructorDecisions[structure] = selection;
        var exists = selection.Outcome == OmittedBaseOutcome.Selected;
        structure.CompleteImplicitConstructor(exists);
        if (exists)
        {
            var scope = this.scopes[structure];
            var symbol = constructor.BoundSymbol!;
            symbol.Next = scope.Values.GetValueOrDefault("init");
            scope.Values["init"] = symbol;
        }
        else
        {
            this.withdrawnConstructors.Add(constructor);
        }

        return selection;
    }

    // A withdrawn constructor and its syntax, which follow it contiguously in visit order, leave the pass's nodes, and the obligations
    // raised under them are removed.
    private void PurgeWithdrawnConstructors()
    {
        var kept = 0;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is FunctionKoto { IsImplicitConstructor: true } constructor && this.withdrawnConstructors.Contains(constructor))
            {
                while (i + 1 < this.nodes.Count && IsWithin(this.nodes[i + 1], constructor))
                {
                    i++;
                }

                continue;
            }

            this.nodes[kept++] = node;
        }

        this.nodes.RemoveRange(kept, this.nodes.Count - kept);
        kept = 0;
        for (var i = 0; i < this.obligations.Count; i++)
        {
            var obligation = this.obligations[i];
            if (this.WithinWithdrawnConstructor(obligation.Use))
            {
                this.obligationSet.Remove(obligation);
                continue;
            }

            this.obligations[kept++] = obligation;
        }

        this.obligations.RemoveRange(kept, this.obligations.Count - kept);
    }

    private bool WithinWithdrawnConstructor(Koto use)
    {
        foreach (var constructor in this.withdrawnConstructors)
        {
            if (IsWithin(use, constructor))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 6.3.6: the declaration-time decisions must hold after bodies. The selection is judged again, and an existing synthesized
    // constructor's bound base call selected the same constructor; a disagreement fails closed at the base clause. A signature kept Proven
    // stays Proven here (DeclQueryKind.SignatureVerdict): a declaration that failed after the bodies is seen through its own record, not
    // through a disagreement of this selection (PLAN G375).
    private void RevalidateImplicitConstructors()
    {
        foreach (var constructor in this.judgedConstructors)
        {
            var structure = (StructKoto)constructor.Parent!;
            var decision = this.implicitConstructorDecisions[structure];
            if (this.SelectOmittedBaseConstructor(constructor) != decision ||
                (decision.Winner is { } winner && constructor.BaseInitializer is { } initializer && this.CallOf(initializer) is { } bound && !ReferenceEquals(bound.Target.Declaration, winner)))
            {
                this.Fail(structure.BaseClause!, BindingFailure.Unsupported);
            }
        }
    }

    /// <summary>The `implicitConstructorDecisions` family's one accessor (DECISIONS BU-220): a pass family, a derived structure's omitted-base
    /// decision (SPEC 6.3.6), kept in its module's NodeColumns at its SyntaxId as a nullable (Selected is outcome 0, so the flag keeps every
    /// decision distinct from absent). CompleteImplicitConstructors, the last step of each run, cleared it; no read observes a difference
    /// from the pass counter (G949\n53\o1), which empties it at its first write. Its members keep the names its readers use.</summary>
    private readonly struct ImplicitConstructorDecisionRows(Binding binding)
    {
        internal OmittedBaseSelection this[StructKoto structure]
        {
            get => this.TryGetValue(structure, out var decision) ? decision : throw new KeyNotFoundException($"`{structure}` has no decision in this pass.");
            set => HirTables.Of(structure).Families.ImplicitConstructorDecisions.Ref(structure.SyntaxId, binding) = value;
        }

        internal void Add(StructKoto structure, OmittedBaseSelection decision)
        {
            ref var row = ref HirTables.Of(structure).Families.ImplicitConstructorDecisions.Ref(structure.SyntaxId, binding);
            row = row is null ? decision : throw new ArgumentException($"`{structure}` was already decided in this pass.", nameof(structure));
        }

        internal bool ContainsKey(StructKoto structure) => this.TryGetValue(structure, out _);

        internal bool TryGetValue(StructKoto structure, out OmittedBaseSelection decision)
        {
            var row = structure.SyntaxOwner is { } table ? table.Hir.Families.ImplicitConstructorDecisions.Get(structure.SyntaxId, binding) : null;
            decision = row ?? default;
            return row.HasValue;
        }
    }
}
