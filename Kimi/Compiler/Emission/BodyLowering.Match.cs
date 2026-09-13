// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<OwnershipEdge> executionEdges = new();
    private readonly HashSet<long> matchNumbers = new();
    private readonly HashSet<string> matchTexts = new(StringComparer.Ordinal);
    private int[] executionHeads = [];
    private int[] matchTests = [];
    private int[] matchPlaces = [];
    private int[] subjectInitializers = [];
    private int[] logicalIncoming = [];
    private int[] patternAcquisitions = [];
    private int[] slotUses = [];
    private bool hasMatches;

    private void PruneMatchStorage(OwnershipBody body, EmissionFunction function, ReadOnlySpan<byte> marks)
    {
        Grow(ref this.slotUses, body.Places.Count);
        this.slotUses.AsSpan(0, body.Places.Count).Clear();
        foreach (var instruction in this.validation.Instructions)
        {
            if ((marks[instruction.Operation] & NormalMark) == 0)
            {
                continue;
            }

            if (instruction.Place >= 0 && instruction.Opcode is EmissionOpcode.LoadScalar or EmissionOpcode.StoreScalar or EmissionOpcode.MoveString or EmissionOpcode.DestroyStringIfLive or EmissionOpcode.StoreStaticString or EmissionOpcode.StringPattern)
            {
                this.UseMatchStorage(function, instruction.Place);
            }

            foreach (var operand in this.validation.GetOperands(instruction))
            {
                if (operand.Kind == EmissionOperandKind.SlotAddress)
                {
                    this.UseMatchStorage(function, (int)operand.Value);
                }
            }
        }

        for (var i = function.Slots.Count - 1; i >= 0; i--)
        {
            if (this.slotUses[function.Slots[i].Place] == 0)
            {
                function.Slots.RemoveAt(i);
            }
        }
    }

    private void UseMatchStorage(EmissionFunction function, int place)
    {
        var address = function.SlotAddresses[place];
        if (address.Kind == EmissionOperandKind.SlotAddress)
        {
            this.slotUses[(int)address.Value] = 1;
        }
    }

    private int LogicalIncoming(int id) => this.hasMatches ? this.logicalIncoming[id] : this.incoming[id];

    private EmissionOperand PhysicalOperandForBranch(OwnershipBody body, int id, ref int yes, ref int no)
    {
        if (body.Operations[id].Kind != OwnershipOperationKind.PatternTest)
        {
            return this.PhysicalOperand(body, Input(body, id, 0));
        }

        var arm = body.MatchArms[this.matchTests[id]];
        var match = body.Matches[arm.Match];
        if (ReferenceEquals(body.Places[match.Subject].Type, BoundType.Boolean))
        {
            if (match.Binding.Positions[arm.Pattern].Literal.Magnitude == 0)
            {
                (yes, no) = (no, yes);
            }

            return this.PhysicalOperand(body, Input(body, this.subjectInitializers[match.Subject], 0));
        }

        return new(EmissionOperandKind.Value, id);
    }

    private IReadOnlyList<OwnershipEdge> ExecutionEdges(OwnershipBody body) => this.hasMatches ? this.executionEdges : body.Edges;

    private int ExecutionHead(OwnershipBody body, int operation) => this.hasMatches ? this.executionHeads[operation] : body.EdgeHeads[operation];

    private void AddExecutionEdge(int from, int to, OwnershipEdgeKind kind)
    {
        var index = this.executionEdges.Count;
        this.executionEdges.Add(new(from, to, kind, this.executionHeads[from]));
        this.executionHeads[from] = index;
    }

    // Only dispatch edges change. Pattern testing has no ownership or Loan effects;
    // every selected arm therefore starts in the same state as its verification edge.
    private bool PrepareMatches(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        this.hasMatches = body.Matches.Count != 0;
        if (!this.hasMatches)
        {
            return true;
        }

        Grow(ref this.matchPlaces, body.Places.Count);
        Grow(ref this.subjectInitializers, body.Places.Count);
        Grow(ref this.matchTests, body.Operations.Count);
        Grow(ref this.patternAcquisitions, body.Operations.Count);
        this.patternAcquisitions.AsSpan(0, body.Operations.Count).Clear();
        this.matchPlaces.AsSpan(0, body.Places.Count).Clear();
        this.subjectInitializers.AsSpan(0, body.Places.Count).Fill(-1);
        this.matchTests.AsSpan(0, body.Operations.Count).Fill(-1);
        Grow(ref this.executionHeads, body.Operations.Count);
        Grow(ref this.logicalIncoming, body.Operations.Count);
        this.executionEdges.Clear();
        for (var e = 0; e < body.Edges.Count; e++)
        {
            this.executionEdges.Add(body.Edges[e]);
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            this.executionHeads[id] = body.EdgeHeads[id];
            var operation = body.Operations[id];
            if (operation.Kind == OwnershipOperationKind.InitializeSubject)
            {
                if ((uint)operation.Place >= (uint)body.Places.Count || (uint)operation.Input >= (uint)body.Places.Count ||
                    this.subjectInitializers[operation.Place] >= 0 || body.Places[operation.Place].Kind != OwnershipPlaceKind.Subject ||
                    body.Places[operation.Input].Kind is not (OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ||
                    !ReferenceEquals(body.Places[operation.Input].Type, body.Places[operation.Place].Type))
                {
                    return Fail("Invalid match Subject acquisition.", out failure);
                }

                this.subjectInitializers[operation.Place] = id;
                // Ownership changes identity; the already acquired storage is reused.
                function.SlotAddresses[operation.Place] = function.SlotAddresses[operation.Input];
            }
        }

        for (var m = 0; m < body.Matches.Count; m++)
        {
            var match = body.Matches[m];
            var binding = match.Binding;
            if (!binding.IsCurrent || binding.Coverage.State != MatchCoverageState.Exhaustive || match.ArmCount <= 0 ||
                match.ArmCount != binding.Arms.Count || match.ArmStart < 0 || match.ArmStart > body.MatchArms.Count - match.ArmCount ||
                (uint)match.Subject >= (uint)body.Places.Count || this.matchPlaces[match.Subject] != 0 || this.subjectInitializers[match.Subject] < 0 ||
                (!IsScalar(body.Places[match.Subject].Type) && !ReferenceEquals(body.Places[match.Subject].Type, BoundType.Unit) && !ReferenceEquals(body.Places[match.Subject].Type, BoundType.String)))
            {
                return Fail("Unsupported or inconsistent match plan.", out failure);
            }

            this.matchPlaces[match.Subject] = 1;
            var initializer = this.subjectInitializers[match.Subject];
            var dispatchEdge = body.EdgeHeads[initializer];
            if (dispatchEdge < 0 || body.Edges[dispatchEdge].Next >= 0)
            {
                return Fail("Subject acquisition has no unique dispatch.", out failure);
            }

            var dispatch = body.Edges[dispatchEdge].To;
            if (body.Operations[dispatch].Kind != OwnershipOperationKind.MatchDispatch || body.Operations[dispatch].Place != match.Subject ||
                body.OperationSteps[dispatch] != m || !ReferenceEquals(body.Operations[dispatch].Source, binding.Syntax) || !this.PureMatchTest(body, dispatch))
            {
                return Fail("Match dispatch changes the verified Subject state.", out failure);
            }

            var edgeCount = 0;
            for (var e = body.EdgeHeads[dispatch]; e >= 0; e = body.Edges[e].Next)
            {
                var edge = body.Edges[e];
                if (edge.Kind != OwnershipEdgeKind.MatchArm || body.Operations[edge.To].Kind != OwnershipOperationKind.PatternTest ||
                    body.OperationSteps[edge.To] < match.ArmStart || body.OperationSteps[edge.To] >= match.ArmStart + match.ArmCount || this.matchTests[edge.To] != -1)
                {
                    return Fail("Invalid verification arm edge.", out failure);
                }

                this.matchTests[edge.To] = -2;
                edgeCount++;
            }

            if (edgeCount != match.ArmCount)
            {
                return Fail("Missing verification arm edge.", out failure);
            }

            this.executionHeads[dispatch] = -1;
            this.matchNumbers.Clear();
            this.matchTexts.Clear();
            var previous = dispatch;
            var finished = false;
            var booleanMask = 0;
            for (var n = 0; n < match.ArmCount; n++)
            {
                var armIndex = match.ArmStart + n;
                var arm = body.MatchArms[armIndex];
                if (arm.Match != m || arm.Pattern != binding.Arms[n].Pattern || (uint)arm.Pattern >= (uint)binding.Positions.Count ||
                    (uint)arm.Test >= (uint)body.Operations.Count || this.matchTests[arm.Test] != -2 || arm.DecompositionCount != 0 || binding.Arms[n].Syntax.Guard is not null)
                {
                    return Fail("Unsupported match arm or guard.", out failure);
                }

                var pattern = binding.Positions[arm.Pattern];
                if (pattern.Parent != -1 || pattern.End != arm.Pattern + 1 || pattern.AccessMode != PatternAccessMode.Owned || pattern.ImplicitDeref != PatternImplicitDeref.None ||
                    !ReferenceEquals(pattern.MatchedType, body.Places[match.Subject].Type) ||
                    body.Operations[arm.Test].Kind != OwnershipOperationKind.PatternTest || body.Operations[arm.Test].Place != match.Subject ||
                    body.OperationSteps[arm.Test] != armIndex || !ReferenceEquals(KotoHelper.UnwrapParentheses(body.Operations[arm.Test].Source), pattern.Source) || !this.PureMatchTest(body, arm.Test))
                {
                    return Fail("Pattern test is not a pure whole-Subject inspection.", out failure);
                }

                this.matchTests[arm.Test] = armIndex;
                var unconditional = pattern.Kind is BoundPatternKind.Wildcard or BoundPatternKind.Binding or BoundPatternKind.Unit;
                var duplicate = false;
                if (pattern.Kind == BoundPatternKind.Literal)
                {
                    if (pattern.Literal.Kind == PatternLiteralKind.String && ReferenceEquals(pattern.MatchedType, BoundType.String) && pattern.Literal.Text is { } text)
                    {
                        duplicate = !this.matchTexts.Add(text);
                    }
                    else if (pattern.Literal.Kind == PatternLiteralKind.Boolean && ReferenceEquals(pattern.MatchedType, BoundType.Boolean) && pattern.Literal.Magnitude <= 1)
                    {
                        var bit = 1 << (int)pattern.Literal.Magnitude;
                        duplicate = (booleanMask & bit) != 0;
                        unconditional = !duplicate && booleanMask != 0;
                        booleanMask |= bit;
                    }
                    else if (pattern.Literal.Kind == PatternLiteralKind.Integer && ScalarTypes.TryLiteral(pattern.MatchedType, pattern.Literal.Magnitude, pattern.Literal.Negative, this.pointerWidth, out var bits))
                    {
                        duplicate = !this.matchNumbers.Add(bits);
                    }
                    else
                    {
                        return Fail("Unsupported or invalid pattern literal.", out failure);
                    }
                }
                else if (!unconditional || (pattern.Kind == BoundPatternKind.Unit && !ReferenceEquals(pattern.MatchedType, BoundType.Unit)))
                {
                    return Fail("Unsupported decomposition pattern.", out failure);
                }

                var success = body.EdgeHeads[arm.Test];
                if (success < 0 || body.Edges[success].Next >= 0 || body.Edges[success].Kind != OwnershipEdgeKind.Normal)
                {
                    return Fail("Pattern test has no unique success entry.", out failure);
                }

                if (pattern.Kind == BoundPatternKind.Binding)
                {
                    var expectedAcquisition = ReferenceEquals(pattern.MatchedType, BoundType.String) ? PatternAcquisition.Move : PatternAcquisition.Copy;
                    var declaration = body.Edges[success].To;
                    var next = body.EdgeHeads[declaration];
                    if (pattern.Acquisition != expectedAcquisition || pattern.BodySymbol is null || !body.SymbolPlaces.TryGetValue(pattern.BodySymbol, out var local) ||
                        body.Operations[declaration].Kind != OwnershipOperationKind.Declare || body.Operations[declaration].Place != local ||
                        next < 0 || body.Edges[next].Next >= 0 || body.Edges[next].Kind != OwnershipEdgeKind.Normal)
                    {
                        return Fail("Pattern binding is not declared on its success edge.", out failure);
                    }

                    var acquisition = body.Edges[next].To;
                    var acquire = body.Operations[acquisition];
                    if (acquire.Kind != OwnershipOperationKind.AcquirePattern || acquire.Place != match.Subject || acquire.Input != local ||
                        !ReferenceEquals(acquire.Source, pattern.Source) || this.patternAcquisitions[acquisition] != 0)
                    {
                        return Fail("Pattern acquisition is not owned by its selected binding.", out failure);
                    }

                    this.patternAcquisitions[acquisition] = match.Subject + 1;
                }

                if (finished || duplicate)
                {
                    continue; // Still validated and lowered into checking scratch, never serialized.
                }

                this.AddExecutionEdge(previous, arm.Test, previous == dispatch ? OwnershipEdgeKind.Normal : OwnershipEdgeKind.False);
                if (unconditional)
                {
                    finished = true; // Coverage proves success; no synthetic failure/unreachable block.
                }
                else
                {
                    this.executionEdges[success] = body.Edges[success] with { Kind = OwnershipEdgeKind.True };
                    previous = arm.Test;
                }
            }

            if (!finished)
            {
                return Fail("Match dispatch lacks a proven final arm.", out failure);
            }
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            var operation = body.Operations[id];
            if (operation.Kind == OwnershipOperationKind.AcquirePattern)
            {
                if ((uint)operation.Place >= (uint)body.Places.Count || this.matchPlaces[operation.Place] != 1 || this.patternAcquisitions[id] != operation.Place + 1 || (uint)operation.Input >= (uint)body.Places.Count ||
                    body.Places[operation.Input].Kind != OwnershipPlaceKind.Local || !ReferenceEquals(body.Places[operation.Input].Source, operation.Source) ||
                    !ReferenceEquals(body.Places[operation.Input].Type, body.Places[operation.Place].Type))
                {
                    return Fail("Invalid selected pattern binding.", out failure);
                }

                this.matchPlaces[operation.Input] = 2;
            }
        }

        return true;
    }

    private bool PureMatchTest(OwnershipBody body, int id) => body.Values[id].Kind == OwnershipValueKind.None && body.Operations[id].Input == -1 &&
        (body.LoanInputs.Count == 0 || body.LoanInputs[id] == body.LoanStates[id]);

    private bool LowerMatchOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (!this.hasMatches || operation.Place < 0 || this.matchPlaces[operation.Place] != 1 || operation.Source.AttributeChain is not null)
        {
            return Fail("Match operation has no verified Subject.", out failure);
        }

        var type = body.Places[operation.Place].Type;
        if (operation.Kind == OwnershipOperationKind.InitializeSubject)
        {
            if (this.subjectInitializers[operation.Place] != id || (body.IsReachable(id) && ((body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0 ||
                (body.GetInputState(id, operation.Place) & PlaceState.MayInit) != 0)))
            {
                return Fail("Subject is not acquired into fresh storage.", out failure);
            }

            if (IsScalar(type) && (body.Values[id].Kind != OwnershipValueKind.Alias || body.Values[id].Count != 1 ||
                ValuePlace(body.Operations[Input(body, id, 0)]) != operation.Input || (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id))))
            {
                return Fail("Scalar Subject does not retain its acquired value.", out failure);
            }

            this.AddStringFlags(function, operation, id);
            return true;
        }

        if (operation.Kind == OwnershipOperationKind.AcquirePattern)
        {
            var expected = ReferenceEquals(type, BoundType.String) ? AcquisitionKind.Move : AcquisitionKind.Copy;
            if (operation.Acquisition != expected || this.matchPlaces[operation.Input] != 2 || (body.IsReachable(id) &&
                ((body.GetInputState(id, operation.Place) & PlaceState.MustInit) == 0 || (body.GetInputState(id, operation.Input) & PlaceState.MayInit) != 0)))
            {
                return Fail("Pattern binding has invalid acquisition responsibility.", out failure);
            }

            if (ReferenceEquals(type, BoundType.String))
            {
                function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.SlotAddress, operation.Place)], place: operation.Input);
                this.AddStringFlags(function, operation, id);
            }
            else if (IsScalar(type))
            {
                if (body.Values[id].Kind != OwnershipValueKind.Alias || body.Values[id].Count != 1 ||
                    Input(body, id, 0) != this.subjectInitializers[operation.Place] || (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
                {
                    return Fail("Scalar pattern binding does not copy its Subject snapshot.", out failure);
                }

                var layout = WindowsLowering.GetValue(type)!;
                function.AddScalar(EmissionOpcode.StoreScalar, id, [this.PhysicalOperand(body, Input(body, id, 0))], layout.ComputationType, place: operation.Input, representation: layout);
            }

            return true;
        }

        if (operation.Kind == OwnershipOperationKind.MatchDispatch || this.successor[id] >= 0 || this.blocks[id] < 0)
        {
            return true;
        }

        var arm = body.MatchArms[this.matchTests[id]];
        var pattern = body.Matches[arm.Match].Binding.Positions[arm.Pattern];
        if (ReferenceEquals(type, BoundType.String))
        {
            var text = pattern.Literal.Text!;
            var constant = text.Length == 0 ? -1 : constants.Intern(text, LlvmConstantKind.Text);
            function.Add(EmissionOpcode.StringPattern, id, operation.Place, constant);
        }
        else if (!ReferenceEquals(type, BoundType.Boolean))
        {
            ScalarTypes.TryLiteral(type, pattern.Literal.Magnitude, pattern.Literal.Negative, this.pointerWidth, out var bits);
            function.AddScalar(EmissionOpcode.Scalar, id, [this.PhysicalOperand(body, Input(body, this.subjectInitializers[operation.Place], 0)), new(EmissionOperandKind.Integer, bits)], WindowsLowering.GetValue(type)!.ComputationType, "eq", comparison: true);
        }

        return true;
    }
}
