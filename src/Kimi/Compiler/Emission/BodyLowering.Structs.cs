// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static bool ReceiverField(OwnershipBody body, int place)
        => place >= 0 && (place == body.ReceiverBase || ((uint)place < (uint)body.Places.Count && body.Places[place] is { Kind: OwnershipPlaceKind.Local, Source: PropertyKoto field } &&
            ReferenceEquals(field.Parent, body.Function.BoundSymbol?.Scope.Owner)));

    private static bool ValidateReceiverInitialization(OwnershipBody body, OwnershipOperation operation)
        => (operation.Kind == OwnershipOperationKind.CheckReceiverField ? body.Function.IsConstructor : body.Function.IsDestructor) && AdtDef.ReceiverType(body.Function) is { } type &&
            operation.Place >= 0 && (operation.Place == body.ReceiverBase ? AdtDef.Base(type) is not null && body.Places[operation.Place].Kind == OwnershipPlaceKind.Local :
            body.Places[operation.Place] is { Kind: OwnershipPlaceKind.Local, Source: PropertyKoto field } &&
            ReferenceEquals(field.Parent, AdtDef.Declaration(type)) && (operation.Kind == OwnershipOperationKind.CheckReceiverField || ReferenceEquals(operation.Source, field)) &&
            field.BoundSymbol is { } symbol && body.SymbolPlaces.TryGetValue(symbol, out var place) && place == operation.Place);

    private bool PrepareReceiverFields(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        if (AdtDef.ReceiverType(body.Function) is not { } declared)
        {
            return true;
        }

        // An instance's receiver is the instantiated declaring Type of its call.
        // An unused nongeneric constructor still needs complete base storage; no construction call prepared it.
        var type = this.instance is null ? declared : this.instance.DeclaringType;
        var layout = type is not null && body.Function.CodeContext.Compilation.Binding.PrepareTypeStorage(type) ? this.aggregateLayouts.Get(type) : null;
        if (type is null || layout is null || !function.Abi.ResultSlot)
        {
            return Fail("Special receiver requires concrete structure storage and its dedicated address.", out failure);
        }

        if (body.ReceiverBase >= 0)
        {
            var place = body.ReceiverBase;
            if (AdtDef.Base(type) is not { } parent || layout.Base is null || !ReferenceTypes.StorageMatches(body.Places[place].Type, parent))
            {
                return Fail("Special receiver base requires its complete instantiated prefix layout.", out failure);
            }

            function.SlotAddresses[place] = new(EmissionOperandKind.ProjectedSlot, place);
            function.Subslots.Add(new(place, -1, 0));
        }

        for (var i = 0; i < AdtDef.Count(type); i++)
        {
            var field = AdtDef.Field(type, i);
            if (field.BoundSymbol is not { } symbol || !body.SymbolPlaces.TryGetValue(symbol, out var place) ||
                body.Places[place].Kind != OwnershipPlaceKind.Local || !ReferenceEquals(body.Places[place].Source, field) ||
                !ReferenceEquals(body.Places[place].Type, body.Resolve(field.BoundType, InterpretationContext.Root)))
            {
                return Fail("Special receiver field has no verified storage Place.", out failure);
            }

            function.SlotAddresses[place] = new(EmissionOperandKind.ProjectedSlot, place);
            function.Subslots.Add(new(place, -1, layout.Offset(i)));
        }

        return true;
    }
}
