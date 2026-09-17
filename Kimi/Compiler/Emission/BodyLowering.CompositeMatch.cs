// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<PatternTestStep> compositeTests = new();

    private bool IsCompositeSubject(BoundType type) => (type.Kind == BoundTypeKind.Tuple || EnumStorage.IsEnum(type)) && this.aggregateLayouts.Get(type) is { NeedsDestruction: false };

    private int PatternOffset(BoundMatch match, int position)
    {
        long offset = 0;
        var depth = 0;
        while (match.Positions[position].Parent >= 0)
        {
            var child = match.Positions[position];
            if (++depth > 64 || child.Parent >= position)
            {
                return -1;
            }

            var parent = match.Positions[child.Parent];
            var type = parent.MatchedType;
            var shape = this.aggregateLayouts.Get(type);
            if (shape is null)
            {
                return -1;
            }

            if (parent.Kind == BoundPatternKind.Case)
            {
                if (parent.Case is not { } selected || !ReferenceEquals(EnumStorage.Case(type, selected.Ordinal), selected) || shape.Cases is not { } cases || (uint)selected.Ordinal >= (uint)cases.Length)
                {
                    return -1;
                }

                offset += shape.PayloadOffset;
                shape = cases[selected.Ordinal];
                type = type.StoredCases![selected.Ordinal];
            }
            else if (parent.Kind != BoundPatternKind.Tuple)
            {
                return -1;
            }

            if ((uint)child.Element >= (uint)shape.Count || !ReferenceEquals(child.MatchedType, type.Components[child.Element]))
            {
                return -1;
            }

            offset += shape.Offset(child.Element);
            position = child.Parent;
        }

        return offset <= int.MaxValue ? (int)offset : -1;
    }

    private bool ValidateCompositePattern(BoundMatch match, int root)
    {
        var end = match.Positions[root].End;
        if (end <= root || end > match.Positions.Count)
        {
            return false;
        }

        for (var i = root; i < end; i++)
        {
            var node = match.Positions[i];
            if (node.End <= i || node.End > end || node.AccessMode != PatternAccessMode.Owned || node.ImplicitDeref != PatternImplicitDeref.None ||
                node.Source.BindingState != BindingState.Resolved || node.Source.AttributeChain is not null || this.PatternOffset(match, i) < 0)
            {
                return false;
            }

            if (node.Kind is BoundPatternKind.Case or BoundPatternKind.Tuple)
            {
                var count = node.Kind == BoundPatternKind.Case ? node.Case?.Payload.Length ?? -1 : node.MatchedType.Components.Count;
                if ((node.Kind == BoundPatternKind.Tuple && node.MatchedType.Kind != BoundTypeKind.Tuple) ||
                    (node.Kind == BoundPatternKind.Case && !ReferenceEquals(EnumStorage.Case(node.MatchedType, node.Case!.Ordinal), node.Case)))
                {
                    return false;
                }

                var child = i + 1;
                for (var element = 0; element < count; element++)
                {
                    if (child >= node.End || match.Positions[child].Parent != i || match.Positions[child].Element != element || match.Positions[child].End <= child)
                    {
                        return false;
                    }

                    child = match.Positions[child].End;
                }

                if (child != node.End)
                {
                    return false;
                }
            }
            else if (node.End != i + 1 || node.Kind is not (BoundPatternKind.Wildcard or BoundPatternKind.Binding or BoundPatternKind.Unit or BoundPatternKind.Literal) ||
                (node.Kind == BoundPatternKind.Unit && !ReferenceEquals(node.MatchedType, BoundType.Unit)) ||
                (node.Kind == BoundPatternKind.Literal && !(ReferenceEquals(node.MatchedType, BoundType.Boolean) && node.Literal.Kind == PatternLiteralKind.Boolean && node.Literal.Magnitude <= 1) && !this.TryMatchNumber(node, out _)))
            {
                return false;
            }
        }

        return true;
    }

    private int NextPatternOperation(OwnershipBody body, int id)
    {
        if ((uint)id >= (uint)body.Operations.Count)
        {
            return -1;
        }

        var edge = body.EdgeHeads[id];
        return edge >= 0 && body.Edges[edge].Next < 0 && body.Edges[edge].Kind == OwnershipEdgeKind.Normal ? body.Edges[edge].To : -1;
    }

    private bool PrepareCompositeAcquisitions(OwnershipBody body, OwnershipMatchArmPlan arm, int subject, out string? failure)
    {
        var cursor = this.NextPatternOperation(body, arm.BodyEntry);
        var decomposition = arm.DecompositionStart;
        failure = null;
        if (!this.PrepareCompositeAcquisition(body, body.Matches[arm.Match].Binding, arm.Pattern, subject, ref cursor, ref decomposition) ||
            decomposition != arm.DecompositionStart + arm.DecompositionCount)
        {
            return Fail("Selected composite acquisition does not follow its Pattern tree.", out failure);
        }

        return true;
    }

    private bool PrepareCompositeAcquisition(OwnershipBody body, BoundMatch plan, int index, int input, ref int cursor, ref int decomposition)
    {
        var pattern = plan.Positions[index];
        var needed = false;
        for (var i = index; i < pattern.End; i++)
        {
            needed |= plan.Positions[i].Kind == BoundPatternKind.Binding;
        }

        if (!needed)
        {
            return true;
        }

        if (pattern.Kind == BoundPatternKind.Binding)
        {
            if (pattern.BodySymbol is null || !body.SymbolPlaces.TryGetValue(pattern.BodySymbol, out var local) || (uint)cursor >= (uint)body.Operations.Count ||
                body.Operations[cursor].Kind != OwnershipOperationKind.Declare || body.Operations[cursor].Place != local || !ReferenceEquals(body.Places[local].Type, pattern.MatchedType))
            {
                return false;
            }

            cursor = this.NextPatternOperation(body, cursor);
            var expected = pattern.Acquisition == PatternAcquisition.Copy ? AcquisitionKind.Copy : AcquisitionKind.Move;
            if ((uint)cursor >= (uint)body.Operations.Count || body.Operations[cursor] is not { Kind: OwnershipOperationKind.AcquirePattern } acquire ||
                acquire.Place != input || acquire.Input != local || acquire.Acquisition != expected || !ReferenceEquals(acquire.Source, pattern.Source) || this.patternAcquisitions[cursor] != 0)
            {
                return false;
            }

            this.patternAcquisitions[cursor] = input + 1;
            this.matchPlaces[input] = 1;
            cursor = this.NextPatternOperation(body, cursor);
            return true;
        }

        if ((uint)decomposition >= (uint)body.Decompositions.Count)
        {
            return false;
        }

        var d = decomposition++;
        var split = body.Decompositions[d];
        if (split.Place != input || !ReferenceEquals(split.Case, pattern.Case))
        {
            return false;
        }

        var element = 0;
        for (var child = index + 1; child < pattern.End; child = plan.Positions[child].End)
        {
            if ((uint)cursor >= (uint)body.Operations.Count || body.Operations[cursor].Kind != OwnershipOperationKind.Declare ||
                body.Operations[cursor].Place != split.PayloadStart + element || !ReferenceEquals(body.Operations[cursor].Source, plan.Positions[child].Source))
            {
                return false;
            }

            element++;
            cursor = this.NextPatternOperation(body, cursor);
        }

        if (element != split.PayloadCount || (uint)cursor >= (uint)body.Operations.Count || body.Operations[cursor].Kind != OwnershipOperationKind.DecomposeCase ||
            body.Operations[cursor].Place != input || body.OperationSteps[cursor] != d || !ReferenceEquals(body.Operations[cursor].Source, pattern.Source) || this.patternDecompositions[cursor] != 0)
        {
            return false;
        }

        this.matchPlaces[input] = 1;
        this.patternDecompositions[cursor] = d + 1;
        cursor = this.NextPatternOperation(body, cursor);
        element = 0;
        for (var child = index + 1; child < pattern.End; child = plan.Positions[child].End)
        {
            if (!this.PrepareCompositeAcquisition(body, plan, child, split.PayloadStart + element++, ref cursor, ref decomposition))
            {
                return false;
            }
        }

        return true;
    }

    private bool LowerCompositeMatchOperation(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (body.IsReachable(id) && operation.Kind != OwnershipOperationKind.InitializeSubject && (body.GetInputState(id, operation.Place) & PlaceState.MustInit) == 0)
        {
            return Fail("Composite Pattern requires initialized source storage.", out failure);
        }

        if (operation.Kind == OwnershipOperationKind.AcquirePattern)
        {
            if (this.patternAcquisitions[id] != operation.Place + 1 || (body.IsReachable(id) && (body.GetInputState(id, operation.Input) & PlaceState.MayInit) != 0))
            {
                return Fail("Composite binding has no checked acquisition.", out failure);
            }

            var type = body.Places[operation.Place].Type;
            if (IsScalar(type))
            {
                var representation = WindowsLowering.GetValue(type)!;
                function.AddScalar(EmissionOpcode.PatternRead, id, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.Integer, 0)], representation.ComputationType, place: operation.Place, representation: representation);
                function.AddScalar(EmissionOpcode.StoreScalar, id, [new(EmissionOperandKind.Value, id)], representation.ComputationType, place: operation.Input, representation: representation);
            }
            else if (this.aggregateLayouts.Get(type) is { } layout && layout.Value.Layout.Size != 0)
            {
                function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, operation.Input, operation.Place, Aggregate: layout));
            }

            return true;
        }

        if (operation.Kind == OwnershipOperationKind.InitializeSubject)
        {
            return (this.subjectInitializers[operation.Place] == id && (!body.IsReachable(id) ||
                ((body.GetInputState(id, operation.Input) & PlaceState.MustInit) != 0 && (body.GetInputState(id, operation.Place) & PlaceState.MayInit) == 0))) || Fail("Composite Subject acquisition is not fresh.", out failure);
        }

        if (operation.Kind == OwnershipOperationKind.DecomposeCase)
        {
            return (this.patternDecompositions[id] == body.OperationSteps[id] + 1 && this.patternDecompositions[id] > 0) || Fail("Decomposition is not owned by a selected Pattern.", out failure);
        }

        if (operation.Kind == OwnershipOperationKind.MatchDispatch)
        {
            return true; // Verified subslots reuse active payload storage.
        }

        var arm = body.MatchArms[this.matchTests[id]];
        if (this.LogicalIncoming(arm.BodyEntry) != ((arm.GuardEntry < 0 || arm.GuardBranch >= 0) && body.IsReachable(id) ? 1 : 0))
        {
            return Fail("Composite body acquisition has an unexpected incoming path.", out failure);
        }

        if (this.successor[id] >= 0 || this.blocks[id] < 0)
        {
            return true;
        }

        var binding = body.Matches[arm.Match].Binding;
        this.compositeTests.Clear();
        for (var i = arm.Pattern; i < binding.Positions[arm.Pattern].End; i++)
        {
            var node = binding.Positions[i];
            if (node.Kind == BoundPatternKind.Case)
            {
                this.compositeTests.Add(new(this.PatternOffset(binding, i), WindowsLowering.GetValue(BoundType.I32)!, node.Case!.Ordinal));
            }
            else if (node.Kind == BoundPatternKind.Literal)
            {
                var bits = (Int128)node.Literal.Magnitude;
                if (!ReferenceEquals(node.MatchedType, BoundType.Boolean) && !this.TryMatchNumber(node, out bits))
                {
                    return Fail("Unsupported composite literal.", out failure);
                }

                this.compositeTests.Add(new(this.PatternOffset(binding, i), WindowsLowering.GetValue(node.MatchedType)!, bits));
            }
        }

        function.Instructions.Add(new(EmissionOpcode.CompositePattern, id, operation.Place, Pattern: this.compositeTests.ToArray()));
        return true;
    }

    private bool LowerPatternProjection(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var armIndex = body.OperationSteps[id];
        if ((uint)armIndex >= (uint)body.MatchArms.Count || operation.Kind != OwnershipOperationKind.Read)
        {
            return Fail("Candidate projection has no guard arm.", out failure);
        }

        var arm = body.MatchArms[armIndex];
        var match = body.Matches[arm.Match];
        var index = body.Values[id].Constant;
        if (index <= arm.Pattern || index >= match.Binding.Positions[arm.Pattern].End || operation.Place != match.Subject ||
            (uint)operation.Input >= (uint)body.Places.Count || arm.GuardEntry < 0 || (body.IsReachable(id) && !this.Dominates(arm.GuardEntry, id)))
        {
            return Fail("Candidate projection is outside its selected guard.", out failure);
        }

        var node = match.Binding.Positions[(int)index];
        var offset = this.PatternOffset(match.Binding, (int)index);
        if (node.Kind != BoundPatternKind.Binding || node.CandidateSymbol is null || !ReferenceEquals(operation.Source.BoundSymbol, node.CandidateSymbol) ||
            !ReferenceEquals(body.Places[operation.Input].Type, node.MatchedType) || !ReferenceEquals(operation.Source.BoundType, node.CandidateSymbol.Type) ||
            offset < 0 || (body.IsReachable(id) && (body.GetInputState(id, match.Subject) & PlaceState.MustInit) == 0))
        {
            return Fail("Candidate projection has the wrong position or Type.", out failure);
        }

        if (ScalarTypes.Supports(node.MatchedType))
        {
            var representation = WindowsLowering.GetValue(node.MatchedType)!;
            function.AddScalar(EmissionOpcode.PatternRead, id, [new(EmissionOperandKind.SlotAddress, match.Subject), new(EmissionOperandKind.Integer, offset)], representation.ComputationType, place: match.Subject, representation: representation);
        }

        return true;
    }
}
