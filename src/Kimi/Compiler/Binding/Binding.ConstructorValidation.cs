// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Test/measurement switch for the full reference path. It never changes language behavior.
    internal bool UseConstructorReferenceCheck { get; set; }

    // A failed typed-input obligation with no slot, projection, length or Origin premise is
    // independent of inference mode. Other rejections must be revisited, even after a partial fit.
    private static bool IndependentInputPattern(BoundType type)
    {
        if (type.CarriesOrigin || type.LengthExpression is not null || type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection)
        {
            return false;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!IndependentInputPattern(type.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StableConstructionPremises(CandidateApplicability state, bool unsolved, BoundType? construction, ReadOnlySpan<BoundArgumentOperation> operations)
    {
        if (state != CandidateApplicability.Applicable || unsolved || construction is null || construction.CarriesOrigin)
        {
            return false;
        }

        foreach (var operation in operations)
        {
            if (operation.Source is null)
            {
                continue;
            }

            // A complete, exact input has no literal/default or waiting-context dependency.
            // Its checked acquisition and constraints remain valid at the same fixed bindings.
            if (operation.Source.BoundType is not { } actual || operation.ParameterType is not { } required ||
                operation.Adaptation != ArgumentAdaptation.Exact || !ReferenceEquals(actual, required))
            {
                return false;
            }
        }

        return true;
    }

    // A constructor check's closed reason, the low byte of its record's Reason (RecordCorrelation, ValidateFixedConstruction's Reject).
    internal enum ConstructionCheck : byte
    {
        ChangedSelection,
        WaitingContext,
        UnprovenCandidate,
        AcquisitionCorrelation,
    }

    private static bool ContainsUnspellableType(BoundType type)
    {
        if (type.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (ContainsUnspellableType(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // The reference check restores the original declaration group, including inference-rejected
    // candidates. Syntax mappings are unchanged (each candidate's MappingOf, read); only binding-dependent applicability is rerun. The
    // re-check never solves through the evaluation's Solution: its rows, child slice, operations, Origins and input Origins are its own
    // scratch, and only the selected candidate's Origins (at its re-trial) and operations (on success) are copied into its columns.
    private bool ValidateFixedConstruction(in CallEvaluation evaluation, BoundType construction, int selected, bool[] completedWaiting, BoundType?[] waitingContexts)
    {
        var call = evaluation.Call;
        var scope = evaluation.Scope;
        var frames = evaluation.Frames;
        var stride = evaluation.OperationStride;
        var original = evaluation.Evaluated.AsSpan(0, evaluation.Count);
        var count = original.Length;
        var argumentCount = call.ArgumentNodes.Count;
        var checkedCandidates = this.candidateScratch.Rent(count);
        var checkedOperations = this.argumentOperationScratch.Rent(count * stride);
        var rows = this.rowScratch.Rent(argumentCount);
        var childSlice = this.indexScratch.Rent(2);
        var origins = this.originScratch.Rent(evaluation.OriginSlots);
        var inputs = this.originScratch.Rent(evaluation.InputSlots);
        var childBase = this.childRows.Count;
        var positionBase = this.positionOperations.Count;
        var selectedFunction = (FunctionKoto)original[selected].Symbol!.Declaration;
        FunctionKoto? unproven = null;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var previous = original[i];
                var function = (FunctionKoto)previous.Symbol!.Declaration;
                var candidateOperations = checkedOperations.AsSpan(i * stride, stride);
                candidateOperations.Clear();
                if (!previous.ArgumentMap.Valid || !previous.Accessible || (!this.UseConstructorReferenceCheck && previous.IndependentRejection))
                {
                    if (this.MeasureCallInference)
                    {
                        this.inferenceFixedReuses++;
                    }

                    checkedCandidates[i] = previous with { State = CandidateApplicability.Inapplicable };
                    continue;
                }

                if (!this.UseConstructorReferenceCheck && previous.StableConstruction && ReferenceEquals(previous.DeclaringType, construction))
                {
                    if (this.MeasureCallInference)
                    {
                        this.inferenceFixedReuses++;
                    }

                    checkedCandidates[i] = previous;
                    evaluation.OperationsOf(i).CopyTo(candidateOperations);
                    continue;
                }

                if (this.MeasureCallInference)
                {
                    this.inferenceFixedChecks++;
                }

                RequireOriginWidths(function, evaluation.OriginSlots, evaluation.InputSlots);
                var mapping = evaluation.MappingOf(i);
                var solution = new SlotSolution
                {
                    Binding = this,
                    Call = call,
                    Binder = function,
                    Scope = scope,
                    DeclaringType = construction,
                    Frame = frames[i], // R7-I2 c: a re-trial of candidate i writes its own frame.
                    Mapping = mapping,
                    Origins = origins.AsSpan(0, evaluation.OriginSlots),
                    Inputs = inputs.AsSpan(0, evaluation.InputSlots),
                    FixedConstruction = true,
                    Rows = rows.AsSpan(0, argumentCount), // F1 of the re-check; the selected call reads its first evaluation's (R4-M164).
                    ChildSlice = ResetSlice(childSlice), // R3: the re-check's own composition, at its first solve.
                };
                Debug.Assert(
                    !solution.Rows.Overlaps(evaluation.Rows) && !solution.ChildSlice.Overlaps(evaluation.ChildSlices) &&
                    !solution.Origins.Overlaps(evaluation.Origins) && !solution.Inputs.Overlaps(evaluation.Inputs),
                    "The fixed re-check solves into its own scratch.");
                // The re-check's own row, seeded from the first evaluation's argument map (no requirement group: the view is null).
                ref var row = ref checkedCandidates[i];
                row = new(previous.Symbol, CandidateApplicability.Inapplicable, construction, 0, ArgumentMap: previous.ArgumentMap);
                this.TryCandidate(solution, evaluation.Expected, candidateOperations, ref row);

                // SPEC 10.1, 10.2.1 (PLAN G680): the selected constructor, its waiting arguments completed, keeps its selection past a
                // refuted clause, which is the call's obligation (AsSelected), never a changed selection.
                row.State = i == selected ? AsSelected(row.State, row.ConstraintFailure, CandidateApplicability.Applicable) : row.State;
                var state = row.State;
                if ((state == CandidateApplicability.Pending && !row.PremiseUnknown) || state == CandidateApplicability.Error || row.Unsolved)
                {
                    return Reject(function, ConstructionCheck.UnprovenCandidate);
                }

                unproven ??= row.PremiseUnknown ? function : null;

                if (state is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                {
                    for (var a = 0; a < argumentCount; a++)
                    {
                        if (!completedWaiting[a])
                        {
                            continue;
                        }

                        var pattern = function.Parameters[mapping[a]].Type.BoundType!;
                        // SPEC 10.5, 10.8: the context its completion used, S for a waiting callable and else the parameter Type.
                        var contextPattern = this.WasWaitingCallable(call.ArgumentNodes[a]) ? this.ExpectedCallSignature(function, pattern) : pattern;
                        var context = contextPattern is null ? null : this.CallType(contextPattern, function, frames[i].Types, scope, null, solution.Origins, solution.Inputs, construction, frames[i].Lengths);
                        var usedContext = waitingContexts[a];
                        if (!ReferenceEquals(context, usedContext) &&
                            (context is null || usedContext is null || !this.IsSubtype(context, usedContext) || !this.IsSubtype(usedContext, context)))
                        {
                            return Reject(function, ConstructionCheck.WaitingContext);
                        }
                    }
                }

                if (state == CandidateApplicability.Waiting)
                {
                    return Reject(function, ConstructionCheck.WaitingContext);
                }

                if (i == selected)
                {
                    solution.Origins.CopyTo(evaluation.OriginsOf(i));
                    solution.Inputs.CopyTo(evaluation.InputsOf(i));
                }
            }

            // SPEC 8.3.3: as at the first selection, an Unknown premise rejects the fixed construction only when it can affect it.
            if (unproven is not null && !this.PremisesCannotAffect(call, checkedCandidates.AsSpan(0, count), checkedOperations, stride))
            {
                return Reject(unproven, ConstructionCheck.UnprovenCandidate);
            }

            var winner = this.SelectBest(checkedCandidates.AsSpan(0, count), checkedOperations, stride);
            if (winner != selected)
            {
                var other = winner;
                for (var i = 0; other < 0 && i < count; i++)
                {
                    if (i != selected && checkedCandidates[i].State == CandidateApplicability.Applicable)
                    {
                        other = i;
                    }
                }

                return Reject(other < 0 ? null : (FunctionKoto)original[other].Symbol!.Declaration, ConstructionCheck.ChangedSelection);
            }

            checkedOperations.AsSpan(selected * stride, stride).CopyTo(evaluation.OperationsOf(selected));
            _ = this.FixCallSlots(call, selectedFunction, construction, [], [], frames[selected], fixedConstruction: true);
            return true;
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.rowScratch.Return(rows, clearArray: true);
            this.indexScratch.Return(childSlice);
            this.childRows.RemoveRange(childBase, this.childRows.Count - childBase);
            this.positionOperations.RemoveRange(positionBase, this.positionOperations.Count - positionBase);
            this.argumentOperationScratch.Return(checkedOperations, clearArray: true);
            this.candidateScratch.Return(checkedCandidates, clearArray: true);
        }

        // SPEC 10.8: the check (Reason), the tentative selection (role 0) with the construction it was selected for (its Instance, a
        // construction's constructed Type), and the constructor the check found instead (role 1), when it is another.
        bool Reject(FunctionKoto? other, ConstructionCheck check)
        {
            var site = this.FactSite(selectedFunction);
            this.candidateFacts.Add(new(ProblemFactKind.Reason, SourceRef.Of(call), (long)check));
            this.candidateFacts.Add(new(ProblemFactKind.Related, site, 0));
            this.candidateFacts.Add(new(ProblemFactKind.Instance, site, this.report.Keep(construction)));
            if (other is not null && !ReferenceEquals(other, selectedFunction))
            {
                this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(other), 1));
            }

            this.RecordSelection(call, BindingFailure.ConstructorSelectionChanged);
            return false;
        }
    }

    // SPEC 7.4: a group contract's correlation at a slot that the call's inputs and fixed bindings do not establish (CheckCallShapeContracts):
    // the check with the slot's ordinal above it (Reason), the group contract (role 0), the corresponding declaration (role 1), when it is
    // another, and, where both are known, the slot's two bindings at its Type parameter, Actual then Expected as the candidate builder's
    // pair: the corresponding declaration's found (Actual), the group contract's required (Expected).
    private void RecordCorrelation(InvocationKoto call, FunctionKoto group, FunctionKoto corresponding, int slot, BoundType? left, BoundType? right)
    {
        this.candidateFacts.Add(new(ProblemFactKind.Reason, SourceRef.Of(call), (long)ConstructionCheck.AcquisitionCorrelation | ((long)slot << 8)));
        this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(group), 0));
        if (!ReferenceEquals(corresponding, group))
        {
            this.candidateFacts.Add(new(ProblemFactKind.Related, this.FactSite(corresponding), 1));
        }

        if (left is not null && right is not null)
        {
            var parameter = this.FactSite(CallOwnSlots(group)[slot]);
            this.candidateFacts.Add(new(ProblemFactKind.Actual, parameter, this.report.Keep(right)));
            this.candidateFacts.Add(new(ProblemFactKind.Expected, parameter, this.report.Keep(left)));
        }

        this.RecordSelection(call, BindingFailure.UnprovenAcquisitionCorrelation);
    }

    // SPEC 7.4, 10.8, 23.3: a constructor check's record from its facts (Reject, RecordCorrelation): the check and a correlation's slot, the
    // two declarations labelled by the check (the second read by its role), the Note by the check, and as evidence the construction or the
    // slot with its two bindings in the group contract's order. The record has no Cause fact (RecordSelection's record), so its tail is the
    // pair, read by position.
    private void ReportConstructionCheck(Koto node, DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var check = (ConstructionCheck)(byte)facts[0].Value;
        var selected = (FunctionKoto)this.compilation.SyntaxOf(facts[1].Site);
        var correlation = check == ConstructionCheck.AcquisitionCorrelation;
        (string Role, Koto At, string? Label)[] related = facts[(correlation ? 2 : 3)..] is [{ Kind: ProblemFactKind.Related, Value: 1 } other, ..]
            ? [("declaration", selected, correlation ? "Group contract" : "Tentative selection"), ("declaration", this.compilation.SyntaxOf(other.Site), correlation ? "Corresponding declaration" : "Fixed-binding check")]
            : [("declaration", selected, "Tentative selection")];
        var note = check switch
        {
            ConstructionCheck.WaitingContext => "The fixed-binding check needs a different or additional waiting-argument context; the argument is never analyzed again to select another constructor (SPEC 10.8)",
            ConstructionCheck.UnprovenCandidate => "The original constructor group cannot be fully judged with the inferred construction Type fixed (SPEC 10.8)",
            ConstructionCheck.AcquisitionCorrelation => "The group contract is checked before applicability filtering; independent inputs and fixed bindings do not establish the required shared acquisition (SPEC 7.4)",
            _ => "The original constructor group, including inference-rejected candidates, no longer has the same unique Best Candidate when the construction Type is fixed (SPEC 10.8)",
        };
        object[] evidence = !correlation ? [DiagnosticTypeName(this.report.Value(facts[2]))]
            : facts[^1] is { Kind: ProblemFactKind.Expected } left
                ? [CallOwnSlots(selected)[(int)(facts[0].Value >> 8)].Identifier, DiagnosticTypeName(this.report.Value(left)), DiagnosticTypeName(this.report.Value(facts[^2]))]
                : [CallOwnSlots(selected)[(int)(facts[0].Value >> 8)].Identifier];
        node.ReportIn(store, problem.Requirement, problem.Code, evidence: evidence, related: related, note: note, condition: problem.Condition);
    }
}
