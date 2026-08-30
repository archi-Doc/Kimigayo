// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a static group. The root syntax tree is also represented by this type so it can
/// share member parsing and qualified <c>rootgroup A.B</c> expansion with ordinary groups.
/// </summary>
[TinyhandObject]
public sealed partial class GroupKoto : StaticCollectionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Group;

    /// <inheritdoc/>
    public override TokenKind TokenKind => TokenKind.Group;

    /// <summary>Initializes a new instance of the <see cref="GroupKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    public GroupKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal GroupKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}
