// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 7.4 (R7-N5 B7; DECISIONS BU-220): the pass's judged pairs, keyed by the pair's SourceRefs (a library function's too: a program
    // pass judges library pairs at a gathered use), emptied at each pass's start (ResetParameterShapes). A pair is judged once per pass
    // whatever reaches it again (another correspondence ground, a refining Contract, each use of a gathered group), which also keeps one
    // shape contract per pair in the later function's list.
    private readonly Dictionary<(SourceRef Left, int LeftIndex, SourceRef Right, int RightIndex), ShapeJudgment> shapeJudgments = new();
    private readonly ShapeContractHeadRows shapeContractHeads;
    private readonly List<ShapeContract> shapeContracts = [];
    private readonly List<int> shapeDependencies = [];
    private readonly List<int> shapeSharedSlots = [];
    private FunctionKoto? collisionLeft;
    private FunctionKoto? collisionRight;
    private bool collisionShared;
    private long shapeJudgmentRuns;

    /// <summary>Gets the parameter-shape judgments run so far, one per pair and pass, read by the scaling tests.</summary>
    internal long ShapeJudgmentWork => this.shapeJudgmentRuns;

    private readonly record struct ShapePair(FunctionKoto Left, int LeftIndex, FunctionKoto Right, int RightIndex);

    private readonly record struct ShapeJudgment(bool Conflict, AcquisitionMode Mode = default, AcquisitionMode Other = default, ParameterShape Shape = default, bool Conservative = false);

    private readonly record struct ShapeContract(ShapePair Pair, int Start, int Count, int Next, bool ModeOnly);

    private static bool PermittedSlotSharing(FunctionKoto left, FunctionKoto right)
        => left.IsConstructor || right.IsConstructor
            ? left.IsConstructor && right.IsConstructor && ReferenceEquals(CallSlotOwner(left), CallSlotOwner(right))
            : SameExplicitArgumentShape(left, right);

    private ShapeJudgment JudgeParameterShapes(ShapePair pair, BoundType left, BoundType right)
    {
        var key = (SourceRef.Of(pair.Left), pair.LeftIndex, SourceRef.Of(pair.Right), pair.RightIndex);
        if (this.shapeJudgments.TryGetValue(key, out var known))
        {
            return known;
        }

        this.shapeJudgmentRuns++;
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

        this.shapeJudgments.Add(key, result);
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

    /// <summary>The `shapeContractHeads` family's one accessor (DECISIONS BU-220): a pass family, the first of a function's shape contracts
    /// as the later function of an overload pair (its list runs through ShapeContract.Next), kept in the function's module's NodeColumns at
    /// its SyntaxId as the contract's index + 1, so the column's default is no contract; absent after a pass until the column's first
    /// write. Its members keep the names its readers use.</summary>
    private readonly struct ShapeContractHeadRows(Binding binding)
    {
        internal int this[FunctionKoto function]
        {
            set => HirTables.Of(function).Families.ShapeContractHeads.Ref(function.SyntaxId, binding) = value + 1;
        }

        internal int GetValueOrDefault(FunctionKoto function, int absent)
            => function.SyntaxOwner is { } table && table.Hir.Families.ShapeContractHeads.Get(function.SyntaxId, binding) is var head and not 0 ? head - 1 : absent;

        internal bool ContainsKey(FunctionKoto function) => this.GetValueOrDefault(function, -1) >= 0;
    }
}
