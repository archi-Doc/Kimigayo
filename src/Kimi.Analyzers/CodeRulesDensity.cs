// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns syntax-only density rules through the analyzer's existing file walk.
// Inputs: syntax candidates and existing source lines. Output: observations with shared source identities.
// Only outer conditionals and flat new-local deconstructions own observations; callable boundaries are independent.
// Must not resolve symbols, copy line strings, precompute exclusion tables or deduplicate reports afterwards.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Kimi.Analyzers;

internal static class CodeRulesDensity
{
    private static readonly CodeRule LineLength = CodeRule.Find("KR3001")!;
    private static readonly CodeRule Conditional = CodeRule.Find("KR3002")!;
    private static readonly CodeRule Deconstruction = CodeRule.Find("KR3003")!;

    internal static void CheckNode(CodeRulesSource source, SyntaxNode node)
    {
        if (node is ConditionalExpressionSyntax conditional && IsOuterConditional(conditional))
        {
            foreach (var descendant in conditional.DescendantNodes(DescendIntoExpression))
            {
                if (descendant is ConditionalExpressionSyntax)
                {
                    source.Record(Conditional, conditional, 1, 0, source.NodeAnchor(conditional.DescendantTokens()));
                    break;
                }
            }
        }
        else if (node is AssignmentExpressionSyntax assignment && IsComputedDeclaration(assignment))
        {
            source.Record(Deconstruction, assignment, 1, 0, source.NodeAnchor(assignment.DescendantTokens()));
        }
    }

    internal static void CheckLines(CodeRulesSource source, CodeRulesConfiguration config, CancellationToken cancellation)
    {
        var limit = config.Limit(LineLength);
        foreach (var line in source.Text.Lines)
        {
            cancellation.ThrowIfCancellationRequested();
            if (line.Span.Length > limit && !LiteralLine(source, line.Span))
            {
                source.RecordLine(LineLength, line, limit);
            }
        }
    }

    private static bool DescendIntoExpression(SyntaxNode node)
        => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);

    private static bool IsOuterConditional(ConditionalExpressionSyntax expression)
    {
        for (var parent = expression.Parent; parent is not null && DescendIntoExpression(parent); parent = parent.Parent)
        {
            if (parent is ConditionalExpressionSyntax)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsComputedDeclaration(AssignmentExpressionSyntax assignment)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            assignment.Left is not DeclarationExpressionSyntax
            {
                Type.IsVar: true,
                Designation: ParenthesizedVariableDesignationSyntax variables,
            }

            || Unwrap(assignment.Right) is not TupleExpressionSyntax { Arguments.Count: >= 2 } tuple)
        {
            return false;
        }

        foreach (var variable in variables.Variables)
        {
            if (variable is not SingleVariableDesignationSyntax)
            {
                return false;
            }
        }

        foreach (var argument in tuple.Arguments)
        {
            if (!Simple(argument.Expression))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Simple(ExpressionSyntax expression)
    {
        expression = Unwrap(expression);
        while (expression is MemberAccessExpressionSyntax member && member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
            expression = Unwrap(member.Expression);
        }

        return expression is SimpleNameSyntax or LiteralExpressionSyntax or ThisExpressionSyntax or DefaultExpressionSyntax;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses.Expression;
        }

        return expression;
    }

    private static bool LiteralLine(CodeRulesSource source, TextSpan line)
    {
        var token = source.Root.FindToken(line.Start);
        if (!token.Span.Contains(line))
        {
            return false;
        }

        SyntaxNode? literal = token.Parent switch
        {
            LiteralExpressionSyntax value when value.IsKind(SyntaxKind.StringLiteralExpression) ||
                value.IsKind(SyntaxKind.Utf8StringLiteralExpression) => value,
            InterpolatedStringTextSyntax text => text.Parent,
            _ => null,
        };
        return literal is not null &&
            source.Text.Lines.GetLineFromPosition(literal.SpanStart).LineNumber !=
            source.Text.Lines.GetLineFromPosition(literal.Span.End).LineNumber;
    }
}
