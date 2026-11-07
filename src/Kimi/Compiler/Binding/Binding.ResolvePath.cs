// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// ResolvePath: a written Type name resolved to its declaration by SPEC 9.5's stages, 9.6's member steps and `::` paths (DECISIONS R7-N1a).
// - Responsibility: the one evaluation of a Type name and its one publication. b, c, d, e and f bring the rest of name resolution here.
// - Inputs: the name's syntax, the use's scope, the role and the written Type-argument count.
// - Outputs: a ResolvedPath (how the path ended, the selected symbol, a stage stop's typed facts); its steps stand on nameSteps.
// - Invariants: evaluation writes nothing on the use's nodes or in a table keyed by them, and reads no state a publication writes. It may
//   force a declaration query (a base clause, an alias target), whose facts and records are that declaration's own. PublishPath alone
//   writes, once. One path stands above a mark, but for an adaptation target, whose roles are compared (ResolveAdaptationTarget).
// - Prohibited: reading an outcome from anywhere but the result; a second stage walk; a publish on the way.
public sealed partial class Binding
{
    private readonly List<NameStep> nameSteps = new();

    // The candidate filters of a Type name's lookup. These are not SPEC 9.2's roles: Any filters nothing and is the only row that reaches
    // stage 4, at qualifiers and at the other positions (base clauses, Constraint subjects, Contract names) alike; NoContainer drops
    // Containers; Type and Semantics are an adaptation target's two roles (SPEC 13.5.1). Type legality rejects the rest after selection.
    private enum TypeLookupRole : byte
    {
        Any,
        NoContainer,
        Type,
        Semantics,
    }

    private enum PathEnd : byte
    {
        Selected,
        Unresolved,
        Ambiguous,
        QualificationStop,
    }

    private enum NameStepKind : byte
    {
        Head,
        Member,
        InheritedMember,
        RootMember,
        RootOperand,
        AdaptationTarget,
        AmbiguousRole,
    }

    // One step of a path, with what its publication writes (PublishPath's table).
    private readonly record struct NameStep(NameStepKind Kind, Koto Use, BindingSymbol? Symbol, BindingSymbol? Qualifier = null, AliasImport Import = default);

    private readonly record struct ResolvedPath(PathEnd End, BindingSymbol? Symbol, Koto Use, QualificationFact Stop = default);

    private static bool HasTypeLookupRole(BindingSymbol candidate, TypeLookupRole role) => role switch
    {
        TypeLookupRole.Any => true,
        TypeLookupRole.NoContainer => candidate.Kind != BindingSymbolKind.Container,
        TypeLookupRole.Type => candidate.Declaration is not ContractKoto &&
            candidate.Kind is BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or
                BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType,
        _ => candidate.Kind == BindingSymbolKind.SemanticsParameter,
    };

    // SPEC 9.3: the first enclosing Container that is no group is the declaration a written Self names.
    private static BindingSymbol? EnclosingSelf(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is DeclarationContainerKoto and not GroupKoto)
            {
                return current.Owner.BoundSymbol;
            }
        }

        return null;
    }

    private static bool IsAdaptationTarget(Koto syntax)
    {
        var target = syntax;
        while (target.Parent is TypeSemanticsKoto { IsTransparentWrapper: true } wrapper)
        {
            target = wrapper;
        }

        return target.Parent is ConversionKoto conversion && ReferenceEquals(conversion.Right, target);
    }

    private BindingSymbol? TypeName(Koto syntax, BindingScope scope, TypeLookupRole role, int arity = 0)
    {
        var mark = this.nameSteps.Count;
        return this.PublishPath(this.ResolvePath(syntax, scope, role, arity), mark);
    }

    // SPEC 13.5.1: each role fixes its own first eligible lookup stage before the two roles are compared.
    // Operand or target fitting never resolves an ambiguity or reopens either search.
    private BindingSymbol? LookupAdaptationTarget(Koto syntax, BindingScope scope, out bool semantics)
    {
        var mark = this.nameSteps.Count;
        var path = this.ResolveAdaptationTarget(syntax, scope);
        semantics = path.Symbol?.Kind == BindingSymbolKind.SemanticsParameter;
        return this.PublishPath(path, mark);
    }

    private BindingSymbol? PublishPath(in ResolvedPath path, int mark)
    {
        var refused = false;
        for (var i = mark; i < this.nameSteps.Count; i++)
        {
            var step = this.nameSteps[i];
            switch (step.Kind)
            {
                case NameStepKind.Head:
                    this.importCandidates?.GetValueOrDefault(step.Use)?.Clear();
                    this.qualificationStops?.Remove((step.Use, true));
                    if (step.Import.Environment is not null)
                    {
                        this.importedContainers[step.Use] = step.Import;
                    }

                    break;
                case NameStepKind.Member:
                    var member = (MemberAccessKoto)step.Use;
                    PublishQualifier(member.Left, step.Qualifier!);
                    member.Right.BoundSymbol = step.Symbol;
                    member.Right.BindingState = BindingState.Resolved;
                    break;
                case NameStepKind.InheritedMember:
                    var inherited = (MemberAccessKoto)step.Use;
                    inherited.Left.BoundSymbol = step.Qualifier;
                    inherited.Right.BoundSymbol = step.Symbol;
                    inherited.Right.BindingState = BindingState.Resolved;
                    break;
                case NameStepKind.RootMember:
                    var rooted = (MemberAccessKoto)step.Use;
                    PublishQualifier(rooted.Left, step.Qualifier!);
                    rooted.Right.BoundSymbol = step.Symbol;
                    rooted.Right.BindingState = step.Symbol is null ? BindingState.Unresolved : BindingState.Resolved;
                    if (step.Symbol is null)
                    {
                        rooted.BoundSymbol = null;
                        rooted.BindingState = BindingState.Unresolved;
                    }
                    else
                    {
                        PublishQualifier(rooted, step.Symbol); // The outer level's qualifier in a longer `::` path (SPEC 9.7.1 step 1).
                    }

                    break;
                case NameStepKind.RootOperand:
                    step.Use.BoundSymbol = step.Symbol;
                    step.Use.BindingState = BindingState.Resolved;
                    break;
                case NameStepKind.AmbiguousRole:
                    this.Fail(step.Use, BindingFailure.Ambiguous, true);
                    break;
                default:
                    // A target that already failed selects nothing (SPEC 13.5.1: fitting never reopens the search).
                    refused = step.Use.BindingState == BindingState.Invalid;
                    if (!refused)
                    {
                        step.Use.BoundSymbol = step.Symbol;
                    }

                    break;
            }
        }

        this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
        switch (path.End)
        {
            case PathEnd.Selected:
                return refused ? null : path.Symbol;
            case PathEnd.Ambiguous:
                this.Fail(path.Use, BindingFailure.Ambiguous, true);
                return null;
            case PathEnd.QualificationStop:
                this.RecordQualificationStop(path.Use, true, path.Stop);
                return null;
            default:
                return null;
        }
    }

    // The evaluation alone, for a question that selects nothing (a repair's check, a namespace's verdict).
    private BindingSymbol? ProbePath(string name, BindingScope scope, Koto use, TypeLookupRole role)
    {
        var mark = this.nameSteps.Count;
        var path = this.ResolveName(name, scope, use, role, 0);
        this.nameSteps.RemoveRange(mark, this.nameSteps.Count - mark);
        return path.Symbol;
    }

    private ResolvedPath ResolvePath(Koto syntax, BindingScope scope, TypeLookupRole role, int arity = 0)
    {
        while (syntax is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            syntax = inner;
        }

        switch (syntax)
        {
            case GenericsKoto generic:
                return this.ResolvePath(generic.Identifier!, scope, role, generic.TypeArguments.Count);
            case ParenthesizedTypeKoto parentheses:
                return this.ResolvePath(parentheses.Type, scope, role, arity);
            case MemberAccessKoto member:
                return this.ResolveMember(member, this.ResolvePath(member.Left, scope, TypeLookupRole.Any), scope, role, arity);
            case SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root:
                var rooted = this.ResolveRootPath(root.Operands[0], this.ModuleScope(root.Operands[0]), role, arity);
                if (rooted.End == PathEnd.Selected)
                {
                    this.nameSteps.Add(new(NameStepKind.RootOperand, root.Operands[0], rooted.Symbol));
                }

                return rooted;
        }

        if (TypeSpelling(syntax) is not { } name)
        {
            return new(PathEnd.Unresolved, null, syntax);
        }

        if (IsAdaptationTarget(syntax))
        {
            return this.ResolveAdaptationTarget(syntax, scope);
        }

        return name == "Self" && EnclosingSelf(scope) is { } self ? new(PathEnd.Selected, self, syntax) : this.ResolveName(name, scope, syntax, role, arity);
    }

    // The one form with more than one lookup above a mark: both roles' steps replay, and a role's ambiguity stands beside the other's selection.
    private ResolvedPath ResolveAdaptationTarget(Koto syntax, BindingScope scope)
    {
        if (TypeSpelling(syntax) is not { } name)
        {
            return new(PathEnd.Unresolved, null, syntax);
        }

        if (name == "Self")
        {
            return EnclosingSelf(scope) is { } self ? new(PathEnd.Selected, self, syntax) : new(PathEnd.Unresolved, null, syntax);
        }

        var target = this.ResolveName(name, scope, syntax, TypeLookupRole.Type, 0);
        var parameter = this.ResolveName(name, scope, syntax, TypeLookupRole.Semantics, 0);
        if (target.End == PathEnd.Ambiguous || parameter.End == PathEnd.Ambiguous)
        {
            this.nameSteps.Add(new(NameStepKind.AmbiguousRole, syntax, null));
        }

        if (target.Symbol is not null && parameter.Symbol is not null)
        {
            return new(PathEnd.Ambiguous, null, syntax);
        }

        // Preserve the ordinary wrong-role or missing-name diagnostic when neither role applies.
        var selected = parameter;
        if (selected.Symbol is null)
        {
            selected = target.Symbol is not null ? target : this.ResolveName(name, scope, syntax, TypeLookupRole.NoContainer, 0);
        }

        this.nameSteps.Add(new(NameStepKind.AdaptationTarget, syntax, selected.Symbol));
        return selected;
    }

    // SPEC 9.5: stages 1-3 the scope chain with the inherited-layer stop, 4 a Type qualifier's reference names, 5-6 the imports (not
    // while an alias target resolves). Lookup stops at the first stage with an eligible declaration.
    private ResolvedPath ResolveName(string name, BindingScope scope, Koto use, TypeLookupRole role, int arity)
    {
        var step = this.nameSteps.Count;
        this.nameSteps.Add(new(NameStepKind.Head, use, null));
        for (var current = scope; current is not null; current = current.Parent)
        {
            var candidates = default(TypeCandidates);
            this.AddTypeCandidates(ref candidates, current.Types.GetValueOrDefault(name), scope, role, arity);
            if (candidates.First is not null)
            {
                return this.SelectedPath(candidates, use, step);
            }

            if (this.InheritedStop(current, scope, use, name, type: true, role, arity) is { } stop)
            {
                return new(PathEnd.QualificationStop, null, use, stop);
            }
        }

        if (role == TypeLookupRole.Any && this.ModuleReference(use, name) is { } reference)
        {
            return new(PathEnd.Selected, reference, use);
        }

        return this.aliasResolutionDepth != 0 ? new(PathEnd.Unresolved, null, use) : this.ResolveImportedType(name, scope, use, role, arity, step);
    }

    // An alias target's segment and a default alias's segment: one unfiltered selection with its ambiguity reported (part e folds both walks).
    private BindingSymbol? SelectQualifier(BindingSymbol? head, BindingScope scope, Koto use)
    {
        var candidates = default(TypeCandidates);
        this.AddTypeCandidates(ref candidates, head, scope, TypeLookupRole.Any, 0);
        if (candidates.Ambiguous)
        {
            this.Fail(use, BindingFailure.Ambiguous, true);
            return null;
        }

        return candidates.Match ?? candidates.First;
    }

    private ResolvedPath SelectedPath(in TypeCandidates candidates, Koto use, int step)
    {
        if (candidates.Ambiguous)
        {
            return new(PathEnd.Ambiguous, null, use);
        }

        // Retain a mismatching declaration for the existing formation/arity diagnostic.
        // A populated nearer stage must never fall through to an outer or default stage.
        var symbol = candidates.Match ?? candidates.First;
        this.nameSteps[step] = this.nameSteps[step] with { Import = candidates.Import };
        return new(PathEnd.Selected, symbol, use);
    }

    // SPEC 9.1, 9.6: the later components search the selected target's members; a qualified Type name never selects a generic parameter;
    // the declaration's own Type selects an inherited member, so a qualifier's arguments and Origins never change the selection.
    private ResolvedPath ResolveMember(MemberAccessKoto member, in ResolvedPath qualifier, BindingScope scope, TypeLookupRole role, int arity)
    {
        if (qualifier.End != PathEnd.Selected)
        {
            return qualifier;
        }

        var left = qualifier.Symbol!;
        if (TypeSpelling(member.Right) is not { } name)
        {
            return new(PathEnd.Unresolved, null, member);
        }

        var candidates = default(TypeCandidates);
        if (this.scopes.TryGetValue(left.Declaration, out var members))
        {
            this.AddTypeCandidates(ref candidates, QualifiedTypeMember(members.Types.GetValueOrDefault(name)), scope, role, arity);
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, member);
            }

            if ((candidates.Match ?? candidates.First) is { } target)
            {
                this.nameSteps.Add(new(NameStepKind.Member, member, target, left));
                return new(PathEnd.Selected, target, member);
            }
        }

        if (left.Declaration is StructKoto && this.LookupTypeMember(this.SelfType(left), name, scope, typeRole: true) is { Member: { } inherited })
        {
            this.nameSteps.Add(new(NameStepKind.InheritedMember, member, inherited, left));
            candidates = default;
            this.AddTypeCandidates(ref candidates, inherited, scope, role, arity);
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, member);
            }

            var selected = candidates.Match ?? candidates.First;
            return new(selected is null ? PathEnd.Unresolved : PathEnd.Selected, selected, member);
        }

        return new(PathEnd.Unresolved, null, member);
    }

    // SPEC 9.6: `::A.B` starts at the Compilation root of the use's Kotonoha, ignoring locals and aliases.
    private ResolvedPath ResolveRootPath(Koto syntax, BindingScope root, TypeLookupRole role, int arity = 0)
    {
        syntax = UnwrapTypeSyntax(syntax);
        if (syntax is GenericsKoto generic)
        {
            return this.ResolveRootPath(generic.Identifier!, root, role, generic.TypeArguments.Count);
        }

        if (TypeSpelling(syntax) == "Kimi")
        {
            return new(PathEnd.Selected, this.Library.Module, syntax);
        }

        var candidates = default(TypeCandidates);
        if (syntax is MemberAccessKoto member && this.ResolveRootPath(member.Left, root, TypeLookupRole.Any) is { Symbol: { } qualifier } &&
            this.scopes.TryGetValue(qualifier.Declaration, out var members) && TypeSpelling(member.Right) is { } rightName)
        {
            this.AddTypeCandidates(ref candidates, members.Types.GetValueOrDefault(rightName), root, role, arity);
            var selected = candidates.Ambiguous ? null : candidates.Match ?? candidates.First;
            this.nameSteps.Add(new(NameStepKind.RootMember, member, selected, qualifier));
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, member);
            }

            return new(selected is null ? PathEnd.Unresolved : PathEnd.Selected, selected, member);
        }

        if (TypeSpelling(syntax) is { } name)
        {
            this.AddTypeCandidates(ref candidates, root.Types.GetValueOrDefault(name), root, role, arity);
            if (candidates.Ambiguous)
            {
                return new(PathEnd.Ambiguous, null, syntax);
            }

            var target = candidates.Match ?? candidates.First ?? this.ModuleReference(syntax, name);
            var eligible = target is not null && HasTypeLookupRole(target, role) && this.Accessible(target, root);
            return new(eligible ? PathEnd.Selected : PathEnd.Unresolved, eligible ? target : null, syntax);
        }

        return new(PathEnd.Unresolved, null, syntax);
    }
}
