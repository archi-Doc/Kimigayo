// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<OwnershipEdge> executionEdges = new();
    private readonly HashSet<Int128> matchNumbers = new();
    private readonly HashSet<string> matchTexts = new(StringComparer.Ordinal);
    private int[] executionHeads = [];
    private int[] matchTests = [];
    private int[] matchPlaces = [];
    private int[] subjectInitializers = [];
    private int[] logicalIncoming = [];
    private int[] patternAcquisitions = [];
    private int[] patternDecompositions = [];
    private int[] slotUses = [];
    private bool hasMatches;

    private void PruneMatchStorage(OwnershipBody body, EmissionFunction function, ReadOnlySpan<byte> marks)
    {
        Grow(ref this.slotUses, function.SlotAddresses.Count);
        this.slotUses.AsSpan(0, function.SlotAddresses.Count).Clear();
        foreach (var instruction in this.validation.Instructions)
        {
            if ((marks[instruction.Operation] & NormalMark) == 0)
            {
                continue;
            }

            if (instruction.Place >= 0 && instruction.Opcode is EmissionOpcode.LoadScalar or EmissionOpcode.StoreScalar or EmissionOpcode.MoveString or EmissionOpcode.DestroyStringIfLive or EmissionOpcode.StoreStaticString or EmissionOpcode.StringPattern or EmissionOpcode.CompositePattern or EmissionOpcode.PatternRead or EmissionOpcode.TransferAggregate or EmissionOpcode.FillArray or EmissionOpcode.DestroyAggregate)
            {
                this.UseMatchStorage(function, instruction.Place);
            }

            if (instruction.Opcode is EmissionOpcode.TransferAggregate or EmissionOpcode.FillArray && instruction.OperandCount == 0)
            {
                this.UseMatchStorage(function, instruction.Constant);
            }

            foreach (var operand in this.validation.GetOperands(instruction))
            {
                if (operand.Kind == EmissionOperandKind.SlotAddress)
                {
                    this.UseMatchStorage(function, (int)operand.Value);
                }
            }
        }

        for (var i = function.Subslots.Count - 1; i >= 0; i--)
        {
            var slot = function.Subslots[i];
            if (this.slotUses[slot.Place] == 0)
            {
                function.Subslots.RemoveAt(i);
            }
            else if (slot.Parent >= 0)
            {
                // Parent -1 denotes the dedicated init/deinit receiver address,
                // which has no local storage slot to retain.
                this.UseMatchStorage(function, slot.Parent);
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
        if (address.Kind is EmissionOperandKind.SlotAddress or EmissionOperandKind.ProjectedSlot)
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

    // Pure Pattern tests may be pruned. Guard evaluation and cleanup retain their
    // verification edges and effects before either runtime continuation.
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
        Grow(ref this.patternDecompositions, body.Operations.Count);
        this.patternDecompositions.AsSpan(0, body.Operations.Count).Clear();
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
                (!IsScalar(body.Places[match.Subject].Type) && !ReferenceEquals(body.Places[match.Subject].Type, BoundType.Unit) && !ReferenceEquals(body.Places[match.Subject].Type, BoundType.String) && !this.IsCompositeSubject(body.Places[match.Subject].Type)))
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

            var first = body.EdgeHeads[dispatch];
            if (first < 0 || body.Edges[first].Next >= 0 || body.Edges[first].Kind != OwnershipEdgeKind.MatchArm || body.Edges[first].To != body.MatchArms[match.ArmStart].Test)
            {
                return Fail("Invalid verification dispatch entry.", out failure);
            }

            this.executionEdges[first] = body.Edges[first] with { Kind = OwnershipEdgeKind.Normal };
            this.matchNumbers.Clear();
            this.matchTexts.Clear();
            var finished = false;
            var booleanMask = 0;
            for (var n = 0; n < match.ArmCount; n++)
            {
                var armIndex = match.ArmStart + n;
                var arm = body.MatchArms[armIndex];
                if (arm.Match != m || arm.Pattern != binding.Arms[n].Pattern || (uint)arm.Pattern >= (uint)binding.Positions.Count ||
                    (uint)arm.Test >= (uint)body.Operations.Count || this.matchTests[arm.Test] != -1)
                {
                    return Fail("Unsupported match arm or guard.", out failure);
                }

                var pattern = binding.Positions[arm.Pattern];
                var composite = this.IsCompositeSubject(this.Matched(pattern.MatchedType));
                if (pattern.Parent != -1 || (!composite && (pattern.End != arm.Pattern + 1 || arm.DecompositionCount != 0)) || pattern.AccessMode != PatternAccessMode.Owned || pattern.ImplicitDeref != PatternImplicitDeref.None ||
                    !ReferenceEquals(this.Matched(pattern.MatchedType), body.Places[match.Subject].Type) ||
                    body.Operations[arm.Test].Kind != OwnershipOperationKind.PatternTest || body.Operations[arm.Test].Place != match.Subject ||
                    body.OperationSteps[arm.Test] != armIndex || !ReferenceEquals(KotoHelper.UnwrapParentheses(body.Operations[arm.Test].Source), pattern.Source) || !this.PureMatchTest(body, arm.Test))
                {
                    return Fail("Pattern test is not a pure whole-Subject inspection.", out failure);
                }

                this.matchTests[arm.Test] = armIndex;
                var guarded = binding.Arms[n].Syntax.Guard is not null;
                if (guarded && !MatchTypes.SupportsGuard(binding, arm.Pattern))
                {
                    return Fail("Unsupported guarded Subject Type.", out failure);
                }

                var unconditional = pattern.Kind is BoundPatternKind.Wildcard or BoundPatternKind.Binding or BoundPatternKind.Unit;
                var duplicate = false;
                if (composite)
                {
                    if (!this.ValidateCompositePattern(binding, arm.Pattern) || !this.PrepareCompositeAcquisitions(body, arm, match.Subject, out failure))
                    {
                        return Fail(failure ?? "Unsupported or inconsistent composite Pattern.", out failure);
                    }

                    // Exhaustive Binding proves the remaining domain reaches the final
                    // unguarded arm. Earlier tests retain source-order short circuiting.
                    unconditional |= !guarded && n == match.ArmCount - 1;
                }
                else if (pattern.Kind == BoundPatternKind.Literal)
                {
                    if (pattern.Literal.Kind == PatternLiteralKind.String && ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.String) && pattern.Literal.Text is { } text)
                    {
                        duplicate = guarded ? this.matchTexts.Contains(text) : !this.matchTexts.Add(text);
                    }
                    else if (pattern.Literal.Kind == PatternLiteralKind.Boolean && ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.Boolean) && pattern.Literal.Magnitude <= 1)
                    {
                        var bit = 1 << (int)pattern.Literal.Magnitude;
                        duplicate = (booleanMask & bit) != 0;
                        unconditional = !guarded && !duplicate && booleanMask != 0;
                        if (!guarded)
                        {
                            booleanMask |= bit;
                        }
                    }
                    else if (this.TryMatchNumber(pattern, out var bits))
                    {
                        duplicate = guarded ? this.matchNumbers.Contains(bits) : !this.matchNumbers.Add(bits);
                    }
                    else
                    {
                        return Fail("Unsupported or invalid pattern literal.", out failure);
                    }
                }
                else if (!unconditional || (pattern.Kind == BoundPatternKind.Unit && !ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.Unit)))
                {
                    return Fail("Unsupported decomposition pattern.", out failure);
                }

                var nextTest = n + 1 < match.ArmCount ? body.MatchArms[armIndex + 1].Test : -1;
                var success = this.MatchSuccess(body, arm.Test, nextTest);
                if (success < 0 || (uint)arm.BodyEntry >= (uint)body.Operations.Count || body.Operations[arm.BodyEntry].Kind != OwnershipOperationKind.Branch ||
                    !ReferenceEquals(body.Operations[arm.BodyEntry].Source, binding.Arms[n].Syntax.Body))
                {
                    return Fail("Pattern test has invalid verification continuations.", out failure);
                }

                if (guarded)
                {
                    var guard = binding.Arms[n].Syntax.Guard!;
                    if (ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.String) &&
                        ((uint)arm.GuardLoan >= (uint)body.ComparisonLoans.Count || body.ComparisonLoans[arm.GuardLoan].Guard != armIndex))
                    {
                        return Fail("String guard has no Subject protection plan.", out failure);
                    }

                    if ((uint)arm.GuardEntry >= (uint)body.Operations.Count || body.Edges[success].To != arm.GuardEntry ||
                        body.Operations[arm.GuardEntry].Kind != OwnershipOperationKind.Branch || !ReferenceEquals(body.Operations[arm.GuardEntry].Source, guard))
                    {
                        return Fail("Pattern success does not enter its guard.", out failure);
                    }

                    if (arm.GuardBranch >= 0)
                    {
                        var selected = this.MatchSuccess(body, arm.GuardBranch, nextTest);
                        if (selected < 0 || body.Edges[selected].To != arm.BodyEntry || body.Operations[arm.GuardBranch].Kind != OwnershipOperationKind.Branch ||
                            !ReferenceEquals(body.Operations[arm.GuardBranch].Source, guard) || body.Values[arm.GuardBranch].Kind != OwnershipValueKind.Alias)
                        {
                            return Fail("Guard does not select its body after evaluation and cleanup.", out failure);
                        }

                        if (arm.GuardValue < 0 || Input(body, arm.GuardBranch, 0) != arm.GuardValue || arm.GuardCleanupStart <= arm.GuardEntry || arm.GuardCleanupStart > arm.GuardBranch)
                        {
                            return Fail("Guard does not retain its pre-cleanup Boolean.", out failure);
                        }

                        for (var cleanup = arm.GuardCleanupStart; cleanup < arm.GuardBranch; cleanup++)
                        {
                            var next = body.EdgeHeads[cleanup];
                            if (body.Operations[cleanup].Kind is not (OwnershipOperationKind.Cleanup or OwnershipOperationKind.EndComparisonLoans) || !ReferenceEquals(body.Operations[cleanup].Source, guard) ||
                                next < 0 || body.Edges[next].Next >= 0 || body.Edges[next].Kind != OwnershipEdgeKind.Normal || body.Edges[next].To != cleanup + 1)
                            {
                                return Fail("Guard continuation bypasses temporary cleanup.", out failure);
                            }
                        }

                        if (arm.GuardLoan >= 0 && (body.HasComparisonLoan(arm.GuardBranch, arm.GuardLoan) ||
                            body.Operations[arm.GuardBranch - 1].Kind != OwnershipOperationKind.EndComparisonLoans ||
                            !body.HasComparisonLoan(arm.GuardBranch - 1, arm.GuardLoan)))
                        {
                            return Fail("Guard protection must end after cleanup and before acquisition.", out failure);
                        }
                    }
                }
                else if (arm.GuardEntry != -1 || arm.GuardBranch != -1 || arm.GuardValue != -1 || arm.GuardCleanupStart != -1 || body.Edges[success].To != arm.BodyEntry)
                {
                    return Fail("Unguarded Pattern has an unexpected guard plan.", out failure);
                }

                if (!composite && pattern.Kind == BoundPatternKind.Binding)
                {
                    var expectedAcquisition = ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.String) ? PatternAcquisition.Move : PatternAcquisition.Copy;
                    var entry = body.EdgeHeads[arm.BodyEntry];
                    if (entry < 0 || body.Edges[entry].Next >= 0 || body.Edges[entry].Kind != OwnershipEdgeKind.Normal)
                    {
                        return Fail("Selected binding has no unique declaration entry.", out failure);
                    }

                    var declaration = body.Edges[entry].To;
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

                this.executionHeads[arm.Test] = -1;
                if (finished || duplicate)
                {
                    if (nextTest >= 0)
                    {
                        this.AddExecutionEdge(arm.Test, nextTest, OwnershipEdgeKind.Normal);
                    }

                    continue; // Still validated and lowered into checking scratch, never serialized.
                }

                this.AddExecutionEdge(arm.Test, body.Edges[success].To, unconditional ? OwnershipEdgeKind.Normal : OwnershipEdgeKind.True);
                if (unconditional)
                {
                    finished |= !guarded; // A true Pattern does not prove a true guard.
                }
                else
                {
                    if (nextTest < 0)
                    {
                        return Fail("Conditional final Pattern has no continuation.", out failure);
                    }

                    this.AddExecutionEdge(arm.Test, nextTest, OwnershipEdgeKind.False);
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

    // A Pattern position's matched Type as the lowered body sees it; a monomorphized instance sees its substitution (SPEC 21.3.1).
    private BoundType Matched(BoundType? type) => SignatureType(this, type)!;

    private bool PureMatchTest(OwnershipBody body, int id) => body.Values[id].Kind == OwnershipValueKind.None && body.Operations[id].Input == -1 &&
        (body.LoanInputs.Count == 0 || body.LoanInputs[id] == body.LoanStates[id]);

    private int MatchSuccess(OwnershipBody body, int test, int next)
    {
        if ((uint)test >= (uint)body.Operations.Count)
        {
            return -1;
        }

        var success = -1;
        var failure = false;
        for (var e = body.EdgeHeads[test]; e >= 0; e = body.Edges[e].Next)
        {
            var edge = body.Edges[e];
            if (edge.Kind == OwnershipEdgeKind.True && success < 0)
            {
                success = e;
            }
            else if (edge.Kind == OwnershipEdgeKind.False && next >= 0 && edge.To == next && !failure)
            {
                failure = true;
            }
            else
            {
                return -1;
            }
        }

        return failure == (next >= 0) ? success : -1;
    }

    private bool LowerMatchOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (!this.hasMatches || operation.Place < 0 || this.matchPlaces[operation.Place] != 1 || operation.Source.AttributeChain is not null)
        {
            return Fail("Match operation has no verified Subject.", out failure);
        }

        var type = body.Places[operation.Place].Type;
        if (operation.Kind == OwnershipOperationKind.PatternTest)
        {
            var verifiedArm = body.MatchArms[this.matchTests[id]];
            var selected = verifiedArm.GuardEntry < 0 || verifiedArm.GuardBranch >= 0;
            if (this.LogicalIncoming(verifiedArm.BodyEntry) != (selected && body.IsReachable(id) ? 1 : 0))
            {
                return Fail("Body acquisition has an unexpected incoming path.", out failure);
            }

            if (verifiedArm.GuardBranch >= 0 && body.IsReachable(verifiedArm.GuardBranch))
            {
                if (!this.Dominates(verifiedArm.GuardEntry, verifiedArm.GuardBranch) || !this.Dominates(verifiedArm.GuardValue, verifiedArm.GuardBranch) ||
                    (verifiedArm.GuardCleanupStart < verifiedArm.GuardBranch && !this.Dominates(verifiedArm.GuardCleanupStart, verifiedArm.GuardBranch)))
                {
                    return Fail("Guard selection is not dominated by its evaluation and cleanup.", out failure);
                }
            }
        }

        if (this.IsCompositeSubject(type) || operation.Kind == OwnershipOperationKind.DecomposeCase || this.decompositionOwners[operation.Place] >= 0)
        {
            return this.LowerCompositeMatchOperation(body, function, constants, id, out failure);
        }

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
            if (!this.TryMatchNumber(pattern, out var bits))
            {
                return Fail("Invalid scalar pattern literal.", out failure);
            }

            function.AddScalar(EmissionOpcode.Scalar, id, [this.PhysicalOperand(body, Input(body, this.subjectInitializers[operation.Place], 0)), new(EmissionOperandKind.Integer, bits)], WindowsLowering.GetValue(type)!.ComputationType, "eq", comparison: true);
        }

        return true;
    }

    private bool TryMatchNumber(BoundPattern pattern, out Int128 bits)
    {
        if (pattern.Literal.Kind == PatternLiteralKind.Character && ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.Char) &&
            !pattern.Literal.Negative && pattern.Literal.Magnitude <= 0x10FFFF &&
            pattern.Source is CharLiteralKoto { Value: { } scalar } && (UInt128)scalar.Value == pattern.Literal.Magnitude)
        {
            bits = scalar.Value;
            return ScalarTypes.IsCharacterValue(bits);
        }

        bits = 0;
        return pattern.Literal.Kind == PatternLiteralKind.Integer &&
            ScalarTypes.TryLiteral(this.Matched(pattern.MatchedType), pattern.Literal.Magnitude, pattern.Literal.Negative, this.pointerWidth, out bits);
    }

    private bool ValidateCandidateRead(OwnershipBody body, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var index = body.OperationSteps[id];
        if ((uint)index >= (uint)body.MatchArms.Count)
        {
            return Fail("Candidate read has no selected Pattern position.", out failure);
        }

        var arm = body.MatchArms[index];
        if ((uint)arm.Match >= (uint)body.Matches.Count || (uint)arm.Pattern >= (uint)body.Matches[arm.Match].Binding.Positions.Count)
        {
            return Fail("Candidate read has no matching Subject plan.", out failure);
        }

        var match = body.Matches[arm.Match];
        var pattern = match.Binding.Positions[arm.Pattern];
        var scalar = ScalarTypes.Supports(this.Matched(pattern.MatchedType));
        if (arm.GuardEntry < 0 || operation.Place != match.Subject || operation.Source.BoundSymbol?.Kind != BindingSymbolKind.PatternCandidate ||
            !ReferenceEquals(operation.Source.BoundSymbol, pattern.CandidateSymbol) || !ReferenceEquals(SignatureType(this, operation.Source.BoundType), pattern.CandidateSymbol?.Type) ||
            (body.IsReachable(id) && !this.Dominates(arm.GuardEntry, id)) ||
            (scalar && (body.Values[id].Kind != OwnershipValueKind.Alias || Input(body, id, 0) != this.subjectInitializers[match.Subject])) ||
            (!scalar && !ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.Unit) && !ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.String)))
        {
            return Fail("Candidate read does not inspect its protected Subject snapshot.", out failure);
        }

        if (ReferenceEquals(this.Matched(pattern.MatchedType), BoundType.String))
        {
            var type = SignatureType(this, operation.Source.BoundType);
            var protection = body.LoanStates[id];
            while (protection >= 0 && body.ComparisonLoans[protection].Guard != index)
            {
                protection = body.ComparisonLoans[protection].Parent;
            }

            if (!ReferenceTypes.IsString(type) || body.Values[id].Kind != OwnershipValueKind.Borrow ||
                operation.Acquisition != AcquisitionKind.None || operation.LoanMode != LoanRequirement.None ||
                (uint)operation.Input >= (uint)body.Places.Count || !ReferenceEquals(body.Places[operation.Input].Type, type) ||
                body.Places[operation.Input].Kind != OwnershipPlaceKind.Temporary || !ReferenceEquals(body.Places[operation.Input].Source, operation.Source) ||
                type!.Origin is not { Kind: OriginKind.Projection } origin || !ReferenceEquals(origin.Binder, pattern.CandidateSymbol!.Declaration) || origin.Slot != pattern.CandidateSymbol.Slot ||
                (uint)arm.GuardLoan >= (uint)body.ComparisonLoans.Count || protection < 0)
            {
                return Fail("Candidate reference has no active Subject Loan or matching Origin.", out failure);
            }
        }

        return true;
    }
}
