// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;

namespace Kimi.Compiler.Lexing;

#pragma warning disable SA1202 // Elements should be ordered by access

/// <summary>
/// Provides token-related helper methods used by the Kimi tokenizer.
/// </summary>
public static partial class TokenHelper
{
    /// <summary>
    /// The maximum number of token kinds supported by the descriptor table.
    /// </summary>
    public const int MaxTokens = 256;

    private const int MinKeywordLength = 2;
    private const int MaxKeywordLength = 22;

    private static readonly string[] TokenTexts;

    /// <summary>
    /// Maps UTF-16 keyword spellings to their corresponding keyword token kinds.
    /// </summary>
    public static readonly Utf16Hashtable<TokenKind> KeywordToTokenKind;

    // Characters that terminate an identifier/keyword scan, as a 128-bit ASCII bitmap.
    private static readonly ulong SeparatorBitsLow;
    private static readonly ulong SeparatorBitsHigh;

    static TokenHelper()
    {
        foreach (var c in " \t\r\n(){}[].,;:?+-*/%&|^!~=<>#$@'\"")
        {
            if (c < 64)
            {
                SeparatorBitsLow |= 1UL << c;
            }
            else
            {
                SeparatorBitsHigh |= 1UL << (c - 64);
            }
        }

        TokenTexts = new string[MaxTokens];
        Array.Fill(TokenTexts, string.Empty);

        // Keywords (Primitive types)
        Set(TokenKind.Bool, Constants.BoolKeyword);
        Set(TokenKind.Isize, Constants.IsizeKeyword);
        Set(TokenKind.Usize, Constants.UsizeKeyword);
        Set(TokenKind.I8, Constants.I8Keyword);
        Set(TokenKind.I16, Constants.I16Keyword);
        Set(TokenKind.I32, Constants.I32Keyword);
        Set(TokenKind.I64, Constants.I64Keyword);
        Set(TokenKind.I128, Constants.I128Keyword);
        Set(TokenKind.U8, Constants.U8Keyword);
        Set(TokenKind.U16, Constants.U16Keyword);
        Set(TokenKind.U32, Constants.U32Keyword);
        Set(TokenKind.U64, Constants.U64Keyword);
        Set(TokenKind.U128, Constants.U128Keyword);
        Set(TokenKind.F32, Constants.F32Keyword);
        Set(TokenKind.F64, Constants.F64Keyword);
        Set(TokenKind.Char, Constants.CharKeyword);
        Set(TokenKind.String, Constants.StringKeyword);

        // Keywords
        Set(TokenKind.True, Constants.TrueKeyword);
        Set(TokenKind.False, Constants.FalseKeyword);
        Set(TokenKind.Let, Constants.LetKeyword);
        Set(TokenKind.Var, Constants.VarKeyword);
        Set(TokenKind.Func, Constants.FuncKeyword);

        // Expression keyword
        Set(TokenKind.If, Constants.IfKeyword);
        Set(TokenKind.Else, Constants.ElseKeyword);
        Set(TokenKind.Case, Constants.CaseKeyword);
        Set(TokenKind.As, Constants.AsKeyword);
        Set(TokenKind.Is, Constants.IsKeyword);
        Set(TokenKind.Not, Constants.NotKeyword);
        Set(TokenKind.And, Constants.AndKeyword);
        Set(TokenKind.Or, Constants.OrKeyword);
        Set(TokenKind.For, Constants.ForKeyword);
        Set(TokenKind.In, Constants.InKeyword);
        Set(TokenKind.While, Constants.WhileKeyword);
        Set(TokenKind.Loop, Constants.LoopKeyword);
        Set(TokenKind.Match, Constants.MatchKeyword);
        Set(TokenKind.Return, Constants.ReturnKeyword);
        Set(TokenKind.Exit, Constants.ExitKeyword);
        Set(TokenKind.Continue, Constants.ContinueKeyword);
        Set(TokenKind.Yield, Constants.YieldKeyword);

        // Contextual keyword
        Set(TokenKind.Alias, Constants.AliasKeyword);
        Set(TokenKind.RootGroup, Constants.RootgroupKeyword);
        Set(TokenKind.Group, Constants.GroupKeyword);
        Set(TokenKind.Struct, Constants.StructKeyword);
        Set(TokenKind.Enum, Constants.EnumKeyword);
        Set(TokenKind.Extension, Constants.ExtensionKeyword);
        Set(TokenKind.Contract, Constants.ContractKeyword);
        Set(TokenKind.Static, Constants.StaticKeyword);
        Set(TokenKind.Public, Constants.PublicKeyword);
        Set(TokenKind.Protected, Constants.ProtectedKeyword);
        Set(TokenKind.Private, Constants.PrivateKeyword);
        Set(TokenKind.Internal, Constants.InternalKeyword);
        Set(TokenKind.ProtectedOrInternal, Constants.ProtectedOrInternalKeyword);
        Set(TokenKind.ProtectedAndInternal, Constants.ProtectedAndInternalKeyword);
        Set(TokenKind.Open, Constants.OpenKeyword);
        Set(TokenKind.Associate, Constants.AssociateKeyword);
        Set(TokenKind.Get, Constants.GetKeyword);
        Set(TokenKind.Set, Constants.SetKeyword);
        Set(TokenKind.Has, Constants.HasKeyword);

        // Single token
        Set(TokenKind.Sharp, "#");
        Set(TokenKind.Dollar, "$");
        Set(TokenKind.At, "@");
        Set(TokenKind.Comma, ",");
        Set(TokenKind.OpenBracket, "[");
        Set(TokenKind.CloseBracket, "]");
        Set(TokenKind.OpenParenthesis, "(");
        Set(TokenKind.CloseParenthesis, ")");
        Set(TokenKind.OpenBrace, "{");
        Set(TokenKind.CloseBrace, "}");
        Set(TokenKind.Colon, ":");
        Set(TokenKind.Semicolon, ";");
        Set(TokenKind.Question, "?");

        // Others
        Set(TokenKind.Ampersand, "&");
        Set(TokenKind.AmpersandAmpersand, "&&");
        Set(TokenKind.AmpersandEquals, "&=");
        Set(TokenKind.Asterisk, "*");
        Set(TokenKind.AsteriskEquals, "*=");
        Set(TokenKind.Bar, "|");
        Set(TokenKind.BarBar, "||");
        Set(TokenKind.BarEquals, "|=");
        Set(TokenKind.Caret, "^");
        Set(TokenKind.CaretEquals, "^=");
        Set(TokenKind.Dot, ".");
        Set(TokenKind.DotDot, "..");
        Set(TokenKind.DotDotEquals, "..=");
        Set(TokenKind.Equals, "=");
        Set(TokenKind.EqualsEquals, "==");
        Set(TokenKind.EqualsGreaterThan, "=>");
        Set(TokenKind.Exclamation, "!");
        Set(TokenKind.ExclamationEquals, "!=");
        Set(TokenKind.GreaterThan, ">");
        Set(TokenKind.GreaterThanEquals, ">=");
        Set(TokenKind.GreaterThanGreaterThan, ">>");
        Set(TokenKind.GreaterThanGreaterThanEquals, ">>=");
        Set(TokenKind.LessThan, "<");
        Set(TokenKind.LessThanEquals, "<=");
        Set(TokenKind.LessThanLessThan, "<<");
        Set(TokenKind.LessThanLessThanEquals, "<<=");
        Set(TokenKind.Minus, "-");
        Set(TokenKind.MinusEquals, "-=");
        Set(TokenKind.MinusGreaterThan, "->");
        Set(TokenKind.MinusMinus, "--");
        Set(TokenKind.Percent, "%");
        Set(TokenKind.PercentEquals, "%=");
        Set(TokenKind.Plus, "+");
        Set(TokenKind.PlusEquals, "+=");
        Set(TokenKind.PlusPlus, "++");
        Set(TokenKind.Slash, "/");
        Set(TokenKind.SlashEquals, "/=");

        KeywordToTokenKind = new();
        for (var i = (int)TokenKind.Bool; i < (int)TokenKind.Identifier; i++)
        {
            var text = TokenTexts[i];
            if (text.Length > 0)
            {
                KeywordToTokenKind.TryAdd(text, (TokenKind)i);
            }
        }

        static void Set(TokenKind kind, string text)
            => TokenTexts[(int)kind] = text;
    }

    /// <summary>
    /// Gets the source spelling of a token kind.
    /// </summary>
    /// <param name="tokenKind">The token kind to convert.</param>
    /// <returns>The source spelling, or an empty string for synthetic tokens.</returns>
    public static string ToText(this TokenKind tokenKind)
        => TokenTexts[(int)tokenKind];

    /// <summary>
    /// Determines whether a character terminates an identifier or keyword scan.
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns><see langword="true"/> for a tokenizer separator.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSeparator(char c)
    {
        if (c < 64)
        {
            return ((SeparatorBitsLow >> c) & 1) != 0;
        }

        return c < 128 && ((SeparatorBitsHigh >> (c - 64)) & 1) != 0;
    }

    /// <summary>
    /// Finds the first tokenizer separator in the specified text.
    /// </summary>
    /// <remarks>Identifiers are short, so a scalar scan beats the setup cost of a vectorized search.</remarks>
    /// <param name="text">The text to search.</param>
    /// <returns>The zero-based index of the first separator, or -1 if no separator is found.</returns>
    public static int IndexOfSeparator(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (IsSeparator(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Determines whether a token starts an indentation block.
    /// </summary>
    /// <remarks>The check relies on the block token kinds forming a contiguous range.</remarks>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="tokenKind"/> is a block-starting token; otherwise, <see langword="false"/>.</returns>
    public static bool IsBlockToken(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Group && tokenKind <= TokenKind.Match;

    /// <summary>
    /// Determines whether a token represents a primitive type keyword.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for a primitive type keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPrimitiveType(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Bool && tokenKind <= TokenKind.String;

    /// <summary>
    /// Determines whether a token represents a reserved keyword.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for a reserved keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyword(this TokenKind tokenKind)
        => tokenKind < TokenKind.Alias;

    /// <summary>
    /// Determines whether a token can be used as an identifier.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for an identifier or contextual keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierOrContextualKeyword(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Alias && tokenKind <= TokenKind.Identifier;

    /// <summary>
    /// Classifies identifier-like text as a keyword.
    /// </summary>
    /// <remarks>
    /// Keywords are lowercase ASCII, so the text is dispatched on its length and first
    /// character before a full comparison. This avoids hashing every identifier.
    /// </remarks>
    /// <param name="text">The identifier-like text.</param>
    /// <returns>The keyword token kind, or <see cref="TokenKind.Identifier"/>.</returns>
    public static TokenKind GetKeywordOrIdentifierKind(ReadOnlySpan<char> text)
    {
        var length = text.Length;
        if (length < MinKeywordLength || length > MaxKeywordLength)
        {
            return TokenKind.Identifier;
        }

        var c0 = text[0];
        if (c0 < 'a' || c0 > 'z')
        {
            return TokenKind.Identifier;
        }

        var kind = length switch
        {
            2 => c0 switch
            {
                'i' => text[1] switch
                {
                    '8' => TokenKind.I8,
                    'f' => TokenKind.If,
                    's' => TokenKind.Is,
                    'n' => TokenKind.In,
                    _ => TokenKind.Identifier,
                },
                'u' => text[1] == '8' ? TokenKind.U8 : TokenKind.Identifier,
                'a' => text[1] == 's' ? TokenKind.As : TokenKind.Identifier,
                'o' => text[1] == 'r' ? TokenKind.Or : TokenKind.Identifier,
                _ => TokenKind.Identifier,
            },
            3 => c0 switch
            {
                'i' => Match(text, Constants.I16Keyword, TokenKind.I16, Constants.I32Keyword, TokenKind.I32, Constants.I64Keyword, TokenKind.I64),
                'u' => Match(text, Constants.U16Keyword, TokenKind.U16, Constants.U32Keyword, TokenKind.U32, Constants.U64Keyword, TokenKind.U64),
                'f' => Match(text, Constants.F32Keyword, TokenKind.F32, Constants.F64Keyword, TokenKind.F64, Constants.ForKeyword, TokenKind.For),
                'l' => Match(text, Constants.LetKeyword, TokenKind.Let),
                'v' => Match(text, Constants.VarKeyword, TokenKind.Var),
                'n' => Match(text, Constants.NotKeyword, TokenKind.Not),
                'a' => Match(text, Constants.AndKeyword, TokenKind.And),
                'g' => Match(text, Constants.GetKeyword, TokenKind.Get),
                's' => Match(text, Constants.SetKeyword, TokenKind.Set),
                'h' => Match(text, Constants.HasKeyword, TokenKind.Has),
                _ => TokenKind.Identifier,
            },
            4 => c0 switch
            {
                'b' => Match(text, Constants.BoolKeyword, TokenKind.Bool),
                'c' => Match(text, Constants.CaseKeyword, TokenKind.Case, Constants.CharKeyword, TokenKind.Char),
                'i' => Match(text, Constants.I128Keyword, TokenKind.I128),
                'u' => Match(text, Constants.U128Keyword, TokenKind.U128),
                't' => Match(text, Constants.TrueKeyword, TokenKind.True),
                'f' => Match(text, Constants.FuncKeyword, TokenKind.Func),
                'e' => Match(text, Constants.ElseKeyword, TokenKind.Else, Constants.EnumKeyword, TokenKind.Enum, Constants.ExitKeyword, TokenKind.Exit),
                'l' => Match(text, Constants.LoopKeyword, TokenKind.Loop),
                'o' => Match(text, Constants.OpenKeyword, TokenKind.Open),
                _ => TokenKind.Identifier,
            },
            5 => c0 switch
            {
                'i' => Match(text, Constants.IsizeKeyword, TokenKind.Isize),
                'u' => Match(text, Constants.UsizeKeyword, TokenKind.Usize),
                'f' => Match(text, Constants.FalseKeyword, TokenKind.False),
                'w' => Match(text, Constants.WhileKeyword, TokenKind.While),
                'm' => Match(text, Constants.MatchKeyword, TokenKind.Match),
                'y' => Match(text, Constants.YieldKeyword, TokenKind.Yield),
                'a' => Match(text, Constants.AliasKeyword, TokenKind.Alias),
                'g' => Match(text, Constants.GroupKeyword, TokenKind.Group),
                _ => TokenKind.Identifier,
            },
            6 => c0 switch
            {
                's' => Match(text, Constants.StringKeyword, TokenKind.String, Constants.StructKeyword, TokenKind.Struct, Constants.StaticKeyword, TokenKind.Static),
                'r' => Match(text, Constants.ReturnKeyword, TokenKind.Return),
                'p' => Match(text, Constants.PublicKeyword, TokenKind.Public),
                _ => TokenKind.Identifier,
            },
            7 => Match(text, Constants.PrivateKeyword, TokenKind.Private),
            8 => c0 switch
            {
                'c' => Match(text, Constants.ContinueKeyword, TokenKind.Continue, Constants.ContractKeyword, TokenKind.Contract),
                'i' => Match(text, Constants.InternalKeyword, TokenKind.Internal),
                _ => TokenKind.Identifier,
            },
            9 => c0 switch
            {
                'r' => Match(text, Constants.RootgroupKeyword, TokenKind.RootGroup),
                'e' => Match(text, Constants.ExtensionKeyword, TokenKind.Extension),
                'a' => Match(text, Constants.AssociateKeyword, TokenKind.Associate),
                'p' => Match(text, Constants.ProtectedKeyword, TokenKind.Protected),
                _ => TokenKind.Identifier,
            },
            21 => Match(text, Constants.ProtectedOrInternalKeyword, TokenKind.ProtectedOrInternal),
            22 => Match(text, Constants.ProtectedAndInternalKeyword, TokenKind.ProtectedAndInternal),
            _ => TokenKind.Identifier,
        };

        return kind;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TokenKind Match(ReadOnlySpan<char> text, string keyword, TokenKind kind)
        => text.SequenceEqual(keyword) ? kind : TokenKind.Identifier;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TokenKind Match(ReadOnlySpan<char> text, string keyword1, TokenKind kind1, string keyword2, TokenKind kind2)
        => text.SequenceEqual(keyword1) ? kind1 : text.SequenceEqual(keyword2) ? kind2 : TokenKind.Identifier;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TokenKind Match(ReadOnlySpan<char> text, string keyword1, TokenKind kind1, string keyword2, TokenKind kind2, string keyword3, TokenKind kind3)
        => text.SequenceEqual(keyword1) ? kind1 : text.SequenceEqual(keyword2) ? kind2 : text.SequenceEqual(keyword3) ? kind3 : TokenKind.Identifier;

    /// <summary>
    /// Tries to classify a single-character token and reports its grouping-depth effect.
    /// </summary>
    /// <param name="c">The character to classify.</param>
    /// <param name="tokenKind">When this method returns, contains the token kind for <paramref name="c"/>, or <see cref="TokenKind.Invalid"/>.</param>
    /// <param name="groupingDepth">When this method returns, contains +1 for an opening grouping token, -1 for a closing grouping token, or 0 for a neutral token.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is a recognized single-character token; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetSingleCharTokenKind(char c, out TokenKind tokenKind, out int groupingDepth)
    {
        (tokenKind, groupingDepth) = c switch
        {
            Constants.SharpChar => (TokenKind.Sharp, 0),
            Constants.DollarChar => (TokenKind.Dollar, 0),
            Constants.AmpersandChar => (TokenKind.Ampersand, 0),
            Constants.AsteriskChar => (TokenKind.Asterisk, 0),
            Constants.DotChar => (TokenKind.Dot, 0),
            Constants.CommaChar => (TokenKind.Comma, 0),
            Constants.OpenBracketChar => (TokenKind.OpenBracket, +1),
            Constants.CloseBracketChar => (TokenKind.CloseBracket, -1),
            Constants.OpenParenthesisChar => (TokenKind.OpenParenthesis, +1),
            Constants.CloseParenthesisChar => (TokenKind.CloseParenthesis, -1),
            Constants.OpenBraceChar => (TokenKind.OpenBrace, +1),
            Constants.CloseBraceChar => (TokenKind.CloseBrace, -1),
            Constants.ColonChar => (TokenKind.Colon, 0),
            Constants.SemicolonChar => (TokenKind.Semicolon, 0),
            Constants.BarChar => (TokenKind.Bar, 0),
            Constants.CaretChar => (TokenKind.Caret, 0),
            Constants.EqualsChar => (TokenKind.Equals, 0),
            Constants.ExclamationChar => (TokenKind.Exclamation, 0),
            Constants.GreaterThanChar => (TokenKind.GreaterThan, 0),
            Constants.LessThanChar => (TokenKind.LessThan, 0),
            Constants.MinusChar => (TokenKind.Minus, 0),
            Constants.PercentChar => (TokenKind.Percent, 0),
            Constants.PlusChar => (TokenKind.Plus, 0),
            Constants.SlashChar => (TokenKind.Slash, 0),
            Constants.QuestionChar => (TokenKind.Question, 0),
            _ => (TokenKind.Invalid, 0),
        };

        return tokenKind != TokenKind.Invalid;
    }
}
