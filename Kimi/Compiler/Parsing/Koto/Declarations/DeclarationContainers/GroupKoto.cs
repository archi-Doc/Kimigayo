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
        if (ReferenceEquals(this, this.Kotonoha.RootKoto))
        {
            this.ParseRoot(ref reader);
        }
        else
        {
            this.ParseMembers(ref reader, parseTypeConstraints: false, parseDeclarationContainers: true);
        }
    }

    protected override IEnumerable<Koto> GetChildNodes()
    {
        foreach (var child in base.GetChildNodes())
        {
            yield return child;
        }

        if (ReferenceEquals(this, this.Kotonoha.RootKoto) && this.Kotonoha.GeneratedFunction is { } generatedFunction)
        {
            yield return generatedFunction;
        }
    }

    private void ParseRoot(ref TokenReader reader)
    {
        ConsumeBlockStart(ref reader);
        var hasNonAliasDeclaration = false;
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
                hasNonAliasDeclaration = true;
                var caseGroup = Parser.ParseCompileTimeCaseGroup(ref reader);
                caseGroup = Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, caseGroup);
                this.Kotonoha.AddGeneratedFunctionItem(reader.CodeContext, caseGroup, true);
                continue;
            }

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
                    if (!isExcluded)
                    {
                        var aliasKoto = new AliasKoto(ref reader, qualifiedName);
                        this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, aliasKoto));
                    }
                }

                continue;
            }

            hasNonAliasDeclaration = true;
            if (tokenKind == TokenKind.RootGroup)
            {
                reader.Advance();
                var name = KotoHelper.ValidateAndGetNamespace(ref reader);
                if (isExcluded)
                {
                    reader.SkipCurrentBlock(false);
                    continue;
                }

                var state = reader.TakeContext();
                if (compileTimeIfPrefixes is not null)
                {
                    var standalone = DeclarationContainerKoto.CreateStandalone(
                        reader.CodeContext,
                        TokenKind.Group,
                        state,
                        token.Span,
                        name.ToString());
                    if (reader.CurrentTokenKind == TokenKind.StartBlock)
                    {
                        standalone.Parse(ref reader);
                    }

                    this.AddLast(Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, standalone));
                    continue;
                }

                var groupKoto = this.GetOrAddDeclarationContainer(name, TokenKind.Group, state, token.Span);
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    groupKoto.Parse(ref reader);
                }

                continue;
            }

            if (this.TryParseDeclarationContainer(ref reader, token, compileTimeIfPrefixes, isExcluded))
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

            if (item is not null && !isExcluded)
            {
                item = Parser.ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, item);
                this.Kotonoha.AddGeneratedFunctionItem(reader.CodeContext, item, hasTrailingExpression);
            }

            if (reader.Position == oldPosition)
            {
                reader.Advance();
            }
        }
    }
}
