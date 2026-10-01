// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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

        this.SkipUntil(bodyMayFollow ? TokenKind.StartBlock : TokenKind.Separator, TokenKind.Separator, TokenKind.EndBlock, null);
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
            // Distinct forms missing at one insertion point, such as the closers of nested groupings, are distinct problems;
            // a repeated report of one form merges by its key.
            return this.Diagnostic.ReportSyntax(new SourceSpan(this.PreviousSyntaxEnd, 0), DiagnosticCode.MissingSyntax_Kd, form, null, this.CodeContext.SourceDocument);
        }

        // A token is blamed once: the lexical Error that rejected it, or an earlier expectation at it, explains this recovery too.
        return this.Diagnostic.RecallError(found.Span) ? this.Diagnostic.LastError!.Value
            : this.Diagnostic.ReportSyntax(found.Span, DiagnosticCode.ExpectedSyntax_Kd, form, this.GetSpan(found).ToString(), this.CodeContext.SourceDocument);
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
}
