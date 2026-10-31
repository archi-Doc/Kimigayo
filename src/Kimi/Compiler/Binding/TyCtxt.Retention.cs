// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns call retention (SPEC 15.6.4; design A3 4.3, C1+C8): which outputs of a call may retain which arguments, as A5's Loan flow reads it.
// Input: a callee with its call Subst, or a Value callee operand's Type (CallPlan.CalleeType, which MIR's operand reproduces) with the
// Callable signature it declares (CallPlan.DeclaredSignature, DECISIONS R4-M41), or a destroyed part whose `drop` is unknown; the calling
// definition (null: top-level statements).
// A Closure callee's contract has its call receiver at input 0 (HasReceiverInput; SPEC 15.8.2, DECISIONS R4-M148): `R/Env during ρ`, `Env`
// by value under `owner/Self`, its `let` bindings never written (D1); every Closure part of an input is read as its body sees it, its
// definition's environment under its closure context (ClosureEnvironment), one that does not substitute a CallRetention.Limit.
// A destruction retains as a call of each `drop` it runs (SPEC 15.6.4, 16.3.1; DECISIONS BU-47): a `drop`'s contract has its destruction
// receiver at input 0, though it declares no parameter (Signature numbers none), `uniq/Self` over the layer the Subst's declaring Type
// names, without an Origin; an erased destruction, of a part whose `drop` the destruction walk cannot see (SPEC 16.3.3), retains as the
// erased call `(uniq/P) -> ()` over the same receiver (RetainsErasedDestruction). The receiver, which cannot be stored, captured or
// returned, publishes no Value row: it has no Origin a caller names, and rule A keeps only the written-through input's chain.
// Output: an immutable CallRetention, one per callee, signature or erased destruction's receiver, Type instantiation and the premise
// scope or declaration it reads, for the latest Binding pass and reused by later passes until an edit (DECISIONS R4-M154): its Result
// (a StorageRetention: per input the RetentionKind flags the result retains it by, and Untied), per output it may write (Writes, ascending)
// an OutputRetention, and ReferentStores; an OutputRetention holds the StorageRetention of the output's root storage (Own: what the
// argument's direct Loans name, the referent of a borrowed input, a raw input's pointee, or what the exclusive layers and the raw
// layers the call may write that a by-value input holds refer to, in its Fields, payloads and elements too) and of the storage past its
// exclusive layers (Reached: one entry per Origin-erased storage Type, joined over that Type's occurrences, an Abstract entry for what
// an abstract part hides, and All, their join);
// CallRetention.Limit, a genuine limit only, where the contract or a Type its hold walk reaches has storage past Binding's limits (nested
// past its depth bound, which a growing recursive Type of SPEC 15.3.5 always reaches; Binding.StorageOutcome.Limit), the contract's
// substitution does not complete (no reproducer), or a raw layer's member walk is incomplete (a keeper cycle that changes its Type
// arguments; Binding.RawMembers). Storage a Binding failure left Rejected is no limit: its parts that do not substitute hold nothing and
// the rows that hold whatever they are stay (SPEC 23.1.2; DECISIONS BU-36); in a complete compilation it is a broken contract, an
// exception, which ends the check as CheckFaulted_Kd.
// Invariants: a storage retains an input when the callee's body may move a value of the input there under the premises its body is
// checked under: the instantiated contract (the Subst's Types, Self and declaring Type substituted, the callee's own Origins left as the
// call's fresh regions, so retention is per argument) and the premises of its enclosing declarations; or when both name one abstract
// carrier. Per storage (SPEC 15.6.4 read as (iii) with (β′), DECISIONS R4-M154): a position retains the input's own value (Value) only
// where its Type can hold, Origins fitted, a value of the input's Type, of a part it holds by value, or a borrow formed through it (a
// borrowed input's parts are its layer; its referent's parts, and those reached through its exclusive layers before a shared one, are
// reads or borrows formed through it; a raw input has none), an abstract part taking any value whose Origin reaches it; any other
// occurrence of the input it reaches is Content. Where the fit relates two Origins of the call's Type arguments,
// Self or declaring Type that the premises do not prove, it is the call's solved Origins' to decide (Revision 2): the storage lists the
// pair, under which the input is Value, and A5 judges it with the caller's Origin classes. An erased callable, one whose implementation
// the call cannot see (a Function Type value or a value of a Callable-bounded Type parameter, each callable its premises and inputs hand
// its body: a Callable bound, a requirement of a Contract premise, a Function part of an input; a requirement called on an abstract Self,
// a virtual or overriding function called on an abstract declaring Type), is read as its erased signature (Binding.ErasedSignature, the
// signature with the Binder of its per-call Origins): compared under premises no view reads, it relates each fixed Origin of an input to
// each fixed Origin of an output, an abstract carrier's hidden Origins among them (owner decision 2026-10-17, DECISIONS R4-M52, R4-M154).
// An unproven absence of retention is a row. Raw layers (SPEC 5.7 Calls, 15.6.4; D6, D10, D15; DECISIONS BU-24, R4-M116): nothing is
// written past a shared layer or a raw layer whose step is
// Shared; a raw parameter layer without an Origin, or with `static`, lets the call write its pointee, one whose Origin steps Exclusive
// what that Origin's borrows admit (their referents, a slot's Keepers) as that input's storage. A raw input is retained as a borrowed
// one, its pointee as the referent: an Origin inside its pointee is Content, its own layer's Origin gives no row, and its own value is
// a Value row only of an output that can hold it where the call may write it (a raw layer stating an Origin or `static`, whatever its
// pointee Type, or an abstract part where a callable the call may run may retain it there, DECISIONS BU-66, at or below a position of
// the output's writable storage: a value
// stored there brings its whole Type, every layer but a callable's, a struct's or enum's stored content included), of a result the call
// forms within its storage (Raw.slice), or by the rules below; never of its own pointee, so a raw input may have both rows to one
// output. A raw result layer with an Origin retains as that borrow would. A raw layer without an Origin that the call may write (SPEC
// 15.6.4 as DECISIONS R4-M131 states it; BU-40), in the result (a nominal Type's Fields included) or in input j's writable storage
// (each position of it, a by-value input's included, DECISIONS BU-52; a consuming callee's cleanup of the input writes only there, so
// its drop's rows are among these, SPEC 15.6.4 "including callee cleanup"), retains every input by what any chain of reads through it
// carries (Chain) and every input but j by its own value (Value), and unties that output (its records' Untied), as a `static` layer
// does without these rows; one behind a borrow in the result (a Slice's or UniqSlice's elements included, and a result raw layer
// stating a borrow's Origin, which retains as that borrow would) is lent, not written: it retains only the raw inputs, by Value and
// Chain, and unties nothing (R′). A published operation that transfers values (an Iterator's `next`, `exchange`, `swap`, an operation
// of SPEC 4.6 to 4.9, keyed by declaring identity in Binding.Retention.Transfers.cs) retains there a borrowed input by its Chain only,
// `exchange`'s result only its target's Chain, and a 4.9 operation's receiver storage, of the other inputs, only its added ones
// (KimiLibraryCatalog.Added; a member without a receiver, construction, has no receiver storage, so its result keeps every input). It
// writes only its own outputs (TransferWrites): a 4.9 operation its result and its receiver's element storage, `exchange` its result
// and its target; at another output rule A forms no row and a raw layer there unties nothing (DECISIONS BU-52: an added value's
// Fields, payloads and elements are no storage the operation writes). An operation of SPEC 4.6 to 4.9 writes the store at its
// borrowed receiver only in the storage that receiver refers to (ReferentStores; 4.9.6).
// Content and Chain are two measures: Content what one read through the input carries (premises, step 4), Chain any chain of reads.
// Files: this one (the contract and its records RetentionKind, StorageRetention, OutputRetention, CallRetention); TyCtxt.Retention.Calls.cs
// (TyCtxt.Retains, RetainsErasedDestruction and their memo, the callables handed to a body); Binding.Retention.cs with .Atoms.cs,
// .Storage.cs, .Sources.cs, .Rows.cs and .Transfers.cs (the computation).
// Must not: read the call's solved Origins or its arguments, judge an Origin relation, cover unknown callees or the operands an intrinsic
// takes beyond its library declaration (A5's; an intrinsic's declaration is a callee like any other, A3 E14), or expose its premise walk as
// a view of its own (A3 9, item 9).

namespace Kimi.Compiler;

/// <summary>How a storage may retain an input (SPEC 15.6.4; DECISIONS R4-M154): flags, joined per input.</summary>
[Flags]
internal enum RetentionKind : byte
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The argument's own value (its Loans and the holders it addresses) and, for a borrow, what a read through it carries: a
    /// position whose Type can hold a value of the input's Type, of a part it holds by value, or a borrow formed through it, Origins
    /// fitted; for a raw input, a layer of the hold walk that can hold its own value.</summary>
    Value = 1,

    /// <summary>What a read through the argument carries: any other occurrence of the input the storage reaches (step 4: an Origin inside
    /// a borrowed input's referent Type or a raw input's pointee, or one whose Type no position reached holds).</summary>
    Content = 2,

    /// <summary>What any chain of reads through the argument carries (SPEC 15.6.4: through a raw or borrowed layer anywhere in its value,
    /// then in each value read): a raw layer without an Origin the call writes or lends (DECISIONS R4-M131, BU-40). A5 closes it over
    /// every read (DECISIONS R4-M138).</summary>
    Chain = 4,
}

/// <summary>What a call may retain in one storage (DECISIONS R4-M154): per input its kinds (<see cref="Kinds"/>, by the input slots
/// <see cref="TyCtxt.Signature"/> numbers, the receiver at its written slot, a Callable signature's input positions, or a destruction's
/// input 0), whether it may receive a pointer tied to no input (<see cref="Untied"/>: a raw layer stepping Unstated or Static that the
/// call may write there, or for the result an atom reached from no input, design A3's Φ_E), and the conditions under which an input not
/// Value in <see cref="Kinds"/> is Value (<see cref="Pairs"/>: conjunctions, each of an input and the pairs of two Origins of the call's
/// Type arguments, Self or declaring Type whose relations `Longer outlives Shorter` the fit needs and the premises do not prove, one per
/// layer of a source's path the position's closure misses, the call's solved Origins' to judge; a pair `Joined` continues the previous
/// pair's conjunction; the input is Value where every pair of one of its conjunctions holds; DECISIONS R4-M154, Revisions 2 and 10).
/// Immutable.</summary>
internal sealed class StorageRetention(RetentionKind[] kinds, (int Input, BoundOrigin Longer, BoundOrigin Shorter, bool Joined)[] pairs, bool untied)
{
    private readonly RetentionKind[] kinds = kinds;
    private readonly (int Input, BoundOrigin Longer, BoundOrigin Shorter, bool Joined)[] pairs = pairs;
    private readonly bool untied = untied;

    internal ReadOnlySpan<RetentionKind> Kinds => this.kinds;

    internal ReadOnlySpan<(int Input, BoundOrigin Longer, BoundOrigin Shorter, bool Joined)> Pairs => this.pairs;

    internal bool Untied => this.untied;
}

/// <summary>What a call may retain in the storage it may write through one input (<see cref="Output"/>; DECISIONS R4-M154): its root
/// storage (<see cref="Own"/>, what the argument's direct Loans name) and the storage past that storage's exclusive layers
/// (<see cref="Reached"/>: one entry per Origin-erased storage Type, in the contract's terms, and an Abstract entry, Type null, for what an
/// abstract part hides), with <see cref="All"/>, the join of every Reached entry. Immutable.</summary>
internal sealed class OutputRetention(int output, StorageRetention own, (BoundType? Type, StorageRetention Retention)[] reached, StorageRetention all)
{
    private readonly (BoundType? Type, StorageRetention Retention)[] reached = reached;

    internal int Output { get; } = output;

    internal StorageRetention Own { get; } = own;

    internal ReadOnlySpan<(BoundType? Type, StorageRetention Retention)> Reached => this.reached;

    internal StorageRetention All { get; } = all;
}

/// <summary>A call's retention: what its result retains (<see cref="Result"/>), what the storage it may write through each input retains
/// (<see cref="Writes"/>, by ascending output), and the stores it writes only in the storage its receiver refers to (BU-42); or
/// <see cref="Limit"/>, which has none of them.</summary>
internal sealed class CallRetention(StorageRetention? result, OutputRetention[] writes, int[] referentStores)
{
    /// <summary>The output slot of a call's result, beside its inputs' slots (0 and up).</summary>
    internal const int ResultOutput = -1;

    /// <summary>A genuine limit: the retention of a call whose contract, or a Type its hold walk reaches, has storage past Binding's limits
    /// under its Subst (nested past its depth bound, which a growing recursive Type of SPEC 15.3.5 always reaches), or whose raw layer's
    /// member walk is incomplete (a keeper cycle that changes its Type arguments), as A4's effects take it: nothing is computed, and A5
    /// records a located Unsupported_Kd at the call. Storage a Binding failure left Rejected is not a Limit (DECISIONS BU-36). Reading its
    /// Result, Writes or ReferentStores is a broken contract.</summary>
    internal static readonly CallRetention Limit = new(null, [], []);

    private readonly StorageRetention? result = result;
    private readonly OutputRetention[] writes = writes;
    private readonly int[] referentStores = referentStores;

    internal bool IsLimit => this.result is null;

    internal StorageRetention Result => this.result ?? throw new InvalidOperationException("The Limit retention has no result.");

    internal ReadOnlySpan<OutputRetention> Writes
        => this.result is null ? throw new InvalidOperationException("The Limit retention has no Writes.") : this.writes;

    /// <summary>Gets the stores, ascending, that the call writes only in the storage its receiver refers to (SPEC 4.9.6: the row of an
    /// operation of 4.6 to 4.9 is its complete published effect; 15.6.4 defers to it, DECISIONS R4-M131): the store at the receiver of
    /// such an operation (a fixed-array member's and a 4.6 to 4.8 view's included) when the receiver is an exclusive borrow
    /// (Binding.Retention.Transfers.cs). The operation moves values into, out of and within that storage and writes through no layer
    /// stored there, so A5 stores there into the receiver's referents, never past them. The user code a row lists (destruction, a
    /// Callable argument's calls) is the callers' to compose (4.9.6): this states the row alone, and A5 guards a destruction it does not
    /// compose (PLAN G264). User code such an operation runs inside itself other than the destruction it composes, equality and
    /// comparison witnesses (<c>ref/Self</c>) and Callable-bounded callbacks (Shared, SPEC 8.10), reaches what an element holds only
    /// through a pointer read past a shared layer, which SPEC 5.2.1 condition 3's first limit gives no write authority (5.7: no error is
    /// required; a writing closure is Exclusive by 7.7.3 and does not bind), or through a pointer it cannot trace (static storage, an
    /// integer round trip; 5.2.1 (5), 5.5); destruction, whose <c>drop</c> has exclusive access, is the only such user code with
    /// authority, and A5 keeps W there. A witness or callback copying an element's pointer into static storage is open (DECISIONS
    /// R4-M131). A 4.6 to 4.8 view's referent is its own handle, which is sound only while no 4.6 to 4.8 operation writes an
    /// element through a view (KimiLibraryShapeTest pins the operations this covers; DECISIONS BU-42).</summary>
    internal ReadOnlySpan<int> ReferentStores
        => this.result is null ? throw new InvalidOperationException("The Limit retention has no ReferentStores.") : this.referentStores;
}
