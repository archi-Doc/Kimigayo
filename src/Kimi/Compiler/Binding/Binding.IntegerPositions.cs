// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 4.6.2, 8.4.7, 22.1: every integer Type conforms to the closed Contract Position. The conformance is built in: its
// witness is the receiver function `tryResolve<I>` of the internal Kimi group IntegerPosition, instantiated at the
// integer Type, and integers gain no members.
public sealed partial class Binding
{
    /// <summary>Adds the Kimi integer Position witness that a requirement call on an integer dispatches to.</summary>
    /// <param name="destination">The function list to extend without duplicates.</param>
    internal void CollectIntegerPositionWitness(List<FunctionKoto> destination)
    {
        if (this.IntegerPositionWitness()?.Declaration is FunctionKoto function && !destination.Contains(function))
        {
            destination.Add(function);
        }
    }

    private static bool IsIntegerPosition(BoundType? type, BindingSymbol contract)
        => type is { IsInteger: true, Semantics: SemanticsKind.Owner } && contract.LibraryDeclaration == KimiDeclarationId.Position;

    // SPEC 8.4.7: only the Kimi Types that §22.1 names conform to Position and PositionRange; a declaration elsewhere, including
    // one of a user Contract that refines either, is an error rather than a conformance.
    private bool IsClosedContractGrant(BindingSymbol contract, BindingSymbol type)
        => (IsRefinement(contract, this.Library.Position) || IsRefinement(contract, this.Library.PositionRange)) &&
            !ReferenceEquals(type.Declaration.CodeContext.Kotonoha, this.Library.Kotonoha);

    // The witness of the integer conformance: the one generic `tryResolve` of the Kimi group, whose explicit
    // specializations share its Name. Null when the group lacks exactly one.
    private BindingSymbol? IntegerPositionWitness()
    {
        if (this.Library.IntegerPositionMembers is not { } members || !this.scopes.TryGetValue(members, out var scope) ||
            !scope.Values.TryGetValue("tryResolve", out var candidate))
        {
            return null;
        }

        BindingSymbol? witness = null;
        for (var current = candidate; current is not null; current = current.Next)
        {
            if (current.Declaration is FunctionKoto { IsSpecialization: false })
            {
                if (witness is not null)
                {
                    return null;
                }

                witness = current;
            }
        }

        return witness;
    }

    // SPEC 21.3.1: a generic `tryResolve` requirement call on an integer instantiates the witness at that integer Type.
    private CallPlan? InstantiateIntegerPosition(CallPlan call, BoundType self, CallPlan? destination)
    {
        if (this.IntegerPositionWitness() is not { Declaration: FunctionKoto witness } target ||
            !this.scopes.TryGetValue(witness, out var scope) || !scope.Types.TryGetValue("I", out var parameter) || parameter.Slot < 0)
        {
            return null;
        }

        var count = parameter.Slot + 1;
        var types = this.RentTypes(count);
        var lengths = this.lengthScratch.Rent(count);
        try
        {
            // SPEC 8.8.3: the call holds every generic slot, a Type slot with a null length, so that the explicit
            // specializations of the witness (such as `tryResolve<i32>`) are selected.
            types[parameter.Slot] = self;
            var resolved = destination ?? new CallPlan();
            resolved.Set(target, call.ReturnType, call.Receiver, call.ArgumentToParameter, types.AsSpan(0, count), origins: call.Origins, inputOrigins: call.InputOrigins, operations: call.ArgumentOperations, receiverOperation: call.ReceiverOperation, defaults: call.DefaultArguments, lengthArguments: lengths.AsSpan(0, count));
            return resolved;
        }
        finally
        {
            this.lengthScratch.Return(lengths, clearArray: true);
            this.typeScratch.Return(types, clearArray: true);
        }
    }
}
