// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Binding's recorder (R7-E1; DIAGNOSTICS 4.1): a check records its failure as a problem with typed facts in BindingReport.
// - Responsibility: Record, a check's problem; ClearFailure, how a failure leaves a node inside a pass; the one table from a failure to its
//   code (CodeOf); the summary's walk of a node's problems beyond its slot's issue (CollectProblems); the adapter's reads of a problem's
//   facts (ReportProblem, Mismatch, LocatedPart, FactOf).
// - Inputs: a node, its failure and the facts its check read.
// - Outputs: a problem and its facts in the report; the node's state and failure, through Fail, as its first failure; a node's further
//   issues, each by its own code and condition. Record is a check's way in; RecordFurtherOriginRecord and a report's defect add a problem
//   that sets no slot.
// - Invariants: one way in data: a problem writes the slot, and no slot value becomes a problem's key, code or facts. Not one way in control:
//   until a family migrates, its sites read the slot to decide whether to record. A node without a failure has no linked problem: a pass's
//   reset empties the report, and inside a pass a failure leaves through ClearFailure or a snapshot that saves its chain; HirTables.Reset
//   of a try's arms comes before anything binds them (SemanticsIndexed).
// - Prohibitions: no failure is decided and no message rendered here.
public sealed partial class Binding
{
    private readonly BindingReport report = new();

    /// <summary>Gets the fact sites of this pass that no semantic write had numbered (R7-E1's hypothesis is none).</summary>
    [Transitional("R7", Reason = "The row of the fact-site hypothesis, until R7-N5 owns SourceRef.Of numbering an id-less node.")]
    internal int IdlessFactSites => this.report.IdlessSites;

    /// <summary>Clears a node's failure for a retried attempt: its problems go with it.</summary>
    /// <param name="node">The node.</param>
    internal void ClearFailure(Koto node)
    {
        node.BindingFailure = BindingFailure.None;
        this.UnlinkProblems(node);
    }

    // The code of a failure at a node: the one table, with the rows that read the node until their checks record their own (E3, E4).
    private DiagnosticCode CodeOf(Koto node, BindingFailure failure)
    {
        var code = failure switch
        {
            BindingFailure.InvalidTestDefinition => DiagnosticCode.InvalidTestDefinition_Kd,
            BindingFailure.InvalidLayoutAttribute => DiagnosticCode.InvalidLayoutAttribute_Kd,
            BindingFailure.ConflictingLayout => DiagnosticCode.ConflictingLayout_Kd,
            BindingFailure.InvalidLibraryImport => DiagnosticCode.InvalidLibraryImport_Kd,
            BindingFailure.MissingNativeRequirement => DiagnosticCode.MissingNativeRequirement_Kd,
            BindingFailure.InvalidImportSignature => DiagnosticCode.InvalidImportSignature_Kd,
            BindingFailure.ConflictingImportSignature => DiagnosticCode.ConflictingImportSignature_Kd,
            BindingFailure.ConflictingRuntimeSymbol => DiagnosticCode.ConflictingRuntimeSymbol_Kd,
            BindingFailure.ConflictingImportSupply => DiagnosticCode.ConflictingImportSupply_Kd,
            BindingFailure.DirectCallFunctionValue => DiagnosticCode.DirectCallFunctionValue_Kd,
            BindingFailure.UnavailableReservedImport => DiagnosticCode.UnavailableReservedImport_Kd,
            BindingFailure.SplitCLayoutStorage => DiagnosticCode.SplitCLayoutStorage_Kd,
            BindingFailure.InvalidInlineLayout => DiagnosticCode.InvalidInlineLayout_Kd,
            BindingFailure.MissingName or BindingFailure.MissingType => DiagnosticCode.UnresolvedBinding_Kd,
            BindingFailure.QualificationRequired => DiagnosticCode.QualificationRequired_Kd,
            BindingFailure.Ambiguous => DiagnosticCode.AmbiguousBinding_Kd,
            BindingFailure.Duplicate => DiagnosticCode.DuplicateBinding_Kd,
            BindingFailure.DuplicateDictionaryKey => DiagnosticCode.DuplicateDictionaryKey_Kd,
            BindingFailure.TypeMismatch => DiagnosticCode.TypeMismatch_Kd,
            BindingFailure.NotCallable => DiagnosticCode.NotCallable_Kd,
            BindingFailure.NoApplicableCandidate => DiagnosticCode.NoApplicableOverload_Kd,
            BindingFailure.Cycle => DiagnosticCode.CyclicBinding_Kd,
            BindingFailure.CaseLimit => DiagnosticCode.OwnershipCaseLimit_Kd,
            BindingFailure.TypeHeightLimit => DiagnosticCode.TypeHeightLimit_Kd,
            BindingFailure.InvalidAssignment => DiagnosticCode.InvalidAssignment_Kd,
            BindingFailure.InvalidLiteral => DiagnosticCode.InvalidNumericLiteral_Kd,
            BindingFailure.Access => DiagnosticCode.InaccessibleBinding_Kd,
            BindingFailure.Capture => DiagnosticCode.InvalidCaptureBinding_Kd,
            BindingFailure.InvalidOrigin => DiagnosticCode.InvalidOriginBinding_Kd,
            BindingFailure.MissingOrigin => DiagnosticCode.MissingOriginBinding_Kd,
            BindingFailure.InvalidTypeFormation => DiagnosticCode.InvalidTypeFormation_Kd,
            BindingFailure.InvalidConstraint => DiagnosticCode.InvalidConstraint_Kd,
            BindingFailure.InvalidSelfClause => DiagnosticCode.InvalidSelfClause_Kd,
            BindingFailure.ClosedContractConformance => DiagnosticCode.ClosedContractConformance_Kd,
            BindingFailure.ArithmeticConformance => DiagnosticCode.InvalidArithmeticConformance_Kd,
            BindingFailure.ArithmeticSelection => DiagnosticCode.ArithmeticSelection_Kd,
            BindingFailure.ArithmeticDirection => DiagnosticCode.ArithmeticDirection_Kd,
            BindingFailure.NotIndexable => DiagnosticCode.NotIndexable_Kd,
            BindingFailure.NotObjectPayload => DiagnosticCode.NotObjectPayload_Kd,
            BindingFailure.UnprovenConstraint => DiagnosticCode.UnprovenConstraint_Kd,
            BindingFailure.UnsatisfiedConstraint => DiagnosticCode.UnsatisfiedConstraint_Kd,
            BindingFailure.MissingImplementation => DiagnosticCode.MissingContractImplementation_Kd,
            BindingFailure.IncompatibleImplementation => DiagnosticCode.IncompatibleContractImplementation_Kd,
            BindingFailure.InvalidAssociatedType => DiagnosticCode.InvalidAssociatedType_Kd,
            BindingFailure.AssociatedInference => DiagnosticCode.AssociatedTypeInferenceFailed_Kd,
            BindingFailure.InvalidPattern => DiagnosticCode.InvalidPattern_Kd,
            BindingFailure.NonExhaustiveMatch => DiagnosticCode.NonExhaustiveMatch_Kd,
            BindingFailure.TransferRequired => DiagnosticCode.TransferRequired_Kd,
            BindingFailure.MissingSpecializationTarget => DiagnosticCode.MissingSpecializationTarget_Kd,
            BindingFailure.SpecializationInputMismatch => DiagnosticCode.SpecializationInputMismatch_Kd,
            BindingFailure.ExclusiveBorrowRequired => DiagnosticCode.ExclusiveBorrowRequired_Kd,
            BindingFailure.ParameterShapeMismatch => DiagnosticCode.ParameterShapeMismatch_Kd,
            BindingFailure.UnboundTypeArgument => DiagnosticCode.UnboundTypeArgument_Kd,
            BindingFailure.UnprovenAcquisitionCorrelation => DiagnosticCode.UnprovenAcquisitionCorrelation_Kd,
            BindingFailure.ConstructorSelectionChanged => DiagnosticCode.ConstructorSelectionChanged_Kd,
            BindingFailure.BoundMethodValue => DiagnosticCode.BoundMethodValue_Kd,
            BindingFailure.OriginRelation => this.OriginRelationCode(node),
            BindingFailure.OriginContract => DiagnosticCode.UnprovenOriginContract_Kd,
            BindingFailure.InvalidEffectBound => DiagnosticCode.InvalidEffectBound_Kd,
            BindingFailure.ArrayAnnotationInference => DiagnosticCode.ArrayAnnotationInference_Kd,
            BindingFailure.NoInit => DiagnosticCode.InvalidNoInit_Kd,
            BindingFailure.SharedBindingAssignment => DiagnosticCode.SharedBindingAssignment_Kd,
            BindingFailure.ExclusiveBindingAssignment => DiagnosticCode.ExclusiveBindingAssignment_Kd,
            BindingFailure.SharedPathAccess => DiagnosticCode.SharedPathAccess_Kd,
            BindingFailure.ExclusivePathTake => DiagnosticCode.ExclusivePathTake_Kd,
            BindingFailure.ReceiverShapeMismatch => DiagnosticCode.ReceiverShapeMismatch_Kd,
            BindingFailure.AccessorReceiverShape => DiagnosticCode.AccessorReceiverShape_Kd,
            BindingFailure.BareOwningShorthand => DiagnosticCode.BareOwningShorthand_Kd,
            BindingFailure.NonCopyOperand => DiagnosticCode.NonCopyOperand_Kd,
            BindingFailure.NonNumericOperand => DiagnosticCode.NonNumericOperand_Kd,
            BindingFailure.NonIntegerOperand => DiagnosticCode.NonIntegerOperand_Kd,
            BindingFailure.OperatorNotDefined => DiagnosticCode.OperatorNotDefined_Kd,
            BindingFailure.InvalidShiftCount => DiagnosticCode.InvalidShiftCount_Kd,
            BindingFailure.InvalidWrapConversion => DiagnosticCode.InvalidWrapConversion_Kd,
            BindingFailure.InvalidBitConversion => DiagnosticCode.InvalidBitConversion_Kd,
            BindingFailure.GenericBitConversion => DiagnosticCode.GenericBitConversion_Kd,
            BindingFailure.ProtectedPlacement => DiagnosticCode.ProtectedPlacement_Kd,
            BindingFailure.VirtualDeclaration => DiagnosticCode.InvalidVirtualDeclaration_Kd,
            BindingFailure.MissingOverrideTarget => DiagnosticCode.MissingOverrideTarget_Kd,
            BindingFailure.AmbiguousOverrideTarget => DiagnosticCode.AmbiguousOverrideTarget_Kd,
            BindingFailure.OverrideContractMismatch => DiagnosticCode.OverrideContractMismatch_Kd,
            BindingFailure.DuplicateOverride => DiagnosticCode.DuplicateOverride_Kd,
            BindingFailure.UnprovenOverrideCondition => DiagnosticCode.UnprovenOverrideCondition_Kd,
            BindingFailure.BaseCall => DiagnosticCode.InvalidBaseCall_Kd,
            BindingFailure.Internal => DiagnosticCode.InternalInvariant_Kd,
            _ => DiagnosticCode.Unsupported_Kd,
        };

        if (failure == BindingFailure.TypeMismatch && (node is TryKoto || node is ReturnKoto { Parent: TryKoto }))
        {
            code = DiagnosticCode.InvalidTry_Kd;
        }
        else if (failure == BindingFailure.TransferRequired && this.PreparedMove(node))
        {
            code = DiagnosticCode.DefaultArgumentMove_Kd; // SPEC 7.2.3 (Q8-4, Q8-8): a default never moves a preceding argument.
        }

        return code;
    }

    // A check's problem. Fail sets the state and slot of the node's first failure, and its state write numbers the node, so the subject
    // numbers nothing new.
    private BoundType? Record(Koto node, BindingFailure failure, ReadOnlySpan<ProblemFact> facts, ushort condition = 0, bool unresolved = false)
    {
        this.Fail(node, failure, unresolved);
        this.report.Add(new(this.CodeOf(node, failure), DiagnosticRequirement.Binding(failure), SourceRef.Of(node), condition), facts);
        return null;
    }

    // SPEC 23.3: the sides a mismatch compared and the syntax that shows them. Until E3 records the family's conditions, a node keeps its
    // first failure, as a Fail does.
    private BoundType? RecordTypeMismatch(Koto node, Koto at, in MismatchSide actual, in MismatchSide expected)
    {
        if (node.BindingFailure != BindingFailure.None)
        {
            return this.Fail(node, BindingFailure.TypeMismatch);
        }

        if (this.IdentityPremiseErrorRead(node, actual.Type, expected.Type) is { } premise)
        {
            this.AddPrerequisite(node, premise); // SPEC 8.2.3: a premise error is never reported again at uses (IsDerived).
        }

        var site = this.FactSite(at);
        return this.Record(
            node,
            BindingFailure.TypeMismatch,
            [
                this.SideFact(ProblemFactKind.Actual, ProblemFactKind.ActualForm, site, actual),
                this.SideFact(ProblemFactKind.Expected, ProblemFactKind.ExpectedForm, site, expected),
            ]);
    }

    // A side's fact: a Type's kept index, or a form's row with, above its byte, its count plus one (ProblemFact.cs).
    private ProblemFact SideFact(ProblemFactKind type, ProblemFactKind form, SourceRef site, in MismatchSide side)
        => side.Type is { } bound ? new(type, site, this.report.Keep(bound)) : new(form, site, (long)side.Form | ((long)(side.Count + 1) << 8));

    // SPEC 23.1.1: a form outside the subset, located at the part that shows it, such as one capture entry or a callee.
    [Transitional("R7", Reason = "Records only at a node whose slot takes the failure (its first), for snapshot 0, until E2's located batch.")]
    private void FailUnsupportedAt(Koto node, SourceSpan part, bool unresolved = false)
    {
        if (node.BindingFailure != BindingFailure.None)
        {
            this.Fail(node, BindingFailure.Unsupported, unresolved);
            return;
        }

        this.RecordLocated(node, part, unresolved);
    }

    private void RecordLocated(Koto node, SourceSpan part, bool unresolved = false)
    {
        long value = ((long)part.Start << 32) | (uint)part.Length;
        this.Record(node, BindingFailure.Unsupported, [new(ProblemFactKind.Span, SourceRef.Of(node), value)], unresolved: unresolved);
    }

    // A fact's site is a node the check read, so normally numbered. One that is not takes its module's next id here, as a semantic write would,
    // on a rejected program only, and is counted.
    [Transitional("R7", Reason = "Its count is the row of the fact-site hypothesis, until R7-N5 owns SourceRef.Of numbering an id-less node.")]
    private SourceRef FactSite(Koto at)
    {
        this.report.IdlessSites += ReferenceEquals(at.SyntaxOwner, at.Kotonoha.Syntax) ? 0 : 1;
        return SourceRef.Of(at);
    }

    // The summary's walk (DIAGNOSTICS 4.3, several problems of one node): each problem of a failed node but its slot's own issue is an issue of
    // its own, however the slot's failure stands. An Origin record beyond the first is published with the node's first record, since
    // Binding.Issues has one row per relation node, which the test MIR side compares, until it is the problem list (R7-E5).
    private void CollectProblems(Koto node)
    {
        if (this.report.Count == 0)
        {
            return;
        }

        var slot = this.SlotProblem(node);
        for (var i = this.report.First(SourceRef.Written(node)); i >= 0; i = this.report.Next(i))
        {
            if (i != slot && this.FactOf(i, ProblemFactKind.OriginRecord) is null)
            {
                var problem = this.report.Problem(i);
                this.issues.Add(new(node, problem.Code) { Failure = (BindingFailure)problem.Requirement.Kind, Problem = i });
            }
        }
    }

    // A valid program has no problem, so it pays this test and no dictionary operation.
    private (int First, int Last) UnlinkProblems(Koto node)
        => this.report.Count == 0 ? BindingReport.NoChain : this.report.Unlink(SourceRef.Written(node));

    private void RestoreProblems(Koto node, (int First, int Last) chain)
    {
        if (this.report.Count != 0)
        {
            this.report.Restore(SourceRef.Written(node), chain);
        }
    }

    // The problem of the node's slot, the issue's bridge to its facts; -1 without one.
    [Transitional("R7", Reason = "Finds the slot's issue among a node's problems until the slot's reads and Binding.Issues go (R7-E5).")]
    private int SlotProblem(Koto node)
        => this.report.Count == 0 ? -1 : this.report.Find(SourceRef.Written(node), DiagnosticRequirement.Binding(node.BindingFailure), 0);

    // The part of a located problem (its Span fact), or null for no problem.
    private SourceSpan? LocatedPart(int problem)
        => problem >= 0 && this.FactOf(problem, ProblemFactKind.Span) is { } fact ? new((int)(fact.Value >> 32), (int)fact.Value) : null;

    // A problem's fact of a kind, or null.
    private ProblemFact? FactOf(int problem, ProblemFactKind kind)
    {
        foreach (ref readonly var fact in this.report.Facts(problem))
        {
            if (fact.Kind == kind)
            {
                return fact;
            }
        }

        return null;
    }

    // The adapter's rows of a problem whose facts are its whole record, so no table of its node's first failure answers for it; false for a
    // mismatch or a located part, whose ReportIssue arms read their facts.
    private bool ReportProblem(Koto node, int index)
    {
        var problem = this.report.Problem(index);
        if (this.FactOf(index, ProblemFactKind.Operand) is not null)
        {
            this.ReportOperatorOperand(node, index, problem);
        }
        else if (this.report.Facts(index).IsEmpty)
        {
            node.Report(problem.Requirement, problem.Code, condition: problem.Condition);
        }
        else
        {
            return false;
        }

        return true;
    }

    // A TypeMismatch problem's facts: the syntax that shows it and what it compared, a form as its text.
    private (Koto At, object Actual, object Expected) Mismatch(int problem)
    {
        var facts = this.report.Facts(problem);
        return (this.compilation.SyntaxOf(facts[0].Site), this.SideOf(facts[0]), this.SideOf(facts[1]));
    }

    private object SideOf(in ProblemFact fact)
        => fact.Kind is ProblemFactKind.Actual or ProblemFactKind.Expected
            ? this.report.Value(fact) : FormText((MismatchForm)(byte)fact.Value, (int)(fact.Value >> 8) - 1);
}
