// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // SPEC 15.8: equals and compare borrow two values of Self at distinct input Origins; Comparable
    // refines the recognized Equatable, whose requirement it inherits unchanged.
    private bool ValidBoundComparisonContract(BindingSymbol symbol, KimiDeclarationId id)
    {
        var ordering = id == KimiDeclarationId.Comparable;
        if (symbol.Declaration is not ContractKoto { Members: [FunctionKoto function] } declaration || function.BoundSymbol is not { Type: { } result } requirement ||
            !BoundContractSelf(symbol, out var self) || !ReferenceEquals(result, ordering ? BoundType.Primitives["i32"] : BoundType.Boolean))
        {
            return false;
        }

        for (var i = 0; i < 2; i++)
        {
            if (function.Parameters[i].Type.BoundType is not { } input || !OwnInputBorrow(input, SemanticsKind.Ref, function, i, out var referent) || !ReferenceEquals(referent, self))
            {
                return false;
            }
        }

        if (!ordering)
        {
            return symbol.Contract is { Requirements: [var only] } && only == new BoundRequirement(requirement, symbol);
        }

        var equatable = this.GetSymbol(KimiDeclarationId.Equatable);
        return equatable is not null && ReferenceEquals(declaration.Bases[0].BoundSymbol, equatable) && symbol.Contract?.Ancestors.Contains(equatable) == true && equatable.Declaration is ContractKoto { Members: [FunctionKoto { BoundSymbol: { } equals }] } &&
            symbol.Contract is { Requirements: [var inherited, var own] } && inherited == new BoundRequirement(equals, equatable) && own == new BoundRequirement(requirement, symbol);
    }

    // SPEC 4.6.9, 22.1: index/indexUniq borrow Self<Key> with the Contract's mode and the key shared, and
    // publish the Element Place of the recognized Indexable during the receiver borrow.
    private bool ValidBoundIndexableContract(BindingSymbol symbol, KimiDeclarationId id)
    {
        var exclusive = id == KimiDeclarationId.UniqIndexable;
        var semantics = exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref;
        if (symbol.Declaration is not ContractKoto { GenericParameterNodes: [var parameter], Members: [.., FunctionKoto function] } declaration ||
            parameter.BoundType is not { Kind: BoundTypeKind.Parameter } key || function.BoundSymbol is not { Type: { } result } requirement ||
            (exclusive ? this.GetSymbol(KimiDeclarationId.Indexable) : symbol)?.Declaration is not ContractKoto { Members: [SyntaxFormKoto { BoundSymbol: { Kind: BindingSymbolKind.AssociatedType } element }, ..] } ||
            symbol.Contract is not { } contract || contract.Requirements.Count != (exclusive ? 2 : 1) || contract.Requirements[^1] != new BoundRequirement(requirement, symbol) ||
            contract.AssociatedTypes is not [var associated] || !ReferenceEquals(associated, element) ||
            function.Parameters[0].Type.BoundType is not { } receiver || !OwnInputBorrow(receiver, semantics, function, 0, out var self) ||
            function.Parameters[1].Type.BoundType is not { } borrowedKey || !OwnInputBorrow(borrowedKey, SemanticsKind.Ref, function, 1, out var keyReferent))
        {
            return false;
        }

        if (exclusive)
        {
            // The refined parent is a bound reference of the recognized Indexable applied to this Contract's own Key.
            var parent = this.GetSymbol(KimiDeclarationId.Indexable)!.Declaration;
            BindingSymbol? reference = null;
            for (var i = 0; i < contract.Ancestors.Count; i++)
            {
                reference = ReferenceEquals(contract.Ancestors[i].Declaration, parent) ? contract.Ancestors[i] : reference;
            }

            if (reference?.Type is not { Kind: BoundTypeKind.Constructed, Components: [var parentKey] } || !ReferenceEquals(parentKey, key))
            {
                return false;
            }
        }

        return BoundContractSelf(symbol, out var contractSelf) && ReferenceEquals(self, contractSelf) && ReferenceEquals(keyReferent, key) &&
            result is { Kind: BoundTypeKind.Semantics, Symbol: null, OriginArguments.Count: 0, Components: [var place] } && result.Semantics == semantics &&
            ReferenceEquals(result.Origin, receiver.Origin) &&
            place is { Kind: BoundTypeKind.AssociatedProjection, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var subject, { Symbol.Declaration: var declaring, Components: [var declaredKey] }] } &&
            ReferenceEquals(place.Symbol, element) && ReferenceEquals(subject, contractSelf) && ReferenceEquals(declaring, element.Scope.Owner) && ReferenceEquals(declaredKey, key);
    }
}
