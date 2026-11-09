// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the per-declaration component summaries (SPEC 15.2.3, 15.3.5; DECISIONS BU-22): for each struct or enum, what its storage adds,
// in its own formals, to an instance's Owned beyond the instance's Type and Origin arguments (unit 2b-4 adds Transferable's half), as the
// least fixed point over the declarations one solve reaches; Owned's judgment of a nominal instance through it; the sites of the located
// Unsupported_Kd a summary decides (a projection family, F5 (a); the bound; the owner's option 2 for a pair parameter), each a limit (L)
// a proof carries until its answer is settled (EndProof), and the cause a summary's Error rests on (ICheckCause): the stored Type it
// reads that failed, that Type's failed operand context, or the hops of a conformance resolution that re-entered itself (SPEC 8.3.3;
// the steps tested are those the hops resolved, so a step resolved through an identity premise is walked, never a re-entry; DECISIONS
// BU-22, BU-76), with the site of the stored Type read there, whose Unsupported_Kd a use takes when no hop clause failed (ReentrySite).
// Input: a declaration's stored shape (AdtDef.Types) with its Binding states; the Owned leaves (TryLeafCapability); the summaries of the
// declarations its storage names; ContractType's answer and hops, each with the step it resolved, for a mapped projection whose base is
// not abstract.
// Output: Summary; SummaryCapability, Owned of D<A, o>; the sites a settled proof needed, which Binding.Check publishes (FailNeededSites,
// PublishSites).
// Invariants: storage is walked in the declaration's formals and never instantiated; a summary's projection is based at a formal or the
// pair target of its declaration; a summary belongs to one capability epoch (ClearCapabilityResults ends it); the declarations one solve
// reaches are iterated together from no exclusion to their least fixed point, which a family cut keeps finite; a warm rebind allocates
// nothing.
// Must not instantiate storage, decide Copy, re-derive a leaf rule, record a Language error or cap a result silently.
public sealed partial class Binding
{
    // A projection of more steps makes its summary a family site too (F-a: a conformance binding can lengthen a projection with no
    // projection link); it only yields the located Unsupported_Kd, never a verdict (DECISIONS BU-22).
    private const int ProjectionBound = 64;

    private readonly Dictionary<BindingSymbol, ComponentSummary> summaries = new(ReferenceEqualityComparer.Instance);
    private readonly List<ComponentSummary> summarySet = []; // The declarations the solve in progress reaches, in discovery order.
    private readonly List<ComponentSite> neededSites = [];
    private readonly List<(ComponentSummary Summary, int Slot)> linkPending = [];
    private readonly HashSet<(ComponentSummary Summary, int Slot)> linkSeen = [];
    private readonly TypeMemo<BoundType, bool> summaryParts = new(); // The parts one summary walk met.
    private readonly TypeMemo<BoundType, bool> scannedParts = new(); // The parts one storage scan met.
    private readonly List<BoundType> resolvingSteps = []; // The steps the walk in progress resolved through conformance hops.
    private int summaryEpoch;
    private bool solvingSummaries;

    // Whether a pair argument's outer Semantics, and so its target, is unknown in D's formals; D's own pair whole maps to D's target.
    private static bool OuterUnknown(BoundType argument)
        => argument.Kind is BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection ||
            argument is { Kind: BoundTypeKind.Parameter, Symbol.Kind: not BindingSymbolKind.SemanticsTarget };

    // A stored Type's own Type: a Field's annotation, or an inferred Property's.
    private static BoundType? StoredTypeOf(Koto stored) => stored.BoundType ?? (stored as PropertyKoto)?.BoundSymbol?.Type;

    // Owned of D<A, o> (SPEC 15.2.3): Error absorbs, then Refuted, then a site (L), then Unknown. An Error rests on the stored Type that
    // decided it, a site's Unknown on the site (ReadVerdict).
    private ConstraintProof SummaryCapability(CapabilityWork work, DeclarationContainerKoto container)
    {
        if (container.BoundSymbol is not { Adt: not null } symbol)
        {
            return ConstraintProof.Unknown;
        }

        var summary = this.Summary(symbol);
        if (summary.Error)
        {
            return this.ReadVerdict(ConstraintProof.Error, summary, null);
        }

        var result = summary.ExcludesOwned ? ConstraintProof.Refuted : summary.Unresolved ? ConstraintProof.Unknown : ConstraintProof.Proven;
        for (var i = 0; i < work.Type.Components.Count; i++)
        {
            // Every complete Type argument is retained, including unused and inherited slots.
            result = CombineProof(result, this.RequestCapability(work.Type.Components[i], work.Intrinsic, work.Scope), true);
        }

        for (var i = 0; i < work.Type.OriginArguments.Count; i++)
        {
            result = CombineProof(result, this.StaticOrigin(work.Type.OriginArguments[i], work.Scope), true);
        }

        for (var i = 0; i < summary.Projections.Count; i++)
        {
            var projection = this.StoredType(summary.Projections[i], work.Type);
            var proof = projection is null ? ConstraintProof.Unknown
                : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, projection, contract: work.Intrinsic)), work.Scope);
            result = CombineProof(result, proof, true);
        }

        return summary.Sites.Count != 0 && result is ConstraintProof.Proven or ConstraintProof.Unknown
            ? this.ReadVerdict(ConstraintProof.Unknown, summary.Sites[0], null) : result;
    }

    // SPEC 23.1.1 (DECISIONS BU-76, BU-81): each site a settled proof needed fails its stored Type: in Binding.Check's Final prologue,
    // where a derived use held on it is settled, and again after the producers' loop for the sites its checks needed. The counting
    // records and counts that Use once, as PublishNeededLimits' limits.
    private void FailNeededSites()
    {
        for (var i = 0; this.capabilityMode == BindingMode.Final && i < this.neededSites.Count; i++)
        {
            this.Fail(this.neededSites[i].Use, BindingFailure.Unsupported);
        }
    }

    private void ClearNeededSites()
    {
        foreach (var site in this.neededSites)
        {
            site.Needed = false;
        }

        this.neededSites.Clear();
    }

    private ComponentSummary Summary(BindingSymbol declaration)
    {
        if (!this.summaries.TryGetValue(declaration, out var summary))
        {
            this.summaries.Add(declaration, summary = new(declaration));
        }

        if (summary.Epoch != this.summaryEpoch)
        {
            if (this.solvingSummaries)
            {
                this.Discover(summary); // A conformance binding named a declaration no written storage of the solve names.
            }
            else
            {
                this.SolveSummaries(summary);
            }
        }

        return summary;
    }

    private void SolveSummaries(ComponentSummary root)
    {
        var set = this.summarySet;
        this.solvingSummaries = true;
        try
        {
            this.Discover(root);
            var marked = 0;
            for (var changed = true; changed;)
            {
                changed = false;
                var count = set.Count;
                if (marked != count)
                {
                    this.MarkFamilies();
                    marked = count;
                }

                for (var i = count - 1; i >= 0; i--)
                {
                    changed |= this.WalkSummary(set[i]);
                }

                changed |= set.Count != count;
            }
        }
        finally
        {
            this.solvingSummaries = false;
            this.resolvingSteps.Clear();
            set.Clear();
        }
    }

    // Adds a declaration and every declaration its written storage names, transitively, at the bottom (no exclusion), with their
    // projection links: (D, i) -> (E, j) when D's storage names E with argument j equal to D's formal i, after one layer, extended by
    // projection steps.
    private void Discover(ComponentSummary root)
    {
        var set = this.summarySet;
        var start = set.Count;
        root.Reset(this.summaryEpoch);
        set.Add(root);
        for (var i = start; i < set.Count; i++)
        {
            var summary = set[i];
            var types = summary.Declaration.Adt!.Types;
            for (var t = 0; t < types.Count; t++)
            {
                if (types[t].BindingState != BindingState.Invalid && StoredTypeOf(types[t]) is { } stored)
                {
                    this.Scan(summary, stored, t, this.scannedParts.Begin(this.storageVersion));
                }
            }
        }
    }

    private void Scan(ComponentSummary summary, BoundType type, int stored, uint query)
    {
        if (this.scannedParts.TryGet(type, query, out _))
        {
            return;
        }

        this.scannedParts.Set(type, query, true);
        if (type.Kind is not (BoundTypeKind.Array or BoundTypeKind.Dictionary) && type.Symbol is { Adt: not null } declaration &&
            declaration.Declaration is DeclarationContainerKoto)
        {
            if (!this.summaries.TryGetValue(declaration, out var named))
            {
                this.summaries.Add(declaration, named = new(declaration));
            }

            if (named.Epoch != this.summaryEpoch)
            {
                named.Reset(this.summaryEpoch);
                this.summarySet.Add(named);
            }

            for (var j = 0; j < type.Components.Count; j++)
            {
                var argument = type.Components[j].Kind == BoundTypeKind.Semantics ? type.Components[j].Components[0] : type.Components[j];
                var root = argument;
                while (root.Kind == BoundTypeKind.AssociatedProjection)
                {
                    root = root.Components[0];
                }

                if (root.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection && root.Symbol is { } formal &&
                    ContainerSlot(summary.Declaration.Declaration, formal) is >= 0 and var slot)
                {
                    summary.Links.Add(new(slot, named, j, argument.Kind == BoundTypeKind.AssociatedProjection, stored));
                }
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.Scan(summary, type.Components[i], stored, query);
        }
    }

    // F5 (a): a projection link on a cycle of links makes a projection family. Its target's projections are not mapped (the cut), and
    // the stored Type naming the link is the site; readers meet it through the summaries they read.
    private void MarkFamilies()
    {
        foreach (var summary in this.summarySet)
        {
            foreach (var link in summary.Links)
            {
                if (link.Projection && this.Reaches(link.To, link.Index, summary, link.From))
                {
                    link.To.Family = true;
                    this.AddSite(summary, link.Stored);
                }
            }
        }
    }

    private bool Reaches(ComponentSummary from, int slot, ComponentSummary to, int target)
    {
        this.linkPending.Clear();
        this.linkSeen.Clear();
        this.linkPending.Add((from, slot));
        while (this.linkPending.Count != 0)
        {
            var (summary, index) = this.linkPending[^1];
            this.linkPending.RemoveAt(this.linkPending.Count - 1);
            if (ReferenceEquals(summary, to) && index == target)
            {
                return true;
            }

            if (summary.Epoch == this.summaryEpoch && this.linkSeen.Add((summary, index)))
            {
                foreach (var link in summary.Links)
                {
                    if (link.From == index)
                    {
                        this.linkPending.Add((link.To, link.Index));
                    }
                }
            }
        }

        return false;
    }

    // One walk of a declaration's storage, adding what it finds; the iteration repeats while any summary of the solve grows.
    private bool WalkSummary(ComponentSummary summary)
    {
        var before = (summary.ExcludesOwned, summary.Error, summary.Unresolved, summary.Family, summary.Projections.Count, summary.Sites.Count);
        var types = summary.Declaration.Adt!.Types;
        var query = this.summaryParts.Begin(this.storageVersion);
        for (var t = 0; t < types.Count; t++)
        {
            // As the stored walk judged an instantiated Field: Invalid is Error, an unbound or unresolved one Unknown.
            if (types[t].BindingState == BindingState.Invalid)
            {
                summary.Fail(types[t]);
            }
            else if (StoredTypeOf(types[t]) is not { } stored)
            {
                summary.Unresolved = true;
            }
            else if (this.OperandContexts(stored) is var (invalid, unresolved) && (invalid || unresolved))
            {
                summary.Unresolved |= !invalid;
                if (invalid)
                {
                    summary.Fail(this.InvalidOperandContext(stored)!); // OperandContexts' walk and predicate: an Invalid node.
                }
            }
            else
            {
                this.Walk(summary, stored, t, query);
            }
        }

        return before != (summary.ExcludesOwned, summary.Error, summary.Unresolved, summary.Family, summary.Projections.Count, summary.Sites.Count);
    }

    // A component of D's storage, as the capability engine decomposes Owned (StructuralCapability), in D's formals.
    private void Walk(ComponentSummary summary, BoundType type, int stored, uint query)
    {
        if (this.summaryParts.TryGet(type, query, out _))
        {
            return;
        }

        this.summaryParts.Set(type, query, true);
        if (TryLeafCapability(type, IntrinsicKind.Owned, out var leaf))
        {
            summary.ExcludesOwned |= leaf == ConstraintProof.Refuted; // An Owned leaf is never Error.
            return;
        }

        if (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            this.WalkProjection(summary, type, stored, query);
            return;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)
        {
            return; // A formal or the pair target: required as a Type argument.
        }

        if (JudgedByDeclaration(type))
        {
            if (type.Symbol!.Adt is null)
            {
                summary.Unresolved = true;
                return;
            }

            this.WalkComponent(summary, type, stored, query);
            return;
        }

        foreach (var (part, _) in new CapabilityParts(type))
        {
            this.Walk(summary, part, stored, query);
        }
    }

    // A nominal component E<B>: E's bits and sites, each Bj, and E's projections mapped through [E's formals -> B] unless E is cut.
    private void WalkComponent(ComponentSummary summary, BoundType type, int stored, uint query)
    {
        var component = this.Summary(type.Symbol!);
        summary.ExcludesOwned |= component.ExcludesOwned;
        summary.Unresolved |= component.Unresolved;
        if (component.Error)
        {
            summary.Fail(component);
        }

        for (var i = 0; i < component.Sites.Count; i++)
        {
            summary.AddSite(component.Sites[i]);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.Walk(summary, type.Components[i], stored, query);
        }

        var count = component.Family ? 0 : component.Projections.Count;
        for (var i = 0; i < count; i++)
        {
            var projection = component.Projections[i];
            var pair = projection;
            while (pair.Kind == BoundTypeKind.AssociatedProjection)
            {
                pair = pair.Components[0];
            }

            // Option 2 (owner, 2026-10-21): a pair target maps exactly only under a known outer Semantics: not for D's plain formal, its
            // pair target passed whole (a peel) or a projection; then the obligation is not recorded and the stored Type is a site.
            if (pair is { Kind: BoundTypeKind.TargetProjection, Symbol: { Kind: BindingSymbolKind.SemanticsTarget } target } &&
                ContainerSlot(type.Symbol!.Declaration, target) is >= 0 and var slot && slot < type.Components.Count && OuterUnknown(type.Components[slot]))
            {
                this.AddSite(summary, stored);
                continue;
            }

            if (this.StoredType(projection, type) is { } mapped)
            {
                this.Walk(summary, mapped, stored, query);
            }
            else
            {
                summary.Unresolved = true;
            }
        }
    }

    // A projection based at D's formal or pair target is an obligation of D; one with a base that is not abstract is resolved under D's
    // premises, by a conformance binding or an identity premise, and walked; the steps its hops resolved through a conformance are
    // tested for re-entry, and a step resolved through an identity premise is walked untested.
    private void WalkProjection(ComponentSummary summary, BoundType projection, int stored, uint query)
    {
        var root = projection.Components[0];
        var length = 1;
        while (root.Kind == BoundTypeKind.AssociatedProjection)
        {
            root = root.Components[0];
            length++;
        }

        if (root.IsAbstract)
        {
            if (length > ProjectionBound)
            {
                summary.Family = true; // F-a's closure: the located Unsupported_Kd at the stored Type, never a verdict.
                this.AddSite(summary, stored);
            }
            else if (!summary.Projections.Contains(projection))
            {
                summary.Projections.Add(projection);
            }

            return;
        }

        var resolved = this.ContractType(projection, this.ConstraintScope(summary.Declaration.Declaration), out var hops);
        if (resolved.Kind == BoundTypeKind.AssociatedProjection && ReferenceEquals(resolved, projection))
        {
            summary.Unresolved = true; // No conformance binding resolves it: Unknown, as an unresolved stored Type is.
            return;
        }

        if (hops is null)
        {
            // SPEC 8.2.3 (DECISIONS BU-22): an answer whose outer step is resolved through an identity premise has no hops; it is walked
            // untested, with any growth through an inner conformance. A scope's premise identities are finitely many, and a Contract's
            // own are bounded by SPEC 8.3.3 (unbounded growth through them fails the Contract, and the walk then reads a failed operand
            // context); an outer step left unresolved ends the walk as Unknown.
            this.Walk(summary, resolved, stored, query);
            return;
        }

        if (this.Reenters(hops))
        {
            // SPEC 8.3.3: a step a hop resolved re-enters a conformance binding the walk is resolving through, at a deeper instance: a
            // conformance of the resolution revisits itself and fails with its own record. Error, never a cap, resting on the hops
            // ContractType resolved through (DECISIONS BU-76); only a hop's step is tested, so an identity step of the chain never is.
            // The depth test is no proof of growth: when no hop clause fails, the use rests on the stored Type's site (DECISIONS BU-22).
            summary.Fail(hops.Use, hops.Next, this.OwnSite(summary, stored));
            return;
        }

        var depth = this.resolvingSteps.Count;
        for (var hop = hops; hop is not null; hop = hop.Inner)
        {
            this.resolvingSteps.Add(hop.Step);
        }

        this.Walk(summary, resolved, stored, query);
        this.resolvingSteps.RemoveRange(depth, this.resolvingSteps.Count - depth);
    }

    // Whether a hop's step re-enters a step the walk is resolving through: the same associated Type on the same root declaration at a
    // deeper instance (SPEC 8.3.3). The hops of one answer are not compared with each other: one answer's nesting is the projection's
    // finite syntax.
    private bool Reenters(ResolutionHops hops)
    {
        for (var hop = hops; hop is not null; hop = hop.Inner)
        {
            var root = hop.Step.Components[0];
            foreach (var outer in this.resolvingSteps)
            {
                if (ReferenceEquals(outer.Symbol, hop.Step.Symbol) && ReferenceEquals(outer.Components[0].Symbol, root.Symbol) &&
                    root.Height > outer.Components[0].Height)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void AddSite(ComponentSummary summary, int stored) => summary.AddSite(this.OwnSite(summary, stored));

    // The site of a stored Type of the declaration, reused across passes.
    private ComponentSite OwnSite(ComponentSummary summary, int stored)
    {
        var types = summary.Declaration.Adt!.Types;
        if (summary.OwnSites.Length != types.Count)
        {
            summary.OwnSites = new ComponentSite?[types.Count];
        }

        var site = summary.OwnSites[stored] ??= new(this.neededSites);
        site.Use = types[stored];
        return site;
    }

    // A link of the projection-link graph: D's formal From reaches argument Index of To, through projection steps when Projection.
    private readonly record struct ComponentLink(int From, ComponentSummary To, int Index, bool Projection, int Stored);

    // A summary is the cause its Error rests on: the node the walk read that decided it (a stored Type or its operand context), or the
    // hops of a resolution that re-entered itself, whose candidates the summary carries with the re-entry's site (DECISIONS BU-76).
    private sealed class ComponentSummary(BindingSymbol declaration) : ICheckCause
    {
        public Koto Use { get; private set; } = null!;

        public ICheckCause? Next { get; private set; }

        public ICheckCause? ReentrySite { get; private set; }

        internal BindingSymbol Declaration { get; } = declaration;

        internal int Epoch { get; private set; } = -1;

        internal bool ExcludesOwned { get; set; }

        internal bool Error { get; private set; }

        internal bool Unresolved { get; set; }

        // A projection family member (F5 (a)) or past the bound: its projections are not mapped by its readers.
        internal bool Family { get; set; }

        internal List<BoundType> Projections { get; } = [];

        // Its own sites and those of the summaries it reads, in the order met; a requiring check rests on the first.
        internal List<ComponentSite> Sites { get; } = [];

        internal List<ComponentLink> Links { get; } = [];

        // A site per stored Type of the declaration, reused across passes.
        internal ComponentSite?[] OwnSites { get; set; } = [];

        public void Rest()
        {
        }

        internal void Fail(Koto use) => this.Fail(use, null, null);

        internal void Fail(ICheckCause held) => this.Fail(held.Use, held.Next, held.ReentrySite);

        internal void Fail(Koto use, ICheckCause? next, ICheckCause? reentrySite)
        {
            if (!this.Error)
            {
                this.Error = true;
                this.Use = use;
                this.Next = next;
                this.ReentrySite = reentrySite;
            }
        }

        internal void AddSite(ComponentSite site)
        {
            if (!this.Sites.Contains(site))
            {
                this.Sites.Add(site);
            }
        }

        internal void Reset(int epoch)
        {
            this.Epoch = epoch;
            this.ExcludesOwned = false;
            this.Error = false;
            this.Unresolved = false;
            this.Family = false;
            this.Projections.Clear();
            this.Sites.Clear();
            this.Links.Clear();
        }
    }

    // A stored Type whose summary is unsupported (a projection family, the bound, the owner's option 2); one Unsupported_Kd at Use.
    private sealed class ComponentSite(List<ComponentSite> needed) : ICheckCause
    {
        public Koto Use { get; set; } = null!;

        public ICheckCause? Next => null;

        public ICheckCause? ReentrySite => null;

        internal bool Needed { get; set; }

        public void Rest()
        {
            if (!this.Needed)
            {
                this.Needed = true;
                needed.Add(this);
            }
        }
    }
}
