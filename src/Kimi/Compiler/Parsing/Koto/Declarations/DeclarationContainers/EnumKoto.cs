// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents an enumeration declaration.
/// </summary>
public sealed class EnumKoto : DeclarationContainerKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Enum;

    /// <inheritdoc/>
    public override TokenKind TokenKind => TokenKind.Enum;

    /// <inheritdoc/>
    public override bool IsInstantiable => true;

    /// <inheritdoc/>
    public override bool SupportsGenerics => true;

    /// <inheritdoc/>
    public override bool SupportsOrigins => true;

    /// <inheritdoc/>
    public override bool SupportsTypeConstraints => true;

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

    /// <inheritdoc/>
    public override void Parse(ref TokenReader reader)
    {
        this.ParseMembers(ref reader, true, false);
        if (this.Members.Count == 0)
        {
            this.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }
    }
}
