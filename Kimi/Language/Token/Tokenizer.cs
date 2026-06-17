// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

/// <summary>
/// Provides token-related helper methods used by the Kimigayo tokenizer.
/// </summary>
public static class TokenHelper
{
    /// <summary>
    /// Gets a read-only mapping from keyword token kinds to their keyword spellings.
    /// </summary>
    public static IReadOnlyDictionary<TokenKind, string> KeywordKindToKeyword => _keywordKindToKeyword;

    /// <summary>
    /// Maps UTF-16 keyword spellings to their corresponding keyword token kinds.
    /// </summary>
    public static readonly Utf16Hashtable<TokenKind> KeywordToKeywordKind;

    private static readonly FrozenDictionary<TokenKind, string> _keywordKindToKeyword;

    private static readonly SearchValues<char> Separators = SearchValues.Create(
    [// Characters that terminate an identifier/keyword scan.
        ' ', '\t', '\r', '\n',
        '(', ')', '{', '}', '[', ']',
        '.', ',', ';', ':', '?',
        '+', '-', '*', '/', '%',
        '&', '|', '^', '!', '~',
        '=', '<', '>', '#', '$',
    ]);

    static TokenHelper()
    {
        // NOTE: This relies on the layout of TokenKind:
        //   - TokenKind.Invalid comes first.
        //   - All keyword kinds are placed between Invalid and Identifier.
        //   - The enum member name, lower-cased, is the keyword spelling.
        // If TokenKind is reordered, this initializer silently breaks, so keep the
        // contract documented on the TokenKind declaration as well.
        var dic = new Dictionary<TokenKind, string>();
        KeywordToKeywordKind = new();
        foreach (var x in Enum.GetValues<TokenKind>())
        {
            if (x == TokenKind.Invalid)
            {
                continue;
            }
            else if (x == TokenKind.Identifier)
            {// Anything after TokenKind.Identifier is not a keyword.
                break;
            }

            var keyword = x.ToString().ToLowerInvariant();
            dic[x] = keyword;
            KeywordToKeywordKind.TryAdd(keyword, x);
        }

        Debug.Assert(dic.Count > 0, "No keywords were generated. Check the layout of TokenKind.");
        _keywordKindToKeyword = dic.ToFrozenDictionary();
    }

    public static bool IsIdentifierToken(this Token token, ReadOnlySpan<char> identifier)
        => token.Kind == TokenKind.Identifier && token.Text.Span.SequenceEqual(identifier);

    /// <summary>
    /// Determines whether the specified statement context represents a group-like declaration.
    /// </summary>
    /// <param name="statementContext">The statement context to inspect.</param>
    /// <returns><see langword="true"/> if the context represents a namespace, group, struct, or enum; otherwise, <see langword="false"/>.</returns>
    public static bool IsGroup(this StatementContext statementContext) => statementContext switch
    {
        StatementContext.Namespace => true,
        StatementContext.Group => true,
        StatementContext.Struct => true,
        StatementContext.Enum => true,
        _ => false,
    };

    /// <summary>
    /// Scans a numeric literal at the start of <paramref name="text"/>.<br/>
    /// Returns <see langword="true"/> when a valid literal was found; <paramref name="length"/> is its length.<br/>
    /// Returns <see langword="false"/> with <paramref name="length"/> == 0 when the text does not start with a digit.<br/>
    /// Returns <see langword="false"/> with <paramref name="length"/> &gt; 0 when the text starts with a digit but does not
    /// form a valid literal (e.g. "0x", "1e+", "1.0u8", "123abc"); <paramref name="length"/> then covers the malformed
    /// literal so that the caller can emit a single Invalid token with a diagnostic.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="length">When this method returns, contains the number of characters consumed by the literal or malformed literal.</param>
    /// <returns><see langword="true"/> if a valid numeric literal was found; otherwise, <see langword="false"/>.</returns>
    public static bool ScanNumberLiteral(ReadOnlySpan<char> text, out int length)
    {
        length = 0;
        if (text.IsEmpty || !IsDigit(text[0]))
        {
            return false;
        }

        if (text.Length >= 2 && text[0] == '0')
        {// 0b..., 0o..., 0x...
            var p = (char)(text[1] | 0x20);
            if (p == 'b')
            {
                return ScanBasedInteger(text, 2, 2, out length);
            }

            if (p == 'o')
            {
                return ScanBasedInteger(text, 2, 8, out length);
            }

            if (p == 'x')
            {
                return ScanBasedInteger(text, 2, 16, out length);
            }
        }

        // Decimal integer part.
        var i = ScanDecDigitsOrUnderscores(text, 0);
        var isFloat = false;

        // Fraction part.
        // 1.0  => float
        // 1.   => integer + dot
        // 1..2 => integer literal "1"
        // 1.foo => integer literal "1"
        if ((uint)i < (uint)text.Length && text[i] == '.')
        {
            if (i + 1 < text.Length && IsDigit(text[i + 1]))
            {
                isFloat = true;
                i++;
                i = ScanDecDigitsOrUnderscores(text, i);
            }
        }

        // Exponent part.
        if ((uint)i < (uint)text.Length)
        {
            var c = text[i];
            if ((c | 0x20) == 'e')
            {
                i++;
                if ((uint)i < (uint)text.Length)
                {
                    c = text[i];
                    if (c == '+' || c == '-')
                    {
                        i++;
                    }
                }

                var hasDigit = false;
                while ((uint)i < (uint)text.Length)
                {
                    c = text[i];

                    if (IsDigit(c))
                    {
                        hasDigit = true;
                        i++;
                        continue;
                    }

                    if (c == '_')
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                if (!hasDigit)
                {// e.g. "1e", "1e+", "1ex"
                    length = ExtendWithIdentifierContinue(text, i);
                    return false;
                }

                isFloat = true;
            }
        }

        var suffixLength = ScanSuffix(text.Slice(i), isFloat);
        if (suffixLength < 0)
        {// e.g. "1.0u8", "1f16"
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        i += suffixLength;

        if ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {// e.g. "1u8x", "123abc"
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        length = i;
        return true;
    }

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
    /// Determines whether the specified character is an ASCII decimal digit.
    /// </summary>
    /// <param name="c">The character to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is in the range '0' through '9'; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(char c)
    {
        return (uint)(c - '0') <= 9;
    }

    /// <summary>
    /// Determines whether the specified character is an ASCII decimal digit or an underscore separator.
    /// </summary>
    /// <param name="c">The character to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is a digit or '_'; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigitOrNumericSeparator(char c)
    {
        return IsDigit(c) || c == '_';
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
            Constants.DotChar => (TokenKind.Dot, 0),
            Constants.CommaChar => (TokenKind.Comma, 0),
            Constants.SharpChar => (TokenKind.Sharp, 0),
            Constants.OpenBracketChar => (TokenKind.OpenBracket, +1),
            Constants.CloseBracketChar => (TokenKind.CloseBracket, -1),
            Constants.OpenParenthesisChar => (TokenKind.OpenParenthesis, +1),
            Constants.CloseParenthesisChar => (TokenKind.CloseParenthesis, -1),
            Constants.OpenBraceChar => (TokenKind.OpenBrace, +1),
            Constants.CloseBraceChar => (TokenKind.CloseBrace, -1),
            Constants.ColonChar => (TokenKind.Colon, 0),
            Constants.SemicolonChar => (TokenKind.Semicolon, 0),
            Constants.DollarChar => (TokenKind.Dollar, 0),
            Constants.TildeChar => (TokenKind.Tilde, 0),
            Constants.AmpersandChar => (TokenKind.Ampersand, 0),
            Constants.AsteriskChar => (TokenKind.Asterisk, 0),
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

    private static bool ScanBasedInteger(ReadOnlySpan<char> text, int start, int numberBase, out int length)
    {
        var i = start;
        var hasDigit = false;
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];
            if (c == '_')
            {
                i++;
                continue;
            }

            if (numberBase == 2)
            {
                if (c != '0' && c != '1')
                {
                    break;
                }
            }
            else if (numberBase == 8)
            {
                if ((uint)(c - '0') > 7)
                {
                    break;
                }
            }
            else
            {// Hex
                if ((uint)(c - '0') > 9 && (uint)((c | 0x20) - 'a') > 5)
                {
                    break;
                }
            }

            hasDigit = true;
            i++;
        }

        if (!hasDigit)
        {// e.g. "0x", "0b2"
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        var suffixLength = ScanSuffix(text.Slice(i), isFloat: false);
        if (suffixLength < 0)
        {
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        i += suffixLength;
        if ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {// e.g. "0x1g", "0b01u8x"
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        length = i;
        return true;
    }

    private static int ScanDecDigitsOrUnderscores(ReadOnlySpan<char> text, int i)
    {
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];

            if (IsDigit(c) || c == '_')
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    /// <summary>
    /// Extends <paramref name="i"/> over any trailing identifier-continue characters so that a malformed
    /// numeric literal is reported as a single token (e.g. "1.0u8" instead of "1" + "." + "0u8").
    /// </summary>
    /// <param name="text">The text that contains the malformed numeric literal.</param>
    /// <param name="i">The first character position to test for identifier continuation.</param>
    /// <returns>The first index after the trailing identifier-continue sequence.</returns>
    private static int ExtendWithIdentifierContinue(ReadOnlySpan<char> text, int i)
    {
        while ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {
            i++;
        }

        return i;
    }

    private static int ScanSuffix(ReadOnlySpan<char> text, bool isFloat)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        var c0 = text[0];

        if (isFloat)
        {
            if (text.Length >= 3 &&
                c0 == 'f' &&
                ((text[1] == '3' && text[2] == '2') ||
                (text[1] == '6' && text[2] == '4')))
            {
                return 3; // f32 / f64
            }

            return IsIdentifierStart(c0) ? -1 : 0;
        }

        if (text.Length >= 2 &&
            (c0 == 'u' || c0 == 'i') &&
            text[1] == '8')
        {// u8 / i8
            return 2;
        }

        if (text.Length >= 3 &&
            (c0 == 'u' || c0 == 'i'))
        {// u16 / i16 / u32 / i32 / u64 / i64
            var c1 = text[1];
            var c2 = text[2];
            if ((c1 == '1' && c2 == '6') ||
                (c1 == '3' && c2 == '2') ||
                (c1 == '6' && c2 == '4'))
            {
                return 3;
            }
        }

        // u128 / i128
        if (text.Length >= 4 &&
            (c0 == 'u' || c0 == 'i') &&
            text[1] == '1' &&
            text[2] == '2' &&
            text[3] == '8')
        {
            return 4;
        }

        // usize / isize
        if (text.Length >= 5 &&
            (c0 == 'u' || c0 == 'i') &&
            text[1] == 's' &&
            text[2] == 'i' &&
            text[3] == 'z' &&
            text[4] == 'e')
        {
            return 5;
        }

        return IsIdentifierStart(c0) ? -1 : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierStart(char c)
    {
        return c == '_' || (uint)(c - 'A') <= 25 || (uint)(c - 'a') <= 25 || c >= 0x80;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierContinue(char c)
    {
        return IsIdentifierStart(c) || IsDigit(c);
    }
}

/// <summary>
/// Converts Kimigayo source text into a sequence of lexical tokens and indentation tokens.
/// </summary>
internal sealed class Tokenizer
{
    private enum IndentSource : byte
    {
        Block,
        Parenthesis, // ()
        Bracket, // []
        AngleBracket, // <>: Not supported yet because distinguishing generics from comparison operators is difficult.
        Brace, // {}
        LineContinuation, // Implicit continuation, such as a method chain line starting with ".".
    }

    #region FieldAndProperty

    private readonly UrlDiagnostic urlDiagnostic;
    private readonly List<Token> tokenList = new();
    private readonly Stack<IndentSource> indentStack = new();

    private ReadOnlyMemory<char> text;
    private int position;
    private int line;
    private int character;

    private int blockDepth;
    private int nonBlockDepth;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="Tokenizer"/> class.
    /// </summary>
    /// <param name="urlDiagnostic">The diagnostic sink used to report lexical errors.</param>
    public Tokenizer(UrlDiagnostic urlDiagnostic)
    {
        this.urlDiagnostic = urlDiagnostic;
    }

    /// <summary>
    /// Resets the tokenizer to read from the specified text and source position.
    /// </summary>
    /// <param name="text">The source text to tokenize.</param>
    /// <param name="line">The initial zero-based line number.</param>
    /// <param name="character">The initial zero-based character position.</param>
    public void Initialize(ReadOnlyMemory<char> text, int line, int character)
    {
        this.text = text;
        this.position = 0;
        this.line = line;
        this.character = character;

        this.ClearState();
    }

    /// <summary>
    /// Reads the next logical line and returns its tokens.<br/>
    /// NOTE: The returned list is an internal buffer that is cleared and reused by the next call to
    /// <see cref="Read"/> or <see cref="Initialize"/>. Callers must consume (or copy) the tokens before
    /// invoking this tokenizer again.
    /// </summary>
    /// <param name="currentIndentLevel">The current logical indentation level. The value is updated as blocks are opened or closed.</param>
    /// <returns>The internal token buffer containing the tokens read for the next logical line.</returns>
    public List<Token> Read(ref int currentIndentLevel)
    {
        this.ClearState();

Loop:
        var span = this.text.Slice(this.position).Span;
        if (span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }

        if (span[0] == Constants.SpaceChar)
        {// If whitespace is present, process it first.
            goto MeasureIndentation;
        }

        while (span.Length > 0)
        {
            while (span[0] == Constants.SpaceChar)
            {// Skip spaces
                this.Slice(ref span, 1);
                if (span.Length == 0)
                {// End-of-file
                    goto EndOfFile;
                }
            }

            // span.Length >= 1
            switch (span[0])
            {
                case Constants.CrChar:
                    if (span.Length > 1 && span[1] == Constants.LfChar)
                    {// \r\n
                        this.Slice(ref span, 2);
                        this.NextLine();
                        goto NextLine;
                    }
                    else
                    {
                        this.Slice(ref span, 1);
                        this.NextLine();
                        goto NextLine;
                    }

                case Constants.LfChar: // \n
                    this.Slice(ref span, 1);
                    this.NextLine();
                    goto NextLine;

                case Constants.AmpersandChar: // && &= &
                    if (span.Length == 1)
                    {// &
                        this.AddTokenAndSlice(TokenKind.Ampersand, ref span, 1);
                    }
                    else if (span[1] == Constants.AmpersandChar)
                    {// &&
                        this.AddTokenAndSlice(TokenKind.AmpersandAmpersand, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// &=
                        this.AddTokenAndSlice(TokenKind.AmpersandEquals, ref span, 2);
                    }
                    else
                    {// &
                        this.AddTokenAndSlice(TokenKind.Ampersand, ref span, 1);
                    }

                    break;

                case Constants.AsteriskChar: // * *=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// *=
                        this.AddTokenAndSlice(TokenKind.AsteriskEquals, ref span, 2);
                    }
                    else
                    {// *
                        this.AddTokenAndSlice(TokenKind.Asterisk, ref span, 1);
                    }

                    break;

                case Constants.BarChar: // | || |=
                    if (span.Length == 1)
                    {// |
                        this.AddTokenAndSlice(TokenKind.Bar, ref span, 1);
                    }
                    else if (span[1] == Constants.BarChar)
                    {// ||
                        this.AddTokenAndSlice(TokenKind.BarBar, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// |=
                        this.AddTokenAndSlice(TokenKind.BarEquals, ref span, 2);
                    }
                    else
                    {// |
                        this.AddTokenAndSlice(TokenKind.Bar, ref span, 1);
                    }

                    break;

                case Constants.CaretChar: // ^ ^=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// ^=
                        this.AddTokenAndSlice(TokenKind.CaretEquals, ref span, 2);
                    }
                    else
                    {// ^
                        this.AddTokenAndSlice(TokenKind.Caret, ref span, 1);
                    }

                    break;

                case Constants.DotChar: // . .. ..=
                    if (span.Length == 1)
                    {// .
                        this.AddTokenAndSlice(TokenKind.Dot, ref span, 1);
                    }
                    else if (span[1] == Constants.DotChar)
                    {// ..
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// ..=
                            this.AddTokenAndSlice(TokenKind.DotDotEquals, ref span, 3);
                        }
                        else
                        {// ..
                            this.AddTokenAndSlice(TokenKind.DotDot, ref span, 2);
                        }
                    }
                    else
                    {// .
                        this.AddTokenAndSlice(TokenKind.Dot, ref span, 1);
                    }

                    break;

                case Constants.EqualsChar: // = == =>
                    if (span.Length == 1)
                    {// =
                        this.AddTokenAndSlice(TokenKind.Equals, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// ==
                        this.AddTokenAndSlice(TokenKind.EqualsEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// =>
                        this.AddTokenAndSlice(TokenKind.EqualsGreaterThan, ref span, 2);
                    }
                    else
                    {// =
                        this.AddTokenAndSlice(TokenKind.Equals, ref span, 1);
                    }

                    break;

                case Constants.ExclamationChar: // ! !=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// !=
                        this.AddTokenAndSlice(TokenKind.ExclamationEquals, ref span, 2);
                    }
                    else
                    {// !
                        this.AddTokenAndSlice(TokenKind.Exclamation, ref span, 1);
                    }

                    break;

                case Constants.GreaterThanChar: // > >= >> >>=
                    if (span.Length == 1)
                    {// >
                        this.AddTokenAndSlice(TokenKind.GreaterThan, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// >=
                        this.AddTokenAndSlice(TokenKind.GreaterThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// >>
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// >>=
                            this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThanEquals, ref span, 3);
                        }
                        else
                        {// >>
                            this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThan, ref span, 2);
                        }
                    }
                    else
                    {// >
                        this.AddTokenAndSlice(TokenKind.GreaterThan, ref span, 1);
                    }

                    break;

                case Constants.LessThanChar: // < <= << <<=
                    if (span.Length == 1)
                    {// <
                        this.AddTokenAndSlice(TokenKind.LessThan, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// <=
                        this.AddTokenAndSlice(TokenKind.LessThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.LessThanChar)
                    {// <<
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// <<=
                            this.AddTokenAndSlice(TokenKind.LessThanLessThanEquals, ref span, 3);
                        }
                        else
                        {// <<
                            this.AddTokenAndSlice(TokenKind.LessThanLessThan, ref span, 2);
                        }
                    }
                    else
                    {// <
                        this.AddTokenAndSlice(TokenKind.LessThan, ref span, 1);
                    }

                    break;

                case Constants.MinusChar: // -- -= -
                    if (span.Length == 1)
                    {// -
                        this.AddTokenAndSlice(TokenKind.Minus, ref span, 1);
                    }
                    else if (span[1] == Constants.MinusChar)
                    {// --
                        this.AddTokenAndSlice(TokenKind.MinusMinus, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// -=
                        this.AddTokenAndSlice(TokenKind.MinusEquals, ref span, 2);
                    }
                    else
                    {// -
                        this.AddTokenAndSlice(TokenKind.Minus, ref span, 1);
                    }

                    break;

                case Constants.PercentChar: // % %=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// %=
                        this.AddTokenAndSlice(TokenKind.PercentEquals, ref span, 2);
                    }
                    else
                    {// %
                        this.AddTokenAndSlice(TokenKind.Percent, ref span, 1);
                    }

                    break;

                case Constants.PlusChar: // ++ += +
                    if (span.Length == 1)
                    {// +
                        this.AddTokenAndSlice(TokenKind.Plus, ref span, 1);
                    }
                    else if (span[1] == Constants.PlusChar)
                    {// ++
                        this.AddTokenAndSlice(TokenKind.PlusPlus, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// +=
                        this.AddTokenAndSlice(TokenKind.PlusEquals, ref span, 2);
                    }
                    else
                    {// +
                        this.AddTokenAndSlice(TokenKind.Plus, ref span, 1);
                    }

                    break;

                case Constants.SlashChar: // // /* /= /
                    if (span.Length == 1)
                    {// /
                        this.AddTokenAndSlice(TokenKind.Slash, ref span, 1);
                    }
                    else if (span[1] == Constants.SlashChar)
                    {// //
                        if (this.ReadSingleLineComment(ref span))
                        {
                            this.NextLine();
                        }

                        goto NextLine;
                    }
                    else if (span[1] == Constants.AsteriskChar)
                    {// /*
                        this.ReadMultiLineComment(ref span);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// /=
                        this.AddTokenAndSlice(TokenKind.SlashEquals, ref span, 2);
                    }
                    else
                    {// /
                        this.AddTokenAndSlice(TokenKind.Slash, ref span, 1);
                    }

                    break;

                default:
                    {
                        if (TokenHelper.TryGetSingleCharTokenKind(span[0], out var tokenKind, out var depth))
                        {// Single char token
                            if (depth > 0)
                            {
                                this.PushIndentSource(tokenKind);
                            }
                            else if (depth < 0)
                            {
                                this.PopIndentSource(tokenKind);
                            }

                            this.AddTokenAndSlice(tokenKind, ref span, 1);
                            continue;
                        }

                        if (TokenHelper.ScanNumberLiteral(span, out var numberLiteralLength))
                        {// Numeric literal
                         // If the current position starts a numeric literal, scan the entire numeric literal before checking separators.
                            this.AddTokenAndSlice(TokenKind.NumericLiteral, ref span, numberLiteralLength);
                        }
                        else if (numberLiteralLength > 0)
                        {// Starts with a digit but is not a valid numeric literal (e.g. "0x", "1e+", "1.0u8", "123abc").
                         // Emit a single Invalid token with a diagnostic instead of silently falling back
                         // to the identifier path, which would produce bogus Identifier tokens.
                            this.urlDiagnostic.Add(this.NewRange(numberLiteralLength), Hashed.Kimi.InvalidNumericLiteral);
                            this.AddTokenAndSlice(TokenKind.Invalid, ref span, numberLiteralLength);
                        }
                        else if (TokenHelper.StartsWithStringLiteral(span, out var literalLength, out var quoteCount))
                        {// String literal
                            if (literalLength < 0)
                            {// Invalid literal
                                var invalidLength = Arc.BaseHelper.IndexOfLfOrCrLf(span, out _);
                                if (invalidLength < 0)
                                {
                                    invalidLength = span.Length;
                                }

                                this.urlDiagnostic.Add(this.NewRange(1), Hashed.Kimi.MissingStringLiteralEnd);

                                if (quoteCount >= 3)
                                {// An unterminated raw string literal may contain line breaks.
                                    this.AddTokenAndSliceWithLineTracking(TokenKind.Invalid, ref span, invalidLength);
                                }
                                else
                                {
                                    this.AddTokenAndSlice(TokenKind.Invalid, ref span, invalidLength);
                                }
                            }
                            else if (quoteCount >= 3)
                            {// Raw string literal: may span multiple lines, so track line breaks.
                                this.AddTokenAndSliceWithLineTracking(TokenKind.Literal, ref span, literalLength);
                            }
                            else
                            {// Regular string literal (quoteCount is 1 or 2; "" is an empty literal).
                                this.AddTokenAndSlice(TokenKind.Literal, ref span, literalLength);
                            }
                        }
                        else
                        {// Keyword or Identifier
                            var length = TokenHelper.IndexOfSeparator(span);
                            if (length < 0)
                            {
                                length = span.Length;
                            }
                            else if (length == 0)
                            {
                                this.urlDiagnostic.Add(this.NewRange(1), Hashed.Kimi.InvalidCharacter, span[0]);
                                this.AddTokenAndSlice(TokenKind.Invalid, ref span, 1);
                                break;
                            }

                            if (TokenHelper.KeywordToKeywordKind.TryGetValue(span.Slice(0, length), out var tokenKind2))
                            {// Keyword
                                this.AddTokenAndSlice(tokenKind2, ref span, length);
                            }
                            else
                            {// Identifier
                                this.AddTokenAndSlice(TokenKind.Identifier, ref span, length);
                            }
                        }

                        break;
                    }
            }
        }

NextLine:
        if (span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }
        else if (this.tokenList.Count == 0)
        {// If text remains but no token was found, such as on a blank line, retry processing.
            goto Loop;
        }

MeasureIndentation:
// Indentation is measured once, at the physical line start.
// Comments that follow do not change it.
        var numberOfSpaces = Arc.BaseHelper.CountLeadingSpaces(span);
        this.Slice(ref span, numberOfSpaces);

LineContent:
        if (span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }

        if (span[0] == Constants.LfChar)
        {// Empty line (\n)
            this.Slice(ref span, 1);
            this.NextLine();
            goto NextLine;
        }
        else if (span[0] == Constants.CrChar)
        {// Empty line (\r\n or \r)
            this.Slice(ref span, span.Length > 1 && span[1] == Constants.LfChar ? 2 : 1);
            this.NextLine();
            goto NextLine;
        }
        else if (span.Length >= 2 && span[0] == Constants.SlashChar)
        {// /
            if (span[1] == Constants.SlashChar)
            {// // Single line comment
                if (this.ReadSingleLineComment(ref span))
                {
                    this.NextLine();
                }

                goto NextLine;
            }
            else if (span[1] == Constants.AsteriskChar)
            {// /* Multi line comment */
                _ = this.ReadMultiLineComment(ref span);

                // Skip spaces after the comment WITHOUT counting them as indentation;
                // the indentation of this line was already measured at the line start
                // (this prevents a bogus InvalidIndentation diagnostic for "/* c */ foo").
                // If the comment spanned multiple physical lines, code following the
                // closing "*/" inherits the indentation of the line that opened it.
                this.Slice(ref span, Arc.BaseHelper.CountLeadingSpaces(span));
                goto LineContent;
            }
        }

        var unnecessarySpaces = numberOfSpaces % Constants.IndentationSpaces;
        if (unnecessarySpaces > 0)
        {// Invalid indentation
            this.urlDiagnostic.Add(new(new(this.line, 0), new(this.line, numberOfSpaces)), Hashed.Kimi.InvalidIndentation, Constants.IndentationSpaces);
            numberOfSpaces += Constants.IndentationSpaces - unnecessarySpaces;
        }

        var indentLevel = numberOfSpaces / Constants.IndentationSpaces;
        if (currentIndentLevel < 0)
        {
            currentIndentLevel = indentLevel;
        }

        // Indentation remains significant even inside grouping constructs.
        // Therefore, both block depth and non-block depth are subtracted when
        // calculating the indentation difference.
        var dif = indentLevel - currentIndentLevel - this.blockDepth - this.nonBlockDepth;
        if (dif > 0)
        {
            if (dif == 1)
            {
                // A line that starts with "." is treated as a continuation of the previous
                // expression. It contributes one required indentation level, like grouping
                // constructs, but does not require an explicit closing token.
                if (span.Length > 0 && span[0] == Constants.DotChar)
                {// Method chain
                    this.PushIndentSource(IndentSource.LineContinuation);
                    goto Loop;
                }
                else if (span.Length > 1 && span[0] == Constants.EqualsChar && span[1] == Constants.GreaterThanChar)
                {// =>
                    goto Loop;
                }
            }

            // TODO: Consider reporting Hashed.Kimi.UnexpectedIndent when dif > 1.
            for (var i = 0; i < dif; i++)
            {
                this.AddToken(new(TokenKind.StartBlock, default));
                this.PushIndentSource(IndentSource.Block);
            }
        }

        // When indentation decreases inside a grouping construct, the current token
        // may be the matching closing delimiter placed at the outer indentation level.
        // In that case, consume the closing token and close the grouping context.
        // Otherwise, keep the grouping context open and report an indentation mismatch.
        else if (dif < 0)
        {
            for (var i = dif; i < 0; i++)
            {
                if (this.indentStack.TryPop(out var indentSource))
                {
                    if (indentSource == IndentSource.Block)
                    {
                        this.AddToken(new(TokenKind.EndBlock, default));
                        this.blockDepth--;
                    }
                    else if (indentSource == IndentSource.LineContinuation)
                    {
                        this.nonBlockDepth--;
                        continue;
                    }
                    else if (this.TryCloseIndentSourceByCurrentToken(indentSource, ref span))
                    {
                        // Treat only an immediate member access after an outer-indented closing
                        // delimiter as part of the same logical line.
                        //
                        // Example:
                        //     foo(
                        //         a
                        //     ).bar
                        //
                        // Other cases, such as ") + 1" or a "." on the next physical line, are not
                        // continued here.
                        if (!span.IsEmpty && span[0] == Constants.DotChar)
                        {
                            goto Loop;
                        }

                        continue;
                    }
                    else
                    {
                        this.indentStack.Push(indentSource);
                        this.urlDiagnostic.Add(new(new(this.line, 0), new(this.line, numberOfSpaces)), Hashed.Kimi.IndentationLevelMismatch);
                        break;
                    }
                }
                else if (currentIndentLevel > 0)
                {
                    this.AddToken(new(TokenKind.EndBlock, default));
                    currentIndentLevel--;
                }
                else
                {
                    this.urlDiagnostic.Add(new(new(this.line, 0), new(this.line, numberOfSpaces)), Hashed.Kimi.IndentationLevelMismatch);
                    break;
                }
            }
        }

        if (this.nonBlockDepth > 0)
        {
            if (dif == 0 && this.indentStack.Peek() == IndentSource.Block)
            {
                this.AddToken(new(TokenKind.Separator, default));
            }

            goto Loop;
        }
        else
        {
            currentIndentLevel += this.blockDepth;
            this.blockDepth = 0;

            return this.tokenList;
        }

EndOfFile:
        this.ClearIndentStack();
        while (currentIndentLevel-- > 0)
        {
            this.AddToken(new(TokenKind.EndBlock, default));
        }

        Debug.Assert(this.blockDepth == 0);
        Debug.Assert(this.nonBlockDepth == 0);

        return this.tokenList;
    }

    private int ReadMultiLineComment(ref ReadOnlySpan<char> text)
    {
        var length = text.IndexOf("*/");
        if (length < 0)
        {
            this.urlDiagnostic.Add(new(new(this.line, this.character), new(this.line, this.character + 2)), Hashed.Kimi.MissingBlockCommentEnd);

            return this.AddTokenAndSliceWithLineTracking(TokenKind.Invalid, ref text, text.Length);
        }

        length += 2;
        return this.AddTokenAndSliceWithLineTracking(TokenKind.MultiLineComment, ref text, length);
    }

    private bool ReadSingleLineComment(ref ReadOnlySpan<char> span)
    {// // Comment\n
        var idx = Arc.BaseHelper.IndexOfLfOrCrLf(span, out var newLineLength);
        if (idx < 0)
        {
            this.AddTokenAndSlice(TokenKind.SingleLineComment, ref span, span.Length);
            return false;
        }
        else
        {
            this.AddTokenAndSlice(TokenKind.SingleLineComment, ref span, idx);
            this.Slice(ref span, newLineLength);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Diagnostics.SourceRange NewRange(int length)
    {
        return new(new(this.line, this.character), new(this.line, this.character + length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Slice(ref ReadOnlySpan<char> span, int length)
    {
        span = span.Slice(length);
        this.position += length;
        this.character += length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToken(Token token)
    {
        this.tokenList.Add(token);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddTokenAndSlice(TokenKind tokenKind, ref ReadOnlySpan<char> span, int length)
    {
        this.tokenList.Add(new(tokenKind, this.text.Slice(this.position, length), this.line, this.character));

        span = span.Slice(length);
        this.position += length;
        this.character += length;
    }

    /// <summary>
    /// Adds a token that may contain line breaks (\r\n, \n, or a lone \r) and updates
    /// <see cref="line"/>/<see cref="character"/> accordingly. Returns the number of line breaks consumed.
    /// </summary>
    /// <param name="tokenKind">The token kind to add.</param>
    /// <param name="span">The remaining text span. The span is advanced by <paramref name="length"/> characters.</param>
    /// <param name="length">The number of characters to consume.</param>
    /// <returns>The number of line breaks consumed.</returns>
    private int AddTokenAndSliceWithLineTracking(TokenKind tokenKind, ref ReadOnlySpan<char> span, int length)
    {
        SourcePosition start = new(this.line, this.character);

        var consumed = span.Slice(0, length);
        var newLines = 0;
        var lastNewLineEnd = 0;
        for (var j = 0; j < consumed.Length; j++)
        {
            var c = consumed[j];
            if (c == Constants.LfChar)
            {// \n
                newLines++;
                lastNewLineEnd = j + 1;
            }
            else if (c == Constants.CrChar)
            {// \r\n or \r
                if (j + 1 < consumed.Length && consumed[j + 1] == Constants.LfChar)
                {
                    j++;
                }

                newLines++;
                lastNewLineEnd = j + 1;
            }
        }

        if (newLines > 0)
        {
            this.line += newLines;
            this.character = consumed.Length - lastNewLineEnd;
        }
        else
        {
            this.character += length;
        }

        this.tokenList.Add(new(tokenKind, this.text.Slice(this.position, length), new Diagnostics.SourceRange(start, new(this.line, this.character))));

        this.position += length;
        span = span.Slice(length);
        return newLines;
    }

    private void PushIndentSource(IndentSource indentSource)
    {
        this.indentStack.Push(indentSource);
        if (indentSource == IndentSource.Block)
        {
            this.blockDepth++;
        }
        else
        {
            this.nonBlockDepth++;
        }
    }

    private void PushIndentSource(TokenKind tokenKind)
    {
        switch (tokenKind)
        {
            case TokenKind.StartBlock:
                this.indentStack.Push(IndentSource.Block);
                this.blockDepth++;
                break;

            case TokenKind.OpenParenthesis:
                this.indentStack.Push(IndentSource.Parenthesis);
                this.nonBlockDepth++;
                break;

            case TokenKind.OpenBracket:
                this.indentStack.Push(IndentSource.Bracket);
                this.nonBlockDepth++;
                break;

            case TokenKind.LessThan:
                // Currently unreachable from the main loop ('<' is handled as an operator);
                // kept for when angle-bracket grouping is supported.
                this.indentStack.Push(IndentSource.AngleBracket);
                this.nonBlockDepth++;
                break;

            case TokenKind.OpenBrace:
                this.indentStack.Push(IndentSource.Brace);
                this.nonBlockDepth++;
                break;

            default:
                throw new InvalidOperationException();
        }
    }

    private void PopIndentSource(TokenKind expected)
    {
        while (this.indentStack.TryPeek(out var indentSource))
        {
            if (indentSource == IndentSource.Block)
            {
                this.indentStack.Pop();
                this.AddToken(new(TokenKind.EndBlock, default));
                this.blockDepth--;
                continue;
            }
            else if (indentSource == IndentSource.LineContinuation)
            {
                this.indentStack.Pop();
                this.nonBlockDepth--;
                continue;
            }

            var tokenKind = indentSource switch
            {
                IndentSource.Parenthesis => TokenKind.CloseParenthesis,
                IndentSource.Bracket => TokenKind.CloseBracket,
                IndentSource.AngleBracket => TokenKind.GreaterThan,
                IndentSource.Brace => TokenKind.CloseBrace,
                _ => TokenKind.Invalid,
            };

            if (tokenKind == expected)
            {
                this.indentStack.Pop();
                this.nonBlockDepth--;
                return;
            }

            break;
        }

        // Error recovery policy: the mismatched closer is treated as spurious and the
        // stack is left intact, so the still-open grouping can be matched (or reported)
        // later. e.g. "(]" reports an unmatched ']' and keeps '(' open.
        var diagnostic = expected switch
        {
            TokenKind.CloseParenthesis => Hashed.Kimi.UnmatchedParenthesis,
            TokenKind.CloseBracket => Hashed.Kimi.UnmatchedBracket,
            TokenKind.CloseBrace => Hashed.Kimi.UnmatchedBrace,
            TokenKind.GreaterThan => Hashed.Kimi.UnmatchedAngleBracket,
            _ => Hashed.Kimi.UnmatchedBracket,
        };

        this.urlDiagnostic.Add(this.NewRange(1), diagnostic);
    }

    private void ClearIndentStack()
    {
        while (this.indentStack.TryPop(out var indentSource))
        {
            switch (indentSource)
            {
                case IndentSource.Block:
                    this.AddToken(new(TokenKind.EndBlock, true));
                    this.blockDepth--;
                    break;

                case IndentSource.Parenthesis: // ()
                    this.AddToken(new(TokenKind.CloseParenthesis, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.Bracket: // []
                    this.AddToken(new(TokenKind.CloseBracket, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.AngleBracket: // <>
                    this.AddToken(new(TokenKind.GreaterThan, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.Brace: // {}
                    this.AddToken(new(TokenKind.CloseBrace, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.LineContinuation:
                    this.nonBlockDepth--;
                    break;

                default:
                    throw new UnreachableException();
            }
        }
    }

    private bool TryCloseIndentSourceByCurrentToken(IndentSource indentSource, ref ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        var tokenKind = indentSource switch
        {
            IndentSource.Parenthesis when span[0] == Constants.CloseParenthesisChar => TokenKind.CloseParenthesis,
            IndentSource.Bracket when span[0] == Constants.CloseBracketChar => TokenKind.CloseBracket,
            IndentSource.AngleBracket when span[0] == Constants.GreaterThanChar => TokenKind.GreaterThan,
            IndentSource.Brace when span[0] == Constants.CloseBraceChar => TokenKind.CloseBrace,
            _ => TokenKind.Invalid,
        };

        if (tokenKind == TokenKind.Invalid)
        {
            return false;
        }

        this.AddTokenAndSlice(tokenKind, ref span, 1);
        this.nonBlockDepth--;
        return true;
    }

    private void ClearState()
    {
        this.tokenList.Clear();
        this.indentStack.Clear();
        this.blockDepth = 0;
        this.nonBlockDepth = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextLine()
    {
        this.line += 1;
        this.character = 0;
    }
}
