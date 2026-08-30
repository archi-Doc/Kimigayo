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

    /// <inheritdoc/>
    public override bool SupportsTypeConstraints => true;

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

    /// <inheritdoc/>
    public override void Parse(ref TokenReader reader)
    {
        ConsumeBlockStart(ref reader);
        while (TryBeginDeclaration(ref reader))
        {
            var token = reader.CurrentToken;
            if (token.Kind != TokenKind.Associate)
            {
                SkipUnexpectedDeclaration(ref reader, token);
                continue;
            }

            reader.Advance();
            if (!Parser.IsTypeConstraintStart(ref reader))
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
                continue;
            }

            var constraint = Parser.ParseTypeConstraint(ref reader);
            if (constraint is not null)
            {
                this.AddTypeConstraint(constraint);
            }
        }
    }

    /// <inheritdoc/>
    protected override void WriteTypeConstraintTo(IsKoto constraint, ref IndentedStringBuilder builder)
    {
        builder.Append(Constants.AssociateKeyword);
        builder.AppendSpace();
        constraint.WriteTo(ref builder);
    }
}
