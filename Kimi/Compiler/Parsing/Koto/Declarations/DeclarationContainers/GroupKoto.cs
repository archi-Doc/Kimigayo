// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a static group. The root syntax tree is also represented by this type so it can
/// share member parsing and qualified <c>rootgroup A.B</c> expansion with ordinary groups.
/// </summary>
[TinyhandObject]
public sealed partial class GroupKoto : DeclarationContainerKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Group;

    /// <inheritdoc/>
    public override TokenKind TokenKind => TokenKind.Group;

    /// <inheritdoc/>
    public override bool IsInstantiable => false;

    /// <inheritdoc/>
    public override bool HasStaticMembersOnly => true;

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

    /// <inheritdoc/>
    public override void Parse(ref TokenReader reader)
    {
        if (!ReferenceEquals(this, this.Kotonoha.RootKoto))
        {
            this.ParseMembers(ref reader);
            return;
        }

        this.ParseRoot(ref reader);
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var child in base.GetChildNodes())
        {
            yield return child;
        }

        if (ReferenceEquals(this, this.Kotonoha.RootKoto) && this.Kotonoha.GeneratedFunction is not null)
        {
            yield return this.Kotonoha.GeneratedFunction;
        }
    }

    private void ParseMembers(ref TokenReader reader)
    {
        ConsumeBlockStart(ref reader);
        var declarationOrder = DeclarationOrder.None;
        while (TryBeginDeclaration(ref reader))
        {
            var token = reader.CurrentToken;
            if (this.TryParseDeclarationContainer(ref reader, token))
            {
                continue;
            }

            if (!this.TryParseFieldOrFunction(ref reader, ref declarationOrder))
            {
                SkipUnexpectedDeclaration(ref reader, token);
            }
        }
    }

    private bool TryParseDeclarationContainer(ref TokenReader reader, Token token)
    {
        var tokenKind = token.Kind;
        if (tokenKind is not (TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract))
        {
            return false;
        }

        reader.Advance();
        var supportsGenericHeader = tokenKind == TokenKind.Struct;
        var declaration = Parser.ParseDeclarationContainerHeader(
            ref reader,
            supportsGenericHeader,
            supportsGenericHeader);
        if (reader.IsExcluded)
        {
            reader.SkipCurrentBlock(false);
            return true;
        }

        var state = reader.TakeContext();
        var container = this.GetOrAddDeclarationContainer(declaration.Name, tokenKind, state, token.Span);
        if (declaration.GenericArguments is not null && container.GenericArguments.Count == 0)
        {
            container.AddGenericArguments(declaration.GenericArguments);
        }

        if (declaration.Origins is not null && container.Origins.Count == 0)
        {
            container.AddOrigins(declaration.Origins);
        }

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            container.Parse(ref reader);
        }

        return true;
    }

    private void ParseRoot(ref TokenReader reader)
    {
        ConsumeBlockStart(ref reader);
        var hasNonAliasDeclaration = false;
        while (TryBeginDeclaration(ref reader))
        {
            var token = reader.CurrentToken;
            var tokenKind = token.Kind;
            if (tokenKind == TokenKind.Alias)
            {
                reader.Advance();
                var qualifiedName = KotoHelper.ParseQualifiedNameSegments(ref reader);
                if (hasNonAliasDeclaration)
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.TopLevelKeywordAfterCode_Kd);
                }
                else
                {
                    var aliasKoto = new AliasKoto(ref reader, qualifiedName);
                    if (!reader.IsExcluded)
                    {
                        this.AddLast(aliasKoto);
                    }
                }

                continue;
            }

            hasNonAliasDeclaration = true;
            if (tokenKind == TokenKind.RootGroup)
            {
                reader.Advance();
                var name = KotoHelper.ValidateAndGetNamespace(ref reader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(false);
                    continue;
                }

                var state = reader.TakeContext();
                var groupKoto = this.GetOrAddDeclarationContainer(name, TokenKind.Group, state, token.Span);
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    groupKoto.Parse(ref reader);
                }

                continue;
            }

            if (this.TryParseDeclarationContainer(ref reader, token))
            {
                continue;
            }

            var oldPosition = reader.Position;
            var item = Parser.ParseBlockItem(ref reader, out var isDeclaration, requiresFunctionBody: false);
            var hasTrailingExpression = !isDeclaration;
            if (reader.CurrentTokenKind == TokenKind.Semicolon)
            {
                hasTrailingExpression = false;
                reader.Advance();
            }
            else if (reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock) && reader.CanRead)
            {
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
            }

            var isExcluded = item is FunctionKoto function ? function.IsExcluded : reader.IsExcluded;
            if (item is not null && !isExcluded)
            {
                this.Kotonoha.AddGeneratedFunctionItem(reader.CodeContext, item, hasTrailingExpression);
            }

            if (reader.Position == oldPosition)
            {
                reader.Advance();
            }
        }
    }
}
