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
public sealed partial class GroupKoto : CollectionKoto
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
            this.ParseFieldAndFunctionMembers(ref reader, false);
            return;
        }

        this.ParseRoot(ref reader);
    }

    private void ParseRoot(ref TokenReader reader)
    {
        ConsumeBlockStart(ref reader);
        var declarationOrder = DeclarationOrder.None;
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
                var groupKoto = this.GetOrAddCollection(name, TokenKind.Group, state, token.Span);
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    groupKoto.Parse(ref reader);
                }

                continue;
            }

            if (tokenKind is TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract)
            {
                reader.Advance();
                var supportsGenericHeader = tokenKind == TokenKind.Struct;
                var declaration = Parser.ParseGroupDeclaration(
                    ref reader,
                    supportsGenericHeader,
                    supportsGenericHeader);
                if (reader.IsExcluded)
                {
                    reader.SkipCurrentBlock(false);
                    continue;
                }

                var state = reader.TakeContext();
                var collection = this.GetOrAddCollection(declaration.Name, tokenKind, state, token.Span);
                if (declaration.GenericArguments is not null && collection.GenericArguments.Count == 0)
                {
                    collection.AddGenericArguments(declaration.GenericArguments);
                }

                if (declaration.Origins is not null && collection.Origins.Count == 0)
                {
                    collection.AddOrigins(declaration.Origins);
                }

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    collection.Parse(ref reader);
                }

                continue;
            }

            if (!this.TryParseFieldOrFunction(ref reader, ref declarationOrder))
            {
                SkipUnexpectedDeclaration(ref reader, token);
            }
        }
    }
}
