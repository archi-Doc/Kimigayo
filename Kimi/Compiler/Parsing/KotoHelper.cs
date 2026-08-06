// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    public static string ToText(this ReferenceKind referenceKind, bool appendSpace)
    {
        if (appendSpace)
        {
            return referenceKind switch
            {
                ReferenceKind.Borrow => "&",
                ReferenceKind.Owner => "&owner ",
                ReferenceKind.Unsafe => "&unsafe ",
                ReferenceKind.Rc => "&rc ",
                ReferenceKind.Arc => "&arc ",
                _ => string.Empty,
            };
        }
        else
        {
            return referenceKind switch
            {
                ReferenceKind.Borrow => "&",
                ReferenceKind.Owner => "&owner",
                ReferenceKind.Unsafe => "&unsafe",
                ReferenceKind.Rc => "&rc",
                ReferenceKind.Arc => "&arc",
                _ => string.Empty,
            };
        }
    }

    public static ReferenceKind ToReferenceKind(this ReadOnlySpan<char> text)
    {
        var length = text.Length;
        if (length == 0)
        {
            return ReferenceKind.Borrow;
        }
        else if (length == 2)
        {
            if (text[0] == 'r' && text[1] == 'c')
            {
                return ReferenceKind.Rc;
            }
        }
        else if (length == 3)
        {
            if (text[0] == 'a' && text[1] == 'r' && text[2] == 'c')
            {
                return ReferenceKind.Arc;
            }
        }
        else if (length == 5)
        {
            if (text[0] == 'o' && text[1] == 'w' && text[2] == 'n' && text[3] == 'e' && text[4] == 'r')
            {
                return ReferenceKind.Owner;
            }
        }
        else if (length == 6)
        {
            if (text[0] == 'u' && text[1] == 'n' && text[2] == 's' && text[3] == 'a' && text[4] == 'f' && text[5] == 'e')
            {
                return ReferenceKind.Unsafe;
            }
        }

        return ReferenceKind.None;
    }

    public static Koto NewUnaryKoto(ref TokenReader reader, Token token, Koto operand) => token.Kind switch
    {
        TokenKind.Sharp => new AttributeKoto(ref reader, token.Range, operand),
        TokenKind.Dollar => new MacroKoto(ref reader, token.Range, operand),
        TokenKind.Asterisk => new ReferenceKoto(ref reader, token.Range, operand, ReferenceKind.None),
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
        TokenKind.Asterisk => new AsteriskKoto(ref reader, token.Range, left, right),
        TokenKind.Slash => new SlashKoto(ref reader, token.Range, left, right),
        TokenKind.Percent => new PercentKoto(ref reader, token.Range, left, right),
        TokenKind.Plus => new PlusKoto(ref reader, token.Range, left, right),
        TokenKind.Minus => new MinusKoto(ref reader, token.Range, left, right),
        TokenKind.LessThanLessThan => new LessThanLessThanKoto(ref reader, token.Range, left, right),
        TokenKind.GreaterThanGreaterThan => new GreaterThanGreaterThanKoto(ref reader, token.Range, left, right),
        TokenKind.LessThan => new LessThanKoto(ref reader, token.Range, left, right),
        TokenKind.LessThanEquals => new LessThanEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.GreaterThan => new GreaterThanKoto(ref reader, token.Range, left, right),
        TokenKind.GreaterThanEquals => new GreaterThanEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.As => new AsKoto(ref reader, token.Range, left, right),
        TokenKind.Is => new IsKoto(ref reader, token.Range, left, right),
        TokenKind.EqualsEquals => new EqualsEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.ExclamationEquals => new ExclamationEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.Ampersand => new AmpersandKoto(ref reader, token.Range, left, right),
        TokenKind.Caret => new CaretKoto(ref reader, token.Range, left, right),
        TokenKind.Bar => new BarKoto(ref reader, token.Range, left, right),
        TokenKind.And => new AndKoto(ref reader, token.Range, left, right),
        TokenKind.Or => new OrKoto(ref reader, token.Range, left, right),
        TokenKind.Equals => new EqualsKoto(ref reader, token.Range, left, right),
        TokenKind.PlusEquals => new PlusEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.MinusEquals => new MinusEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.AsteriskEquals => new AsteriskEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.SlashEquals => new SlashEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.PercentEquals => new PercentEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.AmpersandEquals => new AmpersandEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.CaretEquals => new CaretEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.BarEquals => new BarEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.LessThanLessThanEquals => new LessThanLessThanEqualsKoto(ref reader, token.Range, left, right),
        TokenKind.GreaterThanGreaterThanEquals => new GreaterThanGreaterThanEqualsKoto(ref reader, token.Range, left, right),

        _ => throw new InvalidOperationException(),
    };

    public static bool Replace(Koto parent, Koto oldKoto, Koto newKoto)
    {
        if (parent.ReplaceChild(oldKoto, newKoto))
        {
            // Koto structure
            newKoto.Parent = parent;
            newKoto.Goshujin?.ChildLinkChain.UnsafeReplaceInstance(oldKoto, newKoto);
            /*newKoto.ChildLinkLink.Previous = oldKoto.Previous;
            newKoto.Next = oldKoto.Next;

            oldKoto.Parent = default;
            oldKoto.Previous = default;
            oldKoto.Next = default;*/
            oldKoto.Goshujin = default;

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
        if (reader.IsEnd)
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
                var span = reader.GetSpan(token);
                if (IsValidIdentifier(span))
                {
                    sb.Append(span);
                }
                else
                {
                    reader.Diagnostic.AddToken(token, Hashed.Kimi.InvalidIdentifier, span.ToString());
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
            reader.Diagnostic.Add(reader.CurrentTokenRange, Hashed.Kimi.IdentifierExpected);
        }

        return sb.ToString();
    }

    public static List<string> ValidateAndGetNamespace2(ref TokenReader reader)
    {
        if (reader.IsEnd)
        {
            return [];
        }

        var list = new List<string>();
        var flag = true;
        while (reader.CanRead)
        {
            var token = reader.CurrentToken;
            reader.Advance();

            if (token.Kind == TokenKind.Separator)
            {
                break;
            }

            if (flag)
            {// Identifier
                flag = false;
                var span = reader.GetSpan(token);
                if (IsValidIdentifier(span))
                {
                    list.Add(span.ToString());
                }
                else
                {
                    reader.Diagnostic.AddToken(token, Hashed.Kimi.InvalidIdentifier, span.ToString());
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
            reader.Diagnostic.Add(reader.CurrentTokenRange, Hashed.Kimi.IdentifierExpected);
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
