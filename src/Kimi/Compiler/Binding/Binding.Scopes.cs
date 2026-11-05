// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Scopes and symbols: the scope tree and the symbol tables of its scopes.
// - Responsibility: one scope per key, created once and attached to its parent each pass (GetScope); the scope a node binds in
//   (NodeScope); a symbol's declaration in its scope's Type or value table with the duplicate rule (Declare); a parameter's symbol.
// - Inputs: the indexing walk and the declaration steps that declare a symbol. Outputs: scopes, symbols, each declaring node's
//   BoundSymbol, Duplicate failures.
// - Invariants: scopes and symbols are kept across passes by their key (a node or a source document) and reset by ResetPass.
// - Prohibitions: no name is resolved here (lookup is Binding.Names.cs's and Binding.Types.cs's); Declare reads a scope's table only
//   to chain a name's symbols (Next) and apply the duplicate rule.
public sealed partial class Binding
{
    internal BindingSymbol ParameterSymbol(FunctionKoto function, int index) => function.Accessor is { } accessor
        ? index == 0 && accessor.Receiver is not null ? accessor.SelfSymbol! : accessor.ValueSymbol!
        : this.symbols[function.Parameters[index]];

    private BindingScope GetScope(object key, BindingScope? parent, Koto? owner = null)
    {
        if (!this.scopes.TryGetValue(key, out var scope))
        {
            scope = new(owner ?? (Koto)key);
            this.scopes.Add(key, scope);
        }

        scope.Parent = parent;
        scope.Function = scope.Owner is FunctionKoto function ? function : parent?.Function;
        return scope;
    }

    private BindingScope NodeScope(Koto node, BindingScope fallback)
    {
        if (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } source && this.scopes.TryGetValue(source, out var sourceScope))
        {
            fallback = sourceScope;
        }

        return this.scopes.TryGetValue(node, out var scope) ? scope : fallback;
    }

    private BindingSymbol Declare(object key, string name, BindingSymbolKind kind, Koto node, BindingScope scope)
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
