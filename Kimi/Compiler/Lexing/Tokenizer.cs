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

    private static readonly CharacterHandler?[] CharacterHandlerTable;

    static Tokenizer()
    {
        // Dispatch common leading characters without a large branch chain in the read loop.
        CharacterHandlerTable = new CharacterHandler?[Constants.ExclusiveUpperBound];

        CharacterHandlerTable[(int)Constants.CrChar] = (ref tokenizer) =>
        {// \r
            if (tokenizer.span.Length > 1 && tokenizer.span[1] == Constants.LfChar)
            {// \r\n
                tokenizer.Slice(2);
            }
            else
            {
                tokenizer.Slice(1);
            }

            return true;
        };

        CharacterHandlerTable[(int)Constants.LfChar] = (ref tokenizer) =>
        {// \n
            tokenizer.Slice(1);
            return true;
        };

        CharacterHandlerTable[(int)Constants.AmpersandChar] = (ref tokenizer) =>
        {// && &= &
            if (tokenizer.span.Length == 1)
            {// &
                tokenizer.AddTokenAndSlice(TokenKind.Ampersand, 1);
            }
            else if (tokenizer.span[1] == Constants.AmpersandChar)
            {// &&
                tokenizer.AddTokenAndSlice(TokenKind.AmpersandAmpersand, 2);
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// &=
                tokenizer.AddTokenAndSlice(TokenKind.AmpersandEquals, 2);
            }
            else
            {// &
                tokenizer.AddTokenAndSlice(TokenKind.Ampersand, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.AsteriskChar] = (ref tokenizer) =>
        {// * *=
            if (tokenizer.span.Length > 1 && tokenizer.span[1] == Constants.EqualsChar)
            {// *=
                tokenizer.AddTokenAndSlice(TokenKind.AsteriskEquals, 2);
            }
            else
            {// *
                tokenizer.AddTokenAndSlice(TokenKind.Asterisk, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.BarChar] = (ref tokenizer) =>
        {// | || |=
            if (tokenizer.span.Length == 1)
            {// |
                tokenizer.AddTokenAndSlice(TokenKind.Bar, 1);
            }
            else if (tokenizer.span[1] == Constants.BarChar)
            {// ||
                tokenizer.AddTokenAndSlice(TokenKind.BarBar, 2);
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// |=
                tokenizer.AddTokenAndSlice(TokenKind.BarEquals, 2);
            }
            else
            {// |
                tokenizer.AddTokenAndSlice(TokenKind.Bar, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.CaretChar] = (ref tokenizer) =>
        {// ^ ^=
            if (tokenizer.span.Length > 1 && tokenizer.span[1] == Constants.EqualsChar)
            {// ^=
                tokenizer.AddTokenAndSlice(TokenKind.CaretEquals, 2);
            }
            else
            {// ^
                tokenizer.AddTokenAndSlice(TokenKind.Caret, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.DotChar] = (ref tokenizer) =>
        {// . .. ..=
            if (tokenizer.span.Length == 1)
            {// .
                tokenizer.AddTokenAndSlice(TokenKind.Dot, 1);
            }
            else if (tokenizer.span[1] == Constants.DotChar)
            {// ..
                if (tokenizer.span.Length >= 3 && tokenizer.span[2] == Constants.EqualsChar)
                {// ..=
                    tokenizer.AddTokenAndSlice(TokenKind.DotDotEquals, 3);
                }
                else
                {// ..
                    tokenizer.AddTokenAndSlice(TokenKind.DotDot, 2);
                }
            }
            else
            {// .
                tokenizer.AddTokenAndSlice(TokenKind.Dot, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.EqualsChar] = (ref tokenizer) =>
        {// = == =>
            if (tokenizer.span.Length == 1)
            {// =
                tokenizer.AddTokenAndSlice(TokenKind.Equals, 1);
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// ==
                tokenizer.AddTokenAndSlice(TokenKind.EqualsEquals, 2);
            }
            else if (tokenizer.span[1] == Constants.GreaterThanChar)
            {// =>
                tokenizer.AddTokenAndSlice(TokenKind.EqualsGreaterThan, 2);
            }
            else
            {// =
                tokenizer.AddTokenAndSlice(TokenKind.Equals, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.ExclamationChar] = (ref tokenizer) =>
        {// ! !=
            if (tokenizer.span.Length > 1 && tokenizer.span[1] == Constants.EqualsChar)
            {// !=
                tokenizer.AddTokenAndSlice(TokenKind.ExclamationEquals, 2);
            }
            else
            {// !
                tokenizer.AddTokenAndSlice(TokenKind.Exclamation, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.GreaterThanChar] = (ref tokenizer) =>
        {// > >= >> >>=
            if (tokenizer.span.Length == 1)
            {// >
                tokenizer.AddTokenAndSlice(TokenKind.GreaterThan, 1);
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// >=
                tokenizer.AddTokenAndSlice(TokenKind.GreaterThanEquals, 2);
            }
            else if (tokenizer.span[1] == Constants.GreaterThanChar)
            {// >>
                if (tokenizer.span.Length >= 3 && tokenizer.span[2] == Constants.EqualsChar)
                {// >>=
                    tokenizer.AddTokenAndSlice(TokenKind.GreaterThanGreaterThanEquals, 3);
                }
                else
                {// >>
                    tokenizer.AddTokenAndSlice(TokenKind.GreaterThanGreaterThan, 2);
                }
            }
            else
            {// >
                tokenizer.AddTokenAndSlice(TokenKind.GreaterThan, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.LessThanChar] = (ref tokenizer) =>
        {// < <= << <<=
            if (tokenizer.span.Length == 1)
            {// <
                tokenizer.AddTokenAndSlice(TokenKind.LessThan, 1);
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// <=
                tokenizer.AddTokenAndSlice(TokenKind.LessThanEquals, 2);
            }
            else if (tokenizer.span[1] == Constants.LessThanChar)
            {// <<
                if (tokenizer.span.Length >= 3 && tokenizer.span[2] == Constants.EqualsChar)
                {// <<=
                    tokenizer.AddTokenAndSlice(TokenKind.LessThanLessThanEquals, 3);
                }
                else
                {// <<
                    tokenizer.AddTokenAndSlice(TokenKind.LessThanLessThan, 2);
                }
            }
            else
            {// <
                tokenizer.AddTokenAndSlice(TokenKind.LessThan, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.MinusChar] = (ref tokenizer) =>
        {// -- -= -> -
            if (tokenizer.span.Length == 1)
            {// -
                tokenizer.AddTokenAndSlice(TokenKind.Minus, 1);
            }
            else if (tokenizer.span[1] == Constants.MinusChar)
            {// --
                tokenizer.AddTokenAndSlice(TokenKind.MinusMinus, 2);
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// -=
                tokenizer.AddTokenAndSlice(TokenKind.MinusEquals, 2);
            }
            else if (tokenizer.span[1] == Constants.GreaterThanChar)
            {// ->
                tokenizer.AddTokenAndSlice(TokenKind.MinusGreaterThan, 2);
            }
            else
            {// -
                tokenizer.AddTokenAndSlice(TokenKind.Minus, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.PercentChar] = (ref tokenizer) =>
        {// % %=
            if (tokenizer.span.Length > 1 && tokenizer.span[1] == Constants.EqualsChar)
            {// %=
                tokenizer.AddTokenAndSlice(TokenKind.PercentEquals, 2);
            }
            else
            {// %
                tokenizer.AddTokenAndSlice(TokenKind.Percent, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.PlusChar] = (ref tokenizer) =>
        {// ++ += +
            if (tokenizer.span.Length == 1)
            {// +
                tokenizer.AddTokenAndSlice(TokenKind.Plus, 1);
            }
            else if (tokenizer.span[1] == Constants.PlusChar)
            {// ++
                tokenizer.AddTokenAndSlice(TokenKind.PlusPlus, 2);
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// +=
                tokenizer.AddTokenAndSlice(TokenKind.PlusEquals, 2);
            }
            else
            {// +
                tokenizer.AddTokenAndSlice(TokenKind.Plus, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.SlashChar] = (ref tokenizer) =>
        {// // /* /= /
            if (tokenizer.span.Length == 1)
            {// /
                tokenizer.AddTokenAndSlice(TokenKind.Slash, 1);
            }
            else if (tokenizer.span[1] == Constants.SlashChar)
            {// //
                tokenizer.ReadSingleLineComment();
                return true;
            }
            else if (tokenizer.span[1] == Constants.AsteriskChar)
            {// /*
                tokenizer.ReadMultiLineComment();
            }
            else if (tokenizer.span[1] == Constants.EqualsChar)
            {// /=
                tokenizer.AddTokenAndSlice(TokenKind.SlashEquals, 2);
            }
            else
            {// /
                tokenizer.AddTokenAndSlice(TokenKind.Slash, 1);
            }

            return false;
        };

        CharacterHandlerTable[(int)Constants.AtChar] = (ref tokenizer) =>
        {// @ = as
            tokenizer.AddTokenAndSlice(TokenKind.At, 1);

            /*var length = TokenHelper.IndexOfSeparator(tokenizer.span.Slice(1));
            if (length < 0)
            {
                length = tokenizer.span.Length;
            }
            else
            {
                length++;
            }

            tokenizer.AddTokenAndSlice(TokenKind.Identifier, length);*/

            return false;
        };

        CharacterHandlerTable[(int)'"'] = (ref tokenizer) =>
        {
            var result = StringLiteralHelper.ScanStringLiteral(tokenizer.span, out var doubleQuoteCount, out var stringLiteralLength);
            if (result == ScanStringLiteralResult.String)
            {// "Text" -> Text
                if (doubleQuoteCount == 1)
                {
                    tokenizer.Slice(1);
                    stringLiteralLength -= 2;
                    tokenizer.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
                    tokenizer.Slice(1);
                }
                else
                {
                    tokenizer.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
                }
            }
            else if (result == ScanStringLiteralResult.MultilineString)
            {// """Text"""
                tokenizer.AddTokenAndSlice(TokenKind.StringLiteral, stringLiteralLength);
            }
            else
            {// Invalid
                tokenizer.diagnostics.Add(tokenizer.NewRange(1), DiagnosticCode.MissingStringLiteralEnd_Kd);
                tokenizer.Slice(stringLiteralLength);
            }

            return false;
        };
    }

    #region FieldAndProperty

    private readonly DiagnosticCollection diagnostics;
    private readonly SourceDocument sourceDocument;
    private readonly ReadOnlySpan<char> sourceText;
    private readonly Stack<IndentSource> indentStack;
    private SequenceBuilder<Token> builder;
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
        this.indentStack = new();
        this.builder = new(1024 * 4);

        diagnostics.SetSourceDocument(sourceDocument);
    }

    /// <summary>
    /// Releases the pooled token storage.
    /// </summary>
    public void Dispose()
        => this.builder.Dispose();

    /// <summary>
    /// Finalizes and returns the generated token sequence.
    /// </summary>
    /// <returns>The generated tokens.</returns>
    public ReadOnlySequence<Token> ToReadOnlySequence()
        => this.builder.ToReadOnlySequence();

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

        /*while (this.Read() > 0 &&
            this.position < this.sourceText.Length)
        {
        }*/
    }

    private int Read()
    {
        this.ClearState();

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
                {
                    goto NextLine;
                }
            }
            else
            {// Single char token, Number literal, String literal, Keyword, Identifier
                if (TokenHelper.TryGetSingleCharTokenKind(this.span[0], out var tokenKind, out var depth))
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

                if (NumberLiteralHelper.ScanNumberLiteral(this.span, out var numberLiteralLength))
                {// Numeric literal
                 // If the current position starts a numeric literal, scan the entire numeric literal before checking separators.
                    this.AddTokenAndSlice(TokenKind.NumericLiteral, numberLiteralLength);
                }
                else if (numberLiteralLength > 0)
                {// Starts with a digit but is not a valid numeric literal.
                 // Emit a single Invalid token with a diagnostic instead of silently falling back
                 // to the identifier path, which would produce bogus Identifier tokens.
                    this.diagnostics.Add(this.NewRange(numberLiteralLength), DiagnosticCode.InvalidNumericLiteral_Kd);
                    this.AddTokenAndSlice(TokenKind.Invalid, numberLiteralLength);
                }
                else
                {// Keyword or Identifier
                    var length = TokenHelper.IndexOfSeparator(this.span);
                    if (length < 0)
                    {
                        length = this.span.Length;
                    }
                    else if (length == 0)
                    {
                        this.diagnostics.Add(this.NewRange(1), DiagnosticCode.InvalidCharacter_Kd, this.span[0]);
                        this.AddTokenAndSlice(TokenKind.Invalid, 1);
                        continue;
                    }

                    if (TokenHelper.KeywordToTokenKind.TryGetValue(this.span.Slice(0, length), out var keywordKind))
                    {// Keyword
                        this.AddTokenAndSlice(keywordKind, length);
                    }
                    else
                    {// Identifier
                        this.AddTokenAndSlice(TokenKind.Identifier, length);
                    }
                }
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
            if (this.span.Length > 0 && this.span[0] == Constants.DotChar)
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
                        while (!this.span.IsEmpty && this.span[0] == Constants.SpaceChar)
                        {
                            this.Slice(1);
                        }

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
                        // this.indentStack.Push(indentSource);
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
            this.builder.Add(new(TokenKind.EndBlock, this.CurrentRange));
            this.currentIndentLevel--;
        }

        Debug.Assert(this.blockDepth == 0);
        Debug.Assert(this.nonBlockDepth == 0);

        return this.tokenAdded;
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

        length += 2;
        this.Slice(length); // TokenKind.MultiLineComment -> TokenKind.Invalid
    }

    private void ReadSingleLineComment()
    {// // Comment\n
        var idx = BaseHelper.IndexOfLfOrCrLf(this.span, out var newLineLength);
        if (idx < 0)
        {
            // this.AddTokenAndSlice(TokenKind.SingleLineComment, span.Length);
            this.Slice(this.span.Length);
        }
        else
        {
            // this.AddTokenAndSlice(TokenKind.SingleLineComment, idx);
            this.Slice(idx);
            this.Slice(newLineLength);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SourceSpan NewRange(int length)
    {
        return new(this.position, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Slice(int length)
    {
        this.span = this.span.Slice(length);
        this.position += length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToken(Token token)
    {
        this.builder.Add(token);
        this.tokenAdded++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddTokenAndSlice(TokenKind tokenKind, int length)
    {
        this.builder.Add(new(tokenKind, this.position, length));
        this.tokenAdded++;

        this.span = this.span.Slice(length);
        this.position += length;
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
            TokenKind.CloseParenthesis => DiagnosticCode.UnmatchedParenthesis_Kd,
            TokenKind.CloseBracket => DiagnosticCode.UnmatchedBracket_Kd,
            TokenKind.CloseBrace => DiagnosticCode.UnmatchedBrace_Kd,
            TokenKind.GreaterThan => DiagnosticCode.UnmatchedAngleBracket_Kd,
            _ => DiagnosticCode.UnmatchedBracket_Kd,
        };

        this.diagnostics.Add(this.NewRange(1), diagnostic);
    }

    private void ClearIndentStack()
    {
        var missingRange = this.CurrentRange;
        while (this.indentStack.TryPop(out var indentSource))
        {
            switch (indentSource)
            {
                case IndentSource.Block:
                    this.AddToken(new(TokenKind.EndBlock, missingRange, true));
                    this.blockDepth--;
                    break;

                case IndentSource.Parenthesis: // ()
                    this.diagnostics.Add(missingRange, DiagnosticCode.MissingExpectedToken_Kd, TokenKind.CloseParenthesis.ToText());
                    this.AddToken(new(TokenKind.CloseParenthesis, missingRange, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.Bracket: // []
                    this.diagnostics.Add(missingRange, DiagnosticCode.MissingExpectedToken_Kd, TokenKind.CloseBracket.ToText());
                    this.AddToken(new(TokenKind.CloseBracket, missingRange, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.AngleBracket: // <>
                    this.diagnostics.Add(missingRange, DiagnosticCode.MissingExpectedToken_Kd, TokenKind.GreaterThan.ToText());
                    this.AddToken(new(TokenKind.GreaterThan, missingRange, true));
                    this.nonBlockDepth--;
                    break;

                case IndentSource.Brace: // {}
                    this.diagnostics.Add(missingRange, DiagnosticCode.MissingExpectedToken_Kd, TokenKind.CloseBrace.ToText());
                    this.AddToken(new(TokenKind.CloseBrace, missingRange, true));
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
        if (this.span.IsEmpty)
        {
            return false;
        }

        var tokenKind = indentSource switch
        {
            IndentSource.Parenthesis when this.span[0] == Constants.CloseParenthesisChar => TokenKind.CloseParenthesis,
            IndentSource.Bracket when this.span[0] == Constants.CloseBracketChar => TokenKind.CloseBracket,
            IndentSource.AngleBracket when this.span[0] == Constants.GreaterThanChar => TokenKind.GreaterThan,
            IndentSource.Brace when this.span[0] == Constants.CloseBraceChar => TokenKind.CloseBrace,
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
}
