// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Resolves source aliases to their container scopes and environments, and rebuilds that state for each Binding pass.
// Inputs are source-local alias declarations; outputs are the selected containers and named/open alias lookup entries.
// Alias targets never resolve through another alias. Ordinary lookup owns which imported candidate a use selects.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<AliasKoto> aliases = new();
    private readonly AliasTargetRows aliasTargets;
    private readonly Dictionary<(SourceDocument Document, string Name), AliasImport> namedAliases = new();
    private readonly List<AliasKoto> aliasWarnings = new();
    private readonly ImportedContainerRows importedContainers;
    private Dictionary<SourceDocument, List<AliasImport>> aliasesByDocument; // The running pass's part's (PassPart.AliasesByDocument).
    private int aliasResolutionDepth;
    private ImportedOriginVisitor? importedOriginVisitor;

    // A pass-local selected import: AliasTarget completed before construction, so Declaration.BoundType is stable. Its environment (SPEC
    // 9.7.1, 18.2.1; F's ruling N1-5 (2)) is that Type when it binds Type or Origin arguments, its outer chain's included; a target binding
    // none carries no environment, so two opened targets binding nothing never differ by environment, and no use records one.
    // Same-candidate/environment merges retain the first actual witness; replacement candidates replace provenance as well.
    // ResetAliases clears every consumer before the next pass or source invalidation. Normalized Type identity carries no syntax.
    internal readonly record struct AliasImport(AliasKoto? Declaration, Koto? WrittenOrigin)
    {
        internal BoundType? Environment => this.Declaration?.BoundType is { } target && (target.Components.Count > 0 || target.OriginArguments.Count > 0) ? target : null;

        // The caller has kept the same selection and environment; only the first actual witness may be added.
        internal AliasImport MergeWitness(AliasImport other) => this with { WrittenOrigin = this.WrittenOrigin ?? other.WrittenOrigin };
    }

    private void ResetAliases()
    {
        foreach (var list in this.aliasesByDocument.Values)
        {
            list.Clear();
        }

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
            var import = new AliasImport(alias, target is not null ? Parser.WrittenOrigin(alias.TargetSyntax) : null);
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
                    !ReferenceEquals(previous.Environment, import.Environment) || previous.Declaration!.BindingState == BindingState.Invalid)
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

    // SPEC 18.2.1: a target resolves from the Compilation root by the path walk (TypeName from the module scope, stages 5-6 off while
    // aliasResolutionDepth is set), plain or bound alike; its formation is the bound Container's (BindContainerQualifier, SPEC 9.7).
    private BindingScope? AliasTarget(AliasKoto alias)
    {
        if (this.aliasTargets.TryGetValue(alias, out var cached))
        {
            return alias.BindingState == BindingState.Invalid ? null : cached;
        }

        this.aliasTargets.Add(alias, null);
        var scope = this.ModuleScope(alias);
        var targetSyntax = alias.TargetSyntax;
        this.aliasResolutionDepth++;
        try
        {
            var originDeclaration = this.BeginOriginDeclaration(alias, scope);
            var path = this.NamePath(targetSyntax, scope);
            var symbol = path.Symbol;

            // SPEC 23.1.2, 18.2.1 (F's ruling N1-7 (2)): a target the walk selects nothing for is bound as every Type use is, which records
            // the walk's failure where it fails (FailingStep: the missing or refused component), and the alias rests on that record (rule W);
            // a built-in Type forms, and is no Container.
            if (symbol is null && this.BindType(targetSyntax, scope, this.TypeContext(targetSyntax, scope)) is null)
            {
                this.CompleteDependent(alias, FailingStep(path, targetSyntax));
                RestPendingOn(originDeclaration, alias);
                return null;
            }

            if (symbol?.Declaration is not DeclarationContainerKoto || (alias.Name is not null && symbol.Declaration is not GroupKoto))
            {
                this.Record(alias, BindingFailure.InvalidTypeFormation, [], unresolved: true);
                RestPendingOn(originDeclaration, alias);
                return null;
            }

            if (this.BindContainerQualifier(targetSyntax, symbol, scope, this.TypeContext(targetSyntax, scope)) is not { } reference)
            {
                // SPEC 23.1.2 (rule W; F's ruling N1-5 (4)): the target's formation failed with its own record, which the alias reads directly.
                this.CompleteDependent(alias, targetSyntax);
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

    /// <summary>The `aliasTargets` family's one accessor (DECISIONS BU-220): a pass family, an alias's container scope, kept in the alias's
    /// module's NodeColumns at its SyntaxId; a present row is Started, its Scope null while the alias resolves or once it failed. Its
    /// members keep the names its readers use.</summary>
    private readonly struct AliasTargetRows(Binding binding)
    {
        internal BindingScope? this[AliasKoto alias]
        {
            get => this.TryGetValue(alias, out var scope) ? scope : throw new KeyNotFoundException($"`{alias}` has no target in this pass.");
            set => HirTables.Of(alias).Families.AliasTargets.Ref(alias.SyntaxId, binding) = (true, value);
        }

        internal void Add(AliasKoto alias, BindingScope? scope)
        {
            ref var row = ref HirTables.Of(alias).Families.AliasTargets.Ref(alias.SyntaxId, binding);
            row = row.Started ? throw new ArgumentException($"`{alias}` already has a target in this pass.", nameof(alias)) : (true, scope);
        }

        internal bool TryGetValue(AliasKoto alias, out BindingScope? scope)
        {
            var row = alias.SyntaxOwner is { } table ? table.Hir.Families.AliasTargets.Get(alias.SyntaxId, binding) : default;
            scope = row.Scope;
            return row.Started;
        }
    }

    /// <summary>The `importedContainers` family's one accessor (DECISIONS BU-220): a pass family, the alias import a qualifier's head
    /// selected (PublishPath), kept in the use's module's NodeColumns at its SyntaxId (a present row always has its Declaration, so the
    /// column's default is none). Its members keep the names its readers use.</summary>
    private readonly struct ImportedContainerRows(Binding binding)
    {
        internal AliasImport this[Koto use]
        {
            set => HirTables.Of(use).Families.ImportedContainers.Ref(use.SyntaxId, binding) = value;
        }

        internal bool TryGetValue(Koto node, out AliasImport import)
            => (import = node.SyntaxOwner is { } table ? table.Hir.Families.ImportedContainers.Get(node.SyntaxId, binding) : default).Declaration is not null;
    }
}
