// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Selects imported declarations after ordinary scope lookup has exhausted its eligible stages.
// Inputs are a name, use and lookup role; outputs are a declaration and its imported environment or an ambiguity failure.
// Source aliases precede defaults even on an arity mismatch; Type arities and value overload sets keep their distinct contracts.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
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

    // SPEC 18.2.3, 18.2.1, 23.1.2 (F's rulings N1-4, N1-7 (5), N1-13 B2): a configured default alias is the opening alias its configuration
    // declares (Compilation.ParseConfiguredPath), an alias of its module: indexed with its module's trees (IndexConfiguredPaths) and
    // evaluated where every alias is (PrepareAliases, AliasTarget), so its records, its rest and its import (its environment included)
    // are a source alias's; the module root is not failed. A use's miss rests on a failed one (ImportMiss). A required path's row is the
    // library's (Compilation.DefaultAliases; the owner's O-511 (a)), evaluated in the library's pass, so AliasTarget reads its frozen target.
    private (BindingScope? Target, AliasImport Import) DefaultAliasTarget(Compilation.DefaultAlias row) => this.EvaluateAlias(row.Alias);

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

        this.nameSteps.Add(new(NameStepKind.ImportGroup, use, candidate, imported, default, default, default, null, null, null));
        return true;
    }

    // An ImportGroup step's publication: the use's gathered group, which its readers reach through GroupOf (Binding.FunctionGroups.cs).
    private void AddImportCandidates(Koto use, BindingSymbol imported, BindingSymbol candidate)
    {
        var candidates = GatheredGroupRows.Of(use);
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

        Koto? failed = null;
        for (var i = 0; documentAliases is not null && i < documentAliases.Count; i++)
        {
            var alias = documentAliases[i];
            var target = this.AliasTarget(alias.Declaration!);
            failed ??= target is null ? alias.Declaration : null;
            if (target is not null)
            {
                this.AddTypeCandidates(ref candidates, MemberTypes(target, name), scope, role, arity, alias);
            }
        }

        if (candidates.First is not null)
        {
            return this.SelectedPath(candidates, use, step);
        }

        foreach (var row in this.compilation.DefaultAliases(use.CodeContext.Kotonoha))
        {
            var (target, import) = this.DefaultAliasTarget(row);
            failed ??= target is null ? import.Declaration : null;
            this.AddTypeCandidates(ref candidates, target is null ? null : MemberTypes(target, name), scope, role, arity, import);
        }

        return candidates.First is not null ? this.SelectedPath(candidates, use, step) : this.ImportMiss(use, failed);
    }

    // SPEC 9.5 stages 5 and 6 in the Value namespace: one stage's sources together (9.5.1). A use holds one imported environment, so two
    // differing ones are an implementation limit (ImportStageEnd). A default alias's segment may fail here (part e).
    private ResolvedPath ResolveImportedValue(string name, BindingScope scope, Koto use, int step)
    {
        var stage = default(ValueImports);
        var documentAliases = use.CodeContext.SourceDocument is { } document ? this.aliasesByDocument.GetValueOrDefault(document) : null;
        Koto? failed = null;
        for (var i = 0; !stage.Ends && documentAliases is not null && i < documentAliases.Count; i++)
        {
            var alias = documentAliases[i];
            var target = this.AliasTarget(alias.Declaration!);
            failed ??= target is null ? alias.Declaration : null;
            this.AddImport(ref stage, target, alias, name, scope, use);
        }

        // Stage 6, the configured default aliases, when stage 5 imported nothing (SPEC 9.5).
        foreach (var row in stage.Imported is null ? this.compilation.DefaultAliases(use.CodeContext.Kotonoha) : [])
        {
            if (stage.Ends)
            {
                break;
            }

            var (target, import) = this.DefaultAliasTarget(row);
            failed ??= target is null ? import.Declaration : null;
            this.AddImport(ref stage, target, import, name, scope, use);
        }

        // The sources merged before an ambiguity keep their environment as the use's import, a configured source's too (N1-13 B2).
        if (stage.Import.Environment is not null)
        {
            this.nameSteps[step] = this.nameSteps[step] with { Import = stage.Import };
        }

        return this.ImportStageEnd(stage, name, scope, use, failed);
    }

    // SPEC 23.1.2 (rule W; F's rulings N1-2 C2, N1-9 (1)): a use whose import stages selected nothing read every opening alias of its
    // document and every configured default alias, a failed one included, so its miss rests on that alias's record (a configured one's
    // is at its path's failing component) and records nothing of its own.
    private ResolvedPath ImportMiss(Koto use, Koto? failed)
    {
        if (failed is not null)
        {
            this.AddPrerequisite(use, failed);
        }

        return new(PathEnd.Unresolved, null, use);
    }

    // The walk's refusal at the Name (as Supported refuses a component): its Unsupported step, which PublishPath publishes as its failure.
    private ResolvedPath ImportLimit(Koto use)
    {
        this.nameSteps.Add(new(NameStepKind.Unsupported, use, null, null, default, default, default, null, null, null));
        return new(PathEnd.Unresolved, null, use);
    }

    // One opened source's same-name declarations join its stage (SPEC 9.5, 9.5.1): every accessible one takes part in the stage's
    // conflict check, Cases and members needing a receiver too (9.2: the Value role admits all value declarations).
    private void AddImport(ref ValueImports stage, BindingScope? target, in AliasImport alias, string name, BindingScope scope, Koto use)
    {
        if (target is null || !target.Values.TryGetValue(name, out var candidate) || this.AccessibleImport(candidate, scope) is not { } accessible)
        {
            return;
        }

        var imported = stage.Imported;
        var ambiguous = !this.MergeImport(use, accessible, ref imported);
        var limited = !ambiguous && alias.Environment is { } environment && stage.Import.Environment is { } previous && !ReferenceEquals(previous, environment);
        stage = stage with
        {
            Imported = imported,
            Source = stage.Source ?? target,
            Selectable = stage.Selectable || this.HasUnqualifiedCandidate(candidate, scope),
            Ambiguous = ambiguous,
            Limited = stage.Limited || limited,
        };

        if (!ambiguous && !limited && alias.Environment is not null)
        {
            stage = stage with { Import = stage.Import.Environment is null ? alias : stage.Import.MergeWitness(alias) };
        }
    }

    // SPEC 9.5, 9.5.1, 18.2.1 (PLAN G632): an import stage's end, once its same-name declarations are merged. The conflict first (a
    // function and another value kind, or two other values: the ordinary same-name ambiguity); then the implementation limit (PLAN
    // G633: one group gathered from two environments is valid, SPEC 9.5.1 and 7.4, but a use holds one imported environment), which
    // refuses the Name with the walk's Unsupported step (the located Unsupported_Kd); then 9.5's stop, the stage having only Cases and
    // members needing a receiver (none Selectable); else the merged selection. A miss rests on a failed alias (ImportMiss).
    private ResolvedPath ImportStageEnd(in ValueImports stage, string name, BindingScope scope, Koto use, Koto? failed)
        => stage.Ambiguous ? new(PathEnd.Ambiguous, null, use)
            : stage.Limited ? this.ImportLimit(use)
            : stage.Imported is not { } imported ? this.ImportMiss(use, failed)
            : stage.Selectable ? new(PathEnd.Selected, imported, use)
            : new(PathEnd.QualificationStop, null, use, new(stage.Source!, scope, imported, name, LookupRole.Value, 0));

    // One import stage's same-name value declarations merged so far: the selection (a gathered group's first source), the merged
    // environment, the first source, whether any candidate may be selected unqualified (HasUnqualifiedCandidate), a conflict and the
    // two-environment limit, met at any source so far (F's ruling N1-13 B4: only the conflict ends the stage, so the sources' order never
    // decides which of the two the use reports; ImportStageEnd reads the conflict first).
    private readonly record struct ValueImports(BindingSymbol? Imported, AliasImport Import, BindingScope? Source, bool Selectable, bool Ambiguous, bool Limited)
    {
        internal bool Ends => this.Ambiguous;
    }

    /// <summary>The `gatheredGroups` family's one accessor (DECISIONS BU-220): a kept family, a use's function group gathered at the alias
    /// stage (its ImportGroup steps' publication), a list in the use's module's NodeColumns at its SyntaxId, made at the use's first
    /// gathering, emptied in place at each publication of its head (PublishPath) and refilled, so a warm Bind reuses it.</summary>
    private readonly struct GatheredGroupRows
    {
        // The use's list, made at its first gathering.
        internal static List<BindingSymbol> Of(Koto use) => HirTables.Of(use).Families.GatheredGroups.Ref(use.SyntaxId) ??= new();

        internal static List<BindingSymbol>? GetValueOrDefault(Koto use) => use.SyntaxOwner is { } table ? table.Hir.Families.GatheredGroups.Get(use.SyntaxId) : null;
    }
}
