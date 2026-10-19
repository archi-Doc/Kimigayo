// Logical protocol only. These immutable symbolic values stand in for producer publications.
// Object allocation, layout, Binding/MIR correspondence, and language proofs are NOT modeled.
namespace RecordsModel;

enum BodyName { Zero, Other }
enum SiteName { Call, Exit, Cleanup, Earlier, SameSpanA, SameSpanB, Clause, Fixed, WitnessA, WitnessB, Header, TypeSite }
enum BorrowName { A, B, Other }
enum GuardName { A, B }
enum PointName { Call, Return, Return2, Cleanup, Earlier }
enum AccessName { First, Second }
enum ObligationName { ReturnA, ReturnB, SecondBoundary, GuardA, GuardB, Independent, Unknown, Proof }
enum OriginValue { A, B, C }
enum TypeValue { RefA, RefB }
enum FieldName { X, Y }
enum PreparedName { Input, Other }
enum CaseMeaning { Alpha, Beta, Gamma, Delta, Red, Blue }
enum Code { Loan, DefaultBorrow, DefaultAccess, OriginRefuted, OriginUnknown, Source, Unsupported }
enum Requirement { Ownership, Other }
enum Assessment { Checked, Held, Limit }
enum ConflictKind { Access, Activation, Destruction, StorageEnd, GuardEnd, DefaultAccess, DefaultBorrow }
enum Relation { Equal, Outlives }
enum AccessKind { Read, Write }
enum Role { Anchor, Use, Store, Reservation, Call, Clause, FixedBy, Origin }
enum Coverage { GuardReverse, ReturnStorageEnd, DefaultAccess, DefaultOrigin }
enum Outcome { Refuted, Unknown, Proof }
enum Notation { Qualified, StorageClause }
enum ClosurePart { CallReceiver, CallResult }
enum EndRecipe { Expression, Borrow, Omitted, Closure }
enum Fault { Lifecycle, Domain, Mask, Requirement, Pair, Outcome, Position, Handle }

readonly record struct Id<T>(int Version, T Name) where T : struct, Enum;
readonly record struct Body(Id<BodyName>? Id)
{
    public static Body None => new(null);
}

abstract record Finite
{
    public sealed record None : Finite;
    public sealed record Borrow(Id<BorrowName> Id) : Finite;
    public sealed record Guard(Id<GuardName> Id) : Finite;
}

abstract record EffectKey
{
    public sealed record Static(Id<FieldName> Field, AccessKind Access) : EffectKey;
    public sealed record Unknown : EffectKey;
}

abstract record Issue
{
    public sealed record Source(int Condition) : Issue;
    public sealed record Origin(Id<ObligationName> Obligation, Finite Chain) : Issue;
    public sealed record Conflict(Id<PointName> At, Id<AccessName> Access, Finite Cause, ConflictKind Kind) : Issue;
    public sealed record Effect(Id<PointName> At, Id<BorrowName> Borrow, EffectKey Key) : Issue;
}

readonly record struct IssueRef(Body Body, Code Code, Id<SiteName> Subject, Issue Issue);

abstract record End(Id<OriginValue> Value)
{
    public sealed record Expression(Id<OriginValue> Origin, Notation Notation) : End(Origin);
    public sealed record Borrow(Id<OriginValue> Origin, Finite Source, Id<SiteName> Site) : End(Origin);
    public sealed record Omitted(Id<OriginValue> Origin, Id<SiteName> TypeOccurrence) : End(Origin);
    public sealed record Closure(Id<OriginValue> Origin, Id<SiteName> Header, ClosurePart Part) : End(Origin);
}

abstract record FitDestination
{
    public sealed record Type(Id<TypeValue> Value) : FitDestination;
    public sealed record OriginOnly : FitDestination; // This Reason's Shorter, not an independent end.
}

abstract record OriginSource
{
    public sealed record Fit(FitDestination Destination) : OriginSource;
    public sealed record Declared : OriginSource;
    public sealed record WellFormed : OriginSource;
}

abstract record Reason
{
    public sealed record Empty : Reason;
    public sealed record Origin(Relation Relation, End Longer, End Shorter, OriginSource Source) : Reason;
}

abstract record Copy
{
    public sealed record Outside : Copy;
    public sealed record Inlined(int Scope, Id<SiteName> Call) : Copy;
}

abstract record Fact
{
    public sealed record Site(Role Role, Id<SiteName> At) : Fact;
    public sealed record Effect(EffectKey Key, Id<SiteName> Call) : Fact;
    public sealed record CoveredBy(Coverage Rule, IssueRef Target) : Fact;
}

// Set equality uses typed values. Text is used only for the oracle and deterministic rendering.
sealed class ValueSet<T> : IEquatable<ValueSet<T>> where T : notnull
{
    private readonly HashSet<T> values;
    public ValueSet(IEnumerable<T> values) => this.values = new(values);
    public IEnumerable<T> Items => values;
    public bool Equals(ValueSet<T>? other) => other is not null && values.SetEquals(other.values);
    public override bool Equals(object? other) => other is ValueSet<T> set && Equals(set);
    public override int GetHashCode() => values.Aggregate(0, (hash, value) => hash ^ value.GetHashCode());
}

sealed record Input(IssueRef Ref, Requirement Requirement, Reason Reason, Assessment Assessment, Copy Copy, ValueSet<Fact> Facts, ulong Mask);
sealed record RowKey(IssueRef Ref, Requirement Requirement, Reason Reason, Assessment Assessment, Copy Copy, ValueSet<Fact> Facts);
sealed record Result(RowKey Key, Code Code, ulong Mask);
readonly record struct Handle(int Generation, int Index);
sealed class ProtocolFault(Fault fault) : Exception(fault.ToString())
{
    public Fault Fault { get; } = fault;
}

// Public end equality drops private finite-chain identity. Display recipes remain captured.
// The fixture's OriginValue stands for the complete immutable public Origin, not a bounded label.
sealed record PublicEnd(Id<OriginValue> Value, EndRecipe Recipe, Notation? Notation, SiteName? RecipeSite, ClosurePart? Part);
sealed record PublicReason(Relation? Relation, PublicEnd? Longer, PublicEnd? Shorter, OriginSource? Source);
sealed record PublicKey(Code Code, Id<SiteName> Subject, PublicReason Reason);
sealed record Association(Body Body, ValueSet<Fact> Facts, ValueSet<CaseMeaning> Cases, bool Bodyless);
sealed record Publication(PublicKey Key, Association[] Explanations);
