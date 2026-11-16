// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Scopes and symbols: the scope tree and the symbol tables of its scopes.
// - Responsibility: one scope per key, created once and attached to its parent each pass (GetScope; a source document's, which its
//   top-level statements share, DocumentScope); the scope kept for a node (ScopeOf) and the one it binds in (NodeScope); a symbol's
//   declaration in its scope's Type or value table with the duplicate rule (Declare); a function parameter's symbol by its function
//   and index, declared (DeclareParameter) and read (ParameterSymbol).
// - Inputs: the indexing walk and the declaration steps that declare a symbol. Outputs: scopes, symbols, each declaring node's
//   BoundSymbol, Duplicate failures.
// - Invariants: scopes and symbols are kept across passes by their key (a node, a parameter's function and index, or a top-level
//   statement's source document) and reset by ResetPass.
// - Prohibitions: no name is resolved here (lookup is Binding.Names.cs's and Binding.Types.cs's); Declare reads a scope's table only
//   to chain a name's symbols (Next) and apply the duplicate rule. No other file keys either table by anything but a node: a
//   parameter's symbol goes through DeclareParameter and ParameterSymbol, a document's scope through DocumentScope and ScopeOf
//   (DECISIONS BU-220's key rules).
public sealed partial class Binding
{
    internal BindingSymbol ParameterSymbol(FunctionKoto function, int index) => function.Accessor is { } accessor
        ? index == 0 && accessor.Receiver is not null ? accessor.SelfSymbol! : accessor.ValueSymbol!
        : this.symbols[function.Parameters[index]];

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

    private BindingSymbol Declare(Koto key, string name, BindingSymbolKind kind, Koto node, BindingScope scope) => this.DeclareKept(key, name, kind, node, scope);

    private void DeclareParameter(FunctionKoto function, int index, BindingScope scope)
    {
        var parameter = function.Parameters[index];
        this.DeclareKept(parameter, parameter.InternalName, BindingSymbolKind.Parameter, function, scope).Slot = index;
    }

    private BindingSymbol DeclareKept(object key, string name, BindingSymbolKind kind, Koto node, BindingScope scope)
    {
        if (!this.symbols.TryGetValue(key, out var symbol))
        {
            symbol = new(name, kind, node, scope);
            this.symbols.Add(key, symbol);
        }

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
}
