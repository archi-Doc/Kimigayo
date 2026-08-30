// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a contract declaration.
/// </summary>
[TinyhandObject]
public sealed partial class ContractKoto : CollectionKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Contract;

    /// <inheritdoc/>
    public override TokenKind TokenKind => TokenKind.Contract;

    /// <inheritdoc/>
    public override bool IsInstantiable => false;

    /// <summary>Initializes a new instance of the <see cref="ContractKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The declaration source span.</param>
    public ContractKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal ContractKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}
