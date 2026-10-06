// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Lexing;

/// <summary>
/// Converts Kimi source text into a sequence of lexical tokens and indentation tokens.
/// </summary>
internal ref struct Tokenizer
{
    private enum IndentSource : byte
    {
        Block,
        Parenthesis, // ()
        Bracket, // []
        AngleBracket, // Recognized adjacent generic delimiters.
        Brace, // {}
        LineContinuation, // Implicit continuation, such as a method chain line starting with ".".
    }

    // HeaderLevel is the absolute indentation of a leading "->" header continuation line, otherwise -1.
    private readonly record struct IndentEntry(IndentSource Source, int Position, bool SharesBodyIndent = false, int HeaderLevel = -1);

    private const int InitialIndentStackCapacity = 32;
    private const int MinimumTokenCapacity = 256;

    /// <summary>Gets the ASCII characters that may continue an identifier: letters, digits, and underscore.</summary>
    private static ReadOnlySpan<byte> AsciiIdentifierPart =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x00
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x10
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x20
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, // 0x30 0-9
        0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // 0x40 A-O
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, // 0x50 P-Z _
        0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // 0x60 a-o
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, // 0x70 p-z
    ];

    /// <summary>
    /// Gets the class of each ASCII lead character: 1 starts an identifier, keyword, or number literal;
    /// 2 is a single-character token; 0 needs the operator, literal, or line-break dispatch.
    /// </summary>
    private static ReadOnlySpan<byte> LeadClass =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x00
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x10
        0, 0, 0, 2, 2, 0, 0, 0, 2, 2, 0, 0, 2, 0, 0, 0, // 0x20
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 2, // 0x30
        0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // 0x40
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 2, 0, 1, // 0x50
        0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, // 0x60
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 0, 2, 0, 0, // 0x70
    ];

    #region FieldAndProperty

    private readonly DiagnosticCollection diagnostics;
    private readonly SourceDocument sourceDocument;
    private readonly ReadOnlySpan<char> sourceText;
    private readonly int indentationOffset;
    private Token[] tokens;
    private int tokenCount;
    private IndentEntry[] indentStack;
    private int indentCount;
    private ReadOnlySpan<char> span;
    private int position;
    private int currentIndentLevel;
    private int blockDepth;
    private int nonBlockDepth;
    private int sharedBodyIndentDepth;
    private int tokenAdded;

    // Only malformed literals allocate this map. A delimiter opened before the rejected literal cannot be diagnosed
    // independently until the literal's extent is known; delimiters opened by later source items remain independent.
    private Dictionary<int, DiagnosticKey>? delimiterRecoveryCauses;

    /// <summary>
    /// Gets the source document being tokenized.
    /// </summary>
    public SourceDocument SourceDocument => this.sourceDocument;

    /// <summary>
    /// Gets the complete source text.
    /// </summary>
    public ReadOnlySpan<char> SourceText => this.sourceText;

    /// <summary>
    /// Gets an empty span at the current source position.
    /// </summary>
    public SourceSpan CurrentRange => new(this.position, 0);

    /// <summary>
    /// Gets the generated tokens. The span is valid until <see cref="Dispose"/> is called.
    /// </summary>
    public ReadOnlySpan<Token> Tokens => this.tokens.AsSpan(0, this.tokenCount);

    internal bool CollectDocumentation { get; set; }

    internal Documentation.DocumentationSource? Documentation { get; private set; }

    /// <summary>
    /// Gets the character following the current one, or NUL at the end of the source.
    /// </summary>
    private readonly char NextChar => this.span.Length > 1 ? this.span[1] : '\0';

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="Tokenizer"/> struct.
    /// </summary>
    /// <param name="diagnostics">The destination for lexical diagnostics.</param>
    /// <param name="sourceDocument">The source document to tokenize.</param>
    public Tokenizer(DiagnosticCollection diagnostics, SourceDocument sourceDocument)
        : this(diagnostics, sourceDocument, new SourceSpan(0, (sourceDocument ?? throw new ArgumentNullException(nameof(sourceDocument))).SourceText.Length))
    {
    }

    // A bounded view retains original source offsets for interpolation diagnostics and Koto spans.
    internal Tokenizer(DiagnosticCollection diagnostics, SourceDocument sourceDocument, SourceSpan range)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);

        this.diagnostics = diagnostics;
        diagnostics.Register(sourceDocument);
        this.sourceDocument = sourceDocument;
        this.sourceText = sourceDocument.AsSpan()[..range.End];
        this.position = range.Start;
        if (range.Start > 0)
        {
            var line = sourceDocument.GetPosition(range.Start).Line;
            this.indentationOffset = BaseHelper.CountLeadingSpaces(sourceDocument.GetLineSpan(line)) / Constants.IndentationSpaces;
        }

        this.indentStack = ArrayPool<IndentEntry>.Shared.Rent(InitialIndentStackCapacity);

        // Typical source yields roughly one token per four characters; the array grows on demand.
        this.tokens = ArrayPool<Token>.Shared.Rent(Math.Max(MinimumTokenCapacity, (range.Length >> 2) + 64));
    }

    /// <summary>
    /// Releases the pooled token storage.
    /// </summary>
    public void Dispose()
    {
        if (this.tokens.Length > 0)
        {
            ArrayPool<Token>.Shared.Return(this.tokens);
            this.tokens = [];
            this.tokenCount = 0;
        }

        if (this.indentStack.Length > 0)
        {
            ArrayPool<IndentEntry>.Shared.Return(this.indentStack);
            this.indentStack = [];
        }
    }

    /// <summary>
    /// Tokenizes the complete source document.
    /// </summary>
    public void ReadAll()
    {
        // .NET hosts source as UTF-16. Reject unpaired surrogates even in comments
        // and raw strings; they cannot originate from valid UTF-8 source.
        var offset = this.position;
        while (offset < this.sourceText.Length)
        {
            var relative = this.sourceText[offset..].IndexOfAnyInRange('\uD800', '\uDFFF');
            if (relative < 0)
            {
                break;
            }

            offset += relative;
            if (!char.IsHighSurrogate(this.sourceText[offset]) ||
                offset + 1 == this.sourceText.Length || !char.IsLowSurrogate(this.sourceText[offset + 1]))
            {
                this.Report(new SourceSpan(offset, 1), DiagnosticCode.InvalidSourceEncoding_Kd);
                return;
            }

            offset += 2;
        }

        this.currentIndentLevel = 0;
        do
        {
            this.ReadLogicalLine();
        }
        while (this.position < this.sourceText.Length);
    }

    private static TokenKind GetClosingTokenKind(IndentSource indentSource)
        => indentSource switch
        {
            IndentSource.Parenthesis => TokenKind.CloseParenthesis,
            IndentSource.Bracket => TokenKind.CloseBracket,
            IndentSource.AngleBracket => TokenKind.GreaterThan,
            IndentSource.Brace => TokenKind.CloseBrace,
            _ => throw new UnreachableException(),
        };

    /// <summary>Gets the syntax form of the closing delimiter an open grouping needs.</summary>
    private static SyntaxForm CloserForm(TokenKind closingKind)
        => closingKind switch
        {
            TokenKind.CloseParenthesis => SyntaxForm.CloseParenthesis,
            TokenKind.CloseBrace => SyntaxForm.CloseBrace,
            TokenKind.GreaterThan => SyntaxForm.CloseAngleBracket,
            _ => SyntaxForm.CloseBracket,
        };

    /// <summary>Measures the ASCII identifier prefix: letters, digits, and underscores.</summary>
    /// <remarks>
    /// Eight characters are classified per step, which covers most identifiers in a single step.
    /// Kept out of line so that the caller does not preserve vector registers on every token.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ScanAsciiIdentifierLength(ReadOnlySpan<char> span)
    {
        ref var start = ref MemoryMarshal.GetReference(span);
        var spanLength = span.Length;
        var length = 0;
        if (Vector128.IsHardwareAccelerated && spanLength >= Vector128<ushort>.Count)
        {
            var lastStart = spanLength - Vector128<ushort>.Count;
            while (true)
            {
                var chars = Vector128.LoadUnsafe(ref Unsafe.As<char, ushort>(ref start), (nuint)length);
                var letters = Vector128.LessThanOrEqual((chars | Vector128.Create((ushort)0x20)) - Vector128.Create((ushort)'a'), Vector128.Create((ushort)('z' - 'a')));
                var digits = Vector128.LessThanOrEqual(chars - Vector128.Create((ushort)'0'), Vector128.Create((ushort)9));
                var underscores = Vector128.Equals(chars, Vector128.Create((ushort)'_'));
                var mask = (letters | digits | underscores).ExtractMostSignificantBits();
                if (mask != 0xFF)
                {
                    return length + BitOperations.TrailingZeroCount(~mask);
                }

                length += Vector128<ushort>.Count;
                if (length > lastStart)
                {
                    break;
                }
            }
        }

        while (length < spanLength)
        {
            var c = Unsafe.Add(ref start, length);
            if (c >= 128 || AsciiIdentifierPart[c] == 0)
            {
                break;
            }

            length++;
        }

        return length;
    }

    /// <summary>Counts leading spaces. Runs of spaces are short, so a scalar loop beats a vectorized search.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountSpaces(ReadOnlySpan<char> span)
    {
        var count = 0;
        while (count < span.Length && span[count] == Constants.SpaceChar)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Reads one logical line: its tokens, the physical lines that continue it inside delimiters, and the layout tokens that the
    /// indentation of the next effective line produces (SPEC 2.2.1). The indentation stack lives for one logical line only; the
    /// body levels it leaves open become <see cref="currentIndentLevel"/>.
    /// </summary>
    private void ReadLogicalLine()
    {
        this.tokenAdded = 0;
        this.indentCount = 0;
        this.blockDepth = 0;
        this.nonBlockDepth = 0;

Loop:
        this.span = this.sourceText.Slice(this.position);
        if (this.span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }

        if (this.span[0] == Constants.SpaceChar)
        {// If whitespace is present, process it first.
            goto MeasureIndentation;
        }

        while (true)
        {
            // Skip spaces; indentation was measured at the physical line start.
            var spaces = CountSpaces(this.span);
            if (spaces > 0)
            {
                this.Slice(spaces);
            }

            if (this.span.Length == 0)
            {// End-of-file
                goto EndOfFile;
            }

            // span.Length >= 1
            var c = this.span[0];
            if (c < 128)
            {// Identifiers dominate ordinary source; dispatch them before the operator switch.
                var leadClass = LeadClass[c];
                if (leadClass == 1)
                {
                    this.ReadLiteralKeywordOrIdentifier();
                    continue;
                }
                else if (leadClass == 2)
                {// A character that is always a complete token by itself.
                    TokenHelper.TryGetSingleCharTokenKind(c, out var singleKind, out var singleDepth);
                    if (singleDepth > 0)
                    {
                        this.PushIndentSource(singleKind);
                    }
                    else if (singleDepth < 0 && !this.PopIndentSource(singleKind))
                    {
                        // A closer that closes no grouping is reported; it stays a closer, which list recovery skips.
                        this.Add(Token.UnmatchedCloser(singleKind, this.position));
                        this.tokenAdded++;
                        this.Slice(1);
                        continue;
                    }

                    this.AddTokenAndSlice(singleKind, 1);
                    continue;
                }
            }

            switch (c)
            {
                case Constants.ColonChar:
                    if (this.NextChar == Constants.ColonChar)
                    {
                        this.AddTokenAndSlice(TokenKind.ColonColon, 2);
                    }
                    else
                    {
                        this.AddTokenAndSlice(TokenKind.Colon, 1);
                    }

                    continue;

                case Constants.CrChar:
                    {// \r \r\n
                        this.Slice(this.NextChar == Constants.LfChar ? 2 : 1);
                        goto NextLine;
                    }

                case Constants.LfChar:
                    {// \n
                        this.Slice(1);
                        goto NextLine;
                    }

                case Constants.SemicolonChar:
                    this.Report(this.NewRange(1), DiagnosticCode.SemicolonNotAllowed_Kd);
                    if (this.indentCount > 0 && this.indentStack[this.indentCount - 1].Source is not (IndentSource.Block or IndentSource.LineContinuation))
                    {
                        // Inside a grouping no statement ends: the semicolon is an invalid token, which list recovery skips.
                        this.AddTokenAndSlice(TokenKind.Invalid, 1);
                        continue;
                    }

                    // Recover as a separator so the parser can still inspect following syntax.
                    // This does not end the physical line or alter indentation.
                    this.AddTokenAndSlice(TokenKind.Separator, 1);
                    continue;

                case Constants.AmpersandChar:
                    {// & && &=
                        var next = this.NextChar;
                        if (next == Constants.AmpersandChar)
                        {
                            this.AddTokenAndSlice(TokenKind.AmpersandAmpersand, 2);
                        }
                        else if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.AmpersandEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Ampersand, 1);
                        }

                        continue;
                    }

                case Constants.AsteriskChar:
                    {// * *=
                        if (this.NextChar == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.AsteriskEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Asterisk, 1);
                        }

                        continue;
                    }

                case Constants.BarChar:
                    {// | || |=
                        var next = this.NextChar;
                        if (next == Constants.BarChar)
                        {
                            this.AddTokenAndSlice(TokenKind.BarBar, 2);
                        }
                        else if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.BarEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Bar, 1);
                        }

                        continue;
                    }

                case Constants.CaretChar:
                    {// ^ ^=
                        if (this.NextChar == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.CaretEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Caret, 1);
                        }

                        continue;
                    }

                case Constants.DotChar:
                    {// . .. ..=
                        if (this.NextChar == Constants.DotChar)
                        {
                            if (this.span.Length >= 3 && this.span[2] == Constants.EqualsChar)
                            {
                                this.AddTokenAndSlice(TokenKind.DotDotEquals, 3);
                            }
                            else
                            {
                                this.AddTokenAndSlice(TokenKind.DotDot, 2);
                            }
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Dot, 1);
                        }

                        continue;
                    }

                case Constants.EqualsChar:
                    {// = == =>
                        var next = this.NextChar;
                        if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.EqualsEquals, 2);
                        }
                        else if (next == Constants.GreaterThanChar)
                        {
                            this.AddTokenAndSlice(TokenKind.EqualsGreaterThan, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Equals, 1);
                        }

                        continue;
                    }

                case Constants.ExclamationChar:
                    {// ! !=
                        if (this.NextChar == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.ExclamationEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Exclamation, 1);
                        }

                        continue;
                    }

                case Constants.GreaterThanChar:
                    {// > >= >> >>=
                        // The longest spelling is kept; '>' and '>>' close one or two recognized Type argument lists, and the parser
                        // splits '>>' and '>>=' where it reads Types (SPEC 2.4). '>=' closes none.
                        var next = this.NextChar;
                        if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.GreaterThanEquals, 2);
                        }
                        else if (next == Constants.GreaterThanChar)
                        {
                            this.CloseTypeArguments(2);
                            if (this.span.Length >= 3 && this.span[2] == Constants.EqualsChar)
                            {
                                this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThanEquals, 3);
                            }
                            else
                            {
                                this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThan, 2);
                            }
                        }
                        else
                        {
                            this.CloseTypeArguments(1);
                            this.AddTokenAndSlice(TokenKind.GreaterThan, 1);
                        }

                        continue;
                    }

                case Constants.LessThanChar:
                    {// < <= << <<=
                        var next = this.NextChar;
                        if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.LessThanEquals, 2);
                        }
                        else if (next == Constants.LessThanChar)
                        {
                            if (this.span.Length >= 3 && this.span[2] == Constants.EqualsChar)
                            {
                                this.AddTokenAndSlice(TokenKind.LessThanLessThanEquals, 3);
                            }
                            else
                            {
                                this.AddTokenAndSlice(TokenKind.LessThanLessThan, 2);
                            }
                        }
                        else
                        {
                            var opensTypeArguments = this.IsGenericOpen();
                            if (opensTypeArguments)
                            {
                                this.PushIndentSource(IndentSource.AngleBracket);
                            }

                            this.Add(Token.LessThan(this.position, opensTypeArguments));
                            this.tokenAdded++;
                            this.Slice(1);
                        }

                        continue;
                    }

                case Constants.MinusChar:
                    {// - -- -= ->
                        var next = this.NextChar;
                        if (next == Constants.MinusChar)
                        {
                            this.AddTokenAndSlice(TokenKind.MinusMinus, 2);
                        }
                        else if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.MinusEquals, 2);
                        }
                        else if (next == Constants.GreaterThanChar)
                        {
                            this.AddTokenAndSlice(TokenKind.MinusGreaterThan, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Minus, 1);
                        }

                        continue;
                    }

                case Constants.PercentChar:
                    {// % %=
                        if (this.NextChar == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.PercentEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Percent, 1);
                        }

                        continue;
                    }

                case Constants.PlusChar:
                    {// + ++ +=
                        var next = this.NextChar;
                        if (next == Constants.PlusChar)
                        {
                            this.AddTokenAndSlice(TokenKind.PlusPlus, 2);
                        }
                        else if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.PlusEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Plus, 1);
                        }

                        continue;
                    }

                case Constants.SlashChar:
                    {// / // /* /=
                        var next = this.NextChar;
                        if (next == Constants.SlashChar)
                        {// Single line comment
                            this.ReadSingleLineComment();
                            goto NextLine;
                        }

                        if (next == Constants.AsteriskChar)
                        {// Multi line comment
                            if (this.ReadMultiLineComment())
                            {
                                goto NextLine;
                            }
                        }
                        else if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.SlashEquals, 2);
                        }
                        else
                        {
                            this.AddTokenAndSlice(TokenKind.Slash, 1);
                        }

                        continue;
                    }

                case Constants.AtChar:
                    {// @
                        this.AddTokenAndSlice(TokenKind.At, 1);
                        continue;
                    }

                case '"':
                    {// "Text" or """Text"""
                        this.ReadStringLiteral();
                        continue;
                    }

                case '\'':
                    {
                        this.ReadCharLiteral();
                        continue;
                    }

                default:
                    // A non-ASCII Name, or a character that starts no token, which the identifier path reports.
                    this.ReadLiteralKeywordOrIdentifier();
                    continue;
            }
        }

NextLine:
        if (this.span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }
        else if (this.tokenAdded == 0)
        {// If text remains but no token was found, such as on a blank line, retry processing.
            goto Loop;
        }

MeasureIndentation:
// Indentation is measured once, at the physical line start.
// Comments that follow do not change it.
        var indentationStart = this.position;
        var numberOfSpaces = CountSpaces(this.span);
        var indentationLength = numberOfSpaces;
        this.Slice(numberOfSpaces);

LineContent:
        if (this.span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }

        if (this.span[0] == Constants.LfChar)
        {// Empty line (\n)
            this.Slice(1);
            goto NextLine;
        }
        else if (this.span[0] == Constants.CrChar)
        {// Empty line (\r\n or \r)
            this.Slice(this.span.Length > 1 && this.span[1] == Constants.LfChar ? 2 : 1);
            goto NextLine;
        }
        else if (this.span.Length >= 2 && this.span[0] == Constants.SlashChar)
        {// /
            if (this.span[1] == Constants.SlashChar)
            {// // Single line comment
                this.ReadSingleLineComment();
                goto NextLine;
            }
            else if (this.span[1] == Constants.AsteriskChar)
            {// /* Multi line comment */
                if (this.ReadMultiLineComment())
                {
                    goto NextLine;
                }

                // Skip spaces after the comment WITHOUT counting them as indentation;
                // the indentation of this line was already measured at the line start
                // (this prevents a bogus InvalidIndentation diagnostic for "/* c */ foo").
                this.Slice(CountSpaces(this.span));
                goto LineContent;
            }
        }

        var unnecessarySpaces = numberOfSpaces % Constants.IndentationSpaces;
        if (unnecessarySpaces > 0)
        {// Invalid indentation
            this.Report(new(indentationStart, indentationLength), DiagnosticCode.InvalidIndentation_Kd, Constants.IndentationSpaces);
            numberOfSpaces += Constants.IndentationSpaces - unnecessarySpaces;
        }

        var indentLevel = (numberOfSpaces / Constants.IndentationSpaces) - this.indentationOffset;
        if (this.tokenCount == 0 && indentLevel > 0)
        {
            this.Report(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatch_Kd);
            indentLevel = 0;
        }

        // A "->" continuation ends before the next line at or above its own level; that line
        // starts the body or the next item rather than continuing the header.
        while (this.indentCount > 0 && this.indentStack[this.indentCount - 1] is { HeaderLevel: >= 0 } header && indentLevel <= header.HeaderLevel)
        {
            this.indentCount--;
            this.nonBlockDepth--;
        }

        // Indentation remains significant even inside grouping constructs.
        // Therefore, both block depth and non-block depth are subtracted when
        // calculating the indentation difference.
        this.ShareHeaderDelimiterIndent(indentLevel);
        var indentDelta = indentLevel - this.currentIndentLevel - this.blockDepth - this.nonBlockDepth + this.sharedBodyIndentDepth;

        if (indentDelta == 1)
        {
            // A line that starts with "." is treated as a continuation of the previous
            // expression. It contributes one required indentation level, like grouping
            // constructs, but does not require an explicit closing token.
            if (this.span[0] == Constants.DotChar && this.NextChar != Constants.DotChar && !this.PreviousLineStartsBody())
            {// Method chain
                this.PushIndentSource(IndentSource.LineContinuation);
                goto Loop;
            }
            else if (this.span.Length > 1 && this.span[0] == Constants.MinusChar && this.span[1] == Constants.GreaterThanChar)
            {// A header "->" line is one level deeper; delimiters opened on it nest from that line (SPEC 2.2.1).
                this.PushIndentSource(IndentSource.LineContinuation, indentLevel);
                goto Loop;
            }
            else if (this.span.Length > 1 && this.span[0] == Constants.EqualsChar && this.span[1] == Constants.GreaterThanChar)
            {// =>
                goto Loop;
            }
        }

        var separatorInserted = false;

        if (indentDelta > 0)
        {
            this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            separatorInserted = true;

            if (indentDelta > 1)
            {
                this.Report(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatch_Kd);
                indentDelta = 1;
            }

            // Recover at the written level after reporting a skipped body level.
            for (var i = 0; i < indentDelta; i++)
            {
                this.AddToken(new(TokenKind.StartBlock, this.CurrentRange));
                this.PushIndentSource(IndentSource.Block);
            }
        }
        else if (indentDelta < 0)
        {
            // When indentation decreases inside a grouping construct, the current token
            // may be the matching closing delimiter placed at the outer indentation level.
            // If it matches, consume the delimiter and close the grouping context.
            // Otherwise, recover by treating the grouping construct as implicitly closed,
            // remove it from the indentation stack, and report a missing delimiter.

            var hasTrailingContentOnCurrentLine = false;
            var indentationMismatch = false;

            for (var i = indentDelta; i < 0; i++)
            {
                if (this.indentCount > 0)
                {
                    var entry = this.indentStack[--this.indentCount];
                    var indentSource = entry.Source;
                    if (entry.SharesBodyIndent)
                    {
                        this.sharedBodyIndentDepth--;
                        i--;
                    }

                    if (indentSource == IndentSource.Block)
                    {
                        this.AddToken(new(TokenKind.EndBlock, this.CurrentRange));
                        this.blockDepth--;
                    }
                    else if (indentSource == IndentSource.LineContinuation)
                    {
                        this.nonBlockDepth--;
                        continue;
                    }
                    else if (this.TryCloseIndentSourceByCurrentToken(indentSource))
                    {
                        // Content after an outer-indented closing delimiter remains part of
                        // the same logical line, even when separated from the delimiter by spaces.
                        //
                        // Example:
                        //     foo(
                        //         a
                        //     ) + 1
                        //
                        // A newline or single-line comment still ends the logical line. Finish
                        // processing the remaining indentation sources before continuing so that
                        // enclosing blocks are closed correctly.
                        this.Slice(CountSpaces(this.span));

                        hasTrailingContentOnCurrentLine =
                            !this.span.IsEmpty &&
                            this.span[0] != Constants.CrChar &&
                            this.span[0] != Constants.LfChar &&
                            !(this.span.Length >= 2 &&
                                this.span[0] == Constants.SlashChar &&
                                this.span[1] == Constants.SlashChar);

                        continue;
                    }
                    else
                    {
                        this.nonBlockDepth--;
                        // Close the malformed delimiter before the next source item. A grouping that the
                        // indentation ends unclosed is reported like one left open at the end of the source.
                        // The closer is missing right after the grouping's last written token: an insertion point.
                        var closingKind = GetClosingTokenKind(indentSource);
                        var missing = this.InsertionPoint(new SourceSpan(indentationStart, indentationLength));

                        this.AddToken(new(closingKind, this.CurrentRange, true));
                        this.ReportMissingDelimiter(entry, missing, closingKind);
                        indentationMismatch = true;
                        // Finish unwinding to the written indentation. Stopping at the first recovered delimiter
                        // would put the next source item inside an outer call or the preceding function body.
                        continue;
                    }
                }
                else if (this.currentIndentLevel > 0)
                {
                    this.AddToken(new(TokenKind.EndBlock, this.CurrentRange));
                    this.currentIndentLevel--;
                }
                else
                {
                    this.Report(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatch_Kd);
                    indentationMismatch = true;
                    break;
                }
            }

            if (hasTrailingContentOnCurrentLine && !indentationMismatch)
            {
                goto Loop;
            }

            this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            separatorInserted = true;
        }

        if (this.nonBlockDepth > 0)
        {
            // A nested executable body has statement boundaries even while its
            // enclosing argument/element delimiter suppresses continuation separators.
            if (!separatorInserted && this.tokenAdded > 0 && this.indentCount > 0 && this.indentStack[this.indentCount - 1].Source == IndentSource.Block)
            {
                this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            }

            goto Loop;
        }
        else
        {
            this.currentIndentLevel += this.blockDepth;
            this.blockDepth = 0;

            if (this.span.IsEmpty)
            {
                // An outer-aligned closer can consume the last character above.
                // ReadAll will not call ReadLogicalLine again to close the enclosing bodies.
                goto EndOfFile;
            }

            if (this.tokenAdded > 0 && !separatorInserted)
            {
                this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            }

            return;
        }

EndOfFile:
        this.ClearIndentStack();
        while (this.currentIndentLevel > 0)
        {
            this.Add(new(TokenKind.EndBlock, this.CurrentRange));
            this.currentIndentLevel--;
        }

        Debug.Assert(this.blockDepth == 0);
        Debug.Assert(this.nonBlockDepth == 0);
    }

    /// <summary>
    /// Determines whether the '&lt;' at the current position opens Type arguments or parameters: it follows a Name, adjacent to it in an
    /// expression (SPEC 12.4.2), and its matching '&gt;' follows within the grouping that encloses it. Its content continues on a later
    /// line only as SPEC 2.2.1 lets the content of a delimiter continue, and a '&lt;' that is not adjacent, as in a declaration
    /// <c>List &lt;T&gt;</c>, continues only when the line ends after it. Otherwise it is a comparison.
    /// </summary>
    private readonly bool IsGenericOpen()
    {
        if (this.tokenCount == 0)
        {
            return false;
        }

        var previous = this.tokens[this.tokenCount - 1];
        if (!(previous.Kind.IsIdentifierOrContextualKeyword() || previous.Kind.IsPrimitiveType() || previous.Kind == TokenKind.Self))
        {
            return false;
        }

        var multiline = true;
        if (previous.Span.End != this.position)
        {
            if (!this.IsDeclarationGenericContext())
            {
                return false;
            }

            var after = 1 + CountSpaces(this.span[1..]);
            multiline = after >= this.span.Length || this.span[after] is Constants.CrChar or Constants.LfChar ||
                (this.span[after] == Constants.SlashChar && after + 1 < this.span.Length && this.span[after + 1] == Constants.SlashChar);
        }

        // Angle brackets count only outside nested groupings, whose own '<' is judged when it is read.
        var depth = 1;
        var nesting = 0;
        var openingIndent = -1;
        for (var i = 1; i < this.span.Length; i++)
        {
            var c = this.span[i];
            switch (c)
            {
                case Constants.SlashChar when i + 1 < this.span.Length && this.span[i + 1] == Constants.SlashChar:
                    {
                        var end = this.span[(i + 2)..].IndexOfAny(Constants.CrChar, Constants.LfChar);
                        if (end < 0)
                        {
                            return false;
                        }

                        i += end + 1; // The line break is read next.
                        continue;
                    }

                case Constants.SlashChar when i + 1 < this.span.Length && this.span[i + 1] == Constants.AsteriskChar:
                    {
                        var end = this.span[(i + 2)..].IndexOf("*/");
                        if (end < 0)
                        {
                            return false;
                        }

                        i += end + 3;
                        continue;
                    }

                case Constants.CrChar or Constants.LfChar:
                    {
                        // The content continues on the next effective line when it is indented deeper than the line of the '<', or when
                        // the closer stands at that line's indentation; any other line starts a new item, so no '>' there matches.
                        if (!multiline)
                        {
                            return false;
                        }

                        if (openingIndent < 0)
                        {
                            var lineStart = this.sourceText[..this.position].LastIndexOfAny(Constants.CrChar, Constants.LfChar) + 1;
                            openingIndent = CountSpaces(this.sourceText[lineStart..]);
                        }

                        var next = this.NextEffectiveLine(i, out var indent);
                        if (next < 0 || !(indent > openingIndent || (indent == openingIndent && this.span[next] == Constants.GreaterThanChar)))
                        {
                            return false;
                        }

                        i = next - 1;
                        continue;
                    }

                case Constants.OpenParenthesisChar or Constants.OpenBracketChar or Constants.OpenBraceChar:
                    nesting++;
                    continue;

                case Constants.CloseParenthesisChar or Constants.CloseBracketChar or Constants.CloseBraceChar:
                    if (nesting-- == 0)
                    {
                        return false; // The enclosing grouping closes first.
                    }

                    continue;

                case Constants.LessThanChar when nesting == 0:
                    depth++;
                    continue;

                case Constants.MinusChar when i + 1 < this.span.Length && this.span[i + 1] == Constants.GreaterThanChar:
                    i++; // '->' of a Function Type.
                    continue;

                case Constants.EqualsChar when i + 1 < this.span.Length && this.span[i + 1] == Constants.GreaterThanChar:
                    if (nesting == 0)
                    {
                        return false; // An expression body; inside braces it binds an Origin name.
                    }

                    i++;
                    continue;

                case Constants.GreaterThanChar when nesting == 0:
                    if (i + 1 < this.span.Length && this.span[i + 1] == Constants.EqualsChar && this.span[i - 1] != Constants.GreaterThanChar)
                    {
                        i++; // '>=' is one token and closes nothing.
                        continue;
                    }

                    if (--depth == 0)
                    {
                        return true;
                    }

                    continue;

                case Constants.SemicolonChar when nesting > 0 || this.IsDeclarationGenericContext():
                    continue; // No statement ends inside a grouping or a declaration's generic list; the semicolon is reported there.

                case Constants.SemicolonChar or Constants.EqualsChar or '"' or '\'':
                    return false;
            }
        }

        return false;
    }

    // The first character of the next line after the line break at span[index] that holds more than spaces and a line comment,
    // with that line's indentation; -1 at the end of the source.
    private readonly int NextEffectiveLine(int index, out int indent)
    {
        var i = index;
        while (true)
        {
            i += this.span[i] == Constants.CrChar && i + 1 < this.span.Length && this.span[i + 1] == Constants.LfChar ? 2 : 1;
            indent = CountSpaces(this.span[i..]);
            i += indent;
            if (i >= this.span.Length)
            {
                return -1;
            }

            if (this.span[i] is Constants.CrChar or Constants.LfChar)
            {
                continue;
            }

            if (this.span[i] == Constants.SlashChar && i + 1 < this.span.Length && this.span[i + 1] == Constants.SlashChar)
            {
                var end = this.span[i..].IndexOfAny(Constants.CrChar, Constants.LfChar);
                if (end < 0)
                {
                    return -1;
                }

                i += end;
                continue;
            }

            return i;
        }
    }

    private readonly bool IsDeclarationGenericContext()
    {
        if (this.indentCount > 0 && this.indentStack[this.indentCount - 1].Source == IndentSource.AngleBracket)
        {
            return true;
        }

        for (var i = this.tokenCount - 2; i >= 0; i--)
        {
            var kind = this.tokens[i].Kind;
            if (kind is TokenKind.Colon or TokenKind.MinusGreaterThan or TokenKind.Func or TokenKind.Struct or TokenKind.Enum)
            {
                return true;
            }

            if (!(kind.IsIdentifierOrContextualKeyword() || kind.IsPrimitiveType() ||
                kind is TokenKind.Dot or TokenKind.ColonColon or TokenKind.Slash or TokenKind.OpenParenthesis or TokenKind.Comma))
            {
                return false;
            }
        }

        return false;
    }

    // Delimiters opened on a body header share its baseline. Their content does
    // not add a second indentation level to the executable body.
    private void ShareHeaderDelimiterIndent(int indentLevel)
    {
        if (this.indentCount == 0 || this.tokenCount == 0 || this.indentStack[this.indentCount - 1].Source == IndentSource.AngleBracket)
        {
            return;
        }

        var last = this.tokens[this.tokenCount - 1].Span.Start;
        var lineStart = this.sourceText[..last].LastIndexOfAny('\r', '\n') + 1;
        var headerIndent = (CountSpaces(this.sourceText[lineStart..]) / Constants.IndentationSpaces) - this.indentationOffset;
        if (indentLevel != headerIndent + 1)
        {
            return;
        }

        var nesting = 0;
        var body = false;
        for (var i = this.tokenCount - 1; i >= 0 && this.tokens[i].Span.Start >= lineStart; i--)
        {
            var kind = this.tokens[i].Kind;
            if (kind is TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace)
            {
                nesting++;
            }
            else if (kind is TokenKind.OpenParenthesis or TokenKind.OpenBracket or TokenKind.OpenBrace)
            {
                if (nesting-- == 0)
                {
                    break;
                }
            }
            else if (nesting == 0)
            {
                if (kind == TokenKind.EqualsGreaterThan)
                {
                    break;
                }

                if (kind is TokenKind.If or TokenKind.Else or TokenKind.Match or TokenKind.Switch or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Do or TokenKind.Func or TokenKind.Defer ||
                    (kind == TokenKind.Identifier && this.sourceText.Slice(this.tokens[i].Span.Start, this.tokens[i].Span.Length).SequenceEqual("unsafe")))
                {
                    body = true;
                    break;
                }
            }
        }

        if (!body)
        {
            return;
        }

        for (var i = this.indentCount - 1; i >= 0; i--)
        {
            var entry = this.indentStack[i];
            if (entry.Position < lineStart || entry.Source is IndentSource.Block or IndentSource.LineContinuation)
            {
                break;
            }

            if (!entry.SharesBodyIndent)
            {
                this.indentStack[i] = entry with { SharesBodyIndent = true };
                this.sharedBodyIndentDepth++;
            }
        }
    }

    private bool PreviousLineStartsBody()
    {
        if (this.tokenCount > 0 && this.tokens[this.tokenCount - 1].Kind is TokenKind.Colon or TokenKind.EqualsGreaterThan)
        {
            // A named alias arrow introduces a Container reference, never a Body.
            for (var i = this.tokenCount - 2; i >= 0; i--)
            {
                var kind = this.tokens[i].Kind;
                if (kind == TokenKind.Alias)
                {
                    return false;
                }

                if (kind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock)
                {
                    break;
                }
            }

            return true;
        }

        // Inspect only the preceding logical header; no source strings are created.
        // Keywords inside delimiters belong to nested expressions, and an outer "=>" has already
        // supplied a single-item body, whose expression may continue with a leading dot (SPEC 2.2.2).
        var nesting = 0;
        for (var i = this.tokenCount - 1; i >= 0; i--)
        {
            var kind = this.tokens[i].Kind;
            if (kind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock)
            {
                break;
            }

            if (kind is TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace)
            {
                nesting++;
                continue;
            }

            if (kind is TokenKind.OpenParenthesis or TokenKind.OpenBracket or TokenKind.OpenBrace)
            {
                if (nesting-- == 0)
                {
                    break;
                }

                continue;
            }

            if (nesting > 0)
            {
                continue;
            }

            if (kind == TokenKind.EqualsGreaterThan)
            {
                return false;
            }

            if (kind == TokenKind.Identifier && this.sourceText.Slice(this.tokens[i].Span.Start, this.tokens[i].Span.Length).SequenceEqual("unsafe"))
            {
                return true;
            }

            if (kind is TokenKind.Match or TokenKind.Switch or TokenKind.If or TokenKind.Else or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Func or TokenKind.Do or TokenKind.Defer or TokenKind.Require)
            {
                return true;
            }
        }

        return false;
    }

    private void ReadLiteralKeywordOrIdentifier()
    {
        var span = this.span;
        if ((uint)(span[0] - '0') <= 9 && this.TryReadNumberLiteral(span))
        {
            return;
        }

        // Scan and validate ordinary names together; Unicode takes the shared slow path.
        var length = ScanAsciiIdentifierLength(span);
        if (length < span.Length && !TokenHelper.IsSeparator(span[length]))
        {
            this.ReadNonAsciiIdentifier(span);
            return;
        }

        if (length == 0)
        {
            this.ReadInvalidCharacter(span);
            return;
        }

        this.AddTokenAndSlice(TokenHelper.GetKeywordOrIdentifierKind(span.Slice(0, length)), length);
    }

    /// <summary>Reads a literal that starts with a digit.</summary>
    /// <returns><see langword="false"/> when no literal text was consumed and the identifier path applies.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryReadNumberLiteral(ReadOnlySpan<char> span)
    {
        if (this.tokenCount > 0 && this.tokens[this.tokenCount - 1].Kind == TokenKind.Dot)
        {
            var digits = 1;
            while (digits < span.Length && span[digits] is >= '0' and <= '9')
            {
                digits++;
            }

            this.AddTokenAndSlice(TokenKind.NumericLiteral, digits);
            return true;
        }

        if (NumberLiteralHelper.ScanNumberLiteral(span, out var numberLiteralLength))
        {// Numeric literal
            this.AddTokenAndSlice(TokenKind.NumericLiteral, numberLiteralLength);
            return true;
        }
        else if (numberLiteralLength > 0)
        {// Starts with a digit but is not a valid numeric literal.
         // Emit a single Invalid token with a diagnostic instead of silently falling back
         // to the identifier path, which would produce bogus Identifier tokens.
            this.Report(this.NewRange(numberLiteralLength), DiagnosticCode.InvalidNumericLiteral_Kd);
            this.AddTokenAndSlice(TokenKind.Invalid, numberLiteralLength);
            return true;
        }

        return false;
    }

    /// <summary>Reads a name containing non-ASCII characters, validating it as a Unicode identifier.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReadNonAsciiIdentifier(ReadOnlySpan<char> span)
    {
        var length = TokenHelper.IndexOfSeparator(span);
        if (length < 0)
        {
            length = span.Length;
        }

        var spelling = span[..length];
        var kind = TokenHelper.GetKeywordOrIdentifierKind(spelling);
        if (!IdentifierHelper.IsValidIdentifier(spelling))
        {
            this.Report(this.NewRange(length), DiagnosticCode.InvalidIdentifier_Kd, spelling.ToString());
            kind = TokenKind.Invalid;
        }

        this.AddTokenAndSlice(kind, length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReadInvalidCharacter(ReadOnlySpan<char> span)
    {
        this.Report(this.NewRange(1), DiagnosticCode.InvalidCharacter_Kd, span[0]);
        this.AddTokenAndSlice(TokenKind.Invalid, 1);
    }

    private void ReadCharLiteral()
    {
        if (CharLiteralHelper.Scan(this.span, out var length))
        {
            this.AddTokenAndSlice(TokenKind.CharLiteral, length);
        }
        else
        {
            this.Report(this.NewRange(length), DiagnosticCode.MissingCharLiteralEnd_Kd);
            this.AddTokenAndSlice(TokenKind.Invalid, length);
        }
    }

    private void ReadStringLiteral()
    {
        var result = StringLiteralHelper.ScanStringLiteral(this.span, out var doubleQuoteCount, out var stringLiteralLength);
        if (result == ScanStringLiteralResult.String)
        {// Like every other literal, the token spans the literal as written, delimiters included.
            this.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
        }
        else if (result == ScanStringLiteralResult.Interpolation)
        {
            this.AddTokenAndSlice(TokenKind.InterpolatedStringLiteral, stringLiteralLength);
        }
        else
        {// Invalid
            var opening = this.NewRange(doubleQuoteCount);
            this.Report(opening, DiagnosticCode.MissingStringLiteralEnd_Kd, new string('"', doubleQuoteCount));
            var cause = this.diagnostics.LastError!.Value;
            for (var i = 0; i < this.indentCount; i++)
            {
                var entry = this.indentStack[i];
                if (entry.Source is not (IndentSource.Block or IndentSource.LineContinuation))
                {
                    (this.delimiterRecoveryCauses ??= []).TryAdd(entry.Position, cause);
                }
            }

            // Retain the operand and its exact lexical subject for the parser's ErrorKoto. Dropping it would turn a
            // malformed argument into an absent argument and cause an unrelated overload-selection diagnostic.
            this.AddToken(new(TokenKind.Invalid, opening));
            this.Slice(stringLiteralLength);
        }
    }

    // Returns true when a comment crosses a physical line and ends the current line.
    private bool ReadMultiLineComment()
    {
        var crossedLine = false;
        while (true)
        {
            var length = this.span.IndexOf("*/");
            if (length < 0)
            {
                this.Report(this.NewRange(Math.Min(2, this.span.Length)), DiagnosticCode.MissingBlockCommentEnd_Kd);
                this.Slice(this.span.Length);
                return true;
            }

            crossedLine |= this.span[..length].IndexOfAny('\r', '\n') >= 0;
            this.Slice(length + 2);
            if (!crossedLine)
            {
                return false;
            }

            // Only spaces and comments may follow a multiline comment's terminator.
            this.Slice(CountSpaces(this.span));
            if (this.span.StartsWith("/*"))
            {
                continue;
            }

            if (!this.span.IsEmpty && this.span[0] is not ('\r' or '\n') && !this.span.StartsWith("//"))
            {
                this.Report(this.NewRange(1), DiagnosticCode.CodeAfterMultilineComment_Kd);
            }

            // Consume the closing line (including invalid trailing code for recovery).
            // The next physical line is processed with ordinary indentation rules.
            this.ReadSingleLineComment();
            return true;
        }
    }

    private void ReadSingleLineComment()
    {// // Comment\n
        var idx = this.span.IndexOfAny('\r', '\n');
        if (this.CollectDocumentation && this.span.Length >= 3 && this.span[2] == '/' && (this.span.Length == 3 || this.span[3] != '/'))
        {
            this.Documentation ??= new(this.sourceDocument);
            this.Documentation.AddLine(this.position, this.position + (idx < 0 ? this.span.Length : idx));
        }

        if (idx < 0)
        {
            this.Slice(this.span.Length);
        }
        else
        {
            var newLineLength = this.span[idx] == '\r' && idx + 1 < this.span.Length && this.span[idx + 1] == '\n' ? 2 : 1;
            this.Slice(idx + newLineLength);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SourceSpan NewRange(int length)
        => new(this.position, length);

    // A lexical problem of this document, recorded through the target the parser also uses, so its recovery can rest on it.
    private void Report(SourceSpan range, DiagnosticCode code, object? argument = null)
        => this.diagnostics.Add(range, code, argument, null, this.sourceDocument);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Slice(int length)
    {
        this.span = this.span.Slice(length);
        this.position += length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Add(Token token)
    {
        var tokens = this.tokens;
        var count = this.tokenCount;
        if ((uint)count >= (uint)tokens.Length)
        {
            tokens = this.GrowTokens();
        }

        tokens[count] = token;
        this.tokenCount = count + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToken(Token token)
    {
        this.Add(token);
        this.tokenAdded++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddTokenAndSlice(TokenKind tokenKind, int length)
    {
        this.Add(new(tokenKind, this.position, length));
        this.tokenAdded++;

        this.span = this.span.Slice(length);
        this.position += length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Token[] GrowTokens()
    {
        var larger = ArrayPool<Token>.Shared.Rent(this.tokens.Length * 2);
        this.tokens.AsSpan(0, this.tokenCount).CopyTo(larger);
        ArrayPool<Token>.Shared.Return(this.tokens);
        this.tokens = larger;
        return larger;
    }

    private void PushIndentSource(TokenKind tokenKind)
        => this.PushIndentSource(tokenKind switch
        {
            TokenKind.StartBlock => IndentSource.Block,
            TokenKind.OpenParenthesis => IndentSource.Parenthesis,
            TokenKind.OpenBracket => IndentSource.Bracket,
            TokenKind.LessThan => IndentSource.AngleBracket,
            TokenKind.OpenBrace => IndentSource.Brace,
            _ => throw new UnreachableException(),
        });

    private void PushIndentSource(IndentSource indentSource, int headerLevel = -1)
    {
        if (this.indentCount == this.indentStack.Length)
        {
            var larger = ArrayPool<IndentEntry>.Shared.Rent(this.indentStack.Length * 2);
            this.indentStack.AsSpan().CopyTo(larger);
            ArrayPool<IndentEntry>.Shared.Return(this.indentStack);
            this.indentStack = larger;
        }

        this.indentStack[this.indentCount++] = new(indentSource, this.position, false, headerLevel);
        if (indentSource == IndentSource.Block)
        {
            this.blockDepth++;
        }
        else
        {
            this.nonBlockDepth++;
        }
    }

    // Closes up to count recognized Type argument lists at the top of the stack.
    private void CloseTypeArguments(int count)
    {
        while (count-- > 0 && this.indentCount > 0 && this.indentStack[this.indentCount - 1].Source == IndentSource.AngleBracket)
        {
            _ = this.PopIndentSource(TokenKind.GreaterThan);
        }
    }

    /// <summary>Closes the grouping that a closer ends, with the bodies and continuations still open inside it.</summary>
    /// <param name="expected">The closer read.</param>
    /// <returns><see langword="false"/> when the closer matches no open grouping: it is reported, and the grouping it does not match
    /// stays open.</returns>
    private bool PopIndentSource(TokenKind expected)
    {
        var closesBody = false;
        while (this.indentCount > 0)
        {
            var entry = this.indentStack[this.indentCount - 1];
            var indentSource = entry.Source;
            if (indentSource == IndentSource.AngleBracket && expected != TokenKind.GreaterThan)
            {
                // The '<' had no matching '>' before this enclosing closer after all: it is a comparison, not a grouping.
                this.indentCount--;
                this.nonBlockDepth--;
                if (entry.SharesBodyIndent)
                {
                    this.sharedBodyIndentDepth--;
                }

                this.DemoteTypeArgumentsOpen(entry.Position);
                continue;
            }

            if (indentSource == IndentSource.Block)
            {
                // A body opens only at a line start, so this closer shares a line with body content.
                // Close it for recovery, but a dedent must precede the outer closer (SPEC 2.2.1).
                if (!closesBody)
                {
                    closesBody = true;
                    this.Report(this.NewRange(1), DiagnosticCode.OuterCloserInBody_Kd);
                }

                this.indentCount--;
                this.AddToken(new(TokenKind.EndBlock, this.CurrentRange));
                this.blockDepth--;
                continue;
            }
            else if (indentSource == IndentSource.LineContinuation)
            {
                this.indentCount--;
                this.nonBlockDepth--;
                continue;
            }

            if (GetClosingTokenKind(indentSource) == expected)
            {
                if (entry.SharesBodyIndent)
                {
                    this.sharedBodyIndentDepth--;
                }

                this.indentCount--;
                this.nonBlockDepth--;
                return true;
            }

            break;
        }

        // Error recovery policy: the mismatched closer is treated as spurious and the
        // stack is left intact, so the still-open grouping can be matched (or reported)
        // later. e.g. "(]" reports an unmatched ']' and keeps '(' open.
        this.diagnostics.ReportSyntax(this.NewRange(1), DiagnosticCode.MisplacedSyntax_Kd, SyntaxForm.UnmatchedCloser, null, this.sourceDocument);
        return false;
    }

    // Marks the '<' at a position as a comparison.
    private void DemoteTypeArgumentsOpen(int position)
    {
        for (var t = this.tokenCount - 1; t >= 0; t--)
        {
            if (this.tokens[t].Start == position && this.tokens[t].Kind == TokenKind.LessThan)
            {
                this.tokens[t] = Token.LessThan(position, false);
                return;
            }
        }
    }

    /// <summary>Gets where a missing closer is inserted: right after the last written token, or a fallback when none was written.</summary>
    private SourceSpan InsertionPoint(SourceSpan fallback)
    {
        for (var t = this.tokenCount - 1; t >= 0; t--)
        {
            if (!this.tokens[t].IsMissing && this.tokens[t].Kind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
            {
                return new(this.tokens[t].Span.End, 0);
            }
        }

        return fallback;
    }

    private void ClearIndentStack()
    {
        var missingRange = this.InsertionPoint(this.CurrentRange);
        while (this.indentCount > 0)
        {
            var entry = this.indentStack[--this.indentCount];
            var indentSource = entry.Source;
            if (entry.SharesBodyIndent)
            {
                this.sharedBodyIndentDepth--;
            }

            if (indentSource == IndentSource.Block)
            {
                this.AddToken(new(TokenKind.EndBlock, missingRange, true));
                this.blockDepth--;
                continue;
            }

            this.nonBlockDepth--;
            if (indentSource != IndentSource.LineContinuation)
            {
                var closingKind = GetClosingTokenKind(indentSource);
                this.ReportMissingDelimiter(entry, missingRange, closingKind);
                this.AddToken(new(closingKind, missingRange, true));
            }
        }
    }

    private void ReportMissingDelimiter(IndentEntry entry, SourceSpan range, TokenKind closingKind)
    {
        if (this.delimiterRecoveryCauses?.TryGetValue(entry.Position, out var cause) == true)
        {
            this.diagnostics.AddDependentSyntax(range, closingKind.ToText(), cause, this.sourceDocument);
        }
        else
        {
            // The closer is missing at an insertion point; the grouping it closes is related evidence (SPEC 23.3.6.2), and the closer
            // inserted there is the repair candidate (SPEC 23.3.6.9).
            var opened = this.diagnostics.Relate("opening delimiter", new SourceSpan(entry.Position, 1), this.sourceDocument, "opened here");
            var closer = closingKind.ToText();
            var repairs = this.sourceDocument is null ? null : new DiagnosticRepairFact[] { new(RepairKind.InsertToken, [closer], [this.diagnostics.Edit(new(range.Start, 0), closer, this.sourceDocument)], RepairConditionSet.None) };
            this.diagnostics.ReportSyntax(range, DiagnosticCode.MissingSyntax_Kd, CloserForm(closingKind), null, this.sourceDocument, [opened], repairs);
        }
    }

    private bool TryCloseIndentSourceByCurrentToken(IndentSource indentSource)
    {
        if (this.span.IsEmpty)
        {
            return false;
        }

        var tokenKind = GetClosingTokenKind(indentSource);
        if (this.span[0] != tokenKind.ToText()[0])
        {
            return false;
        }

        this.AddTokenAndSlice(tokenKind, 1);
        this.nonBlockDepth--;
        return true;
    }
}
