// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns call retention (SPEC 15.6.4; design A3 4.3, C1+C8): which outputs of a call may retain which arguments, as A5's Loan flow reads it.
// Input: a callee with its call Subst, or a Value callee operand's Type (CallPlan.CalleeType, which MIR's operand reproduces) with the
// Callable signature it declares (CallPlan.DeclaredSignature, DECISIONS R4-M41); the calling definition (null: top-level statements).
// Output: an immutable CallRetention, rows Retention(Input, Kind, Output), Untied and UntiedStores, one per callee or signature, Type
// instantiation and the premise scope or declaration it reads, for the latest Binding pass and reused by later passes until an edit;
// CallRetention.Limit where Binding's storage instantiation does not complete the contract (storage nested past its depth bound, which a
// growing recursive Type of SPEC 15.3.5 always reaches) or a raw layer's member walk (a keeper cycle that changes its Type arguments;
// Binding.RawMembers), never an exception.
// Invariants: an output retains an input exactly when the callee's body may move a value of the input there under the premises its
// body is checked under: the instantiated contract (the Subst's Types, Self and declaring Type substituted, the callee's own Origins
// left as the call's fresh regions, so retention is per argument) and the premises of its enclosing declarations; or when both name
// one abstract carrier. An erased callable, one whose implementation the call cannot see (a Function Type value or a value of a
// Callable-bounded Type parameter, each callable Type its contract names and each Callable bound of the callee's premises), was compared
// under premises no view reads, so it relates each fixed Origin of an input to each fixed Origin of an output, an abstract carrier's
// hidden Origins among them (owner decision 2026-10-17, DECISIONS R4-M52). An unproven absence of retention is a row. Raw layers
// (SPEC 5.7 Calls, 15.6.4; D6, D10, D15; DECISIONS BU-24, R4-M116): nothing is written past a shared layer or a raw layer whose step is
// Shared; a raw parameter layer without an Origin, or with `static`, lets the call write its pointee, one whose Origin steps Exclusive
// what that Origin's borrows admit (their referents, a slot's Keepers) as that input's storage. A raw input is retained as a borrowed
// one, its pointee as the referent: an Origin inside its pointee is Content, its own layer's Origin gives no row, and its own value is
// a Value row only of an output that can hold a value of its Type (a raw layer agreeing with it, Origins fitted, or an abstract part)
// where the call may write it: at a position of the output's Type or in a struct's or enum's stored content, through an exclusive borrow
// or a raw layer stepping Unstated, Static or Own, never past a shared one; never of its own pointee, so a raw input may have both rows
// to one output. A raw result layer with an Origin retains as that borrow would; one without, outside a nominal Type's Fields, retains
// every input, a raw one also as Content; a result the call forms within a raw parameter's storage (Raw.slice) retains its own value.
// A result or input holding a raw layer stepping Unstated or Static there is untied (Untied, UntiedStores).
// Files: this one (the contract and its records RetentionKind, Retention, CallRetention); TyCtxt.Retention.Calls.cs (TyCtxt.Retains
// and its memo); Binding.Retention.cs with .Atoms.cs and .Rows.cs (the computation).
// Must not: read the call's solved Origins or its arguments, judge an Origin relation, cover unknown callees or the operands an intrinsic
// takes beyond its library declaration (A5's; an intrinsic's declaration is a callee like any other, A3 E14), or expose its premise walk as
// a view of its own (A3 9, item 9).

namespace Kimi.Compiler;

/// <summary>How an output may retain an input (SPEC 15.6.4).</summary>
internal enum RetentionKind : byte
{
    /// <summary>An Origin of the input outside a borrowed input's referent reaches the output, which carries the argument's Loans and,
    /// for a borrow, those a read through it carries; for a raw input, the output can hold its own value (its Loans and the holders it
    /// addresses).</summary>
    Value,

    /// <summary>Only Origins inside a borrowed input's referent Type, or a raw input's pointee Type, reach the output (step 4: its
    /// OwnedOrigins), which carries the Loans a read through the argument carries.</summary>
    Content,
}

/// <summary>One retention of a call: <see cref="Output"/>, the result (<see cref="Result"/>) or the writable storage of input j (j: its
/// storage at invariant positions, such as an exclusive input's referent or a raw pointee), may retain <see cref="Input"/>. Inputs are
/// the parameter slots <see cref="TyCtxt.Signature"/> numbers, the receiver at its written slot, or a Callable signature's input
/// positions.</summary>
internal readonly record struct Retention(int Input, RetentionKind Kind, int Output)
{
    internal const int Result = -1;
}

/// <summary>A call's retention: its rows, by output (the result, then each written input) and input; whether the result is untied, so it
/// may refer to storage the caller does not own (design A3's Φ_E): a result atom is reached from no input and is not <c>static</c>, or
/// the result holds a raw layer stepping Unstated or Static; and the inputs whose writable storage holds such a layer, where the call
/// may write a pointer tied to no input (DECISIONS R4-M116 R-B2, BU-30); or <see cref="Limit"/>, which has none of them.</summary>
internal sealed class CallRetention(Retention[]? rows, bool untied, int[] untiedStores)
{
    /// <summary>The retention of a call whose contract Binding's storage instantiation does not complete under its Subst (storage nested
    /// past its depth bound, which a growing recursive Type of SPEC 15.3.5 always reaches), or whose raw layer's member walk is incomplete
    /// (a keeper cycle that changes its Type arguments), as A4's effects take it: no rows are computed, and A5 records a located
    /// Unsupported_Kd at the call. Reading its rows, Untied or UntiedStores is a broken contract.</summary>
    internal static readonly CallRetention Limit = new(null, untied: false, []);

    private readonly Retention[]? rows = rows;
    private readonly bool untied = untied;
    private readonly int[] untiedStores = untiedStores;

    internal bool IsLimit => this.rows is null;

    internal ReadOnlySpan<Retention> Rows => this.rows ?? throw new InvalidOperationException("The Limit retention has no rows.");

    internal bool Untied => this.rows is null ? throw new InvalidOperationException("The Limit retention has no Untied.") : this.untied;

    /// <summary>Gets the inputs, ascending, whose writable storage holds a raw layer that steps Unstated or Static, at a position or in a
    /// struct's or enum's stored content (Binding.Held): the call may write there a pointer tied to no input, so A5 joins Φ_E into that
    /// input's write targets.</summary>
    internal ReadOnlySpan<int> UntiedStores
        => this.rows is null ? throw new InvalidOperationException("The Limit retention has no UntiedStores.") : this.untiedStores;
}
