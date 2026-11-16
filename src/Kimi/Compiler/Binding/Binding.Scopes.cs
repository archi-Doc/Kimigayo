// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Scopes and symbols: the scope tree and the symbol tables of its scopes.
// - Responsibility: one scope per key, created once and attached to its parent each pass (GetScope; a source document's execution
//   scope, which its top-level statements in one module share, DocumentScope); the scope kept for a node (ScopeOf) and the one it
//   binds in (NodeScope); a symbol's declaration in its scope's Type or value table with the duplicate rule (Declare); a function
//   parameter's symbol by its function and index, declared (DeclareParameter) and read (ParameterSymbol); the `symbols` and `scopes`
//   families' accessors (DeclaredSymbols, KeptScopes) and their order over the modules (FamilyRows).
// - Inputs: the indexing walk and the declaration steps that declare a symbol. Outputs: scopes, symbols, each declaring node's
//   BoundSymbol, Duplicate failures.
// - Invariants: scopes and symbols are kept across passes in their key's module's NodeColumns and reset by ResetPass (DECISIONS
//   BU-220): a symbol at its declaration's SyntaxId, a parameter's at its function's row by index; a scope at its node's SyntaxId, a
//   document's execution scope at its module root's row by document, the module the one that holds the document's top-level items now
//   (AST2), so a document whose items sit in two modules has a scope in each (the owner's O-380 (a)).
// - Prohibitions: no name is resolved here (lookup is Binding.Names.cs's and Binding.Types.cs's); Declare reads a scope's table only
//   to chain a name's symbols (Next) and apply the duplicate rule. No other file keys either family by anything but a node: a
//   parameter's symbol goes through DeclareParameter and ParameterSymbol, a document's scope through DocumentScope and ScopeOf
//   (DECISIONS BU-220's key rules).
public sealed partial class Binding
{
    internal BindingSymbol ParameterSymbol(FunctionKoto function, int index) => function.Accessor is { } accessor
        ? index == 0 && accessor.Receiver is not null ? accessor.SelfSymbol! : accessor.ValueSymbol!
        : this.symbols.Parameter(function, index);

    private BindingScope GetScope(Koto node, BindingScope? parent) => this.KeptScope(ref KeptScopes.Slot(node), node, parent);

    // A document's execution scope (SPEC 6.1.1) in the module whose generated function holds the statement now (AST2): that body owns
    // it, its first top-level statement there creates it.
    private BindingScope DocumentScope(SourceDocument document, BindingScope parent, Koto owner)
        => this.KeptScope(ref KeptScopes.DocumentSlot(owner.Kotonoha.RootKoto, document), owner, parent);

    // The kept slot is read and written first: a later column write may carve a page and move the arena it points into.
    private BindingScope KeptScope(ref BindingScope? kept, Koto owner, BindingScope? parent)
    {
        var scope = kept ??= new(owner);
        scope.Parent = parent;
        scope.Function = scope.Owner is FunctionKoto function ? function : parent?.Function;
        return scope;
    }

    // The scope a node opens, else for a top-level statement (a node directly in its module's generated function) its document's in
    // that module.
    private BindingScope? ScopeOf(Koto node) => this.scopes.GetValueOrDefault(node) ??
        (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } body && node.CodeContext.SourceDocument is { } document
            ? KeptScopes.Document(body.Kotonoha.RootKoto, document) : null);

    private BindingScope NodeScope(Koto node, BindingScope fallback) => this.ScopeOf(node) ?? fallback;

    private BindingSymbol Declare(Koto key, string name, BindingSymbolKind kind, Koto node, BindingScope scope)
        => this.DeclareKept(ref DeclaredSymbols.Slot(key), name, kind, node, scope);

    private void DeclareParameter(FunctionKoto function, int index, BindingScope scope)
        => this.DeclareKept(ref DeclaredSymbols.ParameterSlot(function, index), function.Parameters[index].InternalName, BindingSymbolKind.Parameter, function, scope).Slot = index;

    // The kept slot is read and written first: a later column write may carve a page and move the arena it points into.
    private BindingSymbol DeclareKept(ref BindingSymbol? kept, string name, BindingSymbolKind kind, Koto node, BindingScope scope)
    {
        var symbol = kept ??= new(name, kind, node, scope);
        symbol.Scope = scope;
        // A pending synthesized constructor is declared in its structure but joins the `init` group only once it exists (SPEC 6.3.6).
        if ((name == "_" && node.Parent is ForKoto) || (kind == BindingSymbolKind.Function && node is FunctionKoto { IsOverride: true }) ||
            node is FunctionKoto { IsImplicitConstructor: true, Parent: StructKoto { ImplicitConstructorPending: true } })
        {
            node.BoundSymbol = symbol;
            return symbol;
        }

        var table = kind is BindingSymbolKind.Container or BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType ? scope.Types : scope.Values;
        if (table.TryGetValue(name, out var previous))
        {
            symbol.Next = previous;
            if ((kind != BindingSymbolKind.Function || previous.Kind != BindingSymbolKind.Function) &&
                !(kind == BindingSymbolKind.Type && node is DeclarationContainerKoto declaration && DistinctTypeArities(declaration, previous)))
            {
                this.FailDuplicate(node, previous.Declaration);
            }
        }

        table[name] = symbol;
        node.BoundSymbol = symbol;
        return symbol;
    }

#pragma warning disable SA1401 // Fields should be private: a declaration writes its row's slot in place (DeclareKept, KeptScope).
    /// <summary>The `symbols` family's row (DECISIONS BU-220): the symbol declared at its node, and a function's parameters' by index.</summary>
    internal struct SymbolRow
    {
        internal BindingSymbol? Symbol;
        internal BindingSymbol?[]? Parameters;
    }

    /// <summary>The `scopes` family's row (DECISIONS BU-220): the scope kept at its node, and at a module root its documents' execution
    /// scopes by document (one pair per document whose top-level items the module holds).</summary>
    internal struct ScopeRow
    {
        internal BindingScope? Scope;
        internal (SourceDocument Document, BindingScope? Scope)[]? Documents;
    }
#pragma warning restore SA1401

    /// <summary>A node family's present rows over the compilation's modules in G949 D2b-d's d8 order: the source modules by ordinal, then
    /// the library, each module's by ascending id.</summary>
    /// <typeparam name="TRow">The family's row.</typeparam>
    internal struct FamilyRows<TRow>(Binding binding, Func<SyntaxTable, PagedColumn<TRow>> column)
    {
        private int module = -1;
        private PagedColumn<TRow>.Enumerator rows;

        internal readonly TRow Current => this.rows.Current;

        // The current module's next row, else the next module's first.
        internal bool MoveNext()
        {
            var modules = binding.compilation.SourceModules;
            while (this.module > modules.Length || this.module < 0 || !this.rows.MoveNext())
            {
                if (++this.module > modules.Length)
                {
                    return false;
                }

                this.rows = column(this.module < modules.Length ? modules[this.module].Syntax : binding.Library.Kotonoha.Syntax).Enumerate();
            }

            return true;
        }
    }

    /// <summary>The `symbols` family's one accessor (DECISIONS BU-220): a symbol is kept in its declaration's module, the node's
    /// <see cref="Koto.SyntaxOwner"/>, at the node's SyntaxId; a function's parameters at the function's row by index. Its members keep
    /// the names its readers use.</summary>
    internal readonly struct DeclaredSymbols(Binding binding)
    {
        /// <summary>Gets every kept symbol, a parameter's included.</summary>
        internal int Count
        {
            get
            {
                var (count, values) = (0, this.Values);
                while (values.MoveNext())
                {
                    count++;
                }

                return count;
            }
        }

        /// <summary>Gets every kept symbol in <see cref="FamilyRows{TRow}"/>' order, a row's symbol before its parameters.</summary>
        internal Enumerator Values => new(new(binding, static table => table.Hir.Families.Symbols));

        internal BindingSymbol this[Koto node] => this.GetValueOrDefault(node) ?? throw new KeyNotFoundException($"No symbol is declared at `{node}`.");

        internal static ref BindingSymbol? Slot(Koto node) => ref HirTables.Of(node).Families.Symbols.Ref(node.SyntaxId).Symbol;

        internal static ref BindingSymbol? ParameterSlot(FunctionKoto function, int index)
        {
            ref var parameters = ref HirTables.Of(function).Families.Symbols.Ref(function.SyntaxId).Parameters;
            if (parameters is null || parameters.Length < function.Parameters.Count)
            {
                Array.Resize(ref parameters, function.Parameters.Count);
            }

            return ref parameters[index];
        }

        internal BindingSymbol? GetValueOrDefault(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Families.Symbols.Get(node.SyntaxId).Symbol : null;

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out BindingSymbol? symbol) => (symbol = this.GetValueOrDefault(node)) is not null;

        internal void Add(Koto node, BindingSymbol symbol)
        {
            ref var kept = ref Slot(node);
            kept = kept is null ? symbol : throw new ArgumentException($"A symbol is already declared at `{node}`.", nameof(node));
        }

        internal void Remove(Koto node)
        {
            if (this.GetValueOrDefault(node) is not null)
            {
                Slot(node) = null;
            }
        }

        internal BindingSymbol Parameter(FunctionKoto function, int index)
            => (function.SyntaxOwner is { } table && table.Hir.Families.Symbols.Get(function.SyntaxId).Parameters is { } parameters && index < parameters.Length ? parameters[index] : null)
                ?? throw new KeyNotFoundException($"No symbol is declared for parameter {index} of `{function}`.");

        /// <summary>The kept symbols in <see cref="Values"/>' order.</summary>
        internal struct Enumerator(FamilyRows<SymbolRow> rows)
        {
            private int parameter;
            private SymbolRow row;

            public BindingSymbol Current { readonly get; private set; } = null!;

            public readonly Enumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                while (true)
                {
                    if (this.row.Parameters is { } parameters && this.parameter < parameters.Length)
                    {
                        if (parameters[this.parameter++] is { } symbol)
                        {
                            this.Current = symbol;
                            return true;
                        }
                    }
                    else if (!rows.MoveNext())
                    {
                        return false;
                    }
                    else
                    {
                        (this.row, this.parameter) = (rows.Current, 0);
                        if (this.row.Symbol is { } symbol)
                        {
                            this.Current = symbol;
                            return true;
                        }
                    }
                }
            }
        }
    }

    /// <summary>The `scopes` family's one accessor (DECISIONS BU-220): a scope is kept in its key node's module at the node's SyntaxId,
    /// a document's execution scope at its module root's row by document. Its members keep the names its readers use.</summary>
    internal readonly struct KeptScopes(Binding binding)
    {
        /// <summary>Gets every kept row's scope, a scope that two nodes keep counted at each.</summary>
        internal int Count
        {
            get
            {
                var (count, values) = (0, this.Values);
                while (values.MoveNext())
                {
                    count++;
                }

                return count;
            }
        }

        /// <summary>Gets every kept scope in <see cref="FamilyRows{TRow}"/>' order, a module root's scope before its documents'.</summary>
        internal Enumerator Values => new(new(binding, static table => table.Hir.Families.Scopes));

        internal BindingScope this[Koto node]
        {
            get => this.GetValueOrDefault(node) ?? throw new KeyNotFoundException($"No scope is kept at `{node}`.");
            set => Slot(node) = value;
        }

        internal static ref BindingScope? Slot(Koto node) => ref HirTables.Of(node).Families.Scopes.Ref(node.SyntaxId).Scope;

        internal static ref BindingScope? DocumentSlot(Koto root, SourceDocument document)
        {
            ref var documents = ref HirTables.Of(root).Families.Scopes.Ref(root.SyntaxId).Documents;
            var i = IndexOf(documents, document);
            if (i < 0)
            {
                i = documents?.Length ?? 0;
                Array.Resize(ref documents, i + 1);
                documents[i].Document = document;
            }

            return ref documents![i].Scope;
        }

        internal static BindingScope? Document(Koto root, SourceDocument document)
            => root.SyntaxOwner is { } table && table.Hir.Families.Scopes.Get(root.SyntaxId).Documents is { } documents && IndexOf(documents, document) is var i and >= 0
                ? documents[i].Scope : null;

        internal BindingScope? GetValueOrDefault(Koto node) => node.SyntaxOwner is { } table ? table.Hir.Families.Scopes.Get(node.SyntaxId).Scope : null;

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out BindingScope? scope) => (scope = this.GetValueOrDefault(node)) is not null;

        internal bool ContainsKey(Koto node) => this.GetValueOrDefault(node) is not null;

        internal void Remove(Koto node)
        {
            if (this.GetValueOrDefault(node) is not null)
            {
                Slot(node) = null;
            }
        }

        private static int IndexOf((SourceDocument Document, BindingScope? Scope)[]? documents, SourceDocument document)
        {
            for (var i = 0; documents is not null && i < documents.Length; i++)
            {
                if (ReferenceEquals(documents[i].Document, document))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The kept scopes in <see cref="Values"/>' order.</summary>
        internal struct Enumerator(FamilyRows<ScopeRow> rows)
        {
            private int document;
            private ScopeRow row;

            public BindingScope Current { readonly get; private set; } = null!;

            public readonly Enumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                while (true)
                {
                    if (this.row.Documents is { } documents && this.document < documents.Length)
                    {
                        if (documents[this.document++].Scope is { } scope)
                        {
                            this.Current = scope;
                            return true;
                        }
                    }
                    else if (!rows.MoveNext())
                    {
                        return false;
                    }
                    else
                    {
                        (this.row, this.document) = (rows.Current, 0);
                        if (this.row.Scope is { } scope)
                        {
                            this.Current = scope;
                            return true;
                        }
                    }
                }
            }
        }
    }
}
