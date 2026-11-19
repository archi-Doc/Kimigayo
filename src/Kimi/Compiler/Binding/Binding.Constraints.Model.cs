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
// Reset; ArchitectureRulesTest), each counting a change in Version, which the view's serial reads (ContractSolver.ViewOf); the mark of
// the declaration's own supply (Declared) only by MarkDeclared and Reset. An Invalid
// environment is the node whose failure made it Invalid (Failed; PLAN G651 U1): each writer fails that node in the same step and names
// it here; the first one is kept. As an ICheckCause it is what a proof that reads the environment rests on (Binding.InvalidEnvironment,
// the one proof read), so a use derives from a Resource record or a recovery (RestsOnRecoveryOrLimit) and keeps an independent record
// otherwise.
// R7-C3 (B1'): what a proof that reads a failed premise rests on: the declaration whose record the premise's failure is.
internal sealed class PremiseFailure(Koto owner) : ICheckCause
{
    public Koto Use { get; } = owner;

    public ICheckCause? Next => null;

    public ICheckCause? ReentrySite => null;

    public void Rest()
    {
    }
}

internal sealed class ConstraintEnvironment : ICheckCause
{
    public Koto Use => this.Failed!;

    public ICheckCause? Next => null;

    public ICheckCause? ReentrySite => null;

    internal HashSet<BoundConstraint> Facts { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<BoundConstraint> DirectFacts { get; } = new(ReferenceEqualityComparer.Instance);

    internal List<(BoundConstraint Fact, BindingSymbol Source)> DerivedFacts { get; } = new();

    // R7-C2c2 c2-4b (DECISIONS R4.13): the first Declared of DerivedFacts are the declaration's own supply (Declares).
    internal int Declared { get; private set; }

    /// <summary>Gets the node whose failure made the environment Invalid; null while it is valid.</summary>
    internal Koto? Failed { get; private set; }

    internal bool Invalid => this.Failed is not null;

    // R7-C3 (B1'; SPEC 8.2.3, 23.1.2): the premises whose proof is Error (FailPremise), out of the evidence; and what a proof that reads
    // one rests on (PremiseFailure, Use = the declaration that holds the record).
    internal List<BoundConstraint>? FailedPremises { get; private set; } // allocated at the first failed premise

    internal PremiseFailure? FailedPremise { get; private set; }

    internal bool HasAssociatedProjection { get; private set; }

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
            this.HasTypeIdentity |= fact is { Kind: ConstraintKind.TypeIdentity, RequiredType: not null };
        }

        if (added || availability)
        {
            this.Version++;
        }
    }

    // The declaration's own supply ends here (ExpandScopeContractPremises' end). No Version change: no view-keyed table reads the mark.
    internal void MarkDeclared() => this.Declared = this.DerivedFacts.Count;

    // R7-C2c2 c2-4b (DECISIONS R4.13; SPEC 8.3.3 Limits, "the closure of a declaration's premises"): whether `fact` is a ground of
    // the declaration's closure, the one fact an extending request's table reads (ExtendedClassesOf): written, or derived by the
    // declaration's own supply of its written Contract premises; never a use's supply alone. Invariant: the declaration's expansion
    // (ExpandContractPremises) runs before any use supply (Binding.AssociatedTypes holds its supply while bindingConstraintTypes,
    // until after the expansion; Binding.ContractCalls' is body time), so every derived fact written after the mark is a use's
    // (the syntax-triggered supply, staged until C3). Known exception (read, not measured): an override's implementation premises are
    // expanded again after its signature is bound (PrepareVirtualOverridePremises), so there the mark also covers that signature's
    // use supply. Temporary: C3 removes the syntax-triggered supply, after which every derived fact is the declaration's and this
    // split goes. Cost: a linear scan of the declared derived facts, asked only for an identity fact (RP8's spm rows measure it).
    internal bool Declares(BoundConstraint fact)
    {
        if (this.DirectFacts.Contains(fact))
        {
            return true;
        }

        for (var i = 0; i < this.Declared; i++)
        {
            if (ReferenceEquals(this.DerivedFacts[i].Fact, fact))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Fails a premise whose proof is Error (R7-C3 B1', SPEC 8.2.3's kept premise-error context extended to any failed premise):
    /// it leaves the evidence, the environment stays valid, and a proof that reads it rests on its declaration's record.</summary>
    /// <param name="fact">The failed premise.</param>
    /// <param name="owner">The declaration whose record it is.</param>
    internal void FailPremise(BoundConstraint fact, Koto owner)
    {
        if (this.Facts.Remove(fact))
        {
            this.DirectFacts.Remove(fact);
            this.DerivedFacts.RemoveAll(x => ReferenceEquals(x.Fact, fact));
            (this.FailedPremises ??= new()).Add(fact);
            this.FailedPremise ??= new(owner);
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

    /// <summary>Gets or sets a value indicating whether a fact is a Type-identity premise `X is U` of any subject (SPEC 8.2.3), a ground
    /// of the identity relation and the identity classes' input (Binding.TypeIdentity); G721's interim reads it until G170's identity
    /// query answers which Types it makes identical.</summary>
    internal bool HasTypeIdentity { get; set; }

    internal void Reset()
    {
        if (this.Facts.Count != 0 || this.Invalid)
        {
            this.Version++;
        }

        this.Facts.Clear();
        this.DirectFacts.Clear();
        this.DerivedFacts.Clear();
        this.Declared = 0;
        this.Failed = null;
        this.FailedPremises?.Clear();
        this.FailedPremise = null;
        this.HasAssociatedProjection = false;
        this.HasTypeIdentity = false;
    }
}
