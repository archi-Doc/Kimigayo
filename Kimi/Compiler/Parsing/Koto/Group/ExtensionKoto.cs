// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an extension declaration.
/// </summary>
[TinyhandObject]
public sealed partial class ExtensionKoto : GroupKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Extension;

    /// <summary>Initializes a new instance of the <see cref="ExtensionKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    public ExtensionKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal ExtensionKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}
