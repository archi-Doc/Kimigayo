// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a static group. The root syntax tree is also represented by this type so it can
/// share member parsing and qualified <c>rootgroup A.B</c> expansion with ordinary groups.
/// </summary>
public sealed class GroupKoto : DeclarationContainerKoto
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

    protected override void VisitChildrenCore(KotoVisitor visitor)
    {
        base.VisitChildrenCore(visitor);

        if (ReferenceEquals(this, this.Kotonoha.RootKoto) && this.Kotonoha.GeneratedFunction is { } generatedFunction)
        {
            visitor.Visit(generatedFunction);
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
        var state = default(RootParseState);
        this.ParseRootItems(ref reader, ref state);
    }

    /// <summary>Parses SourceDocument root items through the end of the current block.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="state">The root state, shared by directive targets and arms.</param>
    /// <remarks>
    /// Root directive targets and arms keep the root item grammar, including aliases and <c>rootgroup</c> (SPEC 6.1.1, 19.1).
    /// Excluded syntax is parsed into a detached group and contributes no root item (SPEC 19.5).
    /// </remarks>
    private void ParseRootItems(ref TokenReader reader, ref RootParseState state)
    {
        while (TryBeginDeclaration(ref reader))
        {
            if (reader.IsExcluded)
            {
                var owner = this.ExcludedRootOwner(ref reader, ref state);
                var region = Parser.BeginExcludedRegion(ref reader);
                owner.ParseRootDirectiveOrItem(ref reader, ref state);
                Parser.EndExcludedRegion(ref reader, region);
            }
            else if (reader.HasCompileTimeIfPrefix || Parser.IsCompileTimeSwitchStart(ref reader))
            {
                this.ParseRootDirectiveOrItem(ref reader, ref state);
            }
            else
            {
                this.ParseRootItem(ref reader, ref state);
            }
        }
    }

    private void ParseRootDirectiveOrItem(ref TokenReader reader, ref RootParseState state)
    {
        if (Parser.IsCompileTimeSwitchStart(ref reader))
        {
            var start = reader.CurrentTokenRange.Start;
            var selection = Parser.ScanCompileTimeSwitch(ref reader);
            Parser.RejectDirectiveBlockAttributes(ref reader);
            if (Parser.BeginCompileTimeSwitchArms(ref reader))
            {
                for (var arm = 0; Parser.TryNextCompileTimeSwitchArm(ref reader, out var header); arm++)
                {
                    if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                    {
                        reader.Expect(SyntaxForm.Body);
                        continue;
                    }

                    if (arm == selection.Selected)
                    {
                        reader.Advance();
                        this.ParseRootItems(ref reader, ref state);
                    }
                    else
                    {
                        var owner = this.ExcludedRootOwner(ref reader, ref state);
                        var region = Parser.BeginExcludedRegion(ref reader, header, header.Start);
                        reader.Advance();
                        owner.ParseRootItems(ref reader, ref state);
                        Parser.EndExcludedRegion(ref reader, region);
                    }
                }
            }

            if (Parser.UnselectedCompileTimeSwitch(ref reader, start, selection) is { } unselected)
            {
                this.Kotonoha.AddGeneratedFunctionItem(reader.CodeContext, unselected);
            }

            return;
        }

        if (reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            Parser.RejectDirectiveBlockAttributes(ref reader);
            reader.Advance();
            this.ParseRootItems(ref reader, ref state);
            return;
        }

        this.ParseRootItem(ref reader, ref state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseRootItem(ref TokenReader reader, ref RootParseState state)
    {
        var token = reader.CurrentToken;
        var tokenKind = token.Kind;
        if (tokenKind == TokenKind.Alias)
        {
            reader.Advance();
            string? aliasName = null;
            if (reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.EqualsGreaterThan)
            {
                aliasName = reader.GetIdentifier(reader.Read());
                reader.Advance();
            }

            reader.TryConsume(TokenKind.ColonColon);
            var targetSyntax = Parser.IsBoundContainerReference(ref reader) ? Parser.ParseContainerReference(ref reader) : null;
            var qualifiedName = targetSyntax is null ? KotoHelper.ParseQualifiedNameSegments(ref reader) : [];
            if (state.HasNonAliasDeclaration)
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.TopLevelKeywordAfterCode_Kd);
            }
            else
            {
                var context = reader.TakeContext();
                if (context.ModifierKind != default || context.AttributeKoto is not null)
                {
                    reader.Unexpected(SyntaxForm.Decoration, token.Span);
                }

                var alias = new AliasKoto(ref reader, qualifiedName, aliasName, token.Span, targetSyntax);
                Parser.ParseAttachedOriginBlock(ref reader, alias);
                this.AddLast(alias);
            }

            return;
        }

        state.HasNonAliasDeclaration = true;
        if (tokenKind == TokenKind.RootGroup)
        {
            reader.Advance();
            var name = KotoHelper.ValidateAndGetNamespace(ref reader);
            var context = reader.TakeContext();
            var groupKoto = this.GetOrAddDeclarationContainer(name, TokenKind.Group, context, token.Span, codeContext: reader.CodeContext);
            reader.Document(groupKoto, SourceSpan.FromBounds(token.Span.Start, reader.PreviousSyntaxEnd), context.AttributeKoto);
            groupKoto.AddHeader(TokenKind.Group, context.ModifierKind, null, null, context.AttributeKoto);
            if (reader.CurrentTokenKind == TokenKind.StartBlock)
            {
                groupKoto.Parse(ref reader);
            }

            return;
        }

        if (this.TryParseDeclarationContainer(ref reader, token))
        {
            return;
        }

        var oldPosition = reader.Position;
        var item = Parser.ParseBlockItem(ref reader);
        reader.ExpectLineEnd();

        if (item is not null && !reader.InExcludedSyntax)
        {
            this.Kotonoha.AddGeneratedFunctionItem(reader.CodeContext, item);
        }

        if (reader.Position == oldPosition)
        {
            reader.Advance();
        }
    }

    /// <summary>Gets the owner of excluded root syntax: this group inside excluded syntax, otherwise a detached group.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="state">The root state that keeps the detached group.</param>
    /// <returns>The group that receives the excluded declarations.</returns>
    private GroupKoto ExcludedRootOwner(ref TokenReader reader, ref RootParseState state)
        => reader.InExcludedSyntax ? this : state.Detached ??= (GroupKoto)CreateStandalone(reader.CodeContext, TokenKind.Group, default, this.Span, string.Empty);

    /// <summary>The source-order state of SourceDocument root items (SPEC 18.1.1, 19.5).</summary>
    private struct RootParseState
    {
        /// <summary>Whether an ordinary declaration or executable item precedes, so a later alias is misplaced.</summary>
        public bool HasNonAliasDeclaration;

        /// <summary>The detached group that receives excluded root declarations.</summary>
        public GroupKoto? Detached;
    }
}
