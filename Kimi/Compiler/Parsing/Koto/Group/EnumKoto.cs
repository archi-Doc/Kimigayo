// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an enumeration declaration.
/// </summary>
[TinyhandObject]
public sealed partial class EnumKoto : GroupKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Enum;

    /// <summary>Initializes a new instance of the <see cref="EnumKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    public EnumKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal EnumKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}
