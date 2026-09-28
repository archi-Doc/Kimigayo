// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an invalid expression created during parser recovery.
/// </summary>
public sealed class ErrorKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Error;

    /// <summary>Initializes a new instance of the <see cref="ErrorKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The invalid source span.</param>
    public ErrorKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => builder.Append("Error");
}
