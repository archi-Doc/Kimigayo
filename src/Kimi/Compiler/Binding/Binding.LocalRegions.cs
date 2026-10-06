// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundOrigin, LocalRegion> localRegions = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundOrigin> regionWork = new();
    private readonly HashSet<BoundOrigin> regionVisited = new(ReferenceEqualityComparer.Instance);
    private int regionVersion;

    internal static bool IsLocalRegion(BoundOrigin origin)
        => origin.Open || origin is { Kind: OriginKind.Inference, Binder: VariableKoto, Slot: < 0 };

    internal static bool HasLocalRegion(BoundType type)
    {
        if (!type.CarriesOrigin || type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem)
        {
            return false;
        }

        if (type.Origin is { } origin && HasLocalRegion(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (HasLocalRegion(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasLocalRegion(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    internal bool RegionContains(BoundOrigin expression, BoundOrigin origin)
    {
        if (ReferenceEquals(expression, origin))
        {
            return true;
        }

        if (this.HasRegionBounds(expression))
        {
            foreach (var source in this.LocalRegionSources(expression))
            {
                if (ReferenceEquals(source, origin))
                {
                    return true;
                }
            }
        }

        for (var i = 0; i < expression.Operands.Count; i++)
        {
            if (this.RegionContains(expression.Operands[i], origin))
            {
                return true;
            }
        }

        return false;
    }

    // The graph stores contract bounds, never a current value. Ownership separately follows the definitions reaching each use.
    internal ReadOnlySpan<BoundOrigin> LocalRegionSources(BoundOrigin origin)
    {
        if (!this.localRegions.TryGetValue(origin, out var region))
        {
            return [];
        }

        if (region.Version != this.regionVersion)
        {
            region.Sources.Clear();
            this.regionWork.Clear();
            this.regionVisited.Clear();
            this.regionWork.Add(origin);
            while (this.regionWork.Count != 0)
            {
                var current = this.regionWork[^1];
                this.regionWork.RemoveAt(this.regionWork.Count - 1);
                if (!this.regionVisited.Add(current))
                {
                    continue;
                }

                if (this.localRegions.TryGetValue(current, out var parents))
                {
                    for (var i = parents.Parents.Count - 1; i >= 0; i--)
                    {
                        this.regionWork.Add(this.OriginAtUse(parents.Parents[i], parents.Values[i]));
                    }
                }

                if (current.Kind == OriginKind.Intersection)
                {
                    for (var i = 0; i < current.Operands.Count; i++)
                    {
                        this.regionWork.Add(current.Operands[i]);
                    }
                }
                else if (!IsLocalRegion(current))
                {
                    // A finite Origin keeps its own Loan anchor in addition to constraints reaching it.
                    region.Sources.Add(current);
                }
            }

            region.Version = this.regionVersion;
        }

        return CollectionsMarshal.AsSpan(region.Sources);
    }

    internal bool HasRegionBounds(BoundOrigin origin)
        => IsLocalRegion(origin) || (this.localRegions.TryGetValue(origin, out var region) && region.Parents.Count != 0);

    internal bool HasRegionBounds(BoundType type)
    {
        if (!type.CarriesOrigin || type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem)
        {
            return false;
        }

        if (type.Origin is { } origin && this.HasRegionBounds(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.HasRegionBounds(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.HasRegionBounds(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    internal Koto? LocalRegionSourceUse(BoundOrigin region, BoundOrigin source)
    {
        this.regionWork.Clear();
        this.regionVisited.Clear();
        this.regionWork.Add(region);
        while (this.regionWork.Count != 0)
        {
            var current = this.regionWork[^1];
            this.regionWork.RemoveAt(this.regionWork.Count - 1);
            if (!this.regionVisited.Add(current) || !this.localRegions.TryGetValue(current, out var bounds))
            {
                continue;
            }

            for (var i = 0; i < bounds.Parents.Count; i++)
            {
                if (ReferenceEquals(bounds.Parents[i], source))
                {
                    return bounds.Values[i] is VariableKoto { InitializerKoto: { } initializer } ? initializer : bounds.Values[i];
                }

                this.regionWork.Add(bounds.Parents[i]);
            }
        }

        return null;
    }

    private static bool HasLocalRegion(BoundOrigin origin)
    {
        if (IsLocalRegion(origin))
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (HasLocalRegion(origin.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void ResetLocalRegions()
    {
        foreach (var region in this.localRegions.Values)
        {
            region.Parents.Clear();
            region.Values.Clear();
            region.Sources.Clear();
            region.Version = -1;
        }

        this.regionVersion = 0;
        this.regionWork.Clear();
        this.regionVisited.Clear();
    }

    private BoundOrigin LocalRegionSlot(VariableKoto owner, int slot)
    {
        var origin = this.OriginAtom(owner, OriginKind.Inference, slot);
        if (!this.localRegions.ContainsKey(origin))
        {
            this.localRegions.Add(origin, new());
        }

        return origin;
    }

    private BoundType LocalBorrowType(BoundType type, VariableKoto owner, Koto value)
    {
        if (!IsMutableDeclaration(owner) || !type.CarriesOrigin)
        {
            return type;
        }

        var slot = -1;
        var result = Open(type);
        _ = this.CheckTypeUse(type, result, value);
        return result;

        BoundType Open(BoundType current)
        {
            // A complete generic binding and a callable's quantified signature are contracts, not local storage slots.
            if (!current.CarriesOrigin || current.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem or BoundTypeKind.Parameter)
            {
                return current;
            }

            var components = this.RentTypes(current.Components.Count);
            var arguments = this.originScratch.Rent(current.OriginArguments.Count);
            try
            {
                var origin = current.Origin is null ? null : this.LocalRegionSlot(owner, slot--);
                for (var i = 0; i < current.OriginArguments.Count; i++)
                {
                    arguments[i] = this.LocalRegionSlot(owner, slot--);
                }

                for (var i = 0; i < current.Components.Count; i++)
                {
                    components[i] = Open(current.Components[i]);
                }

                return this.InternType(current.Kind, current.Symbol, current.Semantics, components.AsSpan(0, current.Components.Count), current.Length, origin, arguments.AsSpan(0, current.OriginArguments.Count), current.LengthExpression, current.ClosureContext, current.LengthArguments, current.ResultMode);
            }
            finally
            {
                this.typeScratch.Return(components, clearArray: true);
                this.originScratch.Return(arguments, clearArray: true);
            }
        }
    }

    private BoundType ResolveLocalResultOrigins(BoundType type)
    {
        if (!HasLocalRegion(type))
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        var arguments = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.ResolveLocalResultOrigins(type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                arguments[i] = Resolve(type.OriginArguments[i]);
            }

            return this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, type.Origin is { } origin ? Resolve(origin) : null, arguments.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments, type.ResultMode);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(arguments, clearArray: true);
        }

        BoundOrigin Resolve(BoundOrigin origin)
        {
            if (origin.Kind == OriginKind.Intersection)
            {
                var resolved = BoundOrigin.Static;
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    resolved = this.Meet(resolved, Resolve(origin.Operands[i]));
                }

                return resolved;
            }

            if (!IsLocalRegion(origin))
            {
                return origin;
            }

            var sources = this.LocalRegionSources(origin);
            if (sources.IsEmpty)
            {
                return origin;
            }

            var result = sources[0];
            for (var i = 1; i < sources.Length; i++)
            {
                result = this.Meet(result, sources[i]);
            }

            return result;
        }
    }

    private void RecordLocalRegionBound(in BindingObligation obligation)
    {
        if (obligation is not { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: { } shorter })
        {
            return;
        }

        var evidence = obligation.Use;
        Add(longer, shorter);
        if (obligation.Equality)
        {
            Add(shorter, longer);
        }

        void Add(BoundOrigin source, BoundOrigin target)
        {
            source = this.OriginAtUse(source, evidence);
            target = this.OriginAtUse(target, evidence);
            if (ReferenceEquals(source, target) || (!IsLocalRegion(target) && target.Kind is not (OriginKind.Projection or OriginKind.Anchor or OriginKind.Intersection)))
            {
                return;
            }

            if (!this.localRegions.TryGetValue(target, out var region))
            {
                this.localRegions.Add(target, region = new());
            }

            if (!region.Parents.Contains(source))
            {
                region.Parents.Add(source);
                region.Values.Add(evidence);
                this.regionVersion++;
            }
        }
    }

    // Lowering may reuse only relations already represented in the checked graph. A new finite fit needs ownership analysis.
    private bool VerifiedRegionOutlives(BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (this.ProvesOriginOutlives(longer, shorter, use))
        {
            return true;
        }

        longer = this.OriginAtUse(longer, use);
        shorter = this.OriginAtUse(shorter, use);
        if (this.HasRegionBounds(longer))
        {
            foreach (var source in this.LocalRegionSources(longer))
            {
                if (!this.ProvesOriginOutlives(source, shorter, use) && !this.RegionContains(shorter, source))
                {
                    return false;
                }
            }

            return !this.OriginRelationFails(longer, shorter, use);
        }

        if (longer.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                if (!this.VerifiedRegionOutlives(longer.Operands[i], shorter, use))
                {
                    return false;
                }
            }

            return true;
        }

        return this.RegionContains(shorter, longer) && !this.OriginRelationFails(longer, shorter, use);
    }

    private bool CheckLocalTypeUse(BoundType actual, BoundType expected, Koto use)
        => (HasLocalRegion(actual) || HasLocalRegion(expected)) && this.CheckTypeUse(actual, expected, use);

    private OriginJudgment JudgeLocalRegion(BoundOrigin longer, BoundOrigin shorter, Koto use)
    {
        if (!FixedOrigin(shorter))
        {
            return OriginJudgment.Proven;
        }

        var sources = this.LocalRegionSources(longer);
        var result = OriginJudgment.Proven;
        foreach (var source in sources)
        {
            var judgment = this.JudgeOriginAtoms(source, shorter, use);
            if (judgment == OriginJudgment.Refuted)
            {
                return judgment;
            }

            if (judgment == OriginJudgment.Unknown)
            {
                result = judgment;
            }
        }

        return result;
    }

    private sealed class LocalRegion
    {
        internal List<BoundOrigin> Parents { get; } = new();

        internal List<BoundOrigin> Sources { get; } = new();

        internal List<Koto> Values { get; } = new();

        internal int Version { get; set; } = -1;
    }
}
