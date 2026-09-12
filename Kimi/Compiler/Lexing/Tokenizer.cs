// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Kimi.Compiler.Helper;
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

    private readonly record struct IndentEntry(IndentSource Source, int Position, bool SharesBodyIndent = false);

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
    private int genericLookaheadEnd;

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

        diagnostics.SetSourceDocument(sourceDocument);
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
    /// Returns the generated tokens as a sequence. The sequence is valid until <see cref="Dispose"/> is called.
    /// </summary>
    /// <returns>The generated tokens.</returns>
    public ReadOnlySequence<Token> ToReadOnlySequence()
        => new(this.tokens.AsMemory(0, this.tokenCount));

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
                this.diagnostics.Add(new SourceSpan(offset, 1), DiagnosticCode.InvalidSourceEncoding_Kd);
                return;
            }

            offset += 2;
        }

        this.currentIndentLevel = 0;
        do
        {
            this.Read();
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

    private int Read()
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
                {
                    TokenHelper.TryGetSingleCharTokenKind(c, out var singleKind, out var singleDepth);
                    if (singleDepth > 0)
                    {
                        this.PushIndentSource(singleKind);
                    }
                    else if (singleDepth < 0)
                    {
                        this.PopIndentSource(singleKind);
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
                    this.diagnostics.Add(this.NewRange(1), DiagnosticCode.SemicolonNotAllowed_Kd);
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
                        if (this.indentCount > 0 && this.indentStack[this.indentCount - 1].Source == IndentSource.AngleBracket)
                        {
                            this.PopIndentSource(TokenKind.GreaterThan);
                            this.AddTokenAndSlice(TokenKind.GreaterThan, 1);
                            continue;
                        }

                        var next = this.NextChar;
                        if (next == Constants.EqualsChar)
                        {
                            this.AddTokenAndSlice(TokenKind.GreaterThanEquals, 2);
                        }
                        else if (next == Constants.GreaterThanChar)
                        {
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
                            if (this.IsGenericOpen())
                            {
                                this.PushIndentSource(IndentSource.AngleBracket);
                            }

                            this.AddTokenAndSlice(TokenKind.LessThan, 1);
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
                    if (TokenHelper.TryGetSingleCharTokenKind(c, out var tokenKind, out var groupingDepth))
                    {// Single char token
                        if (groupingDepth > 0)
                        {
                            this.PushIndentSource(tokenKind);
                        }
                        else if (groupingDepth < 0)
                        {
                            this.PopIndentSource(tokenKind);
                        }

                        this.AddTokenAndSlice(tokenKind, 1);
                    }
                    else
                    {// Number literal, keyword, or identifier
                        this.ReadLiteralKeywordOrIdentifier();
                    }

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
            this.diagnostics.Add(new(indentationStart, indentationLength), DiagnosticCode.InvalidIndentation_Kd, Constants.IndentationSpaces);
            numberOfSpaces += Constants.IndentationSpaces - unnecessarySpaces;
        }

        var indentLevel = (numberOfSpaces / Constants.IndentationSpaces) - this.indentationOffset;
        if (this.tokenCount == 0 && indentLevel > 0)
        {
            this.diagnostics.Add(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatch_Kd);
            indentLevel = 0;
        }

        if (this.currentIndentLevel < 0)
        {
            this.currentIndentLevel = indentLevel;
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
            else if (this.span.Length > 1 &&
                (this.span[0] == Constants.EqualsChar || this.span[0] == Constants.MinusChar) &&
                this.span[1] == Constants.GreaterThanChar)
            {// => or ->
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
                this.diagnostics.Add(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatch_Kd);
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
            // remove it from the indentation stack, and report an indentation mismatch.

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
                        // Close the malformed delimiter before the next source item.
                        this.AddToken(new(GetClosingTokenKind(indentSource), this.CurrentRange, true));

                        this.diagnostics.Add(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatch_Kd);
                        indentationMismatch = true;
                        break;
                    }
                }
                else if (this.currentIndentLevel > 0)
                {
                    this.AddToken(new(TokenKind.EndBlock, this.CurrentRange));
                    this.currentIndentLevel--;
                }
                else
                {
                    this.diagnostics.Add(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatch_Kd);
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
                // ReadAll will not call Read again to close the enclosing bodies.
                goto EndOfFile;
            }

            if (this.tokenAdded > 0 && !separatorInserted)
            {
                this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            }

            return this.tokenAdded;
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

        return this.tokenAdded;
    }

    private bool IsGenericOpen()
    {
        if (this.tokenCount == 0)
        {
            return false;
        }

        var previous = this.tokens[this.tokenCount - 1];
        if (!(previous.Kind.IsIdentifierOrContextualKeyword() || previous.Kind.IsPrimitiveType() || previous.Kind == TokenKind.Self) ||
            (previous.Span.End != this.position && !this.IsDeclarationGenericContext()))
        {
            return false;
        }

        if (this.position < this.genericLookaheadEnd)
        {
            return true;
        }

        // Cache balanced lookahead so nested arguments do not rescan the same suffix.
        var depth = 1;
        for (var i = 1; i < this.span.Length; i++)
        {
            var c = this.span[i];
            if (c == '/' && i + 1 < this.span.Length && this.span[i + 1] == '/')
            {
                var end = this.span[(i + 2)..].IndexOfAny('\r', '\n');
                if (end < 0)
                {
                    return false;
                }

                i += end + 1;
            }
            else if (c == '/' && i + 1 < this.span.Length && this.span[i + 1] == '*')
            {
                var end = this.span[(i + 2)..].IndexOf("*/");
                if (end < 0)
                {
                    return false;
                }

                i += end + 3;
            }
            else if (c == '<')
            {
                depth++;
            }
            else if (c == '=' && i + 1 < this.span.Length && this.span[i + 1] == '>')
            {
                i++;
            }
            else if (c == '>' && this.span[i - 1] != '-')
            {
                if (--depth == 0)
                {
                    this.genericLookaheadEnd = this.position + i;
                    return true;
                }
            }
            else if (c is ';' or '=' or '"' or '\'' or '{' or '}')
            {
                return false;
            }
        }

        return false;
    }

    private bool IsDeclarationGenericContext()
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

                if (kind is TokenKind.If or TokenKind.Else or TokenKind.Match or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Do or TokenKind.Func or TokenKind.Defer ||
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

            if (kind is TokenKind.Match or TokenKind.If or TokenKind.Else or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Func or TokenKind.Do or TokenKind.Defer or TokenKind.Require)
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
            this.diagnostics.Add(this.NewRange(numberLiteralLength), DiagnosticCode.InvalidNumericLiteral_Kd);
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
            this.diagnostics.Add(this.NewRange(length), DiagnosticCode.InvalidIdentifier_Kd, spelling.ToString());
            kind = TokenKind.Invalid;
        }

        this.AddTokenAndSlice(kind, length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReadInvalidCharacter(ReadOnlySpan<char> span)
    {
        this.diagnostics.Add(this.NewRange(1), DiagnosticCode.InvalidCharacter_Kd, span[0]);
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
            this.diagnostics.Add(this.NewRange(length), DiagnosticCode.MissingCharLiteralEnd_Kd);
            this.AddTokenAndSlice(TokenKind.Invalid, length);
        }
    }

    private void ReadStringLiteral()
    {
        var result = StringLiteralHelper.ScanStringLiteral(this.span, out var doubleQuoteCount, out var stringLiteralLength);
        if (result is ScanStringLiteralResult.String or ScanStringLiteralResult.MultilineString)
        {// "Text" -> Text
            if (doubleQuoteCount == 1)
            {
                this.Slice(1);
                stringLiteralLength -= 2;
                this.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
                this.Slice(1);
            }
            else
            {
                this.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
            }
        }
        else if (result is ScanStringLiteralResult.Interpolation or ScanStringLiteralResult.MultilineInterpolation)
        {
            this.AddTokenAndSlice(TokenKind.InterpolatedStringLiteral, stringLiteralLength);
        }
        else
        {// Invalid
            this.diagnostics.Add(this.NewRange(1), DiagnosticCode.MissingStringLiteralEnd_Kd);
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
                this.diagnostics.Add(this.NewRange(Math.Min(2, this.span.Length)), DiagnosticCode.MissingBlockCommentEnd_Kd);
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
                this.diagnostics.Add(this.NewRange(1), DiagnosticCode.CodeAfterMultilineComment_Kd);
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

    private void PushIndentSource(IndentSource indentSource)
    {
        if (this.indentCount == this.indentStack.Length)
        {
            var larger = ArrayPool<IndentEntry>.Shared.Rent(this.indentStack.Length * 2);
            this.indentStack.AsSpan().CopyTo(larger);
            ArrayPool<IndentEntry>.Shared.Return(this.indentStack);
            this.indentStack = larger;
        }

        this.indentStack[this.indentCount++] = new(indentSource, this.position);
        if (indentSource == IndentSource.Block)
        {
            this.blockDepth++;
        }
        else
        {
            this.nonBlockDepth++;
        }
    }

    private void PopIndentSource(TokenKind expected)
    {
        var closesBody = false;
        while (this.indentCount > 0)
        {
            var entry = this.indentStack[this.indentCount - 1];
            var indentSource = entry.Source;
            if (indentSource == IndentSource.Block)
            {
                // A body opens only at a line start, so this closer shares a line with body content.
                // Close it for recovery, but a dedent must precede the outer closer (SPEC 2.2.1).
                if (!closesBody)
                {
                    closesBody = true;
                    this.diagnostics.Add(this.NewRange(1), DiagnosticCode.OuterCloserInBody_Kd);
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
                return;
            }

            break;
        }

        // Error recovery policy: the mismatched closer is treated as spurious and the
        // stack is left intact, so the still-open grouping can be matched (or reported)
        // later. e.g. "(]" reports an unmatched ']' and keeps '(' open.
        var diagnostic = expected switch
        {
            TokenKind.CloseParenthesis => DiagnosticCode.UnmatchedParenthesis_Kd,
            TokenKind.CloseBrace => DiagnosticCode.UnmatchedBrace_Kd,
            TokenKind.GreaterThan => DiagnosticCode.UnmatchedAngleBracket_Kd,
            _ => DiagnosticCode.UnmatchedBracket_Kd,
        };

        this.diagnostics.Add(this.NewRange(1), diagnostic);
    }

    private void ClearIndentStack()
    {
        var missingRange = this.CurrentRange;
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
                this.diagnostics.Add(missingRange, DiagnosticCode.MissingExpectedToken_Kd, closingKind.ToText());
                this.AddToken(new(closingKind, missingRange, true));
            }
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
