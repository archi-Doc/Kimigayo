// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns KR2001–2010 decisions from the file walk's declaration measurements and syntax candidates.
// Inputs: one active member frame, existing syntax and candidate-only semantic facts. Output: shared observations.
// Locals own independent measurements; lambdas contribute statements to their enclosing member.
// Must not walk bodies again, cache symbols per node, invent member identities or maintain another baseline.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Frame = Kimi.Analyzers.CodeRulesAnalyzer.Measurement;

namespace Kimi.Analyzers;

internal static class CodeRulesAccretion
{
    internal static readonly CodeRule Statements = CodeRule.Find("KR2006")!;
    internal static readonly CodeRule Nesting = CodeRule.Find("KR2007")!;
    private static readonly CodeRule Parameters = CodeRule.Find("KR2001")!;
    private static readonly CodeRule OutParameters = CodeRule.Find("KR2002")!;
    private static readonly CodeRule FailureText = CodeRule.Find("KR2003")!;
    private static readonly CodeRule Booleans = CodeRule.Find("KR2004")!;
    private static readonly CodeRule BooleanArgument = CodeRule.Find("KR2005")!;
    private static readonly CodeRule LogicalChain = CodeRule.Find("KR2008")!;
    private static readonly CodeRule FileTokens = CodeRule.Find("KR2009")!;
    private static readonly CodeRule Optionals = CodeRule.Find("KR2010")!;

    internal static BaseParameterListSyntax? ParameterList(SyntaxNode owner)
        => owner switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList,
            LocalFunctionStatementSyntax local => local.ParameterList,
            IndexerDeclarationSyntax indexer => indexer.ParameterList,
            DelegateDeclarationSyntax declaration => declaration.ParameterList,
            _ => null,
        };

    internal static bool CheckBooleans(SyntaxNode owner, CodeRulesConfiguration config)
    {
        var possible = 0;
        if (ParameterList(owner) is { } parameters)
        {
            foreach (var parameter in parameters.Parameters)
            {
                if (ValueParameter(parameter) && PossibleType(parameter.Type, SyntaxKind.BoolKeyword))
                {
                    possible++;
                }
            }
        }

        return possible > config.Limit(Booleans);
    }

    internal static void CheckNode(CodeRulesSource source, SyntaxNode node, ISymbol? symbol, ref Frame frame, CodeRulesConfiguration config, CancellationToken cancellation)
    {
        if (node is ParameterSyntax parameter)
        {
            CheckParameter(source, parameter, symbol as IParameterSymbol, ref frame, cancellation);
        }
        else if (node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax)
        {
            CheckCall(source, node, cancellation);
        }
        else if (node.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.AndPattern or SyntaxKind.OrPattern &&
            node.Parent?.RawKind != node.RawKind)
        {
            var count = ChainTerms(node, node.RawKind);
            var limit = config.Limit(LogicalChain);
            if (count > limit)
            {
                source.Record(LogicalChain, node, count, limit, source.NodeAnchor(node.DescendantTokens()));
            }
        }
    }

    internal static void Finish(CodeRulesSource source, Frame frame, CodeRulesConfiguration config)
    {
        if (frame.Owner is null or BaseFieldDeclarationSyntax)
        {
            return;
        }

        if (ParameterList(frame.Owner) is { } parameters)
        {
            var outs = 0;
            var optionals = 0;
            foreach (var parameter in parameters.Parameters)
            {
                outs += parameter.Modifiers.Any(SyntaxKind.OutKeyword) ? 1 : 0;
                optionals += parameter.Default is not null && !parameter.Modifiers.Any(SyntaxKind.ParamsKeyword) ? 1 : 0;
            }

            Record(Parameters, parameters.Parameters.Count, null);
            Record(OutParameters, outs, null);
            Record(Booleans, frame.Booleans, null);
            Record(Optionals, optionals, null);
        }

        Record(Statements, frame.Statements, frame.StatementException);
        Record(Nesting, frame.MaximumDepth, frame.NestingException);

        void Record(CodeRule rule, int value, CodeRuleAnnotation? annotation)
        {
            var limit = config.Limit(rule);
            var partial = rule == Statements || rule == Nesting;
            partial &= frame.Owner is MemberDeclarationSyntax member && member.Modifiers.Any(SyntaxKind.PartialKeyword);
            if (value <= limit && annotation is null && !partial)
            {
                return;
            }

            if (frame.Symbol is { } symbol)
            {
                source.RecordMember(rule, frame.Owner, symbol, value, limit, annotation);
            }
            else
            {
                source.Record(rule, frame.Owner, value, limit, source.MemberAnchor(frame.Owner), annotation);
            }
        }
    }

    internal static void CheckFile(CodeRulesSource source, CodeRulesConfiguration config)
    {
        var value = CodeRulesSource.CountTokens(source.Root);
        var limit = config.Limit(FileTokens);
        if (value > limit)
        {
            source.Record(FileTokens, source.Root, value, limit, "file");
        }
    }

    private static bool ValueParameter(ParameterSyntax parameter)
        => !parameter.Modifiers.Any(SyntaxKind.RefKeyword) && !parameter.Modifiers.Any(SyntaxKind.OutKeyword) &&
            !parameter.Modifiers.Any(SyntaxKind.InKeyword);

    private static bool PossibleType(TypeSyntax? type, SyntaxKind keyword)
    {
        if (type is NullableTypeSyntax nullable)
        {
            type = nullable.ElementType;
        }

        return type is NameSyntax || (type is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(keyword));
    }

    private static bool IsType(ITypeSymbol? type, SpecialType expected)
        => type?.SpecialType == expected || (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable &&
            nullable.TypeArguments[0].SpecialType == expected);

    private static void CheckParameter(CodeRulesSource source, ParameterSyntax parameter, IParameterSymbol? symbol, ref Frame frame, CancellationToken cancellation)
    {
        var boolean = frame.CheckBooleans && parameter.Parent?.Parent == frame.Owner && ValueParameter(parameter) &&
            PossibleType(parameter.Type, SyntaxKind.BoolKeyword);
        var failure = parameter.Modifiers.Any(SyntaxKind.OutKeyword) && PossibleType(parameter.Type, SyntaxKind.StringKeyword) && FailureName(parameter.Identifier.ValueText);
        if (!boolean && !failure)
        {
            return;
        }

        var syntaxType = parameter.Type is NullableTypeSyntax nullable ? nullable.ElementType : parameter.Type;
        var type = syntaxType is PredefinedTypeSyntax ? null : (symbol ?? source.Semantics.GetDeclaredSymbol(parameter, cancellation) as IParameterSymbol)?.Type;
        if (boolean && ((syntaxType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.BoolKeyword)) || IsType(type, SpecialType.System_Boolean)))
        {
            frame.Booleans++;
        }

        if (failure && ((syntaxType is PredefinedTypeSyntax text && text.Keyword.IsKind(SyntaxKind.StringKeyword)) || IsType(type, SpecialType.System_String)))
        {
            source.Record(FailureText, parameter, 1, 0, source.NodeAnchor(parameter.DescendantTokens()));
        }
    }

    private static bool FailureName(string name)
        => name.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("reason", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("message", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("problem", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void CheckCall(CodeRulesSource source, SyntaxNode node, CancellationToken cancellation)
    {
        var arguments = node switch
        {
            InvocationExpressionSyntax invocation => invocation.ArgumentList,
            BaseObjectCreationExpressionSyntax creation => creation.ArgumentList,
            ConstructorInitializerSyntax initializer => initializer.ArgumentList,
            _ => null,
        };
        if (arguments is null)
        {
            return;
        }

        foreach (var argument in arguments.Arguments)
        {
            if (argument.NameColon is null && CodeRulesSource.Unwrap(argument.Expression).Kind() is SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression)
            {
                var method = source.Semantics.GetSymbolInfo(node, cancellation).Symbol as IMethodSymbol;
                method = method?.ReducedFrom ?? method;
                if (method is { DeclaringSyntaxReferences.Length: > 0, MethodKind: MethodKind.Ordinary or MethodKind.Constructor or MethodKind.LocalFunction })
                {
                    source.Record(BooleanArgument, node, 1, 0, source.NodeAnchor(node.DescendantTokens()));
                }

                break;
            }
        }
    }

    private static int ChainTerms(SyntaxNode node, int kind)
    {
        if (node.RawKind != kind)
        {
            return 1;
        }

        return node switch
        {
            BinaryExpressionSyntax binary => ChainTerms(binary.Left, kind) + ChainTerms(binary.Right, kind),
            BinaryPatternSyntax pattern => ChainTerms(pattern.Left, kind) + ChainTerms(pattern.Right, kind),
            _ => 1,
        };
    }
}
