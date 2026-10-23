// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Resolves source aliases to their container scopes and environments, and rebuilds that state for each Binding pass.
// Inputs are source-local alias declarations; outputs are the selected containers and named/open alias lookup entries.
// Alias targets never resolve through another alias. Ordinary lookup owns which imported candidate a use selects.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<SourceDocument, List<AliasKoto>> aliasesByDocument = new();
    private readonly Dictionary<AliasKoto, BindingScope?> aliasTargets = new();
    private readonly Dictionary<(SourceDocument Document, string Name), AliasKoto> namedAliases = new();
    private readonly List<AliasKoto> aliasWarnings = new();
    private readonly Dictionary<Koto, BoundType> importedContainerEnvironments = new(ReferenceEqualityComparer.Instance);
    private int aliasResolutionDepth;

    private void ResetAliases()
    {
        foreach (var list in this.aliasesByDocument.Values)
        {
            list.Clear();
        }

        this.aliasTargets.Clear();
        this.importedContainerEnvironments.Clear();
        this.defaultAliasTargets.Clear();
        this.namedAliases.Clear();
        this.aliasWarnings.Clear();
    }

    private void PrepareAliases()
    {
        foreach (var alias in this.aliases)
        {
            if (alias.CodeContext.SourceDocument is not { } document)
            {
                continue;
            }

            if (alias.Name is null)
            {
                if (!this.aliasesByDocument.TryGetValue(document, out var list))
                {
                    list = new();
                    this.aliasesByDocument.Add(document, list);
                }

                list.Add(alias);
            }

            if (this.AliasTarget(alias) is null || alias.Name is not { } name)
            {
                continue;
            }

            if (this.namedAliases.TryGetValue((document, name), out var previous))
            {
                if (!ReferenceEquals(previous.BoundSymbol, alias.BoundSymbol) || !ReferenceEquals(previous.BoundType, alias.BoundType) || previous.BindingState == BindingState.Invalid)
                {
                    this.FailDuplicate(previous, alias, true);
                }
            }
            else
            {
                this.namedAliases.Add((document, name), alias);
            }
        }

        // Reloaded documents must not retain obsolete alias syntax indefinitely.
        foreach (var entry in this.aliasesByDocument)
        {
            if (entry.Value.Count == 0)
            {
                this.aliasesByDocument.Remove(entry.Key);
            }
        }
    }

    private void CompleteAliasWarnings()
    {
        foreach (var alias in this.aliases)
        {
            if (alias.Name is not { } name || alias.BindingState != BindingState.Resolved)
            {
                continue;
            }

            var root = this.ModuleScope(alias);
            var found = false;
            var same = false;
            for (var candidate = root.Types.GetValueOrDefault(name); candidate is not null; candidate = candidate.Next)
            {
                if (!this.Accessible(candidate, root))
                {
                    continue;
                }

                found = true;
                same |= ReferenceEquals(candidate, alias.BoundSymbol);
            }

            if (!found && this.ModuleReference(alias, name) is { } reference)
            {
                found = true;
                same = ReferenceEquals(reference, alias.BoundSymbol);
            }

            if (found && !same)
            {
                this.aliasWarnings.Add(alias);
            }
        }
    }

    private BindingScope? AliasTarget(AliasKoto alias)
    {
        if (this.aliasTargets.TryGetValue(alias, out var cached))
        {
            return alias.BindingState == BindingState.Invalid ? null : cached;
        }

        this.aliasTargets.Add(alias, null);
        var scope = this.ModuleScope(alias);
        var declarationScope = scope;
        if (alias.TargetSyntax is { } targetSyntax)
        {
            this.aliasResolutionDepth++;
            try
            {
                var originDeclaration = this.BeginOriginDeclaration(alias, scope);
                var symbol = this.TypeName(targetSyntax, scope, false);
                if (symbol?.Declaration is not DeclarationContainerKoto || (alias.Name is not null && symbol.Declaration is not GroupKoto) ||
                    this.BindContainerQualifier(targetSyntax, symbol, scope, this.TypeContext(targetSyntax, scope)) is not { } reference)
                {
                    this.Fail(alias, BindingFailure.InvalidTypeFormation, true);
                    return null;
                }

                alias.BoundSymbol = symbol;
                Complete(alias, reference);
                this.CompleteOriginDeclaration(originDeclaration);
                reference = alias.BoundType!;
                if (reference.OriginArguments.Count != (symbol.Schema?.Origins.Count ?? 0) || reference.OriginArguments.Contains(null!))
                {
                    this.Fail(alias, BindingFailure.InvalidOrigin);
                    return null;
                }

                this.aliasTargets[alias] = this.scopes[symbol.Declaration];
                return this.aliasTargets[alias];
            }
            finally
            {
                this.aliasResolutionDepth--;
            }
        }

        for (var i = 0; i < alias.QualifiedName.Count; i++)
        {
            var name = alias.QualifiedName[i];
            var symbol = i == 0 && name == "Kimi" ? this.Library.Module : this.SelectTypeCandidate(scope.Types.GetValueOrDefault(name), declarationScope, alias, false) ?? (i == 0 ? this.ModuleReference(alias, name) : null);
            if (symbol is null || !this.Accessible(symbol, declarationScope) || !this.scopes.TryGetValue(symbol.Declaration, out var next))
            {
                this.Fail(alias, BindingFailure.MissingName, true);
                return null;
            }

            if (symbol.Declaration is DeclarationContainerKoto { GenericParameterNodes.Count: > 0 } or DeclarationContainerKoto { OriginNames.Count: > 0 })
            {
                this.Fail(alias, BindingFailure.InvalidTypeFormation, true);
                return null;
            }

            if (symbol.Declaration is DeclarationContainerKoto { HasIncompatibleBindingHeader: true })
            {
                this.Fail(alias, BindingFailure.InvalidTypeFormation, true);
                return null;
            }

            alias.BoundSymbol = symbol;
            scope = next;
        }

        if (alias.QualifiedName.Count == 0 || (alias.Name is not null && scope.Owner is not GroupKoto))
        {
            this.Fail(alias, BindingFailure.InvalidTypeFormation, true);
            return null;
        }

        alias.BindingState = BindingState.Resolved;
        this.aliasTargets[alias] = scope;
        return scope;
    }
}
