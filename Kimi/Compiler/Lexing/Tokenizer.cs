// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        AngleBracket, // <>: Not supported yet because distinguishing generics from comparison operators is difficult.
        Brace, // {}
        LineContinuation, // Implicit continuation, such as a method chain line starting with ".".
    }

    private delegate bool CharacterHandler(ref Tokenizer tokenizer);

    private const int InitialIndentStackCapacity = 32;
    private const int MinimumTokenCapacity = 256;

    /// <summary>
    /// Dispatches common leading characters without a large branch chain in the read loop.
    /// A handler returns <see langword="true"/> when the logical line ends.
    /// Characters without a handler fall back to the single-char, literal, and identifier path.
    /// </summary>
    private static readonly CharacterHandler?[] CharacterHandlerTable;

    static Tokenizer()
    {
        CharacterHandlerTable = new CharacterHandler?[Constants.ExclusiveUpperBound];

        CharacterHandlerTable[Constants.CrChar] = (ref tokenizer) =>
        {// \r \r\n
            tokenizer.Slice(tokenizer.NextChar == Constants.LfChar ? 2 : 1);
            return true;
        };

        CharacterHandlerTable[Constants.LfChar] = (ref tokenizer) =>
        {// \n
            tokenizer.Slice(1);
            return true;
        };

        CharacterHandlerTable[Constants.AmpersandChar] = (ref tokenizer) =>
        {// & && &=
            var next = tokenizer.NextChar;
            if (next == Constants.AmpersandChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.AmpersandAmpersand, 2);
            }
            else if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.AmpersandEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Ampersand, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.AsteriskChar] = (ref tokenizer) =>
        {// * *=
            if (tokenizer.NextChar == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.AsteriskEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Asterisk, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.BarChar] = (ref tokenizer) =>
        {// | || |=
            var next = tokenizer.NextChar;
            if (next == Constants.BarChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.BarBar, 2);
            }
            else if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.BarEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Bar, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.CaretChar] = (ref tokenizer) =>
        {// ^ ^=
            if (tokenizer.NextChar == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.CaretEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Caret, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.DotChar] = (ref tokenizer) =>
        {// . .. ..=
            if (tokenizer.NextChar == Constants.DotChar)
            {
                if (tokenizer.span.Length >= 3 && tokenizer.span[2] == Constants.EqualsChar)
                {
                    tokenizer.AddTokenAndSlice(TokenKind.DotDotEquals, 3);
                }
                else
                {
                    tokenizer.AddTokenAndSlice(TokenKind.DotDot, 2);
                }
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Dot, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.EqualsChar] = (ref tokenizer) =>
        {// = == =>
            var next = tokenizer.NextChar;
            if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.EqualsEquals, 2);
            }
            else if (next == Constants.GreaterThanChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.EqualsGreaterThan, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Equals, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.ExclamationChar] = (ref tokenizer) =>
        {// ! !=
            if (tokenizer.NextChar == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.ExclamationEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Exclamation, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.GreaterThanChar] = (ref tokenizer) =>
        {// > >= >> >>=
            var next = tokenizer.NextChar;
            if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.GreaterThanEquals, 2);
            }
            else if (next == Constants.GreaterThanChar)
            {
                if (tokenizer.span.Length >= 3 && tokenizer.span[2] == Constants.EqualsChar)
                {
                    tokenizer.AddTokenAndSlice(TokenKind.GreaterThanGreaterThanEquals, 3);
                }
                else
                {
                    tokenizer.AddTokenAndSlice(TokenKind.GreaterThanGreaterThan, 2);
                }
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.GreaterThan, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.LessThanChar] = (ref tokenizer) =>
        {// < <= << <<=
            var next = tokenizer.NextChar;
            if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.LessThanEquals, 2);
            }
            else if (next == Constants.LessThanChar)
            {
                if (tokenizer.span.Length >= 3 && tokenizer.span[2] == Constants.EqualsChar)
                {
                    tokenizer.AddTokenAndSlice(TokenKind.LessThanLessThanEquals, 3);
                }
                else
                {
                    tokenizer.AddTokenAndSlice(TokenKind.LessThanLessThan, 2);
                }
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.LessThan, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.MinusChar] = (ref tokenizer) =>
        {// - -- -= ->
            var next = tokenizer.NextChar;
            if (next == Constants.MinusChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.MinusMinus, 2);
            }
            else if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.MinusEquals, 2);
            }
            else if (next == Constants.GreaterThanChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.MinusGreaterThan, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Minus, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.PercentChar] = (ref tokenizer) =>
        {// % %=
            if (tokenizer.NextChar == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.PercentEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Percent, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.PlusChar] = (ref tokenizer) =>
        {// + ++ +=
            var next = tokenizer.NextChar;
            if (next == Constants.PlusChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.PlusPlus, 2);
            }
            else if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.PlusEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Plus, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.SlashChar] = (ref tokenizer) =>
        {// / // /* /=
            var next = tokenizer.NextChar;
            if (next == Constants.SlashChar)
            {// Single line comment
                tokenizer.ReadSingleLineComment();
                return true;
            }

            if (next == Constants.AsteriskChar)
            {// Multi line comment
                tokenizer.ReadMultiLineComment();
            }
            else if (next == Constants.EqualsChar)
            {
                tokenizer.AddTokenAndSlice(TokenKind.SlashEquals, 2);
            }
            else
            {
                tokenizer.AddTokenAndSlice(TokenKind.Slash, 1);
            }

            return false;
        };

        CharacterHandlerTable[Constants.AtChar] = (ref tokenizer) =>
        {// @
            tokenizer.AddTokenAndSlice(TokenKind.At, 1);
            return false;
        };

        CharacterHandlerTable['"'] = (ref tokenizer) =>
        {// "Text" or """Text"""
            tokenizer.ReadStringLiteral();
            return false;
        };

        CharacterHandlerTable['\''] = (ref tokenizer) =>
        {
            tokenizer.ReadCharLiteral();
            return false;
        };
    }

    #region FieldAndProperty

    private readonly DiagnosticCollection diagnostics;
    private readonly SourceDocument sourceDocument;
    private readonly ReadOnlySpan<char> sourceText;
    private readonly int indentationOffset;
    private Token[] tokens;
    private int tokenCount;
    private IndentSource[] indentStack;
    private int indentCount;
    private ReadOnlySpan<char> span;
    private int position;
    private int currentIndentLevel;
    private int blockDepth;
    private int nonBlockDepth;
    private int tokenAdded;

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

        this.indentStack = ArrayPool<IndentSource>.Shared.Rent(InitialIndentStackCapacity);

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
            ArrayPool<IndentSource>.Shared.Return(this.indentStack);
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

        while (this.span.Length > 0)
        {
            while (this.span[0] == Constants.SpaceChar)
            {// Skip spaces
                this.Slice(1);
                if (this.span.Length == 0)
                {// End-of-file
                    goto EndOfFile;
                }
            }

            // span.Length >= 1
            var c = this.span[0];
            if (c < Constants.ExclusiveUpperBound &&
                CharacterHandlerTable[c] is { } handler)
            {
                if (handler(ref this))
                {// The logical line ended.
                    goto NextLine;
                }
            }
            else if (TokenHelper.TryGetSingleCharTokenKind(c, out var tokenKind, out var groupingDepth))
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
        var numberOfSpaces = BaseHelper.CountLeadingSpaces(this.span);
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
                this.ReadMultiLineComment();

                // Skip spaces after the comment WITHOUT counting them as indentation;
                // the indentation of this line was already measured at the line start
                // (this prevents a bogus InvalidIndentation diagnostic for "/* c */ foo").
                // If the comment spanned multiple physical lines, code following the
                // closing "*/" inherits the indentation of the line that opened it.
                this.Slice(BaseHelper.CountLeadingSpaces(this.span));
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
        if (this.currentIndentLevel < 0)
        {
            this.currentIndentLevel = indentLevel;
        }

        // Indentation remains significant even inside grouping constructs.
        // Therefore, both block depth and non-block depth are subtracted when
        // calculating the indentation difference.
        var indentDelta = indentLevel - this.currentIndentLevel - this.blockDepth - this.nonBlockDepth;

        if (indentDelta == 1)
        {
            // A line that starts with "." is treated as a continuation of the previous
            // expression. It contributes one required indentation level, like grouping
            // constructs, but does not require an explicit closing token.
            if (this.span[0] == Constants.DotChar)
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

            // TODO: Consider reporting KimiDiagnostic.UnexpectedIndent_Kd when dif > 1.
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
                    var indentSource = this.indentStack[--this.indentCount];
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
                        this.Slice(BaseHelper.CountLeadingSpaces(this.span));

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
                this.diagnostics.Add(new(indentationStart, indentationLength), DiagnosticCode.IndentationLevelMismatchWarning_Kd);

                goto Loop;
            }

            this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            separatorInserted = true;
        }

        if (this.nonBlockDepth > 0)
        {
            goto Loop;
        }
        else
        {
            this.currentIndentLevel += this.blockDepth;
            this.blockDepth = 0;

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

    private void ReadLiteralKeywordOrIdentifier()
    {
        if (NumberLiteralHelper.ScanNumberLiteral(this.span, out var numberLiteralLength))
        {// Numeric literal
            this.AddTokenAndSlice(TokenKind.NumericLiteral, numberLiteralLength);
            return;
        }
        else if (numberLiteralLength > 0)
        {// Starts with a digit but is not a valid numeric literal.
         // Emit a single Invalid token with a diagnostic instead of silently falling back
         // to the identifier path, which would produce bogus Identifier tokens.
            this.diagnostics.Add(this.NewRange(numberLiteralLength), DiagnosticCode.InvalidNumericLiteral_Kd);
            this.AddTokenAndSlice(TokenKind.Invalid, numberLiteralLength);
            return;
        }

        // Keyword or Identifier
        var length = TokenHelper.IndexOfSeparator(this.span);
        if (length < 0)
        {
            length = this.span.Length;
        }
        else if (length == 0)
        {
            this.diagnostics.Add(this.NewRange(1), DiagnosticCode.InvalidCharacter_Kd, this.span[0]);
            this.AddTokenAndSlice(TokenKind.Invalid, 1);
            return;
        }

        this.AddTokenAndSlice(TokenHelper.GetKeywordOrIdentifierKind(this.span.Slice(0, length)), length);
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

    private void ReadMultiLineComment()
    {
        var length = this.span.IndexOf("*/");
        if (length < 0)
        {
            this.diagnostics.Add(this.NewRange(Math.Min(2, this.span.Length)), DiagnosticCode.MissingBlockCommentEnd_Kd);
            this.Slice(this.span.Length);
            return;
        }

        this.Slice(length + 2);
    }

    private void ReadSingleLineComment()
    {// // Comment\n
        var idx = BaseHelper.IndexOfLfOrCrLf(this.span, out var newLineLength);
        if (idx < 0)
        {
            this.Slice(this.span.Length);
        }
        else
        {
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
            var larger = ArrayPool<IndentSource>.Shared.Rent(this.indentStack.Length * 2);
            this.indentStack.AsSpan().CopyTo(larger);
            ArrayPool<IndentSource>.Shared.Return(this.indentStack);
            this.indentStack = larger;
        }

        this.indentStack[this.indentCount++] = indentSource;
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
        while (this.indentCount > 0)
        {
            var indentSource = this.indentStack[this.indentCount - 1];
            if (indentSource == IndentSource.Block)
            {
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
            var indentSource = this.indentStack[--this.indentCount];
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
