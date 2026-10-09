// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The outcome of an omitted base clause's selection (SPEC 6.2.3.6).</summary>
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

    /// <summary>An Unknown premise of a candidate can affect the selection (SPEC 8.4.8.2).</summary>
    Unproven,

    /// <summary>The selection rests on another failure, such as an invalid declaration.</summary>
    Dependent,

    /// <summary>The selection reaches a state the query does not decide.</summary>
    Unsupported,
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
    private readonly Dictionary<StructKoto, OmittedBaseSelection> implicitConstructorDecisions = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionKoto> judgedConstructors = new();
    private readonly List<FunctionKoto> withdrawnConstructors = new();
    private Dictionary<FunctionKoto, OmittedBaseSelection>? omittedBaseQueries;

    /// <summary>Gets or sets a value indicating whether each declaration pass records the omitted base query of every explicit
    /// constructor whose base clause is omitted, for comparison with its bound base call.</summary>
    internal bool CaptureOmittedBaseQueries { get; set; }

    /// <summary>Gets the omitted base queries of the latest declaration pass when <see cref="CaptureOmittedBaseQueries"/> is set.</summary>
    internal IReadOnlyDictionary<FunctionKoto, OmittedBaseSelection>? OmittedBaseQueries => this.omittedBaseQueries;

    // SPEC 6.2.3.6: the omitted base clause's ordinary selection over the direct base's constructors with no arguments, made from the
    // derived constructor's scope with the evaluation and classification of a call (Binding.CallSelection), without publishing. The
    // obligations, candidate bounds and per-call state that evaluation touches are restored.
    internal OmittedBaseSelection SelectOmittedBaseConstructor(FunctionKoto constructor)
    {
        if (constructor.BaseInitializer is not { ArgumentNodes.Count: 0 } call)
        {
            return new(OmittedBaseOutcome.Unsupported);
        }

        if (this.BaseConstructorGroup(constructor, out _) is not { } group)
        {
            return new(OmittedBaseOutcome.NoBaseConstructor);
        }

        var candidates = new CallCandidates(group, null, null);
        var shape = MeasureCandidates(candidates, true);
        var obligationCount = this.obligations.Count;
        var acquisition = this.acquisitionFailure;
        var requirementContract = this.activeRequirementContract;
        var (mappings, comparisons, checks) = (this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks);
        var scratch = this.typeScratch.Rent(Math.Max(1, shape.Generics));
        var lengthArguments = this.lengthScratch.Rent(shape.Generics);
        var mapping = this.indexScratch.Rent(1);
        var used = this.flagScratch.Rent(Math.Max(1, shape.Parameters));
        var origins = this.originScratch.Rent(shape.Origins);
        var inputs = this.originScratch.Rent(shape.InputOrigins);
        var evaluated = this.candidateScratch.Rent(shape.Count);
        var operations = this.argumentOperationScratch.Rent(shape.Count);
        var boundStarts = this.indexScratch.Rent(shape.Count + 1);
        var boundMark = this.BeginCandidateBounds();
        try
        {
            var evaluation = new CallEvaluation
            {
                Call = call,
                Callee = call.Method,
                Scope = this.scopes[constructor],
                Scratch = scratch,
                LengthArguments = lengthArguments,
                ExplicitLengths = [],
                Mapping = mapping,
                Used = used,
                Origins = origins,
                Inputs = inputs,
                Evaluated = evaluated,
                Operations = operations,
                OperationStride = 1,
                BoundStarts = boundStarts,
                AllTypes = [],
                AllLengths = [],
                AllMaps = [],
                AllOrigins = [],
                AllInputs = [],
                MaxGenerics = shape.Generics,
                OriginSlots = shape.Origins,
                InputSlots = shape.InputOrigins,
                Winner = -1,
                PremisesOnly = true,
            };
            this.acquisitionFailure = null;
            foreach (var candidate in candidates)
            {
                this.EvaluateCallCandidate(ref evaluation, candidate);
            }

            boundStarts[evaluation.Count] = this.candidateBounds.Count;
            var (selection, winner) = this.ClassifySelection(ref evaluation);
            return selection switch
            {
                CallSelection.Selected => evaluated[winner] is { State: CandidateApplicability.Applicable, Unsolved: false } selected
                    ? new(OmittedBaseOutcome.Selected, (FunctionKoto)selected.Symbol!.Declaration)
                    : new(OmittedBaseOutcome.Unsupported),
                CallSelection.NoneApplicable => new(OmittedBaseOutcome.NoneApplicable),
                CallSelection.Ambiguous => new(OmittedBaseOutcome.Ambiguous),
                CallSelection.Unproven => this.UnprovenBaseSelection(ref evaluation),
                CallSelection.InvalidDeclaration => evaluation.InvalidDeclaration is { } invalid ? new(OmittedBaseOutcome.Dependent, Cause: invalid) : new(OmittedBaseOutcome.Unsupported),
                CallSelection.FailedSignature => new(OmittedBaseOutcome.Dependent, Cause: evaluation.FailedPendingSignature),
                _ => new(OmittedBaseOutcome.Dependent, Cause: evaluation.IncompleteSignature),
            };
        }
        finally
        {
            this.EndCandidateBounds(boundMark, false);
            for (var i = obligationCount; i < this.obligations.Count; i++)
            {
                this.obligationSet.Remove(this.obligations[i]);
            }

            this.obligations.RemoveRange(obligationCount, this.obligations.Count - obligationCount);
            this.acquisitionFailure = acquisition;
            this.activeRequirementContract = requirementContract;
            (this.inferenceMappings, this.inferenceComparisons, this.inferenceCandidateChecks) = (mappings, comparisons, checks);
            this.indexScratch.Return(boundStarts);
            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.candidateScratch.Return(evaluated, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.flagScratch.Return(used);
            this.indexScratch.Return(mapping);
            this.lengthScratch.Return(lengthArguments, clearArray: true);
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    // As a call publishes it (BindCallCore): one pending candidate's Callable or Constraint premise, an exclusive witness waiting for
    // OCC-X as the conformance's own limit (SPEC 12.4.4.1), or no single premise.
    private OmittedBaseSelection UnprovenBaseSelection(ref CallEvaluation evaluation)
    {
        if (evaluation.PendingCount != 1)
        {
            return new(OmittedBaseOutcome.Unproven);
        }

        if (evaluation.CallableFailure is { } callable)
        {
            return new(OmittedBaseOutcome.Unproven, Cause: callable.Clause);
        }

        if (evaluation.ConstraintFailure is not { } constraint)
        {
            return new(OmittedBaseOutcome.Unproven);
        }

        return this.PendingExclusiveConformance(constraint.Constraint) is not { } path ? new(OmittedBaseOutcome.Unproven, Cause: constraint.Clause)
            : path.InheritedFrom is null ? new(OmittedBaseOutcome.Dependent, Cause: path.Use)
            : new(OmittedBaseOutcome.Unsupported);
    }

    // SPEC 6.2.3.6: decides each pending synthesized constructor of a derived structure once the declarations are bound, base first.
    // A selected base constructor makes it a member of the structure's `init` group; any other outcome withdraws it from the pass.
    private void CompleteImplicitConstructors()
    {
        this.implicitConstructorDecisions.Clear();
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
        var clause = structure.Bases[0];
        var constructor = structure.SynthesizedConstructor!;
        var selection = new OmittedBaseSelection(OmittedBaseOutcome.Dependent, Cause: clause);
        this.implicitConstructorDecisions.Add(structure, selection);
        if (this.inheritanceStates.TryGetValue(structure.BoundSymbol!, out var state) && state == 2 && clause.BoundType is { } baseType &&
            this.CheckTypeConstraints(baseType, this.scopes[structure]) == ConstraintProof.Proven)
        {
            // A base synthesized constructor that does not exist for a reason other than absence leaves this one resting on that reason.
            if (baseType.Symbol?.Declaration is StructKoto parent && (parent.ImplicitConstructorPending || this.implicitConstructorDecisions.ContainsKey(parent)) &&
                this.CompleteImplicitConstructor(parent) is { Outcome: OmittedBaseOutcome.Unproven or OmittedBaseOutcome.Dependent or OmittedBaseOutcome.Unsupported } inherited)
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

    // SPEC 6.2.3.6: the declaration-time decisions must hold after bodies. The selection is judged again, and an existing synthesized
    // constructor's bound base call selected the same constructor; a disagreement fails closed at the base clause.
    private void RevalidateImplicitConstructors()
    {
        foreach (var constructor in this.judgedConstructors)
        {
            var structure = (StructKoto)constructor.Parent!;
            var decision = this.implicitConstructorDecisions[structure];
            if (this.SelectOmittedBaseConstructor(constructor) != decision ||
                (decision.Winner is { } winner && constructor.BaseInitializer?.BoundCall is { } bound && !ReferenceEquals(bound.Target.Declaration, winner)))
            {
                this.Fail(structure.Bases[0], BindingFailure.Unsupported);
            }
        }
    }

    // Records the omitted base query of every explicit constructor whose base clause is omitted (CaptureOmittedBaseQueries).
    private void CaptureOmittedBases()
    {
        this.omittedBaseQueries?.Clear();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is FunctionKoto { IsConstructor: true, HasOmittedBaseInitializer: true } constructor && this.scopes.ContainsKey(constructor))
            {
                (this.omittedBaseQueries ??= new(ReferenceEqualityComparer.Instance))[constructor] = this.SelectOmittedBaseConstructor(constructor);
            }
        }
    }
}
