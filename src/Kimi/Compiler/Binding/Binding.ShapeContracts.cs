// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<ShapePair, ShapeJudgment> shapeJudgments = new();
    private readonly Dictionary<FunctionKoto, int> shapeContractHeads = new(ReferenceEqualityComparer.Instance);
    private readonly List<ShapeContract> shapeContracts = [];
    private readonly List<int> shapeDependencies = [];
    private readonly List<int> shapeSharedSlots = [];
    private FunctionKoto? collisionLeft;
    private FunctionKoto? collisionRight;
    private bool collisionShared;

    private readonly record struct ShapePair(FunctionKoto Left, int LeftIndex, FunctionKoto Right, int RightIndex);

    private readonly record struct ShapeJudgment(bool Conflict, AcquisitionMode Mode = default, AcquisitionMode Other = default, ParameterShape Shape = default, bool Conservative = false);

    private readonly record struct ShapeContract(ShapePair Pair, int Start, int Count, int Next, bool ModeOnly);

    private static bool PermittedSlotSharing(FunctionKoto left, FunctionKoto right)
        => left.IsConstructor || right.IsConstructor
            ? left.IsConstructor && right.IsConstructor && ReferenceEquals(CallSlotOwner(left), CallSlotOwner(right))
            : SameExplicitArgumentShape(left, right);

    private ShapeJudgment JudgeParameterShapes(ShapePair pair, BoundType left, BoundType right)
    {
        if (this.shapeJudgments.TryGetValue(pair, out var known))
        {
            return known;
        }

        var sameGenerics = SameExplicitArgumentShape(pair.Left, pair.Right);
        var a = this.ShapeOf(left, pair.Left, sameGenerics);
        var b = this.ShapeOf(right, pair.Right, sameGenerics);
        var union = a.Modes | b.Modes;
        var independent = union == AcquisitionModes.None || IsSingleMode(union) || !this.KeysOverlap(a, b, pair.Left, pair.Right);
        var conditional = false;
        var modeOnly = false;
        if (!independent)
        {
            this.shapeSharedSlots.Clear();
            if (SameBinding(left, right, pair.Left, pair.Right, sameGenerics))
            {
                modeOnly = left.Kind == BoundTypeKind.Parameter;
                this.CollectShapeDependencies(left, pair.Left);
                this.CollectShapeDependencies(right, pair.Right);
                conditional = true;
            }
            else if (PermittedSlotSharing(pair.Left, pair.Right) && !this.KeysOverlap(a, b, pair.Left, pair.Right, shared: true))
            {
                conditional = true; // The common unifier retained every correlation it used; no minimal-set search.
            }
        }

        ShapeJudgment result;
        if (independent || conditional)
        {
            if (conditional && this.shapeSharedSlots.Count != 0)
            {
                var start = this.shapeDependencies.Count;
                this.shapeDependencies.AddRange(this.shapeSharedSlots);
                var next = this.shapeContractHeads.GetValueOrDefault(pair.Right, -1);
                this.shapeContractHeads[pair.Right] = this.shapeContracts.Count;
                this.shapeContracts.Add(new(pair, start, this.shapeSharedSlots.Count, next, modeOnly));
            }

            result = new(false);
        }
        else
        {
            this.ParameterShapesConflict(left, right, a, b, out var mode, out var other, out var shape, out var conservative, overlap: true);
            result = new(true, mode, other, shape, conservative);
        }

        this.shapeJudgments.Add(pair, result);
        return result;
    }

    private void CollectShapeDependencies(BoundType type, FunctionKoto function)
    {
        if (type.Symbol is { } symbol && ReferenceEquals(symbol.Scope.Owner, CallSlotOwner(function)) &&
            symbol.Slot >= 0 && symbol.Slot < CallOwnSlots(function).Count && !this.shapeSharedSlots.Contains(symbol.Slot))
        {
            this.shapeSharedSlots.Add(symbol.Slot);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.CollectShapeDependencies(type.Components[i], function);
        }
    }
}
