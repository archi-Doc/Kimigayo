// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public static class LanguageHelper
{
    public static IReadOnlyDictionary<TokenKind, string> KeywordKindToKeyword => _keywordKindToKeyword;

    public static IReadOnlyDictionary<string, TokenKind> KeywordToKeywordKind => _keywordToKeywordKind;

    private static readonly Dictionary<TokenKind, string> _keywordKindToKeyword;
    private static readonly Dictionary<string, TokenKind> _keywordToKeywordKind;

    static LanguageHelper()
    {
        _keywordKindToKeyword = new();
        _keywordToKeywordKind = new();
        foreach (var x in Enum.GetValues<TokenKind>())
        {
            var keyword = x.ToString().ToLower();
            _keywordKindToKeyword[x] = keyword;
            _keywordToKeywordKind[keyword] = x;
        }
    }
}
