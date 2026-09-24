// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static BoundType ComparisonReferent(BoundType type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? type.Components[0] : type;

    private BoundType? BindContractComparison(BinaryKoto binary, BoundType self, BindingScope scope)
    {
        var equality = binary.Akind is KotoKind.EqualsEquals or KotoKind.ExclamationEquals;
        var contract = this.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        var proof = this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, self, contract: contract)), scope);
        if (proof != ConstraintProof.Proven)
        {
            return Fail(binary, proof == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : BindingFailure.UnprovenConstraint, proof == ConstraintProof.Unknown);
        }

        var name = equality ? "equals" : "compare";
        if (contract.Contract is not { } shape || !shape.MembersByName.TryGetValue(name, out var members) || members.Count != 1)
        {
            return Fail(binary, BindingFailure.InvalidConstraint);
        }

        var call = binary.ComparisonStorage;
        if (call is null || !call.Span.Equals(binary.Span))
        {
            var target = new ComparisonCalleeKoto(binary) { Parent = binary };
            binary.ComparisonStorage = call = new(binary, target, new Koto[2]);
        }

        var callee = (ComparisonCalleeKoto)call.Method;
        callee.Self = self;
        callee.BoundSymbol = members[0];
        callee.BoundType = members[0].Type;
        callee.BindingState = BindingState.Resolved;
        callee.BindingFailure = BindingFailure.None;
        ((Koto[])call.ArgumentNodes)[0] = binary.Left;
        ((Koto[])call.ArgumentNodes)[1] = binary.Right;
        call.BindingState = BindingState.Unvisited;
        call.BindingFailure = BindingFailure.None;
        call.BoundMeaning = null;
        call.BoundSymbol = null;
        this.nodes.Add(call);
        this.BindCall(call, scope, null);
        if (call.BoundCall is not { } selected)
        {
            return Complete(binary, null);
        }

        if (!DependentType(self))
        {
            var resolved = this.InstantiateRequirementCall(selected, selected);
            if (resolved is null)
            {
                return Fail(binary, BindingFailure.Unsupported, true);
            }

            call.CallStorage = resolved;
        }

        binary.ComparisonActive = true;
        return Complete(binary, BoundType.Boolean);
    }
}
