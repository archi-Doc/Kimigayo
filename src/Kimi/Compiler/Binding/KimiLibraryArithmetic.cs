// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private bool ValidArithmeticContract(BindingSymbol symbol, KimiDeclarationId id)
    {
        var unary = id == KimiDeclarationId.Negatable;
        var left = ArithmeticContracts.IsLeft(id);
        if (symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not ContractKoto { HasIncompatibleBindingHeader: false, Members.Count: 2, ConstraintNodes.Count: 0, Bases.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration ||
            !ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) || OriginClauses.Get(declaration).Count != 0 ||
            declaration.GenericParameterNodes.Count != (unary ? 0 : 1) ||
            (!unary && (declaration.GenericParameterNodes[0] is not GenericParameterKoto { SemanticsParameter: null, AttributeChain: null } parameter || parameter.Identifier != (left ? "Lhs" : "Rhs"))) ||
            declaration.Members[0] is not SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands: [IdentifierNameKoto { IdentifierName: "Output" }], AttributeChain: null } ||
            declaration.Members[1] is not FunctionKoto { IsRequirement: true, IsGenerated: false, IsSpecialization: false, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, EffectBounds.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function ||
            (function.Modifier & ModifierKind.Unsafe) != 0 || function.Name != ArithmeticContracts.Method(id) || function.NameBoundaryIndex >= 0 ||
            function.Parameters.Count != (unary ? 1 : 2) || OriginClauses.Get(function).Count != 0 ||
            BareType(function.ReturnType) is not MemberAccessKoto result || !BareName(result.Left, "Self") || !BareName(result.Right, "Output"))
        {
            return false;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var receiver = i == (left ? 1 : 0);
            var name = receiver ? "self" : left ? "left" : "right";
            var input = function.Parameters[i];
            if (input.InternalName != name || input.ExternalName != name || input.DefaultValue is not null || input.AttributeChain is not null ||
                input.Type is not TypeSemanticsKoto { SemanticsKind: SemanticsKind.Ref, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null } reference ||
                !BareName(reference.Type, receiver ? "Self" : left ? "Lhs" : "Rhs"))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidBoundArithmeticContract(BindingSymbol symbol, KimiDeclarationId id)
    {
        var declaration = (ContractKoto)symbol.Declaration;
        var function = (FunctionKoto)declaration.Members[1];
        var left = ArithmeticContracts.IsLeft(id);
        if (!BoundContractSelf(symbol, out var self) ||
            declaration.Members[0].BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } output ||
            function.BoundSymbol is not { Type: { } result } requirement ||
            symbol.Contract is not { Requirements: [var only], AssociatedTypes: [var associated] } ||
            only != new BoundRequirement(requirement, symbol) || !ReferenceEquals(associated, output) ||
            result is not { Kind: BoundTypeKind.AssociatedProjection, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var subject, var contract] } ||
            !ReferenceEquals(result.Symbol, output) || !ReferenceEquals(subject, self) || !ReferenceEquals(contract.Symbol, symbol))
        {
            return false;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            var expected = i == (left ? 1 : 0) ? self : declaration.GenericParameterNodes[0].BoundType;
            if (function.Parameters[i].Type.BoundType is not { } input || !OwnInputBorrow(input, SemanticsKind.Ref, function, i, out var referent) || !ReferenceEquals(referent, expected))
            {
                return false;
            }
        }

        return id == KimiDeclarationId.Negatable || (contract.Components is [var counterpart] && ReferenceEquals(counterpart, declaration.GenericParameterNodes[0].BoundType));
    }
}
