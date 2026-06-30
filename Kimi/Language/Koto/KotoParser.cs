// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;
using Kimigayo.Language;

namespace Kimi.Language;

public static class KotoParser
{
    public static AttributeKoto? ParseAttribute(ref TokenReader reader, Koto parent)
    {// #Attribute(...)
        if (!reader.TryConsume(TokenKind.Sharp, out var range))
        {
            return default;
        }

        var expression = ParseExpression(ref reader);
        if (expression is null)
        {
            return default;
        }

        var attributeKoto = new AttributeKoto(ref reader, parent, ref reader);

        if (!reader.TryConsume(TokenKind.OpenParenthesis, out range))
        {
            return default;
        }

        ParseArgumentList(ref reader);

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

    public static Koto ParseExpression(ref TokenReader reader, int minBindingPower = 0)
    {
        var left = ParsePrefixExpression(ref reader);

        while (true)
        {
            if (TryParsePostfixExpression(ref reader, ref left))
            {
                continue;
            }

            var tokenKind = reader.CurrentTokenKind;
            var bp = GetInfixBindingPower(tokenKind);

            if (bp == default || bp.Left < minBindingPower)
            {
                break;
            }

            reader.TryRead(out var token);
            var right = ParseExpression(ref reader, bp.Right);
            left = new BinaryKoto(ref reader, token, left, right);
        }

        return left;
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader)
    {
        var tokenKind = reader.CurrentTokenKind;
        var bindingPower = GetPrefixBindingPower(tokenKind);
        if (bindingPower > 0)
        {
            reader.TryRead(out var token);
            var operand = ParseExpression(ref reader, bindingPower);
            var koto = new PrefixUnaryKoto(ref reader, token, operand);
            return koto;
        }

        return ParsePrimaryExpression(ref reader);
    }

    private static bool TryParsePostfixExpression(ref TokenReader reader, ref Koto left)
    {
        var tokenKind = reader.CurrentTokenKind;
        switch (tokenKind)
        {
            case TokenKind.Dot:
                {// Class.Member
                    reader.TryRead(out var token); // .

                    if (!reader.TryRead(out var token2) ||
                        token2.Kind != TokenKind.Identifier)
                    {
                        break;
                    }

                    var koto = new UnresolvedKoto(ref reader, token2);
                    left = new MemberAccessKoto(ref reader, new(token.Range.Start, token2.Range.End), left, koto);
                    return true;
                }

            case TokenKind.OpenParenthesis:
                {// Method(A, B)
                    reader.TryRead(out var token); // (
                    var arguments = ParseArgumentList(ref reader);
                    reader.TryConsume(TokenKind.CloseParenthesis, out var range, true); // )

                    left = new InvocationKoto(ref reader, left, arguments);
                    return true;
                }

            case TokenKind.OpenBracket:
                {// Array[index]
                    reader.TryRead(out var token); // [
                    var index = ParseExpression(ref reader);
                    reader.TryConsume(TokenKind.CloseBracket, out var range, true); // ]

                    left = new IndexKoto(ref reader, new(token.Range.Start, range.End), left, index);
                    return true;
                }

            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                {
                    reader.TryRead(out var token); // '++'
                    left = new PostfixUnaryKoto(ref reader, token, left);
                    return true;
                }
        }

        return false;
    }

    private static List<Koto> ParseArgumentList(ref TokenReader reader)
    {
        SourceRange range;
        var tokenKind = reader.CurrentTokenKind;
        var arguments = new List<Koto>();
        if (tokenKind == TokenKind.CloseParenthesis)
        {
            reader.MoveNext();
            return arguments;
        }

        while (tokenKind != TokenKind.Invalid &&
               tokenKind != TokenKind.CloseParenthesis)
        {
            arguments.Add(ParseExpression(ref reader));

            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                reader.MoveNext();
                if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
                {
                    break;
                }

                continue;
            }

            if (reader.CurrentTokenKind != TokenKind.CloseParenthesis)
            {
                reader.TryConsume(TokenKind.Comma, out range);
                reader.SkipUntil(TokenKind.Comma, TokenKind.CloseParenthesis);

                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    reader.MoveNext();
                    continue;
                }
            }

            break;
        }

        reader.TryConsume(TokenKind.CloseParenthesis, out range);
        return arguments;
    }

    private static Koto ParsePrimaryExpression(ref TokenReader reader)
    {
        var tokenKind = reader.CurrentTokenKind;
        switch (tokenKind)
        {
            case TokenKind.Identifier:
                {
                    return UnresolvedKoto.FromReader(ref reader);
                }

            case TokenKind.NumericLiteral:
                {
                    reader.TryRead(out var token);
                    return new NumericLiteralKoto(ref reader, token);
                }

            case TokenKind.StringLiteral:
                {
                    reader.TryRead(out var token);
                    return new StringLiteralKoto(ref reader, token);
                }

            case TokenKind.True:
            case TokenKind.False:
                {
                    reader.TryRead(out var token);
                    return new BoolLiteralKoto(ref reader, token);
                }

            case TokenKind.OpenParenthesis:
                {
                    reader.TryRead(out var token);

                    var expression = ParseExpression(ref reader);
                    reader.TryConsume(TokenKind.CloseParenthesis, out var range, true);

                    return new ParenthesizedKoto(ref reader, new(token.Range.Start, range.End), expression);
                }

            default:
                {
                    reader.TryRead(out var token);
                    reader.ReportUnexpectedToken(token);

                    return new ErrorKoto(ref reader, token);
                }
        }
    }

    private static int GetPrefixBindingPower(TokenKind kind)
        => kind switch
        {
            TokenKind.Plus => 90,
            TokenKind.Minus => 90,
            TokenKind.Not => 90,
            TokenKind.Tilde => 90,
            TokenKind.PlusPlus => 90,
            TokenKind.MinusMinus => 90,
            TokenKind.Asterisk => 90,
            TokenKind.Ampersand => 90,
            _ => 0,
        };

    private static (int Left, int Right) GetInfixBindingPower(TokenKind kind)
        => kind switch
        {
            TokenKind.Asterisk => (80, 81),
            TokenKind.Slash => (80, 81),
            TokenKind.Percent => (80, 81),

            TokenKind.Plus => (70, 71),
            TokenKind.Minus => (70, 71),

            TokenKind.LessThan => (60, 61),
            TokenKind.LessThanEquals => (60, 61),
            TokenKind.GreaterThan => (60, 61),
            TokenKind.GreaterThanEquals => (60, 61),

            TokenKind.EqualsEquals => (50, 51),
            TokenKind.ExclamationEquals => (50, 51),

            TokenKind.Ampersand => (40, 41),
            TokenKind.Caret => (35, 36),
            TokenKind.Bar => (30, 31),

            TokenKind.AmpersandAmpersand => (20, 21),
            TokenKind.BarBar => (10, 11),

            _ => default,
        };
}
