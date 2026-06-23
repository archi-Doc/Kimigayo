// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

internal static class AttributeKotoHelper
{
    public static ConditionKoto? Parse(ref TokenReader reader)
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

        return expression;
    }

    private static ConditionKoto? ParseExpression(ref TokenReader reader)
        => ParseOr(ref reader);

    private static ConditionKoto? ParseOr(ref TokenReader reader)
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

            left = new ConditionOrKoto(left, right);
        }

        return left;
    }

    private static ConditionKoto? ParseAnd(ref TokenReader reader)
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

            left = new ConditionAndKoto(left, right);
        }

        return left;
    }

    private static ConditionKoto? ParseEquality(ref TokenReader reader)
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

                left = new ConditionEqualsKoto(left, right);

                continue;
            }

            if (reader.TryConsume(TokenKind.ExclamationEquals))
            {
                var right = ParseUnary(ref reader);
                if (right is null)
                {
                    return null;
                }

                left = new ConditionNotEqualsKoto(left, right);

                continue;
            }

            return left;
        }
    }

    private static ConditionKoto? ParseUnary(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.Not, false))
        {
            var operand = ParseUnary(ref reader);
            if (operand is null)
            {
                return null;
            }

            return new ConditionNegateKoto(operand);
        }

        return ParsePrimary(ref reader);
    }

    private static ConditionKoto? ParsePrimary(ref TokenReader reader)
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
                return new UnresolvedKoto(token);
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
