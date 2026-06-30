// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

internal static class AttributeKotoHelper
{
    public static Koto? Parse(ref TokenReader reader, Koto parent)
    {// #Attribute(...)
        if (!reader.TryConsume(TokenKind.Sharp, out var range))
        {
            return default;
        }

        var identifier = reader.ReadIdentifier();
        if (identifier.Length == 0)
        {
            return default;
        }

        // var attributeKoto = new AttributeKoto(parent, ref reader);

        if (!reader.TryConsume(TokenKind.OpenParenthesis, out range))
        {
            return default;
        }

        var expression = ParseExpression(ref reader);
        if (expression is null)
        {
            return default;
        }

        expression.Parent = parent;

        if (!reader.TryConsume(TokenKind.CloseParenthesis, out range))
        {
            return default;
        }

        return expression;
    }

    private static Koto? ParseExpression(ref TokenReader reader)
        => ParseOr(ref reader);

    private static Koto? ParseOr(ref TokenReader reader)
    {
        var left = ParseAnd(ref reader);
        if (left is null)
        {
            return null;
        }

        while (reader.TryConsume(TokenKind.Or, out var range, false))
        {
            var right = ParseAnd(ref reader);
            if (right is null)
            {
                return null;
            }

            left = new ConditionOrKoto(ref reader, range, left, right);
        }

        return left;
    }

    private static Koto? ParseAnd(ref TokenReader reader)
    {
        var left = ParseEquality(ref reader);
        if (left is null)
        {
            return null;
        }

        while (reader.TryConsume(TokenKind.And, out var range, false))
        {
            var right = ParseEquality(ref reader);
            if (right is null)
            {
                return null;
            }

            left = new ConditionAndKoto(ref reader, range, left, right);
        }

        return left;
    }

    private static Koto? ParseEquality(ref TokenReader reader)
    {
        var left = ParseUnary(ref reader);
        if (left is null)
        {
            return null;
        }

        while (true)
        {
            if (reader.TryConsume(TokenKind.EqualsEquals, out var range, false))
            {
                var right = ParseUnary(ref reader);
                if (right is null)
                {
                    return null;
                }

                left = new ConditionEqualsKoto(ref reader, range, left, right);

                continue;
            }

            if (reader.TryConsume(TokenKind.ExclamationEquals, out range, false))
            {
                var right = ParseUnary(ref reader);
                if (right is null)
                {
                    return null;
                }

                left = new ConditionNotEqualsKoto(ref reader, range, left, right);

                continue;
            }

            return left;
        }
    }

    private static Koto? ParseUnary(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.Not, out var range, false))
        {
            var operand = ParseUnary(ref reader);
            if (operand is null)
            {
                return null;
            }

            return new ConditionNegateKoto(ref reader, range, operand);
        }

        return ParsePrimary(ref reader);
    }

    private static Koto? ParsePrimary(ref TokenReader reader)
    {
        if (reader.TryConsume(TokenKind.OpenParenthesis, out var range, false))
        {
            var expression = ParseExpression(ref reader);
            if (expression is null)
            {
                return null;
            }

            if (!reader.TryConsume(TokenKind.CloseParenthesis, out range))
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
                return new UnresolvedKoto(ref reader, token);
            }

            if (token.Kind == TokenKind.Literal)
            {
                reader.MoveNext();
                return new LiteralKoto(ref reader, token);
            }
        }

        return null;
    }
}
