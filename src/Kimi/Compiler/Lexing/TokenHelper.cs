// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

    // Compound access specifications are two keyword tokens (SPEC 2.5), so no keyword is longer than nine characters.
    private const int MaxKeywordLength = 9;

    // A keyword begins with an ASCII letter or '_', all within 'A' through 'z'.
    private const int FirstCharCount = 'z' - 'A' + 1;

    // The one spelling table of SPEC F.3's keywords and the punctuation, indexed by token kind.
    private static readonly string[] TokenTexts;

    // The keyword index, derived from TokenTexts: the keywords grouped by spelling length and first character, group g, with
    // g = (length * FirstCharCount) + (first - 'A'), being KeywordEntries[KeywordStarts[g]..KeywordStarts[g + 1]], each spelling held as
    // the integers Pack reads, so a lookup compares integers and calls nothing. About 2.3 KB per process (69 entries of 24 bytes and
    // 581 group starts).
    private static readonly KeywordEntry[] KeywordEntries;
    private static readonly byte[] KeywordStarts = new byte[((MaxKeywordLength + 1) * FirstCharCount) + 1];

    // The characters that are always a complete token by themselves, indexed by ASCII code. The tokenizer's lead-character
    // table (Tokenizer.LeadClass) sends exactly these characters here; every other punctuation has a longer spelling.
    private static readonly TokenKind[] SingleCharTokenKinds = new TokenKind[128];
    private static readonly sbyte[] SingleCharGroupingDepths = new sbyte[128];

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
        Set(TokenKind.Bool, "bool");
        Set(TokenKind.Isize, "isize");
        Set(TokenKind.Usize, "usize");
        Set(TokenKind.I8, "i8");
        Set(TokenKind.I16, "i16");
        Set(TokenKind.I32, "i32");
        Set(TokenKind.I64, "i64");
        Set(TokenKind.I128, "i128");
        Set(TokenKind.U8, "u8");
        Set(TokenKind.U16, "u16");
        Set(TokenKind.U32, "u32");
        Set(TokenKind.U64, "u64");
        Set(TokenKind.U128, "u128");
        Set(TokenKind.F32, "f32");
        Set(TokenKind.F64, "f64");
        Set(TokenKind.Char, "char");
        Set(TokenKind.String, "string");

        // Reserved words
        Set(TokenKind.True, "true");
        Set(TokenKind.False, "false");
        Set(TokenKind.Let, "let");
        Set(TokenKind.Var, "var");
        Set(TokenKind.Func, "func");
        Set(TokenKind.Try, "try");
        Set(TokenKind.Underscore, "_");
        Set(TokenKind.Public, "public");
        Set(TokenKind.Protected, "protected");
        Set(TokenKind.Private, "private");
        Set(TokenKind.Internal, "internal");
        Set(TokenKind.Open, "open");

        // Reserved words of expressions and statements
        Set(TokenKind.If, "if");
        Set(TokenKind.Else, "else");
        Set(TokenKind.Case, "case");
        Set(TokenKind.As, "as");
        Set(TokenKind.Is, "is");
        Set(TokenKind.Not, "not");
        Set(TokenKind.And, "and");
        Set(TokenKind.Or, "or");
        Set(TokenKind.For, "for");
        Set(TokenKind.In, "in");
        Set(TokenKind.While, "while");
        Set(TokenKind.Loop, "loop");
        Set(TokenKind.Match, "match");
        Set(TokenKind.Switch, "switch");
        Set(TokenKind.Return, "return");
        Set(TokenKind.Exit, "exit");
        Set(TokenKind.Continue, "continue");
        Set(TokenKind.Yield, "yield");
        Set(TokenKind.Do, "do");
        Set(TokenKind.Null, "null");
        Set(TokenKind.Require, "require");
        Set(TokenKind.Defer, "defer");
        Set(TokenKind.Self, "Self");
        Set(TokenKind.Init, "init");
        Set(TokenKind.Drop, "drop");
        Set(TokenKind.Base, "base");
        Set(TokenKind.ColonColon, "::");

        // Contextual keywords
        Set(TokenKind.Alias, "alias");
        Set(TokenKind.RootGroup, "rootgroup");
        Set(TokenKind.Group, "group");
        Set(TokenKind.Struct, "struct");
        Set(TokenKind.Enum, "enum");
        Set(TokenKind.Extension, "extension");
        Set(TokenKind.Contract, "contract");
        Set(TokenKind.Static, "static");
        Set(TokenKind.Associate, "associate");
        Set(TokenKind.Get, "get");
        Set(TokenKind.Set, "set");
        Set(TokenKind.Has, "has");
        Set(TokenKind.Computed, "computed");
        Set(TokenKind.Property, "property");

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

        SetSingleChar(Constants.SharpChar, TokenKind.Sharp, 0);
        SetSingleChar(Constants.DollarChar, TokenKind.Dollar, 0);
        SetSingleChar(Constants.CommaChar, TokenKind.Comma, 0);
        SetSingleChar(Constants.OpenBracketChar, TokenKind.OpenBracket, +1);
        SetSingleChar(Constants.CloseBracketChar, TokenKind.CloseBracket, -1);
        SetSingleChar(Constants.OpenParenthesisChar, TokenKind.OpenParenthesis, +1);
        SetSingleChar(Constants.CloseParenthesisChar, TokenKind.CloseParenthesis, -1);
        SetSingleChar(Constants.OpenBraceChar, TokenKind.OpenBrace, +1);
        SetSingleChar(Constants.CloseBraceChar, TokenKind.CloseBrace, -1);
        SetSingleChar(Constants.QuestionChar, TokenKind.Question, 0);

        KeywordEntries = IndexKeywords();

        static void Set(TokenKind kind, string text)
            => TokenTexts[(int)kind] = text;

        static void SetSingleChar(char c, TokenKind kind, int groupingDepth)
        {
            SingleCharTokenKinds[c] = kind;
            SingleCharGroupingDepths[c] = (sbyte)groupingDepth;
        }
    }

    // The keyword index over TokenTexts' rows below Identifier (SPEC F.3), a counting sort by group: each group's count, the running
    // ends, then each entry placed below its group's end, which leaves KeywordStarts[g] at group g's start. A keyword the index cannot
    // hold (longer than MaxKeywordLength, or not starting with 'A' through 'z') fails the type's initialization.
    private static KeywordEntry[] IndexKeywords()
    {
        for (var kind = TokenKind.Bool; kind < TokenKind.Identifier; kind++)
        {
            if (KeywordGroup(kind) is var group and >= 0)
            {
                KeywordStarts[group]++;
            }
        }

        for (var group = 1; group < KeywordStarts.Length; group++)
        {
            KeywordStarts[group] += KeywordStarts[group - 1];
        }

        var entries = new KeywordEntry[KeywordStarts[^1]];
        for (var kind = TokenKind.Bool; kind < TokenKind.Identifier; kind++)
        {
            if (KeywordGroup(kind) is var group and >= 0)
            {
                entries[--KeywordStarts[group]] = KeywordEntry.Pack(TokenTexts[(int)kind], kind);
            }
        }

        return entries;

        // An empty row (a gap in TokenKind) has no group.
        static int KeywordGroup(TokenKind kind)
        {
            var text = TokenTexts[(int)kind];
            if (text.Length == 0)
            {
                return -1;
            }

            return text.Length <= MaxKeywordLength && (uint)(text[0] - 'A') < FirstCharCount ? (text.Length * FirstCharCount) + (text[0] - 'A') :
                throw new InvalidOperationException($"The keyword index cannot hold {kind}'s spelling '{text}'.");
        }
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
    /// Gets the length of the word that starts the text: the characters before the first tokenizer separator.
    /// </summary>
    /// <remarks>Identifiers are short, so a scalar scan beats the setup cost of a vectorized search.</remarks>
    /// <param name="text">The text to search.</param>
    /// <returns>The zero-based index of the first separator, or the text's length if no separator is found.</returns>
    public static int WordLength(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (IsSeparator(text[i]))
            {
                return i;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Determines whether a token represents a primitive type keyword.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for a primitive type keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPrimitiveType(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Bool && tokenKind <= TokenKind.String;

    /// <summary>
    /// Determines whether a token is a literal token: a number, a character, or a string without Interpolation (SPEC F.2.3, F.2.4).
    /// </summary>
    /// <remarks>A string with Interpolation is <see cref="TokenKind.InterpolatedStringLiteral"/>, read by its parts and outside this class.</remarks>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for <see cref="TokenKind.NumericLiteral"/> through <see cref="TokenKind.StringLiteral"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLiteral(this TokenKind tokenKind)
        => tokenKind is >= TokenKind.NumericLiteral and <= TokenKind.StringLiteral;

    /// <summary>
    /// Determines whether a token is a reserved word, which is never a Name (SPEC F.3).
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for a reserved keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyword(this TokenKind tokenKind)
        => tokenKind is > TokenKind.Invalid and < TokenKind.Alias;

    /// <summary>
    /// Determines whether a token can be used as a Name: an identifier, or a contextual keyword outside its context (SPEC F.3).
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for an identifier or contextual keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierOrContextualKeyword(this TokenKind tokenKind)
        => tokenKind is >= TokenKind.Alias and <= TokenKind.Identifier;

    /// <summary>
    /// Classifies identifier-like text as a keyword by the spelling table.
    /// </summary>
    /// <remarks>
    /// The text's length and first character select the few keywords it can equal, and each is compared as two or three integers.
    /// This avoids hashing every identifier.
    /// </remarks>
    /// <param name="text">The identifier-like text.</param>
    /// <returns>The keyword token kind, or <see cref="TokenKind.Identifier"/>.</returns>
    public static TokenKind GetKeywordOrIdentifierKind(ReadOnlySpan<char> text)
    {
        var first = text.IsEmpty ? -1 : text[0] - 'A';
        if (text.Length > MaxKeywordLength || (uint)first >= FirstCharCount)
        {
            return TokenKind.Identifier;
        }

        // The group is below KeywordStarts.Length - 1 by the two checks above.
        ref var starts = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(KeywordStarts), (text.Length * FirstCharCount) + first);
        int i = starts, end = Unsafe.Add(ref starts, 1);
        if (i == end)
        {
            return TokenKind.Identifier;
        }

        // KeywordStarts holds indexes into KeywordEntries only.
        var probe = KeywordEntry.Pack(text, TokenKind.Identifier);
        ref var entries = ref MemoryMarshal.GetArrayDataReference(KeywordEntries);
        do
        {
            ref var entry = ref Unsafe.Add(ref entries, i);
            if (entry.Head == probe.Head && entry.Tail == probe.Tail && entry.Middle == probe.Middle)
            {
                return entry.Kind;
            }
        }
        while (++i < end);

        return TokenKind.Identifier;
    }

    /// <summary>
    /// Tries to classify a character that is always a complete token by itself, and reports its grouping-depth effect.
    /// </summary>
    /// <param name="c">The character to classify.</param>
    /// <param name="tokenKind">When this method returns, contains the token kind for <paramref name="c"/>, or <see cref="TokenKind.Invalid"/>.</param>
    /// <param name="groupingDepth">When this method returns, contains +1 for an opening grouping token, -1 for a closing grouping token, or 0 for a neutral token.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is a recognized single-character token; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetSingleCharTokenKind(char c, out TokenKind tokenKind, out int groupingDepth)
    {
        if (c < 128)
        {
            tokenKind = SingleCharTokenKinds[c];
            groupingDepth = SingleCharGroupingDepths[c];
            return tokenKind != TokenKind.Invalid;
        }

        tokenKind = TokenKind.Invalid;
        groupingDepth = 0;
        return false;
    }

    // A spelling of one to nine characters as the integers that hold all of them for its length: characters 0-3 and the last four
    // (overlapping) for four or more; characters 0-1 and the last two for two or three; character 0 for one; and the middle character
    // (length / 2), which only a nine-character spelling needs (its character 4).
    private readonly struct KeywordEntry(ulong head, ulong tail, char middle, TokenKind kind)
    {
        public ulong Head { get; } = head;

        public ulong Tail { get; } = tail;

        public char Middle { get; } = middle;

        public TokenKind Kind { get; } = kind;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeywordEntry Pack(ReadOnlySpan<char> text, TokenKind kind)
        {
            var length = text.Length;
            ref var chars = ref MemoryMarshal.GetReference(text);
            ref var bytes = ref Unsafe.As<char, byte>(ref chars);
            var middle = Unsafe.Add(ref chars, length >> 1);
            if (length >= 4)
            {
                return new(
                    Unsafe.ReadUnaligned<ulong>(ref bytes),
                    Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref bytes, (length - 4) * sizeof(char))),
                    middle,
                    kind);
            }

            if (length >= 2)
            {
                return new(
                    Unsafe.ReadUnaligned<uint>(ref bytes),
                    Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref bytes, (length - 2) * sizeof(char))),
                    middle,
                    kind);
            }

            return new(middle, 0, middle, kind);
        }
    }
}
