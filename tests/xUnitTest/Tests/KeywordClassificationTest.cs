// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Xunit;

namespace XunitTest;

/// <summary>The keyword classes of SPEC 2.5.1, written out here independently of the token enumeration's layout.</summary>
public class KeywordClassificationTest
{
    /// <summary>Every reserved spelling of SPEC 2.5.1, including the access and inheritance words.</summary>
    private static readonly string[] ReservedSpellings =
    [
        "isize", "usize", "i8", "i16", "i32", "i64", "i128", "u8", "u16", "u32", "u64", "u128", "f32", "f64", "bool", "char", "string",
        "let", "var", "func",
        "if", "else", "case", "for", "while", "loop", "do", "match", "return", "exit", "continue", "yield", "try", "require", "defer",
        "is", "not", "and", "or", "true", "false", "null",
        "public", "internal", "private", "protected", "open",
        "Self", "init", "drop", "base", "switch", "as", "_",
    ];

    /// <summary>Contextual spellings that the lexer distinguishes; outside their contexts they are Names (SPEC 2.5.1).</summary>
    private static readonly string[] ContextualSpellings =
    [
        "alias", "rootgroup", "group", "struct", "enum", "contract", "computed", "property", "extension",
        "in", "associate", "has", "get", "set", "static",
    ];

    public static TheoryData<string> Reserved => new(ReservedSpellings);

    public static TheoryData<string> Contextual => new(ContextualSpellings);

    [Theory]
    [MemberData(nameof(Reserved))]
    public void ReservedWordsAreNeverNames(string spelling)
    {
        var kind = TokenHelper.GetKeywordOrIdentifierKind(spelling);
        Assert.NotEqual(TokenKind.Identifier, kind);
        Assert.Equal(spelling, kind.ToText());
        Assert.True(kind.IsKeyword());
        Assert.False(kind.IsIdentifierOrContextualKeyword());
    }

    [Theory]
    [MemberData(nameof(Contextual))]
    public void ContextualWordsRemainNames(string spelling)
    {
        var kind = TokenHelper.GetKeywordOrIdentifierKind(spelling);
        Assert.NotEqual(TokenKind.Identifier, kind);
        Assert.Equal(spelling, kind.ToText());
        Assert.False(kind.IsKeyword());
        Assert.True(kind.IsIdentifierOrContextualKeyword());
    }

    [Fact]
    public void ClassesCoverEveryKeywordKind()
    {
        // A kind with a keyword spelling belongs to exactly one class, and the classes name every such kind.
        var named = new HashSet<string>(ReservedSpellings.Concat(ContextualSpellings));
        for (var kind = TokenKind.Bool; kind < TokenKind.Identifier; kind++)
        {
            if (kind.ToText() is { Length: > 0 } text)
            {
                Assert.Contains(text, named);
                Assert.NotEqual(kind.IsKeyword(), kind.IsIdentifierOrContextualKeyword());
            }
        }

        Assert.False(TokenKind.Invalid.IsKeyword());
        Assert.True(TokenKind.Identifier.IsIdentifierOrContextualKeyword());
        Assert.False(TokenKind.ColonColon.IsKeyword());
        Assert.False(TokenKind.ColonColon.IsIdentifierOrContextualKeyword());
    }

    [Theory]
    [InlineData("ifValue")]
    [InlineData("tryGet")]
    [InlineData("publicName")]
    [InlineData("Public")]
    [InlineData("self")]
    [InlineData("label")]
    [InlineData("during")]
    [InlineData("unsafe")]
    public void OtherWordsAreIdentifiers(string spelling)
        => Assert.Equal(TokenKind.Identifier, TokenHelper.GetKeywordOrIdentifierKind(spelling));
}
