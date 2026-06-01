// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public static class LanguageHelper
{
    public static IReadOnlyDictionary<KeywordKind, string> KeywordKindToKeyword => _keywordKindToKeyword;

    public static IReadOnlyDictionary<string, KeywordKind> KeywordToKeywordKind => _keywordToKeywordKind;

    private static readonly Dictionary<KeywordKind, string> _keywordKindToKeyword;
    private static readonly Dictionary<string, KeywordKind> _keywordToKeywordKind;

    static LanguageHelper()
    {
        _keywordKindToKeyword = new();
        _keywordToKeywordKind = new();
        foreach (var x in Enum.GetValues<KeywordKind>())
        {
            var keyword = x.ToString().ToLower();
            _keywordKindToKeyword[x] = keyword;
            _keywordToKeywordKind[keyword] = x;
        }
    }
}
