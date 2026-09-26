// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly AggregateLayoutPool aggregateLayouts = new();
    private readonly Dictionary<BoundType, bool> ownedPatternTypes = new(ReferenceEqualityComparer.Instance);
    private bool formattingRuntimeUsed;

    private AggregateLayout?[] aggregatePlaces = [];
    private int[] payloadOwners = [];
    private int[] constructionOwners = [];
    private int[] aggregateDeclarations = [];
    private int[] payloadPlacements = [];
    private int[] aggregateCompletions = [];
    private int[] decompositionOwners = [];
    private int[] aggregateReadInitializations = [];

    internal AggregateLayoutPool AggregateLayouts => this.aggregateLayouts;

    internal void RegisterAggregates(EmissionModule module)
    {
        foreach (var layout in this.aggregateLayouts.Used)
        {
            if (layout is not null)
            {
                module.Aggregates.Add(layout);
            }
        }

        // Bodies register separately; a helper used by several bodies is defined once per module.
        foreach (var helper in this.arrayHelpers.Values)
        {
            if (!module.ArrayHelpers.Contains(helper))
            {
                module.ArrayHelpers.Add(helper);
            }
        }

        module.NeedsArrayRuntime |= this.arrayRuntimeUsed || this.dictionaryRuntimeUsed || this.arrayHelpers.Count != 0;
        this.arrayRuntimeUsed = false;
        foreach (var helper in this.dictionaryHelpers.Values)
        {
            if (!module.DictionaryHelpers.Contains(helper))
            {
                module.DictionaryHelpers.Add(helper);
            }
        }

        module.NeedsDictionaryRuntime |= this.dictionaryRuntimeUsed;
        this.dictionaryRuntimeUsed = false;
        this.dictionaryHelpers.Clear();
        module.NeedsFormattingRuntime |= this.formattingRuntimeUsed;
        this.formattingRuntimeUsed = false;
        this.arrayHelpers.Clear();
        this.aggregateLayouts.Clear(); // No bound Types survive into the physical module.
        this.ownedPatternTypes.Clear();
    }

    private bool PrepareAggregates(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        this.ownedPatternTypes.Clear();
        // Reuse the signature/parameter resolutions; RegisterAggregates clears bound keys after this body.
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
        Grow(ref this.decompositionOwners, body.Places.Count);
        Grow(ref this.aggregateReadInitializations, body.Places.Count);
        this.aggregateReadInitializations.AsSpan(0, body.Places.Count).Fill(-1);
        this.decompositionOwners.AsSpan(0, body.Places.Count).Fill(-1);
        this.payloadOwners.AsSpan(0, body.Places.Count).Fill(-1);
        this.constructionOwners.AsSpan(0, body.Places.Count).Fill(-1);
        this.aggregateDeclarations.AsSpan(0, body.Places.Count).Fill(-1);
        this.payloadPlacements.AsSpan(0, body.Places.Count).Fill(-1);
        this.aggregateCompletions.AsSpan(0, body.Places.Count).Fill(-1);
        for (var p = 0; p < body.Places.Count; p++)
        {
            var place = body.Places[p];
            if (this.eraseReceiver && ReceiverField(body, p))
            {
                continue;
            }

            if (place.Type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.ResolvedRange or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Function or BoundTypeKind.Closure) && !StructStorage.IsStruct(place.Type) && !EnumStorage.IsEnum(place.Type) && !ObjectTypes.IsOwner(place.Type))
            {
                continue;
            }

            var layout = this.aggregateLayouts.Get(place.Type);
            if (layout is null ||
                (place.Kind == OwnershipPlaceKind.Parameter && this.slotFunctionPlaces[p] != 1) ||
                (place.Kind == OwnershipPlaceKind.Result && this.slotResultPlaces[p] == 0 && this.slotFunctionPlaces[p] != 2))
            {
                return Fail("Aggregate execution requires a finite supported physical layout and verified parameter/result storage.", out failure);
            }

            this.aggregatePlaces[place.Id] = layout;
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            var operation = body.Operations[id];
            if (operation.Kind == OwnershipOperationKind.Produce && (uint)operation.Place < (uint)body.Places.Count &&
                body.Places[operation.Place].Kind == OwnershipPlaceKind.Temporary && this.aggregatePlaces[operation.Place] is not null &&
                (body.Values[id].Kind is OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField ||
                    (body.Values[id].Kind == OwnershipValueKind.Sequence && operation.Source is IndexKoto &&
                        body.Sequences[(int)body.Values[id].Constant].Kind is SequenceOperation.Read or SequenceOperation.ArrayRead) ||
                    (id > 0 && body.Values[id - 1].Kind == OwnershipValueKind.PatternProjection &&
                        body.Operations[id - 1] is { Kind: OwnershipOperationKind.Read } read && read.Input == operation.Place && ReferenceEquals(read.Source, operation.Source))))
            {
                if (this.aggregateReadInitializations[operation.Place] >= 0)
                {
                    return Fail("A complete aggregate read must initialize its own temporary once.", out failure);
                }

                // Each producer is checked by its ordinary lowering path. Retain its
                // initialization so subsequent field access uses the acquired snapshot.
                this.aggregateReadInitializations[operation.Place] = id;
            }
        }

        for (var c = 0; c < body.Constructions.Count; c++)
        {
            var plan = body.Constructions[c];
            if ((uint)plan.Place >= (uint)body.Places.Count || this.aggregatePlaces[plan.Place] is not { } ownerLayout ||
                this.constructionOwners[plan.Place] >= 0 ||
                plan.PayloadStart <= plan.Place || plan.PayloadStart > body.Places.Count - plan.PayloadCount)
            {
                return Fail("Aggregate construction has no matching physical shape.", out failure);
            }

            var type = body.Places[plan.Place].Type;
            var layout = ownerLayout;
            var offset = 0;
            if (plan.Case is { } selected)
            {
                if (ownerLayout.Cases is not { } cases || (uint)selected.Ordinal >= (uint)cases.Length ||
                    !ReferenceEquals(EnumStorage.Case(type, selected.Ordinal), selected) || !ReferenceEquals(body.Places[plan.Place].Source.BoundSymbol?.EnumCase, selected))
                {
                    return Fail("Enum construction has no matching active Case.", out failure);
                }

                layout = cases[selected.Ordinal];
                type = type.StoredCases![selected.Ordinal];
                offset = ownerLayout.PayloadOffset;
            }
            else if (ownerLayout.Cases is not null || body.Places[plan.Place].Source is not (TupleLiteralKoto or ArrayLiteralKoto or DictionaryLiteralKoto { Entries.Count: 0 }))
            {
                return Fail("Aggregate construction has no matching source shape.", out failure);
            }

            this.constructionOwners[plan.Place] = c;
            IReadOnlyList<Koto>? elements = body.Places[plan.Place].Source switch
            {
                TupleLiteralKoto tuple => tuple.Elements,
                ArrayLiteralKoto array => array.Elements,
                InvocationKoto call => call.Arguments,
                _ => null,
            };
            // SPEC 4.3, 4.7.4: an Array literal's payloads keep their own slots; construction moves them into the buffer.
            var handle = type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary;
            var fill = body.Places[plan.Place].Source is ArrayLiteralKoto { FillLength: not null };
            if ((!handle && !fill && layout.Count != plan.PayloadCount) || (fill && plan.PayloadCount != 1) || (elements is not null && elements.Count != plan.PayloadCount))
            {
                return Fail("Aggregate source and payload counts disagree.", out failure);
            }

            for (var i = 0; i < plan.PayloadCount; i++)
            {
                var p = plan.PayloadStart + i;
                if (this.payloadOwners[p] >= 0 || body.Places[p].Kind != OwnershipPlaceKind.Payload ||
                    (elements is not null && !ReferenceEquals(body.Places[p].Source, elements[i])) ||
                    !ReferenceEquals(body.Places[p].Type, type.Components[layout.IsArray || handle ? 0 : i]))
                {
                    return Fail("Aggregate payload ownership or Type does not match its shape.", out failure);
                }

                this.payloadOwners[p] = c;
                if (!handle && !fill && layout.Fields[layout.IsArray ? 0 : i].Layout.Size != 0)
                {
                    function.SlotAddresses[p] = new(EmissionOperandKind.ProjectedSlot, p);
                    function.Subslots.Add(new(p, plan.Place, offset + layout.Offset(i)));
                }
            }
        }

        for (var d = 0; d < body.Decompositions.Count; d++)
        {
            var plan = body.Decompositions[d];
            if ((uint)plan.Place >= (uint)body.Places.Count || this.aggregatePlaces[plan.Place] is not { } owner ||
                (owner.NeedsDestruction && !MatchTypes.SupportsOwnedPatternValue(body.Places[plan.Place].Type, this.ownedPatternTypes)) ||
                plan.PayloadStart <= plan.Place || plan.PayloadStart > body.Places.Count - plan.PayloadCount)
            {
                return Fail("Pattern decomposition requires verified owned storage and cleanup.", out failure);
            }

            var type = body.Places[plan.Place].Type;
            var shape = owner;
            var offset = 0;
            if (plan.Case is { } selected)
            {
                if (owner.Cases is not { } cases || (uint)selected.Ordinal >= (uint)cases.Length || !ReferenceEquals(EnumStorage.Case(type, selected.Ordinal), selected))
                {
                    return Fail("Pattern decomposition has the wrong enum Case.", out failure);
                }

                shape = cases[selected.Ordinal];
                type = type.StoredCases![selected.Ordinal];
                offset = owner.PayloadOffset;
            }
            else if (type.Kind != BoundTypeKind.Tuple)
            {
                return Fail("Pattern decomposition requires a tuple or selected Case.", out failure);
            }

            if (shape.Count != plan.PayloadCount)
            {
                return Fail("Pattern decomposition arity differs from storage.", out failure);
            }

            for (var i = 0; i < plan.PayloadCount; i++)
            {
                var p = plan.PayloadStart + i;
                if (this.payloadOwners[p] >= 0 || this.decompositionOwners[p] >= 0 || body.Places[p].Kind != OwnershipPlaceKind.Payload || !ReferenceEquals(body.Places[p].Type, type.Components[i]))
                {
                    return Fail("Pattern payload has inconsistent ownership or Type.", out failure);
                }

                this.decompositionOwners[p] = d;
                if (shape.Fields[i].Layout.Size != 0)
                {
                    function.SlotAddresses[p] = new(EmissionOperandKind.ProjectedSlot, p);
                    function.Subslots.Add(new(p, plan.Place, offset + shape.Offset(i)));
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
            if (place.Kind == OwnershipPlaceKind.Payload && this.payloadOwners[place.Id] < 0 && this.decompositionOwners[place.Id] < 0)
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
        if (body.Values[id].Kind == OwnershipValueKind.ClosureErasure)
        {
            return this.LowerClosureErasure(body, function, id, out failure);
        }

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
            case OwnershipOperationKind.Read when place.Type.Kind is BoundTypeKind.Function or BoundTypeKind.Closure:
            case OwnershipOperationKind.Read when ObjectTypes.IsOwner(place.Type):
                return !body.IsReachable(id) || (body.GetInputState(id, place.Id) & PlaceState.MustInit) != 0 || Fail("Callable receiver is not initialized.", out failure);
            case OwnershipOperationKind.Read when place.Type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary:
                // SPEC 4.6.1: metadata shares the handle in place; the sequence operation loads its fields.
                return !body.IsReachable(id) || (body.GetInputState(id, place.Id) & PlaceState.MustInit) != 0 || Fail("Array receiver is not initialized.", out failure);
            case OwnershipOperationKind.Declare:
                if (place.Kind == OwnershipPlaceKind.Result && this.slotResultDeclarations[id] == 0)
                {
                    return Fail("Aggregate result declaration has no expression lifetime.", out failure);
                }

                break;
            case OwnershipOperationKind.Produce:
                if (id > 0 && body.Values[id - 1].Kind == OwnershipValueKind.PatternProjection &&
                    body.Operations[id - 1] is { Kind: OwnershipOperationKind.Read } candidate && candidate.Input == place.Id &&
                    ReferenceEquals(candidate.Source, operation.Source) && (!body.IsReachable(id) || this.Dominates(id - 1, id)))
                {
                    break; // The checked guard projection already copied the complete value.
                }

                if (body.Values[id].Kind == OwnershipValueKind.Closure)
                {
                    return this.LowerClosure(body, function, id, out failure);
                }

                if (this.slotFunctionProduces[id] == 0)
                {
                    return Fail("Aggregate receipt has no verified parameter or normal call result.", out failure);
                }

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

                if (plan.Case is { } activeCase)
                {
                    function.AddScalar(EmissionOpcode.StoreScalar, id, [new(EmissionOperandKind.Integer, activeCase.Ordinal)], "i32", place: place.Id, representation: WindowsLowering.GetValue(BoundType.I32));
                }

                if (operation.Source is ArrayLiteralKoto { FillLength: not null } && layout.Value.Layout.Size != 0)
                {
                    // One acquired payload, including N == 0; repeat bytes at runtime, never AST/IR nodes.
                    function.Instructions.Add(new(EmissionOpcode.FillArray, id, place.Id, plan.PayloadStart, Aggregate: layout));
                }

                if (place.Type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
                {
                    // SPEC 4.3, 4.7.4: the handle is zeroed (the empty literal allocates nothing); an element literal reserves its count
                    // once and moves each acquired payload into the buffer in source order.
                    if (!this.TryGetLocation(operation.Source, directory, constants, out var initLocation))
                    {
                        return Fail("Array construction has no diagnostic source location.", out failure);
                    }

                    var dictionary = place.Type.Kind == BoundTypeKind.Dictionary;
                    this.dictionaryRuntimeUsed |= dictionary;
                    function.AddCall(id, dictionary ? WindowsLowering.DictionaryInit : WindowsLowering.ArrayInit, [new(EmissionOperandKind.SlotAddress, place.Id), new(EmissionOperandKind.ConstantAddress, initLocation), new(EmissionOperandKind.ConstantLength, initLocation)]);
                    if (plan.PayloadCount != 0)
                    {
                        if (!this.TryGetArrayElement(place.Type.Components[0], out var element))
                        {
                            return Fail("Array literal has an unsupported element Type.", out failure);
                        }

                        this.arrayRuntimeUsed = true;
                        function.AddCall(id, WindowsLowering.ArrayGrow, [new(EmissionOperandKind.SlotAddress, place.Id), new(EmissionOperandKind.Integer, element.Stride), new(EmissionOperandKind.Integer, plan.PayloadCount), new(EmissionOperandKind.ConstantAddress, initLocation), new(EmissionOperandKind.ConstantLength, initLocation)]);
                        var placeElement = this.GetArrayHelper(ArrayHelperKind.Place, element).Abi;
                        for (var i = 0; i < plan.PayloadCount; i++)
                        {
                            function.AddCall(id, placeElement, [new(EmissionOperandKind.SlotAddress, place.Id), new(EmissionOperandKind.SlotAddress, plan.PayloadStart + i), new(EmissionOperandKind.ConstantAddress, initLocation), new(EmissionOperandKind.ConstantLength, initLocation)]);
                        }
                    }
                }

                break; // Payload slots already occupy their final byte offsets.
            case OwnershipOperationKind.PayloadPlacement:
            case OwnershipOperationKind.Write:
            case OwnershipOperationKind.Consume:
                // A transfer (@move) of a Copy aggregate is the same byte transfer as its Copy.
                var upcast = operation.Kind == OwnershipOperationKind.Consume && operation.Source is ConversionKoto { ConversionBinding: ConversionBinding.ObjectUpcast } conversion &&
                    ObjectTypes.IsOwner(place.Type) && ObjectTypes.IsOwner(conversion.BoundType) &&
                    ReferenceEquals(SignatureType(this, conversion.Left.BoundType), place.Type) &&
                    (uint)operation.Input < (uint)body.Places.Count && ReferenceEquals(SignatureType(this, conversion.BoundType), body.Places[operation.Input].Type) &&
                    ObjectTypes.Supports(place.Type.Components[0], body.Places[operation.Input].Type.Components[0]);
                if ((uint)operation.Input >= (uint)body.Places.Count || operation.Input == place.Id ||
                    (!upcast && !(operation.Kind == OwnershipOperationKind.Consume
                        ? FitsValue(place.Type, body.Places[operation.Input].Type)
                        : FitsValue(body.Places[operation.Input].Type, place.Type))) ||
                    (body.Places[operation.Input].Kind != OwnershipPlaceKind.Temporary && this.slotResultPlaces[operation.Input] == 0 &&
                    !(body.Places[operation.Input].Kind == OwnershipPlaceKind.Result && (IsScalar(place.Type) || ReferenceEquals(place.Type, BoundType.Unit)))) ||
                    (operation.Kind == OwnershipOperationKind.PayloadPlacement && this.payloadOwners[place.Id] < 0) ||
                    (operation.Kind == OwnershipOperationKind.Write && place.Kind != OwnershipPlaceKind.Local && this.slotResultWrites[id] == 0 && this.slotFunctionPlaces[place.Id] != 2) ||
                    (operation.Kind == OwnershipOperationKind.Consume && place.Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter) &&
                    !(upcast && place.Kind == OwnershipPlaceKind.Temporary) && !this.IsPreparedAggregateCopy(body, id)) ||
                    (operation.Kind == OwnershipOperationKind.Consume && operation.Acquisition != place.Acquisition &&
                    !(operation.Acquisition == AcquisitionKind.Move && place.Acquisition == AcquisitionKind.Copy)))
                {
                    return Fail($"Aggregate transfer requires distinct, Type-matched verified storage ({body.Function.Name}, {id}: {operation.Kind}, {place.Id}/{place.Kind}/{place.Type.Name} <- {operation.Input}, result write {this.slotResultWrites[id]}).", out failure);
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

                if (operation.Kind == OwnershipOperationKind.Write && this.slotFunctionPlaces[place.Id] == 2 && body.IsReachable(id) &&
                    (operation.Placement != PlacementKind.Initialization || (body.GetInputState(id, destination) & PlaceState.MayInit) != 0))
                {
                    return Fail("Return storage must be uninitialized before securing its result.", out failure);
                }

                if (operation.Kind == OwnershipOperationKind.Write && place.Kind == OwnershipPlaceKind.Local && !this.LowerStringDestruction(body, function, constants, directory, id, marks, out failure, layout))
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
                        (uint)Input(body, id, 0) >= (uint)id || ValueType(body, Input(body, id, 0)) is not { } inputType || !FitsValue(inputType, place.Type) ||
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
                if (place.Type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary
                    ? !this.LowerArrayDestruction(body, function, constants, directory, id, marks, out failure)
                    : !this.LowerStringDestruction(body, function, constants, directory, id, marks, out failure, layout))
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

    // SPEC 4.7.6: destroying an Array releases its buffer; elements are destroyed first once element storage exists.
    private bool LowerArrayDestruction(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, ReadOnlySpan<byte> marks, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var index = body.OperationSteps[id];
        if ((uint)index >= (uint)body.CleanupSteps.Count || (marks[id] & CleanupMark) == 0)
        {
            return Fail("Array cleanup has no edge cleanup plan.", out failure);
        }

        var step = body.CleanupSteps[index];
        if (step.Operation != id || step.Place != operation.Place)
        {
            return Fail("Array cleanup does not match its plan.", out failure);
        }

        if (step.Action == CleanupAction.Skip)
        {
            return true;
        }

        if (step.Action is not (CleanupAction.Destroy or CleanupAction.Conditional) || !this.TryGetLocation(operation.Source, directory, constants, out var location))
        {
            return Fail("Array cleanup has an unknown action.", out failure);
        }

        return this.LowerArrayRelease(body, function, id, location, step.Action == CleanupAction.Conditional, out failure);
    }

    // Releases the Array in the operation's Place at a cleanup or a whole-value replacement. A possibly
    // initialized Array (for example a short-circuit operand's temporary) is destroyed only when its live flag is set.
    private bool LowerArrayRelease(OwnershipBody body, EmissionFunction function, int id, int location, bool conditional, out string? failure)
    {
        failure = null;
        var place = body.Operations[id].Place;

        // SPEC 4.7.6: elements with cleanup are destroyed in reverse index order before the buffer is released.
        var arrayType = body.Places[place].Type;
        var dictionary = arrayType.Kind == BoundTypeKind.Dictionary;
        ArrayElement element = default;
        if (!dictionary && (arrayType.Kind != BoundTypeKind.Array || !this.TryGetArrayElement(arrayType.Components[0], out element)))
        {
            return Fail("Array destruction has an unsupported element Type.", out failure);
        }

        var iterator = this.arrayIterators[place];
        if (iterator >= 0 && body.IsReachable(id) && !this.Dominates(iterator, id))
        {
            return Fail("Array iterator cleanup requires its initialized cursor.", out failure);
        }

        FunctionAbi callee;
        if (dictionary)
        {
            if (!this.TryGetArrayElement(arrayType.Components[0], out var key, allowEmpty: true) || !this.TryGetArrayElement(arrayType.Components[1], out var item, allowEmpty: true))
            {
                return Fail("Dictionary destruction requires concrete entry storage.", out failure);
            }

            callee = this.GetDictionaryHelper(DictionaryHelperKind.Drop, key, item).Abi;
            this.dictionaryRuntimeUsed = true;
        }
        else
        {
            callee = element.NeedsDestruction ? this.GetArrayHelper(iterator >= 0 ? ArrayHelperKind.IteratorDrop : ArrayHelperKind.Drop, element).Abi : WindowsLowering.ArrayFree;
        }

        if (conditional)
        {
            // An owning iterator always has a dominating unconditional cursor.
            if (iterator >= 0 || this.continuations[id] < 0 || this.liveFlags[place] == 0)
            {
                return Fail("Conditional Array destruction requires a live flag and one split.", out failure);
            }

            var start = function.Operands.Count;
            function.Operands.Add(new(EmissionOperandKind.Block, this.continuations[id]));
            function.Instructions.Add(new(EmissionOpcode.DestroyStringIfLive, id, place, location, callee, start, 1));
            return true;
        }

        function.AddCall(id, callee, [new(EmissionOperandKind.SlotAddress, place), new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
        return true;
    }

    private bool IsPreparedAggregateCopy(OwnershipBody body, int id)
    {
        var operation = body.Operations[id];
        var place = body.Places[operation.Place];
        return place.Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result && operation.Acquisition == AcquisitionKind.Copy &&
            ScalarDefaults.SupportsPatternValue(place.Type) && ReferenceEquals(SignatureType(this, operation.Source.BoundType), place.Type) &&
            operation.Source is IdentifierNameKoto { BoundSymbol: { } symbol } &&
            this.IsPreparedArgument(body, id, symbol, place.Id) && this.IsElementOwnerStorage(place) && this.ValidateElementOwner(body, id);
    }
}
