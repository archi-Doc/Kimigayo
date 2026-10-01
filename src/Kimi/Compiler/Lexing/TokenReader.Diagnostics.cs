// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Lexing;

/// <summary>
/// The one way the parser reports syntax problems (docs/dev/DIAGNOSTICS.md §4.4). A site names the <see cref="SyntaxForm"/> it
/// expected or found misplaced; these members decide the code and the location, blame a token at most once, rest on a lexical
/// Error the tokenizer already reported, and return the key that the recovery nodes name as their cause.
/// </summary>
public ref partial struct TokenReader
{
    /// <summary>Gets the form of a token the grammar requires at a position; the parser never expects another kind by itself.</summary>
    /// <param name="kind">The token kind.</param>
    /// <returns>The form.</returns>
    public static SyntaxForm FormOf(TokenKind kind)
        => kind switch
        {
            TokenKind.OpenParenthesis => SyntaxForm.OpenParenthesis,
            TokenKind.CloseParenthesis => SyntaxForm.CloseParenthesis,
            TokenKind.CloseBracket => SyntaxForm.CloseBracket,
            TokenKind.CloseBrace => SyntaxForm.CloseBrace,
            TokenKind.GreaterThan => SyntaxForm.CloseAngleBracket,
            TokenKind.Colon => SyntaxForm.Colon,
            TokenKind.Comma => SyntaxForm.Comma,
            TokenKind.Else => SyntaxForm.ElseKeyword,
            TokenKind.In => SyntaxForm.InKeyword,
            TokenKind.Base => SyntaxForm.BaseKeyword,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The grammar expects this token only through a form of its own."),
        };

    /// <summary>Consumes the current token when it has the expected kind; otherwise reports the token's own form as expected and leaves the reader unchanged.</summary>
    /// <param name="kind">The expected token kind, one that <see cref="FormOf"/> names.</param>
    /// <returns><see langword="true"/> when the token was consumed.</returns>
    public bool Expect(TokenKind kind)
        => this.Expect(kind, FormOf(kind));

    /// <summary>Consumes the current token when it has the expected kind; otherwise reports the form as expected and leaves the reader unchanged.</summary>
    /// <param name="kind">The expected token kind.</param>
    /// <param name="form">The form the token stands for.</param>
    /// <returns><see langword="true"/> when the token was consumed.</returns>
    public bool Expect(TokenKind kind, SyntaxForm form)
    {
        if (this.currentToken.Kind == kind && this.CanRead)
        {
            this.AdvanceOne();
            return true;
        }

        this.Expect(form);
        return false;
    }

    /// <summary>
    /// After a complete statement or header, reports the first token that remains on the line and skips the rest of the line.
    /// An indented body that follows is reported as misplaced and skipped as well, unless the caller parses it next.
    /// </summary>
    /// <param name="bodyMayFollow">Whether an indented body may follow the header; the reader then stops at its start.</param>
    public void ExpectLineEnd(bool bodyMayFollow = false)
    {
        if (!this.CanRead || this.currentToken.Kind is TokenKind.Separator or TokenKind.EndBlock)
        {
            return;
        }

        if (this.currentToken.Kind == TokenKind.StartBlock)
        {
            if (bodyMayFollow)
            {
                return;
            }

            this.Unexpected(SyntaxForm.IndentedBody);
        }
        else
        {
            this.Expect(SyntaxForm.LineEnd);
        }

        this.SkipUntil(bodyMayFollow ? TokenKind.StartBlock : TokenKind.Separator, TokenKind.Separator, TokenKind.EndBlock);
    }

    /// <summary>
    /// Reports that a form is expected at the current position. When the line ended before it, the form is missing at an insertion
    /// point after the last written token; otherwise the current token is where the form was expected. The reader is unchanged.
    /// </summary>
    /// <param name="form">The expected form.</param>
    /// <returns>The key of the Error that explains the recovery.</returns>
    internal DiagnosticKey Expect(SyntaxForm form)
        => this.Expect(form, this.currentToken);

    /// <summary>
    /// Reports that a form is expected where a token was found. A line boundary or the end of the source means the form is missing
    /// at an insertion point after the last written token. The reader is unchanged.
    /// </summary>
    /// <param name="form">The expected form.</param>
    /// <param name="found">The token found; the current token, or one the caller has already read.</param>
    /// <returns>The key of the Error that explains the recovery.</returns>
    internal DiagnosticKey Expect(SyntaxForm form, Token found)
    {
        if (found.Kind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock || found.Length == 0)
        {
            return this.Missing(form, this.PreviousSyntaxEnd);
        }

        if (IsCloser(form))
        {
            // The tokenizer reported the closer missing at the end of this line, with the grouping it closes: one problem, so the
            // expectation at the token recalls it, and what the parser expects next at that line end rests on it too.
            var lineEnd = this.LineEndInsertionPoint;
            if (this.Diagnostic.RecallError(new SourceSpan(lineEnd, 0), DiagnosticRequirement.SyntaxOf(form)))
            {
                var recalled = this.Diagnostic.LastError!.Value;
                this.lastMissing = (lineEnd, recalled);
                return recalled;
            }
        }

        // A token is blamed once: the lexical Error that rejected it, or an earlier expectation at it, explains this recovery too.
        return this.Diagnostic.RecallError(found.Span) ? this.Diagnostic.LastError!.Value
            : this.Diagnostic.ReportSyntax(found.Span, DiagnosticCode.ExpectedSyntax_Kd, form, this.GetSpan(found).ToString(), this.CodeContext.SourceDocument);
    }

    /// <summary>Reports that a form is missing at an insertion point the caller determined, such as right after a keyword. The reader is unchanged.</summary>
    /// <param name="form">The missing form.</param>
    /// <param name="at">The insertion point.</param>
    /// <returns>The key of the Error that explains the recovery.</returns>
    internal DiagnosticKey Missing(SyntaxForm form, int at)
    {
        // The first form missing at an insertion point is the problem; a later form the parser expects there is its consequence,
        // except a closer: the closers of nested groupings are distinct problems, each merging by its own key.
        if (this.lastMissing is { } last && last.At == at && !IsCloser(form))
        {
            return last.Key;
        }

        var key = this.Diagnostic.ReportSyntax(new SourceSpan(at, 0), DiagnosticCode.MissingSyntax_Kd, form, null, this.CodeContext.SourceDocument);
        this.lastMissing = (at, key);
        return key;
    }

    /// <summary>Reports that a form of syntax is not permitted where it stands. The reader is unchanged.</summary>
    /// <param name="form">The misplaced form.</param>
    /// <param name="span">The misplaced syntax; the current token by default.</param>
    /// <returns>The key of the Error that explains the recovery.</returns>
    internal DiagnosticKey Unexpected(SyntaxForm form, SourceSpan? span = null)
    {
        var at = span ?? this.currentToken.Span;
        return this.Diagnostic.RecallError(at) ? this.Diagnostic.LastError!.Value
            : this.Diagnostic.ReportSyntax(at, DiagnosticCode.MisplacedSyntax_Kd, form, null, this.CodeContext.SourceDocument);
    }

    private static bool IsCloser(SyntaxForm form)
        => form is SyntaxForm.CloseParenthesis or SyntaxForm.CloseBracket or SyntaxForm.CloseBrace or SyntaxForm.CloseAngleBracket;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void NoteMissingCloser(int index)
    {
        var token = this.tokens[index];
        if (token.Kind is not (TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace))
        {
            return;
        }

        // The tokenizer reported the closer right after the last written token before it (Tokenizer.InsertionPoint).
        var at = token.Span.Start;
        for (var i = index - 1; i >= 0; i--)
        {
            if (!this.tokens[i].IsMissing && this.tokens[i].Kind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
            {
                at = this.tokens[i].Span.End;
                break;
            }
        }

        if ((this.lastMissing is { } last && last.At == at) || !this.Diagnostic.RecallError(new SourceSpan(at, 0), DiagnosticRequirement.SyntaxOf(FormOf(token.Kind))))
        {
            return;
        }

        this.lastMissing = (at, this.Diagnostic.LastError!.Value);
    }
}
