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
