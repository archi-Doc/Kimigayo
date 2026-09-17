// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<SourceDocument, List<AliasKoto>> aliasesByDocument = new();
    private readonly Dictionary<AliasKoto, BindingScope?> aliasTargets = new();
    private readonly Dictionary<(SourceDocument Document, string Name), AliasKoto> namedAliases = new();
    private readonly List<AliasKoto> aliasWarnings = new();
    private int reportedAliasWarnings;

    private void ResetAliases()
    {
        foreach (var list in this.aliasesByDocument.Values)
        {
            list.Clear();
        }

        this.aliasTargets.Clear();
        this.defaultAliasTargets.Clear();
        this.namedAliases.Clear();
        this.aliasWarnings.Clear();
        this.reportedAliasWarnings = 0;
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
                if (!ReferenceEquals(previous.BoundSymbol, alias.BoundSymbol) || previous.BindingState == BindingState.Invalid)
                {
                    Fail(previous, BindingFailure.Duplicate, true);
                    Fail(alias, BindingFailure.Duplicate, true);
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
}
