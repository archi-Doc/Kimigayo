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
    internal BoundConstraint(ConstraintKey key)
    {
        this.Kind = key.Kind;
        this.Subject = key.Subject;
        this.RequiredType = key.RequiredType;
        this.Contract = key.Contract;
        this.Mask = key.Mask;
        this.Left = key.Left;
        this.Right = key.Right;
        this.HasAssociatedProjection = ContainsProjection(key.Subject) || ContainsProjection(key.RequiredType) || key.Left?.HasAssociatedProjection == true || key.Right?.HasAssociatedProjection == true;
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

    private static bool ContainsProjection(BoundType? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.AssociatedProjection)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (ContainsProjection(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }
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

internal sealed class ConstraintEnvironment
{
    internal HashSet<BoundConstraint> Facts { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<BoundConstraint> DirectFacts { get; } = new(ReferenceEqualityComparer.Instance);

    internal List<(BoundConstraint Fact, BindingSymbol Source)> DerivedFacts { get; } = new();

    internal bool Invalid { get; set; }

    internal bool HasAssociatedProjection { get; set; }

    internal void Reset()
    {
        this.Facts.Clear();
        this.DirectFacts.Clear();
        this.DerivedFacts.Clear();
        this.Invalid = false;
        this.HasAssociatedProjection = false;
    }
}
