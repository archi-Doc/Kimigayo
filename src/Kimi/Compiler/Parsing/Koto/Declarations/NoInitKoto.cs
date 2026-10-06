// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>A declaration directive that completes Scalar-array construction without initial stores (SPEC 4.3.4).</summary>
public sealed class NoInitKoto : Koto
{
    /// <summary>Initializes a new instance of the <see cref="NoInitKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="span">The directive's source span.</param>
    public NoInitKoto(ref TokenReader reader, SourceSpan span)
        : base(ref reader, span)
    {
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.NoInit;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder) => builder.Append("noinit");
}
