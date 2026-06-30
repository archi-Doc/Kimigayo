// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

namespace Kimi.Language;

public static class KotoParser
{
    public static Koto? ParseExpression(ref TokenReader reader, Koto parent, int minBindingPower = 0)
    {
        var left = ParsePrefixExpression(ref reader, parent);

        while (true)
        {
            // Postfix / call / member access
            if (TryParsePostfixExpression(ref left))
            {
                continue;
            }

            var op = this.Current.Kind;
            var bp = GetInfixBindingPower(op);

            if (bp is null || bp.Value.Left < minBindingPower)
            {
                break;
            }

            reader.MoveNext();

            var right = this.ParseExpression(bp.Value.Right);
            left = new BinaryExpressionSyntax(left, op, right);
        }

        return left;
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader, Koto parent)
    {
        var tokenKind = reader.CurrentTokenKind;

        var bindingPower = GetPrefixBindingPower(tokenKind);
        if (bindingPower > 0)
        {
            reader.MoveNext();

            var operand = ParseExpression(ref reader, parent, bindingPower);
            return new PrefixUnaryKoto(tokenKind, operand);
        }

        return ParsePrimaryExpression();
    }

    private static bool TryParsePostfixExpression(ref TokenReader reader, ref Koto left)
    {
        switch (this.Current.Kind)
        {
            case TokenKind.Dot:
                {
                    .NextToken();

                    var name = ExpectIdentifier();
                    left = new MemberAccessExpressionSyntax(left, name);
                    return true;
                }

            case TokenKind.OpenParen:
                {
                    reader.MoveNext();

                    var arguments = this.ParseArgumentList();

                    this.Expect(TokenKind.CloseParen);

                    left = new InvocationExpressionSyntax(left, arguments);
                    return true;
                }

            case TokenKind.OpenBracket:
                {
                    reader.MoveNext();

                    var index = this.ParseExpression();

                    this.Expect(TokenKind.CloseBracket);

                    left = new IndexExpressionSyntax(left, index);
                    return true;
                }

            default:
                return false;
        }
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader, Koto parent)
    {
        var kind = this.Current.Kind;

        var bp = GetPrefixBindingPower(kind);
        if (bp is not null)
        {
            reader.MoveNext();

            var operand = this.ParseExpression(bp.Value);
            return new PrefixUnaryExpressionSyntax(kind, operand);
        }

        return ParsePrimaryExpression();
    }

    private static Koto ParsePrimaryExpression(ref TokenReader reader, Koto parent)
    {
        switch (this.Current.Kind)
        {
            case TokenKind.Identifier:
                {
                    var token = this.Current;
                    reader.MoveNext();
                    return new IdentifierExpressionSyntax(token);
                }

            case TokenKind.NumberLiteral:
                {
                    var token = this.Current;
                    reader.MoveNext();
                    return new LiteralExpressionSyntax(token);
                }

            case TokenKind.StringLiteral:
                {
                    var token = this.Current;
                    reader.MoveNext();
                    return new LiteralExpressionSyntax(token);
                }

            case TokenKind.True:
            case TokenKind.False:
                {
                    var token = this.Current;
                    reader.MoveNext();
                    return new LiteralExpressionSyntax(token);
                }

            case TokenKind.OpenParen:
                {
                    reader.MoveNext();

                    var expression = this.ParseExpression();

                    this.Expect(TokenKind.CloseParen);

                    return new ParenthesizedExpressionSyntax(expression);
                }

            default:
                {
                    var token = this.Current;
                    this.ReportUnexpectedToken(token);

                    reader.MoveNext();

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

    private static (int Left, int Right)? GetInfixBindingPower(TokenKind kind)
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

            _ => null,
        };
}
