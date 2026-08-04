// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Helper;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Lexing;

/// <summary>
/// Converts Kimi source text into a sequence of lexical tokens and indentation tokens.
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

    private readonly DiagnosticCollection urlDiagnostic;
    private readonly Stack<IndentSource> indentStack = new();

    private ReadOnlyMemory<char> text;
    private int position;
    private int line;
    private int character;

    private int blockDepth;
    private int nonBlockDepth;
    private int tokenAdded;

    public SourceRange CurrentRange => new(new(this.line, this.character), new(this.line, this.character));

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="Tokenizer"/> class.
    /// </summary>
    /// <param name="urlDiagnostic">The diagnostic sink used to report lexical errors.</param>
    public Tokenizer(DiagnosticCollection urlDiagnostic)
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

    public TokenSequenceBuilder ReadAll(ref TokenSequenceBuilder builder)
    {
        var currentIndentLevel = 0;
        while (this.Read(ref currentIndentLevel, ref builder) > 0)
        {
            // builder.Add(new(TokenKind.Separator));
        }

        return builder;
    }

    /// <summary>
    /// Reads the next logical line and returns its tokens.<br/>
    /// NOTE: The returned list is an internal buffer that is cleared and reused by the next call to
    /// <see cref="Read"/> or <see cref="Initialize"/>. Callers must consume (or copy) the tokens before
    /// invoking this tokenizer again.
    /// </summary>
    /// <param name="currentIndentLevel">The current logical indentation level. The value is updated as blocks are opened or closed.</param>
    /// <param name="builder">
    /// The token sequence builder that receives all tokens emitted during this read operation.
    /// </param>
    /// <returns>The internal token buffer containing the tokens read for the next logical line.</returns>
    public int Read(ref int currentIndentLevel, ref TokenSequenceBuilder builder)
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
                        this.AddTokenAndSlice(ref builder, TokenKind.Ampersand, ref span, 1);
                    }
                    else if (span[1] == Constants.AmpersandChar)
                    {// &&
                        this.AddTokenAndSlice(ref builder, TokenKind.AmpersandAmpersand, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// &=
                        this.AddTokenAndSlice(ref builder, TokenKind.AmpersandEquals, ref span, 2);
                    }
                    else
                    {// &
                        this.AddTokenAndSlice(ref builder, TokenKind.Ampersand, ref span, 1);
                    }

                    break;

                case Constants.AsteriskChar: // * *=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// *=
                        this.AddTokenAndSlice(ref builder, TokenKind.AsteriskEquals, ref span, 2);
                    }
                    else
                    {// *
                        this.AddTokenAndSlice(ref builder, TokenKind.Asterisk, ref span, 1);
                    }

                    break;

                case Constants.BarChar: // | || |=
                    if (span.Length == 1)
                    {// |
                        this.AddTokenAndSlice(ref builder, TokenKind.Bar, ref span, 1);
                    }
                    else if (span[1] == Constants.BarChar)
                    {// ||
                        this.AddTokenAndSlice(ref builder, TokenKind.BarBar, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// |=
                        this.AddTokenAndSlice(ref builder, TokenKind.BarEquals, ref span, 2);
                    }
                    else
                    {// |
                        this.AddTokenAndSlice(ref builder, TokenKind.Bar, ref span, 1);
                    }

                    break;

                case Constants.CaretChar: // ^ ^=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// ^=
                        this.AddTokenAndSlice(ref builder, TokenKind.CaretEquals, ref span, 2);
                    }
                    else
                    {// ^
                        this.AddTokenAndSlice(ref builder, TokenKind.Caret, ref span, 1);
                    }

                    break;

                case Constants.DotChar: // . .. ..=
                    if (span.Length == 1)
                    {// .
                        this.AddTokenAndSlice(ref builder, TokenKind.Dot, ref span, 1);
                    }
                    else if (span[1] == Constants.DotChar)
                    {// ..
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// ..=
                            this.AddTokenAndSlice(ref builder, TokenKind.DotDotEquals, ref span, 3);
                        }
                        else
                        {// ..
                            this.AddTokenAndSlice(ref builder, TokenKind.DotDot, ref span, 2);
                        }
                    }
                    else
                    {// .
                        this.AddTokenAndSlice(ref builder, TokenKind.Dot, ref span, 1);
                    }

                    break;

                case Constants.EqualsChar: // = == =>
                    if (span.Length == 1)
                    {// =
                        this.AddTokenAndSlice(ref builder, TokenKind.Equals, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// ==
                        this.AddTokenAndSlice(ref builder, TokenKind.EqualsEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// =>
                        this.AddTokenAndSlice(ref builder, TokenKind.EqualsGreaterThan, ref span, 2);
                    }
                    else
                    {// =
                        this.AddTokenAndSlice(ref builder, TokenKind.Equals, ref span, 1);
                    }

                    break;

                case Constants.ExclamationChar: // ! !=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// !=
                        this.AddTokenAndSlice(ref builder, TokenKind.ExclamationEquals, ref span, 2);
                    }
                    else
                    {// !
                        this.AddTokenAndSlice(ref builder, TokenKind.Exclamation, ref span, 1);
                    }

                    break;

                case Constants.GreaterThanChar: // > >= >> >>=
                    if (span.Length == 1)
                    {// >
                        this.AddTokenAndSlice(ref builder, TokenKind.GreaterThan, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// >=
                        this.AddTokenAndSlice(ref builder, TokenKind.GreaterThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// >>
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// >>=
                            this.AddTokenAndSlice(ref builder, TokenKind.GreaterThanGreaterThanEquals, ref span, 3);
                        }
                        else
                        {// >>
                            this.AddTokenAndSlice(ref builder, TokenKind.GreaterThanGreaterThan, ref span, 2);
                        }
                    }
                    else
                    {// >
                        this.AddTokenAndSlice(ref builder, TokenKind.GreaterThan, ref span, 1);
                    }

                    break;

                case Constants.LessThanChar: // < <= << <<=
                    if (span.Length == 1)
                    {// <
                        this.AddTokenAndSlice(ref builder, TokenKind.LessThan, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// <=
                        this.AddTokenAndSlice(ref builder, TokenKind.LessThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.LessThanChar)
                    {// <<
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// <<=
                            this.AddTokenAndSlice(ref builder, TokenKind.LessThanLessThanEquals, ref span, 3);
                        }
                        else
                        {// <<
                            this.AddTokenAndSlice(ref builder, TokenKind.LessThanLessThan, ref span, 2);
                        }
                    }
                    else
                    {// <
                        this.AddTokenAndSlice(ref builder, TokenKind.LessThan, ref span, 1);
                    }

                    break;

                case Constants.MinusChar: // -- -= -
                    if (span.Length == 1)
                    {// -
                        this.AddTokenAndSlice(ref builder, TokenKind.Minus, ref span, 1);
                    }
                    else if (span[1] == Constants.MinusChar)
                    {// --
                        this.AddTokenAndSlice(ref builder, TokenKind.MinusMinus, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// -=
                        this.AddTokenAndSlice(ref builder, TokenKind.MinusEquals, ref span, 2);
                    }
                    else
                    {// -
                        this.AddTokenAndSlice(ref builder, TokenKind.Minus, ref span, 1);
                    }

                    break;

                case Constants.PercentChar: // % %=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// %=
                        this.AddTokenAndSlice(ref builder, TokenKind.PercentEquals, ref span, 2);
                    }
                    else
                    {// %
                        this.AddTokenAndSlice(ref builder, TokenKind.Percent, ref span, 1);
                    }

                    break;

                case Constants.PlusChar: // ++ += +
                    if (span.Length == 1)
                    {// +
                        this.AddTokenAndSlice(ref builder, TokenKind.Plus, ref span, 1);
                    }
                    else if (span[1] == Constants.PlusChar)
                    {// ++
                        this.AddTokenAndSlice(ref builder, TokenKind.PlusPlus, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// +=
                        this.AddTokenAndSlice(ref builder, TokenKind.PlusEquals, ref span, 2);
                    }
                    else
                    {// +
                        this.AddTokenAndSlice(ref builder, TokenKind.Plus, ref span, 1);
                    }

                    break;

                case Constants.SlashChar: // // /* /= /
                    if (span.Length == 1)
                    {// /
                        this.AddTokenAndSlice(ref builder, TokenKind.Slash, ref span, 1);
                    }
                    else if (span[1] == Constants.SlashChar)
                    {// //
                        if (this.ReadSingleLineComment(ref builder, ref span))
                        {
                            this.NextLine();
                        }

                        goto NextLine;
                    }
                    else if (span[1] == Constants.AsteriskChar)
                    {// /*
                        this.ReadMultiLineComment(ref builder, ref span);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// /=
                        this.AddTokenAndSlice(ref builder, TokenKind.SlashEquals, ref span, 2);
                    }
                    else
                    {// /
                        this.AddTokenAndSlice(ref builder, TokenKind.Slash, ref span, 1);
                    }

                    break;

                case Constants.AtChar: // @Identifier
                    {
                        var length = TokenHelper.IndexOfSeparator(span.Slice(1));
                        if (length < 0)
                        {
                            length = span.Length;
                        }
                        else
                        {
                            length++;
                        }

                        this.AddTokenAndSlice(ref builder, TokenKind.Identifier, ref span, length);

                        break;
                    }

                case '"':
                    {
                        var result = StringLiteralHelper.ScanStringLiteral(span, out var doubleQuoteCount, out var stringLiteralLength);
                        if (result == ScanStringLiteralResult.String)
                        {
                            this.AddTokenAndSlice(ref builder, TokenKind.StringLiteral, ref span, stringLiteralLength);
                        }
                        else if (result == ScanStringLiteralResult.MultilineString)
                        {
                            this.AddTokenAndSliceWithLineTracking(ref builder, TokenKind.StringLiteral, ref span, stringLiteralLength);
                        }
                        else
                        {// Invalid
                            this.urlDiagnostic.Add(this.NewRange(1), Hashed.Kimi.MissingStringLiteralEnd);
                            this.AddTokenAndSlice(ref builder, TokenKind.Invalid, ref span, stringLiteralLength);
                        }

                        break;
                    }

                default:
                    {// Single char token, Number literal, String literal, Keyword, Identifier
                        if (TokenHelper.TryGetSingleCharTokenKind(span[0], out var tokenKind, out var depth))
                        {// Single char token
                            if (depth > 0)
                            {
                                this.PushIndentSource(tokenKind);
                            }
                            else if (depth < 0)
                            {
                                this.PopIndentSource(ref builder, tokenKind);
                            }

                            this.AddTokenAndSlice(ref builder, tokenKind, ref span, 1);
                            continue;
                        }

                        if (NumberLiteralHelper.ScanNumberLiteral(span, out var numberLiteralLength))
                        {// Numeric literal
                         // If the current position starts a numeric literal, scan the entire numeric literal before checking separators.
                            this.AddTokenAndSlice(ref builder, TokenKind.NumericLiteral, ref span, numberLiteralLength);
                        }
                        else if (numberLiteralLength > 0)
                        {// Starts with a digit but is not a valid numeric literal (e.g. "0x", "1e+", "1.0u8", "123abc").
                         // Emit a single Invalid token with a diagnostic instead of silently falling back
                         // to the identifier path, which would produce bogus Identifier tokens.
                            this.urlDiagnostic.Add(this.NewRange(numberLiteralLength), Hashed.Kimi.InvalidNumericLiteral);
                            this.AddTokenAndSlice(ref builder, TokenKind.Invalid, ref span, numberLiteralLength);
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
                                    this.AddTokenAndSliceWithLineTracking(ref builder, TokenKind.Invalid, ref span, invalidLength);
                                }
                                else
                                {
                                    this.AddTokenAndSlice(ref builder, TokenKind.Invalid, ref span, invalidLength);
                                }
                            }
                            else if (quoteCount >= 3)
                            {// Raw string literal: may span multiple lines, so track line breaks.
                                this.AddTokenAndSliceWithLineTracking(ref builder, TokenKind.StringLiteral, ref span, literalLength);
                            }
                            else
                            {// Regular string literal (quoteCount is 1 or 2; "" is an empty literal).
                                this.AddTokenAndSlice(ref builder, TokenKind.StringLiteral, ref span, literalLength);
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
                                this.AddTokenAndSlice(ref builder, TokenKind.Invalid, ref span, 1);
                                break;
                            }

                            if (TokenHelper.KeywordToTokenKind.TryGetValue(span.Slice(0, length), out var tokenKind2))
                            {// Keyword
                                this.AddTokenAndSlice(ref builder, tokenKind2, ref span, length);
                            }
                            else
                            {// Identifier
                                this.AddTokenAndSlice(ref builder, TokenKind.Identifier, ref span, length);
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
        else if (this.tokenAdded == 0)
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
                if (this.ReadSingleLineComment(ref builder, ref span))
                {
                    this.NextLine();
                }

                goto NextLine;
            }
            else if (span[1] == Constants.AsteriskChar)
            {// /* Multi line comment */
                _ = this.ReadMultiLineComment(ref builder, ref span);

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

        var separatorInserted = false;

        if (dif > 0)
        {
            this.AddToken(ref builder, new(TokenKind.Separator, this.CurrentRange));
            separatorInserted = true;

            // TODO: Consider reporting Hashed.Kimi.UnexpectedIndent when dif > 1.
            for (var i = 0; i < dif; i++)
            {
                this.AddToken(ref builder, new(TokenKind.StartBlock, this.CurrentRange));
                this.PushIndentSource(IndentSource.Block);
            }
        }
        else if (dif < 0)
        {
            // When indentation decreases inside a grouping construct, the current token
            // may be the matching closing delimiter placed at the outer indentation level.
            // In that case, consume the closing token and close the grouping context.
            // Otherwise, keep the grouping context open and report an indentation mismatch.

            for (var i = dif; i < 0; i++)
            {
                if (this.indentStack.TryPop(out var indentSource))
                {
                    if (indentSource == IndentSource.Block)
                    {
                        this.AddToken(ref builder, new(TokenKind.EndBlock, this.CurrentRange));
                        this.blockDepth--;
                    }
                    else if (indentSource == IndentSource.LineContinuation)
                    {
                        this.nonBlockDepth--;
                        continue;
                    }
                    else if (this.TryCloseIndentSourceByCurrentToken(ref builder, indentSource, ref span))
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
                    this.AddToken(ref builder, new(TokenKind.EndBlock, this.CurrentRange));
                    currentIndentLevel--;
                }
                else
                {
                    this.urlDiagnostic.Add(new(new(this.line, 0), new(this.line, numberOfSpaces)), Hashed.Kimi.IndentationLevelMismatch);
                    break;
                }
            }

            this.AddToken(ref builder, new(TokenKind.Separator, this.CurrentRange));
            separatorInserted = true;
        }

        if (this.nonBlockDepth > 0)
        {
            if (dif == 0 && this.indentStack.Peek() == IndentSource.Block)
            {
                // this.AddToken(ref builder, new(TokenKind.Separator));
            }

            goto Loop;
        }
        else
        {
            currentIndentLevel += this.blockDepth;
            this.blockDepth = 0;

            if (this.tokenAdded > 0 && !separatorInserted)
            {
                this.AddToken(ref builder, new(TokenKind.Separator, this.CurrentRange));
            }

            return this.tokenAdded;
        }

EndOfFile:
        this.ClearIndentStack(ref builder);
        while (currentIndentLevel-- > 0)
        {
            builder.Add(new(TokenKind.Separator, this.CurrentRange));
        }

        Debug.Assert(this.blockDepth == 0);
        Debug.Assert(this.nonBlockDepth == 0);

        builder.Add(new(TokenKind.Separator, this.CurrentRange));

        return this.tokenAdded;
    }

    private int ReadMultiLineComment(ref TokenSequenceBuilder builder, ref ReadOnlySpan<char> text)
    {
        var length = text.IndexOf("*/");
        if (length < 0)
        {
            this.urlDiagnostic.Add(new(new(this.line, this.character), new(this.line, this.character + 2)), Hashed.Kimi.MissingBlockCommentEnd);

            return this.AddTokenAndSliceWithLineTracking(ref builder, TokenKind.Invalid, ref text, text.Length);
        }

        length += 2;
        return this.AddTokenAndSliceWithLineTracking(ref builder, TokenKind.Invalid, ref text, length); // TokenKind.MultiLineComment -> TokenKind.Invalid
    }

    private bool ReadSingleLineComment(ref TokenSequenceBuilder builder, ref ReadOnlySpan<char> span)
    {// // Comment\n
        var idx = Arc.BaseHelper.IndexOfLfOrCrLf(span, out var newLineLength);
        if (idx < 0)
        {
            // this.AddTokenAndSlice(ref builder, TokenKind.SingleLineComment, ref span, span.Length);
            this.Slice(ref span, span.Length);
            return false;
        }
        else
        {
            // this.AddTokenAndSlice(ref builder, TokenKind.SingleLineComment, ref span, idx);
            this.Slice(ref span, idx);
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
    private void AddToken(ref TokenSequenceBuilder builder, Token token)
    {
        builder.Add(token);
        this.tokenAdded++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddTokenAndSlice(ref TokenSequenceBuilder builder, TokenKind tokenKind, ref ReadOnlySpan<char> span, int length)
    {
        builder.Add(new(tokenKind, this.text.Slice(this.position, length), this.line, this.character));
        this.tokenAdded++;

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
    private int AddTokenAndSliceWithLineTracking(ref TokenSequenceBuilder builder, TokenKind tokenKind, ref ReadOnlySpan<char> span, int length)
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

        if (tokenKind != TokenKind.Invalid)
        {
            builder.Add(new(tokenKind, this.text.Slice(this.position, length), new Diagnostics.SourceRange(start, new(this.line, this.character))));
            this.tokenAdded++;
        }

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

    private void PopIndentSource(ref TokenSequenceBuilder builder, TokenKind expected)
    {
        while (this.indentStack.TryPeek(out var indentSource))
        {
            if (indentSource == IndentSource.Block)
            {
                this.indentStack.Pop();
                this.AddToken(ref builder, new(TokenKind.EndBlock, this.CurrentRange));
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

    private void ClearIndentStack(ref TokenSequenceBuilder builder)
    {
        while (this.indentStack.TryPop(out var indentSource))
        {
            switch (indentSource)
            {
                case IndentSource.Block:
                    this.AddToken(ref builder, new(TokenKind.EndBlock, true));
                    this.blockDepth--;
                    break;

                case IndentSource.Parenthesis: // ()
                    this.AddToken(ref builder, new(TokenKind.CloseParenthesis, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.Bracket: // []
                    this.AddToken(ref builder, new(TokenKind.CloseBracket, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.AngleBracket: // <>
                    this.AddToken(ref builder, new(TokenKind.GreaterThan, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.Brace: // {}
                    this.AddToken(ref builder, new(TokenKind.CloseBrace, true));
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

    private bool TryCloseIndentSourceByCurrentToken(ref TokenSequenceBuilder builder, IndentSource indentSource, ref ReadOnlySpan<char> span)
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

        this.AddTokenAndSlice(ref builder, tokenKind, ref span, 1);
        this.nonBlockDepth--;
        return true;
    }

    private void ClearState()
    {
        this.tokenAdded = 0;
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
