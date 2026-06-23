// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

internal abstract record ConditionNode;

internal sealed record ConditionBinaryNode(ConditionBinaryOperator Operator, ConditionNode Left, ConditionNode Right) : ConditionNode;

internal sealed record ConditionUnaryNode(ConditionUnaryOperator Operator, ConditionNode Operand) : ConditionNode;

internal sealed record ConditionIdentifierNode(ReadOnlyMemory<char> Name) : ConditionNode;

internal sealed record ConditionStringNode(ReadOnlyMemory<char> Value) : ConditionNode;

internal enum ConditionBinaryOperator
{
    Equals,
    NotEquals,
    And,
    Or,
}

internal enum ConditionUnaryOperator
{
    Not,
}

internal readonly struct ConditionParseResult
{
    public ConditionParseResult(bool success, ConditionNode? node)
    {
        this.Success = success;
        this.Node = node;
    }

    public bool Success { get; }

    public ConditionNode? Node { get; }
}

internal static class AttributeKotoHelper
{
    public static ConditionParseResult Parse(ref TokenReader reader)
    {// #Attribute(...)
        if (!reader.TryConsume(TokenKind.Sharp))
        {
            return default;
        }

        var identifier = reader.ReadIdentifier();
        if (identifier.Length == 0)
        {
            return default;
        }

        if (!reader.TryConsume(TokenKind.OpenParenthesis))
        {
            return default;
        }

        var expression = ParseExpression(ref reader);
        if (expression is null)
        {
            return default;
        }

        if (!reader.TryConsume(TokenKind.CloseParenthesis))
        {
            return default;
        }

        return new(true, expression);
    }

    private static ConditionNode? ParseExpression(ref TokenReader reader)
        => ParseOr(ref reader);

    private static ConditionNode? ParseOr(ref TokenReader reader)
    {
        var left = ParseAnd(ref reader);
        if (left is null)
        {
            return null;
        }

        while (reader.TryConsume(TokenKind.BarBar))
        {
            var right = ParseAnd(ref reader);
            if (right is null)
            {
                return null;
            }

            left = new ConditionBinaryNode(ConditionBinaryOperator.Or, left, right);
        }

        return left;
    }

    private static ConditionNode? ParseAnd(ref TokenReader reader)
    {
        var left = ParseEquality(ref reader);
        if (left is null)
        {
            return null;
        }

        while (reader.TryConsume(TokenKind.AmpersandAmpersand))
        {
            var right = ParseEquality(ref reader);
            if (right is null)
            {
                return null;
            }

            left = new ConditionBinaryNode(ConditionBinaryOperator.And, left, right);
        }

        return left;
    }

    private static ConditionNode? ParseEquality(ref TokenReader reader)
    {
        var left = ParseUnary(ref reader);
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            if (reader.TryConsume(TokenKind.EqualsEquals))
            {
                var right = ParseUnary(ref reader);
                if (right is null)
                {
                    return null;
                }

                left = new ConditionBinaryNode(ConditionBinaryOperator.Equals, left, right);

                continue;
            }

            if (reader.TryConsume(TokenKind.ExclamationEquals))
            {
                var right = ParseUnary(ref reader);
                if (right is null)
                {
                    return null;
                }

                left = new ConditionBinaryNode(ConditionBinaryOperator.NotEquals, left, right);

                continue;
            }

            return left;
        }
    }

    private static ConditionNode? ParseUnary(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.Exclamation))
        {
            var operand = ParseUnary(ref reader);
            if (operand is null)
            {
                return null;
            }

            return new ConditionUnaryNode(ConditionUnaryOperator.Not, operand);
        }

        return ParsePrimary(ref reader);
    }

    private static ConditionNode? ParsePrimary(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.OpenParenthesis))
        {
            var expression = ParseExpression(ref reader);
            if (expression is null)
            {
                return null;
            }

            if (!reader.TryConsume(TokenKind.CloseParenthesis))
            {
                return null;
            }

            return expression;
        }

        if (reader.TryPeek(out var token))
        {
            if (token.Kind == TokenKind.Identifier)
            {
                reader.MoveNext();
                return new ConditionIdentifierNode(token.Text);
            }

            if (token.Kind == TokenKind.Literal)
            {
                reader.MoveNext();
                return new ConditionStringNode(token.Text);
            }
        }

        return null;
    }
}
