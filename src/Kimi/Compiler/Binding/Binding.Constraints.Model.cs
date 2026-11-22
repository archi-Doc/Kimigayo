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

    // The key a constraint was interned by (its parts, as BoundConstraint's constructor copies them): the one statement of a constraint's
    // key, which the interner's frozen-level test reads (Binding.FormNegation; PLAN G949 Stage 2 U4).
    internal static ConstraintKey Of(BoundConstraint constraint)
        => new(constraint.Kind, constraint.Subject, constraint.RequiredType, constraint.Contract, constraint.Mask, constraint.Left, constraint.Right);

    private static int Identity(object? value) => value is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
}

// A scope's premises. Its state (the three fact sets, the two flags they set and Failed) is written only by its own members (Add, Fail,
// Reset; ArchitectureRulesTest), each counting a change in Version, which the view's serial reads (ContractSolver.ViewOf); the mark of
// the declaration's own supply (Declared) only by MarkDeclared and Reset. An Invalid
// environment is the node whose failure made it Invalid (Failed; PLAN G651 U1): each writer fails that node in the same step and names
// it here; the first one is kept. Its failed premises' cause is what a proof that consults one rests on (PremiseFailure,
// the one proof read), so a use derives from a Resource record or a recovery (RestsOnRecoveryOrLimit) and keeps an independent record
// otherwise.
// R7-C3 (B1'): what a proof that reads a failed premise rests on: the declaration whose record the premise's failure is.
// R7-E5 c1 (design-inputs-read Rev 2 R2.2 (2), F's ruling 343): and the failed fact's origin, the clause whose own check failed it
// (ConstraintEnvironment.CauseOf), none for a premise failed by its owner's judgment.
internal sealed class PremiseFailure(Koto owner, IsKoto? origin) : ICheckCause
{
    public Koto Use { get; } = owner;

    public IsKoto? Origin { get; } = origin;

    public ICheckCause? Next => null;

    public ICheckCause? ReentrySite => null;

    public void Rest()
    {
    }
}

internal sealed class ConstraintEnvironment
{
    private PremiseFailure? premiseFailure; // R7-C3 B1': FailedPremise's cause, made once
    private List<PremiseFailure>? originCauses; // R7-E5 c1: one cause per origin (CauseOf), made once and kept across passes

    // R7-E5 c1 (Rev 2 R2.2 (1), F's ruling 343; review C6): a failed direct fact's origin, the clause whose own check failed it, written
    // only by that writer (an Error fact at its clause's binding: Add; a part of the clause: FailPremise), never by a premise's owner
    // judgment. Keyed by the fact in this environment (Facts is a reference set, so an interned Error shared by two failed clauses is
    // one entry: the first origin is kept, and every failed clause has its own record). Allocated at the first entry (rejected
    // programs only); Reset clears it and keeps its capacity.
    private List<(BoundConstraint Fact, IsKoto Origin)>? origins;

    internal HashSet<BoundConstraint> Facts { get; } = new(ReferenceEqualityComparer.Instance);

    internal HashSet<BoundConstraint> DirectFacts { get; } = new(ReferenceEqualityComparer.Instance);

    // A derived fact's Origin is the source declaration's clause it instantiates (R7-C3, F's C2 ruling 282: it is that premise, Failed
    // where the origin is; Binding.DerivedFailure reads it); none for a supply that instantiates no clause.
    internal List<(BoundConstraint Fact, BindingSymbol Source, IsKoto? Origin)> DerivedFacts { get; } = new();

    // R7-C2c2 c2-4b (DECISIONS R4.13): the first Declared of DerivedFacts are the declaration's own supply (Declares).
    internal int Declared { get; private set; }

    // R7-C3 (SPEC 8.2.3, 23.1.2; F's C2 rulings 272-273): the premises whose own judgment is Error (FailPremise), their state Failed in
    // the evidence; and what a proof that consults one rests on (PremiseFailure, Use = the declaration that holds the record).
    // Its census (F's C2 ruling 317 (1)): Binding counts each write of this state and of UnprovenInputs in premiseFailures (its
    // FailPremise, the one caller of FailPremise, FailFacts included; FailInput, the one caller of NameUnprovenInput) and zeroes the
    // count where ResetPass resets the pass's environments, so the per-proof readers answer "nothing failed" at once on 0. Sound
    // because no environment a pass reads holds this state without a write counted in that pass: the pass's environments are reset
    // with the count, a mid-pass Reset only leaves it high, and the frozen library's hold none (each failure has its record, and
    // FreezeLibrary refuses a library with records and throws if the census is not 0). A new writer of this state counts it there:
    // ArchitectureRulesTest's row "Writers of the Failed state" pins the two callers (F's C2 ruling 318).
    internal List<BoundConstraint>? FailedPremises { get; private set; } // allocated at the first failed premise

    // R7-C3 (F's C2 ruling 255): the declaration's own unproven projection inputs (NameUnprovenInput): no premise fails, and a proof that
    // reads one (Binding.UnprovenInputRead) rests on PremiseFailure.
    internal List<(BoundType Subject, BindingSymbol Contract)>? UnprovenInputs { get; private set; } // allocated at the first one

    // The cause is made once per environment and kept across passes (warm Bind allocates nothing, BU-64); it is the environment's
    // answer only while a premise of this pass has failed or an input of it is named.
    internal PremiseFailure? FailedPremise => this.FailedPremises is { Count: > 0 } || this.UnprovenInputs is { Count: > 0 } ? this.premiseFailure : null;

    internal bool HasAssociatedProjection { get; private set; }

    // The number of changes of this environment's state.
    internal int Version { get; private set; }

    // Adds `fact` itself (not an And's parts), direct, or derived from `source`'s premise, the instance of its clause `origin`; an
    // Error fact's writer names the failed node (Fail), and a direct one's `origin` is the clause whose binding failed it (origins).
    internal void Add(BoundConstraint fact, BindingSymbol? source, IsKoto? origin)
    {
        var availability = source is null ? this.DirectFacts.Add(fact) : !this.DerivedFrom(fact, source);
        if (source is not null && availability)
        {
            this.DerivedFacts.Add((fact, source, origin));
        }
        else if (source is null && origin is not null)
        {
            this.NoteOrigin(fact, origin);
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

    /// <summary>Gets a value indicating whether a premise of this environment failed (R7-C3, F's C2 rulings 272-273): its one state, read
    /// by the one consult (Binding.AvailableConstraintFact).</summary>
    /// <param name="fact">The premise.</param>
    /// <returns>Whether it failed.</returns>
    internal bool IsFailed(BoundConstraint fact) => this.FailedPremises is { } failed && failed.Contains(fact);

    /// <summary>Fails a premise whose own judgment is Error, whatever the cause (R7-C3, F's C2 rulings 272-273): it stays in the
    /// environment with the state Failed, the environment stays valid, and a proof that consults it rests on its declaration's record.</summary>
    /// <param name="fact">The failed premise.</param>
    /// <param name="owner">The declaration whose record it is.</param>
    /// <param name="origin">The clause whose own check failed it (a part of the clause), or null for a premise failed by its owner's
    /// judgment (origins).</param>
    /// <returns>Whether the premise newly failed (the failed set only grows, so the premise failures reach a fixpoint).</returns>
    internal bool FailPremise(BoundConstraint fact, Koto owner, IsKoto? origin)
    {
        if (!this.Facts.Contains(fact) || this.IsFailed(fact))
        {
            return false;
        }

        if (origin is not null)
        {
            this.NoteOrigin(fact, origin);
        }

        (this.FailedPremises ??= new()).Add(fact);
        _ = this.Cause(owner);
        this.Version++;
        return true;
    }

    /// <summary>Names an unproven projection input of the declaration (F's C2 ruling 255): its own requirement, whose record is the
    /// declaration's; no premise fails, the declaration keeps its context, and a proof that reads the input rests on that record. No
    /// Version change: no view-keyed table reads the names (the judge's intake reads them, Binding.UnprovenInputRead), as with
    /// MarkDeclared.</summary>
    /// <param name="subject">The input's subject.</param>
    /// <param name="contract">The input's Contract.</param>
    /// <param name="owner">The declaration whose record it is.</param>
    internal void NameUnprovenInput(BoundType subject, BindingSymbol contract, Koto owner)
    {
        foreach (var (named, namedContract) in this.UnprovenInputs ??= new())
        {
            if (ReferenceEquals(named, subject) && ReferenceEquals(namedContract, contract))
            {
                return;
            }
        }

        this.UnprovenInputs.Add((subject, contract));
        _ = this.Cause(owner);
    }

    /// <summary>The cause a proof that reads one of this environment's failures rests on (PremiseFailure): its failed premises, its
    /// named inputs and its identity premise error (Binding.IdentityPremiseErrorRead; F's C2 ruling 276 (4)). Made once and kept across
    /// passes (warm Bind allocates nothing, BU-64); not state: no view-keyed table reads it.</summary>
    /// <param name="owner">The declaration whose record it is.</param>
    /// <returns>The environment's one cause.</returns>
    internal PremiseFailure Cause(Koto owner) => this.premiseFailure ??= new(owner, null);

    /// <summary>The cause a proof that reads the failed premise `fact` rests on (R7-E5 c1, Rev 2 R2.2 (2)): the environment's cause
    /// (FailedPremise), or, where the fact has an origin (origins), that origin's cause, with the same Use. Each origin's cause is made
    /// at its first read and kept across passes (warm Bind allocates nothing, BU-64); not state: no view-keyed table reads it.</summary>
    /// <param name="fact">A premise of this environment whose state is Failed.</param>
    /// <returns>The cause, or null while nothing of this pass failed here (FailedPremise).</returns>
    internal PremiseFailure? CauseOf(BoundConstraint fact)
    {
        if (this.FailedPremise is not { } failure || this.OriginOf(fact) is not { } origin)
        {
            return this.FailedPremise;
        }

        foreach (var cause in this.originCauses ??= new())
        {
            if (ReferenceEquals(cause.Origin, origin))
            {
                return cause;
            }
        }

        var made = new PremiseFailure(failure.Use, origin);
        this.originCauses.Add(made);
        return made;
    }

    /// <summary>Gets or sets a value indicating whether a fact is a Type-identity premise `X is U` of any subject (SPEC 8.2.3), a ground
    /// of the identity relation and the identity classes' input (Binding.TypeIdentity); G721's interim reads it until G170's identity
    /// query answers which Types it makes identical.</summary>
    internal bool HasTypeIdentity { get; set; }

    internal void Reset()
    {
        if (this.Facts.Count != 0)
        {
            this.Version++;
        }

        this.Facts.Clear();
        this.DirectFacts.Clear();
        this.DerivedFacts.Clear();
        this.Declared = 0;
        this.FailedPremises?.Clear();
        this.UnprovenInputs?.Clear();
        this.origins?.Clear();
        this.HasAssociatedProjection = false;
        this.HasTypeIdentity = false;
    }

    private void NoteOrigin(BoundConstraint fact, IsKoto origin)
    {
        if (this.OriginOf(fact) is null)
        {
            (this.origins ??= new()).Add((fact, origin));
        }
    }

    private IsKoto? OriginOf(BoundConstraint fact)
    {
        if (this.origins is { } noted)
        {
            foreach (var (written, origin) in noted)
            {
                if (ReferenceEquals(written, fact))
                {
                    return origin;
                }
            }
        }

        return null;
    }

    private bool DerivedFrom(BoundConstraint fact, BindingSymbol source)
    {
        for (var i = 0; i < this.DerivedFacts.Count; i++)
        {
            if (ReferenceEquals(this.DerivedFacts[i].Fact, fact) && ReferenceEquals(this.DerivedFacts[i].Source, source))
            {
                return true;
            }
        }

        return false;
    }
}
