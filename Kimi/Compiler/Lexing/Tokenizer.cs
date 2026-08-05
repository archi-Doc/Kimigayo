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

    #region FieldAndProperty

    private readonly DiagnosticCollection diagnostics;
    private readonly ReadOnlySpan<char> sourceText;
    private readonly Stack<IndentSource> indentStack;
    private readonly SequenceBuilder<Token> builder;

    private ReadOnlySpan<char> span;
    private int position;
    private int line;
    private int character;

    private int blockDepth;
    private int nonBlockDepth;
    private int tokenAdded;

    public ReadOnlySpan<char> SourceText => this.sourceText;

    public SourceRange CurrentRange => new(new(this.line, this.character), new(this.line, this.character));

    #endregion

    public Tokenizer(DiagnosticCollection diagnostics, ReadOnlySpan<char> sourceText)
    {
        this.diagnostics = diagnostics;
        this.sourceText = sourceText;
        this.indentStack = new();
        this.builder = new(1024 * 4);
    }

    public void Dispose()
        => this.builder.Dispose();

    public ReadOnlySequence<Token> ToReadOnlySequence()
        => this.builder.ToReadOnlySequence();

    public void ReadAll()
    {
        var currentIndentLevel = 0;
        while (this.Read(ref currentIndentLevel) > 0)
        {
            // builder.Add(new(TokenKind.Separator));
        }
    }

    /// <summary>
    /// Reads and tokenizes the next logical line.
    /// </summary>
    /// <param name="currentIndentLevel">
    /// The current block indentation level. The value is updated when
    /// indentation blocks are opened or closed.
    /// </param>
    /// <param name="builder">
    /// The builder that receives the generated tokens.
    /// </param>
    /// <returns>
    /// The number of tokens added to <paramref name="builder"/>.
    /// Returns zero when tokenization has finished.
    /// </returns>
    public int Read(ref int currentIndentLevel)
    {
        this.ClearState();

Loop:
        this.span = this.sourceText.Slice(this.position);
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
                this.Slice(1);
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
                        this.Slice(2);
                        this.NextLine();
                        goto NextLine;
                    }
                    else
                    {
                        this.Slice(1);
                        this.NextLine();
                        goto NextLine;
                    }

                case Constants.LfChar: // \n
                    this.Slice(1);
                    this.NextLine();
                    goto NextLine;

                case Constants.AmpersandChar: // && &= &
                    if (span.Length == 1)
                    {// &
                        this.AddTokenAndSlice(TokenKind.Ampersand, 1);
                    }
                    else if (span[1] == Constants.AmpersandChar)
                    {// &&
                        this.AddTokenAndSlice(TokenKind.AmpersandAmpersand, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// &=
                        this.AddTokenAndSlice(TokenKind.AmpersandEquals, 2);
                    }
                    else
                    {// &
                        this.AddTokenAndSlice(TokenKind.Ampersand, 1);
                    }

                    break;

                case Constants.AsteriskChar: // * *=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// *=
                        this.AddTokenAndSlice(TokenKind.AsteriskEquals, 2);
                    }
                    else
                    {// *
                        this.AddTokenAndSlice(TokenKind.Asterisk, 1);
                    }

                    break;

                case Constants.BarChar: // | || |=
                    if (span.Length == 1)
                    {// |
                        this.AddTokenAndSlice(TokenKind.Bar, 1);
                    }
                    else if (span[1] == Constants.BarChar)
                    {// ||
                        this.AddTokenAndSlice(TokenKind.BarBar, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// |=
                        this.AddTokenAndSlice(TokenKind.BarEquals, 2);
                    }
                    else
                    {// |
                        this.AddTokenAndSlice(TokenKind.Bar, 1);
                    }

                    break;

                case Constants.CaretChar: // ^ ^=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// ^=
                        this.AddTokenAndSlice(TokenKind.CaretEquals, 2);
                    }
                    else
                    {// ^
                        this.AddTokenAndSlice(TokenKind.Caret, 1);
                    }

                    break;

                case Constants.DotChar: // . .. ..=
                    if (span.Length == 1)
                    {// .
                        this.AddTokenAndSlice(TokenKind.Dot, 1);
                    }
                    else if (span[1] == Constants.DotChar)
                    {// ..
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// ..=
                            this.AddTokenAndSlice(TokenKind.DotDotEquals, 3);
                        }
                        else
                        {// ..
                            this.AddTokenAndSlice(TokenKind.DotDot, 2);
                        }
                    }
                    else
                    {// .
                        this.AddTokenAndSlice(TokenKind.Dot, 1);
                    }

                    break;

                case Constants.EqualsChar: // = == =>
                    if (span.Length == 1)
                    {// =
                        this.AddTokenAndSlice(TokenKind.Equals, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// ==
                        this.AddTokenAndSlice(TokenKind.EqualsEquals, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// =>
                        this.AddTokenAndSlice(TokenKind.EqualsGreaterThan, 2);
                    }
                    else
                    {// =
                        this.AddTokenAndSlice(TokenKind.Equals, 1);
                    }

                    break;

                case Constants.ExclamationChar: // ! !=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// !=
                        this.AddTokenAndSlice(TokenKind.ExclamationEquals, 2);
                    }
                    else
                    {// !
                        this.AddTokenAndSlice(TokenKind.Exclamation, 1);
                    }

                    break;

                case Constants.GreaterThanChar: // > >= >> >>=
                    if (span.Length == 1)
                    {// >
                        this.AddTokenAndSlice(TokenKind.GreaterThan, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// >=
                        this.AddTokenAndSlice(TokenKind.GreaterThanEquals, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// >>
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// >>=
                            this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThanEquals, 3);
                        }
                        else
                        {// >>
                            this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThan, 2);
                        }
                    }
                    else
                    {// >
                        this.AddTokenAndSlice(TokenKind.GreaterThan, 1);
                    }

                    break;

                case Constants.LessThanChar: // < <= << <<=
                    if (span.Length == 1)
                    {// <
                        this.AddTokenAndSlice(TokenKind.LessThan, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// <=
                        this.AddTokenAndSlice(TokenKind.LessThanEquals, 2);
                    }
                    else if (span[1] == Constants.LessThanChar)
                    {// <<
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// <<=
                            this.AddTokenAndSlice(TokenKind.LessThanLessThanEquals, 3);
                        }
                        else
                        {// <<
                            this.AddTokenAndSlice(TokenKind.LessThanLessThan, 2);
                        }
                    }
                    else
                    {// <
                        this.AddTokenAndSlice(TokenKind.LessThan, 1);
                    }

                    break;

                case Constants.MinusChar: // -- -= -
                    if (span.Length == 1)
                    {// -
                        this.AddTokenAndSlice(TokenKind.Minus, 1);
                    }
                    else if (span[1] == Constants.MinusChar)
                    {// --
                        this.AddTokenAndSlice(TokenKind.MinusMinus, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// -=
                        this.AddTokenAndSlice(TokenKind.MinusEquals, 2);
                    }
                    else
                    {// -
                        this.AddTokenAndSlice(TokenKind.Minus, 1);
                    }

                    break;

                case Constants.PercentChar: // % %=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// %=
                        this.AddTokenAndSlice(TokenKind.PercentEquals, 2);
                    }
                    else
                    {// %
                        this.AddTokenAndSlice(TokenKind.Percent, 1);
                    }

                    break;

                case Constants.PlusChar: // ++ += +
                    if (span.Length == 1)
                    {// +
                        this.AddTokenAndSlice(TokenKind.Plus, 1);
                    }
                    else if (span[1] == Constants.PlusChar)
                    {// ++
                        this.AddTokenAndSlice(TokenKind.PlusPlus, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// +=
                        this.AddTokenAndSlice(TokenKind.PlusEquals, 2);
                    }
                    else
                    {// +
                        this.AddTokenAndSlice(TokenKind.Plus, 1);
                    }

                    break;

                case Constants.SlashChar: // // /* /= /
                    if (span.Length == 1)
                    {// /
                        this.AddTokenAndSlice(TokenKind.Slash, 1);
                    }
                    else if (span[1] == Constants.SlashChar)
                    {// //
                        if (this.ReadSingleLineComment())
                        {
                            this.NextLine();
                        }

                        goto NextLine;
                    }
                    else if (span[1] == Constants.AsteriskChar)
                    {// /*
                        this.ReadMultiLineComment();
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// /=
                        this.AddTokenAndSlice(TokenKind.SlashEquals, 2);
                    }
                    else
                    {// /
                        this.AddTokenAndSlice(TokenKind.Slash, 1);
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

                        this.AddTokenAndSlice(TokenKind.Identifier, length);

                        break;
                    }

                case '"':
                    {
                        var result = StringLiteralHelper.ScanStringLiteral(span, out var doubleQuoteCount, out var stringLiteralLength);
                        if (result == ScanStringLiteralResult.String)
                        {
                            this.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
                        }
                        else if (result == ScanStringLiteralResult.MultilineString)
                        {
                            this.AddTokenAndSliceWithLineTracking(TokenKind.StringLiteral, stringLiteralLength);
                        }
                        else
                        {// Invalid
                            this.diagnostics.Add(this.NewRange(1), Hashed.Kimi.MissingStringLiteralEnd);
                            this.AddTokenAndSliceWithLineTracking(TokenKind.Invalid, stringLiteralLength);
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
                                this.PopIndentSource(tokenKind);
                            }

                            this.AddTokenAndSlice(tokenKind, 1);
                            continue;
                        }

                        if (NumberLiteralHelper.ScanNumberLiteral(span, out var numberLiteralLength))
                        {// Numeric literal
                         // If the current position starts a numeric literal, scan the entire numeric literal before checking separators.
                            this.AddTokenAndSlice(TokenKind.NumericLiteral, numberLiteralLength);
                        }
                        else if (numberLiteralLength > 0)
                        {// Starts with a digit but is not a valid numeric literal.
                         // Emit a single Invalid token with a diagnostic instead of silently falling back
                         // to the identifier path, which would produce bogus Identifier tokens.
                            this.diagnostics.Add(this.NewRange(numberLiteralLength), Hashed.Kimi.InvalidNumericLiteral);
                            this.AddTokenAndSlice(TokenKind.Invalid, numberLiteralLength);
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
                                this.diagnostics.Add(this.NewRange(1), Hashed.Kimi.InvalidCharacter, span[0]);
                                this.AddTokenAndSlice(TokenKind.Invalid, 1);
                                break;
                            }

                            if (TokenHelper.KeywordToTokenKind.TryGetValue(span.Slice(0, length), out var tokenKind2))
                            {// Keyword
                                this.AddTokenAndSlice(tokenKind2, length);
                            }
                            else
                            {// Identifier
                                this.AddTokenAndSlice(TokenKind.Identifier, length);
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
        this.Slice(numberOfSpaces);

LineContent:
        if (span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }

        if (span[0] == Constants.LfChar)
        {// Empty line (\n)
            this.Slice(1);
            this.NextLine();
            goto NextLine;
        }
        else if (span[0] == Constants.CrChar)
        {// Empty line (\r\n or \r)
            this.Slice(span.Length > 1 && span[1] == Constants.LfChar ? 2 : 1);
            this.NextLine();
            goto NextLine;
        }
        else if (span.Length >= 2 && span[0] == Constants.SlashChar)
        {// /
            if (span[1] == Constants.SlashChar)
            {// // Single line comment
                if (this.ReadSingleLineComment())
                {
                    this.NextLine();
                }

                goto NextLine;
            }
            else if (span[1] == Constants.AsteriskChar)
            {// /* Multi line comment */
                _ = this.ReadMultiLineComment();

                // Skip spaces after the comment WITHOUT counting them as indentation;
                // the indentation of this line was already measured at the line start
                // (this prevents a bogus InvalidIndentation diagnostic for "/* c */ foo").
                // If the comment spanned multiple physical lines, code following the
                // closing "*/" inherits the indentation of the line that opened it.
                this.Slice(BaseHelper.CountLeadingSpaces(span));
                goto LineContent;
            }
        }

        var unnecessarySpaces = numberOfSpaces % Constants.IndentationSpaces;
        if (unnecessarySpaces > 0)
        {// Invalid indentation
            this.diagnostics.Add(new(new(this.line, 0), new(this.line, numberOfSpaces)), Hashed.Kimi.InvalidIndentation, Constants.IndentationSpaces);
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
        var indentDelta = indentLevel - currentIndentLevel - this.blockDepth - this.nonBlockDepth;

        if (indentDelta == 1)
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

        if (indentDelta > 0)
        {
            this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            separatorInserted = true;

            // TODO: Consider reporting Hashed.Kimi.UnexpectedIndent when dif > 1.
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
            // In that case, consume the closing token and close the grouping context.
            // Otherwise, keep the grouping context open and report an indentation mismatch.

            for (var i = indentDelta; i < 0; i++)
            {
                if (this.indentStack.TryPop(out var indentSource))
                {
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
                        // this.indentStack.Push(indentSource);
                        this.nonBlockDepth--;

                        this.diagnostics.Add(new(new(this.line, 0), new(this.line, numberOfSpaces)), Hashed.Kimi.IndentationLevelMismatch);
                        break;
                    }
                }
                else if (currentIndentLevel > 0)
                {
                    this.AddToken(new(TokenKind.EndBlock, this.CurrentRange));
                    currentIndentLevel--;
                }
                else
                {
                    this.diagnostics.Add(new(new(this.line, 0), new(this.line, numberOfSpaces)), Hashed.Kimi.IndentationLevelMismatch);
                    break;
                }
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
            currentIndentLevel += this.blockDepth;
            this.blockDepth = 0;

            if (this.tokenAdded > 0 && !separatorInserted)
            {
                this.AddToken(new(TokenKind.Separator, this.CurrentRange));
            }

            return this.tokenAdded;
        }

EndOfFile:
        this.ClearIndentStack();
        while (currentIndentLevel > 0)
        {
            builder.Add(new(TokenKind.EndBlock, this.CurrentRange));
            currentIndentLevel--;
        }

        Debug.Assert(this.blockDepth == 0);
        Debug.Assert(this.nonBlockDepth == 0);

        return this.tokenAdded;
    }

    private int ReadMultiLineComment()
    {
        var length = this.span.IndexOf("*/");
        if (length < 0)
        {
            this.diagnostics.Add(new(new(this.line, this.character), new(this.line, this.character + 2)), Hashed.Kimi.MissingBlockCommentEnd);

            return this.AddTokenAndSliceWithLineTracking(TokenKind.Invalid, this.span.Length);
        }

        length += 2;
        return this.AddTokenAndSliceWithLineTracking(TokenKind.Invalid, length); // TokenKind.MultiLineComment -> TokenKind.Invalid
    }

    private bool ReadSingleLineComment()
    {// // Comment\n
        var idx = BaseHelper.IndexOfLfOrCrLf(span, out var newLineLength);
        if (idx < 0)
        {
            // this.AddTokenAndSlice(TokenKind.SingleLineComment, span.Length);
            this.Slice(span.Length);
            return false;
        }
        else
        {
            // this.AddTokenAndSlice(TokenKind.SingleLineComment, idx);
            this.Slice(idx);
            this.Slice(newLineLength);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Diagnostics.SourceRange NewRange(int length)
    {
        return new(new(this.line, this.character), new(this.line, this.character + length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Slice(int length)
    {
        span = span.Slice(length);
        this.position += length;
        this.character += length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToken(Token token)
    {
        builder.Add(token);
        this.tokenAdded++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddTokenAndSlice(TokenKind tokenKind, int length)
    {
        this.builder.Add(new(tokenKind, this.position, length, this.line, this.character));
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
    private int AddTokenAndSliceWithLineTracking(TokenKind tokenKind, int length)
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
            builder.Add(new(tokenKind, this.position, length, new SourceRange(start, new(this.line, this.character))));
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

    private void PopIndentSource(TokenKind expected)
    {
        while (this.indentStack.TryPeek(out var indentSource))
        {
            if (indentSource == IndentSource.Block)
            {
                this.indentStack.Pop();
                this.AddToken(new(TokenKind.EndBlock, this.CurrentRange));
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

        this.diagnostics.Add(this.NewRange(1), diagnostic);
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

    private bool TryCloseIndentSourceByCurrentToken(IndentSource indentSource)
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

        this.AddTokenAndSlice(tokenKind, 1);
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
