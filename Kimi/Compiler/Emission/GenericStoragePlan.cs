// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Closed, syntax-free shared generation records and their planner.

internal enum SharedStorageOperation : byte
{
    Nothing,
    Initialize,
    StringLiteral,
    WriteLine,
    AbortMessage,
    Declare,
    Transfer,
    Acquire,
    Destroy,
    Deliver,
    Boolean,
    ReadBoolean,
    Length,
    FieldAddress,
    FieldRead,
    FieldWrite,
    SliceAddress,
    ArrayAddress,
    ArrayRead,
    ConstructEnum,
    Indices,
    RangePart,
    Call,
    DirectCall,
    StorageAddress,
    Reborrow,
    CaseTest,
    DecomposeEnum,
    Return,
}

internal readonly record struct SharedScalarValue(string Type, OwnershipValueKind Kind, int First, int Second, long Constant, string? Operator, bool Store, int CountWidth = 0);

internal readonly record struct SharedPhiInput(int Value, int Predecessor);

internal readonly record struct SharedConversion(ValueLowering Source, BodyLowering.ConversionPlan Plan);

internal readonly record struct SharedStorageInstruction(SharedStorageOperation Kind, int Destination, int Source, int Count, int CopyPlace, int Next, int Alternative, int Condition, int Location, bool Reachable, int DestructionStart = -1, int FieldOffset = -1, int Index = -1, SharedScalarValue? Scalar = null);

internal readonly record struct SharedStorageLeaf(int Place, int Parent, int Selector, int Parameter, bool Result, int Policy = -1);

internal readonly record struct SharedEnumConstruction(int Tag, int[] Sources, int[] Offsets);

internal readonly record struct SharedValueCall(int Receiver, int[] Arguments);

internal sealed record SharedCallAdapter(ValueLowering[] Parameters, ValueLowering Result, FunctionAbi? Entry = null, bool Borrowed = false);

internal sealed record SharedDirectAdapter(FunctionAbi Abi, ValueLowering[] Parameters, ValueLowering? Result);

internal sealed record SharedStorageBody(string Name, SharedStorageLeaf[] Leaves, SharedStorageInstruction[] Instructions, int PolicyCount, int ParameterCount, CleanupAction[] Destructions, bool[] LiveFlags, SharedEnumConstruction[] Constructions, SharedValueCall[] Calls, int[][] DirectArguments, SharedStorageLeaf[] Addresses, int AddressOffset, SharedPhiInput[] PhiInputs, bool NoReturn, SharedConversion[] Conversions);

internal readonly record struct SharedStoragePolicy(int Size, bool Copy, string? Destructor, long Length = 0, SharedCallAdapter? Call = null);

internal sealed record SharedStorageEntry(FunctionAbi Abi, SharedStorageBody Body, int[] Offsets, SharedStoragePolicy[] Policies, int ScratchSize, int ScratchAlignment, ValueLowering[] Parameters, ValueLowering? Result, SharedDirectAdapter[] DirectCalls, FunctionAbi? Selected);

/// <summary>Builds storage-polymorphic CFGs from universally checked ownership plans. No lookup or body rebinding.</summary>
internal sealed partial class GenericStoragePlan
{
    private readonly Dictionary<FunctionKoto, Template> templates = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BoundCall, CallEntry> calls = new(ReferenceEqualityComparer.Instance);
    private readonly SourceLocationTable locations = new();
    private readonly BodyLowering verifier = new();
    private IReadOnlyDictionary<FunctionKoto, FunctionAbi>? functions;

    internal IReadOnlyDictionary<BoundCall, CallEntry> Calls => this.calls;

    internal static bool IsGeneric(FunctionKoto function)
        => !function.IsSpecialization && (function.GenericArguments.Count != 0 || (!function.IsDestructor && function.BoundSymbol?.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 }));

    internal void Clear()
    {
        this.templates.Clear();
        this.calls.Clear();
        this.functions = null;
    }

    internal bool Prepare(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, IReadOnlyDictionary<FunctionKoto, FunctionAbi> functions, out string? failure)
    {
        this.Clear();
        this.functions = functions;
        failure = null;
        for (var b = 0; b < compilation.Ownership.Bodies.Count; b++)
        {
            var body = compilation.Ownership.Bodies[b];
            if (IsGeneric(body.Function))
            {
                if (!this.PrepareBody(body, module, compilation.Binding, compilation.Project.Directory, out var template, out failure))
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
            if (IsGeneric(body.Function))
            {
                continue; // Dependent calls receive a concrete context from their caller's entry.
            }

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
            }
        }

        return true;
    }

    private static bool Fail(string reason, out string? failure)
    {
        failure = reason;
        return false;
    }

    // The selected concrete body supplies its verified ABI; receiver and constructor
    // arguments use the same checked acquisition/slot mapping as generic targets.
    private static bool IsConcreteDirect(BoundCall call)
        => call.Target.CompilerFunction == CompilerFunctionKind.None && call.TypeArguments.Length == 0;

    private static ValueLowering? StorageValue(BoundType type, AggregateLayoutPool layouts)
        => type.Kind == BoundTypeKind.ResolvedRange ? layouts.Get(type)?.Value : FunctionAbi.GetValue(type, layouts);

    private bool PrepareBody(OwnershipBody body, EmissionModule module, Binding binding, string directory, out Template? template, out string? failure)
    {
        template = null;
        failure = null;
        var function = body.Function;
        var noReturn = ReferenceEquals(function.BoundSymbol?.Type, BoundType.Never);
        if (!body.IsVerified || function.IsAnonymous || function.IsSpecialization || function.IsDestructor || function.AttributeChain is not null ||
            function.Parameters.Any(x => x.DefaultValue is not null || x.IsOptional))
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
                (op.Place < 0 && op.Kind is not (OwnershipOperationKind.Entry or OwnershipOperationKind.Exit or OwnershipOperationKind.Branch or OwnershipOperationKind.EndComparisonLoans) &&
                 !(op.Kind == OwnershipOperationKind.Call && op.Source is InvocationKoto { BoundCall.ReturnType: var result } &&
                   (ReferenceEquals(result, BoundType.Unit) || ReferenceEquals(result, BoundType.Never)))))
            {
                return Fail("Shared operation refers to invalid storage or projection.", out failure);
            }
        }

        if (!PrepareMatches(body, binding, out var caseTests, out var patternAcquisitions, out var patternDecompositions) ||
            !BodyLowering.ValidateSharedValues(body) || !body.ValidateComparisonLoans() || !this.verifier.ValidateSharedGraph(body))
        {
            return Fail("Shared body has an inconsistent value-flow plan.", out failure);
        }

        if (noReturn)
        {
            for (var id = 0; id < body.Operations.Count; id++)
            {
                if (body.Operations[id].Kind is OwnershipOperationKind.Deliver or OwnershipOperationKind.Exit && this.verifier.SharedDominates(0, id))
                {
                    return Fail("Shared Never body has a normal result delivery or exit.", out failure);
                }
            }
        }

        var leaves = new List<SharedStorageLeaf>();
        var types = new List<BoundType>();
        var starts = new int[body.Places.Count];
        var counts = new int[body.Places.Count];
        var addresses = new SharedStorageLeaf[body.Places.Count];
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
            else if (!ReferenceEquals(place.Type, BoundType.Never) && !Add(place.Type, p, -1, -1, argument, result, 0))
            {
                return Fail("Generic storage requires finite owned fields, scalar values or Type parameters.", out failure);
            }

            counts[p] = leaves.Count - starts[p];
            addresses[p] = counts[p] == 0 ? new(p, -1, -1, argument, result) : leaves[starts[p]] with { Parent = -1, Selector = -1 };
        }

        if (parameter != function.Parameters.Count)
        {
            return Fail("Shared body is missing parameter storage.", out failure);
        }

        var instructions = new SharedStorageInstruction[body.Operations.Count];
        var projections = new List<(int Place, int Selector, int Case)>();
        var constructions = new List<SharedEnumConstruction>();
        var valueCalls = new List<SharedValueCall>();
        var directCalls = new List<BoundCall>();
        var directArguments = new List<int[]>();
        var arguments = new List<int>();
        var destructions = new List<CleanupAction>();
        var liveFlags = new bool[leaves.Count];
        var phiInputs = new List<SharedPhiInput>();
        List<SharedConversion>? conversions = null;
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
                case OwnershipOperationKind.MatchDispatch:
                    break;
                case OwnershipOperationKind.PatternTest:
                    if (caseTests[id] < 0 || count != 1)
                    {
                        return Fail("Shared Pattern test has no checked flat enum Case.", out failure);
                    }

                    kind = SharedStorageOperation.CaseTest;
                    source = caseTests[id];
                    condition = id;
                    break;
                case OwnershipOperationKind.DecomposeCase:
                    if (!patternDecompositions[id] || count != 1)
                    {
                        return Fail("Shared decomposition is not owned by a checked Pattern.", out failure);
                    }

                    var split = body.Decompositions[body.OperationSteps[id]];
                    var splitSources = new int[split.PayloadCount];
                    var splitOffsets = new int[split.PayloadCount];
                    for (var k = 0; k < split.PayloadCount; k++)
                    {
                        if (counts[split.PayloadStart + k] != 1)
                        {
                            return Fail("Shared Pattern payload requires a single storage leaf.", out failure);
                        }

                        splitSources[k] = starts[split.PayloadStart + k];
                        splitOffsets[k] = leaves.Count + projections.Count;
                        projections.Add((op.Place, k, split.Case!.Ordinal));
                    }

                    kind = SharedStorageOperation.DecomposeEnum;
                    source = constructions.Count;
                    constructions.Add(new(split.Case!.Ordinal, splitSources, splitOffsets));
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
                case OwnershipOperationKind.Produce when value.Kind == OwnershipValueKind.None && id > 0 &&
                    body.Operations[id - 1].Kind == OwnershipOperationKind.Call && body.Operations[id - 1].Place == op.Place &&
                    ReferenceEquals(body.Operations[id - 1].Source, op.Source) && ReferenceEquals(op.Source.BoundType, body.Places[op.Place].Type):
                    kind = SharedStorageOperation.Initialize;
                    break;
                case OwnershipOperationKind.Produce when ReferenceEquals(body.Places[op.Place].Type, BoundType.String):
                    if (body.Places[op.Place].Kind != OwnershipPlaceKind.Temporary || op.Source is not StringLiteralKoto { AttributeChain: null } literal ||
                        !ReferenceEquals(literal.BoundType, BoundType.String) || value.Kind != OwnershipValueKind.None || count != 1)
                    {
                        return Fail("Shared string construction requires a checked string literal.", out failure);
                    }

                    kind = SharedStorageOperation.StringLiteral;
                    source = literal.Literal.Length == 0 ? -1 : module.Constants.Intern(literal.Literal, LlvmConstantKind.Text);
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
                case OwnershipOperationKind.Produce when value.Kind is OwnershipValueKind.Constant or OwnershipValueKind.Alias or OwnershipValueKind.Binary or OwnershipValueKind.Unary or OwnershipValueKind.Convert &&
                    IsSharedScalar(body.Places[op.Place].Type):
                    kind = SharedStorageOperation.Initialize;
                    break;
                case OwnershipOperationKind.Produce when value.Kind == OwnershipValueKind.BorrowedField:
                case OwnershipOperationKind.WriteBorrowedField when value.Kind == OwnershipValueKind.BorrowedFieldWrite:
                    var writing = op.Kind == OwnershipOperationKind.WriteBorrowedField;
                    var fieldSyntax = writing ? (op.Source as BinaryKoto)?.Left as MemberAccessKoto : op.Source as MemberAccessKoto;
                    var fieldInput = value.Count > 0 ? body.ValueOperands[value.Start] : -1;
                    var fieldReceiver = (uint)fieldInput < (uint)id ? body.Operations[fieldInput].Place : -1;
                    if (fieldSyntax is null || fieldReceiver < 0 || !ReferenceTypes.IsStruct(fieldSyntax.Left.BoundType) ||
                        !ReferenceEquals(body.Places[fieldReceiver].Type, fieldSyntax.Left.BoundType) ||
                        !ReferenceEquals(body.Operations[fieldInput].Source, fieldSyntax.Left) ||
                        body.Operations[fieldInput].Kind != OwnershipOperationKind.Read || count != 1 ||
                        value.Count != (writing ? 2 : 1) ||
                        (writing ? fieldSyntax.Left.BoundType!.Semantics != SemanticsKind.Uniq || op.Place != fieldReceiver || op.Input < 0 ||
                            !ReferenceEquals(body.Places[op.Input].Type, fieldSyntax.BoundType) || counts[op.Input] != 1
                            : !ReferenceEquals(body.Places[op.Place].Type, fieldSyntax.BoundType) || body.Places[op.Place].Acquisition != AcquisitionKind.Copy) ||
                        (body.IsReachable(id) && (!this.verifier.SharedDominates(fieldInput, id) || (body.GetInputState(id, fieldReceiver) & PlaceState.MustInit) == 0)))
                    {
                        return Fail("Shared field access requires a verified receiver and matching Copy field.", out failure);
                    }

                    var storedField = -1;
                    var fieldOwner = fieldSyntax.Left.BoundType!.Components[0];
                    for (var f = 0; f < StructStorage.Count(fieldOwner); f++)
                    {
                        if (ReferenceEquals(StructStorage.Field(fieldOwner, f).BoundSymbol, fieldSyntax.BoundSymbol) &&
                            ReferenceEquals(StructStorage.FieldType(fieldOwner, f), fieldSyntax.BoundType))
                        {
                            storedField = f;
                            break;
                        }
                    }

                    if (storedField < 0 || (writing && (body.IsReachable(id) && (body.GetInputState(id, op.Input) & PlaceState.MustInit) == 0)))
                    {
                        return Fail("Shared field access lacks its stored field or secured input.", out failure);
                    }

                    fieldOffset = leaves.Count + projections.Count;
                    projections.Add((fieldReceiver, storedField, -1));
                    kind = writing ? SharedStorageOperation.FieldWrite : SharedStorageOperation.FieldRead;
                    if (!writing)
                    {
                        source = starts[fieldReceiver];
                    }

                    break;
                case OwnershipOperationKind.Produce when value.Kind == OwnershipValueKind.Sequence:
                    if (value.Constant >= 0 && value.Constant < body.Sequences.Count && body.Sequences[(int)value.Constant] is { } slicePlan &&
                        (uint)slicePlan.Receiver < (uint)body.Places.Count && body.Places[slicePlan.Receiver].Type is { Kind: BoundTypeKind.Slice } sliceType)
                    {
                        var sliceSyntax = op.Source is BinaryKoto sliceExpression ? sliceExpression.Left : null;
                        if (slicePlan.Operation != id || slicePlan.Projection != -1 || count != 1 || value.Count != 0 || sliceSyntax is null ||
                            !ReferenceEquals(sliceSyntax.BoundType, sliceType) ||
                            !ReferenceEquals(ElementAccess.ValueSource(body.Places[slicePlan.Receiver].Source), ElementAccess.ValueSource(sliceSyntax)) ||
                            (body.IsReachable(id) && (body.GetInputState(id, slicePlan.Receiver) & PlaceState.MustInit) == 0))
                        {
                            return Fail("Shared Slice operation lacks its evaluated Copy handle.", out failure);
                        }

                        source = starts[slicePlan.Receiver];
                        if (slicePlan.Kind == SequenceOperation.Length && slicePlan.Index == -1 &&
                            op.Source is MemberAccessKoto { Right: IdentifierNameKoto { IdentifierName: "length" } } && ReferenceEquals(body.Places[op.Place].Type, BoundType.ISize))
                        {
                            kind = SharedStorageOperation.RangePart;
                            fieldOffset = 8;
                            break;
                        }

                        var reference = body.Places[op.Place].Type;
                        if (slicePlan.Kind != SequenceOperation.Borrow || !ReferenceTypes.IsStorage(reference) || reference.Semantics != SemanticsKind.Ref ||
                            !ReferenceEquals(reference.Components[0], sliceType.Components[0]) || !ReferenceEquals(reference.Origin, sliceType.Origin) ||
                            op.Source is not IndexKoto sliceIndex || (uint)slicePlan.Index >= (uint)id ||
                            !ReferenceEquals(body.Operations[slicePlan.Index].Source, sliceIndex.Right) || !ReferenceEquals(ScalarType(slicePlan.Index), BoundType.ISize) ||
                            (body.IsReachable(id) && !this.verifier.SharedDominates(slicePlan.Index, id)))
                        {
                            return Fail("Shared Slice borrow requires a checked index and backing Origin.", out failure);
                        }

                        kind = SharedStorageOperation.SliceAddress;
                        indexLeaf = slicePlan.Index;
                        fieldOffset = leaves.Count + projections.Count;
                        projections.Add((slicePlan.Receiver, -1, -3));
                        break;
                    }

                    if (value.Constant >= 0 && value.Constant < body.Sequences.Count && body.Sequences[(int)value.Constant] is { Kind: SequenceOperation.Start or SequenceOperation.End } range)
                    {
                        var syntax = op.Source is ForKoto loop ? loop.Iterable : (op.Source as MemberAccessKoto)?.Left;
                        if (range.Operation != id || (uint)range.Receiver >= (uint)body.Places.Count || range.Projection != -1 || range.Index != -1 ||
                            body.Places[range.Receiver].Type.Kind != BoundTypeKind.ResolvedRange || !ReferenceEquals(body.Places[op.Place].Type, BoundType.ISize) ||
                            value.Count != 0 || syntax is null || !ReferenceEquals(ElementAccess.ValueSource(body.Places[range.Receiver].Source), ElementAccess.ValueSource(syntax)) ||
                            (body.IsReachable(id) && (body.GetInputState(id, range.Receiver) & PlaceState.MustInit) == 0))
                        {
                            return Fail("Shared range endpoint lacks its initialized evaluated receiver.", out failure);
                        }

                        kind = SharedStorageOperation.RangePart;
                        source = starts[range.Receiver];
                        fieldOffset = range.Kind == SequenceOperation.Start ? 0 : 8;
                        break;
                    }

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
                        indexLeaf = read.Index; // Retain the evaluated index snapshot, not a later local value.
                        break;
                    }

                    if (value.Constant < 0 || value.Constant >= body.Sequences.Count || count != 1 ||
                        !(ReferenceEquals(body.Places[op.Place].Type, BoundType.ISize) || ReferenceEquals(body.Places[op.Place].Type, BoundType.ResolvedRange)))
                    {
                        return Fail("Shared length metadata has no verified sequence plan.", out failure);
                    }

                    var sequence = body.Sequences[(int)value.Constant];
                    if (sequence.Operation != id || sequence.Kind is not (SequenceOperation.Length or SequenceOperation.Indices) || sequence.Projection != -1 || sequence.Index != -1 ||
                        (uint)sequence.Receiver >= (uint)body.Places.Count ||
                        op.Source is not MemberAccessKoto { Right: IdentifierNameKoto metadataName } metadata ||
                        metadataName.IdentifierName != (sequence.Kind == SequenceOperation.Length ? "length" : "indices") ||
                        !ReferenceEquals(body.Places[op.Place].Type, sequence.Kind == SequenceOperation.Length ? BoundType.ISize : BoundType.ResolvedRange) ||
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

                    kind = sequence.Kind == SequenceOperation.Length ? SharedStorageOperation.Length : SharedStorageOperation.Indices;
                    copyPlace = receiverPlace; // The array policy also carries logical length, independently of stride.
                    break;
                case OwnershipOperationKind.Borrow when value.Kind == OwnershipValueKind.Address && op.Source is IndexKoto borrowedIndex:
                    if (op.Input < 0 || !ReferenceTypes.IsArray(body.Places[op.Place].Type) || body.Places[op.Place].Type.Semantics != SemanticsKind.Ref ||
                        !ReferenceTypes.IsStorage(body.Places[op.Input].Type) || body.Places[op.Input].Type.Semantics != SemanticsKind.Ref ||
                        !ReferenceEquals(borrowedIndex.Left.BoundType, body.Places[op.Place].Type) ||
                        !ReferenceEquals(borrowedIndex.BoundType, body.Places[op.Input].Type.Components[0]) ||
                        !ReferenceEquals(borrowedIndex.BoundType, body.Places[op.Place].Type.Components[0].Components[0]) ||
                        value.Count != 2 || value.Constant != op.Place || count != 1 || counts[op.Input] != 1 || op.LoanMode != LoanRequirement.Ref)
                    {
                        return Fail("Shared indexed reference lacks its checked borrowed array and element Type.", out failure);
                    }

                    var borrowedReceiver = body.ValueOperands[value.Start];
                    indexLeaf = body.ValueOperands[value.Start + 1];
                    if ((uint)borrowedReceiver >= (uint)id || (uint)indexLeaf >= (uint)id ||
                        body.Operations[borrowedReceiver].Place != op.Place || body.Operations[borrowedReceiver].Kind != OwnershipOperationKind.Read ||
                        !ReferenceEquals(body.Operations[borrowedReceiver].Source, borrowedIndex.Left) ||
                        !ReferenceEquals(body.Operations[indexLeaf].Source, borrowedIndex.Right) || !ReferenceEquals(ScalarType(indexLeaf), BoundType.ISize) ||
                        (body.IsReachable(id) && ((body.GetInputState(id, op.Place) & PlaceState.MustInit) == 0 ||
                        !this.verifier.SharedDominates(borrowedReceiver, id) || !this.verifier.SharedDominates(indexLeaf, id))))
                    {
                        return Fail("Shared indexed reference lacks evaluated receiver/index snapshots.", out failure);
                    }

                    fieldOffset = leaves.Count + projections.Count;
                    projections.Add((op.Place, -1, -2));
                    copyPlace = op.Place;
                    (dest, source) = (source, dest);
                    kind = SharedStorageOperation.ArrayAddress;
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
                    projections.Add((op.Place, fieldIndex, -1));
                    (dest, source) = (source, dest);
                    kind = SharedStorageOperation.FieldAddress;
                    break;
                case OwnershipOperationKind.Borrow when value.Kind == OwnershipValueKind.Address && value.Count == 1:
                    var parentType = body.Places[op.Place].Type;
                    var childType = op.Input < 0 ? null : body.Places[op.Input].Type;
                    var parentRead = body.ValueOperands[value.Start];
                    if (!ReferenceTypes.IsStorage(parentType) || !ReferenceTypes.IsStorage(childType) ||
                        !ReferenceEquals(parentType.Components[0], childType!.Components[0]) ||
                        (parentType.Semantics == SemanticsKind.Ref && childType.Semantics != SemanticsKind.Ref) ||
                        op.LoanMode != (childType.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref) ||
                        value.Constant != op.Place || count != 1 || counts[op.Input] != 1 || parentRead != id - 1 ||
                        body.Operations[parentRead].Kind != OwnershipOperationKind.Read || body.Operations[parentRead].Place != op.Place ||
                        !ReferenceEquals(body.Operations[parentRead].Source, KotoHelper.UnwrapParentheses(op.Source)) ||
                        (body.IsReachable(id) && ((body.GetInputState(id, op.Place) & PlaceState.MustInit) == 0 || !this.verifier.SharedDominates(parentRead, id))))
                    {
                        return Fail("Shared reborrow requires an adjacent initialized reference read and compatible authority.", out failure);
                    }

                    kind = SharedStorageOperation.Reborrow;
                    (dest, source) = (source, dest);
                    break;
                case OwnershipOperationKind.Borrow when value.Kind == OwnershipValueKind.Address && value.Count == 0:
                    if (op.Input < 0 || value.Constant != op.Place || !ReferenceTypes.IsStorage(body.Places[op.Input].Type) ||
                        !ReferenceEquals(body.Places[op.Input].Type.Components[0], body.Places[op.Place].Type) ||
                        op.LoanMode != (body.Places[op.Input].Type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref) ||
                        (body.IsReachable(id) && (body.GetInputState(id, op.Place) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared storage borrow lacks its initialized owner and reference contract.", out failure);
                    }

                    kind = SharedStorageOperation.StorageAddress;
                    dest = starts[op.Input];
                    source = op.Place;
                    count = 1;
                    break;
                case OwnershipOperationKind.Read when ReferenceTypes.IsStorage(body.Places[op.Place].Type):
                case OwnershipOperationKind.Read when body.Places[op.Place].Type.Kind == BoundTypeKind.Function:
                    break;
                case OwnershipOperationKind.CallEntry:
                    if (op.Source is not InvocationKoto ||
                        (body.IsReachable(id) && (body.GetInputState(id, op.Place) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared call entry requires a checked acquired common-function argument.", out failure);
                    }

                    arguments.Add(id);
                    kind = SharedStorageOperation.Deliver;
                    break;
                case OwnershipOperationKind.Call:
                    if (op.Source is InvocationKoto { BoundCall: { } direct } directSyntax)
                    {
                        var writeLine = ReferenceEquals(direct.Target, binding.Library.WriteLine);
                        var abort = ReferenceEquals(direct.Target, binding.Library.Abort);
                        if (direct.Target.Declaration is not FunctionKoto directTarget || !(IsGeneric(directTarget) || IsConcreteDirect(direct) || writeLine || abort) ||
                            direct.DefaultArguments.Length != 0 ||
                            arguments.Count != directTarget.Parameters.Count || arguments.Count != directSyntax.ArgumentNodes.Count + (direct.Receiver is null ? 0 : 1) ||
                            direct.ArgumentOperations.Length != directSyntax.ArgumentNodes.Count || direct.ArgumentToParameter.Length != directSyntax.ArgumentNodes.Count ||
                            !(directTarget.IsConstructor ? ReferenceEquals(direct.DeclaringType, direct.ReturnType) : ReferenceTypes.CallTypeMatches(binding.InstantiateStorageType(directTarget.BoundSymbol!.Type!, direct), direct.ReturnType, direct)) ||
                            !(op.Place < 0 ? ReferenceEquals(direct.ReturnType, BoundType.Unit) || ReferenceEquals(direct.ReturnType, BoundType.Never) : ReferenceEquals(body.Places[op.Place].Type, direct.ReturnType)) ||
                            !ReferenceEquals(directSyntax.BoundType, direct.ReturnType))
                        {
                            return Fail($"Shared direct call to '{direct.Target.Name}' requires a checked generic function and explicit arguments.", out failure);
                        }

                        var slots = new int[arguments.Count];
                        Array.Fill(slots, -1);
                        for (var a = 0; a < arguments.Count; a++)
                        {
                            var acquired = body.Operations[arguments[a]];
                            var receiverArgument = direct.Receiver is not null && a == 0;
                            var argumentIndex = a - (direct.Receiver is null ? 0 : 1);
                            var adaptation = receiverArgument ? direct.ReceiverOperation : direct.ArgumentOperations[argumentIndex];
                            var argumentSource = receiverArgument ? direct.Receiver! : directSyntax.ArgumentNodes[argumentIndex];
                            var p = receiverArgument ? direct.Target.ReceiverIndex : direct.ArgumentToParameter[argumentIndex];
                            if ((uint)p >= (uint)slots.Length || slots[p] != -1 || adaptation.ParameterIndex != p ||
                                adaptation.Kind is not (ArgumentOperationKind.Value or ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow) ||
                                !ReferenceEquals(acquired.Source, directSyntax) || !ReferenceEquals(adaptation.Source, argumentSource) ||
                                !ReferenceEquals(adaptation.SourceType, argumentSource.BoundType) ||
                                !ReferenceTypes.CallTypeMatches(binding.InstantiateStorageType(directTarget.Parameters[p].Type.BoundType!, direct), adaptation.ParameterType, direct) ||
                                !ReferenceEquals(body.Places[acquired.Place].Type, adaptation.ParameterType) ||
                                (body.IsReachable(id) && !this.verifier.SharedDominates(arguments[a], id)))
                            {
                                return Fail("Shared direct argument differs from its checked acquisition.", out failure);
                            }

                            slots[p] = acquired.Place;
                        }

                        arguments.Clear();
                        if (writeLine || abort)
                        {
                            if (slots.Length != 1 || !ReferenceEquals(body.Places[slots[0]].Type, BoundType.String) ||
                                !ReferenceEquals(direct.ReturnType, abort ? BoundType.Never : BoundType.Unit) ||
                                (abort && (directSyntax.Parent is not MacroKoto macro || !ReferenceEquals(macro.Operand, directSyntax))))
                            {
                                return Fail("Shared runtime call requires its canonical owned string argument and result contract.", out failure);
                            }

                            kind = abort ? SharedStorageOperation.AbortMessage : SharedStorageOperation.WriteLine;
                            source = starts[slots[0]];
                            dest = -1;
                            count = 1;
                            break;
                        }

                        source = directCalls.Count;
                        directCalls.Add(direct);
                        directArguments.Add(slots);
                        kind = SharedStorageOperation.DirectCall;
                        indexLeaf = op.Place;
                        fieldOffset = count;
                        dest = count == 0 ? -1 : dest;
                        count = 1;
                        break;
                    }

                    if (op.Source is not InvocationKoto { BoundValueCall: { } callPlan } call || op.Input < 0 ||
                        !ReferenceEquals(callPlan.Receiver, call.Method) || !ReferenceEquals(body.Places[op.Input].Type, callPlan.ReceiverType) ||
                        !ReferenceEquals(call.BoundType, callPlan.ReturnType) || !ReferenceEquals(body.Places[op.Place].Type, callPlan.ReturnType) ||
                        callPlan.Arguments.Length != call.ArgumentNodes.Count || arguments.Count != callPlan.Arguments.Length ||
                        !(IsSharedScalar(callPlan.ReturnType) || ReferenceEquals(callPlan.ReturnType, BoundType.Unit)))
                    {
                        return Fail("Shared common-function call has an unsupported result or inconsistent signature.", out failure);
                    }

                    var protectedReceiver = false;
                    for (var l = 0; l < body.ComparisonLoans.Count; l++)
                    {
                        var loan = body.ComparisonLoans[l];
                        protectedReceiver |= loan.Place == op.Input && loan.Mode == (callPlan.ReceiverKind == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref) && body.HasComparisonLoan(id, l) &&
                            ReferenceEquals(body.Operations[loan.Read].Source, callPlan.Receiver) && (!body.IsReachable(id) || this.verifier.SharedDominates(loan.Read, id));
                    }

                    if (!protectedReceiver || (body.IsReachable(id) && (body.GetInputState(id, op.Input) & PlaceState.MustInit) == 0))
                    {
                        return Fail("Shared common-function receiver lacks its call-wide shared Loan.", out failure);
                    }

                    var callInputs = callPlan.Signature.Components[0];
                    if (arguments.Count != (ReferenceEquals(callInputs, BoundType.Unit) ? 0 : callInputs.Components.Count))
                    {
                        return Fail("Shared common-function arguments differ from its signature.", out failure);
                    }

                    var argumentLeaves = new int[arguments.Count];
                    for (var a = 0; a < arguments.Count; a++)
                    {
                        var acquired = body.Operations[arguments[a]];
                        var argument = callPlan.Arguments[a];
                        if (!ReferenceEquals(acquired.Source, call) || argument.ParameterIndex != a || argument.Kind != ArgumentOperationKind.Value ||
                            !ReferenceEquals(argument.ParameterType, callInputs.Components[a]) || !ReferenceEquals(body.Places[acquired.Place].Type, argument.ParameterType) ||
                            !ReferenceEquals(argument.Source, call.ArgumentNodes[a]) || !ReferenceEquals(argument.SourceType, call.ArgumentNodes[a].BoundType) ||
                            counts[acquired.Place] != 1 || (body.IsReachable(id) && !this.verifier.SharedDominates(arguments[a], id)))
                        {
                            return Fail("Shared common-function argument lacks its checked acquisition and complete Type.", out failure);
                        }

                        argumentLeaves[a] = starts[acquired.Place];
                    }

                    arguments.Clear();
                    copyPlace = op.Input;
                    source = valueCalls.Count;
                    valueCalls.Add(new(starts[op.Input], argumentLeaves));
                    kind = SharedStorageOperation.Call;
                    if (count == 0)
                    {
                        dest = -1;
                    }

                    count = 1; // Unit calls still execute, but use no result storage.
                    break;
                case OwnershipOperationKind.Read when IsSharedScalar(body.Places[op.Place].Type):
                    kind = SharedStorageOperation.ReadBoolean;
                    break;
                case OwnershipOperationKind.Consume:
                case OwnershipOperationKind.AcquirePattern when patternAcquisitions[id]:
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
                case OwnershipOperationKind.PayloadPlacement:
                case OwnershipOperationKind.InitializeSubject:
                    if (count != 0 && (op.Input < 0 || !ReferenceEquals(body.Places[op.Place].Type, body.Places[op.Input].Type) ||
                        (body.IsReachable(id) && (body.GetInputState(id, op.Input) & PlaceState.MustInit) == 0)))
                    {
                        return Fail("Shared placement requires matching complete Types.", out failure);
                    }

                    kind = SharedStorageOperation.Transfer;
                    break;
                case OwnershipOperationKind.CompleteConstruction:
                    var constructionIndex = body.OperationSteps[id];
                    if ((uint)constructionIndex >= (uint)body.Constructions.Count || count != 1)
                    {
                        return Fail("Shared enum completion has no retained construction.", out failure);
                    }

                    var construction = body.Constructions[constructionIndex];
                    var enumType = body.Places[op.Place].Type;
                    if (construction.Place != op.Place || construction.Case is not { } selected || !EnumStorage.IsEnum(enumType) ||
                        !ReferenceEquals(enumType.Symbol, selected.Owner) || !ReferenceEquals(EnumStorage.Case(enumType, selected.Ordinal), selected) ||
                        !ReferenceEquals(body.Places[op.Place].Source.BoundSymbol?.EnumCase, selected) ||
                        construction.PayloadCount != selected.Payload.Length || construction.PayloadStart <= op.Place ||
                        construction.PayloadStart > body.Places.Count - construction.PayloadCount)
                    {
                        return Fail("Shared enum completion has an inconsistent selected Case.", out failure);
                    }

                    var payloadSources = new int[construction.PayloadCount];
                    var payloadOffsets = new int[construction.PayloadCount];
                    for (var k = 0; k < construction.PayloadCount; k++)
                    {
                        var payload = construction.PayloadStart + k;
                        if (counts[payload] != 1 || body.Places[payload].Kind != OwnershipPlaceKind.Payload ||
                            !ReferenceEquals(body.Places[payload].Type, enumType.StoredCases![selected.Ordinal].Components[k]) ||
                            (body.IsReachable(id) && (body.GetInputState(id, payload) & PlaceState.MustInit) == 0))
                        {
                            return Fail("Shared enum payload must be initialized with its selected complete Type.", out failure);
                        }

                        payloadSources[k] = starts[payload];
                        payloadOffsets[k] = leaves.Count + projections.Count;
                        projections.Add((op.Place, k, selected.Ordinal));
                    }

                    kind = SharedStorageOperation.ConstructEnum;
                    source = constructions.Count;
                    constructions.Add(new(selected.Ordinal, payloadSources, payloadOffsets));
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
                        condition = body.ValueOperands[value.Start];
                        var conditionPlace = BooleanPlace(condition);
                        if (conditionPlace < 0)
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
                if (edge.Kind == OwnershipEdgeKind.Abort)
                {
                    if (op.Kind != OwnershipOperationKind.Call || body.Operations[edge.To].Kind != OwnershipOperationKind.Exit)
                    {
                        return Fail("Shared CFG has an invalid call Abort edge.", out failure);
                    }

                    continue;
                }

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

            if (op.Kind == OwnershipOperationKind.Call && ReferenceEquals(op.Source.BoundType, BoundType.Never) && (next >= 0 || alternative >= 0))
            {
                return Fail("Shared Never call has a normal continuation.", out failure);
            }

            if ((alternative >= 0 && condition < 0) || !this.locations.TryGet(kind == SharedStorageOperation.AbortMessage ? op.Source.Parent! : op.Source, directory, out var location))
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

            SharedScalarValue? scalar = null;
            var scalarPlace = op.Kind == OwnershipOperationKind.Consume ? op.Input : op.Place;
            if (value.Kind == OwnershipValueKind.Phi)
            {
                if (scalarPlace < 0 || !IsSharedScalar(body.Places[scalarPlace].Type))
                {
                    return Fail("Shared result join requires a supported scalar Type.", out failure);
                }

                var start = phiInputs.Count;
                for (var n = 0; n < value.Count; n++)
                {
                    var input = body.PhiInputs[value.Start + n];
                    phiInputs.Add(new(input.Value, body.Edges[input.Edge].From));
                }

                scalar = new(SharedScalarType(body.Places[scalarPlace].Type), value.Kind, start, value.Count, 0, null, true);
            }
            else if (scalarPlace >= 0 && op.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Produce or OwnershipOperationKind.Consume or OwnershipOperationKind.Call &&
                IsSharedScalar(body.Places[scalarPlace].Type))
            {
                var first = value.Count > 0 ? body.ValueOperands[value.Start] : -1;
                var second = value.Count > 1 ? body.ValueOperands[value.Start + 1] : -1;
                if (value.Kind == OwnershipValueKind.Convert)
                {
                    var inputType = ScalarType(first);
                    var targetType = body.Places[scalarPlace].Type;
                    if (value.Count != 1 || ScalarTypes.Width(inputType) is not (8 or 16 or 32 or 64) ||
                        ScalarTypes.Width(targetType) is not (8 or 16 or 32 or 64))
                    {
                        return Fail("Shared numeric conversion requires supported integer operands.", out failure);
                    }

                    conversions ??= new();
                    second = conversions.Count;
                    conversions.Add(new(WindowsLowering.GetValue(inputType!)!, BodyLowering.PlanConversion(inputType!, targetType, 64)));
                }

                var binary = value.Kind == OwnershipValueKind.Binary;
                var arithmetic = value.Operator is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk or KotoKind.Slash or KotoKind.Percent;
                var bitwise = value.Operator is KotoKind.Ampersand or KotoKind.Bar or KotoKind.Caret;
                var shift = value.Operator is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan;
                var operand = binary ? ScalarType(first) : null;
                var countWidth = binary && shift ? ScalarTypes.Width(ScalarType(second)) : 0;
                if (binary && (value.Count != 2 || !(arithmetic || bitwise || shift || ComparisonPredicate(value.Operator) is not null) ||
                    operand is null || !IsSharedScalar(operand) ||
                    (ReferenceEquals(operand, BoundType.Boolean) && value.Operator is not (KotoKind.EqualsEquals or KotoKind.ExclamationEquals)) ||
                    (shift ? countWidth is not (8 or 16 or 32 or 64) : !ReferenceEquals(operand, ScalarType(second))) ||
                    !ReferenceEquals(body.Places[scalarPlace].Type, arithmetic || bitwise || shift ? operand : BoundType.Boolean)))
                {
                    return Fail("Shared scalar operation requires compatible integer/boolean operands and result.", out failure);
                }

                var unary = value.Kind == OwnershipValueKind.Unary;
                var unaryOperand = unary && value.Count == 1 ? ScalarType(first) : null;
                if (unary && (unaryOperand is null || !ReferenceEquals(unaryOperand, body.Places[scalarPlace].Type) ||
                    !(value.Operator == KotoKind.Not ? ReferenceEquals(unaryOperand, BoundType.Boolean) :
                      (value.Operator == KotoKind.PrefixPlus || (value.Operator == KotoKind.PrefixMinus && ScalarTypes.Signed(unaryOperand))) && !ReferenceEquals(unaryOperand, BoundType.Boolean))))
                {
                    return Fail("Shared scalar unary operation requires a matching bool or signed-integer operand.", out failure);
                }

                // Checked arithmetic names its overflow intrinsic; a comparison carries its operand Type.
                var sign = binary && ScalarTypes.Signed(operand!) ? "s" : "u";
                var operation = unary ? value.Operator switch { KotoKind.Not => "not", KotoKind.PrefixMinus => "neg", _ => "pos" } : !binary ? null : value.Operator switch
                {
                    KotoKind.Plus => sign + "add",
                    KotoKind.Minus => sign + "sub",
                    KotoKind.Asterisk => sign + "mul",
                    KotoKind.Slash => sign + "div",
                    KotoKind.Percent => sign + "rem",
                    KotoKind.Ampersand => "and",
                    KotoKind.Bar => "or",
                    KotoKind.Caret => "xor",
                    KotoKind.LessThanLessThan => "shl",
                    KotoKind.GreaterThanGreaterThan => sign == "s" ? "ashr" : "lshr",
                    KotoKind.EqualsEquals or KotoKind.ExclamationEquals => ComparisonPredicate(value.Operator) + ":" + SharedScalarType(operand!),
                    _ => sign + ComparisonPredicate(value.Operator) + ":" + SharedScalarType(operand!),
                };
                scalar = new(SharedScalarType(body.Places[scalarPlace].Type), value.Kind, first, second, unchecked((long)value.Constant), operation, op.Kind == OwnershipOperationKind.Produce && value.Kind is OwnershipValueKind.Constant or OwnershipValueKind.Alias or OwnershipValueKind.Binary or OwnershipValueKind.Unary or OwnershipValueKind.Convert, countWidth);
            }

            instructions[id] = new(kind, dest, source, count, copyPlace, next, alternative, condition, module.Constants.Intern(location, LlvmConstantKind.Location), this.verifier.SharedDominates(0, id), destructionStart, fieldOffset, indexLeaf, scalar);
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

        if (arguments.Count != 0)
        {
            return Fail("Shared body has unsecured call arguments.", out failure);
        }

        var physical = new SharedStorageBody("__kimi_shared" + module.SharedBodies.Count, leaves.ToArray(), instructions, policies.Count, function.Parameters.Count, destructions.ToArray(), liveFlags, constructions.ToArray(), valueCalls.ToArray(), directArguments.ToArray(), addresses, leaves.Count + projections.Count, phiInputs.ToArray(), noReturn, conversions?.ToArray() ?? []);
        template = new(body, physical, policies.ToArray(), projections.ToArray(), directCalls.ToArray());
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

        BoundType? ScalarType(int id)
        {
            if ((uint)id >= (uint)body.Operations.Count)
            {
                return null;
            }

            var operation = body.Operations[id];
            var place = operation.Kind == OwnershipOperationKind.Consume ? operation.Input : operation.Place;
            return place < 0 ? null : body.Places[place].Type;
        }

        bool Add(BoundType type, int place, int parent, int selector, int argument, bool result, int depth)
        {
            // Origins have already been checked by binding and ownership. They carry
            // dependencies, not runtime storage; retain them in the complete Type.
            var borrowedStorage = ReferenceTypes.IsStorage(type) && type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq;
            if (depth > 32 || (type.Origin is not null && !borrowedStorage && type.Kind != BoundTypeKind.Slice))
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

            if (type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.FixedArray or BoundTypeKind.ResolvedRange or BoundTypeKind.Function or BoundTypeKind.Slice) && !EnumStorage.IsEnum(type) && !IsSharedScalar(type) && !ReferenceEquals(type, BoundType.String) && !borrowedStorage)
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
                return ReferenceEquals(type.Symbol!.Scope.Owner, function) ? "function:" + type.Symbol.Slot
                    : "container:" + Binding.ContainerSlot(function.BoundSymbol!.Scope.Owner, type.Symbol);
            }

            return type.Kind + ":" + type.Name + ":" + (type.LengthExpression is { } length ? LengthKey(length) : type.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "<" + string.Join(",", type.Components.Select(TypeKey)) + ">";
        }

        string LengthKey(BoundLength length) => length.Parameter is { } symbol ? "slot:" + symbol.Slot : length.IsConstant
            ? length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : length.Operation + "(" + LengthKey(length.Left!) + "," + (length.Right is null ? string.Empty : LengthKey(length.Right)) + ")";
    }

    private bool ConcreteAdapter(BoundCall call, FunctionKoto target, AggregateLayoutPool layouts, out SharedDirectAdapter? adapter)
    {
        adapter = null;
        var result = FunctionAbi.GetValue(call.ReturnType, layouts);
        var noReturn = ReferenceEquals(call.ReturnType, BoundType.Never);
        if (this.functions?.GetValueOrDefault(target) is not { } abi || (result is null && !noReturn) || abi.NoReturn != noReturn)
        {
            return false;
        }

        var parameters = new ValueLowering[target.Parameters.Count];
        for (var p = 0; p < parameters.Length; p++)
        {
            if (target.Parameters[p].Type.BoundType is not { } type || FunctionAbi.GetValue(type, layouts) is not { } value)
            {
                return false;
            }

            parameters[p] = value;
        }

        adapter = new(abi, parameters, result);
        return true;
    }

    private bool PrepareEntry(Compilation compilation, EmissionModule module, AggregateLayoutPool layouts, BoundCall call, Template template, out CallEntry? entry, out string? failure, int depth = 0)
    {
        entry = null;
        failure = null;
        if (depth > 128)
        {
            return Fail("Shared call context expansion exceeds the supported depth.", out failure);
        }

        var body = template.Body;
        var target = body.Function;
        var binding = compilation.Binding;
        if (call.DefaultArguments.Length != 0)
        {
            return Fail("Shared entries do not yet lower Origin or default substitution.", out failure);
        }

        var parameters = new BoundType[target.Parameters.Count];
        var values = new ValueLowering[parameters.Length];
        var abiParameters = new List<AbiParameter>();
        var result = call.ReturnType;
        var noReturn = ReferenceEquals(result, BoundType.Never);
        var resultValue = FunctionAbi.GetValue(result, layouts);
        if (resultValue is null && !noReturn)
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
            var borrowedStorage = (ReferenceTypes.IsStorage(type) || ReferenceTypes.IsString(type)) && type!.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && ReferenceTypes.IsStorage(target.Parameters[i].Type.BoundType);
            if (type is null || value is null || ((ReferenceTypes.IsString(type) || ReferenceTypes.IsStorage(type)) && !borrowedStorage) ||
                (borrowedStorage && FunctionAbi.GetValue(type.Components[0], layouts) is null))
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
                this.calls.Add(call, entry);
                return true;
            }
        }

        var offsets = new int[template.Physical.AddressOffset + body.Places.Count];
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
            if (ReferenceEquals(body.Places[p].Type, BoundType.Never))
            {
                resolved[p] = BoundType.Never; // No value, layout, scratch reservation or policy.
                continue;
            }

            var type = binding.InstantiateStorageType(body.Places[p].Type, call);
            var value = type is null ? null : StorageValue(type, layouts);
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

        for (var p = 0; p < body.Places.Count; p++)
        {
            offsets[template.Physical.AddressOffset + p] = placeOffsets[p];
        }

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
            if (projection.Case == -3)
            {
                if (resolved[projection.Place].Kind != BoundTypeKind.Slice || FunctionAbi.GetValue(resolved[projection.Place].Components[0], layouts) is not { } sliceElement)
                {
                    return Fail("Shared Slice borrow has no concrete element stride.", out failure);
                }

                offsets[template.Physical.Leaves.Length + i] = sliceElement.Layout.Stride;
                continue;
            }

            if (projection.Case == -2)
            {
                if (!ReferenceTypes.IsArray(resolved[projection.Place]) || FunctionAbi.GetValue(resolved[projection.Place].Components[0].Components[0], layouts) is not { } elementValue)
                {
                    return Fail("Shared indexed reference has no concrete element stride.", out failure);
                }

                offsets[template.Physical.Leaves.Length + i] = elementValue.Layout.Stride;
                continue;
            }

            if (projection.Case >= 0)
            {
                if (layouts.Get(resolved[projection.Place]) is not { Cases: { } cases } enumeration || (uint)projection.Case >= (uint)cases.Length)
                {
                    return Fail("Shared enum payload has no instantiated layout.", out failure);
                }

                offsets[template.Physical.Leaves.Length + i] = enumeration.PayloadOffset + cases[projection.Case].Offset(projection.Selector);
                continue;
            }

            if (!ReferenceTypes.IsStruct(resolved[projection.Place]) || layouts.Get(resolved[projection.Place].Components[0]) is not { } ownerLayout)
            {
                return Fail("Shared projected borrow has no instantiated receiver layout.", out failure);
            }

            offsets[template.Physical.Leaves.Length + i] = ownerLayout.Offset(projection.Selector);
        }

        for (var i = 0; i < policies.Length; i++)
        {
            var type = binding.InstantiateStorageType(template.Types[i], call);
            var value = type is null ? null : StorageValue(type, layouts);
            var proof = type is null ? ConstraintProof.Unknown : binding.ProveCopy(type, target);
            if (type is null || value is null || value.Layout.Stride != value.Layout.Size || proof is not (ConstraintProof.Proven or ConstraintProof.Refuted))
            {
                return Fail("Shared policy requires concrete layout and proved acquisition effects.", out failure);
            }

            var aggregate = layouts.Get(type);
            var destroy = ReferenceEquals(type, BoundType.String) ? "__kimi_destroy_string" :
                aggregate?.NeedsDestruction == true ? "__kimi_drop_aggregate" + aggregate.Id : null;
            var array = ReferenceTypes.IsArray(type) ? type.Components[0] : type;
            SharedCallAdapter? adapter = null;
            var callable = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
            var concrete = callable.Kind == BoundTypeKind.Closure ? callable.Symbol?.Declaration as FunctionKoto : null;
            if ((callable.Kind == BoundTypeKind.Function || concrete is not null) && template.Physical.Instructions.Any(x => x.Kind == SharedStorageOperation.Call && x.CopyPlace == i))
            {
                var signature = concrete?.BoundClosure?.Signature ?? callable;
                var inputTypes = signature.Components[0];
                var inputValues = new ValueLowering[ReferenceEquals(inputTypes, BoundType.Unit) ? 0 : inputTypes.Components.Count];
                for (var a = 0; a < inputValues.Length; a++)
                {
                    if (!ReferenceTypes.IsValue(inputTypes.Components[a]) || WindowsLowering.GetValue(inputTypes.Components[a]) is not { } inputValue || inputValue.ArgumentType != inputValue.ComputationType)
                    {
                        return Fail("Shared callback adapter requires supported concrete scalar parameters.", out failure);
                    }

                    inputValues[a] = inputValue;
                }

                if (WindowsLowering.GetValue(signature.Components[1]) is not { } callbackResult ||
                    !(IsSharedScalar(signature.Components[1]) || ReferenceEquals(signature.Components[1], BoundType.Unit)))
                {
                    return Fail("Shared callback adapter requires bool, integer or Unit results.", out failure);
                }

                if (concrete is not null && this.functions?.GetValueOrDefault(concrete) is null)
                {
                    return Fail("Concrete Callable witness has no verified entry.", out failure);
                }

                adapter = new(inputValues, callbackResult, concrete is null ? null : this.functions![concrete], type.Kind == BoundTypeKind.Semantics);
            }

            policies[i] = new(value.Layout.Size, proof == ConstraintProof.Proven, destroy, array.Kind == BoundTypeKind.FixedArray ? array.Length : 0, adapter);
        }

        var abi = new FunctionAbi("__kimi_generic_entry" + module.SharedEntries.Count, FunctionAbi.ResultType(result, layouts)!, abiParameters.ToArray(), noReturn: noReturn, resultSlot: resultSlot);
        var selected = binding.SelectSpecialization(call);
        var directAdapters = new SharedDirectAdapter[template.DirectCalls.Length];
        var generated = new SharedStorageEntry(abi, template.Physical, offsets, policies, size, alignment, values, resultValue, directAdapters, selected is null ? null : this.functions!.GetValueOrDefault(selected));
        if (selected is not null && generated.Selected is null)
        {
            return Fail("Selected specialization has no verified implementation ABI.", out failure);
        }

        module.SharedEntries.Add(generated);
        entry = new(template, generated, parameters, result, call.DeclaringType, call.TypeArguments.ToArray(), call.LengthArguments.ToArray());
        this.calls.Add(call, entry); // Reserve before following recursive concrete call contexts.
        for (var i = 0; i < directAdapters.Length; i++)
        {
            if (template.DirectCalls[i].Target.Declaration is FunctionKoto concrete && !IsGeneric(concrete))
            {
                // A concrete target needs no context: call its verified ordinary entry.
                if (!this.ConcreteAdapter(template.DirectCalls[i], concrete, layouts, out directAdapters[i]!))
                {
                    return Fail("Shared concrete call target has no verified entry or representation.", out failure);
                }

                continue;
            }

            var inner = binding.InstantiateForwardedCall(template.DirectCalls[i], call);
            if (inner?.Target.Declaration is not FunctionKoto innerTarget || !this.templates.TryGetValue(innerTarget, out var innerTemplate) ||
                !this.PrepareEntry(compilation, module, layouts, inner, innerTemplate, out var innerEntry, out failure, depth + 1))
            {
                return Fail(failure ?? "Shared forwarded call cannot instantiate its verified context.", out failure);
            }

            directAdapters[i] = new(innerEntry!.Physical.Abi, innerEntry.Physical.Parameters, innerEntry.Physical.Result);
        }

        return true;
    }

    internal sealed record Template(OwnershipBody Body, SharedStorageBody Physical, BoundType[] Types, (int Place, int Selector, int Case)[] Projections, BoundCall[] DirectCalls);

    // bool and integers up to 64 bits; literal constants keep their Type's bit pattern.
    private static bool IsSharedScalar(BoundType type)
        => ReferenceEquals(type, BoundType.Boolean) || ScalarTypes.Width(type) is 8 or 16 or 32 or 64;

    private static string? ComparisonPredicate(KotoKind kind) => kind switch
    {
        KotoKind.LessThan => "lt",
        KotoKind.LessThanEquals => "le",
        KotoKind.GreaterThan => "gt",
        KotoKind.GreaterThanEquals => "ge",
        KotoKind.EqualsEquals => "eq",
        KotoKind.ExclamationEquals => "ne",
        _ => null,
    };

    private static string SharedScalarType(BoundType type) => ReferenceEquals(type, BoundType.Boolean) ? "i1" : "i" + ScalarTypes.Width(type);

    internal sealed record CallEntry(Template Template, SharedStorageEntry Physical, BoundType[] Parameters, BoundType Result, BoundType? DeclaringType, BoundType?[] Arguments, BoundLength?[] Lengths);
}
