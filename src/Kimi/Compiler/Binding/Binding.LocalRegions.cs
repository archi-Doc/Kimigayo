// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The local-region graph (SPEC 15.3.6, 15.4.4): the region slots of a reassignable local's Type, a var local's, a var Pattern binding's
// or a var for slot's (their declarations are IsRegionOwner's; LocalRegionSlot), the contract bounds recorded on them, never a current
// value (RecordLocalRegionBound, RecordRegionBound), their finite sources (LocalRegionSources) and the value that brought a source in
// (LocalRegionSourceUse), and the relations judged through those sources (RegionContains, JudgeLocalRegion). A row is read through
// one find (Region) and written through one create (WrittenRegion). The rows are a pass part's (PassPart.Regions; DECISIONS BU-220's
// D-9 line): the find reads the library layer's part first, then the running pass's, and a new row goes into the running pass's. The
// Types that carry the regions are Binding.LocalRegions.Types.cs's.
public sealed partial class Binding
{
    private readonly List<BoundOrigin> regionWork = new();
    private readonly HashSet<BoundOrigin> regionVisited = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Koto? Value, BoundOrigin? Parent)> regionEvidence = new();

    // The running pass's part of the rows (PassPart.Regions), which its steps write and reset; the driver hands it over (EnterPass),
    // Binding's own between Binds.
    private Dictionary<BoundOrigin, LocalRegion> localRegions;
    private int regionVersion;

    // The region graph's version: every recorded bound moves it, so a body's last one is the whole-body graph.
    internal int RegionVersion => this.regionVersion;

    internal static bool IsLocalRegion(BoundOrigin origin)
        => origin.Open || (origin is { Kind: OriginKind.Inference, Binder: { } binder, Slot: < 0 } && IsRegionOwner(binder));

    // SPEC 15.4.4, 15.6.5: the declarations whose Types have region slots: every local, and a reassignable Pattern binding or for slot.
    internal static bool IsRegionOwner(Koto declaration) => declaration is VariableKoto || IsMutableDeclaration(declaration);

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
        if (this.Region(origin) is not { } region)
        {
            return [];
        }

        if (!Current(region.Version, this.regionVersion))
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

                if (this.Region(current) is { } parents)
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
        => IsLocalRegion(origin) || this.Region(origin) is { Parents.Count: not 0 };

    // The value that brought `source` into the region graph `region` reads, by LocalRegionSources' walk (each parent read through
    // OriginAtUse at its bound's value, meet operands expanded, a meet `region` through its region operands): the value of the bound whose
    // parent is `source` or a meet holding it, with that parent as the bound recorded it (before OriginAtUse, the atom a record's Borrow
    // carries; DECISIONS R4-M133); null when `region` holds `source` itself or does not reach it. A local's own initializer stands for its
    // declaration.
    internal (Koto Value, BoundOrigin Parent)? LocalRegionSourceUse(BoundOrigin region, BoundOrigin source)
    {
        this.regionWork.Clear();
        this.regionEvidence.Clear();
        this.regionVisited.Clear();
        this.regionWork.Add(region);
        this.regionEvidence.Add((null, null));
        while (this.regionWork.Count != 0)
        {
            var current = this.regionWork[^1];
            var evidence = this.regionEvidence[^1];
            this.regionWork.RemoveAt(this.regionWork.Count - 1);
            this.regionEvidence.RemoveAt(this.regionEvidence.Count - 1);
            if (ReferenceEquals(current, source))
            {
                return Use(evidence.Value, evidence.Parent);
            }

            if (!this.regionVisited.Add(current))
            {
                continue;
            }

            if (this.Region(current) is { } bounds)
            {
                for (var i = 0; i < bounds.Parents.Count; i++)
                {
                    var parent = this.OriginAtUse(bounds.Parents[i], bounds.Values[i]);
                    if (ReferenceEquals(parent, source))
                    {
                        return Use(bounds.Values[i], bounds.Parents[i]); // A direct parent first, before a longer path.
                    }

                    this.regionWork.Add(parent);
                    this.regionEvidence.Add((bounds.Values[i], bounds.Parents[i]));
                }
            }

            for (var i = 0; i < current.Operands.Count; i++)
            {
                this.regionWork.Add(current.Operands[i]);
                this.regionEvidence.Add(evidence);
            }
        }

        return null;

        static (Koto Value, BoundOrigin Parent)? Use(Koto? value, BoundOrigin? parent)
            => value is null || parent is null ? null : (value is VariableKoto { InitializerKoto: { } initializer } ? initializer : value, parent);
    }

    // An Origin of atoms of the frozen library table only: such an atom, or a meet of such (contract C's library-only key).
    private static bool LibraryOnly(BoundOrigin origin)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return origin.Binder?.SyntaxOwner is { Frozen: true };
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (!LibraryOnly(origin.Operands[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Contract C (PLAN G949 S1-1, s11-design 4.1): a row of the layer's part is frozen (FrozenRegion, FreezeLocalRegions; a program row
    // never is), so a new parent or solution on it, which the region graph's two mutations would write, is the internal-invariant form.
    // An identical re-record writes nothing and is not refused.
    private static void EnsureWritable(LocalRegion region)
    {
        if (region.Version == FrozenRegion)
        {
            throw new InvalidOperationException("A pass wrote a frozen library region (InternalInvariant; PLAN G949 S1-1).");
        }
    }

    // O25 (PLAN G949 S1-1, R): every row of the running part has its sources for the run's version, so no later pass forms a source
    // list on a row another pass made.
    private void CompleteLocalRegions()
    {
        foreach (var origin in this.localRegions.Keys)
        {
            this.LocalRegionSources(origin);
        }
    }

    // PLAN G949 S1-1's freeze of the layer's part (FreezeLibrary, after CompleteLocalRegions): each row with its sources for the run
    // reads current in every later pass (FrozenRegion, never the "never computed" -1).
    private void FreezeLocalRegions()
    {
        foreach (var region in this.localRegions.Values)
        {
            if (region.Version == this.regionVersion)
            {
                region.Version = FrozenRegion;
            }
        }
    }

    // The running pass's rows, emptied in place; the version starts again per run (BeginRun).
    private void ResetLocalRegions()
    {
        foreach (var region in this.localRegions.Values)
        {
            region.Parents.Clear();
            region.Values.Clear();
            region.Sources.Clear();
            region.Version = -1;
            region.Solved = null;
        }

        this.regionWork.Clear();
        this.regionVisited.Clear();
    }

    // The one find of a target Origin's row (PassPart.Find): the library layer's part first, then the running pass's; null when no
    // bound or slot was recorded on it.
    private LocalRegion? Region(BoundOrigin origin)
    {
        _ = PassPart.Find(PassPart.Parts(this.Library.Layer.Part.Regions, this.localRegions), origin, out var region);
        return region;
    }

    // A write's row: the one the find returns, else a new row in the running pass's part, so an Origin has one row.
    private LocalRegion WrittenRegion(BoundOrigin origin)
    {
        if (this.Region(origin) is { } region)
        {
            return region;
        }

        region = new();
        if (LibraryOnly(origin))
        {
            // Contract C (s11-design 4.1; n55-design 2.3 rule 6): no pass after the freeze records a bound on a target of frozen library
            // atoms only, whose row the layer's part would hold; a frozen Sources would miss it silently.
            throw new InvalidOperationException("A pass after the freeze made a region row of library atoms only (InternalInvariant; PLAN G949 S1-1).");
        }

        this.localRegions.Add(origin, region);
        return region;
    }

    private BoundOrigin LocalRegionSlot(Koto owner, int slot)
    {
        var origin = this.OriginAtom(owner, OriginKind.Inference, slot);
        _ = this.WrittenRegion(origin);
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

        var region = this.WrittenRegion(target);
        if (!region.Parents.Contains(source))
        {
            EnsureWritable(region);
            region.Parents.Add(source);
            region.Values.Add(evidence);
            this.regionVersion++;
        }

        // J2: a bound of a waited variable's own solution `(E and region)` bounds its region too (RecordOpening).
        if (region.Solved is { } solved && BoundsSolved(source, target, solved))
        {
            this.RecordRegionBound(source, solved, evidence);
        }
    }

    // J2 (SPEC 15.3.6, 15.2.2; the owner's O-358 (a), O-362 (a); PLAN G936): the record of a region the selected call's late solve
    // opened beside the evidence candidate `bound` (OriginInference.Opened). The region lies within `bound`, and within the longer
    // end of every relation whose shorter end is the variable's own solution `(bound and region)`: the waiting part's fit, recorded
    // at its completion before this, a well-formedness relation over the solution, and any relation formed later
    // (RecordRegionBound reads Solved). Both are free: with the shorter end exactly the solution, replacing the region by its meet
    // with the longer end keeps every requirement over the solution (SPEC 15.2.2). So the region has the sources that the oracle's
    // reading of a region needs (UniversalRelations.Outlives, SourcesIn), and no row is added. A trial records nothing.
    private void RecordOpening(BoundOrigin region, BoundOrigin bound, Koto call)
    {
        this.RecordRegionBound(bound, region, call);
        var solution = this.Meet(bound, region);
        if (ReferenceEquals(solution, region))
        {
            return; // `bound` is static: the solution is the region itself, whose bounds are recorded on it.
        }

        var entry = this.WrittenRegion(solution);
        if (!ReferenceEquals(entry.Solved, region))
        {
            EnsureWritable(entry);
            entry.Solved = region;
        }

        for (var i = 0; i < entry.Parents.Count; i++)
        {
            if (BoundsSolved(entry.Parents[i], solution, region))
            {
                this.RecordRegionBound(entry.Parents[i], region, entry.Values[i]);
            }
        }
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

    // A target Origin's row: the bounds recorded on it (Parents, each with its Values entry) and the memo of its sources (Sources,
    // current while Version is the graph's version).
    internal sealed class LocalRegion
    {
        internal List<BoundOrigin> Parents { get; } = new();

        internal List<BoundOrigin> Sources { get; } = new();

        internal List<Koto> Values { get; } = new();

        internal int Version { get; set; } = -1;

        // J2: for a waited variable's own solution `(E and region)`, its region (RecordOpening); null for any other Origin.
        internal BoundOrigin? Solved { get; set; }
    }
}
