// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns call retention (SPEC 15.6.4; design A3 4.3, C1+C8): which outputs of a call may retain which arguments, as A5's Loan flow reads it.
// Input: a callee with its call Subst, or a Value callee operand's Type (the callee's acquisition, CallPlan.ReceiverOperation's AdaptedType
// where Binding publishes one, else CallPlan.CalleeType; read below its acquisition layers, Binding.CallableCore) with the
// Callable signature it declares (CallPlan.DeclaredSignature, DECISIONS R4-M41), or a destroyed part whose `drop` is unknown; the calling
// definition (null: top-level statements).
// A Closure callee's contract has its call receiver at input 0 (HasReceiverInput; SPEC 15.8.2, 7.7.3, DECISIONS R4-M148, Z4-M):
// `R/Env during ρ`, R the receiver Env's Type admits (ClosureReceiverIn), `Env` by value under `owner/Self`, its `let` bindings never
// written (D1), its environment written only by an exclusive R; every Closure part of an input is read as its body sees it, its
// definition's environment under its closure context (ClosureEnvironment), one that does not substitute a CallRetention.Limit. A local
// region is one form in signature and environment; one with BU-127's solved value equals it, any other reaches the meet of its sources
// (Binding.AddRegionMeet; DECISIONS: G570, PLAN G492-a (ii), the owner's O-422 (a)).
// A destruction retains as a call of each `drop` it runs (SPEC 15.6.4, 16.3.1; DECISIONS BU-47): a `drop`'s contract has its destruction
// receiver at input 0, though it declares no parameter (Signature numbers none), `uniq/Self` over the layer the Subst's declaring Type
// names, without an Origin; an erased destruction, of a part whose `drop` the destruction walk cannot see (SPEC 16.3.3), retains as the
// erased call `(uniq/P) -> ()` over the same receiver (RetainsErasedDestruction). The receiver, which cannot be stored, captured or
// returned, publishes no Value row: it has no Origin a caller names, and rule A keeps only the written-through input's chain.
// Output: an immutable CallRetention, one per callee, signature or erased destruction's receiver, Type instantiation (a direct call's
// case key, CaseSubst), the premise scope or declaration it reads and a Closure's call receiver, for the latest Binding pass and reused
// by later passes until an edit
// (DECISIONS R4-M154): its Result (a StorageRetention: per input the RetentionKind flags the result retains it by, and Untied), the result read as an output
// (ResultWrites: Own the Result, Reached the storages past the result's written layers; null where it has none; G841's H2), per output it
// may write (Writes, ascending) an OutputRetention, and ReferentStores; an OutputRetention holds the StorageRetention of the output's root storage (Own: what the
// argument's direct Loans name, the referent of a borrowed input, a raw input's pointee, or what the exclusive layers and the raw
// layers the call may write that a by-value input holds refer to, in its Fields, payloads and elements too) and of the storage past its
// exclusive layers (Reached: one entry per Origin-erased storage Type, joined over that Type's occurrences, an Abstract entry for what
// an abstract part hides, and All, their join), each storage with the hold walk's rows of the raw layers it holds itself (a raw layer
// the call writes retains in the storage holding it, never in every storage of the output; C-352);
// CallRetention.Limit, a genuine limit only, where the contract or a Type its hold walk reaches has storage past Binding's limits (nested
// past its depth bound, which a growing recursive Type of SPEC 15.3.5 always reaches; Binding.StorageOutcome.Limit), the contract's
// substitution does not complete (no reproducer), or a raw layer's member walk is incomplete (a keeper cycle that changes its Type
// arguments; Binding.RawMembers); a Deep contract (CallRetention.IsDeep), its rows with a part past the bound beside them, where a walk
// over a contract Type leaves a finding undecided past its own bound and both a holder the rows name and the finding's subject can carry
// a Loan or an address (Binding.Retention.cs, retentionPast; G408B-17, R4-M153's Join). Storage a Binding failure left Rejected is no
// limit: its parts that do not substitute hold nothing and the rows that hold whatever they are stay (SPEC 23.1.2; DECISIONS BU-36); in
// a complete compilation it is a broken contract, an exception, which ends the check as CheckFaulted_Kd.
// Invariants: a storage retains an input when the callee's body may move a value of the input there under the premises its body is
// checked under: the instantiated contract (the Subst's Types, Self and declaring Type substituted, the callee's own Origins left as the
// call's fresh regions, so retention is per argument) and the premises of its enclosing declarations; or when both name one abstract
// carrier. Per storage (SPEC 15.6.4 read as (iii) with (β′), DECISIONS R4-M154): a position retains the input's own value (Value) only
// where its Type can hold, Origins fitted, a value of the input's Type, of a part it holds by value, or a borrow formed through it (a
// borrowed input's parts are its layer; its referent's parts, and those reached through its exclusive layers before a shared one, are
// reads or borrows formed through it; a raw input has none), an abstract part taking any value whose Origin reaches it, and raw storage
// a value holds (`during self` content, an unstated raw layer's pointee) any Type, so a borrow formed within it, punned or not, is formed
// through its holder (SPEC 5.7, 5.2; DECISIONS BU-91); any other
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
// a Value row only of an output that can hold it where the call may write it (a raw layer stating an Origin or `static` where one of its
// own layers states an Origin that one's premise closure reaches, whatever its pointee Type: a raw layer stating none fits it only by a
// claim, SPEC 5.7, so it is no row there, the owner's O-329 (a); or an abstract part where a callable the call may run may retain it there, DECISIONS BU-66, at or below a position of
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
// writes only its own outputs (TransferWrites): a 4.9 operation its result and its receiver's element storage; at another output rule
// A forms no row and a raw layer there unties nothing (DECISIONS BU-52: an added value's
// Fields, payloads and elements are no storage the operation writes). An operation of SPEC 4.6 to 4.9 writes the store at its
// borrowed receiver only in the storage that receiver refers to (ReferentStores; 4.9.6).
// Content and Chain are two measures: Content what one read through the input carries (premises, step 4), Chain any chain of reads.
// An input through which the callee's operation contract stores no value (KimiLibraryCatalog.NoStore, C-189, e.g. Raw.slice's storage;
// SPEC 5.2 Placement, 5.6; the value `replace` and `exchange` place in their target, 15.7) is no output: the call writes no storage
// through it, so it has no Writes entry, rule A forms no row there and nothing unties it (PLAN G780).
// Keeps: whether the result or a storage the call may write may hold an input (MirValidator.ConstantRecords).
// Files: this one (the contract and its records RetentionKind, StorageRetention, OutputRetention, CallRetention); TyCtxt.Retention.Calls.cs
// (TyCtxt.Retains, RetainsErasedDestruction and their memo, the callables handed to a body); Binding.Retention.cs with .Atoms.cs,
// .Storage.cs, .Sources.cs, .Rows.cs and .Transfers.cs (the computation).
// Must not: read the call's solved Origins or its arguments, judge an Origin relation, cover unknown callees or the operands an intrinsic
// takes beyond its library declaration (A5's; an intrinsic's declaration is a callee like any other, A3 E14), or expose its premise walk as
// a view of its own (A3 9, item 9).

using Kimi.Compiler.Parsing;

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

    /// <summary>Whether this storage may hold an input, by any kind or under a condition.</summary>
    /// <param name="input">The input slot.</param>
    /// <returns>Whether a row names it.</returns>
    internal bool Holds(int input)
    {
        if ((uint)input < (uint)this.kinds.Length && this.kinds[input] != RetentionKind.None)
        {
            return true;
        }

        foreach (var pair in this.pairs)
        {
            if (pair.Input == input)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>What a call may retain in the storage it may write through one input (<see cref="Output"/>; DECISIONS R4-M154): its root
/// storage (<see cref="Own"/>, what the argument's direct Loans name) and the storage past that storage's exclusive layers
/// (<see cref="Reached"/>: one entry per Origin-erased storage Type, in the contract's terms, and an Abstract entry, Type null, for what an
/// abstract part hides), with <see cref="All"/>, the join of every Reached entry. Every storage the output's walk enters past a layer has
/// its entry, also where it retains nothing; a call has no OutputRetention for an output none of whose storages retains anything.
/// <see cref="Kept"/>: the called function, where this output was read in its terms (SPEC 15.6.4's abstract-storage sentence; PLAN
/// G935): a storage past a layer that lies in the binding of that function's own kept abstract Type then has its entry under that
/// Type, a part of a mixed storage included (<c>TyCtxt.EntryType</c> names a storage's entry); null for a callee whose rows SPEC
/// states, an erased callee and a by-value input that is the kept Type itself, which are read instantiated. A kept Type's entry and
/// an instantiated Type's are one entry only where the call's Subst names the callee's own Types (a self-recursive generic call, a
/// Closure or a nested function inside the callee): the Types are then one and their rows are joined. <see cref="Handed"/>: where
/// <see cref="Kept"/> is not null, the kept Types this output's walk meets that a callable the callee's declaration hands its body
/// writes (SPEC 15.6.4 "through its result or its writes"; PLAN G935, C-370, the owner's O-398 (a)). Immutable.</summary>
internal sealed class OutputRetention(int output, StorageRetention own, (BoundType? Type, StorageRetention Retention)[] reached, StorageRetention all, FunctionKoto? kept, BoundType[] handed)
{
    private readonly (BoundType? Type, StorageRetention Retention)[] reached = reached;
    private readonly BoundType[] handed = handed;

    internal int Output { get; } = output;

    internal FunctionKoto? Kept { get; } = kept;

    /// <summary>Gets the kept Types this output's walk meets (Origins erased, as <c>TyCtxt.EntryType</c> names a kept storage's entry)
    /// through whose binding a callable the called function's declaration hands its body (a Callable bound, a requirement of a Contract
    /// premise, a Self receiver's included, or a callable value an input holds; the list Binding's fills read in a kept binding) writes:
    /// one of its parameters reaches a value of that Type through no shared layer (Binding's Writes). A call through such a callable
    /// writes past that binding's references by SPEC 15.6.4's routes, so "a raw layer the binding holds adds no reach of its own" is not
    /// the whole of the call's reach in that Type's binding; it stays so in the binding of a kept Type not listed (the owner's O-398 (a):
    /// a writer of T leaves U's hidden layers hidden). A reader asks for the kept Type at its key (<c>EntryType</c>'s). The walk meets a
    /// kept Type at any position it enters, also one the call does not write (a by-value input's part, as <c>Bx&lt;T&gt;</c>'s item): this
    /// is no claim that the kept Type lies in written storage. Empty where <see cref="Kept"/> is null. Input-agnostic: a writer that takes
    /// only the kept Type still writes what the value holds.</summary>
    internal ReadOnlySpan<BoundType> Handed => this.handed;

    internal StorageRetention Own { get; } = own;

    internal ReadOnlySpan<(BoundType? Type, StorageRetention Retention)> Reached => this.reached;

    internal StorageRetention All { get; } = all;
}

/// <summary>A call's retention: what its result retains (<see cref="Result"/>), the storages past the result's written layers
/// (<see cref="ResultWrites"/>), what the storage it may write through each input retains (<see cref="Writes"/>, by ascending output),
/// and the stores it writes only in the storage its receiver refers to (BU-42); or <see cref="Limit"/>, which has none of them. A Deep
/// contract (<see cref="IsDeep"/>) has them with a part past the bound beside them: its rows are a lower bound on the exact rows, so
/// a reader that a present row moves (a record, a Loan or a plan entry) follows them, and A5 records the Deep part at the call
/// (R4-M153's Join: a decided part ranks over Deep); every answer from a row's absence (<see cref="Keeps"/>, the storing rule's
/// refusal, a Closure's receiver independence) is conservative: it reads a Deep contract as the Limit. Immutable.</summary>
internal sealed class CallRetention(StorageRetention? result, OutputRetention? resultWrites, OutputRetention[] writes, int[] referentStores, bool composes, bool deep)
{
    /// <summary>The output slot of a call's result, beside its inputs' slots (0 and up).</summary>
    internal const int ResultOutput = -1;

    /// <summary>A genuine limit: the retention of a call whose contract, or a Type its hold walk reaches, has storage past Binding's limits
    /// under its Subst (nested past its depth bound, which a growing recursive Type of SPEC 15.3.5 always reaches), or whose raw layer's
    /// member walk is incomplete (a keeper cycle that changes its Type arguments), as A4's effects take it: nothing is computed, and A5
    /// records a located Unsupported_Kd at the call. Storage a Binding failure left Rejected is not a Limit (DECISIONS BU-36). Reading its
    /// Result, Writes or ReferentStores is a broken contract.</summary>
    internal static readonly CallRetention Limit = new(null, null, [], [], composes: true, deep: false);

    private readonly StorageRetention? result = result;
    private readonly OutputRetention? resultWrites = resultWrites;
    private readonly OutputRetention[] writes = writes;
    private readonly int[] referentStores = referentStores;

    internal bool IsLimit => this.result is null;

    /// <summary>Gets a value indicating whether a part of the contract lies past a walk's bound beside its rows (G408B-17): a finding a
    /// retention walk left undecided there where both a holder the rows name and the finding's subject can carry a Loan or an address
    /// (Binding.Retention.Rows.cs). Its rows are those found within the bound; never with <see cref="IsLimit"/>.</summary>
    internal bool IsDeep { get; } = deep;

    /// <summary>Gets a value indicating whether the call's writes and reads compose within it (SPEC 15.6.4: "Except for the operations whose
    /// writes SPEC states, ... a call's writes and reads compose within the call"; PLAN G935, F's ruling 12): false for `replace`,
    /// `exchange`, `swap`, `Raw.initialize` and the operations of SPEC 4.6 to 4.9, by declaring identity (Binding.Retention.Transfers.cs,
    /// StatesWrites over TransferKey's table), whose stated stores stay in their Reached entries. A5's
    /// rule 4 (the call's own pointer) and H2 read it before they take any entry.</summary>
    internal bool Composes { get; } = composes;

    /// <summary>Whether the call may keep an input past its end: its result or a storage it may write holds it, and the Limit, which states
    /// nothing, and a Deep contract, whose rows are a lower bound, may. False is what an inspection means (SPEC 13.4: the `bool` result
    /// keeps none).</summary>
    /// <param name="input">The input slot.</param>
    /// <returns>Whether the input may outlive the call in any output.</returns>
    internal bool Keeps(int input)
    {
        if (this.result is null || this.IsDeep || this.result.Holds(input))
        {
            return true;
        }

        foreach (var written in this.writes)
        {
            if (written.Own.Holds(input) || written.All.Holds(input))
            {
                return true;
            }
        }

        return false;
    }

    internal StorageRetention Result => this.result ?? throw new InvalidOperationException("The Limit retention has no result.");

    /// <summary>Gets the result read as an output (G841's H2, PLAN G935): its Own is <see cref="Result"/>, the whole result Type's record,
    /// and its Reached entries the storages past the result's layers the call may write, each with the rows of the positions and raw
    /// layers it holds, by the walk an input's output takes (Binding.Retention.Rows.cs); null where the result has no such storage.</summary>
    internal OutputRetention? ResultWrites
        => this.result is null ? throw new InvalidOperationException("The Limit retention has no ResultWrites.") : this.resultWrites;

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
