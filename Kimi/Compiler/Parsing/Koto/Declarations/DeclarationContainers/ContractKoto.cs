// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a contract declaration.
/// </summary>
[TinyhandObject]
public sealed partial class ContractKoto : DeclarationContainerKoto
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
            var isExcluded = reader.IsExcluded;
            var compileTimeIfPrefixes = reader.TakeCompileTimeIfPrefixes();
            if (isExcluded)
            {
                Parser.SkipExcludedSyntax(ref reader);
                continue;
            }

            if (Parser.IsCompileTimeCaseStart(ref reader))
            {
                var caseGroup = Parser.ParseCompileTimeCaseGroup(ref reader, this);
                this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, caseGroup));
                continue;
            }

            if (reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.StartBlock)
            {
                var body = Parser.ParseDeclarationDirectiveBody(ref reader, this);
                this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, body));
                continue;
            }

            var token = reader.CurrentToken;
            if (token.Kind is TokenKind.Let or TokenKind.Var)
            {
                reader.Advance();
                var property = Parser.ParseProperty(ref reader, ref token);
                if (property is not null)
                {
                    property.IsContractRequirement = true;
                    if (!property.HasInlineAccessors || property.InitializerKoto is not null)
                    {
                        property.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, token.Kind.ToText());
                        continue;
                    }

                    this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, property));
                }

                continue;
            }

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
            if (constraint is not null && !isExcluded)
            {
                constraint.IsAssociatedConstraint = true;
                if (compileTimeIfPrefixes is null)
                {
                    this.AddTypeConstraint(constraint);
                }
                else
                {
                    this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, constraint));
                }
            }
        }
    }

    /// <inheritdoc/>
    protected override void WriteTypeConstraintTo(IsKoto constraint, ref IndentedStringBuilder builder)
    {
        if (!constraint.IsAssociatedConstraint)
        {
            builder.Append("associate ");
        }

        constraint.WriteTo(ref builder);
    }
}
