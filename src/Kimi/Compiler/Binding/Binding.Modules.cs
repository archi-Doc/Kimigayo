// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Kotonoha, BindingSymbol>? moduleSymbols;

    private BindingScope ModuleScope(Koto node) => this.scopes[node.CodeContext.Kotonoha.RootKoto];

    private BindingSymbol? ModuleReference(Koto use, string name)
        => name == "Kimi" ? this.Library.Module : this.TestReferenceAllowed(use, name) && this.compilation.FindReference(use.CodeContext.Kotonoha, name) is { } module ? this.moduleSymbols!.GetValueOrDefault(module) : null;

    private bool TestReferenceAllowed(Koto use, string name)
        => !this.compilation.IsTestBuild || !ReferenceEquals(use.CodeContext.Kotonoha, this.compilation.Kotonoha) ||
            !this.compilation.Project.ProjectFile.TestDependencies.ContainsKey(name) || TestDefinition.IsTestOnly(use);

    private void IndexModuleReferences()
    {
        this.moduleSymbols ??= new();
        foreach (var module in this.pass.Sources)
        {
            var scope = this.scopes[module.RootKoto];
            if (scope.Types.TryGetValue("Kimi", out var reserved) || scope.Values.TryGetValue("Kimi", out reserved))
            {
                this.RecordNumbered(reserved.Declaration, BindingFailure.Duplicate, []); // No first node: the name is reserved (SPEC 18.4.1).
            }

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
                        this.RecordNumbered(conflict.Declaration, BindingFailure.Duplicate, []); // No first node: a reference name.
                    }
                }
            }
        }
    }
}
