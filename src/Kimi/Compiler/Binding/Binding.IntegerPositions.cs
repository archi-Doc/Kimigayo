// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// SPEC 4.6.1, 8.7, 22.1: Position and PositionRange are closed Contracts; every integer Type's Position is a row of the library
// witness table (Binding.LibraryWitnesses), and integers gain no members.
public sealed partial class Binding
{
    // SPEC 8.7: only the Kimi Types that §22.1 names conform to Position and PositionRange; a declaration elsewhere, including
    // one of a user Contract that refines either, is an error rather than a conformance.
    private bool IsClosedContractGrant(BindingSymbol contract, BindingSymbol type)
        => (IsRefinement(contract, this.Library.Position) || IsRefinement(contract, this.Library.PositionRange)) &&
            !ReferenceEquals(type.Declaration.CodeContext.Kotonoha, this.Library.Kotonoha);
}
