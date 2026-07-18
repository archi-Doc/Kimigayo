// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public static class KotoParser
{
    private const int AccessibilityModifierMask = 15;
    private const int PrefixBindingPower = 90;

    public static FieldKoto? ParseField(ref TokenReader reader, Token token)
    {// var x = 1
        var variableKind = token.Kind == TokenKind.Let ? VariableKind.Let : VariableKind.Var;

        // Field name
        if (!reader.TryRead(out var identifierToken) ||
            identifierToken.Kind != TokenKind.Identifier)
        {
            return default;
        }

        var name = identifierToken.Text;

        Token typeToken = default;
        if (reader.TryConsume(TokenKind.Colon, out _))
        {// var x: i32
            if (!reader.TryRead(out typeToken))
            {
                return default;
            }
        }

        Koto? initializer = default;
        if (reader.TryConsume(TokenKind.Equals, out _))
        {// var x = 1 + 2
            initializer = ParseExpression(ref reader);
        }

        var fieldKoto = new FieldKoto(ref reader, token, typeToken, initializer);

        reader.SkipUntil(TokenKind.EndBlock, TokenKind.Separator);// check

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToText(this KotoModifierKind kind)
    {
        var acc = kind.ExtractAccessibilityModifiers();
        string text;
        if (kind.HasFlag(KotoModifierKind.Static))
        {
            text = acc switch
            {
                KotoModifierKind.Public => "public static",
                KotoModifierKind.Protected => "protected static",
                KotoModifierKind.Private => "private static",
                KotoModifierKind.Internal => "internal static",
                KotoModifierKind.ProtectedOrInternal => "protected_or_internal static",
                KotoModifierKind.ProtectedAndInternal => "protected_and_internal static",
                _ => string.Empty,
            };
        }
        else
        {
            text = acc switch
            {
                KotoModifierKind.Public => "public",
                KotoModifierKind.Protected => "protected",
                KotoModifierKind.Private => "private",
                KotoModifierKind.Internal => "internal",
                KotoModifierKind.ProtectedOrInternal => "protected_or_internal",
                KotoModifierKind.ProtectedAndInternal => "protected_and_internal",
                _ => string.Empty,
            };
        }

        if (kind.HasFlag(KotoModifierKind.Open))
        {
            return text + " open";
        }
        else
        {
            return text;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KotoModifierKind ExtractAccessibilityModifiers(this KotoModifierKind kind)
    {
        return (KotoModifierKind)((byte)kind & AccessibilityModifierMask);
    }

    public static AttributeKoto? ConsumeTriviaAndRead(ref TokenReader reader, out Token token)
    {// Consume Attribute and Modifiers
        reader.Clear();

        AttributeKoto? koto = default;
        while (true)
        {
            var tokenKind = reader.CurrentTokenKind;
            switch (tokenKind)
            {
                case TokenKind.Separator:
                    reader.Advance();
                    continue;

                case TokenKind.Static:
                    if (reader.ModifierKind.HasFlag(KotoModifierKind.Static))
                    {// Duplicate
                        reader.AddDiagnostic(Hashed.Kimi.DuplicateModifier, KotoModifierKind.Static.ToString());
                    }

                    reader.ModifierKind |= KotoModifierKind.Static;
                    reader.Advance();
                    continue;

                case TokenKind.Open:
                    if (reader.ModifierKind.HasFlag(KotoModifierKind.Open))
                    {// Duplicate
                        reader.AddDiagnostic(Hashed.Kimi.DuplicateModifier, KotoModifierKind.Open.ToString());
                    }

                    reader.ModifierKind |= KotoModifierKind.Open;
                    reader.Advance();
                    continue;

                case TokenKind.Public:
                    ReadAccessibility(ref reader, KotoModifierKind.Public);
                    continue;

                case TokenKind.Protected:
                    ReadAccessibility(ref reader, KotoModifierKind.Protected);
                    continue;

                case TokenKind.Private:
                    ReadAccessibility(ref reader, KotoModifierKind.Private);
                    continue;

                case TokenKind.Internal:
                    ReadAccessibility(ref reader, KotoModifierKind.Internal);
                    continue;

                case TokenKind.ProtectedOrInternal:
                    ReadAccessibility(ref reader, KotoModifierKind.ProtectedOrInternal);
                    continue;

                case TokenKind.ProtectedAndInternal:
                    ReadAccessibility(ref reader, KotoModifierKind.ProtectedAndInternal);
                    continue;
            }

            if (tokenKind != TokenKind.Sharp)
            {
                reader.TryRead(out token);
                return koto;
            }

            var previousAttribute = reader.PopAttribute();

            reader.TryRead(out var attributeToken);
            var operand = ParseExpression(ref reader, PrefixBindingPower);

            if (previousAttribute is not null)
            {
                reader.PushAttribute(previousAttribute);
            }

            koto = new AttributeKoto(ref reader, attributeToken.Range, operand);
            reader.PushAttribute(koto);
        }

        void ReadAccessibility(ref TokenReader reader, KotoModifierKind kind)
        {
            var acc = reader.ModifierKind.ExtractAccessibilityModifiers();
            if (acc != default)
            {
                if (acc == kind)
                {// Duplicate
                    reader.AddDiagnostic(Hashed.Kimi.DuplicateModifier, kind.ToText());
                }
                else
                {// More than one accessibility modifier
                    reader.AddDiagnostic(Hashed.Kimi.MultipleAccessibilityModifiers);
                }
            }
            else
            {
                reader.ModifierKind = reader.ModifierKind | kind;
            }

            reader.Advance();
        }
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
            var bindingPower = GetInfixBindingPower(tokenKind);

            if (bindingPower == default || bindingPower.Left < minBindingPower)
            {
                break;
            }

            reader.TryRead(out var token);
            var right = ParseExpression(ref reader, bindingPower.Right);
            left = new BinaryKoto(ref reader, token.Range, left, right);
        }

        return left;
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader)
    {
ProcessPrefix:
        var tokenKind = reader.CurrentTokenKind;
        var bindingPower = GetPrefixBindingPower(tokenKind);
        if (bindingPower > 0)
        {
            reader.TryRead(out var token);
            var operand = ParseExpression(ref reader, bindingPower);
            // var koto = new UnaryKoto(ref reader, token, operand);
            var koto = KotoHelper.NewUnaryKoto(ref reader, token, operand);
            if (koto is AttributeKoto attributeKoto)
            {
                reader.PushAttribute(attributeKoto);
                goto ProcessPrefix;
            }

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
                {// A++
                    reader.TryRead(out var token);
                    left = new PostfixIncrementKoto(ref reader, token.Range, left);
                    return true;
                }

            case TokenKind.MinusMinus:
                {// A--
                    reader.TryRead(out var token);
                    left = new PostfixDecrementKoto(ref reader, token.Range, left);
                    return true;
                }
        }

        return false;
    }

    private static List<Koto> ParseArgumentList(ref TokenReader reader)
    {
        var tokenKind = reader.CurrentTokenKind;
        if (tokenKind == TokenKind.CloseParenthesis)
        {
            reader.Advance();
            return [];
        }

        SourceRange range;
        var arguments = new List<Koto>();

        while (tokenKind != TokenKind.Invalid &&
               tokenKind != TokenKind.CloseParenthesis)
        {
            arguments.Add(ParseExpression(ref reader));

            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                reader.Advance();
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
                    reader.Advance();
                    continue;
                }
            }

            break;
        }

        // reader.TryConsume(TokenKind.CloseParenthesis, out range);
        return arguments;
    }

    private static Koto ParsePrimaryExpression(ref TokenReader reader)
    {
Loop:
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
                    reader.TryConsume(TokenKind.CloseParenthesis, out var range, true);

                    return new ParenthesizedKoto(ref reader, new(token.Range.Start, range.End), expression);
                }

            case TokenKind.Separator:
                reader.Advance();
                goto Loop;

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
            TokenKind.Sharp => PrefixBindingPower,
            TokenKind.Dollar => PrefixBindingPower,
            TokenKind.Ampersand => PrefixBindingPower,
            TokenKind.Asterisk => PrefixBindingPower,
            // TokenKind.At => PrefixBindingPower,
            TokenKind.Plus => PrefixBindingPower,
            TokenKind.Minus => PrefixBindingPower,
            TokenKind.Not => PrefixBindingPower,
            TokenKind.Tilde => PrefixBindingPower,
            TokenKind.PlusPlus => PrefixBindingPower,
            TokenKind.MinusMinus => PrefixBindingPower,
            _ => 0,
        };

    private static (int Left, int Right) GetInfixBindingPower(TokenKind kind)
        => kind switch
        {
            // Multiplicative
            TokenKind.Asterisk => (80, 81),
            TokenKind.Slash => (80, 81),
            TokenKind.Percent => (80, 81),

            // Additive
            TokenKind.Plus => (70, 71),
            TokenKind.Minus => (70, 71),

            // Shift
            TokenKind.LessThanLessThan => (65, 66),
            TokenKind.GreaterThanGreaterThan => (65, 66),

            // Relational
            TokenKind.LessThan => (60, 61),
            TokenKind.LessThanEquals => (60, 61),
            TokenKind.GreaterThan => (60, 61),
            TokenKind.GreaterThanEquals => (60, 61),
            TokenKind.As => (60, 61),
            TokenKind.Is => (60, 61),

            // Equality
            TokenKind.EqualsEquals => (50, 51),
            TokenKind.ExclamationEquals => (50, 51),

            // Bitwise
            TokenKind.Ampersand => (40, 41),
            TokenKind.Caret => (35, 36),
            TokenKind.Bar => (30, 31),

            // Logical
            // TokenKind.AmpersandAmpersand => (20, 21),
            TokenKind.And => (20, 21),
            // TokenKind.BarBar => (10, 11),
            TokenKind.Or => (10, 11),

            // Assignment
            TokenKind.Equals => (5, 5),
            TokenKind.PlusEquals => (5, 5),
            TokenKind.MinusEquals => (5, 5),
            TokenKind.AsteriskEquals => (5, 5),
            TokenKind.SlashEquals => (5, 5),
            TokenKind.PercentEquals => (5, 5),
            TokenKind.AmpersandEquals => (5, 5),
            TokenKind.CaretEquals => (5, 5),
            TokenKind.BarEquals => (5, 5),
            TokenKind.LessThanLessThanEquals => (5, 5),
            TokenKind.GreaterThanGreaterThanEquals => (5, 5),

            _ => default,
        };
}
