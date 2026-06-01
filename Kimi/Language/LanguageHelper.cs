// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

using Arc.Collections;

public static class LanguageHelper
{
    public static IReadOnlyDictionary<TokenKind, string> KeywordKindToKeyword => _keywordKindToKeyword;

    public static readonly Utf16Hashtable<TokenKind> KeywordToKeywordKind;

    private static readonly Dictionary<TokenKind, string> _keywordKindToKeyword;

    static LanguageHelper()
    {
        _keywordKindToKeyword = new();
        KeywordToKeywordKind = new();
        foreach (var x in Enum.GetValues<TokenKind>())
        {
            if (x == TokenKind.Attribute)
            {// Anything after the attribute is not a keyword.
                break;
            }

            var keyword = x.ToString().ToLower();
            _keywordKindToKeyword[x] = keyword;
            KeywordToKeywordKind.TryAdd(keyword, x);
        }
    }
}
