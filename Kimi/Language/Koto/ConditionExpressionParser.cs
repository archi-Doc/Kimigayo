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

internal ref struct AttributeParser
{
    private readonly DiagnosticCollection diagnostic;

    public AttributeParser(DiagnosticCollection diagnostic)
    {
        this.diagnostic = diagnostic;
    }

    public ConditionParseResult ParseConditionDirective(ref TokenReader reader)
    {
        // #Condition(...)
        if (!reader.TryConsume(TokenKind.Sharp))
        {
            return default;
        }

        if (!reader.TryConsumeIdentifier(Constants.ConditionKeyword))
        {
            return default;
        }

        if (!reader.TryConsume(TokenKind.OpenParenthesis))
        {
            return default;
        }

        var expression = this.ParseExpression(ref reader);
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

    private ConditionNode? ParseExpression(ref TokenReader reader)
        => this.ParseOr(ref reader);

    private ConditionNode? ParseOr(ref TokenReader reader)
    {
        var left = this.ParseAnd(ref reader);
        if (left is null)
        {
            return null;
        }

        while (reader.TryConsume(TokenKind.BarBar))
        {
            var right = this.ParseAnd(ref reader);
            if (right is null)
            {
                return null;
            }

            left = new ConditionBinaryNode(ConditionBinaryOperator.Or, left, right);
        }

        return left;
    }

    private ConditionNode? ParseAnd(ref TokenReader reader)
    {
        var left = this.ParseEquality(ref reader);
        if (left is null)
        {
            return null;
        }

        while (reader.TryConsume(TokenKind.AmpersandAmpersand))
        {
            var right = this.ParseEquality(ref reader);
            if (right is null)
            {
                return null;
            }

            left = new ConditionBinaryNode(ConditionBinaryOperator.And, left, right);
        }

        return left;
    }

    private ConditionNode? ParseEquality(ref TokenReader reader)
    {
        var left = this.ParseUnary(ref reader);
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            if (reader.TryConsume(TokenKind.EqualsEquals))
            {
                var right = this.ParseUnary(ref reader);
                if (right is null)
                {
                    return null;
                }

                left = new ConditionBinaryNode(ConditionBinaryOperator.Equals, left, right);

                continue;
            }

            if (reader.TryConsume(TokenKind.ExclamationEquals))
            {
                var right = this.ParseUnary(ref reader);
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

    private ConditionNode? ParseUnary(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.Exclamation))
        {
            var operand = this.ParseUnary(ref reader);
            if (operand is null)
            {
                return null;
            }

            return new ConditionUnaryNode(ConditionUnaryOperator.Not, operand);
        }

        return this.ParsePrimary(ref reader);
    }

    private ConditionNode? ParsePrimary(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.OpenParenthesis))
        {
            var expression = this.ParseExpression(ref reader);
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
