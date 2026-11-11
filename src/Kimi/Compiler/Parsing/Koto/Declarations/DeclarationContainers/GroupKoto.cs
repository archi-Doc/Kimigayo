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

    protected override void ForEachChildSlot(ref ChildSlots slots)
    {
        base.ForEachChildSlot(ref slots);
        if (ReferenceEquals(this, this.Kotonoha.RootKoto))
        {
            slots.Fixed(this.Kotonoha.GeneratedFunction);
        }
    }

    private void ParseRoot(ref TokenReader reader)
    {
        ConsumeBlockStart(ref reader);
        var list = new RootItems(this);
        Parser.ParseItems(ref reader, ref list);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseRootItem(ref TokenReader reader, ref RootParseState state)
    {
        var token = reader.CurrentToken;
        var tokenKind = token.Kind;
        if (tokenKind == TokenKind.Alias)
        {
            // An alias takes no modifier or attribute (SPEC F.6.1 AliasDeclaration): the prefix's attributes are reported and attach to
            // nothing, its access word is rejected, and the prefix is taken before its target is read, so nothing lands there.
            Parser.ReportPendingAttributes(ref reader);
            Parser.RejectAccessWord(ref reader);
            _ = reader.TakeContext();
            reader.Advance();
            string? aliasName = null;
            if (reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.EqualsGreaterThan)
            {
                aliasName = reader.GetIdentifier(reader.Read());
                reader.Advance();
            }

            reader.TryConsume(TokenKind.ColonColon);
            var targetSyntax = Parser.IsBoundContainerReference(ref reader) ? Parser.ParseType(ref reader, Parser.TypeContext.AnnotatedType) : null;
            if (targetSyntax is not null)
            {
                // The line ends after the target, as after a plain path; an attached Origin block may follow.
                reader.ExpectLineEnd(bodyMayFollow: true);
            }

            var qualifiedName = targetSyntax is null ? KotoHelper.ParseQualifiedNameSegments(ref reader) : [];
            if (qualifiedName is null)
            {
                // A target whose Name failed declares no alias; the declaration is skipped with what it would attach.
                Parser.OmitDeclaration(ref reader, reader.Diagnostic.LastError);
                return;
            }

            if (state.HasNonAliasDeclaration)
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.TopLevelKeywordAfterCode_Kd);
            }
            else
            {
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
            if (name.IsEmpty)
            {
                // A root group whose Name failed merges with no other; it is skipped with its body.
                Parser.OmitDeclaration(ref reader, reader.Diagnostic.LastError);
                return;
            }

            var context = reader.TakeContext();
            var groupKoto = this.GetOrAddDeclarationContainer(name, TokenKind.Group, context, token.Span, codeContext: reader.CodeContext);
            reader.Document(groupKoto, reader.PreviousSyntaxEnd, context.AttributeKoto);
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

        if (Parser.IsEffectStart(ref reader, specification: false))
        {
            Parser.ParseBodyEffect(ref reader, null); // The top-level body has no leading region (SPEC 7.5).
            return;
        }

        // A root function declaration is the root's own item (SPEC 6.1.1, F.6.1); every other item is the execution scope's.
        var oldPosition = reader.Position;
        Koto? item;
        if (Parser.IsFunctionDefinitionStart(ref reader))
        {
            // Only `public main` takes an access word among the root's functions (SPEC 6.1.1, 22.4); the Name is compared as written.
            if (!(reader.ModifierKind.ExtractAccessibilityModifiers() == ModifierKind.Public && reader.GetSpan(reader.PeekToken(1)) is "main"))
            {
                Parser.RejectAccessWord(ref reader);
            }

            item = Parser.ParseFunctionItem(ref reader);
        }
        else
        {
            item = Parser.ParseBlockItem(ref reader);
        }

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

    /// <summary>The SourceDocument root items (SPEC F.6.1): directive targets and arms keep the root item grammar, aliases and
    /// <c>rootgroup</c> included (SPEC 6.1.1, 19.1); excluded syntax goes to its <see cref="DeclarationContainerKoto.ExcludedOwner{TContainer}"/>
    /// and contributes no root item (SPEC 19.4).</summary>
    /// <param name="root">The root group.</param>
    private struct RootItems(GroupKoto root) : Parser.IItemList
    {
        private ExcludedOwner<GroupKoto> owner = new(root);

        private RootParseState state;

        public readonly bool RequiresItem => false;

        public void EnterExcluded(ref TokenReader reader)
            => this.owner.Enter(ref reader);

        public void ParseItem(ref TokenReader reader)
            => this.owner.Of(ref reader).ParseRootItem(ref reader, ref this.state);

        // A #switch keeps its marker only in selected syntax (Parser.CompileTimeSwitchArms.Unselected), so it goes to the root.
        public readonly void AddUnselected(ref TokenReader reader, Koto unselected)
            => this.owner.Container.Kotonoha.AddGeneratedFunctionItem(reader.CodeContext, unselected);
    }

    /// <summary>The source-order state of SourceDocument root items (SPEC 18.2.1, 19.4).</summary>
    private struct RootParseState
    {
        /// <summary>Whether an ordinary declaration or executable item precedes, so a later alias is misplaced.</summary>
        public bool HasNonAliasDeclaration;
    }
}
