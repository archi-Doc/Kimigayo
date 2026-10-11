// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a string literal expression.
/// </summary>
/// <remarks>
/// A written literal spans its delimiters; a segment of an interpolated string spans its text between the delimiters and
/// interpolations.
/// </remarks>
public sealed class StringLiteralKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.StringLiteral;

    // The unquoted content of an escaped literal or segment, or the complete raw literal (StringLiteralHelper.GetStringLiteralValue).
    private readonly string rawLiteral;

    // The source offset of rawLiteral: escape problems are located at the content, not at the delimiters.
    private readonly int rawStart;

    /// <summary>Gets the decoded string value.</summary>
    public string Literal
    {
        get
        {
            // Decode lazily and cache the result because diagnostics are reported during decoding.
            return field ??= StringLiteralHelper.GetStringLiteralValue(this.rawLiteral, this, new(this.rawStart, this.rawLiteral.Length));
        }
    }

    /// <summary>Initializes a new instance of the <see cref="StringLiteralKoto"/> class for a written string literal.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The string literal token, which spans the literal as written, delimiters included.</param>
    public StringLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Span)
    {
        var text = reader.GetSpan(token);
        if (text.Length > 2 && text[1] == '"')
        {// A raw literal keeps its delimiters.
            this.rawLiteral = text.ToString();
            this.rawStart = token.Start;
        }
        else
        {// An escaped literal keeps its content.
            this.rawLiteral = text[1..^1].ToString();
            this.rawStart = token.Start + 1;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="StringLiteralKoto"/> class for a segment of an interpolated string literal.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="segment">The segment's text between the delimiters and interpolations.</param>
    internal StringLiteralKoto(ref TokenReader reader, SourceSpan segment)
        : base(ref reader, segment)
    {
        this.rawLiteral = reader.GetSpan(new(TokenKind.StringLiteral, segment)).ToString();
        this.rawStart = segment.Start;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.WriteAttributeChainTo(ref builder, KotoWriteOptions.AppendSpace);

        if (this.rawLiteral.Length > 0 && this.rawLiteral[0] == '"')
        {
            builder.Append('"');
            builder.AppendVerbatim(this.rawLiteral.AsSpan(1));
        }
        else
        {
            builder.Append('"');
            builder.AppendVerbatim(this.rawLiteral);
            builder.AppendVerbatim("\"");
        }
    }

    internal void WriteContentTo(ref IndentedStringBuilder builder) => builder.AppendVerbatim(this.rawLiteral);
}
