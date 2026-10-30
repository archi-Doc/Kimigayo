// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared semantic vocabulary.

public enum GenericSlotKind : byte
{
    Type,
    Pair,
    Length,
}

public enum OriginKind : byte
{
    Static,
    Parameter,
    Input,
    Inference,
    Intersection,
    Projection,
    Unbound,

    // SPEC 5.2.2: the fresh anchor of a raw Place borrow; Binder is the borrow. It has no upper bound, so it fits every
    // destination like static, while ownership analysis checks the borrow's Loan and those derived from it under the anchor.
    Anchor,

    // SPEC 5.7 (DECISIONS BU-24): `self` on a raw layer in inline storage, the holding value; only in declared Field and payload
    // Types, never in an instance's stored Types; every read replaces it (Binding.ReadHolder).
    Holder,

    // SPEC 5.7 (DECISIONS BU-24, BU-46): the Origin a raw layer without `during` counts as stating, which every Origin outlives; in
    // relations and their records (Binding.UnstatedRelation), and on a kept part's raw layer its declaration writes without `during`
    // (Binding.WithUnstatedOrigins), never in any other Type.
    Unstated,
}

public enum OriginVariance : byte
{
    Unused,
    Covariant,
    Contravariant,
    Invariant,
}

public enum LoanRequirement : byte
{
    None,
    Ref,
    Uniq,
}

/// <summary>A declared slot's Loan requirement over the Semantics cases of its declaration's pair binders (SPEC 15.3.5; DECISIONS BU-44):
/// <see cref="Unconditional"/> in every case, joined in one case with the class of each binder in <see cref="Binders"/> that the case
/// gives a borrow Semantics (`ref` for ref and objref, `uniq` for uniq and objuniq). A member is a pair binder's ordinal among the pair
/// slots of the schema listing the slot (own then inherited; Type slots take none), for a binder admitting a borrow. Binding.RequirementAt
/// reads it at an instance.</summary>
/// <param name="Unconditional">The requirement in every case.</param>
/// <param name="Binders">The pair binders whose borrow cases use the slot.</param>
public readonly record struct SlotRequirement(LoanRequirement Unconditional, BinderSet Binders);

/// <summary>A set of pair-slot ordinals, the binders of a <see cref="SlotRequirement"/> (DECISIONS BU-44), with no width limit: ordinals
/// below 64 inline and the others in words beyond them, word i holding ordinals 64 + 64i to 127 + 64i. Sets compare by their members.
/// Every operation on binders goes through these members; only the requirement fixed point builds a set beyond 64 into a buffer, its
/// slot's or a reused scratch one (<see cref="Union"/>).</summary>
public readonly struct BinderSet : IEquatable<BinderSet>
{
    private readonly ulong low;
    private readonly ulong[]? high;

    internal BinderSet(ulong low, ulong[]? high) => (this.low, this.high) = (low, high);

    /// <summary>Gets a value indicating whether the set has no member.</summary>
    public bool IsEmpty => this.Next(0) < 0;

    // The words beyond the 64th ordinal, the buffer a slot's own set is built into (OriginParameter.Absorb).
    internal ulong[]? Words => this.high;

    /// <summary>Tests two sets for equal members.</summary>
    /// <param name="left">A set.</param>
    /// <param name="right">Another set.</param>
    /// <returns>Whether their members are equal.</returns>
    public static bool operator ==(BinderSet left, BinderSet right) => left.Equals(right);

    /// <summary>Tests two sets for different members.</summary>
    /// <param name="left">A set.</param>
    /// <param name="right">Another set.</param>
    /// <returns>Whether their members differ.</returns>
    public static bool operator !=(BinderSet left, BinderSet right) => !left.Equals(right);

    /// <summary>Gets the least member at or after an ordinal, for iteration without allocation.</summary>
    /// <param name="ordinal">The least ordinal to consider, 0 or more.</param>
    /// <returns>The member, or -1 when none remains.</returns>
    public int Next(int ordinal)
    {
        if (ordinal < 64 && (this.low >> ordinal) != 0)
        {
            return ordinal + System.Numerics.BitOperations.TrailingZeroCount(this.low >> ordinal);
        }

        for (var i = Math.Max(ordinal - 64, 0) / 64; this.high is not null && i < this.high.Length; i++)
        {
            var skip = Math.Max(ordinal - 64 - (64 * i), 0);
            if ((this.high[i] >> skip) != 0)
            {
                return 64 + (64 * i) + skip + System.Numerics.BitOperations.TrailingZeroCount(this.high[i] >> skip);
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public bool Equals(BinderSet other)
    {
        for (var i = 0; i < Math.Max(this.high?.Length ?? 0, other.high?.Length ?? 0); i++)
        {
            if (Word(this.high, i) != Word(other.high, i))
            {
                return false;
            }
        }

        return this.low == other.low;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BinderSet other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = this.low.GetHashCode();
        for (var i = 0; this.high is not null && i < this.high.Length; i++)
        {
            hash = this.high[i] == 0 ? hash : HashCode.Combine(hash, i, this.high[i]);
        }

        return hash;
    }

    // The set of one ordinal; one beyond 63 in `words`, the caller's array for it (a Binding keeps one per ordinal, so a warm pass
    // allocates nothing).
    internal static BinderSet Of(int ordinal, ulong[]? words)
    {
        if (words is null)
        {
            return new(1UL << ordinal, null);
        }

        words[(ordinal - 64) / 64] = 1UL << ((ordinal - 64) % 64);
        return new(0, words);
    }

    // The empty set over this set's words, cleared and kept for a new pass of the fixed point (OriginParameter.ResetRequirement).
    internal BinderSet Cleared()
    {
        if (this.high is not null)
        {
            Array.Clear(this.high);
        }

        return new(0, this.high);
    }

    // The union with `other`, whether `other` added a member; beyond 64 built into `buffer`, the caller's (a slot's own words, a scratch
    // one), reused and grown only when an ordinal needs a word it lacks. Each word is read before it is written, so either set may live
    // there; a set another holds is never written.
    internal BinderSet Union(BinderSet other, ref ulong[]? buffer, out bool added)
    {
        added = (other.low & ~this.low) != 0;
        for (var i = 0; !added && other.high is not null && i < other.high.Length; i++)
        {
            added = (other.high[i] & ~Word(this.high, i)) != 0;
        }

        var words = Math.Max(this.high?.Length ?? 0, other.high?.Length ?? 0);
        if (!added || words == 0)
        {
            return added ? new(this.low | other.low, null) : this;
        }

        var target = buffer is not null && buffer.Length >= words ? buffer : new ulong[words];
        for (var i = 0; i < target.Length; i++)
        {
            target[i] = Word(this.high, i) | Word(other.high, i);
        }

        buffer = target;
        return new(this.low | other.low, target);
    }

    private static ulong Word(ulong[]? words, int i) => words is not null && i < words.Length ? words[i] : 0;
}

public enum BindingObligationKind : byte
{
    OriginOutlives,
    OriginInference,
    TypeRole,
    TypeFormation,
    Loan,
    StaticStorage,
}

public enum BindingDeadline : byte
{
    Definition,
    BodyOrigins,
    Instantiation,
}

/// <summary>The check that discharges an Origin obligation, recorded where the rule that requires it creates it (PLAN G74 U4).</summary>
public enum OriginDischarge : byte
{
    /// <summary>The obligation's one verdict once Binding is final: proven from the premises at its use, or a finite or inferred
    /// chain judged with region inference (SPEC 15.6.5).</summary>
    Judgment,

    /// <summary>The well-formedness of a direct borrowed input: its stored Origins outlive the borrow at each call, which judges the
    /// instantiated input's well-formedness (SPEC 8.1.2, 15.6.4 step 3).</summary>
    CallBorrows,
}

/// <summary>A declared slot; a pair consumes one argument and retains its whole type.</summary>
public sealed class GenericSlot(GenericSlotKind kind, BindingSymbol symbol, BindingSymbol? semantics)
{
    public GenericSlotKind Kind { get; } = kind;

    public BindingSymbol Symbol { get; } = symbol;

    public BindingSymbol? Semantics { get; } = semantics;

    public OriginVariance OriginVariance { get; internal set; }
}

/// <summary>Declaration-bound Origin identity and inferred declaration requirements.</summary>
public sealed class OriginParameter(string name, int slot, BoundOrigin origin, SourceSpan span)
{
    public string Name { get; } = name;

    public int Slot { get; } = slot;

    public BoundOrigin Origin { get; } = origin;

    public SourceSpan Span { get; } = span;

    public OriginVariance Variance { get; internal set; }

    public SlotRequirement Requirement { get; internal set; }

    // Joins a use into Requirement (SPEC 15.3.5: the stronger unconditional part, every binder), the requirement fixed point being its one
    // writer; whether it changed.
    internal bool Absorb(SlotRequirement use)
    {
        var words = this.Requirement.Binders.Words;
        var binders = this.Requirement.Binders.Union(use.Binders, ref words, out var added);
        if (!added && use.Unconditional <= this.Requirement.Unconditional)
        {
            return false;
        }

        this.Requirement = new(use.Unconditional > this.Requirement.Unconditional ? use.Unconditional : this.Requirement.Unconditional, binders);
        return true;
    }

    // Starts a pass of the requirement fixed point: no requirement, its binders' words cleared and kept (BinderSet.Cleared).
    internal void ResetRequirement() => this.Requirement = new(LoanRequirement.None, this.Requirement.Binders.Cleared());
}

/// <summary>Shared ordered generic and Origin binders for one declaration.</summary>
public sealed class DeclarationSchema(GenericSlot[] slots, OriginParameter[] origins)
{
    public IReadOnlyList<GenericSlot> GenericSlots { get; } = slots;

    public IReadOnlyList<OriginParameter> Origins { get; } = origins;
}

/// <summary>Immutable Origin expression. Identity belongs to its binder, never its spelling.</summary>
public sealed class BoundOrigin
{
    internal BoundOrigin(OriginKind kind, Koto? binder = null, int slot = 0, string? name = null, BoundOrigin[]? operands = null)
    {
        this.Kind = kind;
        this.Binder = binder;
        this.Slot = slot;
        this.Name = name ?? kind.ToString();
        this.Operands = operands ?? [];
        this.InputIndex = slot;
    }

    public static BoundOrigin Static { get; } = new(OriginKind.Static, name: "static");

    public OriginKind Kind { get; }

    public Koto? Binder { get; }

    public int Slot { get; }

    public string Name { get; }

    public IReadOnlyList<BoundOrigin> Operands { get; }

    /// <summary>Gets the value input carrying this Origin; distinct from its inference slot.</summary>
    public int InputIndex { get; internal init; }

    internal static BoundOrigin Holder { get; } = new(OriginKind.Holder, name: "self");

    // Displayed as an omitted Origin (SPEC 23.1).
    internal static BoundOrigin Unstated { get; } = new(OriginKind.Unstated, name: "a raw pointer without `during`");

    internal Koto? Occurrence { get; init; }

    internal int TargetSlot { get; init; }

    // A nested anonymous/local Function input is fixed in its enclosing body, never a temporary signature-evidence stand-in.
    internal bool IsLocalInput { get; init; }

    // SPEC 10.8, 15.3.6: an inferred region, either temporary signature evidence or a body-local omitted Origin.
    // Its binder and slot give stable identity; local constraints and value flow determine the actual Loans.
    internal bool Open { get; init; }

    internal BindingSymbol? BorrowCondition { get; set; }

    // The body whose binding first formed the atom (Binding.ExecutableBodies.cs; DECISIONS R4-M101 E5): defined for a non-declaration
    // binder, which one body binds; a declaration binder's atom is read by its slot's role instead.
    internal ExecutableBody Body { get; init; }
}

/// <summary>A retained semantic requirement, independent of successful name resolution. An <see cref="BindingObligationKind.OriginOutlives"/>
/// obligation with <c>Equality</c> requires <c>Longer == Shorter</c>, the relation at an invariant position (SPEC 15.6.1); one with a
/// <c>Clause</c> is a substituted declaration clause at its supplying argument or Type occurrence <c>Use</c> (SPEC 15.3.3, 15.6.1); one with
/// <c>WellFormed</c> is a callee's result premise substituted at the call <c>Use</c>, the well-formedness of its result, or the intrinsic
/// relation of an input's instantiated Type at that input (SPEC 15.3.7, 15.6.4 step 3), which classifies the record's source and is
/// discharged by judgment. <c>Discharge</c> names the check responsible for the obligation. <c>Condition</c> is the Semantics case
/// the relation is required in (SPEC 15.6.5): the pair binders in scope of <c>Use</c> that must all be borrows, as bits of their
/// order there (<see cref="Binding.PairSlotCondition"/>); its proof may use the premises of exactly those cases. <c>Body</c> is the executable
/// body Binding formed it in, none for a declaration's (Binding.ExecutableBodies.cs), part of its identity.</summary>
public readonly record struct BindingObligation(BindingObligationKind Kind, Koto Use, BindingDeadline Deadline, BoundType? Type = null, BoundOrigin? Longer = null, BoundOrigin? Shorter = null, BoundLength? Length = null, bool Equality = false, Koto? Clause = null, bool WellFormed = false, OriginDischarge Discharge = OriginDischarge.Judgment, ulong Condition = 0)
{
    internal ExecutableBody Body { get; init; }
}

internal enum TypePosition : byte
{
    Explicit,
    Parameter,
    Result,
    Local,
    InstanceStorage,
    StaticStorage,
}

// SPEC 10.7, 15.3.7: one Callable comparison of an implementation signature (Actual) with a required one (Expected). The Origins that
// the implementation binds per call are instantiated at the required inputs in the same positions; the required per-call Origins are
// rigid symbols of the comparison, and every other Origin is compared as written.
internal readonly struct CallableInstance
{
    private readonly BoundType? actual;
    private readonly Koto? actualBinder;
    private readonly BoundType? expected;
    private readonly Koto? expectedBinder;

    internal CallableInstance(BoundType? actual, Koto? actualBinder, BoundType? expected, Koto? expectedBinder)
    {
        this.actual = actual;
        this.actualBinder = actualBinder;
        this.expected = expected;
        this.expectedBinder = expectedBinder;
    }

    internal BoundType? Actual => this.actual;

    // A direct input whose outer Origin the implementation binds at that input's own position.
    internal bool IsQuantifiedInput(BoundType input, int position)
        => this.actualBinder is not null && Binding.IsBorrow(input.Semantics) && input is { Kind: BoundTypeKind.Semantics, Components.Count: 1, OriginArguments.Count: 0, Origin: { Kind: OriginKind.Input, Occurrence: null } origin } &&
            ReferenceEquals(origin.Binder, this.actualBinder) && origin.Slot == position;

    internal BoundOrigin Instantiate(BoundOrigin origin)
    {
        if (this.actual is null || this.actualBinder is null || origin is not { Kind: OriginKind.Input, Occurrence: null } || !ReferenceEquals(origin.Binder, this.actualBinder) ||
            (uint)origin.Slot >= (uint)this.actual.Components[0].Components.Count || this.expected is null ||
            (uint)origin.Slot >= (uint)this.expected.Components[0].Components.Count)
        {
            return origin;
        }

        var input = this.actual.Components[0].Components[origin.Slot];
        return ReferenceEquals(input.Origin, origin) && this.IsQuantifiedInput(input, origin.Slot) && this.expected.Components[0].Components[origin.Slot].Origin is { } required ? required : origin;
    }

    // A per-call Origin of the required signature: the outer Origin of a direct required input, bound by that signature at its slot.
    internal bool IsRequiredSlot(BoundOrigin origin)
        => this.expected is not null && this.expectedBinder is not null && origin is { Kind: OriginKind.Input, Occurrence: null } && ReferenceEquals(origin.Binder, this.expectedBinder) &&
            (uint)origin.Slot < (uint)this.expected.Components[0].Components.Count && ReferenceEquals(this.expected.Components[0].Components[origin.Slot].Origin, origin);

    // SPEC 10.7, 15.3.6: a universal Origin of the implementation that none of its inputs mentions, such as the result-only `s` of
    // `constant() -> ref/i32 during s`, is a call-time Origin that each call instantiates; it is instantiated to an open region of the
    // call, a local region that holds no Loans and is solved by its uses.
    internal bool IsResultOnlyOrigin(BoundOrigin origin)
        => this.actual is not null && this.actualBinder is FunctionKoto && origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, this.actualBinder) &&
            !Mentions(this.actual.Components[0], origin, 0);

    private static bool Mentions(BoundType type, BoundOrigin origin, int depth)
    {
        if (depth > 64 || !type.CarriesOrigin)
        {
            return depth > 64;
        }

        if (Names(type.Origin, origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (Names(type.OriginArguments[i], origin))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (Mentions(type.Components[i], origin, depth + 1))
            {
                return true;
            }
        }

        return false;

        static bool Names(BoundOrigin? candidate, BoundOrigin origin)
        {
            if (candidate is null)
            {
                return false;
            }

            if (ReferenceEquals(candidate, origin) || (candidate.Kind == OriginKind.Parameter && ReferenceEquals(candidate.Binder, origin.Binder) && candidate.Slot == origin.Slot))
            {
                return true;
            }

            for (var i = 0; i < candidate.Operands.Count; i++)
            {
                if (Names(candidate.Operands[i], origin))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

// CallQualifier: the Type expression qualifies a called member in an expression (SPEC 15.4.4), whose omitted Origin slots the call
// infers; Member judges them once the member is known.
internal readonly record struct TypeBindingContext(TypePosition Position, Koto Owner, int Slot = 0, bool Direct = false, bool SuppressOuter = false, bool CallQualifier = false)
{
    internal TypeBindingContext Nested => this with { Direct = false, SuppressOuter = false };
}

// The Origin declaration of a definition, a Type or a local (SPEC 15.3.1-15.3.6): its binding sets, the replacements that resolve its
// omitted Origins, its pending slots and its clauses, with the records they are made of (Binding.OriginDeclarations.cs binds them).
public sealed partial class Binding
{
    private sealed class OriginDeclaration(Koto owner)
    {
        internal Koto Owner { get; } = owner;

        // A local's (a FieldKoto, the parser's local declaration), whose clauses are checks that assume nothing (SPEC 15.3.3); every
        // other owner's clauses are its contract.
        internal bool IsLocal => this.Owner is FieldKoto;

        internal BindingScope? Scope { get; set; }

        // The body that opened it: a local's, or none for a declaration's (DECISIONS R4-M101 E1), whose late rows it owns.
        internal ExecutableBody Body { get; set; }

        internal int State { get; set; }

        internal Koto? Failure { get; set; }

        // The failed part its pending Origins rest on (RestPendingOn): a failed clause, or its Type, alias target or payload operand that
        // failed before completion. Written for every owner and read only for a declaration's rows (UnboundCause): a local's pending
        // Origins reach body rows only, which A5 judges (DECISIONS R4-M118).
        internal Koto? PendingFailure { get; set; }

        // The clauses that bound in a declaration whose contract failed: never premises of its uses (the contract is unavailable), only of
        // the dependence queries (ReadFailedContract while readingFailedContracts; DECISIONS BU-62, BU-69).
        internal List<OriginRelation>? RejectedRelations { get; set; }

        // Each clause that failed with its bound Origins, null where an operand is unavailable (FailOriginClause): a local's failed clause
        // leaves Unbound only the inference variables they name (RestsOnFailedClause; DECISIONS BU-68), and a contract's is repaired by
        // the dependence query (DECISIONS BU-69). Kept across binds.
        internal List<(OriginRelationKoto Clause, BoundOrigin? Left, BoundOrigin? Right)>? FailedClauses { get; set; }

        internal Dictionary<string, TypeSemanticsKoto> Sets { get; } = new(StringComparer.Ordinal);

        internal Dictionary<BoundOrigin, BoundOrigin> Replacements { get; } = new(ReferenceEqualityComparer.Instance);

        internal List<PendingOriginSlot> Pending { get; } = new(2);

        internal List<OriginRelation> Relations { get; } = new(2);

        // A local's omitted Origins inferred from its initializer (SPEC 15.4.4), with the variance of their positions in its Type;
        // allocated only for a local that has such Origins and kept across binds.
        internal Dictionary<BoundOrigin, OriginVariance>? Inferred { get; set; }

        internal void Reset()
        {
            this.State = 0;
            this.Failure = null;
            this.PendingFailure = null;
            this.RejectedRelations?.Clear();
            this.FailedClauses?.Clear();
            this.Scope = null;
            this.Body = default;
            this.Sets.Clear();
            this.Replacements.Clear();
            this.Pending.Clear();
            this.Relations.Clear();
            this.Inferred?.Clear();
        }
    }

    private readonly record struct PendingOriginSlot(Koto Use, BindingScope Scope, TypeBindingContext Context, LoanRequirement Requirement, int Slot, BoundOrigin Origin, BindingSymbol? BorrowCondition);

    private readonly record struct OriginRelation(BoundOrigin Longer, BoundOrigin Shorter, bool Equality, OriginRelationKoto Syntax);
}
