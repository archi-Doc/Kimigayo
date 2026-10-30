// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Resolves source aliases to their container scopes and environments, and rebuilds that state for each Binding pass.
// Inputs are source-local alias declarations; outputs are the selected containers and named/open alias lookup entries.
// Alias targets never resolve through another alias. Ordinary lookup owns which imported candidate a use selects.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<SourceDocument, List<AliasImport>> aliasesByDocument = new();
    private readonly Dictionary<AliasKoto, BindingScope?> aliasTargets = new();
    private readonly Dictionary<(SourceDocument Document, string Name), AliasImport> namedAliases = new();
    private readonly List<AliasKoto> aliasWarnings = new();
    private readonly Dictionary<Koto, AliasImport> importedContainers = new(ReferenceEqualityComparer.Instance);
    private int aliasResolutionDepth;
    private ImportedOriginVisitor? importedOriginVisitor;

    // A pass-local selected import: AliasTarget completed before construction, so Declaration.BoundType is its stable environment.
    // Same-candidate/environment merges retain the first actual witness; replacement candidates replace provenance as well.
    // ResetAliases clears every consumer before the next pass or source invalidation. Normalized Type identity carries no syntax.
    private readonly record struct AliasImport(AliasKoto? Declaration, Koto? WrittenOrigin)
    {
        internal BoundType? Environment => this.Declaration?.BoundType;

        // The caller has kept the same selection and environment; only the first actual witness may be added.
        internal AliasImport MergeWitness(AliasImport other) => this with { WrittenOrigin = this.WrittenOrigin ?? other.WrittenOrigin };
    }

    private void ResetAliases()
    {
        foreach (var list in this.aliasesByDocument.Values)
        {
            list.Clear();
        }

        this.aliasTargets.Clear();
        this.importedContainers.Clear();
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

            var target = this.AliasTarget(alias);
            var import = new AliasImport(alias, target is not null && alias.TargetSyntax is { } syntax ? Parser.WrittenOrigin(syntax) : null);
            if (alias.Name is null)
            {
                if (!this.aliasesByDocument.TryGetValue(document, out var list))
                {
                    list = new();
                    this.aliasesByDocument.Add(document, list);
                }

                list.Add(import);
            }

            if (target is null || alias.Name is not { } name)
            {
                continue;
            }

            if (this.namedAliases.TryGetValue((document, name), out var previous))
            {
                if (!ReferenceEquals(previous.Declaration!.BoundSymbol, alias.BoundSymbol) ||
                    !ReferenceEquals(previous.Environment, alias.BoundType) || previous.Declaration!.BindingState == BindingState.Invalid)
                {
                    this.FailDuplicate(previous.Declaration!, alias, true);
                }
                else
                {
                    this.namedAliases[(document, name)] = previous.MergeWitness(import);
                }
            }
            else
            {
                this.namedAliases.Add((document, name), import);
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
                    RestPendingOn(originDeclaration, alias);
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

    // Read only provenance that ordinary lookup actually selected in this target; no alias lookup or Type reconstruction occurs.
    private Koto? ImportedOrigin(Koto syntax)
    {
        var visitor = this.importedOriginVisitor ??= new(this);
        try
        {
            visitor.Visit(syntax);
            return visitor.Found;
        }
        finally
        {
            visitor.Found = null;
        }
    }

    private sealed class ImportedOriginVisitor(Binding binding) : KotoVisitor
    {
        internal Koto? Found { get; set; }

        public override void Visit(Koto node)
        {
            if (this.Found is not null)
            {
                return;
            }

            if (binding.importedContainers.TryGetValue(node, out var import) && import.WrittenOrigin is { } written)
            {
                this.Found = written;
            }
            else
            {
                node.VisitChildren(this);
            }
        }
    }
}
