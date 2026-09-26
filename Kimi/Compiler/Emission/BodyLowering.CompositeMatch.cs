// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] patternProjectionRoots = [];

    // The selected layers of a structural position are safe value references (SPEC 14.8.1).
    private static bool ValidDereferences(BoundPattern pattern)
    {
        var type = pattern.MatchedType;
        for (var layer = 0; layer < pattern.ImplicitDerefs; layer++)
        {
            if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                return false;
            }

            type = type.Components[0];
        }

        return true;
    }

    private bool IsCompositeSubject(BoundType type) => (type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && ReferenceTypes.IsStorage(type)) ||
        ((type.Kind == BoundTypeKind.Tuple || EnumStorage.IsEnum(type) || StructStorage.IsStruct(type)) &&
        this.aggregateLayouts.Get(type) is { } layout && (!layout.NeedsDestruction || MatchTypes.SupportsOwnedPatternValue(type, this.ownedPatternTypes)));

    private BoundType PatternType(BoundPattern pattern)
    {
        var type = pattern.MatchedType;
        for (var layer = 0; layer < pattern.ImplicitDerefs; layer++)
        {
            type = type.Components[0];
        }

        return this.Matched(type);
    }

    private int PatternOffset(BoundMatch match, int position)
    {
        Span<int> dereferences = stackalloc int[64];
        return this.PatternPath(match, position, -1, dereferences, out var offset) < 0 ? -1 : offset;
    }

    // Follow the retained positional tree. Each implicit reference read starts a new
    // address segment; enum tag tests dominate any payload dereferences at runtime.
    private int PatternPath(BoundMatch match, int position, int root, Span<int> dereferences, out int finalOffset)
    {
        Span<int> ancestors = stackalloc int[64];
        finalOffset = -1;
        var depth = 0;
        for (var current = position; current >= 0; current = match.Positions[current].Parent)
        {
            if (depth == ancestors.Length || (uint)current >= (uint)match.Positions.Count || match.Positions[current].Parent >= current)
            {
                return -1;
            }

            ancestors[depth++] = current;
            if (current == root)
            {
                break;
            }
        }

        if (root >= 0 && ancestors[depth - 1] != root)
        {
            return -1;
        }

        long offset = 0;
        var count = 0;
        for (var level = depth - 1; level >= 0; level--)
        {
            var parent = match.Positions[ancestors[level]];
            for (var layer = 0; layer < parent.ImplicitDerefs; layer++)
            {
                if (offset > int.MaxValue || count == dereferences.Length)
                {
                    return -1;
                }

                dereferences[count++] = (int)offset;
                offset = 0;
            }

            if (level == 0)
            {
                break;
            }

            var child = match.Positions[ancestors[level - 1]];
            var type = this.PatternType(parent);
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

            if ((uint)child.Element >= (uint)shape.Count || !ReferenceEquals(this.Matched(child.MatchedType), type.Components[child.Element]))
            {
                return -1;
            }

            offset += shape.Offset(child.Element);
        }

        if (offset > int.MaxValue)
        {
            return -1;
        }

        finalOffset = (int)offset;
        return count;
    }

    private PatternTestStep PatternStep(EmissionFunction function, BoundMatch match, int position, ValueLowering representation, Int128 expected, int text = -2, int root = -1)
    {
        Span<int> dereferences = stackalloc int[64];
        var count = this.PatternPath(match, position, root, dereferences, out var offset);
        var start = function.PatternDereferences.Count;
        function.PatternDereferences.AddRange(dereferences[..Math.Max(count, 0)]);
        return new(offset, representation, expected, text, start, Math.Max(count, 0));
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
            if (node.End <= i || node.End > end ||
                (node.AccessMode != PatternAccessMode.Owned) != (node.ImplicitDerefs > 0 || (node.Parent >= 0 && match.Positions[node.Parent].AccessMode != PatternAccessMode.Owned)) ||
                !ValidDereferences(node) ||
                node.Source.BindingState != BindingState.Resolved || node.Source.AttributeChain is not null || this.PatternOffset(match, i) < 0)
            {
                return false;
            }

            if (node.Kind is BoundPatternKind.Case or BoundPatternKind.Tuple)
            {
                var count = node.Kind == BoundPatternKind.Case ? node.Case?.Payload.Length ?? -1 : this.PatternType(node).Components.Count;
                if ((node.Kind == BoundPatternKind.Tuple && this.PatternType(node).Kind != BoundTypeKind.Tuple) ||
                    (node.Kind == BoundPatternKind.Case && !ReferenceEquals(EnumStorage.Case(this.PatternType(node), node.Case!.Ordinal), node.Case)))
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
                (node.Kind == BoundPatternKind.Unit && !ReferenceEquals(this.PatternType(node), BoundType.Unit)) ||
                (node.Kind == BoundPatternKind.Literal && !(ReferenceEquals(this.PatternType(node), BoundType.Boolean) && node.Literal.Kind == PatternLiteralKind.Boolean && node.Literal.Magnitude <= 1) &&
                !(ReferenceEquals(this.PatternType(node), BoundType.String) && node.Literal.Kind == PatternLiteralKind.String && node.Literal.Text is not null) && !this.TryMatchNumber(node, out _)))
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

        if (pattern.AccessMode != PatternAccessMode.Owned)
        {
            for (var i = index; i < pattern.End; i++)
            {
                var binding = plan.Positions[i];
                if (binding.Kind != BoundPatternKind.Binding)
                {
                    continue;
                }

                if (binding.BodySymbol?.Type is not { } result || !body.SymbolPlaces.TryGetValue(binding.BodySymbol, out var local) ||
                    (uint)cursor >= (uint)body.Operations.Count || body.Operations[cursor] is not { Kind: OwnershipOperationKind.Declare } declaration || declaration.Place != local ||
                    !ReferenceEquals(body.Places[local].Type, this.Matched(result)))
                {
                    return false;
                }

                cursor = this.NextPatternOperation(body, cursor);
                if ((uint)cursor >= (uint)body.Operations.Count || body.Operations[cursor] is not { Kind: OwnershipOperationKind.AcquirePattern, Acquisition: AcquisitionKind.Copy } acquire ||
                    acquire.Place != input || acquire.Input != local || !ReferenceEquals(acquire.Source, binding.Source) || this.patternAcquisitions[cursor] != 0 ||
                    body.Values[cursor] is not { Kind: OwnershipValueKind.PatternProjection } projection || projection.Constant != i ||
                    (binding.Acquisition == PatternAcquisition.Copy ? !ReferenceEquals(this.Matched(binding.MatchedType), body.Places[local].Type) :
                        binding.Acquisition != PatternAcquisition.Borrow ||
                        !(SharedReadTypes.ReadsStoredPointer(this.Matched(binding.MatchedType), body.Places[local].Type) ||
                            (ReferenceTypes.IsStorage(body.Places[local].Type) && ReferenceEquals(body.Places[local].Type.Components[0], this.Matched(binding.MatchedType))))))
                {
                    return false;
                }

                this.patternAcquisitions[cursor] = input + 1;
                this.patternProjectionRoots[cursor] = index;
                this.matchPlaces[input] = 1;
                cursor = this.NextPatternOperation(body, cursor);
            }

            return true;
        }

        if (pattern.Kind == BoundPatternKind.Binding)
        {
            if (pattern.BodySymbol is null || !body.SymbolPlaces.TryGetValue(pattern.BodySymbol, out var local) || (uint)cursor >= (uint)body.Operations.Count ||
                body.Operations[cursor].Kind != OwnershipOperationKind.Declare || body.Operations[cursor].Place != local || !ReferenceEquals(body.Places[local].Type, this.Matched(pattern.MatchedType)))
            {
                return false;
            }

            cursor = this.NextPatternOperation(body, cursor);
            // A committed CopyOrMove is exact only on the instance's bound Place; a source body cannot lower it.
            var expected = pattern.Acquisition == PatternAcquisition.Copy ? AcquisitionKind.Copy
                : pattern.Acquisition == PatternAcquisition.CopyOrMove ? body.Places[local].Acquisition : AcquisitionKind.Move;
            if (expected is not (AcquisitionKind.Copy or AcquisitionKind.Move) ||
                (uint)cursor >= (uint)body.Operations.Count || body.Operations[cursor] is not { Kind: OwnershipOperationKind.AcquirePattern } acquire ||
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

    private bool LowerCompositeMatchOperation(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, int id, out string? failure)
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

            if (body.Values[id].Kind == OwnershipValueKind.PatternProjection)
            {
                return this.LowerSharedPatternAcquisition(body, function, id, out failure);
            }

            var type = body.Places[operation.Place].Type;
            if (IsScalar(type))
            {
                var representation = WindowsLowering.GetValue(type)!;
                function.AddScalar(EmissionOpcode.PatternRead, id, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.Integer, 0)], representation.ComputationType, place: operation.Place, representation: representation);
                function.AddScalar(EmissionOpcode.StoreScalar, id, [new(EmissionOperandKind.Value, id)], representation.ComputationType, place: operation.Input, representation: representation);
            }
            else if (ReferenceEquals(type, BoundType.String))
            {
                if (operation.Acquisition != AcquisitionKind.Move)
                {
                    return Fail("Owned string Pattern binding must transfer responsibility.", out failure);
                }

                function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.SlotAddress, operation.Place)], place: operation.Input);
            }
            else if (this.aggregateLayouts.Get(type) is { } layout && layout.Value.Layout.Size != 0)
            {
                function.Instructions.Add(new(EmissionOpcode.TransferAggregate, id, operation.Input, operation.Place, Aggregate: layout));
            }

            this.AddStringFlags(function, operation, id);
            return true;
        }

        if (operation.Kind == OwnershipOperationKind.InitializeSubject)
        {
            if (IsScalar(body.Places[operation.Place].Type))
            {
                if (body.Values[id].Kind != OwnershipValueKind.Alias || body.Values[id].Count != 1 ||
                    ValuePlace(body.Operations[Input(body, id, 0)]) != operation.Input || (body.IsReachable(id) && !this.Dominates(Input(body, id, 0), id)))
                {
                    return Fail("Borrowed Subject does not retain its acquired reference.", out failure);
                }

                var representation = WindowsLowering.GetValue(body.Places[operation.Place].Type)!;
                function.AddScalar(EmissionOpcode.StoreScalar, id, [this.PhysicalOperand(body, Input(body, id, 0))], representation.ComputationType, place: operation.Place, representation: representation);
            }

            this.AddStringFlags(function, operation, id);
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
        var patternStart = function.PatternSteps.Count;
        for (var i = arm.Pattern; i < binding.Positions[arm.Pattern].End; i++)
        {
            var node = binding.Positions[i];
            if (node.Kind == BoundPatternKind.Case)
            {
                function.PatternSteps.Add(this.PatternStep(function, binding, i, WindowsLowering.GetValue(BoundType.I32)!, node.Case!.Ordinal));
            }
            else if (node.Kind == BoundPatternKind.Literal)
            {
                if (ReferenceEquals(this.PatternType(node), BoundType.String) && node.Literal.Kind == PatternLiteralKind.String && node.Literal.Text is { } text)
                {
                    var constant = text.Length == 0 ? -1 : constants.Intern(text, LlvmConstantKind.Text);
                    function.PatternSteps.Add(this.PatternStep(function, binding, i, WindowsLowering.String, 0, constant));
                    continue;
                }

                var bits = (Int128)node.Literal.Magnitude;
                if (!ReferenceEquals(this.PatternType(node), BoundType.Boolean) && !this.TryMatchNumber(node, out bits))
                {
                    return Fail("Unsupported composite literal.", out failure);
                }

                function.PatternSteps.Add(this.PatternStep(function, binding, i, WindowsLowering.GetValue(this.PatternType(node))!, bits));
            }
        }

        function.Instructions.Add(new(EmissionOpcode.CompositePattern, id, operation.Place, PatternStart: patternStart, PatternCount: function.PatternSteps.Count - patternStart));
        return true;
    }

    private bool LowerSharedPatternAcquisition(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var armIndex = body.OperationSteps[id];
        if ((uint)armIndex >= (uint)body.MatchArms.Count)
        {
            return Fail("Shared Pattern acquisition has no selected arm.", out failure);
        }

        var arm = body.MatchArms[armIndex];
        var plan = body.Matches[arm.Match].Binding;
        var position = (int)body.Values[id].Constant;
        if (position < arm.Pattern || position >= plan.Positions[arm.Pattern].End ||
            (body.IsReachable(id) && !this.Dominates(arm.BodyEntry, id)))
        {
            return Fail("Shared Pattern acquisition is outside its selected body.", out failure);
        }

        var pattern = plan.Positions[position];
        var type = body.Places[operation.Input].Type;
        var pointerRead = pattern.Acquisition == PatternAcquisition.Copy || SharedReadTypes.ReadsStoredPointer(this.Matched(pattern.MatchedType), type);
        this.LowerPatternRead(function, plan, position, this.patternProjectionRoots[id], type, pointerRead, id, operation.Place, operation.Input, true);
        return true;
    }

    private void LowerPatternRead(EmissionFunction function, BoundMatch plan, int position, int root, BoundType type, bool copy, int id, int subject, int destination, bool storeScalar)
    {
        if (ReferenceEquals(type, BoundType.Unit) || (copy && this.aggregateLayouts.Get(type) is { Value.Layout.Size: 0 }))
        {
            return;
        }

        var representation = WindowsLowering.GetValue(type);
        var scalarCopy = copy && IsScalar(type);
        var step = this.PatternStep(function, plan, position, representation ?? WindowsLowering.GetValue(BoundType.I32)!, 0, root: root);
        function.AddScalar(EmissionOpcode.PatternRead, id, [new(EmissionOperandKind.SlotAddress, subject), new(EmissionOperandKind.Integer, step.Offset)], scalarCopy ? representation!.ComputationType : "ptr", scalarCopy ? null : "address", place: subject, representation: representation);
        function.Instructions[^1] = function.Instructions[^1] with { PatternStart = function.PatternSteps.Count, PatternCount = 1 };
        function.PatternSteps.Add(step);
        if (IsScalar(type) && storeScalar)
        {
            function.AddScalar(EmissionOpcode.StoreScalar, id, [new(EmissionOperandKind.Value, id)], representation!.ComputationType, place: destination, representation: representation);
        }
        else if (this.aggregateLayouts.Get(type) is { } layout && layout.Value.Layout.Size != 0)
        {
            Transfer(function, id, layout, [new(EmissionOperandKind.Value, id)], destination);
        }
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
        if (index < arm.Pattern || index >= match.Binding.Positions[arm.Pattern].End || operation.Place != match.Subject ||
            (uint)operation.Input >= (uint)body.Places.Count || arm.GuardEntry < 0 || (body.IsReachable(id) && !this.Dominates(arm.GuardEntry, id)))
        {
            return Fail("Candidate projection is outside its selected guard.", out failure);
        }

        var node = match.Binding.Positions[(int)index];
        var offset = this.PatternOffset(match.Binding, (int)index);
        if (node.Kind != BoundPatternKind.Binding || node.CandidateSymbol is null || !ReferenceEquals(operation.Source.BoundSymbol, node.CandidateSymbol) ||
            !ReferenceEquals(body.Places[operation.Input].Type, this.Matched(node.CandidateSymbol.Type)) || !ReferenceEquals(SignatureType(this, operation.Source.BoundType), this.Matched(node.CandidateSymbol.Type)) ||
            offset < 0 || (body.IsReachable(id) && (body.GetInputState(id, match.Subject) & PlaceState.MustInit) == 0))
        {
            return Fail("Candidate projection has the wrong position or Type.", out failure);
        }

        if (arm.GuardLoan >= 0)
        {
            var protection = body.LoanStates[id];
            while (protection >= 0 && body.ComparisonLoans[protection].Guard != armIndex)
            {
                protection = body.ComparisonLoans[protection].Parent;
            }

            if (protection < 0)
            {
                return Fail("Candidate projection has no active guard protection.", out failure);
            }
        }

        var type = body.Places[operation.Input].Type;
        var copy = ReferenceEquals(type, this.Matched(node.MatchedType)) || SharedReadTypes.ReadsStoredPointer(this.Matched(node.MatchedType), type);
        if (!copy && (!ReferenceTypes.IsStorage(type) || !ReferenceEquals(type.Components[0], this.Matched(node.MatchedType))))
        {
            return Fail("Candidate projection has no shared-read acquisition.", out failure);
        }

        this.LowerPatternRead(function, match.Binding, (int)index, arm.Pattern, type, copy, id, match.Subject, operation.Input, false);

        return true;
    }
}
