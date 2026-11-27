// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The local-region graph (SPEC 15.3.6, 15.4.4): the region slots of a region owner's Type (IsRegionOwner: every local, Pattern binding
// and for slot, and a Subject borrowed in place (its match or for construct; SPEC 15.1.6); LocalRegionSlot, with its row; ValuedRegion,
// one with its value), the bounds recorded on them (RecordLocalRegionBound, RecordRegionBound) and the lower bounds a slot region is
// recorded to outlive (RecordRegionTarget), their finite sources (LocalRegionSources) and the value that brought a source in
// (LocalRegionSourceUse), and the relations judged through those sources (RegionContains, JudgeLocalRegion). A row is read through one
// find (Region) and written through one create (WrittenRegion).
// The rows are a pass part's (PassPart.Regions; DECISIONS BU-220's D-9 line): the find reads the library layer's part first, then the
// running pass's, and a new row goes into the running pass's. The Types that carry the regions are Binding.LocalRegions.Types.cs's.
// Output (PLAN G492-a (ii); R7-O2 A1, A4-L): a `let` local's region has a value, the Origin its declaration fixes for the slot
// (PublishRegionValue: an unwritten slot's, the initializer's Origin there, LocalBorrowType; a slot its written Type omits, the
// candidate of SPEC 15.3.6's table over the bounds its declaration recorded, FixDeclaredRegions, or none), also recorded as a bound of
// the region, an inclusion (the value outlives the region); a slot that the local's equality fixes, `let` or `var`, is a region whose
// value is the equality's other end as the body reads it, an equality, or an inclusion where that end holds a region published as one
// (FixedRegion, LocalRegion.Equality; SPEC 15.3.6 "Unless fixed by equality", PLAN G1369). Its
// readers at a use: RegionValueAtUse (an Origin; SuppliedAtUse and so OriginAtUse read through it) and RegionValuesAt (a Type),
// RegionalAt and the typed borrow's identity test (SPEC 13.5.6) through it. RegionValue is the
// one reading of any local region's SPEC 15.3.6 value: a published value as published, any other `var` region's its row's candidate
// over its recorded bounds (SPEC 15.3.6's table); a body that carries the region (CarriedRegion) reads it so (the view's row 6, the
// body's oracle, StaticOrigin through ValueIn). ValueIn reads a deferred result slot's atom (R7-O2 O2-F) as its completion once Final
// proved it, and before as a reading with no answer yet (the deferral fact). A `let` region that merges a stored part met on several
// of the local's paths is marked so (LocalRegion.Merged, LocalBorrowType), read by the name limit alone (PLAN G922 U1).
// FixDeclaredRegions (R7-O2 A4-L) reads a local's declaration once its last bound is recorded (its fit, its Type's well-formedness, its
// clauses, which it records itself as region bounds) and publishes each of a `let` local's regions with no value with its own
// candidate, or leaves it without one; a sibling region that reads back to it stays that variable (FixWaiting; the owner's O-544 (b)).
// It adds no obligation row.
// Invariants: a value holds no region of its own body that publishes a value (each is read through once, when published), so a read at
// a use is one step; it may hold a `var` region of its own body that publishes none (a `let` initializer, or an equality, naming a `var`
// local), which a use in that body reads as the region and RegionValue as its candidate, whose walk reads each bound's region with a
// value through it and the body's `var` regions that reach back to the region through their bounds, so each further read moves down
// that body's definitions or one body outward and ends (DECISIONS R8-M56 (ii) fact (a), as amended); values and bounds are reset each pass
// (ResetLocalRegions); a `var` region publishes no value unless its slot is fixed by equality (SPEC 15.3.6): a use in its own body
// reads the region, and only RegionValue (where a body carries it, or its own body's oracle) reads its candidate over its recorded
// bounds. A bound's parent is stored as its store read it at the bound's use (the owner's
// O-385 (a): a body row's ends as the row reads them, SuppliedAtUse with the row; a `let` value as published; an initializer alias as
// its producers read it) and read as held: LocalRegionSources, LocalRegionBound and LocalRegionSourceUse apply the receiver resolution
// alone (ReceiverAtUse; a Closure is classified after its body's bounds are stored), never OriginAtUse (DECISIONS R4-M133 as amended).
// A written Origin binds as the slot it names, not its value (BindOrigin through SlotAtUse): the value is read at each use, by the
// readers above, and a written Origin's region is what a comparison constrains.
// Must not: let a reader at a use in the local's own body take a `let` slot as written. The one exception, decided at one place
// (Binding.OriginProof's OriginAtUse with the comparison): a Callable comparison's relation, in its walk and its conditions alike, is a
// later use that constrains the slot, so it reads the region, an inference variable (SlotAtUse; the owner's O-363 (a)). It constrains
// the region only where the region is named, so a read elsewhere still takes the value (O-375 Q1 (a)). A nested function's signature or
// body reads the region, fixed there (SPEC 15.3.4, DECISIONS BU-28), as a fixed Origin equal to its value where the body carries it
// (CarriedRegion; SPEC 15.6.5's Closure sentence).
public sealed partial class Binding
{
    private readonly List<BoundOrigin> regionWork = new();
    private readonly HashSet<BoundOrigin> regionVisited = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Koto? Value, BoundOrigin? Parent)> regionEvidence = new();

    // UpperBound's scratch (its walk, the bound atoms it reads, and ReachesRegion's walk, which runs inside it), reused.
    private readonly List<BoundOrigin> valueWork = new();
    private readonly List<BoundOrigin> valueAtoms = new();
    private readonly HashSet<BoundOrigin> valueRead = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundOrigin> reachWork = new();
    private readonly List<BoundOrigin> reachAtoms = new();
    private readonly HashSet<BoundOrigin> reachSeen = new(ReferenceEqualityComparer.Instance);

    // FixDeclaredRegions' scratch, its own as the candidates and proofs it runs use the lists above: each waiting `let` region's visit
    // index (-1 unvisited, int.MaxValue fixed), the visit's stack of the regions not yet fixed, and a closing set's candidates, one per
    // member in the stack's order, reused.
    private readonly Dictionary<BoundOrigin, int> waitingIndex = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundOrigin> waitingSet = new();
    private readonly List<BoundOrigin?> waitingCandidates = new();

    // ValueIn's reads once no body is being bound, per (Origin, body) and region-graph version (valueInVersion; stale at each run's start,
    // BeginRun): a DAG of regions is read in turn, so an unmemoized reading is exponential in its depth; reused. The body is keyed by its
    // SourceRef (R7-N5 B8; DECISIONS BU-220: one run's memo, within one numbering; a module's top-level statements have no symbol).
    private readonly Dictionary<(BoundOrigin Origin, SourceRef Body), BoundOrigin> valueInReads = new();

    // The running pass's part of the rows (PassPart.Regions), which its steps write and reset; the driver hands it over (EnterPass),
    // Binding's own between Binds.
    private Dictionary<BoundOrigin, LocalRegion> localRegions;
    private int regionVersion;
    private int regionValues;
    private int valueInVersion = -1;

    // The deferral fact (R7-O2 A1+A2; the owner's O-471 (a)): whether a ValueIn reading since its reader cleared it met a local region with
    // no answer yet (RegionBounded false), which stays the region: the reader's answer depends on that body's binding (StaticOrigin).
    private bool readsUnboundedRegion;

    // The region graph's version: every recorded bound moves it, so a body's last one is the whole-body graph.
    internal int RegionVersion => this.regionVersion;

    internal static bool IsLocalRegion(BoundOrigin origin)
        => origin.Open || (origin is { Kind: OriginKind.Inference, Binder: { } binder, Slot: < 0 } && IsRegionOwner(binder));

    // SPEC 14.8.2, 14.6.1, 15.4.4, 15.6.5, 15.1.6: the declarations whose Types have region slots, the body locals (DeclaringPart).
    internal static bool IsRegionOwner(Koto declaration) => DeclaringPart(declaration) is not null;

    // The region owners, one row each, and each one's declaring part, the node a use within is "the one being declared" (LocalRegionOf;
    // ruling G922-34): every local, every Pattern binding (a BindingPattern form) and every for slot (a name a ForKoto declares, never its
    // Iterable) is its own; a Subject borrowed in place (SPEC 15.1.6) is owned by its match or for construct and declared by its Subject,
    // the match's Expression or the for construct's Iterable. Any other declaration owns no region: null.
    internal static Koto? DeclaringPart(Koto declaration)
        => declaration switch
        {
            VariableKoto or SyntaxFormKoto { Akind: KotoKind.BindingPattern } => declaration,
            IdentifierNameKoto { Parent: ForKoto loop } when !ReferenceEquals(loop.Iterable, declaration) => declaration,
            MatchKoto match => match.Expression,
            ForKoto loop => loop.Iterable,
            _ => null,
        };

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
    // in that body, or a nested signature, reads the region itself, fixed there (15.3.4; DECISIONS BU-28; equal to its value where the body
    // carries it, which the oracle and ValueIn read, not this). A value holds no region of its
    // own body with a value (PublishRegionValue reads each through once), so a read is one step per region and a meet's operands are read
    // once each. A Callable
    // comparison's relation does not call it: it reads the region (the owner's O-363 (a); the contract above).
    internal BoundOrigin RegionValueAtUse(BoundOrigin origin, Koto use)
        => this.regionValues == 0 || origin.Kind is not (OriginKind.Inference or OriginKind.Intersection) ? origin : this.RegionValueIn(origin, BodyOwner(use));

    // SPEC 15.3.6, 15.6.5 (R7-O2 A1+A2): an Origin with each region that the body of `function` reads as its value read so: a `let` region
    // of that body as its published value (as its own body reads it, RegionValueAtUse), a `var` region of that body as its candidate
    // (RegionValue: once the body is bound, A2's own-body half), a region the body carries (CarriedRegion) as its candidate, itself or a
    // meet's operand, each region of that candidate read so in turn (O-466 (b)); a region a nested signature only names (SPEC 15.3.4) and
    // a region with no value stay, and so does a region with no answer yet (RegionBounded), which sets the deferral fact
    // (readsUnboundedRegion). Each read moves one body outward or down that body's definitions; once no body is being bound each is
    // read once per region-graph version (valueInReads). StaticOrigin's Owned judgment reads it.
    internal BoundOrigin ValueIn(BoundOrigin origin, Koto? function)
    {
        if (this.formingBody.IsBody)
        {
            return this.ReadValueIn(origin, function);
        }

        if (this.valueInVersion != this.regionVersion)
        {
            this.valueInReads.Clear();
            this.valueInVersion = this.regionVersion;
        }

        var body = SourceRef.Written(function);
        Debug.Assert(function is null || body != default, "A body ValueIn's memo keys is numbered: an unnumbered one would key as no body (SourceRef.Written's default).");
        var key = (origin, body);
        if (!this.valueInReads.TryGetValue(key, out var read))
        {
            read = this.ReadValueIn(origin, function);
            this.valueInReads[key] = read;
        }

        return read;
    }

    // SPEC 15.3.6's value of a local region (R7-O2 A1: the one reading; DECISIONS BU-127, the owner's O-422 (a), O-466 (b)), null for any
    // other Origin, for a region without a candidate and for one with no answer yet, its bounds as recorded, a bound that is another
    // variable staying that variable, which a reader reads in turn: a `let` region's value as published, fixed at its declaration (SPEC
    // 15.4.4); a `var` region's candidate by its row (VarCandidate, SPEC 15.3.6's table) from the meet of its recorded upper bounds and its
    // strongest recorded lower bound (StrongestLowerBound over LocalRegion.Targets), which reads all of its bounds, so it has no answer
    // until its body is bound (RegionBounded, the gate SolvedStatic reads too; a nested body never bounds an environment region, BU-28):
    // a Binding-time judgment in a body nested in it, as a Closure's body bound at its literal, reads the region, and Final reads the
    // candidate. Each bound is read as stored with its receivers resolved and a `let` region of its own body as its value. A body that
    // carries the region reads it as this value (CarriedRegion: the view's row 6, A5's split at a longer end, the body's oracle,
    // StaticOrigin through ValueIn). A value naming a call's open region (PLAN G936's J2: the waiting part gives its bounds after
    // selection) is a value here: A5's split meets that region as the located limit (MirOrigins.NestedLimit); no region is carried in
    // turn through it and the body's oracle does not equate it (NamesOpenRegion, CarriedRegion's walk and UniversalRelations.Value), so
    // that no premise proves what the limit leaves open, until G936's late solve. A
    // `var` region of the own body that reaches back to the region (a cycle of definitions, which share one solved value) is read
    // through its own bounds, so a value names regions of enclosing bodies and `var` regions of its own body that do not reach the region:
    // each read moves down that body's definitions or one body outward. A `var` region's value is computed once per graph version once
    // its body is bound (the owner's O-426 (a)); a frozen row of the library layer is read, never written (contract C, PLAN G949 S1-1),
    // so its value is computed at each read.
    internal BoundOrigin? RegionValue(BoundOrigin origin)
    {
        var region = this.Region(origin);
        if (region is null || origin.Binder is not { } binder || !SlotRegion(origin) || region.Value is not null || !IsMutableDeclaration(binder))
        {
            return region?.Value;
        }

        if (!this.RegionBounded(origin))
        {
            return null;
        }

        if (region.SolutionVersion == this.regionVersion)
        {
            return region.Solution;
        }

        var solution = this.VarCandidate(origin, region, BodyOwner(binder), binder);
        if (region.Version != FrozenRegion)
        {
            (region.Solution, region.SolutionVersion) = (solution, this.regionVersion); // Contract C: a frozen library row is never written.
        }

        return solution;
    }

    // Whether `origin` is a region slot of a region owner's Type (LocalRegionSlot), not a call's open region.
    private static bool SlotRegion(BoundOrigin origin)
        => origin is { Kind: OriginKind.Inference, Open: false, Binder: { } binder, Slot: < 0 } && IsRegionOwner(binder);

    // Whether `origin` is a `var` region of `body` (a reassignable region owner's region; O-422 (a)).
    private static bool OwnVarRegion(BoundOrigin origin, Koto? body)
        => SlotRegion(origin) && IsMutableDeclaration(origin.Binder!) && ReferenceEquals(BodyOwner(origin.Binder!), body);

    // SPEC 15.3.6: whether a row's candidate is its strongest lower bound rather than `upper`, the meet of its upper bounds: a
    // contravariant row, and an invariant one or one at no position without an upper bound; a covariant row reads only `upper`. The one
    // reading of which bounds the table takes: VarCandidate's candidate and FixWaiting's edges.
    private static bool ReadsLowerBounds(OriginVariance row, BoundOrigin? upper)
        => row == OriginVariance.Contravariant || (row != OriginVariance.Covariant && upper is null);

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

    // ValueIn's one reading of `origin` in the body of `function`.
    private BoundOrigin ReadValueIn(BoundOrigin origin, Koto? function)
    {
        this.OriginProofMetrics?.ValueInRead();
        if (origin.Kind != OriginKind.Intersection)
        {
            if (this.Region(origin) is { Value: { } published } own && ReferenceEquals(own.Body, function))
            {
                return this.ValueIn(published, function);
            }

            if ((OwnVarRegion(origin, function) ? this.RegionValue(origin) : this.CarriedRegion(origin, function, out var value) ? value : null) is { } candidate)
            {
                return this.ValueIn(candidate, function);
            }

            // R7-O2 O2-F: a deferred slot's atom reads as its completion once Final proved it (CompletedOrigin); before, it has no answer
            // yet (DeferredIn), as a local region with no bound.
            if (this.CompletedOrigin(origin) is var completed && !ReferenceEquals(completed, origin))
            {
                return this.ValueIn(completed, function);
            }

            this.readsUnboundedRegion |= this.DeferredIn(origin) is not null || (IsLocalRegion(origin) && !this.RegionBounded(origin));
            return origin;
        }

        var read = BoundOrigin.Static;
        var changed = false;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            var operand = this.ValueIn(origin.Operands[i], function);
            changed |= !ReferenceEquals(operand, origin.Operands[i]);
            read = this.Meet(read, operand);
        }

        return changed ? read : origin;
    }

    // SPEC 15.3.6 (R7-O2 A1): whether a local region's recorded bounds are complete, so that its candidate has an answer: its body is not
    // being bound, that is the forming body is neither its body nor evaluated or declared in it (a Closure's body, bound at its literal, or
    // a named function's: the EvaluatingBody chain, R7-O3 O3-A), or its body is a Closure whose binding has ended
    // (BoundClosure.Classified; its publication's fit re-enters it), or it is a call's region that its call completed (R1-a:
    // CallRegionComplete, Binding.OpenOrigins). The one gate of a candidate's Binding-time reading: RegionValue, ValueIn's deferral fact,
    // SolvedStatic and the erasure's Final class (RegionLeaf) read it, and UpperBound's walk (ReadThrough), which reads a `var` region
    // without an answer as an atom: so a `let` declaration's regions (FixDeclaredRegions) read a `var` region of their own body as itself
    // (R7-O2 A4-L, F's B1).
    private bool RegionBounded(BoundOrigin region)
    {
        var owner = region.Binder is { } binder ? BodyOwner(binder) : null;
        if (!this.formingBody.IsBody || owner is FunctionKoto { IsAnonymous: true, ClosureStorage.Classified: true } || this.CallRegionComplete(region))
        {
            return true;
        }

        for (var body = this.formingBody; body.IsBody; body = EvaluatingBody(body))
        {
            if (body == region.Body)
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 15.3.6's table for a local region's candidate (R7-O2 A1, A4-L): one variable's candidate by its row over its recorded bounds,
    // a `var` region's (RegionValue) or a waiting `let` region's (FixWaiting), a bound that is another variable staying that variable:
    // covariant the meet of its upper bounds (UpperBound; none without one: "never replaced by static"), contravariant its strongest
    // lower bound (LowerBound), invariant the proven equality, which its upper bounds' meet is where it has one (an equality records both
    // directions), otherwise, and at no position (Unused), the covariant, else the contravariant candidate (ReadsLowerBounds). The
    // strongest lower bound is read only for a row that takes it. The call solve's reading departs from the table (OriginInference's
    // RowCandidate, until PLAN G1208). Null with no candidate.
    private BoundOrigin? VarCandidate(BoundOrigin variable, LocalRegion region, Koto? body, Koto binder)
    {
        var upper = this.UpperBound(variable, body);
        return ReadsLowerBounds(region.Row, upper) ? this.LowerBound(variable, region, body, binder) : upper;
    }

    // The meet of the upper bounds recorded on `variable` (VarCandidate), each read as BoundAtoms reads it; a region read through
    // (ReadThrough) that reaches back to `variable` is read through its bounds, one variable with it. Null with none.
    private BoundOrigin? UpperBound(BoundOrigin variable, Koto? body)
    {
        var (value, bounded) = (BoundOrigin.Static, false);
        (var work, var read) = (this.valueWork, this.valueRead);
        work.Clear();
        read.Clear();
        work.Add(variable);
        read.Add(variable);
        while (work.Count != 0)
        {
            var current = work[^1];
            work.RemoveAt(work.Count - 1);
            var atoms = this.BoundAtoms(current, body, this.valueAtoms);
            for (var i = 0; i < atoms.Count; i++)
            {
                var atom = atoms[i];
                if (read.Contains(atom))
                {
                    continue;
                }

                if (this.ReadThrough(atom, body) && this.ReachesRegion(atom, variable, body))
                {
                    read.Add(atom);
                    work.Add(atom);
                }
                else
                {
                    (value, bounded) = (this.Meet(value, atom), true);
                }
            }
        }

        return bounded ? value : null;
    }

    // The strongest of the lower bounds recorded on `variable`'s `region` (VarCandidate), each read as stored with its receivers resolved
    // and a `let` region of `body` as its value, proven at the region's declaration; null with none, and wherever one names another
    // waiting region, a sole one included (FixWaiting: a member of the variable's reading cycle, which stays that variable and is no
    // proven lower bound, so no lower bound is proven to outlive it; the owner's O-544 (b), whose SPEC 15.3.6 sentence has no exception).
    private BoundOrigin? LowerBound(BoundOrigin variable, LocalRegion region, Koto? body, Koto binder)
    {
        if (region.Targets.Count == 0)
        {
            return null;
        }

        var lowers = this.originScratch.Rent(region.Targets.Count);
        try
        {
            for (var i = 0; i < region.Targets.Count; i++)
            {
                lowers[i] = this.RegionValueIn(this.ReceiverAtUse(region.Targets[i], region.TargetValues[i]), body);
                if (this.NamesWaiting(lowers[i], variable))
                {
                    return null;
                }
            }

            return this.StrongestLowerBound(lowers.AsSpan(0, region.Targets.Count), binder, default, out _);
        }
        finally
        {
            this.originScratch.Return(lowers, clearArray: true);
        }
    }

    // The atoms of a region's recorded bounds as UpperBound reads them, into `atoms` (cleared; UpperBound and ReachesRegion each pass
    // their own, as they nest): each bound as stored, its receivers resolved, a `let` region of `body` as its value, a meet as its operands.
    // Counted (CandidateSteps).
    private List<BoundOrigin> BoundAtoms(BoundOrigin region, Koto? body, List<BoundOrigin> atoms)
    {
        this.OriginProofMetrics?.CandidateStep();
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

    // Whether the recorded bounds of regions read through (ReadThrough) lead from `from` back to `target` (UpperBound).
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

                if (this.ReadThrough(atom, body) && seen.Add(atom))
                {
                    work.Add(atom);
                }
            }
        }

        return false;
    }

    // Whether a region UpperBound's walk meets is read through its bounds where they reach back to the variable read (ReachesRegion), one
    // variable with it ("variables in a cycle being one"): a `var` region of `body` with an answer (RegionBounded; a cycle of definitions,
    // which share one solved value), or a waiting `let` region not at a contravariant row (a contravariant position gives a lower
    // bound), whose upper bounds then close an outlives cycle, read through as one variable (SPEC 15.3.6, chapter 15 :442: "Variables
    // connected by a cycle of `outlives` relations are equal and solved as one"). Any other is read as itself, a member that does not
    // reach back among them (FixWaiting). The contravariant exclusion departs from :442's text, which joins every member of a cycle and
    // solves a variable at both positions as invariant: this reading joins only a cycle's non-contravariant members, an open gap
    // (R7-O2 A4-L; its review's C3 twin, a covariant and a contravariant slot in one cycle, is accepted as before A4-L).
    private bool ReadThrough(BoundOrigin origin, Koto? body)
        => OwnVarRegion(origin, body) ? this.RegionBounded(origin)
            : this.Waiting(origin) && this.Region(origin)!.Row != OriginVariance.Contravariant;

    // Whether `origin` is a `let` region FixDeclaredRegions is fixing that has no value fixed yet: at a set's close, the set's members.
    private bool Waiting(BoundOrigin origin) => this.waitingIndex.TryGetValue(origin, out var visit) && visit != int.MaxValue;

    // Whether `origin`, an atom or a meet's operand, is a waiting region other than `variable` (LowerBound).
    private bool NamesWaiting(BoundOrigin origin, BoundOrigin variable)
    {
        for (var k = 0; k < (origin.Kind == OriginKind.Intersection ? origin.Operands.Count : 1); k++)
        {
            var atom = origin.Kind == OriginKind.Intersection ? origin.Operands[k] : origin;
            if (!ReferenceEquals(atom, variable) && this.Waiting(atom))
            {
                return true;
            }
        }

        return false;
    }

    private BoundOrigin RegionValueIn(BoundOrigin origin, Koto? body)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return this.ValueRow(origin, body)?.Value ?? origin;
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

    // The row of `origin` where it is a region of `body` with a published value, which that body reads as the value; null otherwise.
    private LocalRegion? ValueRow(BoundOrigin origin, Koto? body)
        => this.Region(origin) is { Value: not null } region && ReferenceEquals(region.Body, body) ? region : null;

    // A `let` region's value (RegionValueAtUse), published by its declaration once per binding of it, read through the earlier regions of
    // the same body; also the region's one bound, so a nested body that reads the region through its sources (DECISIONS BU-127) reads
    // that value's. A re-binding publishes the same region again (the same slot) with its value read anew. Published as an inclusion;
    // FixedRegion sets an equality-fixed slot's relation (LocalRegion.Equality).
    private LocalRegion PublishRegionValue(BoundOrigin region, BoundOrigin value, Koto evidence)
    {
        var record = this.WrittenRegion(region);
        record.Body = BodyOwner(region.Binder!);
        (record.Value, record.Equality, record.Merged) = (null, false, false);
        record.Value = this.RegionValueIn(value, record.Body);
        this.regionValues++;
        this.RecordRegionBound(record.Value, region, evidence);
        return record;
    }

    // SPEC 15.3.6, 15.4.4, 15.6.5: the region at `slot` of `owner`'s Type (LocalRegionSlot, at `row`) whose value its declaration fixes,
    // `value` published as that value (PublishRegionValue): a `let` local's slot (LocalBorrowType, InferLocalOrigins) and the layer a
    // `let` Pattern binding's or for slot's reach adds (BindingTypeOf, PLAN G922), and a Subject borrowed in place (SubjectRegion, PLAN
    // G922 tws). The one mint and publish of such a region.
    private BoundOrigin ValuedRegion(Koto owner, int slot, OriginVariance row, BoundOrigin value, Koto evidence)
    {
        var region = this.LocalRegionSlot(owner, slot, row);
        this.PublishRegionValue(region, value, evidence);
        return region;
    }

    // SPEC 15.4.4, 15.3.6 (R7-O2 A4-L): `local`'s declaration fixes its regions once its last bound is recorded (BindVariableCore: after
    // its fit and its Type's well-formedness). (a) Each of its clauses but an equality (whose slot FixedRegion fixed) and one with an
    // Unstated end (SPEC 15.6.1: a failed relation adds no constraint; UnstatedRelation) is recorded as a region bound, `let` or `var`
    // alike: the clause's row (LocalClauseRow) stamped as AddObligation stamps it, with no obligation (JudgeLocalClause judges the clause
    // after the body). (b) A `let` local's regions that have no value, the slots its written Type omits (InferLocalOrigins), are
    // published with their candidates (VarCandidate), complete at the declaration (15.4.4), or stay regions with no value (DECISIONS
    // R4-M112). (c) Waiting regions whose candidates read each other, a reading cycle (a strongly connected set over the bounds
    // VarCandidate reads; FixWaiting), are each their own variable, a bound that is another member staying that variable and no proven
    // lower bound (the owner's O-544 (b)); the sets close sinks first and each set's candidates are read before any is published, so a
    // value names no region with a value (the invariant above) and the result is the same in every slot order. It is not reentrant:
    // ReadThrough and Waiting read the per-declaration scratch (waitingIndex) that this call clears and fills, so no call may start
    // while another declaration's fix runs.
    private void FixDeclaredRegions(VariableKoto local)
    {
        if (this.originDeclarations.GetValueOrDefault(local) is not { } declaration)
        {
            return;
        }

        foreach (var relation in declaration.Relations)
        {
            // AddObligation's Unstated test, read through the same predicate (UnstatedRelation): a clause with an Unstated end bounds
            // no region, and its record, if Refuted, is AddObligation's when JudgeLocalClause judges the clause.
            if (!relation.Equality && UnstatedRelation(relation.Longer, relation.Shorter) is null)
            {
                this.RecordLocalRegionBound(this.Stamped(this.LocalClauseRow(declaration, relation)));
            }
        }

        if (IsMutableDeclaration(local))
        {
            return;
        }

        var index = this.waitingIndex;
        index.Clear();
        foreach (var (_, region) in declaration.SubstitutionPairs)
        {
            if (SlotRegion(region) && ReferenceEquals(region.Binder, local) && this.Region(region) is { Value: null })
            {
                index.TryAdd(region, -1);
            }
        }

        var counter = 0;
        foreach (var (_, region) in declaration.SubstitutionPairs)
        {
            if (index.GetValueOrDefault(region) < 0)
            {
                this.FixWaiting(region, local, ref counter);
            }
        }

        index.Clear();
    }

    // One waiting `let` region's visit (FixDeclaredRegions; Tarjan's strongly connected sets, the reading cycles): the region is the root
    // of a set of its own until a bound reaches below it. The visit follows the bounds VarCandidate reads for the region (SPEC 15.3.6):
    // its upper bounds unless its row is contravariant, then its lower bounds where ReadsLowerBounds takes them over its UpperBound, every
    // region its upper bounds name being visited by then. So a set's edges are its members' own reads, with no rule of the set as a
    // whole and no joined row. The root closes the set: each member's own candidate (VarCandidate) is read
    // while every member is still waiting, so no member is read through another's candidate and no slot order enters, and each is then
    // published (PublishWaiting). Returns the region's low link.
    private int FixWaiting(BoundOrigin region, VariableKoto local, ref int counter)
    {
        var (index, set, body) = (this.waitingIndex, this.waitingSet, BodyOwner(local));
        var (own, start, record) = (counter++, set.Count, this.Region(region)!);
        index[region] = own;
        set.Add(region);
        var low = own;
        for (var i = 0; record.Row != OriginVariance.Contravariant && i < record.Parents.Count; i++)
        {
            low = this.VisitWaiting(record.Parents[i], record.Values[i], local, body, low, ref counter);
        }

        var lowers = ReadsLowerBounds(record.Row, this.UpperBound(region, body));
        for (var i = 0; lowers && i < record.Targets.Count; i++)
        {
            low = this.VisitWaiting(record.Targets[i], record.TargetValues[i], local, body, low, ref counter);
        }

        if (low != own)
        {
            return low;
        }

        var candidates = this.waitingCandidates;
        candidates.Clear();
        for (var m = start; m < set.Count; m++)
        {
            candidates.Add(this.VarCandidate(set[m], this.Region(set[m])!, body, local));
        }

        for (var m = 0; m < candidates.Count; m++)
        {
            this.PublishWaiting(m, start, local);
        }

        candidates.Clear();
        set.RemoveRange(start, set.Count - start);
        return own;
    }

    // A closing set's member at `start + m` (FixWaiting), fixed, and published with its candidate (waitingCandidates at `m`) once each
    // member that candidate names is, so that its value reads theirs through (PublishRegionValue) and names no region with a value. A
    // member named is an upper bound that does not reach back (ReadThrough), so none waits on itself. With no candidate the member stays
    // a region with no value. Every set's candidates are read before any of its members is published, so this order changes no value.
    private void PublishWaiting(int m, int start, VariableKoto local)
    {
        var (set, member) = (this.waitingSet, this.waitingSet[start + m]);
        if (this.waitingIndex[member] == int.MaxValue)
        {
            return;
        }

        this.waitingIndex[member] = int.MaxValue;
        if (this.waitingCandidates[m] is not { } candidate)
        {
            return;
        }

        for (var k = 0; k < (candidate.Kind == OriginKind.Intersection ? candidate.Operands.Count : 1); k++)
        {
            var atom = candidate.Kind == OriginKind.Intersection ? candidate.Operands[k] : candidate;
            for (var named = 0; named < this.waitingCandidates.Count; named++)
            {
                if (ReferenceEquals(set[start + named], atom))
                {
                    this.PublishWaiting(named, start, local);
                }
            }
        }

        this.PublishRegionValue(member, candidate, local);
    }

    // FixWaiting's visit of one recorded bound, read as VarCandidate reads it (its receivers resolved, a `let` region of `body` as its
    // value, a meet as its operands): each waiting region it names is visited, or, on the stack, lowers `low` to its index (a fixed
    // one's is int.MaxValue). Returns the low link.
    private int VisitWaiting(BoundOrigin bound, Koto evidence, VariableKoto local, Koto? body, int low, ref int counter)
    {
        var read = this.RegionValueIn(this.ReceiverAtUse(bound, evidence), body);
        for (var k = 0; k < (read.Kind == OriginKind.Intersection ? read.Operands.Count : 1); k++)
        {
            var atom = read.Kind == OriginKind.Intersection ? read.Operands[k] : read;
            if (this.waitingIndex.TryGetValue(atom, out var seen))
            {
                low = Math.Min(low, seen < 0 ? this.FixWaiting(atom, local, ref counter) : seen);
            }
        }

        return low;
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
            region.Targets.Clear();
            region.TargetValues.Clear();
            region.Sources.Clear();
            region.Row = OriginVariance.Unused;
            region.Version = -1;
            (region.Value, region.Equality, region.Merged) = (null, false, false);
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

    // A region slot of `owner`'s Type with its row (SPEC 15.3.6's table: the slot's variance in the local's Type, as RelationWalk composes
    // it); a slot recorded again at another row is invariant.
    private BoundOrigin LocalRegionSlot(Koto owner, int slot, OriginVariance row)
    {
        var origin = this.OriginAtom(owner, OriginKind.Inference, slot);
        var region = this.WrittenRegion(origin);
        var joined = region.Row == OriginVariance.Unused || region.Row == row ? row : OriginVariance.Invariant;
        if (region.Row != joined)
        {
            EnsureWritable(region);
            region.Row = joined;
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
        longer = this.SuppliedAtUse(longer, obligation);
        shorter = this.SuppliedAtUse(shorter, obligation);
        Add(longer, shorter);
        if (obligation.Equality)
        {
            Add(shorter, longer);
        }

        void Add(BoundOrigin source, BoundOrigin target)
        {
            if (ReferenceEquals(source, target))
            {
                return;
            }

            if (BoundableIn(target, body))
            {
                this.RecordRegionBound(source, target, evidence);
            }
            else
            {
                this.RecordRegionTarget(source, target, evidence);
            }
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

        this.RecordRegionTarget(source, target, evidence);

        // J2: a bound of a waited variable's own solution `(E and region)` bounds its region too (RecordOpening).
        if (region.Solved is { } solved && BoundsSolved(source, target, solved))
        {
            this.RecordRegionBound(source, solved, evidence);
        }
    }

    // SPEC 15.3.6: a region slot at the longer end of a recorded bound has its shorter end as a lower bound (LocalRegion.Targets), a fixed
    // Origin included, which no region row bounds; it moves the graph's version, as its value reads it.
    private void RecordRegionTarget(BoundOrigin source, BoundOrigin target, Koto evidence)
    {
        if (SlotRegion(source) && this.WrittenRegion(source) is var region && !region.Targets.Contains(target))
        {
            EnsureWritable(region);
            region.Targets.Add(target);
            region.TargetValues.Add(evidence);
            this.regionVersion++;
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

    // A target Origin's row: the bounds recorded on it (Parents, each with its Values entry), for a local region the Origins it is
    // recorded to outlive (Targets, each with its TargetValues entry) and its slot's variance (Row, LocalRegionSlot), the memo of its sources
    // (Sources, current while Version is the graph's version) and, for a `let` region, its value and the body that reads it so
    // (PublishRegionValue).
    internal sealed class LocalRegion
    {
        internal List<BoundOrigin> Parents { get; } = new();

        internal List<BoundOrigin> Targets { get; } = new();

        internal List<Koto> TargetValues { get; } = new();

        internal OriginVariance Row { get; set; }

        internal List<BoundOrigin> Sources { get; } = new();

        internal List<Koto> Values { get; } = new();

        internal BoundOrigin? Value { get; set; }

        // How the value is published (DECISIONS R8-M56 as amended, PLAN G1369, C-548): an inclusion (false: the value outlives the region,
        // which may lie inside it), or an equality (true: a slot fixed by equality, FixedRegion, bounded both ways, SPEC 15.3.6 "Unless
        // fixed by equality", where no inclusion is read through the equality's end; an end that holds an inclusion makes it one).
        // Written with the value (PublishRegionValue resets it, FixedRegion sets it); read through ReadsLetValue alone (O-400 (b)'s limit,
        // and FixedRegion's reading of an equality's end).
        internal bool Equality { get; set; }

        // A `let` local's region that merges a stored part met on several of its paths (DECISIONS R8-M56; LocalBorrowType's MarkMerged), read
        // by the name limit alone (UnmergedSelection, the owner's O-534 (a) interim; PLAN G922 U1). Written with the value (PublishRegionValue
        // resets it).
        internal bool Merged { get; set; }

        internal Koto? Body { get; set; }

        internal int Version { get; set; } = -1;

        // J2: for a waited variable's own solution `(E and region)`, its region (RecordOpening); null for any other Origin.
        internal BoundOrigin? Solved { get; set; }

        // A `var` region's BU-127 solved value (RegionValue) and the graph version it was computed at (-1: none).
        internal BoundOrigin? Solution { get; set; }

        internal int SolutionVersion { get; set; } = -1;
    }
}
