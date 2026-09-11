// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Contract metadata shares its identity vocabulary.

/// <summary>The effective requirements of a Contract, deduplicated by declaration identity.</summary>
public sealed class BoundContract
{
    internal BoundContract(BindingSymbol symbol) => this.Symbol = symbol;

    /// <summary>Gets the declaring Contract identity.</summary>
    public BindingSymbol Symbol { get; }

    /// <summary>Gets all ancestor identities, with repeated refinement paths removed.</summary>
    public IReadOnlyList<BindingSymbol> Ancestors => this.AncestorStorage;

    /// <summary>Gets the effective function and Property requirement identities.</summary>
    public IReadOnlyList<BindingSymbol> Requirements => this.RequirementStorage;

    /// <summary>Gets the effective associated-Type declaration identities.</summary>
    public IReadOnlyList<BindingSymbol> AssociatedTypes => this.AssociatedStorage;

    internal List<BindingSymbol> AncestorStorage { get; } = new();

    internal List<BindingSymbol> RequirementStorage { get; } = new();

    internal List<BindingSymbol> AssociatedStorage { get; } = new();

    internal List<IsKoto> ClauseStorage { get; } = new();

    internal HashSet<BindingSymbol> Seen { get; } = new(ReferenceEqualityComparer.Instance);

    internal Dictionary<string, List<BindingSymbol>> MembersByName { get; } = new(StringComparer.Ordinal);

    internal byte State { get; set; }
}

/// <summary>A definition-verified implementation of one stable requirement.</summary>
public readonly record struct BoundWitness(BindingSymbol Requirement, BindingSymbol Implementation);

/// <summary>Reusable definition-side conformance metadata. Only verified mappings supply evidence.</summary>
public sealed class BoundConformance
{
    internal BoundConformance(BindingSymbol type, BindingSymbol contract)
    {
        this.Type = type;
        this.Contract = contract;
    }

    /// <summary>Gets the conforming Type declaration; generic slots remain definition-bound.</summary>
    public BindingSymbol Type { get; }

    /// <summary>Gets the required Contract identity.</summary>
    public BindingSymbol Contract { get; }

    /// <summary>Gets a value indicating whether this pass has verified the declaration contract and every witness.</summary>
    public bool IsVerified { get; internal set; }

    /// <summary>Gets requirement-to-member mappings, usable only while IsVerified is true.</summary>
    public IReadOnlyList<BoundWitness> Witnesses => this.WitnessStorage;

    /// <summary>Gets explicit, normalized associated-Type bindings.</summary>
    public IReadOnlyDictionary<BindingSymbol, BoundType> AssociatedTypes => this.AssociatedStorage;

    /// <summary>Gets the separately verified get/set mappings, valid only while IsVerified is true.</summary>
    public IReadOnlyList<BoundPropertyWitness> PropertyWitnesses => this.PropertyWitnessStorage;

    internal List<BoundWitness> WitnessStorage { get; } = new();

    internal Dictionary<BindingSymbol, BindingSymbol> WitnessMap { get; } = new(ReferenceEqualityComparer.Instance);

    internal List<BoundPropertyWitness> PropertyWitnessStorage { get; } = new();

    internal Dictionary<(BindingSymbol Requirement, PropertyAccessorKind Kind), BoundPropertyWitness> PropertyWitnessMap { get; } = new();

    internal Dictionary<BindingSymbol, BoundType> AssociatedStorage { get; } = new(ReferenceEqualityComparer.Instance);

    internal IsKoto? DirectClause { get; set; }

    internal Koto Use { get; set; } = null!;

    internal bool Active { get; set; }

    internal bool Checking { get; set; }

    internal bool Invalid { get; set; }

    /// <summary>Gets a verified implementation by requirement identity, without source member lookup.</summary>
    /// <param name="requirement">The original requirement declaration identity.</param>
    /// <returns>The selected member, or null while the mapping is unverified or has no such requirement.</returns>
    public BindingSymbol? GetImplementation(BindingSymbol requirement)
        => this.IsVerified ? this.WitnessMap.GetValueOrDefault(requirement) : null;

    /// <summary>Gets a verified operation by requirement identity, without member lookup.</summary>
    /// <param name="requirement">The Property requirement identity.</param>
    /// <param name="kind">The requested operation.</param>
    /// <returns>The operation mapping, or null if unavailable.</returns>
    public BoundPropertyWitness? GetPropertyWitness(BindingSymbol requirement, PropertyAccessorKind kind)
        => this.IsVerified && this.PropertyWitnessMap.TryGetValue((requirement, kind), out var witness) ? witness : null;
}
