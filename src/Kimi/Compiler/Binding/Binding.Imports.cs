// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Selects imported declarations after ordinary scope lookup has exhausted its eligible stages.
// Inputs are a name, use and lookup role; outputs are a declaration and its imported environment or an ambiguity failure.
// Source aliases precede defaults even on an arity mismatch; Type arities and value overload sets keep their distinct contracts.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(Kotonoha Module, string Path), BindingScope?> defaultAliasTargets = new();
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
                    this.Fail(module.RootKoto, BindingFailure.MissingName, true);
                }
            }
        }
    }

    private BindingScope? DefaultAliasTarget(Koto use, string path)
    {
        if (path is "Kimi" or "::Kimi")
        {
            return this.Library.Scope;
        }

        var key = (use.CodeContext.Kotonoha, path);
        if (this.defaultAliasTargets.TryGetValue(key, out var cached))
        {
            return cached;
        }

        this.defaultAliasTargets.Add(key, null);
        var root = this.ModuleScope(use);
        var scope = root;
        var remaining = path.AsSpan();
        if (remaining.StartsWith("::"))
        {
            remaining = remaining[2..];
        }

        var first = true;
        while (!remaining.IsEmpty)
        {
            var dot = remaining.IndexOf('.');
            var name = dot < 0 ? remaining : remaining[..dot];
            scope.Types.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var symbol);
            if (first && name.SequenceEqual("Kimi"))
            {
                symbol = this.Library.Module;
            }

            symbol = this.SelectTypeCandidate(symbol, root, use, false);

            if (symbol is null && first && (!this.compilation.IsTestBuild || this.TestReferenceAllowed(use, name.ToString())) && this.compilation.References(use.CodeContext.Kotonoha) is { } references &&
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
                this.defaultAliasTargets[key] = scope;
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
            this.Fail(use, BindingFailure.Ambiguous, true);
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

    private BindingSymbol? LookupImportedType(string name, BindingScope scope, Koto use, bool core, int arity, TypeLookupRole role)
    {
        var candidates = default(TypeCandidates);
        var documentAliases = use.CodeContext.SourceDocument is { } document ? this.aliasesByDocument.GetValueOrDefault(document) : null;
        if (use.CodeContext.SourceDocument is { } source &&
            this.namedAliases.TryGetValue((source, name), out var named) && named.Declaration!.BindingState == BindingState.Resolved)
        {
            this.AddTypeCandidates(ref candidates, named.Declaration!.BoundSymbol, scope, core, arity, named, role);
        }

        for (var i = 0; documentAliases is not null && i < documentAliases.Count; i++)
        {
            var alias = documentAliases[i];
            var target = this.AliasTarget(alias.Declaration!);
            if (target is not null && target.Types.TryGetValue(name, out var candidate))
            {
                this.AddTypeCandidates(ref candidates, candidate, scope, core, arity, alias, role);
            }
        }

        if (candidates.First is not null)
        {
            return this.SelectTypeCandidate(candidates, use);
        }

        foreach (var path in this.compilation.DefaultAliases(use.CodeContext.Kotonoha))
        {
            var target = this.DefaultAliasTarget(use, path);
            this.AddTypeCandidates(ref candidates, target?.Types.GetValueOrDefault(name), scope, core, arity, role: role);
        }

        return candidates.First is not null ? this.SelectTypeCandidate(candidates, use) : null;
    }

    private BindingSymbol? LookupImportedValue(string name, BindingScope scope, Koto use, bool core)
    {
        BindingSymbol? imported = null;
        var documentAliases = use.CodeContext.SourceDocument is { } document ? this.aliasesByDocument.GetValueOrDefault(document) : null;
        for (var i = 0; documentAliases is not null && i < documentAliases.Count; i++)
        {
            var alias = documentAliases[i];
            var target = this.AliasTarget(alias.Declaration!);
            if (target is null || !target.Values.TryGetValue(name, out var candidate))
            {
                continue;
            }

            candidate = this.AccessibleImport(candidate, scope);
            if (candidate is null || (core && candidate.Kind == BindingSymbolKind.Container))
            {
                continue;
            }

            if (!this.MergeImport(use, candidate, ref imported))
            {
                return null;
            }

            if (alias.Environment is { } importedEnvironment)
            {
                if (this.importedContainers.TryGetValue(use, out var previous))
                {
                    if (!ReferenceEquals(previous.Environment, importedEnvironment))
                    {
                        this.Fail(use, BindingFailure.Ambiguous, unresolved: true);
                        return null;
                    }

                    alias = previous.MergeWitness(alias);
                }

                this.importedContainers[use] = alias;
            }
        }

        if (imported is not null)
        {
            return imported;
        }

        foreach (var path in this.compilation.DefaultAliases(use.CodeContext.Kotonoha))
        {
            var target = this.DefaultAliasTarget(use, path);
            if (target is null || !target.Values.TryGetValue(name, out var candidate))
            {
                continue;
            }

            var accessible = this.AccessibleImport(candidate, scope);
            if (accessible is null || (core && accessible.Kind == BindingSymbolKind.Container))
            {
                continue;
            }

            if (!this.MergeImport(use, accessible, ref imported))
            {
                return null;
            }
        }

        return imported;
    }
}
