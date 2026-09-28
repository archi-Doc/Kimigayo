// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private static bool BoundFamilyReceiver(BoundType? type, BoundType self, BoundOrigin origin)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Symbol: null, Components: [var target], OriginArguments.Count: 0 } &&
            ReferenceEquals(target, self) && ReferenceEquals(type.Origin, origin);

    // SPEC 22.1.2.1: next returns the declared LentItem family at its receiver's
    // step; the family's formation binds its own step over the same Contract Self.
    private bool ValidBoundLendingIterator(BindingSymbol symbol)
    {
        if (symbol.Declaration is not ContractKoto { Members: [SyntaxFormKoto family, FunctionKoto next] } ||
            symbol.Type is not { Kind: BoundTypeKind.Nominal, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components.Count: 0 } self ||
            !ReferenceEquals(self.Symbol, symbol) || family.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } familySymbol ||
            !ReferenceEquals(familySymbol.Declaration, family) || family.Operands[1].BoundType?.Origin is not { } formation ||
            next.BoundSymbol is not { Schema.Origins: [var stepParameter], Type: { } result } requirement ||
            next.Parameters.Count != 1 || !ReferenceEquals(next.ReturnType?.BoundType, result) ||
            symbol.Contract is not { AssociatedTypes: [var boundFamily], Requirements: [var boundNext] } ||
            !ReferenceEquals(boundFamily, familySymbol) || !ReferenceEquals(boundNext, requirement))
        {
            return false;
        }

        var step = stepParameter.Origin;
        return formation.Kind == OriginKind.Parameter && formation.Slot == 0 && ReferenceEquals(formation.Binder, family) &&
            step.Kind == OriginKind.Parameter && step.Slot == 0 && ReferenceEquals(step.Binder, next) &&
            BoundFamilyReceiver(family.Operands[1].BoundType, self, formation) && BoundFamilyReceiver(next.Parameters[0].Type.BoundType, self, step) &&
            result is { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var item] } &&
            ReferenceEquals(result.Symbol, this.Option) &&
            item is { Kind: BoundTypeKind.AssociatedProjection, Semantics: SemanticsKind.Owner, Origin: null, Components: [var subject], OriginArguments: [var itemStep] } &&
            ReferenceEquals(item.Symbol, familySymbol) && ReferenceEquals(subject, self) && ReferenceEquals(itemStep, step);
    }
}
