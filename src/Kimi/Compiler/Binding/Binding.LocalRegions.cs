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
// RegionValueAtUse (an Origin; SuppliedAtUse and so OriginAtUse read through it) and RegionValuesAt (a Type), RegionalAt through it; in
// a nested body, RegionValue (the view's row 6, which A5 reads where the region fails at a longer end): BU-127's solved value of a
// `let` or `var` region, a `var` region's the meet of its recorded bounds (the owner's O-422 (a)); wherever asked, LetRegionValue (a
// `let` region's value as published, for StaticOrigin's Owned judgment; B's ruling 28).
// Invariants: a value holds no region of its own body (it is read once, when published); values and bounds are reset each pass
// (ResetLocalRegions); a `var` region publishes no value: a use in its own body reads the region, and only RegionValue (a nested body's
// row 6) reads its meet of recorded bounds. A bound's parent is stored as its store read it at the bound's use (the owner's
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

    // RegionValue's scratch (its walk, the bound atoms it reads, and ReachesRegion's walk, which runs inside it), reused.
    private readonly List<BoundOrigin> valueWork = new();
    private readonly List<BoundOrigin> valueAtoms = new();
    private readonly HashSet<BoundOrigin> valueRead = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundOrigin> reachWork = new();
    private readonly List<BoundOrigin> reachAtoms = new();
    private readonly HashSet<BoundOrigin> reachSeen = new(ReferenceEqualityComparer.Instance);

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

    // B's ruling 28 (SPEC 15.2.3, 15.4.4): an Origin with each `let` region, itself or a meet's operand, read as its published value
    // wherever it is asked, not only at a use in the region's own body: the value is a fact of the region, fixed at its declaration (the
    // reading of Binding.OriginRows' AddRegionValueRows). A judgment of what the Origin itself outlives reads it (StaticOrigin's Owned
    // judgment); a use that constrains the region does not. A `var` region publishes no value and reads as itself.
    internal BoundOrigin LetRegionValue(BoundOrigin origin)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return this.Region(origin)?.Value ?? origin;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (this.Region(origin.Operands[i]) is { Value: not null })
            {
                var read = BoundOrigin.Static;
                for (var k = 0; k < origin.Operands.Count; k++)
                {
                    read = this.Meet(read, this.LetRegionValue(origin.Operands[k]));
                }

                return read;
            }
        }

        return origin;
    }

    // DECISIONS BU-127's solved value of a local region (the owner's O-422 (a)), null for any other Origin and for a region with no bound:
    // a `let` region's value as published; a `var` region's meet of its recorded bounds, complete once its body is bound (a nested body
    // never bounds an environment region, BU-28), each read as stored with its receivers resolved and a `let` region of its own body as
    // its value. The view's row 6 carries it for a body nested in the region's own, where the region at a longer end is judged as its
    // value (MirOrigins.Unproven; the converse of O-400 (b)'s lemma). A `var` region of the own body that reaches back to the region (a
    // cycle of definitions, which share one solved value) is read through its own bounds, so a value names regions of enclosing bodies
    // and `var` regions of its own body that do not reach the region: each read moves down that body's definitions or one body outward.
    // A `var` region's value is computed once per graph version (its bounds are complete once its body is bound; the owner's O-426 (a));
    // a frozen row of the library layer is read, never written (contract C, PLAN G949 S1-1), so its value is computed at each read.
    internal BoundOrigin? RegionValue(BoundOrigin origin)
    {
        var region = this.Region(origin);
        if (region is null || region.Value is not null || origin.Binder is not { } binder || !OwnVarRegion(origin, BodyOwner(binder)))
        {
            return region?.Value;
        }

        if (region.SolutionVersion == this.regionVersion)
        {
            return region.Solution;
        }

        var body = BodyOwner(binder);
        var (value, bounded) = (BoundOrigin.Static, false);
        (var work, var read) = (this.valueWork, this.valueRead);
        work.Clear();
        read.Clear();
        work.Add(origin);
        read.Add(origin);
        while (work.Count != 0)
        {
            var current = work[^1];
            work.RemoveAt(work.Count - 1);
            var atoms = this.BoundAtoms(current, body, this.valueAtoms);
            for (var i = 0; i < atoms.Count; i++)
            {
                var atom = atoms[i];
                if (!OwnVarRegion(atom, body) || !this.ReachesRegion(atom, origin, body))
                {
                    (value, bounded) = (this.Meet(value, atom), true);
                }
                else if (read.Add(atom))
                {
                    work.Add(atom);
                }
            }
        }

        var solution = bounded ? value : null;
        if (region.Version != FrozenRegion)
        {
            (region.Solution, region.SolutionVersion) = (solution, this.regionVersion); // Contract C: a frozen library row is never written.
        }

        return solution;
    }

    // Whether `origin` is a `var` region of `body` (a reassignable region owner's region; O-422 (a)).
    private static bool OwnVarRegion(BoundOrigin origin, Koto? body)
        => origin is { Kind: OriginKind.Inference, Open: false, Binder: { } binder, Slot: < 0 } && IsRegionOwner(binder) && IsMutableDeclaration(binder) &&
            ReferenceEquals(BodyOwner(binder), body);

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

    // The atoms of a region's recorded bounds as RegionValue reads them, into `atoms` (cleared; RegionValue and ReachesRegion each pass
    // their own, as they nest): each bound as stored, its receivers resolved, a `let` region of `body` as its value, a meet as its operands.
    private List<BoundOrigin> BoundAtoms(BoundOrigin region, Koto? body, List<BoundOrigin> atoms)
    {
        atoms.Clear();
        for (var i = 0; this.Region(region) is { } bounds && i < bounds.Parents.Count; i++)
        {
            var bound = this.RegionValueIn(this.ReceiverAtUse(bounds.Parents[i], bounds.Values[i]), body);
            for (var k = 0; k < (bound.Kind == OriginKind.Intersection ? bound.Operands.Count : 1); k++)
            {
                atoms.Add(bound.Kind == OriginKind.Intersection ? bound.Operands[k] : bound);
            }
        }

        return atoms;
    }

    // Whether the recorded bounds of `var` regions of `body` lead from `from` back to `target` (a cycle of definitions; RegionValue).
    private bool ReachesRegion(BoundOrigin from, BoundOrigin target, Koto? body)
    {
        (var work, var seen) = (this.reachWork, this.reachSeen);
        work.Clear();
        seen.Clear();
        work.Add(from);
        seen.Add(from);
        while (work.Count != 0)
        {
            var current = work[^1];
            work.RemoveAt(work.Count - 1);
            var atoms = this.BoundAtoms(current, body, this.reachAtoms);
            for (var i = 0; i < atoms.Count; i++)
            {
                var atom = atoms[i];
                if (ReferenceEquals(atom, target))
                {
                    return true;
                }

                if (OwnVarRegion(atom, body) && seen.Add(atom))
                {
                    work.Add(atom);
                }
            }
        }

        return false;
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
                (region.Solution, region.SolutionVersion) = (null, FrozenRegion); // RegionValue computes a frozen row's value per read.
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
            region.Value = null;
            region.Body = null;
            region.Solved = null;
            (region.Solution, region.SolutionVersion) = (null, -1);
        }

        this.regionValues = 0;
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

        // J2: for a waited variable's own solution `(E and region)`, its region (RecordOpening); null for any other Origin.
        internal BoundOrigin? Solved { get; set; }

        // A `var` region's BU-127 solved value (RegionValue) and the graph version it was computed at (-1: none).
        internal BoundOrigin? Solution { get; set; }

        internal int SolutionVersion { get; set; } = -1;
    }
}
