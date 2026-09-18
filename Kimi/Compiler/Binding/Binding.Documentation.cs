// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    /// <summary>Gets selected documentation using existing Binding identity and access facts, without rebinding.</summary>
    /// <param name="declaration">The declaration to display.</param>
    /// <param name="publicOnly">Whether to require effective external accessibility.</param>
    /// <param name="implementationNote">Whether a specialization should show its own implementation note.</param>
    /// <returns>Independent source-backed fragments in logical declaration order.</returns>
    public IReadOnlyList<DocumentationComment> GetDocumentation(Koto declaration, bool publicOnly = false, bool implementationNote = false)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (!ReferenceEquals(declaration.CodeContext.Compilation, this.compilation))
        {
            throw new ArgumentException("The declaration belongs to another compilation.", nameof(declaration));
        }

        var target = declaration;
        BindingSymbol? original = null;
        if (declaration is FunctionKoto { IsSpecialization: true } function && this.specializations.TryGetValue(function, out var specialization))
        {
            original = specialization.Original;
            if (!implementationNote)
            {
                target = original.Declaration;
            }
        }

        var visibility = original ?? target.BoundSymbol;
        if (target is FunctionKoto { IsRequirement: true } or PropertyKoto { DeclarationKind: PropertyDeclarationKind.Requirement })
        {
            visibility = visibility?.Scope.Owner.BoundSymbol;
        }

        if (publicOnly && (this.Result.Mode != BindingMode.Final || visibility is null || !ExternallyVisible(visibility)))
        {
            return Array.Empty<DocumentationComment>();
        }

        if (publicOnly && target is IsKoto { IsAssociatedConstraint: true } clause)
        {
            var scope = this.ConstraintScope(clause);
            var owner = scope.ConformancePath?.Type ?? scope.Owner.BoundSymbol;
            if (owner is null || !ExternallyVisible(owner))
            {
                return Array.Empty<DocumentationComment>();
            }
        }

        var results = new List<(DocumentationSource Source, DocumentationComment Comment)>();
        foreach (var source in target.Kotonoha.DocumentationSources)
        {
            foreach (var comment in source.Comments)
            {
                if (comment.IsSelected && comment.Declaration is { } candidate &&
                    (ReferenceEquals(candidate, target) || (target is GroupKoto or StructKoto && candidate is DeclarationContainerKoto && target.BoundSymbol is { } bound && ReferenceEquals(candidate.BoundSymbol, bound))))
                {
                    results.Add((source, comment));
                }
            }
        }

        results.Sort(static (a, b) =>
        {
            var order = (a.Source.ModId is not null).CompareTo(b.Source.ModId is not null);
            if (order == 0)
            {
                order = string.CompareOrdinal(a.Source.ModId ?? a.Source.LogicalName, b.Source.ModId ?? b.Source.LogicalName);
            }

            if (order == 0)
            {
                order = a.Source.AdditionOrder.CompareTo(b.Source.AdditionOrder);
            }

            return order != 0 ? order : a.Comment.DeclarationSpan.Start.CompareTo(b.Comment.DeclarationSpan.Start);
        });
        return results.ConvertAll(static x => x.Comment).AsReadOnly();
    }
}
