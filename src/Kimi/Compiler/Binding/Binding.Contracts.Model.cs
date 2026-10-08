// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Contract metadata shares its identity vocabulary.

/// <summary>A requirement declaration together with its normalized declaring Contract reference.</summary>
/// <param name="Symbol">The original declaration identity.</param>
/// <param name="Contract">The exact declaring Contract, including its bindings.</param>
public readonly record struct BoundRequirement(BindingSymbol Symbol, BindingSymbol Contract);

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

    /// <summary>Gets the effective associated-Type declaration identities.</summary>
    public IReadOnlyList<BindingSymbol> AssociatedTypes => this.AssociatedStorage;

    /// <summary>Gets the effect bounds this Contract itself declares (SPEC 8.4.10.1); each ancestor exposes its own.</summary>
    public IReadOnlyList<BoundEffectBound> EffectBounds => this.EffectBoundStorage;

    internal List<BindingSymbol> AncestorStorage { get; } = new();

    internal List<BoundRequirement> RequirementStorage { get; } = new();

    internal List<BindingSymbol> AssociatedStorage { get; } = new();

    internal List<IsKoto> ClauseStorage { get; } = new();

    /// <summary>Gets the effect bounds this Contract itself declares (SPEC 8.4.10.1); each ancestor keeps its own.</summary>
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
}

/// <summary>An effect bound a Contract declares for a function requirement, as its effect item states it (SPEC 8.4.10.1).</summary>
/// <param name="Identity">The requirement's declaration and bound declaring Contract.</param>
/// <param name="Bound">The bound.</param>
/// <param name="Declaration">The effect item.</param>
public readonly record struct BoundEffectBound(BoundRequirement Identity, EffectBoundKind Bound, EffectBoundKoto Declaration)
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

/// <summary>A stable declaration identity. Availability belongs to its evidence paths.</summary>
public sealed class BoundConformance
{
    private static readonly IReadOnlyDictionary<BindingSymbol, BoundType> EmptyAssociated = new Dictionary<BindingSymbol, BoundType>();

    internal BoundConformance(BindingSymbol type, BindingSymbol contract)
    {
        this.Type = type;
        this.Contract = contract;
    }

    public BindingSymbol Type { get; }

    public BindingSymbol Contract { get; }

    public IReadOnlyList<BoundConformancePath> Paths => this.PathStorage;

    public bool IsVerified { get; internal set; }

    // Legacy access exposes only an unconditional, verified definition mapping.
    public IReadOnlyList<BoundWitness> Witnesses => this.UnconditionalPath?.Witnesses ?? Array.Empty<BoundWitness>();

    public IReadOnlyDictionary<BindingSymbol, BoundType> AssociatedTypes => this.UnconditionalPath?.AssociatedTypes ?? EmptyAssociated;

    public IReadOnlyList<BoundPropertyWitness> PropertyWitnesses => this.UnconditionalPath?.PropertyWitnesses ?? Array.Empty<BoundPropertyWitness>();

    internal List<BoundConformancePath> PathStorage { get; } = new();

    internal IsKoto? DirectClause { get; set; }

    internal bool Invalid { get; set; }

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
}

/// <summary>Reusable definition-side conformance metadata. Only verified mappings supply evidence.</summary>
public sealed class BoundConformancePath
{
    internal BoundConformancePath(BindingSymbol type, BindingSymbol contract)
    {
        this.Type = type;
        this.Contract = contract;
        this.Scope = new(type.Declaration) { ConformancePath = this };
    }

    /// <summary>Gets the conforming Type declaration; generic slots remain definition-bound.</summary>
    public BindingSymbol Type { get; }

    /// <summary>Gets the required Contract identity.</summary>
    public BindingSymbol Contract { get; }

    /// <summary>Gets the direct declaration from which this path is inherited.</summary>
    public IsKoto Declaration { get; internal set; } = null!;

    public BindingSymbol RootContract { get; internal set; } = null!;

    public SyntaxFormKoto? Premises { get; internal set; }

    /// <summary>Gets a value indicating whether this pass has verified the declaration contract and every witness.</summary>
    public bool IsVerified { get; internal set; }

    /// <summary>Gets requirement-to-member mappings, usable only while IsVerified is true.</summary>
    public IReadOnlyList<BoundWitness> Witnesses => this.WitnessStorage;

    /// <summary>Gets explicit, normalized associated-Type bindings.</summary>
    public IReadOnlyDictionary<BindingSymbol, BoundType> AssociatedTypes => this.AssociatedStorage;

    /// <summary>Gets the separately verified get/set mappings, valid only while IsVerified is true.</summary>
    public IReadOnlyList<BoundPropertyWitness> PropertyWitnesses => this.PropertyWitnessStorage;

    internal BoundConformance Identity { get; set; } = null!;

    internal BindingScope Scope { get; }

    internal BoundConformancePath RootPath { get; set; } = null!;

    internal BoundConformancePath? InheritedFrom { get; set; }

    internal BoundType? InheritedBase { get; set; }

    internal BindingSymbol? RejectedSelfSignature { get; set; }

    internal List<BoundWitness> WitnessStorage { get; } = new();

    internal Dictionary<BoundRequirement, BoundWitness> WitnessMap { get; } = new();

    internal List<BoundPropertyWitness> PropertyWitnessStorage { get; } = new();

    internal Dictionary<(BoundRequirement Requirement, PropertyAccessorKind Kind), BoundPropertyWitness> PropertyWitnessMap { get; } = new();

    internal Dictionary<BindingSymbol, BoundType> AssociatedStorage { get; } = new(ReferenceEqualityComparer.Instance);

    internal Koto Use { get; set; } = null!;

    internal bool Active { get; set; }

    internal bool Checking { get; set; }

    internal bool Invalid { get; set; }

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
}
