// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly EmissionFunction validation = new();
    private int[] incoming = [];
    private int[] predecessor = [];
    private int[] successor = [];
    private int[] blocks = [];
    private int[] blockEnds = [];
    private int[] queue = [];
    private int[] instructionStarts = [];
    private ArithmeticCheckKind[] checks = [];
    private int[] continuations = [];

    private static bool IsScalar(BoundType? type) => ReferenceTypes.IsValue(type);

    private static ArithmeticCheckKind ClassifyCheck(OwnershipValue value, BoundType? type, ConversionPlan conversion) => value.Kind == OwnershipValueKind.Convert
        ? conversion.Checked ? conversion.Operator == "fptrunc" ? ArithmeticCheckKind.FloatingConversion : ArithmeticCheckKind.Conversion : ArithmeticCheckKind.None
        : FloatingTypes.Supports(type) || ReferenceTypes.IsPointer(type) ? ArithmeticCheckKind.None : value.Kind switch
    {
        OwnershipValueKind.Binary when value.Operator is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan => ArithmeticCheckKind.Shift,
        OwnershipValueKind.Binary when value.Operator is KotoKind.Slash or KotoKind.Percent => type is not null && ScalarTypes.Signed(type) ? ArithmeticCheckKind.Division : ArithmeticCheckKind.UnsignedDivision,
        OwnershipValueKind.Binary when value.Operator is KotoKind.Plus or KotoKind.Minus or KotoKind.Asterisk => ArithmeticCheckKind.Overflow,
        OwnershipValueKind.Unary when value.Operator == KotoKind.PrefixMinus => ArithmeticCheckKind.Overflow,
        _ => ArithmeticCheckKind.None,
    };

    private static void Grow(ref int[] array, int count)
    {
        if (array.Length < count)
        {
            Array.Resize(ref array, Math.Max(count, Math.Max(16, array.Length * 2)));
        }
    }

    private static int Input(OwnershipBody body, int operation, int index) => body.Values[operation].Kind == OwnershipValueKind.Phi
        ? body.PhiInputs[body.Values[operation].Start + index].Value : body.ValueOperands[body.Values[operation].Start + index];

    private static int Definition(OwnershipBody body, int id)
    {
        while (body.Values[id].Kind == OwnershipValueKind.Alias)
        {
            id = Input(body, id, 0);
        }

        return id;
    }

    private static EmissionOperand Operand(OwnershipBody body, int id)
    {
        id = Definition(body, id);
        return body.Values[id].Kind switch
        {
            OwnershipValueKind.Constant => new(ReferenceEquals(ValueType(body, id), BoundType.F32) ? EmissionOperandKind.Float32 : ReferenceEquals(ValueType(body, id), BoundType.F64) ? EmissionOperandKind.Float64 : ReferenceTypes.IsPointer(ValueType(body, id)) ? EmissionOperandKind.NullAddress : EmissionOperandKind.Integer, body.Values[id].Constant),
            OwnershipValueKind.Parameter => new(EmissionOperandKind.Argument, body.Values[id].Constant),
            _ => new(EmissionOperandKind.Value, id),
        };
    }

    private bool LowerGraph(KimiLibrary library, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, Span<byte> marks, out string? failure)
    {
        failure = null;
        var count = body.Operations.Count;
        Grow(ref this.incoming, count);
        Grow(ref this.predecessor, count);
        Grow(ref this.successor, count);
        Grow(ref this.blocks, count);
        Grow(ref this.blockEnds, count);
        Grow(ref this.queue, count);
        Grow(ref this.instructionStarts, count + 1);
        if (this.checks.Length < count)
        {
            Array.Resize(ref this.checks, Math.Max(count, Math.Max(16, this.checks.Length * 2)));
        }

        this.PrepareConversions(body);
        if (!this.PrepareStringComparisons(body, out failure) || !this.PrepareReferences(body, out failure))
        {
            return false;
        }

        if (!this.PrepareSlotFunctions(body, function, out failure) || !this.PrepareSlotResults(body, out failure) || !this.PrepareAggregates(body, function, out failure) || !this.PrepareMatches(body, function, out failure) || !this.PrepareStrings(body, function, out failure) || !this.PrepareElements(body, out failure))
        {
            return false;
        }

        foreach (var sequence in body.Sequences)
        {
            if (sequence.Kind is SequenceOperation.Read or SequenceOperation.ArrayRead or SequenceOperation.Borrow or SequenceOperation.Slice && (uint)sequence.Operation < (uint)count)
            {
                this.continuations[sequence.Operation] = count + sequence.Operation;
            }
        }

        this.PreparePartDestruction(body, function);
        if (!this.PrepareReceiverFields(body, function, out failure))
        {
            return false;
        }

        // Semantic dominance remains on the verification graph, including covered arms.
        if (this.hasMatches)
        {
            this.logicalIncoming.AsSpan(0, count).Clear();
            for (var i = 0; i < count; i++)
            {
                this.blocks[i] = body.IsReachable(i) ? i : -1;
            }

            for (var e = 0; e < body.Edges.Count; e++)
            {
                var edge = body.Edges[e];
                if (body.IsReachable(edge.From) && edge.Kind != OwnershipEdgeKind.Abort)
                {
                    this.logicalIncoming[edge.To]++;
                }
            }

            this.BuildDominators(body);
        }

        var edges = this.ExecutionEdges(body);
        this.incoming.AsSpan(0, count).Clear();
        this.blocks.AsSpan(0, count).Fill(-1);
        this.successor.AsSpan(0, count).Fill(-1);
        this.queue[0] = 0;
        marks[0] |= NormalMark;
        var queued = 1;
        for (var q = 0; q < queued; q++)
        {
            var op = this.queue[q];
            var successors = 0;
            var yes = 0;
            var no = 0;
            var aborts = 0;
            for (var e = this.ExecutionHead(body, op); e >= 0; e = edges[e].Next)
            {
                var edge = edges[e];
                if ((uint)edge.To >= (uint)count || edge.From != op)
                {
                    return Fail("Invalid CFG edge.", out failure);
                }

                if (edge.Kind == OwnershipEdgeKind.Abort)
                {
                    if (body.Operations[op].Kind is not (OwnershipOperationKind.Call or OwnershipOperationKind.TestAbort) || body.Operations[edge.To].Kind != OwnershipOperationKind.Exit || body.EdgeHeads[edge.To] >= 0)
                    {
                        return Fail("Invalid terminal call Abort edge.", out failure);
                    }

                    marks[edge.To] |= AbortMark;
                    aborts++;
                    continue;
                }

                if (edge.Kind is not (OwnershipEdgeKind.Normal or OwnershipEdgeKind.Return or OwnershipEdgeKind.Back or OwnershipEdgeKind.True or OwnershipEdgeKind.False))
                {
                    return Fail("Unsupported control-flow edge.", out failure);
                }

                if ((body.Operations[op].Kind == OwnershipOperationKind.Deliver) != (edge.Kind == OwnershipEdgeKind.Return) ||
                    ((edge.Kind == OwnershipEdgeKind.Return) != (body.Operations[edge.To].Kind == OwnershipOperationKind.Exit)))
                {
                    return Fail("Only result delivery may reach a normal function exit.", out failure);
                }

                successors++;
                yes += edge.Kind == OwnershipEdgeKind.True ? 1 : 0;
                no += edge.Kind == OwnershipEdgeKind.False ? 1 : 0;
                this.successor[op] = edge.To;
                this.incoming[edge.To]++;
                this.predecessor[edge.To] = op;
                if ((marks[edge.To] & NormalMark) == 0)
                {
                    marks[edge.To] |= NormalMark;
                    this.queue[queued++] = edge.To;
                }
            }

            var exit = body.Operations[op].Kind == OwnershipOperationKind.Exit;
            var call = body.Operations[op].Kind is OwnershipOperationKind.Call or OwnershipOperationKind.TestAbort;
            var neverCall = body.Operations[op].Kind == OwnershipOperationKind.TestAbort || (call && body.Operations[op].Source is InvocationKoto invocation && ReferenceEquals(invocation.BoundCall?.ReturnType ?? invocation.BoundValueCall?.ReturnType, BoundType.Never));
            if (aborts != (call ? 1 : 0) || (exit || neverCall ? successors != 0 : successors == 0 || (successors != 1 && (successors != 2 || yes != 1 || no != 1)) || (successors == 1 && yes + no != 0)))
            {
                return Fail("Missing or inconsistent CFG terminator.", out failure);
            }

            if (successors == 2)
            {
                if (body.Operations[op].Kind != OwnershipOperationKind.PatternTest && (body.Operations[op].Kind != OwnershipOperationKind.Branch || body.Values[op].Kind != OwnershipValueKind.Alias))
                {
                    return Fail("Conditional branch has no verified condition value.", out failure);
                }

                this.successor[op] = -1;
            }
        }

        // Compute physical blocks and their actual ending labels, ignoring terminal runtime Abort edges.
        for (var i = 0; i < count; i++)
        {
            if (body.IsReachable(i) != ((marks[i] & (NormalMark | AbortMark)) != 0))
            {
                if (!this.hasMatches || !body.IsReachable(i))
                {
                    return Fail("CFG reachability does not match verified analysis.", out failure);
                }

                marks[i] |= DispatchOmittedMark;
            }

            if ((marks[i] & NormalMark) == 0 || (i != 0 && this.incoming[i] == 1 && this.successor[this.predecessor[i]] == i && body.Values[i].Kind != OwnershipValueKind.Phi))
            {
                continue;
            }

            var end = i;
            for (var cursor = i; ;)
            {
                this.blocks[cursor] = i;
                if (this.continuations[cursor] >= 0)
                {
                    end = this.continuations[cursor];
                }

                var next = this.successor[cursor];
                if (next < 0 || next == 0 || this.incoming[next] != 1 || body.Values[next].Kind == OwnershipValueKind.Phi)
                {
                    break;
                }

                cursor = next;
            }

            this.blockEnds[i] = end;
        }

        if (!this.hasMatches)
        {
            this.BuildDominators(body);
        }

        if (!this.ValidateSlotResults(body, out failure) || !this.ValidateAggregateDominance(body, out failure))
        {
            return false;
        }

        for (var p = 0; p < body.Places.Count; p++)
        {
            var place = body.Places[p];
            if ((this.eraseReceiver && ReceiverField(body, p)) || (ReferenceEquals(place.Type, BoundType.Never) && place.Kind == OwnershipPlaceKind.Result))
            {
                continue;
            }

            if ((this.aggregatePlaces[p]?.Value ?? WindowsLowering.GetValue(place.Type)) is not { } value ||
                (this.aggregatePlaces[p] is null && !IsScalar(place.Type) && !ReferenceEquals(place.Type, BoundType.Unit) && !ReferenceEquals(place.Type, BoundType.String) && !ReferenceTypes.IsString(place.Type)) ||
                (ReferenceTypes.IsString(place.Type) && place.Kind is not (OwnershipPlaceKind.Parameter or OwnershipPlaceKind.Temporary)) ||
                (ReferenceEquals(place.Type, BoundType.String) && !this.IsStringStorage(place)))
            {
                return Fail("Unsupported value storage or string result/parameter.", out failure);
            }

            // Zero-sized arrays/tuples can still own destructible logical values.
            // Their cleanup needs a valid slot just as a zero-sized struct does;
            // the writer reserves an address anchor without changing TypeLayout.
            var addressRequired = this.aggregatePlaces[p] is { NeedsDestruction: true };
            if (value.Layout.Size == 0)
            {
                for (var i = 0; i < body.Operations.Count && !addressRequired; i++)
                {
                    var operation = body.Operations[i];
                    addressRequired = operation.Kind == OwnershipOperationKind.Borrow && operation.Place == p;
                }
            }

            if (!ReferenceTypes.IsString(place.Type) && (value.Layout.Size != 0 || StructStorage.IsStruct(place.Type) || addressRequired) && function.SlotAddresses[p].Kind == EmissionOperandKind.SlotAddress && function.SlotAddresses[p].Value == p && (!IsScalar(place.Type) || place.Kind == OwnershipPlaceKind.Local || IsMaterializedScalar(body, p)))
            {
                function.Slots.Add(new(p, value));
            }
        }

        // Validate even unexecuted operations. The retained scratch function is never serialized.
        this.validation.Reset(function.Abi, false);
        this.validation.LiveFlags.AddRange(function.LiveFlags);
        this.validation.PathFlags.AddRange(function.PathFlags);
        this.arguments.Clear();
        for (var i = 0; i < count; i++)
        {
            this.instructionStarts[i] = this.validation.Instructions.Count;
            var cleanupPhase = body.Function.CodeContext.Compilation.IsTestBuild && body.Values[i].Kind != OwnershipValueKind.Phi &&
                (body.Operations[i].Kind == OwnershipOperationKind.Cleanup || (body.DeferredPlans.Count != 0 && this.deferredOwners[i] >= 0));
            if (cleanupPhase)
            {
                this.validation.Add(EmissionOpcode.TestPhaseEnter, i);
            }

            if (!this.LowerOperation(library, body, this.validation, constants, directory, i, marks, out failure))
            {
                return false;
            }

            this.AddPathFlags(body, this.validation, i);
            if (cleanupPhase && (this.validation.Instructions.Count == 0 || this.validation.Instructions[^1].Opcode is not (EmissionOpcode.Unreachable or EmissionOpcode.ReturnVoid or EmissionOpcode.ReturnScalar)))
            {
                this.validation.Add(EmissionOpcode.TestPhaseLeave, i);
            }
        }

        this.instructionStarts[count] = this.validation.Instructions.Count;
        Grow(ref this.flagValidation, count + body.Places.Count);
        if (!ValidateStringFlags(body, this.validation, this.flagValidation.AsSpan(0, count + body.Places.Count)) ||
            !ValidatePathFlags(body, this.validation, this.flagValidation.AsSpan(0, count)))
        {
            return Fail("Missing or inconsistent string lifetime flag updates.", out failure);
        }

        if (this.arguments.Count != 0)
        {
            return Fail("Incomplete call argument plan.", out failure);
        }

        if (this.hasMatches)
        {
            // A covered arm retains its complete checking plan, but contributes no
            // physical lifetime updates or otherwise unused flag slots.
            this.PruneMatchStorage(body, function, marks);
            for (var id = 0; id < count; id++)
            {
                if ((marks[id] & NormalMark) == 0)
                {
                    continue;
                }

                StringFlagTransition(body.Operations[id], out var clear, out var initialize);
                if (clear >= 0 && this.liveFlags[clear] != 0)
                {
                    this.liveFlags[clear] |= 4;
                }

                if (initialize >= 0 && this.liveFlags[initialize] != 0)
                {
                    this.liveFlags[initialize] |= 4;
                }
            }

            for (var i = function.LiveFlags.Count - 1; i >= 0; i--)
            {
                var place = function.LiveFlags[i];
                if ((this.liveFlags[place] & 4) == 0)
                {
                    this.liveFlags[place] = 0;
                    function.LiveFlags.RemoveAt(i);
                }
            }
        }

        function.AddScalar(EmissionOpcode.Branch, -1, [new(EmissionOperandKind.Block, 0)]);
        for (var i = 0; i < count; i++)
        {
            if (this.blocks[i] != i || body.Operations[i].Kind == OwnershipOperationKind.Exit)
            {
                continue;
            }

            function.Add(EmissionOpcode.Label, i);
            var cursor = i;
            while (true)
            {
                // Serialize each operation's prepared physical plan once, in CFG block order.
                for (var n = this.instructionStarts[cursor]; n < this.instructionStarts[cursor + 1]; n++)
                {
                    var instruction = this.validation.Instructions[n];
                    if (instruction.Opcode is EmissionOpcode.StoreLiveFlag or EmissionOpcode.InitializeLiveFlag && this.liveFlags[instruction.Place] == 0)
                    {
                        continue;
                    }

                    var start = function.Operands.Count;
                    function.Operands.AddRange(this.validation.GetOperands(instruction));
                    function.Instructions.Add(instruction with { OperandStart = start });
                    function.NeedsStringComparison |= instruction.Opcode is EmissionOpcode.StringEquals or EmissionOpcode.StringCompare ||
                        (instruction.Opcode == EmissionOpcode.StringPattern && instruction.Constant >= 0);
                    if (instruction.Opcode == EmissionOpcode.CompositePattern)
                    {
                        foreach (var test in instruction.Pattern!)
                        {
                            function.NeedsStringComparison |= test.Text >= 0;
                        }
                    }
                }

                if (body.Operations[cursor].Kind is OwnershipOperationKind.Deliver or OwnershipOperationKind.TestAbort ||
                    (body.Operations[cursor].Kind == OwnershipOperationKind.Call && ReferenceEquals(body.Operations[cursor].Source.BoundType, BoundType.Never)))
                {
                    break;
                }

                var next = this.successor[cursor];
                if (next >= 0 && next != i && this.blocks[next] == i)
                {
                    cursor = next;
                    continue;
                }

                if (next >= 0)
                {
                    function.AddScalar(EmissionOpcode.Branch, cursor, [new(EmissionOperandKind.Block, this.blocks[next])]);
                }
                else
                {
                    var yes = -1;
                    var no = -1;
                    for (var e = this.ExecutionHead(body, cursor); e >= 0; e = edges[e].Next)
                    {
                        var edge = edges[e];
                        if (edge.Kind == OwnershipEdgeKind.True)
                        {
                            yes = this.blocks[edge.To];
                        }

                        if (edge.Kind == OwnershipEdgeKind.False)
                        {
                            no = this.blocks[edge.To];
                        }
                    }

                    var condition = this.PhysicalOperandForBranch(body, cursor, ref yes, ref no);
                    function.AddScalar(EmissionOpcode.ConditionalBranch, cursor, [condition, new(EmissionOperandKind.Block, yes), new(EmissionOperandKind.Block, no)]);
                }

                break;
            }
        }

        return ((!this.hasMatches || ValidateStringFlags(body, function, this.flagValidation.AsSpan(0, count + body.Places.Count), marks)) &&
            ValidatePathFlags(body, function, this.flagValidation.AsSpan(0, count), marks)) ||
            Fail("Physical string flags do not match dispatch reachability.", out failure);
    }

    private bool LowerScalar(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (operation.Source.AttributeChain is not null)
        {
            return Fail("Scalar expression attributes need explicit lowering support.", out failure);
        }

        var type = operation.Place >= 0 ? body.Places[operation.Place].Type : operation.Source.BoundType;
        if (operation.Kind == OwnershipOperationKind.Branch && value.Kind == OwnershipValueKind.None)
        {
            return true;
        }

        if (operation.Kind == OwnershipOperationKind.Read && operation.Place >= 0 && body.Places[operation.Place].Kind == OwnershipPlaceKind.Subject)
        {
            return this.ValidateCandidateRead(body, id, out failure);
        }

        if (ReferenceEquals(type, BoundType.Unit))
        {
            return true;
        }

        if (!IsScalar(type!))
        {
            return Fail("Unsupported scalar operation Type.", out failure);
        }

        var representation = WindowsLowering.GetValue(type!)!;
        var llvm = representation.ComputationType;
        for (var n = 0; n < value.Count; n++)
        {
            var input = Input(body, id, n);
            if ((uint)input >= (uint)body.Values.Count)
            {
                return Fail("Missing scalar input value.", out failure);
            }

            if (value.Kind != OwnershipValueKind.Phi && body.IsReachable(id) && !this.Dominates(input, id))
            {
                return Fail("Scalar input does not dominate its use.", out failure);
            }
        }

        switch (operation.Kind)
        {
            case OwnershipOperationKind.Declare:
                return true;
            case OwnershipOperationKind.Read:
            case OwnershipOperationKind.Consume:
                if (body.Places[operation.Place].Kind == OwnershipPlaceKind.Parameter)
                {
                    return value.Kind == OwnershipValueKind.Alias || Fail("Parameter read has no incoming SSA value.", out failure);
                }

                if (body.Places[operation.Place].Kind is OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result)
                {
                    return value.Kind == OwnershipValueKind.Alias && value.Count == 1 &&
                        ValuePlace(body.Operations[Input(body, id, 0)]) == operation.Place
                        ? true : Fail("An acquired scalar read requires its own prepared SSA value.", out failure);
                }

                if (body.Places[operation.Place].Kind is not (OwnershipPlaceKind.Local or OwnershipPlaceKind.Parameter))
                {
                    return Fail("Scalar load requires local storage.", out failure);
                }

                function.AddScalar(EmissionOpcode.LoadScalar, id, [], llvm, place: operation.Place, representation: representation);
                return true;
            case OwnershipOperationKind.Write:
                if (body.Places[operation.Place].Kind == OwnershipPlaceKind.Result)
                {
                    return value.Count == 1 && (!body.IsReachable(id) || operation.Placement == PlacementKind.Initialization)
                        ? true : Fail("Result delivery requires a value and initialization placement.", out failure);
                }

                if (body.Places[operation.Place].Kind != OwnershipPlaceKind.Local || value.Count != 1)
                {
                    return Fail("Scalar result placement is not implemented.", out failure);
                }

                if (body.IsReachable(id) && ((uint)body.OperationSteps[id] >= (uint)body.CleanupSteps.Count || body.CleanupSteps[body.OperationSteps[id]].Operation != id || body.CleanupSteps[body.OperationSteps[id]].Place != operation.Place))
                {
                    return Fail("Missing replacement plan at Write.", out failure);
                }

                function.AddScalar(EmissionOpcode.StoreScalar, id, [this.PhysicalOperand(body, Input(body, id, 0))], llvm, place: operation.Place, representation: representation);
                return true;
        }

        if (value.Kind is OwnershipValueKind.Constant or OwnershipValueKind.Alias or OwnershipValueKind.Parameter)
        {
            return true;
        }

        if (value.Kind == OwnershipValueKind.Capture)
        {
            return this.LowerCapture(body, function, id, out failure);
        }

        if (value.Kind == OwnershipValueKind.Phi)
        {
            return this.LowerPhi(body, function, id, llvm, out failure);
        }

        if (value.Kind == OwnershipValueKind.Convert)
        {
            return this.LowerConversion(body, function, constants, directory, id, out failure);
        }

        if (value.Kind == OwnershipValueKind.PointerStore)
        {
            // SPEC 5.2: store the Copy value at the pointee alignment; the temporary itself is never read.
            var address = Input(body, id, 0);
            var stored = Input(body, id, 1);
            if (ValueType(body, address) is not { } pointerType || !ReferenceTypes.IsPointer(pointerType) || !ReferenceEquals(pointerType.Components[0], type) ||
                !ReferenceEquals(ValueType(body, stored), type) || ReferenceEquals(type, BoundType.Boolean) || WindowsLowering.GetValue(type!) is not { } storage)
            {
                return Fail("Unsupported pointer write.", out failure);
            }

            var start = function.Operands.Count;
            function.Operands.Add(this.PhysicalOperand(body, stored));
            function.Operands.Add(this.PhysicalOperand(body, address));
            function.Instructions.Add(new(EmissionOpcode.StorePointer, id, OperandStart: start, OperandCount: 2, ScalarType: storage.ComputationType, Representation: storage));
            return true;
        }

        if (value.Kind == OwnershipValueKind.PointerLoad)
        {
            // SPEC 5.2: a Copy read of the pointee with its own alignment and no invented attributes.
            var address = Input(body, id, 0);
            if (ValueType(body, address) is not { } pointerType || !ReferenceTypes.IsPointer(pointerType) || !ReferenceEquals(pointerType.Components[0], type) ||
                ReferenceEquals(type, BoundType.Boolean) || WindowsLowering.GetValue(type!) is not { } loaded)
            {
                return Fail("Unsupported pointer read.", out failure);
            }

            var start = function.Operands.Count;
            function.Operands.Add(this.PhysicalOperand(body, address));
            function.Instructions.Add(new(EmissionOpcode.LoadPointer, id, OperandStart: start, OperandCount: 1, ScalarType: loaded.ComputationType, Representation: loaded));
            return true;
        }

        if (value.Kind is not (OwnershipValueKind.Binary or OwnershipValueKind.Unary))
        {
            return Fail("Missing scalar computation.", out failure);
        }

        var first = Input(body, id, 0);
        var operandType = ValueType(body, first)!;
        if (ReferenceTypes.IsPointer(operandType) && value.Operator is KotoKind.Plus or KotoKind.Minus)
        {
            // SPEC 5.3: p + n and p - n displace by n * stride(T) with an isize count.
            var stride = FunctionAbi.GetValue(operandType.Components[0], this.aggregateLayouts)?.Layout.Stride ?? 0;
            if (value.Kind != OwnershipValueKind.Binary || stride <= 0 ||
                !ReferenceEquals(type, operandType) || !ReferenceEquals(ValueType(body, Input(body, id, 1)), BoundType.ISize))
            {
                return Fail("Unsupported pointer operation.", out failure);
            }

            var start = function.Operands.Count;
            function.Operands.Add(this.PhysicalOperand(body, first));
            function.Operands.Add(this.PhysicalOperand(body, Input(body, id, 1)));
            function.Instructions.Add(new(EmissionOpcode.PointerOffset, id, Constant: value.Operator == KotoKind.Plus ? stride : -stride, OperandStart: start, OperandCount: 2, ScalarType: "ptr"));
            return true;
        }

        if (FloatingTypes.Supports(operandType))
        {
            return this.LowerFloating(body, function, id, type!, operandType, out failure);
        }

        var integer = ScalarTypes.Width(operandType) != 0;
        if (ScalarTypes.Width(operandType) == 128 && value.Operator is KotoKind.Slash or KotoKind.Percent)
        {
            return Fail("The initial profile does not supply 128-bit division or remainder.", out failure);
        }

        var signed = integer && ScalarTypes.Signed(operandType);
        var op = value.Operator switch
        {
            KotoKind.Plus => signed ? "sadd" : "uadd", KotoKind.Minus or KotoKind.PrefixMinus => signed ? "ssub" : "usub", KotoKind.Asterisk => signed ? "smul" : "umul",
            KotoKind.Slash => signed ? "sdiv" : "udiv", KotoKind.Percent => signed ? "srem" : "urem",
            KotoKind.Ampersand => "and", KotoKind.Bar => "or", KotoKind.Caret => "xor",
            KotoKind.LessThanLessThan => "shl", KotoKind.GreaterThanGreaterThan => signed ? "ashr" : "lshr",
            KotoKind.EqualsEquals => "eq", KotoKind.ExclamationEquals => "ne",
            KotoKind.LessThan => signed ? "slt" : "ult", KotoKind.LessThanEquals => signed ? "sle" : "ule", KotoKind.GreaterThan => signed ? "sgt" : "ugt", KotoKind.GreaterThanEquals => signed ? "sge" : "uge",
            KotoKind.Not => "xor", KotoKind.PrefixPlus => "add",
            _ => null,
        };
        if (op is null || value.Count != (value.Kind == OwnershipValueKind.Binary ? 2 : 1))
        {
            return Fail("Unsupported scalar operator.", out failure);
        }

        var comparison = op is "eq" or "ne" or "slt" or "sle" or "sgt" or "sge" or "ult" or "ule" or "ugt" or "uge";
        var character = ReferenceEquals(operandType, BoundType.Char);
        if (!IsScalar(operandType) ||
            (character ? !comparison : !integer && value.Operator is not (KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.Not)))
        {
            return Fail("Unsupported scalar operand Type.", out failure);
        }

        var shift = value.Operator is KotoKind.LessThanLessThan or KotoKind.GreaterThanGreaterThan;
        if (!ReferenceEquals(type, comparison ? BoundType.Boolean : operandType) ||
            (value.Kind == OwnershipValueKind.Binary && (shift
                ? ScalarTypes.Width(ValueType(body, Input(body, id, 1))) == 0
                : !ReferenceEquals(operandType, ValueType(body, Input(body, id, 1))))) ||
            ((value.Kind == OwnershipValueKind.Unary) != (value.Operator is KotoKind.Not or KotoKind.PrefixPlus or KotoKind.PrefixMinus)) ||
            (value.Operator == KotoKind.Not && !ReferenceEquals(operandType, BoundType.Boolean)) ||
            (value.Operator == KotoKind.PrefixMinus && !signed))
        {
            return Fail("Inconsistent scalar operator or operand Types.", out failure);
        }

        var leftOperand = value.Operator == KotoKind.PrefixMinus ? new(EmissionOperandKind.Integer, 0) : this.PhysicalOperand(body, first);
        var rightOperand = value.Kind == OwnershipValueKind.Binary ? this.PhysicalOperand(body, Input(body, id, 1)) : value.Operator == KotoKind.PrefixMinus ? this.PhysicalOperand(body, first) : new(EmissionOperandKind.Integer, value.Operator == KotoKind.Not ? 1 : 0);
        var location = -1;
        var check = this.checks[id];
        if (check != ArithmeticCheckKind.None && !this.TryGetLocation(operation.Source, directory, constants, out location))
        {
            return Fail("Arithmetic check has no source location.", out failure);
        }

        var operandRepresentation = WindowsLowering.GetValue(operandType)!;
        var countRepresentation = shift ? WindowsLowering.GetValue(ValueType(body, Input(body, id, 1))!) : null;
        function.AddScalar(EmissionOpcode.Scalar, id, [leftOperand, rightOperand], operandRepresentation.ComputationType, op, place: body.Operations.Count + id, location: location, check: check, comparison: comparison, representation: operandRepresentation, countRepresentation: countRepresentation);
        return true;
    }
}
