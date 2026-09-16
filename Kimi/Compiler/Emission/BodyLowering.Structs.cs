// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static bool ValidateReceiverInitialization(OwnershipBody body, OwnershipOperation operation)
        => (operation.Kind == OwnershipOperationKind.CheckReceiverField ? body.Function.IsConstructor : body.Function.IsDestructor) && StructStorage.ReceiverType(body.Function) is { } type &&
            operation.Place >= 0 && body.Places[operation.Place] is { Kind: OwnershipPlaceKind.Local, Source: PropertyKoto field } &&
            ReferenceEquals(field.Parent, StructStorage.Declaration(type)) && (operation.Kind == OwnershipOperationKind.CheckReceiverField || ReferenceEquals(operation.Source, field)) &&
            field.BoundSymbol is { } symbol && body.SymbolPlaces.TryGetValue(symbol, out var place) && place == operation.Place;

    private bool PrepareReceiverFields(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        if (StructStorage.ReceiverType(body.Function) is not { } type)
        {
            return true;
        }

        var layout = this.aggregateLayouts.Get(type);
        if (layout is null || !function.Abi.ResultSlot)
        {
            return Fail("Special receiver requires concrete structure storage and its dedicated address.", out failure);
        }

        for (var i = 0; i < StructStorage.Count(type); i++)
        {
            var field = StructStorage.Field(type, i);
            if (field.BoundSymbol is not { } symbol || !body.SymbolPlaces.TryGetValue(symbol, out var place) ||
                body.Places[place].Kind != OwnershipPlaceKind.Local || !ReferenceEquals(body.Places[place].Source, field) ||
                !ReferenceEquals(body.Places[place].Type, field.BoundType))
            {
                return Fail("Special receiver field has no verified storage Place.", out failure);
            }

            function.SlotAddresses[place] = new(EmissionOperandKind.ProjectedSlot, place);
            function.Subslots.Add(new(place, -1, layout.Offset(i)));
        }

        return true;
    }
}
