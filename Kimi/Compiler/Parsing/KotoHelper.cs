// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static class KotoHelper
{
    public static Koto NewUnaryKoto(ref TokenReader reader, Token token, Koto operand) => token.Kind switch
    {
        TokenKind.Sharp => new AttributeKoto(ref reader, token.Range, operand),
        TokenKind.Dollar => new MacroKoto(ref reader, token.Range, operand),
        TokenKind.Ampersand => new ReferenceKoto(ref reader, token.Range, operand),
        TokenKind.Asterisk => new ReferenceKoto(ref reader, token.Range, operand),
        TokenKind.Plus => new PrefixPlusKoto(ref reader, token.Range, operand),
        TokenKind.Minus => new PrefixMinusKoto(ref reader, token.Range, operand),
        TokenKind.Not => new NotKoto(ref reader, token.Range, operand),
        TokenKind.Caret => new PrefixCaretKoto(ref reader, token.Range, operand),
        TokenKind.PlusPlus => new PrefixPlusPlusKoto(ref reader, token.Range, operand),
        TokenKind.MinusMinus => new PrefixMinusMinusKoto(ref reader, token.Range, operand),
        _ => throw new InvalidOperationException(),
    };

    public static Koto NewBinaryKoto(ref TokenReader reader, Token token, Koto left, Koto right) => token.Kind switch
    {
        TokenKind.EqualsEquals => new EqualsEqualsKoto(ref reader, token.Range, left, right),
        _ => throw new InvalidOperationException(),
    };

    public static bool Replace(Koto parent, Koto oldKoto, Koto newKoto)
    {
        if (parent.ReplaceChild(oldKoto, newKoto))
        {
            // Koto structure
            newKoto.Parent = parent;
            newKoto.Previous = oldKoto.Previous;
            newKoto.Next = oldKoto.Next;

            oldKoto.Parent = default;
            oldKoto.Previous = default;
            oldKoto.Next = default;

            // Frontend Metadata
            newKoto.DiagnosticCollection = oldKoto.DiagnosticCollection;
            newKoto.Range = oldKoto.Range;
            newKoto.CodeContext = oldKoto.CodeContext;
            return true;
        }

        return false;
    }

    public static void Dump(Koto koto, TextWriter writer)
    {
        DumpKoto(koto, writer, indent: "  ", isLast: true, label: null);
    }

    public static string ValidateAndGetNamespace(ref TokenReader reader)
    {
        if (reader.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var flag = true;
        while (reader.TryRead(out var token))
        {
            if (token.Kind == TokenKind.Separator)
            {
                break;
            }

            if (flag)
            {// Identifier
                flag = false;
                if (IsValidIdentifier(token.Span))
                {
                    sb.Append(token.Span);
                }
                else
                {
                    reader.Diagnostic.AddToken(token, Hashed.Kimi.InvalidIdentifier, token.Text);
                    break;
                }
            }
            else
            {// Dot
                flag = true;
                if (token.Kind == TokenKind.Dot)
                {
                    sb.Append(Constants.DotChar);
                }
                else
                {
                    reader.Diagnostic.AddToken(token, Hashed.Kimi.UnexpectedToken, token);
                    break;
                }
            }
        }

        if (flag)
        {
            reader.Diagnostic.Add(reader.CurrentRange(), Hashed.Kimi.IdentifierExpected);
        }

        return sb.ToString();
    }

    public static List<string> ValidateAndGetNamespace2(ref TokenReader reader)
    {
        if (reader.IsEmpty)
        {
            return [];
        }

        var list = new List<string>();
        var flag = true;
        while (reader.TryRead(out var token))
        {
            if (token.Kind == TokenKind.Separator)
            {
                break;
            }

            if (flag)
            {// Identifier
                flag = false;
                if (IsValidIdentifier(token.Span))
                {
                    list.Add(token.Span.ToString());
                }
                else
                {
                    reader.Diagnostic.AddToken(token, Hashed.Kimi.InvalidIdentifier, token.Text);
                    break;
                }
            }
            else
            {// Dot
                flag = true;
                if (token.Kind == TokenKind.Dot)
                {
                }
                else
                {
                    reader.Diagnostic.AddToken(token, Hashed.Kimi.UnexpectedToken, token);
                    break;
                }
            }
        }

        if (flag)
        {
            reader.Diagnostic.Add(reader.CurrentRange(), Hashed.Kimi.IdentifierExpected);
        }

        return list;
    }

    public static bool IsValidIdentifier(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        var enumerator = text.EnumerateRunes();
        if (!enumerator.MoveNext())
        {
            return false;
        }

        if (!IsIdentifierStartCharacter(enumerator.Current))
        {
            return false;
        }

        while (enumerator.MoveNext())
        {
            if (!IsIdentifierPartCharacter(enumerator.Current))
            {
                return false;
            }
        }

        if (TokenHelper.KeywordToTokenKind.TryGetValue(text, out _))
        {
            return false;
        }

        return true;
    }

    private static bool IsIdentifierStartCharacter(Rune rune)
    {
        if (rune.Value == '_')
        {
            return true;
        }

        var category = Rune.GetUnicodeCategory(rune);

        return category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.LetterNumber;
    }

    private static bool IsIdentifierPartCharacter(Rune rune)
    {
        if (IsIdentifierStartCharacter(rune))
        {
            return true;
        }

        var category = Rune.GetUnicodeCategory(rune);

        return category is
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.Format;
    }

    private static void DumpKoto(Koto koto, TextWriter writer, string indent, bool isLast, string? label)
    {
        writer.Write(indent);

        if (indent.Length > 0)
        {
            writer.Write(isLast ? "└─ " : "├─ ");
        }

        var r = koto.Dump();
        writer.WriteLine(r.Text);

        var childIndent = indent;
        if (indent.Length > 0)
        {
            childIndent += isLast ? "   " : "│  ";
        }

        if (r.Children is { } children)
        {
            for (var i = 0; i < children.Length; i++)
            {
                DumpKoto(children[i], writer, childIndent, i == children.Length - 1, default);
            }
        }
    }
}
