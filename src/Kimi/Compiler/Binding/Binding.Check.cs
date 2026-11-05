// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The end of a Binding pass (Check; DECISIONS BU-81, refining R2b-D2).
// - Responsibility: the pass's failure producers (the definition obligations, the needed conformance paths and capability sites, the
//   call and runtime-test Constraint checks, the part prerequisites, Unvisited to Unresolved); in a Final pass, then, the HIR of every
//   module, the library included (PublishPass), and H1's TransferRequired judgment over it (CheckTransfers, DECISIONS BU-84), with one
//   republication when it failed a node; then the counting of the node states and the collection of the issues.
// - Inputs: the bound nodes, the obligations, the needed sites and paths, the facts the pass recorded.
// - Outputs: every failure the pass decides; the published HIR columns of a Final pass; the pass summary, issues and derived issues.
// - Invariants: every step that decides a failure runs before the publication, so the published columns describe the pass's final
//   facts; PublishHir() after a Final pass returns its version without work. CheckBound runs the same three steps again.
// - Prohibitions: the counting decides no failure and writes no fact the publisher reads.
public sealed partial class Binding
{
    private BindingResult Check(BindingMode mode)
    {
        this.ProduceFailures(mode);
        if (mode == BindingMode.Final)
        {
            this.PublishPass();
            if (this.CheckTransfers())
            {
                // H1 (DECISIONS BU-84): its failed nodes are published Error, which the assertion checks creates no new subject.
                this.PublishPass();
                this.AssertTransfers();
            }
        }

        return this.CountStates(mode);
    }

    // Every failure the pass decides at its end, in the order the nodes were bound.
    private void ProduceFailures(BindingMode mode)
    {
        if (mode == BindingMode.Final)
        {
            this.formationCauses?.Clear();
            this.JudgeObligations();

            this.SettleSpecificationLinks();
            this.PublishNeededLimits();
            this.FailNeededSites();
            this.SettleDerivedUses();
        }

        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            // API and constraint validation can invalidate a target or Type after call selection; a node whose own selection calls a
            // declaration that failed (CallPlan.Called: a call's target, a Property use's accessors) rests on that failure (SPEC 23.1.2).
            if (this.InvalidCalledDeclaration(node) is { } invalidTarget && node.BindingFailure == BindingFailure.None)
            {
                this.CompleteDependent(node, invalidTarget);
            }
            else if (node is InvocationKoto { BoundCall: { } call })
            {
                this.RequireConstraint(node, this.CheckCallTypeConstraints(call, this.ConstraintScope(node)), mode);
            }
            else if (node is IsKoto { BoundRuntimeTest: { } runtimeTest } test)
            {
                var proof = this.CheckRuntimeTestTypeConstraints(runtimeTest, this.ConstraintScope(test));
                this.RequireConstraint(test, proof, mode);
                if (proof != ConstraintProof.Proven)
                {
                    test.BoundRuntimeTest = null;
                }
            }

            if (mode == BindingMode.Final && node.BindingFailure is BindingFailure.NoApplicableCandidate or BindingFailure.TypeMismatch && this.UnformedFieldRead(node) is { } unformed)
            {
                this.partPrerequisites[node] = unformed; // Derived from the Field's formation failure (IsDerived).
            }

            if (node.BindingState == BindingState.Unvisited)
            {
                // Skipped dependent syntax has no Type evidence, not a proof of an unsupported feature.
                // Actual subset gates diagnose themselves; the final fallback still rejects unresolved work.
                node.BindingState = BindingState.Unresolved;
            }
        }

        this.FailNeededSites(); // The sites the loop's checks needed (BU-76).
    }

    // The pass summary: each node's state, counted once the failures are decided, and each failed node's issue, or its derived issue.
    private BindingResult CountStates(BindingMode mode)
    {
        var resolved = 0;
        var unresolved = 0;
        var invalid = 0;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            switch (node.BindingState)
            {
                case BindingState.Resolved:
                    resolved++;
                    break;
                case BindingState.Invalid:
                    invalid++;
                    break;
                default:
                    unresolved++;
                    break;
            }

            if (mode == BindingMode.Final && node.BindingState != BindingState.Resolved && node.BindingFailure != BindingFailure.None && this.IsDerived(node))
            {
                this.derivedIssues.Add(node);
            }
            else if (mode == BindingMode.Final && node.BindingState != BindingState.Resolved && node.BindingFailure != BindingFailure.None)
            {
                var code = node.BindingFailure switch
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
                    BindingFailure.UnsafeFunctionValue => DiagnosticCode.UnsafeFunctionValue_Kd,
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
                if (node.BindingFailure == BindingFailure.TypeMismatch && (node is TryKoto || node is ReturnKoto { Parent: TryKoto }))
                {
                    code = DiagnosticCode.InvalidTry_Kd;
                }
                else if (node.BindingFailure == BindingFailure.TransferRequired && this.PreparedMove(node))
                {
                    code = DiagnosticCode.DefaultArgumentMove_Kd; // SPEC 7.2.3 (Q8-4, Q8-8): a default never moves a preceding argument.
                }

                this.issues.Add(new(node, code) { Failure = node.BindingFailure });
                if (this.formationCauses?.TryGetValue(node, out var causes) == true && (causes & TypeRoleCause.Role) != 0)
                {
                    // SPEC 23.1.2: a target role that is not proven beside a Language cause of the same occurrence is its own record.
                    this.issues.Add(new(node, DiagnosticCode.UnprovenConstraint_Kd) { Failure = BindingFailure.UnprovenConstraint });
                }
            }
        }

        return new(mode, resolved, unresolved, invalid);
    }
}
