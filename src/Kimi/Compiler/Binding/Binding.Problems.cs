// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Binding's recorder (R7-E1; DIAGNOSTICS 4.1): a check records its failure as a problem with typed facts in BindingReport.
// - Responsibility: Record, a check's problem; ClearFailure, how a failure leaves a node inside a pass; the one table from a failure to its
//   code (CodeOf); the summary's walk of a failed node's problems, each direct or derived by its own requirement (CollectProblem,
//   CollectProblems); the adapter's reads of a problem's facts (ReportProblem, Mismatch, LocatedPart, FactOf).
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
        this.SetRecordedFailure(node, BindingFailure.None);
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
            BindingFailure.PremiseClosureLimit => DiagnosticCode.PremiseClosureLimit_Kd,
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

    // A problem its check may find again at the node with other facts (one requirement failing twice): numbered by the report's one rule
    // (BindingReport.Condition), so a further problem takes its own key and an equal one its first's.
    private BoundType? RecordNumbered(Koto node, BindingFailure failure, ReadOnlySpan<ProblemFact> facts, bool unresolved = false)
        => this.Record(node, failure, facts, this.report.Condition(new(this.CodeOf(node, failure), DiagnosticRequirement.Binding(failure), SourceRef.Of(node), 0), facts), unresolved);

    // SPEC 23.3: the sides a mismatch compared and the syntax that shows them. Until E3 records the family's conditions, a node keeps its
    // first failure, as a Fail does.
    private BoundType? RecordTypeMismatch(Koto node, Koto at, in MismatchSide actual, in MismatchSide expected)
    {
        if (this.RecordedFailure(node) != BindingFailure.None)
        {
            return this.Fail(node, BindingFailure.TypeMismatch);
        }

        if (this.IdentityPremiseErrorRead(this.ConstraintScope(node), actual.Type, expected.Type) is { } premise)
        {
            this.AddPrerequisite(node, premise.Use); // SPEC 8.2.3: a premise error is never reported again at uses (IsDerived).
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

    // SPEC 23.1.1: a form outside the subset, located at the part that shows it, such as one capture entry or a callee; its own record
    // beside any other failure of its node (SPEC 23.1.2).
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

    // A failed node's problem, the slot's (index -1 for a slot without one) or a further one: derived when its own requirement rests on the
    // node's recorded inputs (IsDerived, rule W), else direct (SPEC 23.1.2).
    private void CollectProblem(Koto node, BindingFailure failure, DiagnosticCode code, int problem)
        => (this.IsDerived(node, failure) ? this.derivedIssues : this.issues).Add(new(node, code) { Failure = failure, Problem = problem });

    // The summary's walk (DIAGNOSTICS 4.3, several problems of one node): each problem of a failed node but its slot's own issue is an issue of
    // its own, direct or derived by its own requirement (CollectProblem). An Origin record beyond the first is published with the node's first
    // record, since Binding.Issues has one row per relation node, which the test MIR side compares, until it is the problem list (R7-E5).
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
                this.CollectProblem(node, (BindingFailure)problem.Requirement.Kind, problem.Code, i);
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
        => this.report.Count == 0 ? -1 : this.report.Find(SourceRef.Written(node), DiagnosticRequirement.Binding(this.RecordedFailure(node)), 0);

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

    // The adapter's rows of a problem whose facts are its whole record, so no table of its node's first failure answers for it: a requirement's
    // row that renders its facts and its subject's syntax, the operator row, then the fact-less row; false for a mismatch or a located part,
    // whose ReportIssue arms read their facts.
    private bool ReportProblem(Koto node, DiagnosticCollection? store, int index)
    {
        var problem = this.report.Problem(index);
        switch ((BindingFailure)problem.Requirement.Kind)
        {
            case BindingFailure.NoInit when this.FactOf(index, ProblemFactKind.Reason) is { } reason:
                ReportNoInit(node, store, problem, (NoInitProblem)reason.Value);
                break;
            case BindingFailure.ArrayAnnotationInference:
                this.ReportArrayInference(node, store, index, problem);
                break;
            case BindingFailure.VirtualDeclaration when this.FactOf(index, ProblemFactKind.Reason) is { } cause:
                ReportVirtualDeclarationFailure((FunctionKoto)node, store, problem, (VirtualDeclarationCause)cause.Value);
                break;
            case BindingFailure.InvalidConstraint when this.report.Facts(index) is [] or [{ Kind: ProblemFactKind.Cause }]: // RecordConstraint's record
                this.ReportConstraint(node, store, problem.Requirement, problem.Code, problem.Condition, this.report.Facts(index) is [var named] ? this.compilation.SyntaxOf(named.Site) : null);
                break;
            case BindingFailure.InvalidTypeFormation when this.FactOf(index, ProblemFactKind.Count) is not null:
                this.ReportArity(node, store, index, problem);
                break;
            case BindingFailure.Duplicate when this.FactOf(index, ProblemFactKind.Related) is { } first:
                ReportDuplicate(node, store, problem, this.compilation.SyntaxOf(first.Site));
                break;
            case BindingFailure.Duplicate when this.FactOf(index, ProblemFactKind.Cause) is { } later:
                node.ReportDerivedIn(store, problem.Requirement, [this.compilation.SyntaxOf(later.Site).KeyIn(this.compilation.StoreOf(this.compilation.SyntaxOf(later.Site)), problem.Requirement)], problem.Condition);
                break;
            case BindingFailure.ParameterShapeMismatch when this.FactOf(index, ProblemFactKind.Reason) is not null:
                this.ReportParameterShapes(node, store, index, problem);
                break;
            case BindingFailure.InvalidEffectBound when node is EffectBoundKoto effect && this.FactOf(index, ProblemFactKind.Reason) is not null:
                this.ReportEffectRejection(effect, store, index, problem);
                break;
            case BindingFailure.Ambiguous when this.FactOf(index, ProblemFactKind.Instance) is not null:
                this.ReportAssociatedAmbiguity(node, store, index, problem);
                break;
            case BindingFailure.InvalidAssociatedType or BindingFailure.IncompatibleImplementation when this.FactOf(index, ProblemFactKind.Instance) is not null:
                this.ReportDisagreement(node, store, index, problem);
                break;
            case BindingFailure.IncompatibleImplementation when this.FactOf(index, ProblemFactKind.Related) is not null:
                this.ReportCallablePremise(node, store, index, problem);
                break;
            case BindingFailure.AssociatedInference when this.FactOf(index, ProblemFactKind.Reason) is not null:
                this.ReportAssociatedInference(node, store, index, problem);
                break;
            case BindingFailure.UnsatisfiedConstraint or BindingFailure.UnprovenConstraint when this.FactOf(index, ProblemFactKind.Related) is { } entry && entry.Value == 2:
                // Role 2, "entry": a range iteration's selected entry declaration (IntoIterable, Iterable or UniqIterable, SPEC 14.6.2),
                // which the range does not satisfy or is not proven to; ordinal 2 keeps the clause record's roles 0 and 1 apart.
                this.ReportRangeIteration(node, store, index, problem);
                break;
            case BindingFailure.UnsatisfiedConstraint or BindingFailure.UnprovenConstraint when this.FactOf(index, ProblemFactKind.Related) is not null && this.FactOf(index, ProblemFactKind.Constraint) is not null:
                this.ReportClauseConstraint(node, store, index, problem); // No clause record carries an Instance, and no absence a Constraint.
                break;
            case BindingFailure.MissingName or BindingFailure.UnprovenConstraint when this.FactOf(index, ProblemFactKind.Instance) is not null:
                this.ReportConstructorAbsence(node, store, index, problem);
                break;
            case BindingFailure.MissingOverrideTarget or BindingFailure.AmbiguousOverrideTarget or BindingFailure.OverrideContractMismatch or BindingFailure.DuplicateOverride or BindingFailure.UnprovenOverrideCondition
                when node is FunctionKoto function && this.FactOf(index, ProblemFactKind.Reason) is not null:
                this.ReportOverrideFailure(function, store, index, problem);
                break;
            case BindingFailure.NotObjectPayload when this.FactOf(index, ProblemFactKind.Related) is { } renouncing: // Role 0: the declaration that opts out, its own or an inherited base's.
                ReportObjectPayload(node, store, problem, this.compilation.SyntaxOf(renouncing.Site));
                break;
            case BindingFailure.ProtectedPlacement:
                ReportProtectedPlacement(node, store, problem.Requirement);
                break;
            case BindingFailure.AccessorReceiverShape when node is PropertyAccessorKoto { ReceiverType: { } written } accessor:
                this.ReportAccessorReceiverShape(accessor, store, written, problem.Requirement);
                break;
            case var _ when this.FactOf(index, ProblemFactKind.Operand) is not null:
                this.ReportOperatorOperand(node, store, index, problem);
                break;
            case var _ when this.report.Facts(index).IsEmpty:
                node.ReportIn(store, problem.Requirement, problem.Code, condition: problem.Condition);
                break;
            default:
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
