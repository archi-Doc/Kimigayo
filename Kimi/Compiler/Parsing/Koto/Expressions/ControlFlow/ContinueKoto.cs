// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Represents a Never-valued <c>continue</c> expression.</summary>
[TinyhandObject]
public sealed partial class ContinueKoto : ExpressionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Continue;

    /// <summary>Initializes a new instance of the <see cref="ContinueKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The keyword span.</param>
    public ContinueKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
        => builder.Append(Constants.ContinueKeyword);
}
