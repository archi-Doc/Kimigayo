// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public static class KotoHelper
{
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
                }
            }
        }

        if (flag)
        {
            reader.Diagnostic.Add(reader.CurrentRange(), Hashed.Kimi.IdentifierExpected);
        }

        return sb.ToString();
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
}
