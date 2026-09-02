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

    private const int InitialIndentStackCapacity = 32;
    private const int MinimumTokenCapacity = 256;

    #region FieldAndProperty

    private readonly DiagnosticCollection diagnostics;
    private readonly SourceDocument sourceDocument;
    private readonly ReadOnlySpan<char> sourceText;
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

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="Tokenizer"/> struct.
    /// </summary>
    /// <param name="diagnostics">The destination for lexical diagnostics.</param>
    /// <param name="sourceDocument">The source document to tokenize.</param>
    public Tokenizer(DiagnosticCollection diagnostics, SourceDocument sourceDocument)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);

        this.diagnostics = diagnostics;
        this.sourceDocument = sourceDocument;
        this.sourceText = sourceDocument.AsSpan();
        this.indentStack = ArrayPool<IndentSource>.Shared.Rent(InitialIndentStackCapacity);

        // Typical source yields roughly one token per four characters; the array grows on demand.
        this.tokens = ArrayPool<Token>.Shared.Rent(Math.Max(MinimumTokenCapacity, (this.sourceText.Length >> 2) + 64));

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
            var next = this.span.Length > 1 ? this.span[1] : '\0';
            switch (this.span[0])
            {
                case Constants.CrChar: // \r \r\n
                    this.Slice(next == Constants.LfChar ? 2 : 1);
                    goto NextLine;

                case Constants.LfChar: // \n
                    this.Slice(1);
                    goto NextLine;

                case Constants.AmpersandChar: // & && &=
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

                case Constants.AsteriskChar: // * *=
                    if (next == Constants.EqualsChar)
                    {
                        this.AddTokenAndSlice(TokenKind.AsteriskEquals, 2);
                    }
                    else
                    {
                        this.AddTokenAndSlice(TokenKind.Asterisk, 1);
                    }

                    continue;

                case Constants.BarChar: // | || |=
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

                case Constants.CaretChar: // ^ ^=
                    if (next == Constants.EqualsChar)
                    {
                        this.AddTokenAndSlice(TokenKind.CaretEquals, 2);
                    }
                    else
                    {
                        this.AddTokenAndSlice(TokenKind.Caret, 1);
                    }

                    continue;

                case Constants.DotChar: // . .. ..=
                    if (next == Constants.DotChar)
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

                case Constants.EqualsChar: // = == =>
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

                case Constants.ExclamationChar: // ! !=
                    if (next == Constants.EqualsChar)
                    {
                        this.AddTokenAndSlice(TokenKind.ExclamationEquals, 2);
                    }
                    else
                    {
                        this.AddTokenAndSlice(TokenKind.Exclamation, 1);
                    }

                    continue;

                case Constants.GreaterThanChar: // > >= >> >>=
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

                case Constants.LessThanChar: // < <= << <<=
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
                        this.AddTokenAndSlice(TokenKind.LessThan, 1);
                    }

                    continue;

                case Constants.MinusChar: // - -- -= ->
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

                case Constants.PercentChar: // % %=
                    if (next == Constants.EqualsChar)
                    {
                        this.AddTokenAndSlice(TokenKind.PercentEquals, 2);
                    }
                    else
                    {
                        this.AddTokenAndSlice(TokenKind.Percent, 1);
                    }

                    continue;

                case Constants.PlusChar: // + ++ +=
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

                case Constants.SlashChar: // / // /* /=
                    if (next == Constants.SlashChar)
                    {
                        this.ReadSingleLineComment();
                        goto NextLine;
                    }
                    else if (next == Constants.AsteriskChar)
                    {
                        this.ReadMultiLineComment();
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

                case '"':
                    this.ReadStringLiteral();
                    continue;

                case Constants.AtChar:
                    this.AddTokenAndSlice(TokenKind.At, 1);
                    continue;

                case Constants.SharpChar:
                    this.AddTokenAndSlice(TokenKind.Sharp, 1);
                    continue;

                case Constants.DollarChar:
                    this.AddTokenAndSlice(TokenKind.Dollar, 1);
                    continue;

                case Constants.CommaChar:
                    this.AddTokenAndSlice(TokenKind.Comma, 1);
                    continue;

                case Constants.ColonChar:
                    this.AddTokenAndSlice(TokenKind.Colon, 1);
                    continue;

                case Constants.SemicolonChar:
                    this.AddTokenAndSlice(TokenKind.Semicolon, 1);
                    continue;

                case Constants.QuestionChar:
                    this.AddTokenAndSlice(TokenKind.Question, 1);
                    continue;

                case Constants.OpenParenthesisChar:
                    this.PushIndentSource(IndentSource.Parenthesis);
                    this.AddTokenAndSlice(TokenKind.OpenParenthesis, 1);
                    continue;

                case Constants.CloseParenthesisChar:
                    this.PopIndentSource(TokenKind.CloseParenthesis);
                    this.AddTokenAndSlice(TokenKind.CloseParenthesis, 1);
                    continue;

                case Constants.OpenBracketChar:
                    this.PushIndentSource(IndentSource.Bracket);
                    this.AddTokenAndSlice(TokenKind.OpenBracket, 1);
                    continue;

                case Constants.CloseBracketChar:
                    this.PopIndentSource(TokenKind.CloseBracket);
                    this.AddTokenAndSlice(TokenKind.CloseBracket, 1);
                    continue;

                case Constants.OpenBraceChar:
                    this.PushIndentSource(IndentSource.Brace);
                    this.AddTokenAndSlice(TokenKind.OpenBrace, 1);
                    continue;

                case Constants.CloseBraceChar:
                    this.PopIndentSource(TokenKind.CloseBrace);
                    this.AddTokenAndSlice(TokenKind.CloseBrace, 1);
                    continue;

                default:
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

        var indentLevel = numberOfSpaces / Constants.IndentationSpaces;
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

    private void ReadStringLiteral()
    {
        var result = StringLiteralHelper.ScanStringLiteral(this.span, out var doubleQuoteCount, out var stringLiteralLength);
        if (result == ScanStringLiteralResult.String)
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
        else if (result == ScanStringLiteralResult.MultilineString)
        {// """Text"""
            this.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
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
