// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Marks a compile-time <c>#switch</c> that selected no arm, so later analyses explain no cascade (SPEC 19.3).</summary>
/// <remarks>Its arms are excluded syntax: they were parsed and source-checked but are not retained (SPEC 19.5).</remarks>
public sealed class CompileTimeSwitchKoto : ExpressionKoto
{
    /// <summary>Initializes a new instance of the <see cref="CompileTimeSwitchKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The complete Case Group span.</param>
    public CompileTimeSwitchKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CompileTimeSwitch;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        // Unparsing reproduces the original Case Group, whose arms exist only as source text.
        var source = this.CodeContext.SourceDocument?.SourceText;
        var span = this.Span;
        if (source is null || span.Length == 0 || span.End > source.Length)
        {
            builder.Append("#switch");
            return;
        }

        var lineStart = span.Start == 0 ? 0 : source.LastIndexOf('\n', span.Start - 1) + 1;
        var baseline = span.Start - lineStart;
        var text = source.AsSpan(span.Start, span.Length).TrimEnd();
        var first = true;
        foreach (var range in text.Split('\n'))
        {
            var line = text[range].TrimEnd('\r');
            if (!first)
            {
                builder.AppendLine();
                var indent = line.Length - line.TrimStart(' ').Length;
                line = line[Math.Min(baseline, indent)..];
            }

            builder.Append(line);
            first = false;
        }
    }
}
