// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class ComparisonTypes
{
    internal static bool IsComposite(BoundType? type)
        => type?.Kind == BoundTypeKind.Tuple || type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 };

    // SPEC 13.4: whether the operator table runs a Type's comparison under a Contract. Whether a library group serves a Type is the witness
    // table's question (Binding.WitnessGroup); conformance asks both.
    internal static bool OperatorTableRuns(BoundType? type, KimiDeclarationId? contract)
        => contract == KimiDeclarationId.Equatable ? ScalarTypes.Supports(type) || ReferenceEquals(type, BoundType.Unit)
            : contract == KimiDeclarationId.Comparable && (ScalarTypes.Width(type) != 0 || ReferenceEquals(type, BoundType.Char));

    // SPEC 13.4, 13.4.1, 5.1, 3.1.3: whether the operator table runs a built-in comparison operator at a terminal Type, == and != under
    // Equatable, the ordering ones under Comparable: the Contract's built-in comparison (OperatorTableRuns), a floating-point Type's
    // order (IEEE; no float is Comparable), a raw pointer's equality (no pointer is Equatable) and every one at Never, which has no value,
    // so none runs (Binding admits each at a Never terminal). The operator table's domain, the one statement MirValidator's comparison
    // BinaryOp and the Backend's Column read: every Type with a built-in operator has equality; Unit and Never, zero-sized, compare by
    // their row's result on equal operands. No other Type: a string's comparison is its library witness's (R8-S1e-b), a Tuple's, a
    // reference layer's and a user Type's a Contract call (TyCtxt.Comparison). Binding's operator rules restate it until the Front's
    // staged removal (its PLAN row, from G662): ComparisonApplicability, CompleteComparison, and ComparisonKindOf's and
    // ComparisonProof's float order under operators.
    internal static bool OperatorCompares(BoundType? type, KimiDeclarationId contract)
        => OperatorTableRuns(type, contract) || type is { IsFloatingPoint: true } || ReferenceEquals(type, BoundType.Never) ||
            (contract == KimiDeclarationId.Equatable && ReferenceTypes.IsPointer(type));
}
