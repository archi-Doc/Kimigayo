// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an extension declaration.
/// </summary>
[TinyhandObject]
public sealed partial class ExtensionKoto : CollectionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Extension;

    /// <inheritdoc/>
    public override TokenKind TokenKind => TokenKind.Extension;

    /// <inheritdoc/>
    public override bool IsInstantiable => false;

    /// <inheritdoc/>
    public override bool HasStaticMembersOnly => true;

    /// <summary>Gets the target named by this extension declaration.</summary>
    public string Target => this.Name;

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

    /// <inheritdoc/>
    public override void Parse(ref TokenReader reader)
        => SkipUnimplementedBody(ref reader);
}
