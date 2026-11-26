// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// DeclQuery (R7-D1; DECISIONS BU-105): the state of a declaration query, a fact Binding computes at most once per pass for one
// declaration and kind, which every step that needs it demands.
// - Inputs: the declaration's own BindingSymbol (its identity, kept across passes) and a DeclQueryKind; the pass stamp storageVersion.
// - Outputs: the state, NotStarted, Running, Done or Error, valid in the pass that set it, two bits per kind in BindingSymbol's ushort,
//   room for eight kinds; a ninth kind fails BindingSymbol.QueryRoom at compile time. The output stays where the kind's owner publishes
//   it (Signature, owned by BindHeader: the symbol's and its parameters' Types). Error is a failed query's answer: its kind's rule
//   recorded the failure at the declaration (SPEC 23.1.2), and each reader of the failed answer its row names rests on that record
//   (QueryRecord), never on what the computation consulted (R7-D2 d2; G812 ruling 167).
// - Invariants: a state set in an earlier pass reads NotStarted (no per-symbol reset), up to the stamp's uint copy: a state not written
//   in any of the 2^32 - 1 passes before a read aliases one 2^32 passes old (about 4.3 * 10^9 Binds of one Binding). NotStarted
//   becomes Running only through BeginQuery, which answers the prior state (the caller computes only on NotStarted); Running becomes
//   Done (CompleteQuery), Error (FailQuery: the kind's rule recorded at the declaration) or NotStarted (AbandonQuery: the output waits
//   on a fact not yet published). Done and Error are final in the pass: CompleteQuery keeps an Error, so the computation a re-entry
//   interrupted completes without clearing it. A demand of a Running query is a re-entry, answered by its kind's rule, never by
//   computing again; only a kind whose row names an Error writer writes Error. A state stamped Frozen (PLAN G949
//   S1-1's freeze of the library pass's declarations; DECISIONS BU-105 as BU-219 amends it) reads as set in every pass and is never
//   written: BeginQuery answers it and writes nothing, AbandonQuery writes nothing, and CompleteQuery on it is a broken contract
//   (InternalInvariant: a library fact completed in a program pass). The same holds for every declaration of the frozen library table,
//   stamped or not (B6: SetQueryState refuses by the declaration's table). Each kind's answer for a Frozen state that is not Done is its
//   row's "Frozen" clause.
// - Kinds (one row each): Signature. Re-entry: a Property's whose Type is not yet formed is CyclicBinding_Kd at the declaration and
//   Error (BindHeader's ReenterHeader, SPEC 11.6, 9); another header's reads no evidence and records nothing. The header frame binds
//   only what typing the declaration needs (a literal whose Type the header fixes without its body publishes no body, TypedByHeader,
//   through HeaderRunningOrFailed), so a re-entry is a SPEC 11.6 cycle for every literal it covers. Error's readers: a use of a Property left without a Type rests on the
//   declaration's record (BindReference); the declaration's own variable binding records nothing more, even where the interrupted
//   computation formed the Type, and still binds its parts (BindVariableCore); DeclaredValueType (SPEC 9.6), the conformance proof
//   (VerifyPropertyRequirement) and a use of a Property whose Type was formed read the Type only, not yet the record (PLAN G1181's
//   remainder). Not ready: a header
//   waiting on an associated Type is abandoned. Frozen: Done (the freeze verifies every library header Done). SignatureVerdict
//   (Binding.ConstraintValidation.Formation): output, the state itself: Done means Proven in this pass. Re-entry: Unknown, no evidence
//   (SPEC 8.3.3). Not ready: any other answer, or a demand before the first ValidateConformances (contractHeadersReady), is abandoned
//   and computed again at each read until R7-C2 (PLAN G375). A requirement's call is outside the query, and no reader runs inside
//   verification, so the answer is the declaration's own. A Proven stays read after a later failure. Frozen, not Done: formed at each
//   use in the user's scope, writing nothing: the per-use path reads a frozen verdict and never completes it (PLAN G375's per-use
//   form, never a kept "nothing"; G949 ruling 187). DirectBase
//   (Binding.BaseLayers): output AdtDef.DirectBase, a clean read's name or a failed clause's; a bound clause's Type answers first.
//   Re-entry: the chain ends there, counted (SPEC 9.5). Not ready: abandoned and counted; a read that moved the count stores nothing
//   (interim until R7-D2 d3). Frozen: never asked (no library struct has a base clause, so DirectBase returns before its query).
//   InheritedConformances (Binding.InheritedConformances, SPEC 8.6.2): output the struct's inherited paths in ImplTable, its answer
//   whether it is Done; demanded by every by-type read of ImplTable once clauses are bindable. Re-entry: not Done, so the heir is
//   abandoned; on a base cycle every member stays NotStarted for the pass, and the records are the base walk's and formation's, none
//   here. Not ready: a reading of its clause by DirectBase that moved the not-ready count (BaseLayers' answer: in progress or not
//   ready), or its base not Done. Frozen: Done (the freeze verifies every library struct's Done).
//   Environment (Binding.Closures' FormEnvironment; PLAN G230-H (i)): output a Closure literal's written parameter Types on their
//   symbols and its written entries in its plan and scope, demanded by its binding after its header and by a local of its body bound
//   before it (BindVariable). Error: an entry's record, or the literal resting on its evaluating body's Error; its readers, the
//   literal's binding and that local, rest on the literal. Not ready: an omitted parameter Type its binding has not bound (SPEC 7.7.1:
//   its expected Function Type gives it), its evaluating body's environment not ready, or an entry's source pending, unbound in the
//   pass or a retry with no recorded failure (G230H-10 C1; the literal rests on it). It is abandoned, with nothing recorded, and the
//   demanding local rests unbound and binds at its statement (G230H-6). Re-entry: reads the entries formed so far.
//   Frozen: never asked (no program pass binds a library literal).
// - Interims (removal stages): BindHeader's guard reads the variable binding's Resolving and BindVariableCore's guard reads
//   Signature's Running state (its CyclicBinding_Kd, a variable binding re-entered, is the variable binding's) until R7-D2 d4 makes the
//   variable binding a kind. This is the only read of a state outside this file. SignatureVerdict's not-ready path and its exclusion of a
//   requirement's call go at R7-C2, where a failed answer is final (PLAN G375).
// - Prohibitions: no output, record or message here; no per-symbol reset; no second pass stamp; no mode or bool parameter; no
//   allocation in a query (the freeze's one domain set per compilation aside); no write of a state outside this file
//   (BindingSymbol.SetQueryState, and FreezeQueries' stamp).

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

    /// <summary>What a Closure literal's body reads of the literal: its written parameter Types and entries (Binding.FormEnvironment).</summary>
    Environment,

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

    /// <summary>Failed in this pass: the kind's rule recorded at the declaration, which the row's readers rest on (Binding.QueryRecord).</summary>
    Error,
}

public sealed partial class Binding
{
    // The freeze's stamp for DeclQuery: each library symbol's states, as the library pass left them, Frozen.
    private static void FreezeQueries(HashSet<BindingSymbol> symbols)
    {
        foreach (var symbol in symbols)
        {
            symbol.FreezeQueries();
        }
    }

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

    // Running to Done; an Error stays (final in the pass).
    private void CompleteQuery(BindingSymbol symbol, DeclQueryKind kind)
    {
        var state = this.QueryState(symbol, kind);
        Debug.Assert(state is DeclState.Running or DeclState.Error, "Only a running or failed query completes.");
        if (state == DeclState.Running)
        {
            symbol.SetQueryState(kind, this.storageVersion, DeclState.Done);
        }
    }

    // Running to Error: the kind's rule has recorded the failure at the declaration (a re-entry its row names).
    private void FailQuery(BindingSymbol symbol, DeclQueryKind kind)
    {
        Debug.Assert(this.QueryState(symbol, kind) == DeclState.Running, "Only a running query fails.");
        symbol.SetQueryState(kind, this.storageVersion, DeclState.Error);
    }

    // SPEC 23.1.2: the record a demand of a failed query rests on, the declaration's own (its kind's rule recorded it), or null. The
    // readers its row names consult it, so each binding of a use in the pass reaches the same reported record.
    private Koto? QueryRecord(BindingSymbol symbol, DeclQueryKind kind) => this.QueryState(symbol, kind) == DeclState.Error ? symbol.Declaration : null;

    // SPEC 11.6 (G812 rulings 174, 176): whether the declaration's Signature query is Running or Error: its header is being computed, or
    // a re-entry failed it (the computation the re-entry interrupted continues, and the variable binding that follows binds again only
    // what is unresolved). An inferred Property's initializer is then bound only as far as typing it needs (TypedByHeader); the variable
    // binding binds what was left with its parts.
    private bool HeaderRunningOrFailed(BindingSymbol symbol) => this.QueryState(symbol, DeclQueryKind.Signature) is DeclState.Running or DeclState.Error;

    // The library's symbols whose query states the freeze keeps (FreezeLibrary, in the library pass): every library symbol the layer
    // reaches (G949 ruling 200; s11-design 2.2 s1), each with its states as the library pass left them (a symbol no query takes keeps
    // NotStarted, as a kind that does not apply), so no program pass writes one: the `symbols` family's library rows (declarations and
    // their parameters), each one's Contract Self and its Property's accessor symbols and execution functions; every symbol a library
    // scope declares (receivers among them); every symbol the library's nodes name (the published Symbols column: iteration variables
    // among them); the bound Contract references of the layer's part; $abort, which no query reads.
    private HashSet<BindingSymbol> LibraryQuerySymbols()
    {
        var symbols = new HashSet<BindingSymbol>(ReferenceEqualityComparer.Instance) { this.Library.Abort };
        foreach (var symbol in this.symbols.Values.In(this.pass))
        {
            symbols.Add(symbol);
            AddSymbol(symbols, symbol.SelfSymbol);
            AddAccessor(symbols, symbol.Property?.Getter);
            AddAccessor(symbols, symbol.Property?.Setter);
        }

        foreach (var scope in this.scopes.Values.In(this.pass))
        {
            AddDeclared(symbols, scope.Types);
            AddDeclared(symbols, scope.Parameters?.Types);
            AddDeclared(symbols, scope.Parameters?.Values);
            AddDeclared(symbols, scope.Values);
        }

        foreach (var named in this.Library.Kotonoha.Syntax.Hir.Symbols)
        {
            AddSymbol(symbols, named);
        }

        symbols.UnionWith(this.boundContracts.Values);
        return symbols;

        static void AddSymbol(HashSet<BindingSymbol> symbols, BindingSymbol? symbol)
        {
            if (symbol is not null)
            {
                symbols.Add(symbol);
            }
        }

        static void AddAccessor(HashSet<BindingSymbol> symbols, BoundAccessor? accessor)
        {
            AddSymbol(symbols, accessor?.SignatureSymbol);
            AddSymbol(symbols, accessor?.SelfSymbol);
            AddSymbol(symbols, accessor?.ValueSymbol);
            AddSymbol(symbols, accessor?.StorageSymbol);
            AddSymbol(symbols, accessor?.ExecutionFunction?.BoundSymbol);
        }

        // A scope's declarations by name, each with the symbols it shadows (Next).
        static void AddDeclared(HashSet<BindingSymbol> symbols, Dictionary<string, BindingSymbol>? declared)
        {
            if (declared is null)
            {
                return;
            }

            foreach (var first in declared.Values)
            {
                var symbol = first;
                while (symbol is not null && symbols.Add(symbol))
                {
                    symbol = symbol.Next;
                }
            }
        }
    }

    // The freeze's verify half for DeclQuery: the first library symbol whose states the freeze would not keep as the library pass's: one
    // set in an earlier run (a thrown pass's residue, which Frozen would revive), a Running query, a header DeclQuery binds (a Function or
    // Property declared at a node of the pass: the header loop's set, G949 ruling 167 Q5) whose Signature is not Done, or a struct whose
    // InheritedConformances is not Done. Null when every state is complete.
    private BindingSymbol? LibraryQueryFault(HashSet<BindingSymbol> symbols)
    {
        foreach (var symbol in symbols)
        {
            for (var kind = DeclQueryKind.Signature; kind < DeclQueryKind.Count; kind++)
            {
                if (!symbol.QueriesFrom(this.storageVersion) || this.QueryState(symbol, kind) == DeclState.Running)
                {
                    return symbol;
                }
            }
        }

        foreach (var node in this.nodes)
        {
            var header = node.BoundSymbol is { Kind: BindingSymbolKind.Function or BindingSymbolKind.Property } declared && ReferenceEquals(declared.Declaration, node);
            if ((header && this.QueryState(node.BoundSymbol!, DeclQueryKind.Signature) != DeclState.Done) ||
                (node is StructKoto { BoundSymbol: { } structure } && this.QueryState(structure, DeclQueryKind.InheritedConformances) != DeclState.Done))
            {
                return node.BoundSymbol;
            }
        }

        return null;
    }

    private void AbandonQuery(BindingSymbol symbol, DeclQueryKind kind)
    {
        Debug.Assert(this.QueryState(symbol, kind) == DeclState.Running || symbol.Declaration.SyntaxOwner is { Frozen: true }, "Only a running query, or a frozen one, is abandoned.");
        symbol.SetQueryState(kind, this.storageVersion, DeclState.NotStarted);
    }
}
