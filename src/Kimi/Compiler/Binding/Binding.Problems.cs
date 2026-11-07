// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Binding's problem records (R7-E1).
// - Responsibility: the one table from a node's failure to its code (CodeOf).
// - Inputs: a failure and the node it is at.
// - Outputs: the DiagnosticCode the failure reports.
// - Invariants: the code is a function of the failure, but for the rows that read the node until their checks record their own (E3, E4).
// - Prohibitions: no failure is decided and no message rendered here.
public sealed partial class Binding
{
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
}
