// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared semantic vocabulary.

public enum ConstraintProof : byte
{
    Unknown,
    Proven,
    Refuted,
    Error,
}

public enum ConstraintKind : byte
{
    Error,
    TypeIdentity,
    Semantics,
    Contract,
    And,
    Or,
    Not,
    Unresolved,
    Callable,
}

/// <summary>An immutable, compilation-interned proposition. Types and symbols retain their binding identities.</summary>
public sealed class BoundConstraint
{
    // `projection`: whether a part of the subject or the required Type is an associated projection, which Binding's walk decides
    // (Binding.ContainsAssociatedProjection, DECISIONS BU-19).
    internal BoundConstraint(ConstraintKey key, bool projection)
    {
        this.Kind = key.Kind;
        this.Subject = key.Subject;
        this.RequiredType = key.RequiredType;
        this.Contract = key.Contract;
        this.Mask = key.Mask;
        this.Left = key.Left;
        this.Right = key.Right;
        this.HasAssociatedProjection = projection || key.Left?.HasAssociatedProjection == true || key.Right?.HasAssociatedProjection == true;
        this.HasUnresolved = key.Kind == ConstraintKind.Unresolved || key.Left?.HasUnresolved == true || key.Right?.HasUnresolved == true;
    }

    public ConstraintKind Kind { get; }

    public BoundType? Subject { get; }

    public BoundType? RequiredType { get; }

    public BindingSymbol? Contract { get; }

    public SemanticsMask Mask { get; }

    public BoundConstraint? Left { get; }

    public BoundConstraint? Right { get; }

    /// <summary>Gets or sets the interned negation, cached because every proof queries it.</summary>
    internal BoundConstraint? Negation { get; set; }

    internal bool HasAssociatedProjection { get; }

    internal bool HasUnresolved { get; }
}

// Do not recursively hash a proposition DAG; operands are compared by interned identity.
internal readonly struct ConstraintKey(ConstraintKind kind, BoundType? subject = null, BoundType? requiredType = null, BindingSymbol? contract = null, SemanticsMask mask = default, BoundConstraint? left = null, BoundConstraint? right = null) : IEquatable<ConstraintKey>
{
    internal ConstraintKind Kind { get; } = kind;

    internal BoundType? Subject { get; } = subject;

    internal BoundType? RequiredType { get; } = requiredType;

    internal BindingSymbol? Contract { get; } = contract;

    internal SemanticsMask Mask { get; } = mask;

    internal BoundConstraint? Left { get; } = left;

    internal BoundConstraint? Right { get; } = right;

    public bool Equals(ConstraintKey other) => this.Kind == other.Kind && this.Mask == other.Mask && ReferenceEquals(this.Subject, other.Subject) && ReferenceEquals(this.RequiredType, other.RequiredType) && ReferenceEquals(this.Contract, other.Contract) && ReferenceEquals(this.Left, other.Left) && ReferenceEquals(this.Right, other.Right);

    public override bool Equals(object? obj) => obj is ConstraintKey other && this.Equals(other);

    public override int GetHashCode() => HashCode.Combine(this.Kind, this.Mask, Identity(this.Subject), Identity(this.RequiredType), Identity(this.Contract), Identity(this.Left), Identity(this.Right));

    private static int Identity(object? value) => value is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
}

// A scope's premises. Its state (the three fact sets, the two flags they set and Failed) is written only by its own members (Add, Fail,
// Reset; ArchitectureRulesTest), each counting a change in Version, which the view's serial reads (ContractSolver.ViewOf). An Invalid
// environment is the node whose failure made it Invalid (Failed; PLAN G651 U1): each writer fails that node in the same step and names
// it here; the first one is kept. As an ICheckCause it is what a proof that reads the environment rests on (Binding.InvalidEnvironment,
// the one proof read), so a use derives from a Resource record or a recovery (RestsOnRecoveryOrLimit) and keeps an independent record
// otherwise.
internal sealed class ConstraintEnvironment : ICheckCause
{
    public Koto Use => this.Failed!;

    public ICheckCause? Next => null;

    public ICheckCause? ReentrySite => null;

    internal HashSet<BoundConstraint> Facts { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<BoundConstraint> DirectFacts { get; } = new(ReferenceEqualityComparer.Instance);

    internal List<(BoundConstraint Fact, BindingSymbol Source)> DerivedFacts { get; } = new();

    /// <summary>Gets the node whose failure made the environment Invalid; null while it is valid.</summary>
    internal Koto? Failed { get; private set; }

    internal bool Invalid => this.Failed is not null;

    internal bool HasAssociatedProjection { get; private set; }

    /// <summary>Gets a value indicating whether a fact identifies a Type parameter with a Type (SPEC 8.2), so that expressions bound in
    /// its scope substitute one for the other.</summary>
    internal bool HasParameterIdentity { get; private set; }

    /// <summary>Gets a value indicating whether a fact is a Type-identity premise, the identity classes' input (Binding.TypeIdentity).</summary>
    internal bool HasIdentity { get; private set; }

    // The number of changes of this environment's state.
    internal int Version { get; private set; }

    public void Rest()
    {
    }

    // Adds `fact` itself (not an And's parts), direct, or derived from `source`'s premise; an Error fact's writer names the failed node
    // (Fail).
    internal void Add(BoundConstraint fact, BindingSymbol? source)
    {
        var availability = source is null ? this.DirectFacts.Add(fact) : !this.DerivedFacts.Contains((fact, source));
        if (source is not null && availability)
        {
            this.DerivedFacts.Add((fact, source));
        }

        var added = this.Facts.Add(fact);
        if (added)
        {
            this.HasAssociatedProjection |= fact.HasAssociatedProjection;
            this.HasParameterIdentity |= fact is { Kind: ConstraintKind.TypeIdentity, Subject.Kind: BoundTypeKind.Parameter, RequiredType: not null };
            this.HasIdentity |= fact is { Kind: ConstraintKind.TypeIdentity, RequiredType: not null };
        }

        if (added || availability)
        {
            this.Version++;
        }
    }

    /// <summary>Makes the environment Invalid by the failure of <paramref name="node"/>, which its writer fails in the same step.</summary>
    /// <param name="node">The failed node.</param>
    internal void Fail(Koto node)
    {
        if (this.Failed is null)
        {
            this.Failed = node;
            this.Version++;
        }
    }

    internal void Reset()
    {
        if (this.Facts.Count != 0 || this.Invalid)
        {
            this.Version++;
        }

        this.Facts.Clear();
        this.DirectFacts.Clear();
        this.DerivedFacts.Clear();
        this.Failed = null;
        this.HasAssociatedProjection = false;
        this.HasParameterIdentity = false;
    }
}
