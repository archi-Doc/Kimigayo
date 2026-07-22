// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public static class KotoParser
{
    private const int AccessibilityModifierMask = 15;
    private const int PrefixBindingPower = 90;

    public static void UnparseAttribute(AttributeKoto? a0, StringWriter writer)
    {
        if (a0 is null)
        {
            return;
        }

        var a1 = a0.AttributeChain;
        if (a1 is null)
        {
            writer.Write(a0.ToString());
            writer.Write(' ');
            return;
        }

        var a2 = a1.AttributeChain;
        if (a2 is null)
        {
            writer.Write(a1.ToString());
            writer.Write(' ');
            writer.Write(a0.ToString());
            writer.Write(' ');
            return;
        }

        var a3 = a2.AttributeChain;
        if (a3 is null)
        {
            writer.Write(a2.ToString());
            writer.Write(' ');
            writer.Write(a1.ToString());
            writer.Write(' ');
            writer.Write(a0.ToString());
            writer.Write(' ');
            return;
        }

        var a4 = a3.AttributeChain;
        if (a4 is null)
        {
            writer.Write(a3.ToString());
            writer.Write(' ');
            writer.Write(a2.ToString());
            writer.Write(' ');
            writer.Write(a1.ToString());
            writer.Write(' ');
            writer.Write(a0.ToString());
            writer.Write(' ');
            return;
        }

        var list = new List<Koto>();
        var x = a0;
        while (x is not null)
        {
            list.Add(x);
            x = x.AttributeChain;
        }

        list.Reverse();
        foreach (var y in list)
        {
            writer.Write(y.ToString());
            writer.Write(' ');
        }
    }

    public static string UnparseAttribute(AttributeKoto? a0)
    {
        if (a0 is null)
        {
            return string.Empty;
        }

        var a1 = a0.AttributeChain;
        if (a1 is null)
        {
            return $"{a0.ToString()} ";
        }

        var a2 = a1.AttributeChain;
        if (a2 is null)
        {
            return $"{a1.ToString()} {a0.ToString()} ";
        }

        var a3 = a2.AttributeChain;
        if (a3 is null)
        {
            return $"{a2.ToString()} {a1.ToString()} {a0.ToString()} ";
        }

        var a4 = a3.AttributeChain;
        if (a4 is null)
        {
            return $"{a3.ToString()} {a2.ToString()} {a1.ToString()} {a0.ToString()} ";
        }

        var list = new List<Koto>();
        var sb = new StringBuilder();
        var x = a0;
        while (x is not null)
        {
            list.Add(x);
            x = x.AttributeChain;
        }

        list.Reverse();
        foreach (var y in list)
        {
            sb.Append(y.ToString());
            sb.Append(' ');
        }

        return sb.ToString();
    }

    public static FieldKoto? ParseField(ref TokenReader reader, ref Token token)
    {// var x = 1
        var variableState = reader.StoreState();

        // Field name
        var nameAttribute = KotoParser.ConsumeAttributeAndRead(ref reader, out var nameToken);
        if (nameToken.Kind != TokenKind.Identifier)
        {
            return default;
        }

        var nameKoto = new UnresolvedKoto(ref reader, nameToken);

        Token typeToken = default;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {// var x: i32
            if (!reader.TryRead(out typeToken))
            {
                return default;
            }
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out _))
        {// var x = 1 + 2
            initializerKoto = ParseExpression(ref reader);
        }

        reader.RestoreState(variableState);

        var fieldKoto = new FieldKoto(ref reader, ref token, typeToken, nameKoto, initializerKoto);

        reader.SkipUntil(TokenKind.EndBlock, TokenKind.Separator, Hashed.Kimi.UnexpectedTrailingToken);

        return fieldKoto;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteTo(this ModifierKind kind, StringWriter writer, bool addSpace = false)
    {
        var acc = kind.ExtractAccessibilityModifiers();
        var accText = acc switch
        {
            ModifierKind.Public => "public",
            ModifierKind.Protected => "protected",
            ModifierKind.Private => "private",
            ModifierKind.Internal => "internal",
            ModifierKind.ProtectedOrInternal => "protected_or_internal",
            ModifierKind.ProtectedAndInternal => "protected_and_internal",
            _ => string.Empty,
        };

        writer.Write(accText);

        if (addSpace)
        {// "public "
            if (kind.HasFlag(ModifierKind.Static))
            {// "public static "
                if (kind.HasFlag(ModifierKind.Open))
                {// "public static open "
                    writer.Write(" static open ");
                }
                else
                {// "public static "
                    writer.Write(" static ");
                }
            }
            else
            {// "public "
                if (kind.HasFlag(ModifierKind.Open))
                {// public open "
                    writer.Write(" open ");
                }
                else
                {// "public "
                    writer.Write(" ");
                }
            }
        }
        else
        {// "public"
            if (kind.HasFlag(ModifierKind.Static))
            {// "public static"
                if (kind.HasFlag(ModifierKind.Open))
                {// "public static open"
                    writer.Write(" static open");
                }
                else
                {// "public static"
                    writer.Write(" static");
                }
            }
            else
            {// "public"
                if (kind.HasFlag(ModifierKind.Open))
                {// public open"
                    writer.Write(" open");
                }
                else
                {// "public"
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToText(this ModifierKind kind, bool addSpace = false)
    {
        var acc = kind.ExtractAccessibilityModifiers();
        var accText = acc switch
        {
            ModifierKind.Public => "public",
            ModifierKind.Protected => "protected",
            ModifierKind.Private => "private",
            ModifierKind.Internal => "internal",
            ModifierKind.ProtectedOrInternal => "protected_or_internal",
            ModifierKind.ProtectedAndInternal => "protected_and_internal",
            _ => string.Empty,
        };

        if (addSpace)
        {// "public "
            if (kind.HasFlag(ModifierKind.Static))
            {// "public static "
                if (kind.HasFlag(ModifierKind.Open))
                {// "public static open "
                    return $"{accText} static open ";
                }
                else
                {// "public static "
                    return $"{accText} static ";
                }
            }
            else
            {// "public "
                if (kind.HasFlag(ModifierKind.Open))
                {// public open "
                    return $"{accText} open ";
                }
                else
                {// "public "
                    return $"{accText} ";
                }
            }
        }
        else
        {// "public"
            if (kind.HasFlag(ModifierKind.Static))
            {// "public static"
                if (kind.HasFlag(ModifierKind.Open))
                {// "public static open"
                    return $"{accText} static open";
                }
                else
                {// "public static"
                    return $"{accText} static";
                }
            }
            else
            {// "public"
                if (kind.HasFlag(ModifierKind.Open))
                {// public open"
                    return $"{accText} open";
                }
                else
                {// "public"
                    return $"{accText}";
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ModifierKind ExtractAccessibilityModifiers(this ModifierKind kind)
    {
        return (ModifierKind)((byte)kind & AccessibilityModifierMask);
    }

    public static AttributeKoto? ConsumeAttributeAndRead(ref TokenReader reader, out Token token)
    {// Consume Attribute
        reader.Clear();

        AttributeKoto? attributeKoto = default;
        while (true)
        {
            while (true)
            {
                var tokenKind = reader.CurrentTokenKind;
                if (tokenKind == TokenKind.Separator)
                {// Separator
                    reader.Advance();
                    continue;
                }
                else if (tokenKind == TokenKind.Sharp)
                {// Attribute
                    break;
                }
                else
                {// Other
                    reader.TryRead(out token);
                    return attributeKoto;
                }
            }

            attributeKoto = ParseAttributeKoto(ref reader);
        }
    }

    public static AttributeKoto? ConsumeAttributeModifierAndRead(ref TokenReader reader, out Token token)
    {// Consume Attributes and Modifiers
        reader.Clear();

        AttributeKoto? attributeKoto = default;
        while (true)
        {
            var tokenKind = reader.CurrentTokenKind;
            switch (tokenKind)
            {
                case TokenKind.Separator:
                    reader.Advance();
                    continue;

                case TokenKind.Static:
                    if (reader.ModifierKind.HasFlag(ModifierKind.Static))
                    {// Duplicate
                        reader.AddDiagnostic(Hashed.Kimi.DuplicateModifier, ModifierKind.Static.ToString());
                    }

                    reader.ModifierKind |= ModifierKind.Static;
                    reader.Advance();
                    continue;

                case TokenKind.Open:
                    if (reader.ModifierKind.HasFlag(ModifierKind.Open))
                    {// Duplicate
                        reader.AddDiagnostic(Hashed.Kimi.DuplicateModifier, ModifierKind.Open.ToString());
                    }

                    reader.ModifierKind |= ModifierKind.Open;
                    reader.Advance();
                    continue;

                case TokenKind.Public:
                    ReadAccessibility(ref reader, ModifierKind.Public);
                    continue;

                case TokenKind.Protected:
                    ReadAccessibility(ref reader, ModifierKind.Protected);
                    continue;

                case TokenKind.Private:
                    ReadAccessibility(ref reader, ModifierKind.Private);
                    continue;

                case TokenKind.Internal:
                    ReadAccessibility(ref reader, ModifierKind.Internal);
                    continue;

                case TokenKind.ProtectedOrInternal:
                    ReadAccessibility(ref reader, ModifierKind.ProtectedOrInternal);
                    continue;

                case TokenKind.ProtectedAndInternal:
                    ReadAccessibility(ref reader, ModifierKind.ProtectedAndInternal);
                    continue;
            }

            if (tokenKind != TokenKind.Sharp)
            {
                reader.TryRead(out token);
                return attributeKoto;
            }

            attributeKoto = ParseAttributeKoto(ref reader);
        }

        void ReadAccessibility(ref TokenReader reader, ModifierKind kind)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AttributeKoto ParseAttributeKoto(ref TokenReader reader)
    {
        var previousAttribute = reader.PopAttribute();

        reader.TryRead(out var attributeToken);
        var operand = ParseExpression(ref reader, PrefixBindingPower);

        if (previousAttribute is not null)
        {
            reader.PushAttribute(previousAttribute);
        }

        var attributeKoto = new AttributeKoto(ref reader, attributeToken.Range, operand);
        reader.PushAttribute(attributeKoto);

        return attributeKoto;
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
            left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
            // left = new BinaryKoto(ref reader, token.Range, left, right);
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
            return [];
        }

        SourceRange range;
        var arguments = new List<Koto>();

        while (tokenKind != TokenKind.Invalid &&
               tokenKind != TokenKind.CloseParenthesis)
        {
            arguments.Add(ParseExpression(ref reader));

            tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Comma)
            {
                reader.Advance();
                if (tokenKind == TokenKind.CloseParenthesis)
                {
                    break;
                }

                continue;
            }

            if (tokenKind != TokenKind.CloseParenthesis)
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
            TokenKind.Caret => PrefixBindingPower,
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
