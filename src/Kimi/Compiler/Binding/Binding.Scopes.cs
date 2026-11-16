// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Scopes and symbols: the scope tree and the symbol tables of its scopes.
// - Responsibility: one scope per key, created once and attached to its parent each pass (GetScope; a source document's, which its
//   top-level statements share, DocumentScope); the scope kept for a node (ScopeOf) and the one it binds in (NodeScope); a symbol's
//   declaration in its scope's Type or value table with the duplicate rule (Declare); a function parameter's symbol by its function
//   and index, declared (DeclareParameter) and read (ParameterSymbol); the `symbols` family's one accessor (DeclaredSymbols).
// - Inputs: the indexing walk and the declaration steps that declare a symbol. Outputs: scopes, symbols, each declaring node's
//   BoundSymbol, Duplicate failures.
// - Invariants: scopes and symbols are kept across passes by their key and reset by ResetPass: a symbol in its declaration's module's
//   NodeColumns at the declaration's SyntaxId, a parameter's at its function's row by index (DECISIONS BU-220); a scope by its node or a
//   top-level statement's source document.
// - Prohibitions: no name is resolved here (lookup is Binding.Names.cs's and Binding.Types.cs's); Declare reads a scope's table only
//   to chain a name's symbols (Next) and apply the duplicate rule. No other file keys either table by anything but a node: a
//   parameter's symbol goes through DeclareParameter and ParameterSymbol, a document's scope through DocumentScope and ScopeOf
//   (DECISIONS BU-220's key rules).
public sealed partial class Binding
{
    internal BindingSymbol ParameterSymbol(FunctionKoto function, int index) => function.Accessor is { } accessor
        ? index == 0 && accessor.Receiver is not null ? accessor.SelfSymbol! : accessor.ValueSymbol!
        : this.symbols.Parameter(function, index);

    private BindingScope GetScope(Koto node, BindingScope? parent) => this.KeptScope(node, node, parent);

    // One scope per document: the module's generated function body owns it, its first top-level statement creates it.
    private BindingScope DocumentScope(SourceDocument document, BindingScope parent, Koto owner) => this.KeptScope(document, owner, parent);

    private BindingScope KeptScope(object key, Koto owner, BindingScope? parent)
    {
        if (!this.scopes.TryGetValue(key, out var scope))
        {
            scope = new(owner);
            this.scopes.Add(key, scope);
        }

        scope.Parent = parent;
        scope.Function = scope.Owner is FunctionKoto function ? function : parent?.Function;
        return scope;
    }

    // The scope a node opens, else for a top-level statement (a node directly in its module's generated function) its document's.
    private BindingScope? ScopeOf(Koto node) => this.scopes.GetValueOrDefault(node) ??
        (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } document ? this.scopes.GetValueOrDefault(document) : null);

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

    /// <summary>The `symbols` family's row (DECISIONS BU-220): the symbol declared at its node, and a function's parameters' by index.</summary>
#pragma warning disable SA1401 // Fields should be private: a declaration writes its row's slot in place (DeclareKept).
    internal struct SymbolRow
    {
        internal BindingSymbol? Symbol;
        internal BindingSymbol?[]? Parameters;
    }
#pragma warning restore SA1401

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

        /// <summary>Gets every kept symbol: the source modules by ordinal, then the library, each by ascending id, a row's symbol before
        /// its parameters (G949 D2b-d's d8 order).</summary>
        internal Enumerator Values => new(binding);

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
        internal struct Enumerator(Binding binding)
        {
            private int module = -1;
            private int parameter;
            private SymbolRow row;
            private PagedColumn<SymbolRow>.Enumerator rows;

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
                    else if (!this.NextRow())
                    {
                        return false;
                    }
                    else if (this.row.Symbol is { } symbol)
                    {
                        this.Current = symbol;
                        return true;
                    }
                }
            }

            // The next present row: the current module's next, else the next module's first.
            private bool NextRow()
            {
                var modules = binding.compilation.SourceModules;
                while (this.module > modules.Length || this.module < 0 || !this.rows.MoveNext())
                {
                    if (++this.module > modules.Length)
                    {
                        return false;
                    }

                    var table = this.module < modules.Length ? modules[this.module].Syntax : binding.Library.Kotonoha.Syntax;
                    this.rows = table.Hir.Families.Symbols.Enumerate();
                }

                (this.row, this.parameter) = (this.rows.Current, 0);
                return true;
            }
        }
    }
}
