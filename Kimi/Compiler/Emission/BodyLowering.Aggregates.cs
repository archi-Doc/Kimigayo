// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly AggregateLayoutPool aggregateLayouts = new();
    private AggregateLayout?[] aggregatePlaces = [];
    private int[] payloadOwners = [];
    private int[] constructionOwners = [];
    private int[] aggregateDeclarations = [];
    private int[] payloadPlacements = [];
    private int[] aggregateCompletions = [];

    internal void RegisterAggregates(EmissionModule module)
    {
        foreach (var layout in this.aggregateLayouts.Used)
        {
            if (layout is not null)
            {
                module.Aggregates.Add(layout);
            }
        }

        this.aggregateLayouts.Clear(); // No bound Types survive into the physical module.
    }

    private bool PrepareAggregates(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        this.aggregateLayouts.Clear();
        if (this.aggregatePlaces.Length < body.Places.Count)
        {
            Array.Resize(ref this.aggregatePlaces, Math.Max(body.Places.Count, this.aggregatePlaces.Length * 2));
        }

        this.aggregatePlaces.AsSpan().Clear();
        Grow(ref this.payloadOwners, body.Places.Count);
        Grow(ref this.constructionOwners, body.Places.Count);
        Grow(ref this.aggregateDeclarations, body.Places.Count);
        Grow(ref this.payloadPlacements, body.Places.Count);
        Grow(ref this.aggregateCompletions, body.Places.Count);
        this.payloadOwners.AsSpan(0, body.Places.Count).Fill(-1);
        this.constructionOwners.AsSpan(0, body.Places.Count).Fill(-1);
        this.aggregateDeclarations.AsSpan(0, body.Places.Count).Fill(-1);
        this.payloadPlacements.AsSpan(0, body.Places.Count).Fill(-1);
        this.aggregateCompletions.AsSpan(0, body.Places.Count).Fill(-1);
        for (var p = 0; p < body.Places.Count; p++)
        {
            var place = body.Places[p];
            if (place.Type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray))
            {
                continue;
            }

            var layout = this.aggregateLayouts.Get(place.Type);
            if (layout is null || place.Kind is OwnershipPlaceKind.Parameter or OwnershipPlaceKind.Result or OwnershipPlaceKind.Subject)
            {
                return Fail("Aggregate execution requires an owned tuple/fixed array of supported values, at most 64 nesting levels and 2147483647 layout bytes; aggregate signatures and selection results are not implemented.", out failure);
            }

            this.aggregatePlaces[place.Id] = layout;
        }

        for (var c = 0; c < body.Constructions.Count; c++)
        {
            var plan = body.Constructions[c];
            if ((uint)plan.Place >= (uint)body.Places.Count || plan.Case is not null || this.aggregatePlaces[plan.Place] is not { } layout ||
                this.constructionOwners[plan.Place] >= 0 ||
                plan.PayloadCount != layout.Count || plan.PayloadStart <= plan.Place || plan.PayloadStart > body.Places.Count - plan.PayloadCount ||
                body.Places[plan.Place].Source is not (TupleLiteralKoto or ArrayLiteralKoto))
            {
                return Fail("Aggregate construction has no matching physical shape.", out failure);
            }

            var type = body.Places[plan.Place].Type;
            this.constructionOwners[plan.Place] = c;
            var elements = body.Places[plan.Place].Source is TupleLiteralKoto tuple ? tuple.Elements : ((ArrayLiteralKoto)body.Places[plan.Place].Source).Elements;
            if (elements.Count != plan.PayloadCount)
            {
                return Fail("Aggregate source and payload counts disagree.", out failure);
            }

            for (var i = 0; i < plan.PayloadCount; i++)
            {
                var p = plan.PayloadStart + i;
                if (this.payloadOwners[p] >= 0 || body.Places[p].Kind != OwnershipPlaceKind.Payload ||
                    !ReferenceEquals(body.Places[p].Source, elements[i]) ||
                    !ReferenceEquals(body.Places[p].Type, type.Components[layout.IsArray ? 0 : i]))
                {
                    return Fail("Aggregate payload ownership or Type does not match its shape.", out failure);
                }

                this.payloadOwners[p] = c;
                if (layout.Fields[layout.IsArray ? 0 : i].Layout.Size != 0)
                {
                    function.SlotAddresses[p] = new(EmissionOperandKind.ProjectedSlot, p);
                    function.Subslots.Add(new(p, plan.Place, layout.Offset(i)));
                }
            }
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            var operation = body.Operations[id];
            var p = operation.Place;
            if (p < 0 || (this.payloadOwners[p] < 0 && this.constructionOwners[p] < 0))
            {
                continue;
            }

            if (operation.Kind == OwnershipOperationKind.Declare)
            {
                if (this.aggregateDeclarations[p] >= 0)
                {
                    return Fail("Aggregate storage has duplicate lifetime declarations.", out failure);
                }

                this.aggregateDeclarations[p] = id;
            }
            else if (operation.Kind == OwnershipOperationKind.PayloadPlacement && this.payloadOwners[p] >= 0)
            {
                if (this.payloadPlacements[p] >= 0)
                {
                    return Fail("Aggregate payload has duplicate placements.", out failure);
                }

                this.payloadPlacements[p] = id;
            }
            else if (operation.Kind == OwnershipOperationKind.CompleteConstruction && this.constructionOwners[p] >= 0)
            {
                if (this.aggregateCompletions[p] >= 0 || body.OperationSteps[id] != this.constructionOwners[p])
                {
                    return Fail("Aggregate completion has a duplicate or mismatched owner.", out failure);
                }

                this.aggregateCompletions[p] = id;
            }
            else if (this.payloadOwners[p] >= 0 && operation.Kind != OwnershipOperationKind.Cleanup)
            {
                return Fail("Payload storage permits only declaration, placement and cleanup.", out failure);
            }
        }

        for (var p = 0; p < body.Places.Count; p++)
        {
            var place = body.Places[p];
            if (place.Kind == OwnershipPlaceKind.Payload && this.payloadOwners[place.Id] < 0)
            {
                return Fail("Payload storage has no unique construction owner.", out failure);
            }

            if ((this.payloadOwners[p] >= 0 || this.constructionOwners[p] >= 0) && this.aggregateDeclarations[p] < 0)
            {
                return Fail("Aggregate storage has no lifetime declaration.", out failure);
            }
        }

        return true;
    }

    private bool ValidateAggregateDominance(OwnershipBody body, out string? failure)
    {
        failure = null;
        for (var p = 0; p < body.Places.Count; p++)
        {
            if (this.payloadOwners[p] < 0)
            {
                continue;
            }

            var parent = body.Constructions[this.payloadOwners[p]].Place;
            var placement = this.payloadPlacements[p];
            var complete = this.aggregateCompletions[parent];
            if ((placement >= 0 && body.IsReachable(placement) && (!this.Dominates(this.aggregateDeclarations[p], placement) || !this.Dominates(this.aggregateDeclarations[parent], placement))) ||
                (complete >= 0 && (placement < 0 || placement >= complete || (body.IsReachable(complete) && !this.Dominates(placement, complete)))))
            {
                return Fail("Aggregate placement must follow its declaration and dominate completion.", out failure);
            }
        }

        return true;
    }

    private bool LowerAggregate(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, ReadOnlySpan<byte> marks, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var place = body.Places[operation.Place];
        var layout = this.aggregatePlaces[place.Id];
        if (operation.Source.AttributeChain is not null)
        {
            return Fail("Aggregate operation attributes are not implemented.", out failure);
        }

        switch (operation.Kind)
        {
            case OwnershipOperationKind.Declare:
                break;
            case OwnershipOperationKind.CompleteConstruction:
                var planIndex = body.OperationSteps[id];
                if ((uint)planIndex >= (uint)body.Constructions.Count || body.Constructions[planIndex].Place != place.Id || layout is null)
                {
                    return Fail("Aggregate completion has no matching construction plan.", out failure);
                }

                var plan = body.Constructions[planIndex];
                if (!ReferenceEquals(operation.Source, place.Source))
                {
                    return Fail("Aggregate completion has the wrong source construction.", out failure);
                }

                if (body.IsReachable(id))
                {
                    if (!this.Dominates(this.aggregateDeclarations[place.Id], id) || (body.GetInputState(id, place.Id) & PlaceState.MayInit) != 0)
                    {
                        return Fail("Aggregate completion would overwrite a live value.", out failure);
                    }

                    for (var i = 0; i < plan.PayloadCount; i++)
                    {
                        if ((body.GetInputState(id, plan.PayloadStart + i) & PlaceState.MustInit) == 0)
                        {
                            return Fail("Aggregate completion requires every payload initialized.", out failure);
                        }
                    }
                }

                break; // Payload slots already occupy their final byte offsets.
            case OwnershipOperationKind.PayloadPlacement:
            case OwnershipOperationKind.Write:
            case OwnershipOperationKind.Consume:
                if ((uint)operation.Input >= (uint)body.Places.Count || operation.Input == place.Id || !ReferenceEquals(place.Type, body.Places[operation.Input].Type) ||
                    (body.Places[operation.Input].Kind != OwnershipPlaceKind.Temporary && !(ReferenceEquals(place.Type, BoundType.String) && this.IsStringValue(body.Places[operation.Input]))) ||
                    (operation.Kind == OwnershipOperationKind.PayloadPlacement && this.payloadOwners[place.Id] < 0) ||
                    (operation.Kind is OwnershipOperationKind.Write or OwnershipOperationKind.Consume && place.Kind != OwnershipPlaceKind.Local) ||
                    (operation.Kind == OwnershipOperationKind.Consume && operation.Acquisition != place.Acquisition))
                {
                    return Fail("Aggregate transfer requires distinct, Type-matched verified storage.", out failure);
                }

                var source = operation.Kind == OwnershipOperationKind.Consume ? place.Id : operation.Input;
                var destination = operation.Kind == OwnershipOperationKind.Consume ? operation.Input : place.Id;
                if (this.constructionOwners[source] >= 0 && (this.aggregateCompletions[source] < 0 ||
                    (body.IsReachable(id) && !this.Dominates(this.aggregateCompletions[source], id))))
                {
                    return Fail("Aggregate acquisition requires a dominating completed construction.", out failure);
                }

                if (body.IsReachable(id) && ((body.GetInputState(id, source) & PlaceState.MustInit) == 0 ||
                    (operation.Kind != OwnershipOperationKind.Write && (body.GetInputState(id, destination) & PlaceState.MayInit) != 0)))
                {
                    return Fail("Aggregate transfer requires an initialized source and fresh destination.", out failure);
                }

                if (operation.Kind == OwnershipOperationKind.Write && !this.LowerStringDestruction(body, function, constants, directory, id, marks, out failure, layout))
                {
                    return false;
                }

                if (layout is not null)
                {
                    if (layout.Value.Layout.Size != 0)
                    {
                        function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, destination, source, Aggregate: layout));
                    }
                }
                else if (ReferenceEquals(place.Type, BoundType.String))
                {
                    function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.SlotAddress, source)], place: destination);
                }
                else if (IsScalar(place.Type))
                {
                    if (body.Values[id].Kind != OwnershipValueKind.Alias || body.Values[id].Count != 1 ||
                        (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
                    {
                        return Fail("Scalar payload placement has no dominating value.", out failure);
                    }

                    var representation = WindowsLowering.GetValue(place.Type)!;
                    function.AddScalar(EmissionOpcode.StoreScalar, id, [this.PhysicalOperand(body, Input(body, id, 0))], representation.ComputationType, place: destination, representation: representation);
                }
                else if (!ReferenceEquals(place.Type, BoundType.Unit))
                {
                    return Fail("Unsupported aggregate payload representation.", out failure);
                }

                break;
            case OwnershipOperationKind.Cleanup:
                if (!this.LowerStringDestruction(body, function, constants, directory, id, marks, out failure, layout))
                {
                    return false;
                }

                break;
            default:
                return Fail("Aggregate operation requires an explicit execution plan.", out failure);
        }

        this.AddStringFlags(function, operation, id);
        return true;
    }
}
