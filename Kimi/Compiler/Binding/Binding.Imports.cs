// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<Koto, List<BindingSymbol>>? importCandidates;

    private BindingSymbol? AccessibleImport(BindingSymbol head, BindingScope scope)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if (this.Accessible(candidate, scope))
            {
                return candidate;
            }
        }

        return null;
    }

    private void ValidateDefaultAliases()
    {
        foreach (var module in this.compilation.SourceModules)
        {
            foreach (var path in this.compilation.DefaultAliases(module))
            {
                if (this.DefaultAliasTarget(module.RootKoto, path) is null)
                {
                    Fail(module.RootKoto, BindingFailure.MissingName, true);
                }
            }
        }
    }

    private BindingScope? DefaultAliasTarget(Koto use, string path)
    {
        var root = this.ModuleScope(use);
        var scope = root;
        var remaining = path.AsSpan();
        var first = true;
        while (!remaining.IsEmpty)
        {
            var dot = remaining.IndexOf('.');
            var name = dot < 0 ? remaining : remaining[..dot];
            scope.Types.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var symbol);
            if (first && name.SequenceEqual("Core"))
            {
                symbol = this.Core.Module;
            }

            if (symbol is null && first && this.compilation.References(use.CodeContext.Kotonoha) is { } references &&
                references.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var module))
            {
                symbol = this.moduleSymbols!.GetValueOrDefault(module);
            }

            if (symbol is null || !this.Accessible(symbol, root) || !this.scopes.TryGetValue(symbol.Declaration, out scope))
            {
                return null;
            }

            if (dot < 0)
            {
                return scope;
            }

            remaining = remaining[(dot + 1)..];
            first = false;
        }

        return null;
    }

    private bool MergeImport(Koto use, BindingSymbol candidate, ref BindingSymbol? imported)
    {
        if (imported is null)
        {
            imported = candidate;
            return true;
        }

        if (ReferenceEquals(imported, candidate))
        {
            return true;
        }

        if (imported.Kind != BindingSymbolKind.Function || candidate.Kind != BindingSymbolKind.Function)
        {
            Fail(use, BindingFailure.Ambiguous, true);
            return false;
        }

        this.importCandidates ??= new();
        if (!this.importCandidates.TryGetValue(use, out var candidates))
        {
            candidates = new();
            this.importCandidates.Add(use, candidates);
        }

        Add(imported, candidates);
        Add(candidate, candidates);
        return true;

        static void Add(BindingSymbol head, List<BindingSymbol> candidates)
        {
            for (var symbol = head; symbol is not null; symbol = symbol.Next)
            {
                if (!candidates.Contains(symbol))
                {
                    candidates.Add(symbol);
                }
            }
        }
    }
}
