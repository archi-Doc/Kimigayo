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
        => origin is { Kind: OriginKind.Inference, Binder: VariableKoto, Slot: < 0 };

    internal static bool HasLocalRegion(BoundType type)
    {
        if (!type.CarriesOrigin || type.Kind is BoundTypeKind.Function or BoundTypeKind.FunctionItem)
        {
            return false;
        }

        if (type.Origin is { } origin && IsLocalRegion(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (IsLocalRegion(type.OriginArguments[i]))
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
                    this.regionWork.AddRange(parents.Parents);
                }
                else if (current.Kind == OriginKind.Intersection)
                {
                    for (var i = 0; i < current.Operands.Count; i++)
                    {
                        this.regionWork.Add(current.Operands[i]);
                    }
                }
                else
                {
                    region.Sources.Add(current);
                }
            }

            region.Version = this.regionVersion;
        }

        return CollectionsMarshal.AsSpan(region.Sources);
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

    private BoundType LocalBorrowType(BoundType type, VariableKoto owner, Koto value)
    {
        // U7 opens the outer borrow slot of mutable locals. Structural and nested storage inference share this path in U9.
        if (!IsMutableDeclaration(owner) || type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Origin: { } source })
        {
            return type;
        }

        var origin = this.OriginAtom(owner, OriginKind.Inference, -1);
        if (!this.localRegions.ContainsKey(origin))
        {
            this.localRegions.Add(origin, new());
        }

        var result = this.WithOrigins(type, origin, (BoundOrigin[])type.OriginArguments);
        this.AddObligation(new(BindingObligationKind.OriginOutlives, value, BindingDeadline.BodyOrigins, result, source, origin));
        return result;
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
            if (!ReferenceEquals(source, target) && this.localRegions.TryGetValue(target, out var region) && !region.Parents.Contains(source))
            {
                region.Parents.Add(source);
                region.Values.Add(evidence);
                this.regionVersion++;
            }
        }
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
        var result = sources.IsEmpty ? OriginJudgment.Unknown : OriginJudgment.Proven;
        foreach (var source in sources)
        {
            var judgment = this.JudgeOriginRelation(source, shorter, use);
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
