// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

namespace Kimi.Language;

public static class KotoParser
{
    public static Koto ParseExpression(ref TokenReader reader, int minBindingPower = 0)
    {
        var left = ParsePrefixExpression(ref reader);

        while (true)
        {
            if (TryParsePostfixExpression(ref left))
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
                {
                    reader.TryRead(out var token);
                    left = new MemberAccessKoto(ref reader, token, left);
                    return true;
                }

            case TokenKind.OpenParenthesis:
                {
                    reader.TryRead(out var token);

                    var arguments = ParseArgumentList();

                    this.Expect(TokenKind.CloseParen);

                    left = new InvocationKoto(left, arguments);
                    return true;
                }

            case TokenKind.OpenBracket:
                {
                    reader.TryRead(out var token);

                    var index = ParseExpression(ref reader);

                    this.Expect(TokenKind.CloseBracket);

                    left = new IndexExpressionSyntax(left, index);
                    return true;
                }

            default:
                return false;
        }
    }

    private static Koto ParsePrimaryExpression(ref TokenReader reader)
    {
        var tokenKind = reader.CurrentTokenKind;
        switch (tokenKind)
        {
            case TokenKind.Identifier:
                {
                    reader.TryRead(out var token);
                    return new UnresolvedKoto(ref reader, token);
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

                    this.Expect(TokenKind.CloseParenthesis);

                    return new ParenthesizedExpressionSyntax(expression);
                }

            default:
                {
                    reader.TryRead(out var token);
                    this.ReportUnexpectedToken(token);

                    return new ErrorExpressionSyntax(token);
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
