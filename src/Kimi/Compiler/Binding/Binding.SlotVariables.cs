// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Slot variables (SPEC 10.8 as revised, 15.3.5, 15.3.6; G1128): a slot's Origins as variables of the call's one Origin solve.
// - Responsibility: the skeleton of an open slot's bindings (SlotSkeleton, SkeletonOf): the structure they agree in, with each Origin
//   position a slot variable of the call (NewSlotVariable; the key family SlotVariable, Binding.OpenOrigins.cs), one per distinct
//   (bindings' sub-Type tuple, polarity) query; whether a position is one, decided over every binding (BecomesSlotVariable); after the
//   solve, a skeleton with its variables' values (SubstituteSlotVariables, SlotValuesOf, SlotValue) and the inference with the values
//   in place of its variables (SettleSlotVariables); the per-query tables of the skeleton walk, of the substitution and of the
//   bindings' collection (slotCollectMemo, which CollectOriginInference reads), and the work counters the scaling tests read.
// - Inputs: a slot's bindings (SlotBinding, newest first, each at its composed polarity), the call and its OriginInference; after the
//   solve, its slot values, one per variable index.
// - Outputs: the skeleton the slot's frame keeps during the solve; the skeleton substituted with the values; the settled inference.
// - Invariants: a variable's bounds are a function of its key (design R1.3 (b)'s theorem), so positions with one key share one
//   variable exactly; a variable never leaves its call's solve: after SettleSlotVariables no bound, premise or variable of the
//   inference names one and SlotVariableCount is 0. Each table is cleared when its walk ends (pooled, BU-55).
// - Prohibitions: no Origin relation is judged here, and no binding's order chooses a variable or a value; opening a slot's
//   variables and reading their values is the slot step's (SlotSolution.OpenSlotVariables, RestoreSlotValues), solving them the
//   call's one solve's (OriginInference).
public sealed partial class Binding
{
    // G1128 (B2; BU-55): the per-query tables of a slot's skeleton walk (SkeletonOf: the bindings' sub-Type tuples and the skeleton per
    // tuple and polarity), of a skeleton's substitution (SlotValuesOf) and of its bindings' collection (slotCollectMemo while
    // OpenSlotVariables collects), pooled and cleared per walk.
    private readonly Dictionary<(int Prefix, BoundType Part), int> slotTupleIds = new();
    private readonly Dictionary<(int Tuple, int Polarity), BoundType> slotSkeletons = new();
    private readonly HashSet<(BoundType, BoundType, int)> slotCollectTable = new();
    private readonly Dictionary<BoundType, BoundType> slotSubstituted = new(ReferenceEqualityComparer.Instance);
    private HashSet<(BoundType, BoundType, int)>? slotCollectMemo;
    private int slotWalks;
    private int slotVariables;

    /// <summary>Gets the steps G1128's slot walks took in the latest pass: a SkeletonOf node entered past its (tuple, polarity) table, a
    /// CollectOriginInference query under the slot collection's table and a SlotValuesOf node; read by the scaling tests (Q20).</summary>
    internal int SlotWalkWork => this.slotWalks;

    /// <summary>Gets the slot variables the latest pass's calls took (NewSlotVariable); read by the scaling tests.</summary>
    internal int SlotVariableWork => this.slotVariables;

    // Whether a skeleton's Origin position, the Types' own Origin (`index` -1) or Origin argument `index`, is a slot variable (SPEC 10.8
    // as revised: each Origin of the solution is a variable bounded by every binding's Origin at its position): some binding has an
    // Origin the evidence fixed there (OpensAsSlotRegion), the caller's pair outer Origin `o` included, which is fixed in its body (G147
    // ruling 43), other than a nested Function Type's per-call input, which that Type binds (SPEC 15.3.4) and which bounds no variable
    // (CollectOriginInference's quantifiers). So no binding's order decides whether a position is solved.
    private static bool BecomesSlotVariable(ReadOnlySpan<BoundType> parts, int index)
    {
        foreach (var part in parts)
        {
            if ((index < 0 ? part.Origin : index < part.OriginArguments.Count ? part.OriginArguments[index] : null) is { } origin &&
                OpensAsSlotRegion(origin) && origin is not { Kind: OriginKind.Input, Binder: FunctionTypeKoto })
            {
                return true;
            }
        }

        return false;
    }

    // G1128 (SPEC 10.8 as revised, 15.3.6): the skeleton of a slot's bindings: the structure they agree in, with each Origin
    // position a slot variable of the call, one per distinct (bindings' sub-Types, polarity) query (B2), so a DAG-shaped binding gives
    // one variable per distinct part.
    private BoundType SlotSkeleton(SlotBinding head, Koto call, OriginInference inference)
    {
        var count = 0;
        for (var node = head; node is not null; node = node.Next)
        {
            count++;
        }

        var parts = this.RentTypes(count);
        try
        {
            var k = 0;
            for (var node = head; node is not null; node = node.Next)
            {
                parts[k++] = node.Type;
            }

            return this.SkeletonOf(parts.AsSpan(0, count), 1, call, inference);
        }
        finally
        {
            this.typeScratch.Return(parts, clearArray: true);
            this.slotTupleIds.Clear();
            this.slotSkeletons.Clear();
        }
    }

    private BoundType SkeletonOf(Span<BoundType> parts, int polarity, Koto call, OriginInference inference)
    {
        // The bindings agree in structure; a pair layer's Origin may be written in one and the original's implicit `o` in another (SPEC
        // 8.1.2), so a node carries Origins when any binding's does, and its Origin position is then written with its variable.
        var type = parts[0];
        var carries = false;
        foreach (var part in parts)
        {
            carries |= part.CarriesOrigin;
        }

        if (!carries)
        {
            return type;
        }

        var tuple = 0;
        foreach (var part in parts)
        {
            if (!this.slotTupleIds.TryGetValue((tuple, part), out var id))
            {
                this.slotTupleIds.Add((tuple, part), id = this.slotTupleIds.Count + 1);
            }

            tuple = id;
        }

        if (this.slotSkeletons.TryGetValue((tuple, polarity), out var known))
        {
            return known;
        }

        this.slotWalks++;

        var origin = BecomesSlotVariable(parts, -1) ? this.NewSlotVariable(call, inference) : type.Origin;
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        var components = this.RentTypes(type.Components.Count);
        var below = this.RentTypes(parts.Length);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = BecomesSlotVariable(parts, i) ? this.NewSlotVariable(call, inference) : type.OriginArguments[i];
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                for (var j = 0; j < parts.Length; j++)
                {
                    below[j] = i < parts[j].Components.Count ? parts[j].Components[i] : type.Components[i];
                }

                components[i] = this.SkeletonOf(below.AsSpan(0, parts.Length), this.ComponentPolarity(type, i, polarity, default), call, inference);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var skeleton = changed ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, origins.AsSpan(0, type.OriginArguments.Count)) : type;
            this.slotSkeletons[(tuple, polarity)] = skeleton;
            return skeleton;
        }
        finally
        {
            this.typeScratch.Return(below, clearArray: true);
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundOrigin NewSlotVariable(Koto call, OriginInference inference)
    {
        this.slotVariables++;
        return this.SlotVariable(call, inference.SlotVariableCount++);
    }

    // The skeleton with each of the call's slot variables replaced by its value; one without a value stays the call's region (15.3.6).
    private BoundType SubstituteSlotVariables(BoundType type, OriginInference inference, ReadOnlySpan<BoundOrigin> slotValues)
    {
        try
        {
            return this.SlotValuesOf(type, inference, slotValues);
        }
        finally
        {
            this.slotSubstituted.Clear();
        }
    }

    private BoundType SlotValuesOf(BoundType type, OriginInference inference, ReadOnlySpan<BoundOrigin> slotValues)
    {
        if (!type.CarriesOrigin)
        {
            return type;
        }

        if (this.slotSubstituted.TryGetValue(type, out var done))
        {
            return done;
        }

        this.slotWalks++;

        var origin = type.Origin is { } outer ? this.SlotValue(outer, inference, slotValues) : null;
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        var components = this.RentTypes(type.Components.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.SlotValue(type.OriginArguments[i], inference, slotValues);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.SlotValuesOf(type.Components[i], inference, slotValues);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            var result = changed ? this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), origin, origins.AsSpan(0, type.OriginArguments.Count)) : type;
            this.slotSubstituted[type] = result;
            return result;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    // An Origin with each of the solve's slot variables in it replaced by its value, itself or an operand of a meet; one without a value
    // stays the call's region (15.3.6).
    private BoundOrigin SlotValue(BoundOrigin origin, OriginInference inference, ReadOnlySpan<BoundOrigin> slotValues)
    {
        if (inference.SolvesSlotVariable(origin))
        {
            return slotValues[SlotVariableIndex(origin, slotValues)] ?? origin;
        }

        var meet = BoundOrigin.Static;
        var changed = false;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            var operand = this.SlotValue(origin.Operands[i], inference, slotValues);
            changed |= !ReferenceEquals(operand, origin.Operands[i]);
            meet = this.Meet(meet, operand);
        }

        return changed ? meet : origin;
    }

    // G1128 R1.3 (e) 3: after the call's solve its slot variables leave the inference with their values: each end of a bound or a
    // premise that names one names its value (SlotValue), and they leave the solve's variables, so the inference's later solves (the
    // candidate's after its late pass, the selected call's) see none and take no span. A bound's flags stay: a value resolves to itself.
    private void SettleSlotVariables(OriginInference inference, ReadOnlySpan<BoundOrigin> slotValues)
    {
        for (var i = 0; i < inference.Bounds.Count; i++)
        {
            var bound = inference.Bounds[i];
            (bound.Longer, bound.Shorter) = (this.SlotValue(bound.Longer, inference, slotValues), this.SlotValue(bound.Shorter, inference, slotValues));
            inference.Bounds[i] = bound;
        }

        for (var i = 0; i < inference.WellFormed.Count; i++)
        {
            var (longer, shorter) = inference.WellFormed[i];
            inference.WellFormed[i] = (this.SlotValue(longer, inference, slotValues), this.SlotValue(shorter, inference, slotValues));
        }

        var kept = 0;
        for (var i = 0; i < inference.Variables.Count; i++)
        {
            if (!inference.SolvesSlotVariable(inference.Variables[i].Origin))
            {
                inference.Variables[kept++] = inference.Variables[i];
            }
        }

        inference.Variables.RemoveRange(kept, inference.Variables.Count - kept);
        inference.SlotVariableCount = 0;
    }
}
