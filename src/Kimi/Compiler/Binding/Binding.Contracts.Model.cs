// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Contract metadata shares its identity vocabulary.

/// <summary>A requirement declaration together with its normalized declaring Contract reference.</summary>
/// <param name="Symbol">The original declaration identity.</param>
/// <param name="Contract">The exact declaring Contract, including its bindings.</param>
public readonly record struct BoundRequirement(BindingSymbol Symbol, BindingSymbol Contract);

/// <summary>Declaration evidence for a completed associated binding; usable only with its path's verified certificate.</summary>
/// <param name="Identity">The associated identity in the destination path.</param>
/// <param name="Type">The completed Type, including Semantics and Origins.</param>
/// <param name="SourcePath">The path whose frozen premises supplied the evidence.</param>
/// <param name="Source">The implementation declaration supplying the value result.</param>
public readonly record struct BoundAssociatedTypeEvidence(BoundRequirement Identity, BoundType Type, BoundConformancePath SourcePath, FunctionKoto Source);

/// <summary>The effective requirements of a Contract, deduplicated by bound requirement identity.</summary>
public sealed class BoundContract
{
    internal BoundContract(BindingSymbol symbol) => this.Symbol = symbol;

    /// <summary>Gets the declaring Contract identity.</summary>
    public BindingSymbol Symbol { get; }

    /// <summary>Gets all ancestor identities, with repeated refinement paths removed.</summary>
    public IReadOnlyList<BindingSymbol> Ancestors => this.AncestorStorage;

    /// <summary>Gets the effective function and Property requirement identities.</summary>
    public IReadOnlyList<BoundRequirement> Requirements => this.RequirementStorage;

    /// <summary>Gets the effective associated-Type declarations and their bound declaring Contracts.</summary>
    public IReadOnlyList<BoundRequirement> AssociatedTypes => this.AssociatedStorage;

    /// <summary>Gets the effect bounds this Contract itself declares (SPEC 8.11.1); each ancestor exposes its own.</summary>
    public IReadOnlyList<BoundEffectBound> EffectBounds => this.EffectBoundStorage;

    internal List<BindingSymbol> AncestorStorage { get; } = new();

    internal List<BoundRequirement> RequirementStorage { get; } = new();

    internal List<BoundRequirement> AssociatedStorage { get; } = new();

    internal List<IsKoto> ClauseStorage { get; } = new();

    /// <summary>Gets the effect bounds this Contract itself declares (SPEC 8.11.1); each ancestor keeps its own.</summary>
    internal List<BoundEffectBound> EffectBoundStorage { get; } = new();

    /// <summary>Gets or sets the progress of building <see cref="EffectBoundStorage"/> in this pass: 0 unbuilt, 1 building, 2 built.</summary>
    internal byte EffectState { get; set; }

    /// <summary>Gets or sets a value indicating whether this Contract or an ancestor declares a bound, once its table is built.</summary>
    internal bool HasEffectBounds { get; set; }

    internal HashSet<BindingSymbol> Seen { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<BoundRequirement> SeenRequirements { get; } = new();

    internal Dictionary<string, List<BoundRequirement>> MembersByName { get; } = new(StringComparer.Ordinal);

    internal byte State { get; set; }

    internal bool HasUnresolvedParents { get; set; }

    /// <summary>Gets the first requirement this Contract declares itself, the one its built-in target executes (Equatable's equals);
    /// null for a Contract that declares none.</summary>
    /// <returns>The requirement's declaration identity.</returns>
    internal BindingSymbol? OwnRequirement()
    {
        for (var i = 0; i < this.RequirementStorage.Count; i++)
        {
            if (ReferenceEquals(this.RequirementStorage[i].Contract, this.Symbol))
            {
                return this.RequirementStorage[i].Symbol;
            }
        }

        return null;
    }
}

/// <summary>An effect bound a Contract declares for a function requirement, as its effect item states it (SPEC 8.11.1).</summary>
/// <param name="Identity">The requirement's declaration and bound declaring Contract.</param>
/// <param name="Declaration">The effect item.</param>
public readonly record struct BoundEffectBound(BoundRequirement Identity, EffectBoundKoto Declaration)
{
    /// <summary>Gets the original requirement declaration.</summary>
    public BindingSymbol Requirement => this.Identity.Symbol;
}

/// <summary>A definition-verified implementation of one stable requirement.</summary>
public readonly record struct BoundWitness(BoundRequirement Identity, BindingSymbol Implementation, BoundFunctionWitness? Function = null)
{
    /// <summary>Gets the original requirement declaration.</summary>
    public BindingSymbol Requirement => this.Identity.Symbol;
}

// DECISIONS BU-220 (PLAN G949 S1-b1): an identity's per-pass state is current only while its Stamp is its store's pass (ImplTable);
// stale, it reads as unregistered, and ImplTable resets it at its first access in a pass. A pooled identity is re-keyed (Reuse).

/// <summary>A stable declaration identity. Availability belongs to its evidence paths.</summary>
public sealed class BoundConformance
{
    internal static readonly IReadOnlyDictionary<BoundRequirement, BoundType> EmptyAssociated = new Dictionary<BoundRequirement, BoundType>();
    private readonly ImplTable store;

    internal BoundConformance(BindingSymbol type, BindingSymbol contract, ImplTable store)
    {
        this.Type = type;
        this.Contract = contract;
        this.store = store;
    }

    public BindingSymbol Type { get; private set; }

    public BindingSymbol Contract { get; private set; }

    public IReadOnlyList<BoundConformancePath> Paths => this.Current ? this.PathStorage : Array.Empty<BoundConformancePath>();

    public bool IsVerified
    {
        get => field && this.Current;
        internal set;
    }

    // Legacy access exposes only an unconditional, verified definition mapping.
    public IReadOnlyList<BoundWitness> Witnesses => this.UnconditionalPath?.Witnesses ?? Array.Empty<BoundWitness>();

    public IReadOnlyDictionary<BoundRequirement, BoundType> AssociatedTypes => this.UnconditionalPath?.AssociatedTypes ?? EmptyAssociated;

    public IReadOnlyList<BoundPropertyWitness> PropertyWitnesses => this.UnconditionalPath?.PropertyWitnesses ?? Array.Empty<BoundPropertyWitness>();

    internal List<BoundConformancePath> PathStorage { get; } = new();

    // Rule (a) (ImplTable): the point of the identity's first path and of its last, and, for a base identity, its paths before the last
    // later point's join (its paths at point 1 while every base row registers at point 1 or 2: ImplTable's premise); written by the pass
    // that registers it.
    internal ImplTable.AdmissionPoint Point { get; private set; }

    internal ImplTable.AdmissionPoint LastPoint { get; private set; }

    internal List<BoundConformancePath>? PointOnePaths { get; private set; }

    internal IsKoto? DirectClause { get; set; }

    internal bool Invalid { get; set; }

    // The pass of the last reset of the per-pass state (ResetPass), and whether that pass listed the identity (ImplTable.Named).
    internal ulong Stamp { get; private set; }

    internal bool Listed { get; set; }

    internal bool Current => this.Stamp == this.store.Pass;

    internal BoundConformancePath? UnconditionalPath
    {
        get
        {
            if (this.IsVerified)
            {
                for (var i = 0; i < this.PathStorage.Count; i++)
                {
                    if (this.PathStorage[i] is { IsVerified: true, Premises: null } path)
                    {
                        return path;
                    }
                }
            }

            return null;
        }
    }

    public BindingSymbol? GetImplementation(BoundRequirement requirement) => this.UnconditionalPath?.GetImplementation(requirement);

    public BoundPropertyWitness? GetPropertyWitness(BoundRequirement requirement, PropertyAccessorKind kind) => this.UnconditionalPath?.GetPropertyWitness(requirement, kind);

    // The base store's freeze (ImplTable.Freeze): the identity's state is current in every later pass.
    internal void Freeze() => this.Stamp = Binding.Frozen;

    // The identity's state at the start of a pass, made current for it.
    internal void ResetPass(ulong pass)
    {
        this.Stamp = pass;
        this.IsVerified = false;
        this.Invalid = false;
        this.DirectClause = null;
        this.PathStorage.Clear();
        this.Listed = false;
        (this.Point, this.LastPoint) = (ImplTable.AdmissionPoint.PassStart, ImplTable.AdmissionPoint.PassStart);
        this.PointOnePaths?.Clear();
    }

    // A path joins the identity at its registration (ImplTable.Register), at its registrar step's point (rule (a)).
    internal void AddPath(BoundConformancePath path, ImplTable.AdmissionPoint point)
    {
        if (this.PathStorage.Count == 0)
        {
            this.Point = point;
        }
        else if (point > this.LastPoint && this.store.IsBase)
        {
            (this.PointOnePaths ??= new()).Clear(); // Only a pass without the library reads it (ImplTable.PathsOf), and only for a base row.
            this.PointOnePaths.AddRange(this.PathStorage);
        }

        this.LastPoint = point;
        this.PathStorage.Add(path);
    }

    // A pooled identity taken for a new key; stale, so its first access resets the rest.
    internal BoundConformance Reuse(BindingSymbol type, BindingSymbol contract)
    {
        this.Type = type;
        this.Contract = contract;
        this.Stamp = 0;
        return this;
    }
}

// DECISIONS BU-220 (PLAN G949 S1-b1): a path's per-pass state is current only while its Stamp is its store's pass (ImplTable); stale,
// it reads as unverified and empty, and ImplTable resets it at its first access in a pass. A pooled path is re-keyed and emptied of
// every fact of its earlier conformance (Reuse).

/// <summary>Reusable definition-side conformance metadata. Only verified mappings supply evidence.</summary>
public sealed class BoundConformancePath : ICheckCause
{
    private readonly ImplTable store;

    internal BoundConformancePath(BindingSymbol type, BindingSymbol contract, ImplTable store)
    {
        this.Type = type;
        this.Contract = contract;
        this.store = store;
        this.Scope = new(type.Declaration) { ConformancePath = this };
    }

    /// <summary>Gets the conforming Type declaration; generic slots remain definition-bound.</summary>
    public BindingSymbol Type { get; private set; }

    /// <summary>Gets the required Contract identity.</summary>
    public BindingSymbol Contract { get; private set; }

    /// <summary>Gets the direct declaration from which this path is inherited.</summary>
    public IsKoto Declaration { get; internal set; } = null!;

    public BindingSymbol RootContract { get; internal set; } = null!;

    public SyntaxFormKoto? Premises { get; internal set; }

    /// <summary>Gets a value indicating whether this pass has verified the declaration contract and every witness.</summary>
    public bool IsVerified
    {
        get => field && this.Current;
        internal set;
    }

    /// <summary>Gets requirement-to-member mappings, usable only while IsVerified is true.</summary>
    public IReadOnlyList<BoundWitness> Witnesses => this.Current ? this.WitnessStorage : Array.Empty<BoundWitness>();

    /// <summary>Gets completed, normalized associated-Type bindings; provenance does not change Type identity.</summary>
    public IReadOnlyDictionary<BoundRequirement, BoundType> AssociatedTypes => this.Current ? this.AssociatedStorage : BoundConformance.EmptyAssociated;

    /// <summary>Gets inferred-binding provenance; this does not certify an unverified conformance.</summary>
    public IReadOnlyList<BoundAssociatedTypeEvidence> InferenceEvidence => this.Current ? this.InferenceStorage : Array.Empty<BoundAssociatedTypeEvidence>();

    /// <summary>Gets the separately verified get/set mappings, valid only while IsVerified is true.</summary>
    public IReadOnlyList<BoundPropertyWitness> PropertyWitnesses => this.Current ? this.PropertyWitnessStorage : Array.Empty<BoundPropertyWitness>();

    Koto ICheckCause.Use => this.Use;

    ICheckCause? ICheckCause.Next => null;

    ICheckCause? ICheckCause.ReentrySite => null;

    internal BoundConformance Identity { get; set; } = null!;

    internal BindingScope Scope { get; private set; }

    internal BoundConformancePath RootPath { get; set; } = null!;

    internal BoundConformancePath? InheritedFrom { get; set; }

    // Distinct ancestor references of the same source clause can converge after a base substitution.
    // Their obligations remain conjunctive; only their equal, verified mappings may share storage.
    internal List<BoundConformancePath>? AdditionalInheritedSources { get; set; }

    internal int InheritedSourceCount => this.InheritedFrom is null ? 0 : 1 + (this.AdditionalInheritedSources?.Count ?? 0);

    internal BoundType? InheritedBase { get; set; }

    // SPEC 8.8.2 (PLAN G665), the one statement of the rule: an inherited path's conformance scope and its conditions at a use are its
    // declared path's premises at its declared Self. DeclaredPath is the end of the InheritedFrom chain (every source's has this path's
    // declaration, Premises and declaring scope); DeclaredSelf is that path's Self in this path's Type's parameters (the declaring layer).
    // The inherited registrar (Binding.InheritedConformances) composes both once, from the first source's, telling a declared source by
    // its kind (no InheritedFrom); no reader walks the chain. Read only on an inherited path. DeclaredSelf is null where this generic
    // composition was refused (SubstituteType's record) and then on every heir below; an instance is never refused for it: the conditions
    // at an instance form its declared Self one base clause at a time, refused at the use's site only if that instance's passes the limit,
    // and only a path with premises, whose generic scope cannot state them, is refused at its reader's site (Binding.TypeHeight).
    internal BoundConformancePath? DeclaredPath { get; set; }

    internal BoundType? DeclaredSelf { get; set; }

    // SPEC 12.7, 23.1.1: the last completed verification's only unproven part is a witness whose ObjectCallCompatible status
    // is not computed (OCC-X); a check that needs this conformance rests on its location-only Unsupported_Kd at Use.
    internal bool Unsupported { get; set; }

    // A failing check rested on this limit, so Binding.Check publishes it; an inherited conformance has no declaration of its own.
    internal bool Needed { get; set; }

    internal BindingSymbol? RejectedSelfSignature { get; set; }

    internal List<BoundWitness> WitnessStorage { get; } = new();

    internal Dictionary<BoundRequirement, BoundWitness> WitnessMap { get; } = new();

    internal List<BoundPropertyWitness> PropertyWitnessStorage { get; } = new();

    internal Dictionary<(BoundRequirement Requirement, PropertyAccessorKind Kind), BoundPropertyWitness> PropertyWitnessMap { get; } = new();

    internal Dictionary<BoundRequirement, BoundType> AssociatedStorage { get; } = new();

    internal List<BoundAssociatedTypeEvidence> InferenceStorage { get; } = new();

    internal Koto Use { get; set; } = null!;

    internal bool Active { get; set; }

    // Rule (a) (ImplTable): the registrar step's point at the path's registration.
    internal ImplTable.AdmissionPoint Point { get; set; }

    internal bool Checking { get; set; }

    // The requests that reached it while it is being verified (Binding.ReentryRead), until its verification ends. Temporary carrier
    // with reentryReads; removal: R7-C2e2 e2-5b, with e2-3's Completion on the answer.
    internal int Reentries { get; set; }

    internal bool Invalid { get; set; }

    // The pass of the last reset of the per-pass state (ResetPass).
    internal ulong Stamp { get; private set; }

    internal bool Current => this.Stamp == this.store.Pass;

    // The facts a component keeps by this path and a second key (DECISIONS BU-220), each a small list on the path, kept with it across
    // passes as ImplTable keeps the path, null until its first entry; the owning component's accessor is the list's one reader and writer.
    // Binding.FunctionWitnesses: a function requirement's callable correspondence (FunctionWitness).
    internal List<(BoundRequirement Requirement, BoundFunctionWitness Witness)>? FunctionWitnesses { get; set; }

    // Binding.ContractMatching: a function requirement's premises scope (CompatibleRequirement).
    internal List<(BoundRequirement Requirement, BindingScope Premises)>? WitnessScopes { get; set; }

    // Binding.PropertyMatching: a non-standard Property operation's input and Origin bindings (OperationOrigins).
    internal List<(BoundAccessor Requirement, BoundOrigin[] Inputs, BoundOrigin[] Origins)>? PropertyWitnessOrigins { get; set; }

    // Binding.PropertyWitnesses: a standard Property operation's bridge accessor (PropertyWitnessFunction).
    internal List<(BoundAccessor Requirement, BoundAccessor Bridge, PropertyWitnessKind Kind)>? PropertyBridges { get; set; }

    // Binding.ConditionalConformances: the overlap scope of this path, the pair's left, with each right path (VerifyConformanceAgreement).
    internal List<(BoundConformancePath Right, BindingScope Scope)>? OverlapScopes { get; set; }

    // Binding.ContractTypes: the hop chains resolving a step through this path, one per step and inner chain (Hop).
    internal List<Binding.ResolutionHops>? HopChains { get; set; }

    // Binding.AssociatedInference: the pass's identification of each requirement in this path (IdentifyRequirement), and the paths
    // whose inferred bindings rest on this path's evidence (AddInferenceConsumer); the first is per-pass state of the path, the second
    // emptied at each pass start (ResetAssociatedInference).
    internal List<(BoundRequirement Requirement, Binding.RequirementMatch Match)>? RequirementMatches { get; set; }

    internal List<BoundConformancePath>? InferenceConsumers { get; set; }

    // Binding.AssociatedTypes: a root path's binding of each associated identity (PrepareAssociatedBindings, AssociatedBindingOf), kept
    // with the path and reset in place with the path's per-pass state.
    internal List<(BoundRequirement Associated, Binding.AssociatedBinding Binding)>? AssociatedBindings { get; set; }

    // Binding.AssociatedInference: whether the pass published this inherited path's inference evidence (PublishInheritedInference).
    internal bool InferencePublished { get; set; }

    /// <summary>Gets a verified implementation by requirement identity, without source member lookup.</summary>
    /// <param name="requirement">The original requirement declaration identity.</param>
    /// <returns>The selected member, or null while the mapping is unverified or has no such requirement.</returns>
    public BindingSymbol? GetImplementation(BoundRequirement requirement)
        => this.IsVerified && this.WitnessMap.TryGetValue(requirement, out var witness) ? witness.Implementation : null;

    /// <summary>Gets a verified operation by requirement identity, without member lookup.</summary>
    /// <param name="requirement">The Property requirement identity.</param>
    /// <param name="kind">The requested operation.</param>
    /// <returns>The operation mapping, or null if unavailable.</returns>
    public BoundPropertyWitness? GetPropertyWitness(BoundRequirement requirement, PropertyAccessorKind kind)
        => this.IsVerified && this.PropertyWitnessMap.TryGetValue((requirement, kind), out var witness) ? witness : null;

    // SPEC 23.1.2: a check resting on a conformance resting on its specification derives its record from Use; one resting on its OCC-X
    // limit needs that limit published.
    void ICheckCause.Rest() => this.Needed |= this.Unsupported;

    internal BoundConformancePath InheritedSource(int index) => index == 0 ? this.InheritedFrom! : this.AdditionalInheritedSources![index - 1];

    // The path's state at the start of a pass, made current for it: the verification flags, witnesses and their maps, associated and
    // inference storage, the per-pass satellites (requirement matches, published inference, the associated bindings' state) and the
    // scope; what the registration writes and the kept satellites stay.
    // The base store's freeze (ImplTable.Freeze): the path's state is current in every later pass.
    internal void Freeze() => this.Stamp = Binding.Frozen;

    internal void ResetPass(ulong pass)
    {
        this.Stamp = pass;
        this.Active = false;
        this.IsVerified = false;
        this.Checking = false;
        this.Reentries = 0;
        this.RejectedSelfSignature = null;
        this.Invalid = false;
        this.WitnessStorage.Clear();
        this.WitnessMap.Clear();
        this.PropertyWitnessStorage.Clear();
        this.PropertyWitnessMap.Clear();
        this.Unsupported = false;
        this.Needed = false;
        this.AssociatedStorage.Clear();
        this.InferenceStorage.Clear();
        this.RequirementMatches?.Clear();
        this.InferencePublished = false;
        if (this.AssociatedBindings is { } bindings)
        {
            foreach (var (_, binding) in bindings)
            {
                binding.Reset();
            }
        }

        this.Scope.Reset();
        this.Point = ImplTable.AdmissionPoint.PassStart;
    }

    // The Front's RR2: a pooled path taken for a new key, emptied of every fact of its earlier conformance (the kept satellites, the
    // registration's facts) with a scope of its own; stale, so its first access resets its per-pass state.
    internal BoundConformancePath Reuse(BindingSymbol type, BindingSymbol contract, BoundConformance identity)
    {
        this.Type = type;
        this.Contract = contract;
        this.Identity = identity;
        this.Stamp = 0;
        this.Scope = new(type.Declaration) { ConformancePath = this };
        this.FunctionWitnesses?.Clear();
        this.WitnessScopes?.Clear();
        this.PropertyWitnessOrigins?.Clear();
        this.PropertyBridges?.Clear();
        this.OverlapScopes?.Clear();
        this.HopChains?.Clear();
        this.InferenceConsumers?.Clear();
        this.AssociatedBindings?.Clear();
        this.InheritedFrom = null;
        this.AdditionalInheritedSources?.Clear();
        this.InheritedBase = null;
        this.DeclaredPath = null;
        this.DeclaredSelf = null;
        this.Use = null!;
        this.Declaration = null!;
        this.RootContract = null!;
        this.RootPath = null!;
        this.Premises = null;
        return this;
    }
}
