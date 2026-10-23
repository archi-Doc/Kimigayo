// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The local-region graph (SPEC 15.3.6, 15.4.4): the region slots of a mutable local's Type (LocalRegionSlot), the contract bounds
// recorded on them, never a current value (RecordLocalRegionBound, RecordRegionBound), their finite sources (LocalRegionSources) and the
// value that brought a source in (LocalRegionSourceUse), and the relations judged through those sources (RegionContains,
// VerifiedRegionOutlives, JudgeLocalRegion). The Types that carry the regions are Binding.LocalRegions.Types.cs's.
public sealed partial class Binding
{
    private readonly Dictionary<BoundOrigin, LocalRegion> localRegions = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundOrigin> regionWork = new();
    private readonly HashSet<BoundOrigin> regionVisited = new(ReferenceEqualityComparer.Instance);
    private readonly List<Koto?> regionEvidence = new();
    private int regionVersion;

    internal static bool IsLocalRegion(BoundOrigin origin)
        => origin.Open || origin is { Kind: OriginKind.Inference, Binder: VariableKoto, Slot: < 0 };

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

                if (!ReferenceEquals(current, origin) && EnclosingAtom(current, origin.Body))
                {
                    region.Sources.Add(current); // SPEC 15.6.5: fixed in the region's own body, so a source there, never followed.
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
                else if (!IsLocalRegion(current) && current.Kind != OriginKind.Inference)
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

    // The value that brought `source` into the region graph `region` reads, by LocalRegionSources' walk (each parent read through
    // OriginAtUse at its bound's value, meet operands expanded, a meet `region` through its region operands): the value of the bound whose
    // parent is `source` or a meet holding it; null when `region` holds `source` itself or does not reach it. A local's own initializer
    // stands for its declaration.
    internal Koto? LocalRegionSourceUse(BoundOrigin region, BoundOrigin source)
    {
        this.regionWork.Clear();
        this.regionEvidence.Clear();
        this.regionVisited.Clear();
        this.regionWork.Add(region);
        this.regionEvidence.Add(null);
        while (this.regionWork.Count != 0)
        {
            var current = this.regionWork[^1];
            var evidence = this.regionEvidence[^1];
            this.regionWork.RemoveAt(this.regionWork.Count - 1);
            this.regionEvidence.RemoveAt(this.regionEvidence.Count - 1);
            if (ReferenceEquals(current, source))
            {
                return Use(evidence);
            }

            if (!this.regionVisited.Add(current))
            {
                continue;
            }

            if (this.localRegions.TryGetValue(current, out var bounds))
            {
                for (var i = 0; i < bounds.Parents.Count; i++)
                {
                    var parent = this.OriginAtUse(bounds.Parents[i], bounds.Values[i]);
                    if (ReferenceEquals(parent, source))
                    {
                        return Use(bounds.Values[i]); // A direct parent first, before a longer path.
                    }

                    this.regionWork.Add(parent);
                    this.regionEvidence.Add(bounds.Values[i]);
                }
            }

            for (var i = 0; i < current.Operands.Count; i++)
            {
                this.regionWork.Add(current.Operands[i]);
                this.regionEvidence.Add(evidence);
            }
        }

        return null;

        static Koto? Use(Koto? value) => value is VariableKoto { InitializerKoto: { } initializer } ? initializer : value;
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

    private void RecordLocalRegionBound(in BindingObligation obligation)
    {
        if (obligation is not { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: { } shorter })
        {
            return;
        }

        var evidence = obligation.Use;
        var body = obligation.Body;
        Add(longer, shorter);
        if (obligation.Equality)
        {
            Add(shorter, longer);
        }

        void Add(BoundOrigin source, BoundOrigin target)
        {
            source = this.OriginAtUse(source, evidence);
            target = this.OriginAtUse(target, evidence);
            if (ReferenceEquals(source, target) || !BoundableIn(target, body))
            {
                return;
            }

            this.RecordRegionBound(source, target, evidence);
        }
    }

    // Also publishes solved initializer aliases: call plans keep their original atoms, while every stored value must
    // reach the same checked source through them. An alias contributes no independent finite Loan anchor.
    private void RecordRegionBound(BoundOrigin source, BoundOrigin target, Koto evidence)
    {
        if (ReferenceEquals(source, target))
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

    // Lowering may reuse only relations already represented in the checked graph. A new finite fit needs ownership analysis.
    // Its readers, lowering and the old analysis's RecordCallInput, run after Binding judged the fit in its body, so it judges in no body
    // (DECISIONS BU-28).
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

            return !this.OriginRelationFails(longer, shorter, use, default);
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

        return this.RegionContains(shorter, longer) && !this.OriginRelationFails(longer, shorter, use, default);
    }

    private OriginJudgment JudgeLocalRegion(BoundOrigin longer, BoundOrigin shorter, Koto use, ExecutableBody body, ulong condition = 0)
    {
        if (!FixedIn(shorter, body))
        {
            return OriginJudgment.Proven;
        }

        var sources = this.LocalRegionSources(longer);
        var result = OriginJudgment.Proven;
        foreach (var source in sources)
        {
            var judgment = this.JudgeOriginAtoms(source, shorter, use, body, condition);
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
