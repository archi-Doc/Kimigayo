// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 22.1, 22.2.1 (PLAN G32): a fixed array's associated iterator Type, read from its entry conformance's witness in the
// library witness table (Binding.LibraryWitnesses).
public sealed partial class Binding
{
    // `T.(Iterable).IteratorType(a)` with `T = [N of E]`: the witness's result Type at `E` and `N`, its one Origin
    // parameter replaced by the projection's argument. Null for any other projection. No closure: this runs on every
    // associated-projection normalization.
    private BoundType? FixedArrayIteratorType(BoundType projection)
    {
        if (projection.Symbol is not { Name: "IteratorType" } associated || associated.Scope.Owner.BoundSymbol is not { } contract ||
            WitnessGroup(projection.Components[0], contract.LibraryDeclaration) is not { } group ||
            this.ContractWitness(group, contract) is not { Type: { } result, Declaration: FunctionKoto witness } target ||
            !this.RentWitnessArguments(group, target, projection.Components[0], out var types, out var lengths, out var count))
        {
            return null;
        }

        try
        {
            return this.SubstituteType(result, witness, types.AsSpan(0, count), lengths.AsSpan(0, count), (BoundOrigin[])projection.OriginArguments);
        }
        finally
        {
            this.ReturnWitnessArguments(types, lengths);
        }
    }
}
