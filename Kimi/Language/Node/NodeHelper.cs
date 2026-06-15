// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public static class NodeHelper
{
    public static Node FromToken(Token token)
    {
        var code = token.Kind switch
        {
            TokenKind.SingleLineComment => new CommentNode(token),
            _ => default!,
        };

        return code;
    }

    public static string ValidateAndGetNamespace(UrlDiagnostic diagnostic, IReadOnlyList<Token> tokens, int start)
    {
        if (tokens.Count <= start)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = start; i < tokens.Count; i++)
        {
            if (i % 2 != 0)
            {// Identifier
                if (IsValidIdentifier(tokens[i].Span))
                {
                    sb.Append(tokens[i].Span);
                }
                else
                {
                    diagnostic.AddToken(tokens[i], Hashed.Kimi.InvalidIdentifier, tokens[i].Text);
                }
            }
            else
            {// Dot
                if (tokens[i].Kind == TokenKind.Dot)
                {
                    sb.Append(Constants.DotChar);
                }
                else
                {
                    diagnostic.AddToken(tokens[i], Hashed.Kimi.UnexpectedToken, tokens[i]);
                }
            }
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

        if (TokenHelper.KeywordToKeywordKind.TryGetValue(text, out _))
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
