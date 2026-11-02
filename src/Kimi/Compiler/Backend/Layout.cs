// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Layout, one instance per compilation (Compilation.Layout; IMPL 21.1; plan P7a-P7c, K10c; DECISIONS R5-M19, R5-M29, R5-M37, R5-M52,
// R5-M155). Responsibility: the size, alignment, stride, Field offsets and LLVM representation of a closed Type (PD2: code generation
// and the collector call it, never a copy), and its verdict: OK, a typed generation limit, an unavailable C layout anywhere in the 64-level
// window (SPEC 6.7.1, the one judge of its four conditions: open, derived or empty from Binding's published AdtDef.CLayoutUnavailable,
// a zero-sized Field from the placed parts; S2-S4 X18), a layout over 2^63 - 1 bytes (Oversized, IMPL 21.1.1), or a failure derived
// from Binding's rejected storage or from an abstract Type (Inputs). Rows (Layout.Rows.cs; static, Row): IMPL 21.1.3's scalars, Unit,
// string, Never, borrows and raw pointers, object and function handles, and the sequence handles (Slice, Array, Dictionary words, each
// with its length word, LengthWord).
// Placement (Layout.Placement.cs): structs (Loan<T> among them), Tuples, fixed arrays, enums, Closures and Function Items, memoized
// per program version, each placement's entry and its one decision in Layout.Decision.cs; Weak<S> comes with its operations (P41).
// Inputs: a closed, interned Type of the owning compilation, or an abstract Type (BoundType.IsAbstract: a parameter, a projection or a
// dependent Semantics term), which no closed Type holds: Of gives it Rejected naming itself without asking Row (DECISIONS R5-M155;
// after a failed Binding a qualified projection that names no Type, PLAN G293; in a complete compilation an open Type, a broken
// contract the collector records at its use, PLAN G187: never a fault); AdtDef (stored Types, IsCLayout, CLayoutUnavailable,
// InlineCycle); IMPL 21.1.3's rows. A borrow, raw pointer, handle or sequence is laid out without reading its referent or element Type
// (X34; Indirect states it, the boundary of a root's storage, DECISIONS R5-M149).
// Outputs: a Result (Outcome, Value, Instance, Oversized); Indirect(type); Row(type), a static row, which code generation reads
// (DECISIONS R5-M148), never asked for an abstract Type (Inputs); it throws
// for a Type without a row, a kind outside Layout's input contract in every compilation. The
// collector (InstanceCollector) alone maps the outcomes: a
// limit to one Resource record per erased Type and declaration, at the declaration's least use, made at the collection's end
// (R5-M171), CLayout to InvalidCLayout_Kd and Oversized to
// OversizedLayout_Kd at each use (R5-M55, R5-M52), Rejected to a derived failure without a record (InternalInvariant_Kd in a complete
// compilation, DECISIONS BU-38); codegen after acceptance treats any outcome but Ok as a broken contract (InternalInvariant_Kd): the
// collector's uses cover every Type it lays out (its by-value set and LayoutNeeds; DECISIONS R5-M149). No size is published.
// After a failed Binding: Rejected for a declaration with InlineCycle, a null stored Field Type or Case (BU-37), a fixed array with
// Length < 0, or an abstract Type (Inputs); an unresolved base,
// which Binding publishes as no base, is laid out without it (Ok; no record follows a failed Binding).
// Invariants: no alignment exceeds 16, the heap's (SPEC 21.4.3; DECISIONS R5-M92): a row's is at most 16 and a placement takes its parts'
// maximum; an answer depends only on the Type and the program version, never on query order; one record per representation
// within a program version; Oversized only for Outcome Oversized and an oversized CLayout; every memo entry's size is exact or a lower
// bound, 2^63 for any size above 2^63 - 1; warm calls allocate 0 B; at most 65 placement levels per call and 65 memo entries per Type;
// the complete base is a prefix of a derived struct, its tail padding included (X1); Never is zero-sized, laid out as Unit (IMPL 21.1.3).
// Must not read syntax (AdtDef.IsCLayout is the C-layout fact), Origins, HIR, MIR or ownership state, decide destruction (DropGlue's),
// keep static mutable state, detect cycles itself (Binding's InlineCycle), decide derived
// failures (the collector's), lay out IMPL 21.1.3's nonnull Option before PLAN U8 (STATUS), use LINQ, be called concurrently on one
// instance, or be given another compilation's Types (their stored Types follow that compilation's edits).

namespace Kimi.Compiler.Backend;

/// <summary>The storage layout of one compilation's closed Types (IMPL 21.1).</summary>
internal sealed partial class Layout
{
    /// <summary>IMPL 21.3.2's bound on a Type's placement height: a row is 0 high; a struct, Tuple, Closure, Function Item, fixed
    /// array or enum 1 more than its highest base, Field, component, element or Case payload Tuple.</summary>
    internal const int MaximumHeight = 64;

    /// <summary>A Type's verdict (S2-S4 X18, refined by DECISIONS R5-M37).</summary>
    internal enum Outcome : byte
    {
        /// <summary>Laid out: the result's Value is its record.</summary>
        Ok,

        /// <summary>A size or offset exceeds 2^31 - 1, none 2^63 - 1 (IMPL 21.3.2).</summary>
        SizeLimit,

        /// <summary>A fixed array holds more than 2^31 - 1 elements, zero-sized ones included, and the size does not exceed 2^63 - 1
        /// (IMPL 21.3.2).</summary>
        CountLimit,

        /// <summary>The placement height exceeds <see cref="MaximumHeight"/> and no CLayout lies in the window (IMPL 21.3.2).</summary>
        DepthLimit,

        /// <summary>The result's Instance, a C-layout instance placed in the window, has no C layout: its declaration is open, derived or
        /// empty, or it stores a zero-sized Field (SPEC 6.7.1, 8.15.3).</summary>
        CLayout,

        /// <summary>The result's Instance is a struct, enum or fixed array whose storage Binding rejected, or an abstract Type (DECISIONS
        /// R5-M155): derived, never reported.</summary>
        Rejected,

        /// <summary>The Type needs a size, stride or Field offset over 2^63 - 1 (IMPL 21.1.1), a Language failure at the use; Value and
        /// Instance are null.</summary>
        Oversized,
    }

    /// <summary>Gets the layout and verdict of a closed Type of this compilation, memoized for the program version; an abstract Type is
    /// Rejected (the contract's Inputs).</summary>
    /// <param name="type">A Type of one of the laid-out rows, or an abstract Type.</param>
    /// <returns>Ok with the representation's one record, or the Type's other outcome (precedence: CLayout in the window,
    /// Oversized, DepthLimit, a limit, Rejected, the Type's own size).</returns>
    internal Result Of(BoundType type) => this.Walk(type, MaximumHeight).Result;

    /// <summary>A Type's layout and verdict: <see cref="Value"/> exactly for Ok, <see cref="Instance"/> exactly for CLayout and
    /// Rejected, <see cref="Oversized"/> exactly for Oversized and a CLayout whose size is also over 2^63 - 1.</summary>
    /// <param name="Outcome">The verdict.</param>
    /// <param name="Value">The representation's one record, when the Type is laid out.</param>
    /// <param name="Instance">The C-layout instance storing a zero-sized Field, or the Type whose storage Binding rejected or the abstract
    /// Type.</param>
    /// <param name="Oversized">Whether the Type needs a layout over 2^63 - 1 bytes.</param>
    internal readonly record struct Result(Outcome Outcome, ValueLowering? Value, BoundType? Instance, bool Oversized)
    {
        /// <summary>Gets a value indicating whether the outcome is a generation limit (IMPL 21.3.2), a Resource record's cause.</summary>
        internal bool IsLimit => this.Outcome is Outcome.SizeLimit or Outcome.CountLimit or Outcome.DepthLimit;
    }
}
