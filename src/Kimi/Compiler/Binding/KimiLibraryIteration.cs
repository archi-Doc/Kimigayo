// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private static bool BoundFamilyReceiver(BoundType? type, BoundType self, BoundOrigin origin, SemanticsKind semantics = SemanticsKind.Uniq)
        => type is { Kind: BoundTypeKind.Semantics, Symbol: null, Components: [var target], OriginArguments.Count: 0 } && type.Semantics == semantics &&
            ReferenceEquals(target, self) && ReferenceEquals(type.Origin, origin);

    private static bool BoundContractSelf(BindingSymbol symbol, out BoundType self)
    {
        self = symbol.Type!;
        return self is { Kind: BoundTypeKind.Nominal, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components.Count: 0 } && ReferenceEquals(self.Symbol, symbol);
    }

    private static bool BoundParameterOrigin(BoundOrigin? origin, Koto binder)
        => origin is { Kind: OriginKind.Parameter, Slot: 0 } && ReferenceEquals(origin.Binder, binder);

    private static bool BoundFamilyProjection(BoundType? type, BindingSymbol family, BoundType self, BoundOrigin? step)
        => type is { Kind: BoundTypeKind.AssociatedProjection, Semantics: SemanticsKind.Owner, Origin: null, Components: [var subject] } &&
            ReferenceEquals(type.Symbol, family) && ReferenceEquals(subject, self) &&
            (step is null ? type.OriginArguments.Count == 0 : type.OriginArguments is [var argument] && ReferenceEquals(argument, step));

    // SPEC 22.1.2.2-3: the entry borrows (or takes) Self and returns the declared IteratorType family at
    // that borrow; the family requires the recognized LendingIterator for the same borrow of Self.
    private bool ValidBoundIterationEntry(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (symbol.Declaration is not ContractKoto { ConstraintNodes: [IsKoto clause], Members: [FunctionKoto entry] } || !BoundContractSelf(symbol, out var self) ||
            clause.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } family || !ReferenceEquals(family.Declaration, clause) ||
            clause.BoundConstraint is not { Contract: { } required } || !ReferenceEquals(required, this.LendingIterator) ||
            entry.BoundSymbol is not { Type: { } result } requirement || entry.Parameters.Count != 1 || !ReferenceEquals(entry.ReturnType?.BoundType, result) ||
            symbol.Contract is not { AssociatedTypes: [var boundFamily], Requirements: [var boundEntry] } ||
            !ReferenceEquals(boundFamily, family) || !ReferenceEquals(boundEntry, requirement))
        {
            return false;
        }

        var receiver = entry.Parameters[0].Type.BoundType;
        if (id == KimiDeclarationId.IntoIterable)
        {
            return clause.FormationType is null && requirement.Schema?.Origins is null or { Count: 0 } &&
                ReferenceEquals(receiver, self) && BoundFamilyProjection(result, family, self, null);
        }

        var semantics = id == KimiDeclarationId.UniqIterable ? SemanticsKind.Uniq : SemanticsKind.Ref;
        return clause.FormationType?.BoundType is { Origin: { } formation } formationType && BoundParameterOrigin(formation, clause) &&
            BoundFamilyReceiver(formationType, self, formation, semantics) &&
            requirement.Schema?.Origins is [var sourceParameter] && BoundParameterOrigin(sourceParameter.Origin, entry) &&
            BoundFamilyReceiver(receiver, self, sourceParameter.Origin, semantics) && BoundFamilyProjection(result, family, self, sourceParameter.Origin);
    }

    // SPEC 22.1.2.1: Iterator refines the recognized LendingIterator and fixes its LentItem family at
    // every step to the declared Item of the same Self.
    private bool ValidBoundIterator(BindingSymbol symbol)
    {
        if (symbol.Declaration is not ContractKoto { Bases: [var parent], Members: [SyntaxFormKoto item], ConstraintNodes: [IsKoto refinement] } ||
            !ReferenceEquals(parent.BoundSymbol, this.LendingIterator) || !BoundContractSelf(symbol, out var self) ||
            item.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } itemSymbol || !ReferenceEquals(itemSymbol.Declaration, item) ||
            symbol.Contract is not { } contract || !contract.AssociatedTypes.Contains(itemSymbol) ||
            this.LendingIterator.Declaration is not ContractKoto { Members: [SyntaxFormKoto { BoundSymbol: { } lent }, ..] } || !ReferenceEquals(refinement.BoundSymbol, lent))
        {
            return false;
        }

        // The refinement is stored at the family's canonical step parameter.
        return refinement.BoundConstraint is { Kind: ConstraintKind.TypeIdentity, Subject: { OriginArguments: [var step] } subject, RequiredType: { } identity } &&
            BoundParameterOrigin(step, lent.Declaration) && BoundFamilyProjection(subject, lent, self, step) && BoundFamilyProjection(identity, itemSymbol, self, null);
    }

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
