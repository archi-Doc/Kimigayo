// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Only failed declarations need this storage. Each collision group retains its first declaration and its earliest
    // later declaration, independently of the order in which pairwise checks find collisions.
    private Dictionary<Koto, (Koto Parent, Koto? Next)>? duplicateDeclarations;

    private static int CompareDeclarations(Koto left, Koto right)
    {
        var requirement = DiagnosticRequirement.Binding(BindingFailure.Duplicate);
        var x = left.KeyOf(requirement);
        var y = right.KeyOf(requirement);
        var order = (x.Source < 0 ? int.MaxValue : x.Source).CompareTo(y.Source < 0 ? int.MaxValue : y.Source);
        order = order != 0 ? order : x.Start.CompareTo(y.Start);
        return order != 0 ? order : x.Length.CompareTo(y.Length);
    }

    private static SourceSpan DuplicateSpan(Koto node) => node switch
    {
        VariableKoto variable => variable.NameKoto.Span,
        FunctionKoto function when function.SignatureSpan.Length != 0 => function.SignatureSpan,
        _ => node.Span,
    };

    private void FailDuplicate(Koto first, Koto second, bool unresolved = false)
    {
        this.Fail(first, BindingFailure.Duplicate, unresolved);
        this.Fail(second, BindingFailure.Duplicate, unresolved);
        if (ReferenceEquals(first, second))
        {
            return;
        }

        var groups = this.duplicateDeclarations ??= new(ReferenceEqualityComparer.Instance);
        groups.TryAdd(first, (first, null));
        groups.TryAdd(second, (second, null));
        first = this.FirstDuplicateDeclaration(first);
        second = this.FirstDuplicateDeclaration(second);
        if (ReferenceEquals(first, second))
        {
            return;
        }

        if (CompareDeclarations(first, second) > 0)
        {
            (first, second) = (second, first);
        }

        var next = groups[first].Next;
        groups[first] = (first, next is not null && CompareDeclarations(next, second) < 0 ? next : second);
        groups[second] = (first, null);
    }

    private Koto FirstDuplicateDeclaration(Koto node)
    {
        while (this.duplicateDeclarations![node].Parent is { } parent && !ReferenceEquals(parent, node))
        {
            node = parent;
        }

        return node;
    }

    private void ReportDuplicateDeclarations()
    {
        if (this.duplicateDeclarations is not { } groups)
        {
            return;
        }

        var requirement = DiagnosticRequirement.Binding(BindingFailure.Duplicate);
        foreach (var (node, group) in groups)
        {
            var first = this.FirstDuplicateDeclaration(node);
            if (ReferenceEquals(node, first))
            {
                // Analysis still invalidates both declarations. Consumers of the first declaration depend on the
                // actual duplicate check, whose later subject publishes the direct Error; this is not positional suppression.
                if (group.Next is { } later)
                {
                    node.ReportDerived(requirement, [later.KeyOf(requirement)]);
                }

                continue;
            }

            var name = node switch
            {
                AliasKoto alias => alias.Name!,
                IsKoto conformance => conformance.Right.ToString(),
                _ => node.BoundSymbol?.Name ?? (node as FunctionKoto)?.Name ?? "declaration",
            };
            if (node.DiagnosticCollection is { } target)
            {
                var related = target.Relate("declaration", DuplicateSpan(first), first.CodeContext.SourceDocument, "first declaration");
                target.Report(requirement.Partition, node.KeyOf(requirement), DuplicateSpan(node), DiagnosticCode.DuplicateBinding_Kd, null, null, null, null, node.CodeContext.SourceDocument, [name], [related]);
            }
        }
    }
}
