// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The local-region graph (SPEC 15.3.6, 15.4.4): the region slots of a region owner's Type (IsRegionOwner: every local, a var Pattern
// binding or a var for slot; LocalRegionSlot), the bounds recorded on them (RecordLocalRegionBound, RecordRegionBound), their finite
// sources (LocalRegionSources) and the value that brought a source in (LocalRegionSourceUse), and the relations judged through those
// sources (RegionContains, JudgeLocalRegion). A row is read through one find (Region) and written through one create (WrittenRegion).
// The rows are a pass part's (PassPart.Regions; DECISIONS BU-220's D-9 line): the find reads the library layer's part first, then the
// running pass's, and a new row goes into the running pass's. The Types that carry the regions are Binding.LocalRegions.Types.cs's.
// Output (PLAN G492-a (ii)): a `let` local's region has a value, the Origin its declaration fixes for the slot (PublishRegionValue: the
// initializer's Origin there, or the omitted slot's solved candidate), also recorded as a bound of the region. Its readers at a use:
// RegionValueAtUse (an Origin; SuppliedAtUse and so OriginAtUse read through it) and RegionValuesAt (a Type), RegionalAt through it.
// Invariants: a value holds no region of its own body (it is read once, when published); values and bounds are reset each pass
// (ResetLocalRegions); a `var` region has no value. A bound's parent is stored as its store read it at the bound's use (the owner's
// O-385 (a): a body row's ends as the row reads them, SuppliedAtUse with the row; a `let` value as published; an initializer alias as
// its producers read it) and read as held: LocalRegionSources, LocalRegionBound and LocalRegionSourceUse apply the receiver resolution
// alone (ReceiverAtUse; a Closure is classified after its body's bounds are stored), never OriginAtUse (DECISIONS R4-M133 as amended).
// A written Origin binds as the slot it names, not its value (BindOrigin through SlotAtUse): the value is read at each use, by the
// readers above, and a written Origin's region is what a comparison constrains.
// Must not: let a reader at a use in the local's own body take a `let` slot as written. The one exception, decided at one place
// (Binding.OriginProof's OriginAtUse with the comparison): a Callable comparison's relation, in its walk and its conditions alike, is a
// later use that constrains the slot, so it reads the region, an inference variable (SlotAtUse; the owner's O-363 (a)). It constrains
// the region only where the region is named, so a read elsewhere still takes the value (O-375 Q1 (a)). A nested function's signature or
// body reads the region, fixed there (SPEC 15.3.4, DECISIONS BU-28).
public sealed partial class Binding
{
    private readonly List<BoundOrigin> regionWork = new();
    private readonly HashSet<BoundOrigin> regionVisited = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Koto? Value, BoundOrigin? Parent)> regionEvidence = new();

    // The running pass's part of the rows (PassPart.Regions), which its steps write and reset; the driver hands it over (EnterPass),
    // Binding's own between Binds.
    private Dictionary<BoundOrigin, LocalRegion> localRegions;
    private int regionVersion;
    private int regionValues;

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

                if (this.Region(current) is { } parents)
                {
                    for (var i = parents.Parents.Count - 1; i >= 0; i--)
                    {
                        this.regionWork.Add(this.ReceiverAtUse(parents.Parents[i], parents.Values[i]));
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

    // The `index`-th Origin a bound recorded as outliving `origin` directly, as stored, its receivers resolved (LocalRegionSources follows
    // them to the finite sources); null past the last.
    internal BoundOrigin? LocalRegionBound(BoundOrigin origin, int index)
        => this.Region(origin) is { } region && index < region.Parents.Count
            ? this.ReceiverAtUse(region.Parents[index], region.Values[index]) : null;

    internal bool HasRegionBounds(BoundOrigin origin)
        => IsLocalRegion(origin) || this.Region(origin) is { Parents.Count: not 0 };

    // The value that brought `source` into the region graph `region` reads, by LocalRegionSources' walk (each parent as stored, its
    // receivers resolved at its bound's value, meet operands expanded, a meet `region` through its region operands): the value of the bound
    // whose parent is `source` or a meet holding it, with that parent as stored (before the receiver resolution, the atom a record's Borrow
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
                    var parent = this.ReceiverAtUse(bounds.Parents[i], bounds.Values[i]);
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

    // SPEC 15.3.6, 15.4.4 (PLAN G492-a (ii), revision 2): the Origin a region of a `let` local stands for where its own body reads it, its
    // value, which its declaration fixes (the initializer's Origin at that slot, or the slot's solved annotation candidate): a body nested
    // in that body, or a nested signature, reads the region itself, fixed there (15.3.4; DECISIONS BU-28). A value holds no region of its
    // own body (PublishRegionValue reads it once), so a read is one step per region and a meet's operands are read once each. A Callable
    // comparison's relation does not call it: it reads the region (the owner's O-363 (a); the contract above).
    internal BoundOrigin RegionValueAtUse(BoundOrigin origin, Koto use)
        => this.regionValues == 0 || origin.Kind is not (OriginKind.Inference or OriginKind.Intersection) ? origin : this.RegionValueIn(origin, BodyOwner(use));

    // The innermost function whose body holds `node`: a Closure's and a nested function's signature and body are their own.
    private static Koto? BodyOwner(Koto node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto)
            {
                return current;
            }
        }

        return null;
    }

    private BoundOrigin RegionValueIn(BoundOrigin origin, Koto? body)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return this.Region(origin) is { Value: { } value } region && ReferenceEquals(region.Body, body) ? value : origin;
        }

        var first = 0;
        var read = origin;
        for (; first < origin.Operands.Count; first++)
        {
            read = this.RegionValueIn(origin.Operands[first], body);
            if (!ReferenceEquals(read, origin.Operands[first]))
            {
                break;
            }
        }

        if (first == origin.Operands.Count)
        {
            return origin;
        }

        var resolved = BoundOrigin.Static;
        for (var i = 0; i < first; i++)
        {
            resolved = this.Meet(resolved, origin.Operands[i]);
        }

        resolved = this.Meet(resolved, read);
        for (var i = first + 1; i < origin.Operands.Count; i++)
        {
            resolved = this.Meet(resolved, this.RegionValueIn(origin.Operands[i], body));
        }

        return resolved;
    }

    // A `let` region's value (RegionValueAtUse), published by its declaration once per binding of it, read through the earlier regions of
    // the same body; also the region's one bound, so a nested body that reads the region through its sources (DECISIONS BU-127) reads
    // that value's. A re-binding publishes the same region again (the same slot) with its value read anew.
    private void PublishRegionValue(BoundOrigin region, BoundOrigin value, Koto evidence)
    {
        var record = this.WrittenRegion(region);
        record.Body = BodyOwner(region.Binder!);
        record.Value = null;
        record.Value = this.RegionValueIn(value, record.Body);
        this.regionValues++;
        this.RecordRegionBound(record.Value, region, evidence);
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
            region.Value = null;
            region.Body = null;
        }

        this.regionValues = 0;
        this.regionWork.Clear();
        this.regionVisited.Clear();
    }

    // The one find of a target Origin's row (PassPart.Find): the library layer's part first, then the running pass's; null when no
    // bound or slot was recorded on it.
    private LocalRegion? Region(BoundOrigin origin)
    {
        _ = PassPart.Find(this.Library.Layer.Part.Regions, this.localRegions, origin, out var region);
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
        longer = this.SuppliedAtUse(longer, obligation);
        shorter = this.SuppliedAtUse(shorter, obligation);
        Add(longer, shorter);
        if (obligation.Equality)
        {
            Add(shorter, longer);
        }

        void Add(BoundOrigin source, BoundOrigin target)
        {
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
            region.Parents.Add(source);
            region.Values.Add(evidence);
            this.regionVersion++;
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

    // A target Origin's row: the bounds recorded on it (Parents, each with its Values entry), the memo of its sources (Sources, current
    // while Version is the graph's version) and, for a `let` region, its value and the body that reads it so (PublishRegionValue).
    internal sealed class LocalRegion
    {
        internal List<BoundOrigin> Parents { get; } = new();

        internal List<BoundOrigin> Sources { get; } = new();

        internal List<Koto> Values { get; } = new();

        internal BoundOrigin? Value { get; set; }

        internal Koto? Body { get; set; }

        internal int Version { get; set; } = -1;
    }
}
