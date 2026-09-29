// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 22.1, 22.1.2.2 (PLAN G32): a fixed array has no source declaration, so the compiler records its standard entry
// conformances; each witness is the same-named receiver function of the internal Kimi fixed-array group, generic over the
// element Type `E` and `length N`.
public sealed partial class Binding
{
    /// <summary>Adds the Kimi fixed-array members that the fixed array's entry conformances map requirements to.</summary>
    /// <param name="destination">The function list to extend without duplicates.</param>
    internal void CollectFixedArrayWitnesses(List<FunctionKoto> destination)
    {
        Add(this.FixedArrayWitness(this.Library.Iterable));
        Add(this.FixedArrayWitness(this.Library.UniqIterable));

        void Add(BindingSymbol? witness)
        {
            if (witness?.Declaration is FunctionKoto function && !destination.Contains(function))
            {
                destination.Add(function);
            }
        }
    }

    private static bool IsFixedArrayEntry(BoundType? type, BindingSymbol contract)
        => type is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } && contract.LibraryDeclaration is KimiDeclarationId.Iterable or KimiDeclarationId.UniqIterable;

    // The witness of the fixed array's conformance to the entry Contract, or null when the Kimi group lacks it.
    private BindingSymbol? FixedArrayWitness(BindingSymbol contract)
    {
        var name = contract.LibraryDeclaration switch
        {
            KimiDeclarationId.Iterable => "iterate",
            KimiDeclarationId.UniqIterable => "iterateUniq",
            _ => null,
        };
        return name is not null && this.Library.FixedArrayMembers is { } members && this.scopes.TryGetValue(members, out var scope) &&
            scope.Values.TryGetValue(name, out var witness) && witness.Next is null ? witness : null;
    }

    // The generic slots of a fixed-array member's `E` and `length N`, which share one slot numbering.
    private bool FixedArraySlots(FunctionKoto witness, out int element, out int length)
    {
        element = length = -1;
        if (this.scopes.TryGetValue(witness, out var scope) && scope.Types.TryGetValue("E", out var type) && scope.Values.TryGetValue("N", out var count))
        {
            element = type.Slot;
            length = count.Slot;
        }

        return element >= 0 && length >= 0 && element != length;
    }

    // `T.(Iterable).IteratorType(a)` with `T = [N of E]`: the witness's result Type at `E` and `N`, its one Origin
    // parameter replaced by the projection's argument. Null for any other projection. No closure: this runs on every
    // associated-projection normalization.
    private BoundType? FixedArrayIteratorType(BoundType projection)
    {
        if (projection.Components[0] is not { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner } receiver ||
            projection.Symbol is not { Name: "IteratorType" } associated || associated.Scope.Owner.BoundSymbol is not { } contract ||
            !IsFixedArrayEntry(receiver, contract) || this.FixedArrayWitness(contract) is not { Type: { } result, Declaration: FunctionKoto witness } ||
            !this.RentFixedArrayArguments(witness, receiver, out var types, out var lengths, out var count))
        {
            return null;
        }

        try
        {
            return this.SubstituteType(result, witness, types.AsSpan(0, count), lengths.AsSpan(0, count)) is { } typed
                ? this.SubstituteStoredOrigins(typed, witness, (BoundOrigin[])projection.OriginArguments) : null;
        }
        finally
        {
            this.ReturnFixedArrayArguments(types, lengths);
        }
    }

    // SPEC 21.3.1: a generic entry call on a fixed array instantiates the Kimi member at the array's element Type and length.
    private BoundCall? InstantiateFixedArrayEntry(BoundCall call, BoundType self, BindingSymbol contract, BoundCall? destination)
    {
        if (this.FixedArrayWitness(contract) is not { Declaration: FunctionKoto witness } target ||
            !this.RentFixedArrayArguments(witness, self, out var types, out var lengths, out var count))
        {
            return null;
        }

        try
        {
            var resolved = destination ?? new BoundCall();
            resolved.Set(target, call.ReturnType, call.Receiver, call.ArgumentToParameter, types.AsSpan(0, count), origins: call.Origins, inputOrigins: call.InputOrigins, operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation, defaults: call.DefaultArguments, lengthArguments: lengths.AsSpan(0, count));
            return resolved;
        }
        finally
        {
            this.ReturnFixedArrayArguments(types, lengths);
        }
    }

    // Rents the member's generic arguments for `array`: `E` and `N` at their slots, null elsewhere.
    private bool RentFixedArrayArguments(FunctionKoto witness, BoundType array, out BoundType?[] types, out BoundLength?[] lengths, out int count)
    {
        types = null!;
        lengths = null!;
        count = 0;
        if (!this.FixedArraySlots(witness, out var element, out var length))
        {
            return false;
        }

        count = Math.Max(element, length) + 1;
        types = this.RentTypes(count);
        lengths = this.lengthScratch.Rent(count);
        types[element] = array.Components[0];
        lengths[length] = array.LengthExpression ?? this.InternLength(KotoKind.NumberLiteral, array.Length);
        return true;
    }

    private void ReturnFixedArrayArguments(BoundType?[] types, BoundLength?[] lengths)
    {
        this.lengthScratch.Return(lengths, clearArray: true);
        this.typeScratch.Return(types, clearArray: true);
    }
}
