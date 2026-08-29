// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a structure declaration.
/// </summary>
[TinyhandObject]
public sealed partial class StructKoto : GroupKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Struct;

    /// <summary>Initializes a new instance of the <see cref="StructKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    public StructKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal StructKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}
