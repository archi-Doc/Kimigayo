// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 13.4: every safe value-reference layer of an operand is followed to the terminal Type.
    internal static BoundType ComparisonReferent(BoundType type)
    {
        while (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            type = type.Components[0];
        }

        return type;
    }

    // SPEC 3.4.3, 8.2.3, 13.4, 13.5.5: the Type a built-in operator's operand or a selection's receiver denotes, in the operand's scope, in
    // one order: its Type as the scope's premises make it (CallableContractType: its associated projections normalized, then its identity
    // premises substituted, the premising MirTypes.Premised reads through TyCtxt.Premised), then below each of its ref, uniq and qualifying
    // pair layers: the one statement of it, which TyCtxt.OperandTerminal publishes to the MIR builder, whose Types keep a premised parameter
    // or projection (DECISIONS BU-72). Premises change only abstract parts, so a Type without one is its own image; the Contract
    // environment's rewrite of a closed Type's Origins alone is not applied, as no premise is in it (MirTypes.Agree ignores Origins). Each
    // step descends into a component of the Type, so the walk ends within the Type's height (IMPL A.4) and needs no limit of its own.
    internal BoundType OperandTerminal(Koto operand, BoundType type)
    {
        var scope = type.HasAbstractPart ? this.ConstraintScope(operand) : null;
        type = scope is null ? type : this.CallableContractType(type, scope);
        while (true)
        {
            type = ComparisonReferent(type);
            if (!TryPairLayer(type, out _, out _) || this.FollowablePair(type, scope ??= this.ConstraintScope(operand), out var target) == SemanticsMask.None)
            {
                return type;
            }

            this.termWalks++;
            type = target;
        }
    }

    private BoundType? CompleteComparison(
        BinaryKoto binary, BoundType left, BoundType right, BindingScope scope, OperatorApplicability decision)
    {
        if ((ReferenceTypes.EndsInString(left) || ReferenceEquals(left, BoundType.Never)) &&
            (ReferenceTypes.EndsInString(right) || ReferenceEquals(right, BoundType.Never)))
        {
            this.CompareInPlace(binary.Left);
            this.CompareInPlace(binary.Right);
            this.StringThroughLayers(binary.Left, left);
            this.StringThroughLayers(binary.Right, right);
            return Complete(binary, BoundType.Boolean);
        }

        // SPEC 3.5.3: Unit behind ref and uniq layers alone is read; one below a pair layer is inspected (OperandTerminal follows it).
        if (!ReferenceEquals(left, BoundType.Unit) && ReferenceEquals(ComparisonReferent(left), BoundType.Unit))
        {
            this.adaptations[binary.Left] = new(ExpectedAdaptationKind.ReferentRead, BoundType.Unit);
        }

        if (!ReferenceEquals(right, BoundType.Unit) && ReferenceEquals(ComparisonReferent(right), BoundType.Unit))
        {
            this.adaptations[binary.Right] = new(ExpectedAdaptationKind.ReferentRead, BoundType.Unit);
        }

        var operand = right;
        left = this.OperandTerminal(binary.Left, left);
        right = this.OperandTerminal(binary.Right, right);

        // SPEC 13.4, 3.8: the terminal Types are equal under the Type relation, which relates Never only to itself, or one operand is Never:
        // the right one's own Type here, as applicability fitted a Never left one to the right; a Never terminal below a layer is ordinary.
        if (!ReferenceEquals(operand, BoundType.Never) && !IsSubtype(right, left, out var failure))
        {
            if (this.CommonOriginType(left, right, out _) is not { } common)
            {
                return this.FailMismatch(binary, binary.Right, right, left, failure);
            }

            left = common;
        }

        var integer = decision.Requirement?.Contract == this.Library.PrimitiveInteger && decision.Proof == ConstraintProof.Proven;
        return left.Kind == BoundTypeKind.Primitive || ReferenceTypes.IsPointer(left) || integer
            ? Complete(binary, BoundType.Boolean) : this.BindContractComparison(binary, left, scope);
    }

    // Applicability has already performed the exact proof; this owner now constructs only the selected comparison call.
    private BoundType? BindContractComparison(BinaryKoto binary, BoundType self, BindingScope scope)
    {
        var equality = binary.Akind is KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var contract = this.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        var tupleOperator = self.Kind == BoundTypeKind.Tuple;
        var name = equality ? "equals" : "compare";
        if (contract.Contract is not { } shape || !shape.MembersByName.TryGetValue(name, out var members) || members.Count != 1)
        {
            return this.Fail(binary, BindingFailure.InvalidConstraint);
        }

        var call = this.BindSelectedRequirement(binary, binary.ComparisonStorage, [binary.Left, binary.Right], self, members[0].Contract, members[0].Symbol, scope, tupleOperator);
        binary.ComparisonStorage = call;
        binary.ComparisonActive = call?.BoundCall is not null;
        return Complete(binary, binary.ComparisonActive ? BoundType.Boolean : null);
    }
}
