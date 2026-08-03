// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;
using Arc.Collections;

namespace Kimi.Compiler.Lexing;

#pragma warning disable SA1202 // Elements should be ordered by access

/// <summary>
/// Provides token-related helper methods used by the Kimi tokenizer.
/// </summary>
public static partial class TokenHelper
{
    private readonly record struct TokenDescriptor(TokenKind Kind, bool IsKeyword, string Text);

    public const int MaxTokens = 256;
    private static readonly TokenDescriptor[] TokenDescriptors;

    /// <summary>
    /// Maps UTF-16 keyword spellings to their corresponding keyword token kinds.
    /// </summary>
    public static readonly Utf16Hashtable<TokenKind> KeywordToTokenKind;

    private static readonly SearchValues<char> Separators = SearchValues.Create(
    [// Characters that terminate an identifier/keyword scan.
        ' ', '\t', '\r', '\n',
        '(', ')', '{', '}', '[', ']',
        '.', ',', ';', ':', '?',
        '+', '-', '*', '/', '%',
        '&', '|', '^', '!', '~',
        '=', '<', '>', '#', '$', '@',
    ]);

    static TokenHelper()
    {
        TokenDescriptors = new TokenDescriptor[MaxTokens];

        // Invalid
        Set(TokenKind.Invalid, false, string.Empty);

        // Keywords (Primitive types)
        Set(TokenKind.Bool, true, "bool");
        Set(TokenKind.Isize, true, "isize");
        Set(TokenKind.Usize, true, "usize");
        Set(TokenKind.I8, true, "i8");
        Set(TokenKind.I16, true, "i16");
        Set(TokenKind.I32, true, "i32");
        Set(TokenKind.I64, true, "i64");
        Set(TokenKind.I128, true, "i128");
        Set(TokenKind.U8, true, "u8");
        Set(TokenKind.U16, true, "u16");
        Set(TokenKind.U32, true, "u32");
        Set(TokenKind.U64, true, "u64");
        Set(TokenKind.U128, true, "u128");
        Set(TokenKind.F32, true, "f32");
        Set(TokenKind.F64, true, "f64");
        Set(TokenKind.String, true, "string");

        // Keywords (Group)
        Set(TokenKind.Static, true, "static");
        Set(TokenKind.Public, true, "public");
        Set(TokenKind.Protected, true, "protected");
        Set(TokenKind.Private, true, "private");
        Set(TokenKind.Internal, true, "internal");
        Set(TokenKind.ProtectedOrInternal, true, "protected_or_internal");
        Set(TokenKind.ProtectedAndInternal, true, "protected_and_internal");
        Set(TokenKind.Open, true, "open");
        Set(TokenKind.Let, true, "let");
        Set(TokenKind.Var, true, "var");
        Set(TokenKind.True, true, "true");
        Set(TokenKind.False, true, "false");

        // Block keyword
        Set(TokenKind.Group, true, "group");
        Set(TokenKind.Struct, true, "struct");
        Set(TokenKind.Enum, true, "enum");
        Set(TokenKind.Extension, true, "extension");
        Set(TokenKind.Contract, true, "contract");
        Set(TokenKind.For, true, "for");
        Set(TokenKind.Loop, true, "loop");
        Set(TokenKind.Match, true, "match");

        // Block or expression keyword
        Set(TokenKind.If, true, "if");
        Set(TokenKind.Else, true, "else");
        Set(TokenKind.Block, true, "block");
        Set(TokenKind.As, true, "as");
        Set(TokenKind.Is, true, "is");
        Set(TokenKind.Not, true, "not");
        Set(TokenKind.And, true, "and");
        Set(TokenKind.Or, true, "or");

        // Non-block keyword
        Set(TokenKind.RootGroup, true, Constants.RootgroupKeyword);
        Set(TokenKind.Return, true, "return");
        Set(TokenKind.Break, true, "break");
        Set(TokenKind.Continue, true, "continue");
        Set(TokenKind.Yield, true, "yield");

        // Not keyword
        Set(TokenKind.Identifier, false, string.Empty);
        Set(TokenKind.StartBlock, false, string.Empty);
        Set(TokenKind.EndBlock, false, string.Empty);
        Set(TokenKind.Separator, false, string.Empty);
        Set(TokenKind.NumericLiteral, false, string.Empty);
        Set(TokenKind.StringLiteral, false, string.Empty);
        Set(TokenKind.RawStringLiteral, false, string.Empty);
        Set(TokenKind.SingleLineComment, false, string.Empty);
        Set(TokenKind.MultiLineComment, false, string.Empty);

        // Single token
        Set(TokenKind.Sharp, false, "#");
        Set(TokenKind.Dollar, false, "$");
        Set(TokenKind.At, false, "@");
        Set(TokenKind.Comma, false, ",");
        Set(TokenKind.OpenBracket, false, "[");
        Set(TokenKind.CloseBracket, false, "]");
        Set(TokenKind.OpenParenthesis, false, "(");
        Set(TokenKind.CloseParenthesis, false, ")");
        Set(TokenKind.OpenBrace, false, "{");
        Set(TokenKind.CloseBrace, false, "}");
        Set(TokenKind.Colon, false, ":");
        Set(TokenKind.Semicolon, false, ";");
        Set(TokenKind.Question, false, "?");

        // Others
        Set(TokenKind.Ampersand, false, "&");
        Set(TokenKind.AmpersandAmpersand, false, "&&");
        Set(TokenKind.AmpersandEquals, false, "&=");
        Set(TokenKind.Asterisk, false, "*");
        Set(TokenKind.AsteriskEquals, false, "*=");
        Set(TokenKind.Bar, false, "|");
        Set(TokenKind.BarBar, false, "||");
        Set(TokenKind.BarEquals, false, "|=");
        Set(TokenKind.Caret, false, "^");
        Set(TokenKind.CaretEquals, false, "^=");
        Set(TokenKind.Dot, false, ".");
        Set(TokenKind.DotDot, false, "..");
        Set(TokenKind.DotDotEquals, false, "..=");
        Set(TokenKind.Equals, false, "=");
        Set(TokenKind.EqualsEquals, false, "==");
        Set(TokenKind.EqualsGreaterThan, false, "=>");
        Set(TokenKind.Exclamation, false, "!");
        Set(TokenKind.ExclamationEquals, false, "!=");
        Set(TokenKind.GreaterThan, false, ">");
        Set(TokenKind.GreaterThanEquals, false, ">=");
        Set(TokenKind.GreaterThanGreaterThan, false, ">>");
        Set(TokenKind.GreaterThanGreaterThanEquals, false, ">>=");
        Set(TokenKind.LessThan, false, "<");
        Set(TokenKind.LessThanEquals, false, "<=");
        Set(TokenKind.LessThanLessThan, false, "<<");
        Set(TokenKind.LessThanLessThanEquals, false, "<<=");
        Set(TokenKind.Minus, false, "-");
        Set(TokenKind.MinusEquals, false, "-=");
        Set(TokenKind.MinusMinus, false, "--");
        Set(TokenKind.Percent, false, "%");
        Set(TokenKind.PercentEquals, false, "%=");
        Set(TokenKind.Plus, false, "+");
        Set(TokenKind.PlusEquals, false, "+=");
        Set(TokenKind.PlusPlus, false, "++");
        Set(TokenKind.Slash, false, "/");
        Set(TokenKind.SlashEquals, false, "/=");

        KeywordToTokenKind = new();
        foreach (var x in TokenDescriptors)
        {
            if (x.IsKeyword)
            {
                KeywordToTokenKind.TryAdd(x.Text, x.Kind);
            }
        }

        static void Set(TokenKind kind, bool isKeyword, string text)
        {
            TokenDescriptors[(int)kind] = new TokenDescriptor(kind, isKeyword, text);
        }
    }

    public static string ToText(this TokenKind tokenKind)
    {
        return TokenDescriptors[(int)tokenKind].Text;
    }

    public static bool IsIdentifierToken(this Token token, ReadOnlySpan<char> identifier)
        => token.Kind == TokenKind.Identifier && token.Text.Span.SequenceEqual(identifier);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="text"/> starts with '"'.<br/>
    /// <paramref name="length"/> is the total literal length (including the delimiters) when the literal is
    /// well-formed, or -1 when the literal is unterminated/invalid; in that case the caller is responsible for
    /// consuming the rest of the line (or the rest of the text) as an Invalid token.<br/>
    /// <paramref name="quoteCount"/> is the number of consecutive leading quotes (&gt;= 3 means a raw string literal,
    /// which may span multiple lines).
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <param name="length">When this method returns, contains the literal length, or -1 if the literal is unterminated or invalid.</param>
    /// <param name="quoteCount">When this method returns, contains the number of consecutive leading quote characters.</param>
    /// <returns><see langword="true"/> if <paramref name="text"/> starts with a string literal delimiter; otherwise, <see langword="false"/>.</returns>
    public static bool StartsWithStringLiteral(ReadOnlySpan<char> text, out int length, out int quoteCount)
    {
        length = 0;
        quoteCount = 0;
        if (text.IsEmpty || text[0] != '"')
        {
            return false;
        }

        quoteCount = CountQuotesAt(text, 0);
        if (quoteCount >= 3)
        {
            TryGetRawStringLiteralLength(text, quoteCount, out length);
        }
        else
        {
            TryGetRegularStringLiteralLength(text, out length);
        }

        return true;
    }

    /// <summary>
    /// Finds the first tokenizer separator in the specified text.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <returns>The zero-based index of the first separator, or -1 if no separator is found.</returns>
    public static int IndexOfSeparator(ReadOnlySpan<char> text)
        => text.IndexOfAny(Separators);

    /// <summary>
    /// NOTE: Relies on TokenKind.Group..TokenKind.Match being a contiguous range.
    /// Keep this in sync with the TokenKind declaration.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="tokenKind"/> is a block-starting token; otherwise, <see langword="false"/>.</returns>
    public static bool IsBlockToken(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Group && tokenKind <= TokenKind.Match;

    public static bool IsPrimitiveType(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Bool && tokenKind <= TokenKind.String;

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
            // Constants.AtChar => (TokenKind.At, 0),
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

    private static void TryGetRegularStringLiteralLength(ReadOnlySpan<char> text, out int length)
    {
        // The caller has already verified that the first character is '"'.
        var i = 1;
        while (i < text.Length)
        {
            var c = text[i];

            // A regular string literal cannot contain a physical line break.
            if (c == '\r' || c == '\n')
            {
                length = -1;
                return;
            }

            // Skip escaped character, such as \" or \\.
            if (c == '\\')
            {
                i++;
                if (i >= text.Length)
                {
                    length = -1;
                    return;
                }

                i++;
                continue;
            }

            // Closing quote.
            if (c == '"')
            {
                length = i + 1;
                return;
            }

            i++;
        }

        length = -1;
    }

    private static void TryGetRawStringLiteralLength(ReadOnlySpan<char> text, int delimiterQuoteCount, out int length)
    {
        // Raw string literals use at least three quotes as the delimiter.
        var i = delimiterQuoteCount;
        while (i < text.Length)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            var quoteCount = CountQuotesAt(text, i);

            // The closing delimiter must have at least the same number of quotes
            // as the opening delimiter. When the run is longer than the delimiter,
            // the entire run is consumed: the last delimiterQuoteCount quotes close
            // the literal and the preceding quotes belong to the content.
            // e.g. """abc""""  => content is [abc"], with no stray '"' left behind.
            if (quoteCount >= delimiterQuoteCount)
            {
                length = i + quoteCount;
                return;
            }

            i += quoteCount;
        }

        length = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountQuotesAt(ReadOnlySpan<char> text, int start)
    {
        var i = start;
        while (i < text.Length && text[i] == '"')
        {
            i++;
        }

        return i - start;
    }
}
