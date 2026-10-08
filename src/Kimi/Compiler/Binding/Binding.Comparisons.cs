// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 13.4: every safe value-reference layer of an operand is followed to the terminal Type.
    private static BoundType ComparisonReferent(BoundType type)
    {
        while (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            type = type.Components[0];
        }

        return type;
    }

    // SPEC 13.4, 13.5.5.1: the Type a comparison operand denotes once its qualifying pair layers are followed; the operand is
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

    private BoundType? BindContractComparison(BinaryKoto binary, BoundType self, BindingScope scope)
    {
        var equality = binary.Akind is KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var contract = this.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        var tupleOperator = self.Kind == BoundTypeKind.Tuple;
        var proof = tupleOperator ? this.ComparisonProof(self, contract, scope, true) : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, self, contract: contract)), scope);
        if (proof != ConstraintProof.Proven)
        {
            return this.Fail(binary, proof == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : BindingFailure.UnprovenConstraint, proof == ConstraintProof.Unknown);
        }

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
