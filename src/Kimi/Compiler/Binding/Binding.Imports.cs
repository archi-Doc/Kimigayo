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

            symbol = this.SelectQualifier(symbol, root, use);

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

    // SPEC 9.5.1: function groups from several sources of one stage gather into one group (an ImportGroup step); any other kind beside a
    // source is ambiguous, which the caller returns.
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
            return false;
        }

        this.nameSteps.Add(new(NameStepKind.ImportGroup, use, candidate, imported));
        return true;
    }

    // An ImportGroup step's publication: the use's gathered group, which its call reads (BindCallCore).
    private void AddImportCandidates(Koto use, BindingSymbol imported, BindingSymbol candidate)
    {
        this.importCandidates ??= new();
        if (!this.importCandidates.TryGetValue(use, out var candidates))
        {
            candidates = new();
            this.importCandidates.Add(use, candidates);
        }

        Add(imported, candidates);
        Add(candidate, candidates);

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

    // SPEC 9.5 stages 5 and 6 in the Type namespace: an opened target's direct members, never its formal parameters (18.2.1).
    private ResolvedPath ResolveImportedType(string name, BindingScope scope, Koto use, LookupRole role, int arity, int step)
    {
        var candidates = default(TypeCandidates);
        var documentAliases = use.CodeContext.SourceDocument is { } document ? this.aliasesByDocument.GetValueOrDefault(document) : null;
        if (use.CodeContext.SourceDocument is { } source &&
            this.namedAliases.TryGetValue((source, name), out var named) && named.Declaration!.BindingState == BindingState.Resolved)
        {
            this.AddTypeCandidates(ref candidates, named.Declaration!.BoundSymbol, scope, role, arity, named);
        }

        for (var i = 0; documentAliases is not null && i < documentAliases.Count; i++)
        {
            var alias = documentAliases[i];
            var target = this.AliasTarget(alias.Declaration!);
            if (target is not null)
            {
                this.AddTypeCandidates(ref candidates, MemberTypes(target, name), scope, role, arity, alias);
            }
        }

        if (candidates.First is not null)
        {
            return this.SelectedPath(candidates, use, step);
        }

        foreach (var path in this.compilation.DefaultAliases(use.CodeContext.Kotonoha))
        {
            var target = this.DefaultAliasTarget(use, path);
            this.AddTypeCandidates(ref candidates, target is null ? null : MemberTypes(target, name), scope, role, arity);
        }

        return candidates.First is not null ? this.SelectedPath(candidates, use, step) : new(PathEnd.Unresolved, null, use);
    }

    // SPEC 9.5 stages 5 and 6 in the Value namespace: one stage's sources together (9.5.1). A use holds one imported environment, so two
    // differing ones are Ambiguous: an implementation limit, not SPEC's rule (PLAN). A default alias's segment may fail here (part e).
    private ResolvedPath ResolveImportedValue(string name, BindingScope scope, Koto use, int step)
    {
        BindingSymbol? imported = null;
        var import = default(AliasImport);
        var ambiguous = false;
        var documentAliases = use.CodeContext.SourceDocument is { } document ? this.aliasesByDocument.GetValueOrDefault(document) : null;
        for (var i = 0; !ambiguous && documentAliases is not null && i < documentAliases.Count; i++)
        {
            var alias = documentAliases[i];
            var target = this.AliasTarget(alias.Declaration!);
            if (target is null || !target.Values.TryGetValue(name, out var candidate) || this.AccessibleImport(candidate, scope) is not { } accessible)
            {
                continue;
            }

            ambiguous = !this.MergeImport(use, accessible, ref imported) ||
                (alias.Environment is { } environment && import.Environment is { } previous && !ReferenceEquals(previous, environment));
            if (!ambiguous && alias.Environment is not null)
            {
                import = import.Environment is null ? alias : import.MergeWitness(alias);
            }
        }

        // The sources merged before an ambiguity keep their environment as the use's import, as the table did.
        if (import.Environment is not null)
        {
            this.nameSteps[step] = this.nameSteps[step] with { Import = import };
        }

        if (ambiguous || imported is not null)
        {
            return new(ambiguous ? PathEnd.Ambiguous : PathEnd.Selected, ambiguous ? null : imported, use);
        }

        foreach (var path in this.compilation.DefaultAliases(use.CodeContext.Kotonoha))
        {
            var target = this.DefaultAliasTarget(use, path);
            if (target is null || !target.Values.TryGetValue(name, out var candidate) || this.AccessibleImport(candidate, scope) is not { } accessible)
            {
                continue;
            }

            if (!this.MergeImport(use, accessible, ref imported))
            {
                return new(PathEnd.Ambiguous, null, use);
            }
        }

        return new(imported is null ? PathEnd.Unresolved : PathEnd.Selected, imported, use);
    }
}
