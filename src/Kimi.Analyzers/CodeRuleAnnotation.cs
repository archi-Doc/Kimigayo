// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns source-authored annotations and their validation, carried by the ordinary observation pipeline.
// Inputs: the already resolved attribute constructor and constant arguments. Output: a sparse bound/stage/reason fact.
// Attribute syntax is decoded once; member identity and reconciliation remain with Source and Baseline.
// Must not resolve aliases independently, suppress ordinary checks or maintain a separate inventory.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kimi.Analyzers;

internal sealed class CodeRuleAnnotation(CodeRule rule, int limit, string reason, Location location, int stage = -1)
{
    internal static readonly CodeRule Transitional = CodeRule.Find("KR1007")!;

    internal CodeRule Rule { get; } = rule;

    internal int Limit { get; } = limit;

    internal string Reason { get; } = reason;

    internal Location Location { get; } = location;

    internal int Stage { get; } = stage;

    internal static CodeRuleAnnotation? Read(CodeRulesSource source, AttributeSyntax attribute, IMethodSymbol? constructor, CodeRulesConfiguration config, Action<Diagnostic> report, CancellationToken cancellation)
    {
        if (constructor?.ContainingType is not { Name: "CodeRuleExceptionAttribute" or "TransitionalAttribute" } type || type.ContainingNamespace.ToDisplayString() != "Kimi")
        {
            return null;
        }

        string? id = null;
        string? reason = null;
        var limit = 0;
        if (attribute.ArgumentList is { } arguments)
        {
            foreach (var argument in arguments.Arguments)
            {
                var value = source.Semantics.GetConstantValue(argument.Expression, cancellation);
                if (!value.HasValue)
                {
                    continue;
                }

                var name = argument.NameEquals?.Name.Identifier.ValueText;
                if (name == "Reason")
                {
                    reason = value.Value as string;
                }
                else if (name == "Limit")
                {
                    limit = value.Value is int number ? number : 0;
                }
                else if (name is null)
                {
                    id = value.Value as string;
                }
            }
        }

        var transitional = type.Name == "TransitionalAttribute";
        var stage = transitional ? Array.IndexOf(CodeRulesConfiguration.Stages, id) : -1;
        var rule = transitional ? Transitional : id is "KR2006" or "KR2007" ? CodeRule.Find(id) : null;
        var list = (AttributeListSyntax)attribute.Parent!;
        var owner = list.Parent;
        var target = list.Target?.Identifier.ValueText;
        var validTarget = owner switch
        {
            BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax => target is null or "method",
            PropertyDeclarationSyntax or IndexerDeclarationSyntax => target is null or "property",
            EventDeclarationSyntax or EventFieldDeclarationSyntax => target is null or "event",
            FieldDeclarationSyntax => target is null or "field",
            EnumMemberDeclarationSyntax => transitional && (target is null or "field"),
            BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => transitional && (target is null or "type"),
            _ => false,
        };
        if (!validTarget || rule is null || !config.Applies(rule, source.Path) || string.IsNullOrWhiteSpace(reason) ||
            (transitional ? stage < 0 : limit <= config.Limit(rule)))
        {
            report(Diagnostic.Create(CodeRule.InvalidAnnotation, attribute.GetLocation(), "Invalid annotation target, rule, reason, limit or removal stage"));
            return null;
        }

        return new(rule, limit, reason!, attribute.GetLocation(), stage);
    }

    internal void RecordTransition(CodeRulesSource source, AttributeSyntax attribute, int currentStage)
    {
        var owner = attribute.Parent!.Parent!;
        if (owner is BaseFieldDeclarationSyntax field)
        {
            foreach (var variable in field.Declaration.Variables)
            {
                source.Record(this.Rule, variable, this.Stage, currentStage, source.MemberAnchor(variable), this);
            }
        }
        else
        {
            source.Record(this.Rule, owner, this.Stage, currentStage, source.MemberAnchor(owner), this);
        }
    }
}
