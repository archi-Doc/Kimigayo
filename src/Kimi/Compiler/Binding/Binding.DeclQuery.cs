// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;

namespace Kimi.Compiler;

// DeclQuery (R7-D1; DECISIONS BU-105): the state of a declaration query, a fact Binding computes at most once per pass for one
// declaration and kind, which every step that needs it demands.
// - Inputs: the declaration's own BindingSymbol (its identity, kept across passes) and a DeclQueryKind; the pass stamp storageVersion.
// - Outputs: the state, NotStarted, Running or Done, valid in the pass that set it, two bits per kind in BindingSymbol's ushort, room for
//   eight kinds and D2's Error state; a ninth kind fails BindingSymbol.QueryRoom at compile time. The output stays where the kind's owner
//   publishes it (Signature, owned by BindHeader: the symbol's and its parameters' Types).
// - Invariants: a state set in an earlier pass reads NotStarted (no per-symbol reset), up to the stamp's uint copy: a state not written
//   in any of the 2^32 - 1 passes before a read aliases one 2^32 passes old (about 4.3 * 10^9 Binds of one Binding). NotStarted
//   becomes Running only through BeginQuery, which answers the prior state (the caller computes only on NotStarted); Running becomes
//   Done (CompleteQuery) or NotStarted (AbandonQuery: the output waits on a fact not yet published). Done is final in the pass. A
//   demand of a Running query is a re-entry, answered by its kind's rule, never by computing again.
// - Kinds (one row each): Signature. Re-entry: an inferred stored Property's is CyclicBinding_Kd at the declaration; another header's
//   reads no evidence and records nothing. Not ready: a header waiting on an associated Type is abandoned. No Error state before its
//   writer (R7-D2 d2). SignatureVerdict (Binding.ConstraintValidation.Formation): output, the state itself: Done means Proven in this
//   pass. Re-entry: Unknown, no evidence (SPEC 8.3.3). Not ready: any other answer, or a demand before the first ValidateConformances
//   (contractHeadersReady), is abandoned and computed again at each read until R7-C2 (PLAN G375). A requirement's call is outside the
//   query, and no reader runs inside verification, so the answer is the declaration's own. A Proven stays read after a later failure.
//   DirectBase (Binding.BaseLayers): output AdtDef.DirectBase, a clean read's name or a failed clause's; a bound clause's Type answers
//   first. Re-entry: the chain ends there, counted (SPEC 9.5). Not ready: abandoned and counted; a read that moved the count stores
//   nothing (interim until R7-D2 d3). InheritedConformances (Binding.InheritedConformances, SPEC 8.6.2): output the struct's inherited
//   paths in ImplTable, its answer whether it is Done; demanded by every by-type read of ImplTable once clauses are bindable. Re-entry:
//   not Done, so the heir is abandoned; on a base cycle every member stays NotStarted for the pass, and the records are the base walk's
//   and formation's, none here. Not ready: a reading of its clause by DirectBase that moved the not-ready count (BaseLayers' answer:
//   in progress or not ready), or its base not Done.
// - Interims (removal stages): a Signature re-entry's CyclicBinding_Kd is recorded by the demanders that record it today
//   (BindReference, BindVariableCore) until R7-D2 d2 moves it into the kind's rule with the Error state; BindHeader's guard reads the
//   variable binding's Resolving and BindVariableCore's guard reads Signature's Running state until R7-D2 d4 makes the variable binding
//   a kind. These two are the only reads of a state outside this file. SignatureVerdict's not-ready path and its exclusion of a
//   requirement's call go at R7-C2, where a failed answer is final (PLAN G375).
// - Prohibitions: no output, record or message here; no per-symbol reset; no second pass stamp; no mode or bool parameter; no
//   allocation; no write of a state outside this file (BindingSymbol.SetQueryState).

/// <summary>A declaration query's kind: a fact Binding computes once per pass for one declaration (Binding.DeclQuery).</summary>
internal enum DeclQueryKind : byte
{
    /// <summary>A function's, property's or annotated variable's header (Binding.BindHeader).</summary>
    Signature,

    /// <summary>A function's declared signature formed in its own scope: Done means Proven (Binding.CheckSignatureTypeConstraints).</summary>
    SignatureVerdict,

    /// <summary>A struct's base clause read as the declaration it names (Binding.BaseLayers).</summary>
    DirectBase,

    /// <summary>A struct's inherited conformance paths registered in ImplTable (Binding.InheritedConformances).</summary>
    InheritedConformances,

    /// <summary>The number of kinds; BindingSymbol packs two bits per kind, so a ninth kind fails its compile-time check.</summary>
    Count,
}

/// <summary>A declaration query's state in a pass.</summary>
internal enum DeclState : byte
{
    /// <summary>Not demanded in this pass.</summary>
    NotStarted,

    /// <summary>Being computed; a demand now is a re-entry.</summary>
    Running,

    /// <summary>Computed in this pass.</summary>
    Done,
}

public sealed partial class Binding
{
    private DeclState QueryState(BindingSymbol symbol, DeclQueryKind kind) => symbol.QueryState(kind, this.storageVersion);

    private DeclState BeginQuery(BindingSymbol symbol, DeclQueryKind kind)
    {
        var state = this.QueryState(symbol, kind);
        if (state == DeclState.NotStarted)
        {
            symbol.SetQueryState(kind, this.storageVersion, DeclState.Running);
        }

        return state;
    }

    private void CompleteQuery(BindingSymbol symbol, DeclQueryKind kind)
    {
        Debug.Assert(this.QueryState(symbol, kind) == DeclState.Running, "Only a running query completes.");
        symbol.SetQueryState(kind, this.storageVersion, DeclState.Done);
    }

    private void AbandonQuery(BindingSymbol symbol, DeclQueryKind kind)
    {
        Debug.Assert(this.QueryState(symbol, kind) == DeclState.Running, "Only a running query is abandoned.");
        symbol.SetQueryState(kind, this.storageVersion, DeclState.NotStarted);
    }
}
