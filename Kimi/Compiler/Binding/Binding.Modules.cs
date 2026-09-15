// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BindingScope ModuleScope(Koto node) => this.scopes[node.CodeContext.Kotonoha.RootKoto];

    private BindingSymbol? ModuleReference(Koto use, string name)
        => this.compilation.FindReference(use.CodeContext.Kotonoha, name) is { } module ? this.moduleSymbols!.GetValueOrDefault(module) : null;

    private void IndexModuleReferences()
    {
        if (this.compilation.SourceModules.Length == 1)
        {
            return;
        }

        this.moduleSymbols ??= new();
        foreach (var module in this.compilation.SourceModules)
        {
            var scope = this.scopes[module.RootKoto];
            if (!this.moduleSymbols.TryGetValue(module, out var symbol) || !ReferenceEquals(symbol.Declaration, module.RootKoto))
            {
                symbol = new(module.Name, BindingSymbolKind.Container, module.RootKoto, scope);
                this.moduleSymbols[module] = symbol;
            }

            if (this.compilation.References(module) is { } references)
            {
                foreach (var name in references.Keys)
                {
                    if (scope.Types.TryGetValue(name, out var conflict) || scope.Values.TryGetValue(name, out conflict))
                    {
                        Fail(conflict.Declaration, BindingFailure.Duplicate);
                    }
                }
            }
        }
    }
}
