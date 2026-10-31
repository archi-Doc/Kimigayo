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

    // SPEC 13.4, 13.5.5: the Type a comparison operand denotes once its qualifying pair layers are followed; the operand is
    // then shared-borrowed through the layer where a reference to it is required.
    private BoundType ComparisonThroughPairs(Koto operand, BoundType type)
    {
        var scope = this.ConstraintScope(operand);
        for (var depth = 0; depth < 8; depth++)
        {
            var terminal = ComparisonReferent(type);
            if (this.FollowablePair(terminal, scope, out var target) == SemanticsMask.None)
            {
                return depth == 0 ? type : terminal;
            }

            type = target;
        }

        return type;
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

        left = this.ComparisonThroughPairs(binary.Left, left);
        right = this.ComparisonThroughPairs(binary.Right, right);
        if (!ReferenceEquals(left, BoundType.Unit) && ReferenceEquals(ComparisonReferent(left), BoundType.Unit))
        {
            this.adaptations[binary.Left] = new(ExpectedAdaptationKind.ReferentRead, BoundType.Unit);
        }

        if (!ReferenceEquals(right, BoundType.Unit) && ReferenceEquals(ComparisonReferent(right), BoundType.Unit))
        {
            this.adaptations[binary.Right] = new(ExpectedAdaptationKind.ReferentRead, BoundType.Unit);
        }

        left = ComparisonReferent(left);
        right = ComparisonReferent(right);
        if (!FitsType(right, left, out var failure))
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
