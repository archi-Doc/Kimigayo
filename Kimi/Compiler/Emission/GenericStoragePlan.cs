// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Closed, syntax-free shared generation records and their planner.

internal enum SharedStorageOperation : byte
{
    Nothing,
    Initialize,
    Declare,
    Transfer,
    Acquire,
    Destroy,
    Deliver,
    Boolean,
    ReadBoolean,
    Length,
    FieldAddress,
    ArrayRead,
    Return,
}

internal readonly record struct SharedStorageInstruction(SharedStorageOperation Kind, int Destination, int Source, int Count, int CopyPlace, int Next, int Alternative, int Condition, int Location, bool Reachable, int DestructionStart = -1, int FieldOffset = -1, int Index = -1);

internal readonly record struct SharedStorageLeaf(int Place, int Parent, int Selector, int Parameter, bool Result, int Policy = -1);

internal sealed record SharedStorageBody(string Name, SharedStorageLeaf[] Leaves, SharedStorageInstruction[] Instructions, int PolicyCount, int ParameterCount, CleanupAction[] Destructions, bool[] LiveFlags);

internal readonly record struct SharedStoragePolicy(int Size, bool Copy, string? Destructor, long Length = 0);

internal sealed record SharedStorageEntry(FunctionAbi Abi, SharedStorageBody Body, int[] Offsets, SharedStoragePolicy[] Policies, int ScratchSize, int ScratchAlignment, ValueLowering[] Parameters, ValueLowering Result);

/// <summary>Builds storage-polymorphic CFGs from universally checked ownership plans. No lookup or body rebinding.</summary>
internal sealed class GenericStoragePlan
{
    private readonly Dictionary<FunctionKoto, Template> templates = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundCall, CallEntry> calls = new(ReferenceEqualityComparer.Instance);
    private readonly SourceLocationTable locations = new();

    internal IReadOnlyDictionary<BoundCall, CallEntry> Calls => this.calls;

    internal static bool IsGeneric(FunctionKoto function)
        => function.GenericArguments.Count != 0 || function.BoundSymbol?.Scope.Owner is StructKoto { GenericArguments.Count: > 0 };

    internal void Clear()
    {
        this.templates.Clear();
        this.calls.Clear();
    }

    internal bool Prepare(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, out string? failure)
    {
        this.Clear();
        failure = null;
        for (var b = 0; b < compilation.Ownership.Bodies.Count; b++)
        {
            var body = compilation.Ownership.Bodies[b];
            if (IsGeneric(body.Function))
            {
                if (!this.PrepareBody(body, module, compilation.Project.Directory, out var template, out failure))
                {
                    return false;
                }

                this.templates.Add(body.Function, template!);
                module.SharedBodies.Add(template!.Physical);
            }
        }

        if (this.templates.Count == 0)
        {
            return true;
        }

        foreach (var body in compilation.Ownership.Bodies)
        {
            foreach (var operation in body.Operations)
            {
                if (operation.Kind != OwnershipOperationKind.Call || operation.Source is not InvocationKoto { BoundCall: { } call } ||
                    call.Target.Declaration is not FunctionKoto target || !this.templates.TryGetValue(target, out var template) || this.calls.ContainsKey(call))
                {
                    continue;
                }

                if (!this.PrepareEntry(compilation, module, layouts, call, template, out var entry, out failure))
                {
                    return false;
                }

                this.calls.Add(call, entry!);
            }
        }

        return true;
    }

    private static bool Fail(string reason, out string? failure)
    {
        failure = reason;
        return false;
    }

    private bool PrepareBody(OwnershipBody body, EmissionModule module, string directory, out Template? template, out string? failure)
    {
        template = null;
        failure = null;
        var function = body.Function;
        if (!body.IsVerified || function.IsAnonymous || function.IsSpecialization || function.IsDestructor || function.AttributeChain is not null ||
            function.Origins.Count != 0 || function.Parameters.Any(x => x.DefaultValue is not null || x.IsOptional))
        {
            return Fail("Generic storage generation requires a verified ordinary definition without captures, defaults or Origins.", out failure);
        }

        if (body.Operations.Count == 0 || body.Operations[0].Kind != OwnershipOperationKind.Entry || body.Places.Count == 0 ||
            body.Places[0].Kind != OwnershipPlaceKind.Result || !ReferenceEquals(body.Places[0].Source, function) ||
            !ReferenceEquals(body.Places[0].Type, function.BoundSymbol?.Type) || body.Values.Count != body.Operations.Count)
        {
            return Fail("Shared body has no verified entry and result contract.", out failure);
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            var op = body.Operations[id];
            if (op.Place < -1 || op.Place >= body.Places.Count || op.Input < -1 || op.Input >= body.Places.Count ||
                op.Projection < -1 || op.Projection >= body.Projections.Count ||
                (op.Place < 0 && op.Kind is not (OwnershipOperationKind.Entry or OwnershipOperationKind.Exit or OwnershipOperationKind.Branch or OwnershipOperationKind.EndComparisonLoans)))
            {
                return Fail("Shared operation refers to invalid storage or projection.", out failure);
            }
        }

        if (!BodyLowering.ValidateSharedValues(body))
        {
            return Fail("Shared body has an inconsistent value-flow plan.", out failure);
        }

        var leaves = new List<SharedStorageLeaf>();
        var types = new List<BoundType>();
        var starts = new int[body.Places.Count];
        var counts = new int[body.Places.Count];
        var parameter = 0;
        for (var p = 0; p < body.Places.Count; p++)
        {
            var place = body.Places[p];
            starts[p] = leaves.Count;
            var argument = place.Kind == OwnershipPlaceKind.Parameter ? parameter++ : -1;
            if (argument >= 0 && (argument >= function.Parameters.Count || !ReferenceEquals(place.Source, function.Parameters[argument].Type) ||
                !ReferenceEquals(place.Type, function.Parameters[argument].Type.BoundType)))
            {
                return Fail("Shared parameter storage differs from its declaration.", out failure);
            }

            var result = p == 0;
            if (function.IsConstructor && place.Source is PropertyKoto field && ReferenceEquals(field.Parent, function.BoundSymbol!.Scope.Owner))
            {
                result = true;
                var receiver = StructStorage.ReceiverType(function)!;
                var selector = 0;
                while (selector < StructStorage.Count(receiver) && !ReferenceEquals(StructStorage.Field(receiver, selector), field))
                {
                    selector++;
                }

                if (!Add(place.Type, p, -2, selector, -1, true, 0))
                {
                    return Fail("Generic receiver field storage cannot be represented.", out failure);
                }
            }
            else if (!Add(place.Type, p, -1, -1, argument, result, 0))
            {
                return Fail("Generic storage requires finite owned fields, scalar values or Type parameters.", out failure);
            }

            counts[p] = leaves.Count - starts[p];
        }

        if (parameter != function.Parameters.Count)
        {
            return Fail("Shared body is missing parameter storage.", out failure);
        }

        var instructions = new SharedStorageInstruction[body.Operations.Count];
        var projections = new List<(int Place, int Selector)>();
        var destructions = new List<CleanupAction>();
        var liveFlags = new bool[leaves.Count];
        for (var id = 0; id < instructions.Length; id++)
        {
            var op = body.Operations[id];
            var value = body.Values[id];
            var kind = SharedStorageOperation.Nothing;
            var dest = op.Place < 0 ? -1 : starts[op.Place];
            var source = op.Input < 0 ? -1 : starts[op.Input];
            var count = op.Place < 0 ? 0 : counts[op.Place];
            var copyPlace = -1;
            var condition = -1;
            var fieldOffset = -1;
            var indexLeaf = -1;
            switch (op.Kind)
            {
                case OwnershipOperationKind.Entry:
                case OwnershipOperationKind.EndComparisonLoans:
                case OwnershipOperationKind.LocateReceiver:
                case OwnershipOperationKind.ProjectElement:
                case OwnershipOperationKind.CheckReceiverField:
                    break;
                case OwnershipOperationKind.Declare:
                    kind = SharedStorageOperation.Declare;
                    break;
                case OwnershipOperationKind.Produce when body.Places[op.Place].Kind == OwnershipPlaceKind.Parameter:
                    kind = SharedStorageOperation.Initialize;
                    break;
                case OwnershipOperationKind.Produce when ReferenceEquals(body.Places[op.Place].Type, BoundType.Unit):
                    kind = SharedStorageOperation.Initialize;
                    break;
                case OwnershipOperationKind.Produce when value.Kind == OwnershipValueKind.Element && op.Projection >= 0:
                    var projection = body.Projections[op.Projection];
                    if (projection.Path != op.Projection || projection.Output != id || projection.Parent >= 0 ||
                        (uint)projection.Root >= (uint)body.Places.Count || (uint)projection.Operation >= (uint)id ||
                        body.Operations[projection.Operation].Source is not MemberAccessKoto fieldSource || !ReferenceEquals(fieldSource, op.Source) ||
                        !StructStorage.IsStruct(body.Places[projection.Root].Type) || !ElementAccess.TryType(fieldSource, out var fieldType, out var selector) ||
                        projection.Selector != selector || !ReferenceEquals(fieldType, body.Places[op.Place].Type) ||
                        op.Acquisition != (body.Places[op.Place].Acquisition == AcquisitionKind.Copy ? AcquisitionKind.None : body.Places[op.Place].Acquisition) ||
                        (body.IsReachable(id) && (body.GetElementState(id, op.Projection) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared field acquisition requires a verified static stored-field path.", out failure);
                    }

                    source = starts[projection.Root];
                    while (source < starts[projection.Root] + counts[projection.Root] && leaves[source].Selector != projection.Selector)
                    {
                        source++;
                    }

                    if (count != 1 || source == starts[projection.Root] + counts[projection.Root] || !ReferenceEquals(types[source], body.Places[op.Place].Type))
                    {
                        return Fail("Shared field acquisition has an unsupported nested storage shape.", out failure);
                    }

                    kind = SharedStorageOperation.Acquire;
                    copyPlace = op.Place;
                    break;
                case OwnershipOperationKind.Produce when value.Kind == OwnershipValueKind.Constant && ReferenceEquals(body.Places[op.Place].Type, BoundType.Boolean):
                    kind = SharedStorageOperation.Boolean;
                    source = value.Constant == 0 ? 0 : 1;
                    break;
                case OwnershipOperationKind.Produce when value.Kind == OwnershipValueKind.Sequence:
                    if (value.Constant >= 0 && value.Constant < body.Sequences.Count && body.Sequences[(int)value.Constant].Kind == SequenceOperation.Read)
                    {
                        var read = body.Sequences[(int)value.Constant];
                        if (read.Operation != id || read.Projection != -1 || (uint)read.Receiver >= (uint)body.Places.Count ||
                            (uint)read.Index >= (uint)id || value.Count != 1 || count != 1 || op.Source is not IndexKoto indexed ||
                            !ReferenceTypes.IsArray(body.Places[read.Receiver].Type) || body.Places[read.Receiver].Type.Semantics != SemanticsKind.Ref ||
                            !ReferenceEquals(body.Places[read.Receiver].Type, indexed.Left.BoundType) ||
                            !ReferenceEquals(body.Places[read.Receiver].Type.Components[0].Components[0], body.Places[op.Place].Type) ||
                            body.Places[op.Place].Acquisition != AcquisitionKind.Copy)
                        {
                            return Fail("Shared array read requires a verified Copy element and borrowed array.", out failure);
                        }

                        var receiverProducer = body.ValueOperands[value.Start];
                        var indexOperation = body.Operations[read.Index];
                        var indexPlace = indexOperation.Kind == OwnershipOperationKind.Consume ? indexOperation.Input : indexOperation.Place;
                        var receiverOperation = (uint)receiverProducer < (uint)id ? body.Operations[receiverProducer] : default;
                        var arrayReceiverPlace = receiverOperation.Kind == OwnershipOperationKind.Consume ? receiverOperation.Input : receiverOperation.Place;
                        if (receiverProducer < 0 || receiverProducer >= id || arrayReceiverPlace != read.Receiver ||
                            !ReferenceEquals(receiverOperation.Source, indexed.Left) ||
                            indexPlace < 0 || !ReferenceEquals(body.Places[indexPlace].Type, BoundType.ISize) ||
                            !ReferenceEquals(indexOperation.Source, indexed.Right) || counts[indexPlace] != 1 ||
                            (body.IsReachable(id) && ((body.GetInputState(id, read.Receiver) & PlaceState.MustInit) == 0 ||
                            (body.GetInputState(id, indexPlace) & PlaceState.MustInit) == 0)))
                        {
                            return Fail("Shared array read lacks checked receiver/index producers.", out failure);
                        }

                        kind = SharedStorageOperation.ArrayRead;
                        source = starts[read.Receiver];
                        copyPlace = read.Receiver;
                        indexLeaf = starts[indexPlace];
                        break;
                    }

                    if (value.Constant < 0 || value.Constant >= body.Sequences.Count || count != 1 ||
                        !ReferenceEquals(body.Places[op.Place].Type, BoundType.ISize))
                    {
                        return Fail("Shared length metadata has no verified sequence plan.", out failure);
                    }

                    var sequence = body.Sequences[(int)value.Constant];
                    if (sequence.Operation != id || sequence.Kind != SequenceOperation.Length || sequence.Projection != -1 || sequence.Index != -1 ||
                        (uint)sequence.Receiver >= (uint)body.Places.Count ||
                        op.Source is not MemberAccessKoto { Right: IdentifierNameKoto { IdentifierName: "length" } } metadata ||
                        metadata.Left.BoundSymbol is not { } receiverSymbol || !body.SymbolPlaces.TryGetValue(receiverSymbol, out var receiverPlace) || receiverPlace != sequence.Receiver ||
                        !ReferenceEquals(metadata.Left.BoundType, body.Places[receiverPlace].Type) ||
                        (body.IsReachable(id) && (body.GetInputState(id, receiverPlace) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared length metadata must inspect its initialized array binding.", out failure);
                    }

                    var receiverType = body.Places[receiverPlace].Type;
                    var borrowed = ReferenceTypes.IsArray(receiverType);
                    if ((!borrowed && receiverType.Kind != BoundTypeKind.FixedArray) || value.Count != (borrowed ? 1 : 0))
                    {
                        return Fail("Shared length metadata has an inconsistent array receiver.", out failure);
                    }

                    if (borrowed)
                    {
                        var producer = body.ValueOperands[value.Start];
                        if ((uint)producer >= (uint)id || body.Operations[producer].Place != receiverPlace ||
                            body.Operations[producer].Kind != OwnershipOperationKind.Read || !ReferenceEquals(body.Operations[producer].Source, metadata.Left))
                        {
                            return Fail("Shared borrowed length requires its retained receiver read.", out failure);
                        }
                    }

                    kind = SharedStorageOperation.Length;
                    copyPlace = receiverPlace; // The array policy also carries logical length, independently of stride.
                    break;
                case OwnershipOperationKind.Borrow when value.Kind == OwnershipValueKind.Address && op.Source is MemberAccessKoto member:
                    var receiver = body.Places[op.Place].Type;
                    var output = op.Input < 0 ? null : body.Places[op.Input].Type;
                    if (!ReferenceTypes.IsStruct(receiver) || receiver.Semantics != SemanticsKind.Ref ||
                        !ReferenceTypes.IsStorage(output) || output!.Semantics != SemanticsKind.Ref ||
                        !ReferenceEquals(member.Left.BoundType, receiver) || !ReferenceEquals(member.BoundType, output.Components[0]) ||
                        value.Count != 1 || value.Constant != op.Place || count != 1 || counts[op.Input] != 1 || op.LoanMode != LoanRequirement.Ref ||
                        (body.IsReachable(id) && (body.GetInputState(id, op.Place) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared projected borrow requires a checked shared receiver and reference result.", out failure);
                    }

                    var fieldProducer = body.ValueOperands[value.Start];
                    if ((uint)fieldProducer >= (uint)id || body.Operations[fieldProducer].Kind != OwnershipOperationKind.Read ||
                        body.Operations[fieldProducer].Place != op.Place || !ReferenceEquals(body.Operations[fieldProducer].Source, member.Left))
                    {
                        return Fail("Shared projected borrow has no retained receiver read.", out failure);
                    }

                    var fieldIndex = -1;
                    for (var f = 0; f < StructStorage.Count(receiver.Components[0]); f++)
                    {
                        if (ReferenceEquals(StructStorage.Field(receiver.Components[0], f).BoundSymbol, member.BoundSymbol))
                        {
                            fieldIndex = f;
                            break;
                        }
                    }

                    if (fieldIndex < 0 || !ReferenceEquals(StructStorage.FieldType(receiver.Components[0], fieldIndex), member.BoundType))
                    {
                        return Fail("Shared projected borrow has no matching stored field.", out failure);
                    }

                    fieldOffset = leaves.Count + projections.Count;
                    projections.Add((op.Place, fieldIndex));
                    (dest, source) = (source, dest);
                    kind = SharedStorageOperation.FieldAddress;
                    break;
                case OwnershipOperationKind.Read when ReferenceTypes.IsStorage(body.Places[op.Place].Type):
                    break;
                case OwnershipOperationKind.Read when ReferenceEquals(body.Places[op.Place].Type, BoundType.Boolean):
                    kind = SharedStorageOperation.ReadBoolean;
                    break;
                case OwnershipOperationKind.Consume:
                    if (op.Input < 0 || !ReferenceEquals(body.Places[op.Place].Type, body.Places[op.Input].Type) ||
                        op.Acquisition != body.Places[op.Place].Acquisition ||
                        (body.IsReachable(id) && (body.GetInputState(id, op.Place) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared acquisition does not match its verified ownership Type/effect.", out failure);
                    }

                    kind = SharedStorageOperation.Acquire;
                    (dest, source) = (source, dest);
                    copyPlace = op.Place;
                    break;
                case OwnershipOperationKind.Write:
                    if (count != 0 && (op.Input < 0 || !ReferenceEquals(body.Places[op.Place].Type, body.Places[op.Input].Type) ||
                        (body.IsReachable(id) && (body.GetInputState(id, op.Input) & PlaceState.MustInit) == 0)))
                    {
                        return Fail("Shared placement requires matching complete Types.", out failure);
                    }

                    kind = SharedStorageOperation.Transfer;
                    break;
                case OwnershipOperationKind.Cleanup:
                    if (!body.CleanupSteps.Any(x => x.Operation == id && x.Place == op.Place && x.Action != CleanupAction.Unsupported))
                    {
                        return Fail("Shared cleanup is missing its verified destruction step.", out failure);
                    }

                    kind = SharedStorageOperation.Destroy;
                    break;
                case OwnershipOperationKind.Deliver:
                    if (op.Place != 0 || !body.Deliveries.Any(x => x.Operation == id) ||
                        (body.IsReachable(id) && (body.GetInputState(id, 0) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared delivery has no initialized secured result.", out failure);
                    }

                    kind = SharedStorageOperation.Deliver;
                    break;
                case OwnershipOperationKind.Branch:
                    if (value.Kind == OwnershipValueKind.Alias && value.Count == 1)
                    {
                        condition = BooleanPlace(body.ValueOperands[value.Start]);
                        if (condition < 0)
                        {
                            return Fail("Shared branch requires an available boolean value.", out failure);
                        }
                    }

                    break;
                case OwnershipOperationKind.Exit:
                    kind = SharedStorageOperation.Return;
                    break;
                default:
                    return Fail("Generic storage CFG contains an operation without a shared lowering: " + op.Kind + ".", out failure);
            }

            var next = -1;
            var alternative = -1;
            for (var edgeId = body.EdgeHeads[id]; edgeId >= 0; edgeId = body.Edges[edgeId].Next)
            {
                var edge = body.Edges[edgeId];
                if (edge.Kind == OwnershipEdgeKind.False)
                {
                    alternative = edge.To;
                }
                else if (next < 0)
                {
                    next = edge.To;
                }
                else
                {
                    return Fail("Shared CFG has unsupported outgoing edges.", out failure);
                }
            }

            if ((alternative >= 0 && condition < 0) || !this.locations.TryGet(op.Source, directory, out var location))
            {
                return Fail("Shared operation lacks its condition or diagnostic location.", out failure);
            }

            var destructionStart = -1;
            if (kind is SharedStorageOperation.Destroy or SharedStorageOperation.Transfer)
            {
                destructionStart = destructions.Count;
                for (var k = 0; k < count; k++)
                {
                    var leaf = dest + k;
                    var state = body.GetStorageState(id, op.Place);
                    if (leaves[leaf].Parent >= 0 && body.MoveRoot(op.Place) is >= 0 and var root)
                    {
                        var path = body.GetMovePath(root).Child;
                        while (path >= 0 && body.GetMovePath(path).Selector != leaves[leaf].Selector)
                        {
                            path = body.GetMovePath(path).Next;
                        }

                        body.LoadPathInput(id);
                        var fieldState = path >= 0 ? body.CurrentPathState(path) : body.CurrentRemainderState(root);
                        state &= fieldState; // Both the containing storage and this field must still be live.
                    }

                    var action = ReferenceEquals(types[leaf], BoundType.Boolean) || !body.IsReachable(id) ? CleanupAction.Skip :
                        (state & PlaceState.MustInit) != 0 ? CleanupAction.Destroy : (state & PlaceState.MayInit) != 0 ? CleanupAction.Conditional : CleanupAction.Skip;
                    destructions.Add(action);
                    liveFlags[leaf] |= action == CleanupAction.Conditional;
                }
            }

            instructions[id] = new(kind, dest, source, count, copyPlace, next, alternative, condition, module.Constants.Intern(location, LlvmConstantKind.Location), body.IsReachable(id), destructionStart, fieldOffset, indexLeaf);
        }

        // Canonical definition-side requirements: equal complete symbolic Types share
        // one size/acquisition/destruction policy, independently of source occurrences.
        var policies = new List<BoundType>();
        foreach (var type in types)
        {
            if (!policies.Contains(type))
            {
                policies.Add(type);
            }
        }

        foreach (var instruction in instructions)
        {
            if (instruction.CopyPlace >= 0 && !policies.Contains(body.Places[instruction.CopyPlace].Type))
            {
                policies.Add(body.Places[instruction.CopyPlace].Type);
            }
        }

        policies.Sort((left, right) => StringComparer.Ordinal.Compare(TypeKey(left), TypeKey(right)));
        for (var i = 0; i < leaves.Count; i++)
        {
            leaves[i] = leaves[i] with { Policy = policies.IndexOf(types[i]) };
        }

        for (var i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].CopyPlace >= 0)
            {
                instructions[i] = instructions[i] with { CopyPlace = policies.IndexOf(body.Places[instructions[i].CopyPlace].Type) };
            }
        }

        var physical = new SharedStorageBody("__kimi_shared" + module.SharedBodies.Count, leaves.ToArray(), instructions, policies.Count, function.Parameters.Count, destructions.ToArray(), liveFlags);
        template = new(body, physical, policies.ToArray(), projections.ToArray());
        return true;

        int BooleanPlace(int id)
        {
            if ((uint)id >= (uint)body.Operations.Count)
            {
                return -1;
            }

            var op = body.Operations[id];
            var p = op.Kind == OwnershipOperationKind.Consume ? op.Input : op.Place;
            return p >= 0 && ReferenceEquals(body.Places[p].Type, BoundType.Boolean) && counts[p] == 1 ? starts[p] : -1;
        }

        bool Add(BoundType type, int place, int parent, int selector, int argument, bool result, int depth)
        {
            var borrowedStorage = ReferenceTypes.IsStorage(type) && type.Semantics == SemanticsKind.Ref &&
                type.Origin is { Kind: OriginKind.Input } origin && ReferenceEquals(origin.Binder, function);
            if (depth > 32 || (type.Origin is not null && !borrowedStorage) || type.OriginArguments.Count != 0)
            {
                return false;
            }

            if (StructStorage.IsStruct(type))
            {
                if (StructStorage.Destructor(type) is not null)
                {
                    return false;
                }

                for (var f = 0; f < StructStorage.Count(type); f++)
                {
                    // Nested symbolic structures need a path schema before admission.
                    if (parent != -1 || !Add(StructStorage.FieldType(type, f)!, place, place, f, argument, result, depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (ReferenceEquals(type, BoundType.Unit))
            {
                return true;
            }

            if (type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.FixedArray) && !ReferenceEquals(type, BoundType.Boolean) && !ReferenceEquals(type, BoundType.ISize) && !borrowedStorage)
            {
                return false;
            }

            leaves.Add(new(place, parent, selector, argument, result));
            types.Add(type);
            return true;
        }

        string TypeKey(BoundType type)
        {
            if (type.Kind == BoundTypeKind.Parameter)
            {
                return (ReferenceEquals(type.Symbol!.Scope.Owner, function) ? "function:" : "container:") + type.Symbol.Slot;
            }

            return type.Kind + ":" + type.Name + ":" + (type.LengthExpression is { } length ? LengthKey(length) : type.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "<" + string.Join(",", type.Components.Select(TypeKey)) + ">";
        }

        string LengthKey(BoundLength length) => length.Parameter is { } symbol ? "slot:" + symbol.Slot : length.IsConstant
            ? length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : length.Operation + "(" + LengthKey(length.Left!) + "," + (length.Right is null ? string.Empty : LengthKey(length.Right)) + ")";
    }

    private bool PrepareEntry(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall call, Template template, out CallEntry? entry, out string? failure)
    {
        entry = null;
        failure = null;
        var body = template.Body;
        var target = body.Function;
        var binding = compilation.Binding;
        if (call.Origins.Length != 0 || call.DefaultArguments.Length != 0)
        {
            return Fail("Shared entries do not yet lower Origin or default substitution.", out failure);
        }

        var parameters = new BoundType[target.Parameters.Count];
        var values = new ValueLowering[parameters.Length];
        var abiParameters = new List<AbiParameter>();
        var result = call.ReturnType;
        var resultValue = FunctionAbi.GetValue(result, layouts);
        if (resultValue is null)
        {
            return Fail("Shared entry result has no concrete representation.", out failure);
        }

        var resultSlot = target.IsConstructor || FunctionAbi.HasResultSlot(result, layouts);
        if (resultSlot)
        {
            abiParameters.Add(new("ptr", "ret", AbiParameterKind.ResultSlot));
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var type = binding.InstantiateStorageType(target.Parameters[i].Type.BoundType!, call);
            var value = type is null ? null : FunctionAbi.GetValue(type, layouts);
            var borrowedStorage = ReferenceTypes.IsStorage(type) && type!.Semantics == SemanticsKind.Ref && ReferenceTypes.IsStorage(target.Parameters[i].Type.BoundType);
            if (type is null || value is null || ReferenceTypes.IsString(type) || (ReferenceTypes.IsStorage(type) && !borrowedStorage) ||
                (borrowedStorage && layouts.Get(type.Components[0]) is null))
            {
                return Fail("Shared entry parameter requires a concrete owned storage representation.", out failure);
            }

            parameters[i] = type;
            values[i] = value;
            if (value.Layout.Size != 0)
            {
                abiParameters.Add(new(value.ArgumentType!, "a" + i, SlotTypes.IsResult(type) ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value, i));
            }
        }

        foreach (var existing in this.calls.Values)
        {
            if (ReferenceEquals(existing.Template, template) && ReferenceEquals(existing.Result, result) &&
                existing.Parameters.AsSpan().SequenceEqual(parameters) && ReferenceEquals(existing.DeclaringType, call.DeclaringType) && existing.Arguments.AsSpan().SequenceEqual(call.TypeArguments) && existing.Lengths.AsSpan().SequenceEqual(call.LengthArguments))
            {
                entry = existing;
                return true;
            }
        }

        var offsets = new int[template.Physical.Leaves.Length + template.Projections.Length];
        var policies = new SharedStoragePolicy[template.Types.Length];
        var placeOffsets = new int[body.Places.Count];
        var scratchPlaces = new bool[body.Places.Count];
        foreach (var leaf in template.Physical.Leaves)
        {
            scratchPlaces[leaf.Place] |= !leaf.Result && leaf.Parameter < 0;
        }

        var size = 0;
        var alignment = 1;
        var resolved = new BoundType[body.Places.Count];
        for (var p = 0; p < resolved.Length; p++)
        {
            var type = binding.InstantiateStorageType(body.Places[p].Type, call);
            var value = type is null ? null : FunctionAbi.GetValue(type, layouts);
            if (type is null || value is null)
            {
                return Fail("Shared storage substitution must resolve representation and exact Copy/Move effects.", out failure);
            }

            resolved[p] = type;
            // Parameters and the function result use entry arguments/result storage;
            // constructor fields address that same result. Reserve only body scratch.
            if (!scratchPlaces[p] || value.Layout.Size == 0)
            {
                continue;
            }

            alignment = Math.Max(alignment, value.Layout.Alignment);
            var aligned = ((long)size + value.Layout.Alignment - 1) & -(long)value.Layout.Alignment;
            if (aligned + value.Layout.Size > int.MaxValue)
            {
                return Fail("Shared entry exceeds the supported fixed-frame size.", out failure);
            }

            size = (int)aligned;
            placeOffsets[p] = size;
            size += value.Layout.Size;
        }

        var capacity = ((long)size + alignment - 1) & -(long)alignment;
        if (capacity > int.MaxValue || alignment > 16)
        {
            return Fail("Shared scratch exceeds the supported capacity or alignment.", out failure);
        }

        size = (int)capacity;

        for (var i = 0; i < template.Physical.Leaves.Length; i++)
        {
            var leaf = template.Physical.Leaves[i];
            var offset = 0;
            if (leaf.Parent >= 0 || leaf.Parent == -2)
            {
                var owner = leaf.Parent == -2 ? call.DeclaringType! : resolved[leaf.Parent];
                if (layouts.Get(owner) is not { } ownerLayout)
                {
                    return Fail("Shared field has no instantiated owner layout.", out failure);
                }

                offset = ownerLayout.Offset(leaf.Selector);
            }

            if (!leaf.Result && leaf.Parameter < 0)
            {
                offset = checked(offset + placeOffsets[leaf.Place]);
            }

            offsets[i] = offset;
        }

        for (var i = 0; i < template.Projections.Length; i++)
        {
            var projection = template.Projections[i];
            if (!ReferenceTypes.IsStruct(resolved[projection.Place]) || layouts.Get(resolved[projection.Place].Components[0]) is not { } ownerLayout)
            {
                return Fail("Shared projected borrow has no instantiated receiver layout.", out failure);
            }

            offsets[template.Physical.Leaves.Length + i] = ownerLayout.Offset(projection.Selector);
        }

        for (var i = 0; i < policies.Length; i++)
        {
            var type = binding.InstantiateStorageType(template.Types[i], call);
            var value = type is null ? null : FunctionAbi.GetValue(type, layouts);
            var proof = type is null ? ConstraintProof.Unknown : binding.ProveCopy(type, target);
            if (type is null || value is null || value.Layout.Stride != value.Layout.Size || proof is not (ConstraintProof.Proven or ConstraintProof.Refuted))
            {
                return Fail("Shared policy requires concrete layout and proved acquisition effects.", out failure);
            }

            var aggregate = layouts.Get(type);
            var destroy = ReferenceEquals(type, BoundType.String) ? "__kimi_destroy_string" :
                aggregate?.NeedsDestruction == true ? "__kimi_drop_aggregate" + aggregate.Id : null;
            var array = ReferenceTypes.IsArray(type) ? type.Components[0] : type;
            policies[i] = new(value.Layout.Size, proof == ConstraintProof.Proven, destroy, array.Kind == BoundTypeKind.FixedArray ? array.Length : 0);
        }

        var abi = new FunctionAbi("__kimi_generic_entry" + module.SharedEntries.Count, FunctionAbi.ResultType(result, layouts)!, abiParameters.ToArray(), resultSlot: resultSlot);
        var generated = new SharedStorageEntry(abi, template.Physical, offsets, policies, size, alignment, values, resultValue);
        module.SharedEntries.Add(generated);
        entry = new(template, generated, parameters, result, call.DeclaringType, call.TypeArguments.ToArray(), call.LengthArguments.ToArray());
        return true;
    }

    internal sealed record Template(OwnershipBody Body, SharedStorageBody Physical, BoundType[] Types, (int Place, int Selector)[] Projections);

    internal sealed record CallEntry(Template Template, SharedStorageEntry Physical, BoundType[] Parameters, BoundType Result, BoundType? DeclaringType, BoundType?[] Arguments, BoundLength?[] Lengths);
}
