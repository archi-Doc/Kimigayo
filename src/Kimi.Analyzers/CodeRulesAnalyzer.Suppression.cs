// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kimi.Analyzers;

public sealed partial class CodeRulesAnalyzer
{
    private static bool ProtectedId(string id)
    {
        var colon = id.IndexOf(':');
        if (colon >= 0)
        {
            id = id.Substring(0, colon);
        }

        return id is "IDE0051" or "IDE0052" or "IDE0060" ||
            (id.Length == 6 && id.StartsWith("KR", StringComparison.Ordinal) && id.Skip(2).All(x => x is >= '0' and <= '9'));
    }

    private static void CheckSuppression(PragmaWarningDirectiveTriviaSyntax pragma, Action<Diagnostic> report)
    {
        if (!pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword) || !pragma.IsActive)
        {
            return;
        }

        if (pragma.ErrorCodes.Count == 0 || pragma.ErrorCodes.Any(x => ProtectedId(x.ToString())))
        {
            report(Diagnostic.Create(CodeRule.Suppression, pragma.GetLocation(), "A pragma suppresses protected diagnostics"));
        }
    }

    private static void CheckSuppression(CodeRulesSource source, AttributeSyntax attribute, IMethodSymbol? constructor, Action<Diagnostic> report, CancellationToken cancellation)
    {
        // Semantic identity also handles aliases and qualified/global-qualified names.
        if (constructor is null ||
            constructor.ContainingType.ContainingNamespace.ToDisplayString() != "System.Diagnostics.CodeAnalysis" ||
            constructor.ContainingType.Name is not ("SuppressMessageAttribute" or "UnconditionalSuppressMessageAttribute"))
        {
            return;
        }

        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments is null)
        {
            return;
        }

        var ordinal = 0;
        foreach (var argument in arguments.Value)
        {
            if (argument.NameEquals is not null)
            {
                continue;
            }

            if (argument.NameColon?.Name.Identifier.ValueText == "checkId" || (argument.NameColon is null && ordinal == 1))
            {
                var id = source.Semantics.GetConstantValue(argument.Expression, cancellation);
                if (id.HasValue && id.Value is string text && ProtectedId(text))
                {
                    report(Diagnostic.Create(CodeRule.Suppression, attribute.GetLocation(), "An attribute suppresses " + text));
                }
            }

            ordinal++;
        }
    }
}
