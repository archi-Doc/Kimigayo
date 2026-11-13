// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// SPEC 4.6.1, 8.7, 22.1: Position and PositionRange are closed Contracts; every integer Type's Position is a row of the library
// witness table (Binding.LibraryWitnesses), and integers gain no members. SPEC 8.7.4: PrimitiveIntegerSupplies is its list of the
// Contracts a PrimitiveInteger premise proves; the Copy and Owned it also supplies are its refinement parents (its row of the
// intrinsic table, KimiLibraryKinds), which Enumerate's ancestor rows give at every projection, the arithmetic Contracts in
// Binding.ArithmeticContracts and Binding.BuiltinArithmetic, and member lookup's Position clause in Binding.ContractCalls
// (temporary, PLAN G776).
public sealed partial class Binding
{
    // SPEC 8.7.4's Contract list: the Contracts a proven `T is PrimitiveInteger` proves for T, the built-in Equatable, Comparable
    // and Utf8Format conformances of the twelve Types and their conformance to the closed Contract Position. ProveConformance
    // proves them for T, and AvailableEffectBounds reads their effect bounds (SPEC 8.11.3) under the PrimitiveInteger premise.
    private static readonly KimiDeclarationId[] PrimitiveIntegerSupplies =
        [KimiDeclarationId.Equatable, KimiDeclarationId.Comparable, KimiDeclarationId.Utf8Format, KimiDeclarationId.Position];

    // SPEC 8.7: only the Kimi Types that §22.1 names conform to Position and PositionRange; a declaration elsewhere, including
    // one of a user Contract that refines either, is an error rather than a conformance.
    private bool IsClosedContractGrant(BindingSymbol contract, BindingSymbol type)
        => (IsRefinement(contract, this.Library.Position) || IsRefinement(contract, this.Library.PositionRange)) &&
            !ReferenceEquals(type.Declaration.CodeContext.Kotonoha, this.Library.Kotonoha);
}
