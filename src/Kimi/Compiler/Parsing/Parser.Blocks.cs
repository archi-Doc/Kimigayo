// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

// Bodies and their items (SPEC 14.2.1, Appendix F.4.1, F.4.4, F.10.1, F.11): the one item loop of every item list that names
// Directive<Item>, with its directive targets and arms (SPEC 19.4); an executable body's items and their Constraint prefix (SPEC 7.5);
// one block item's dispatch, a required indented block and a single-item body.
// Input: a reader at a body or an item; an IItemList for the enclosing list. Output: a CodeBlockKoto, or an item's node, or null when the
// item was omitted. Selected items only: excluded syntax is parsed with the same grammar into the owner its list prepares.
// Invariants: an indented body's items are regions of their own; a block ends at its last written token; an item that read nothing is
// stepped over, so the loop ends.
// Prohibitions: the forms of an item belong to their own parts and lists; the selection of a directive to Parser.Directives.
public static partial class Parser
{
    /// <summary>Parses an indentation-delimited expression block.</summary>
    /// <param name="reader">The token reader positioned at <see cref="TokenKind.StartBlock"/>.</param>
    /// <returns>The parsed block.</returns>
    public static CodeBlockKoto ParseBlock(ref TokenReader reader)
        => ParseFunctionBlock(ref reader, null);

    internal static CodeBlockKoto ParseFunctionBlock(ref TokenReader reader, FunctionKoto? function, Koto? originOwner = null)
    {
        var start = reader.CurrentTokenRange;
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.Expect(SyntaxForm.Body);
            return new CodeBlockKoto(ref reader, start, []);
        }

        // The items of an indented body are regions of their own (SPEC F.4.1, F.4.4), whichever construct owns the body.
        var region = reader.EnterRegion();
        var blockContext = reader.TakeContext();
        var list = new ExecutableItems(function, originOwner);
        reader.Advance(); // StartBlock
        var end = ParseItems(ref reader, ref list);
        reader.RestoreContext(blockContext);
        reader.RestoreRegion(region);
        return new CodeBlockKoto(
            ref reader,
            SourceSpan.FromBounds(start.Start, Math.Max(start.End, end)),
            list.Items.ToArray());
    }

    /// <summary>An item list that names <c>Directive&lt;Item&gt;</c> (SPEC F.11): its directive targets and arms keep its item category.</summary>
    internal interface IItemList
    {
        /// <summary>Gets a value indicating whether each indented list of this category needs a source item before selection (SPEC 14.2.1).</summary>
        bool RequiresItem { get; }

        /// <summary>Prepares the owner of the excluded syntax the loop enters next (SPEC 19.4).</summary>
        /// <param name="reader">The token reader, before the excluded syntax.</param>
        void EnterExcluded(ref TokenReader reader);

        /// <summary>Parses one item of this category at the reader, its prefixes consumed.</summary>
        /// <param name="reader">The token reader.</param>
        void ParseItem(ref TokenReader reader);

        /// <summary>Adds the marker of a <c>#switch</c> that selected no arm (SPEC 19.3).</summary>
        /// <param name="reader">The token reader.</param>
        /// <param name="unselected">The marker.</param>
        void AddUnselected(ref TokenReader reader, Koto unselected);
    }

    /// <summary>Parses the items of one indented list after its StartBlock, through its EndBlock: the one item loop of every list that
    /// names <c>Directive&lt;Item&gt;</c> (SPEC F.6, F.10.1, F.11).</summary>
    /// <typeparam name="TList">The item category of the list.</typeparam>
    /// <param name="reader">The token reader after the list's StartBlock.</param>
    /// <param name="list">The list, shared by its directive targets and arms.</param>
    /// <param name="stopAtCaseGroupBoundary">Whether Case Group recovery must leave its next arm or end unconsumed.</param>
    /// <returns>The end offset of the list.</returns>
    /// <remarks>
    /// Directive targets and arms are parsed by this same loop (SPEC 19.4): a selected one adds its items to the list; excluded
    /// syntax is parsed with the same grammar and recovery into the owner <see cref="IItemList.EnterExcluded"/> prepares.
    /// </remarks>
    internal static int ParseItems<TList>(ref TokenReader reader, ref TList list, bool stopAtCaseGroupBoundary = false)
        where TList : IItemList, allows ref struct
    {
        var hasSourceItem = false;
        while (reader.CanRead)
        {
            reader.SkipSeparators();
            if (!reader.CanRead)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                if (stopAtCaseGroupBoundary)
                {
                    return reader.PreviousSyntaxEnd;
                }

                if (!hasSourceItem && list.RequiresItem)
                {
                    reader.Expect(SyntaxForm.Body);
                }

                // The block ends at its last written token. The EndBlock stands at the next dedented line, after the
                // blank and comment lines and that line's indentation, none of which belongs to the block.
                var end = reader.PreviousSyntaxEnd;
                reader.Advance();
                return end;
            }

            var unavailableDeclaration = ConsumeAttributeAndModifier(ref reader, out var isEnd, allowCompileTimeDirectives: true, stopAtCaseGroupBoundary);
            if (unavailableDeclaration)
            {
                hasSourceItem = true;
                SkipUnavailableDeclaration(ref reader);
                continue;
            }

            if (isEnd)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                continue; // A header left before the EndBlock was reported with it.
            }

            // Source items count before selection; a directive counts with its required syntax (SPEC 14.2.1).
            hasSourceItem = true;
            if (reader.IsExcluded)
            {
                list.EnterExcluded(ref reader);
                var region = BeginExcludedRegion(ref reader);
                ParseDirectiveOrItem(ref reader, ref list);
                EndExcludedRegion(ref reader, region);
            }
            else if (reader.HasCompileTimeIfPrefix || IsCompileTimeSwitchStart(ref reader))
            {
                ParseDirectiveOrItem(ref reader, ref list);
            }
            else
            {
                list.ParseItem(ref reader);
            }
        }

        // The tokenizer closes every block at the end of the source; the loop ends here only after the block's EndBlock.
        return reader.PreviousSyntaxEnd;
    }

    private static void ParseDirectiveOrItem<TList>(ref TokenReader reader, ref TList list)
        where TList : IItemList, allows ref struct
    {
        if (IsCompileTimeSwitchStart(ref reader))
        {
            var arms = CompileTimeSwitchArms.Begin(ref reader);
            while (arms.TryNext(ref reader, out var selected, out var header, out var invalidItems))
            {
                if (invalidItems)
                {
                    list.EnterExcluded(ref reader);
                    var start = BeginInvalidSwitchItems(ref reader);
                    ParseItems(ref reader, ref list, stopAtCaseGroupBoundary: true);
                    EndInvalidSwitchItems(ref reader, start);
                }
                else if (selected)
                {
                    reader.Advance(); // StartBlock
                    ParseItems(ref reader, ref list);
                }
                else
                {
                    list.EnterExcluded(ref reader);
                    var region = BeginExcludedRegion(ref reader, header, header.Start);
                    reader.Advance(); // StartBlock
                    ParseItems(ref reader, ref list);
                    EndExcludedRegion(ref reader, region);
                }
            }

            if (arms.Unselected(ref reader) is { } unselected)
            {
                list.AddUnselected(ref reader, unselected);
            }

            return;
        }

        if (reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            RejectDirectiveBlockAttributes(ref reader);
            reader.Advance(); // StartBlock
            ParseItems(ref reader, ref list);
            return;
        }

        list.ParseItem(ref reader);
    }

    /// <summary>The items of an executable body (SPEC F.10.1): excluded syntax extends its Constraint prefix and is dropped.</summary>
    /// <param name="function">The function whose body this is, if any.</param>
    /// <param name="originOwner">The declaration that owns leading Origin relations, if not the function.</param>
    private ref struct ExecutableItems(FunctionKoto? function, Koto? originOwner) : IItemList
    {
        public TemporaryKotoList Items;

        private ExecutableItemState state = new(function, originOwner);

        public readonly bool RequiresItem => true;

        public readonly void EnterExcluded(ref TokenReader reader)
        {
        }

        public void ParseItem(ref TokenReader reader)
            => ParseExecutableItem(ref reader, ref this.Items, ref this.state);

        public void AddUnselected(ref TokenReader reader, Koto unselected)
            => this.Items.Add(unselected);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseExecutableItem(ref TokenReader reader, ref TemporaryKotoList items, ref ExecutableItemState state)
    {
        // Excluded syntax is parsed like selected syntax but registers nothing with the enclosing declarations (SPEC 19.4).
        var excluded = reader.InExcludedSyntax;
        var function = state.Function;
        if (IsOriginRelationStart(ref reader))
        {
            ReportPendingAttributes(ref reader);
            RejectAccessWord(ref reader);
            var relation = ParseOriginRelation(ref reader);
            if ((function ?? state.OriginOwner) is { } owner && !state.SeenExecutableItem && function is not { IsAnonymous: true } and not { IsDestructor: true })
            {
                if (!excluded)
                {
                    OriginClauses.Add(owner, relation);
                }
            }
            else
            {
                relation.Unexpected(SyntaxForm.OriginClause);
            }

            return;
        }

        if (IsEffectStart(ref reader, specification: false))
        {
            ParseBodyEffect(ref reader, state.SeenExecutableItem ? null : function);
            return;
        }

        // SPEC 7.5: every function, constructor, destructor and accessor body begins with its Constraint prefix, whatever
        // the declaration's generic parameters; a leading `value is Dog` is never an expression statement.
        if ((function is not null || state.OriginOwner is PropertyAccessorKoto) && IsFunctionConstraintStart(ref reader, function))
        {
            // A root-qualified subject is never a generic parameter; do not read "::" as an identifier. A destructor or
            // accessor is never a conditional member, so no subject is permitted in its body.
            var rootQualified = reader.CurrentTokenKind == TokenKind.ColonColon;
            var subject = rootQualified ? default : reader.GetSpan(reader.CurrentToken);
            var isGenericParameter = !rootQualified && function is { IsDestructor: false } && (function.IsGenericParameter(subject) || function.IsDeclaringTypeParameter(subject));
            if (!state.SeenExecutableItem || isGenericParameter)
            {
                ReportPendingAttributes(ref reader);
                RejectAccessWord(ref reader);
                var misplaced = state.SeenExecutableItem || !isGenericParameter ? reader.Unexpected(SyntaxForm.ConstraintPrefix) : default(DiagnosticKey?);
                var constraint = ParseTypeConstraint(ref reader);
                if (constraint is not null && function is { IsDestructor: false } && !excluded)
                {
                    // The prefix stays a Constraint of the function (SPEC 7.5); a misplaced one is a recovery, so Binding's own judgement of it is derived.
                    function.AddTypeConstraint(constraint);
                    if (misplaced is { } cause)
                    {
                        reader.CodeContext.RecordRecovery(constraint, cause);
                    }
                }

                return;
            }
        }

        state.SeenExecutableItem = true;
        var oldPosition = reader.Position;
        var item = ParseBlockItem(ref reader);
        if (item is not null && !excluded)
        {
            items.Add(item);
        }

        if (reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock))
        {
            if (item is IdentifierNameKoto { IdentifierName: "during" })
            {
                // The line is a detached borrow annotation, not a statement; Binding does not resolve 'during' as a Name.
                reader.CodeContext.RecordRecovery(item, reader.Unexpected(SyntaxForm.DetachedDuring, item.Span));
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
            }
            else
            {
                reader.ExpectLineEnd();
            }
        }

        if (reader.Position == oldPosition)
        {
            reader.Advance();
        }
    }

    /// <summary>
    /// Gets whether a Container keyword at the current token declares a Container. The contextual keyword is a Name (SPEC F.3)
    /// where an expression continues after it, through an operator, a member access, a call or an index, and where its line ends
    /// without an indented body; anything else, its Name or a token no expression takes there, makes it a declaration.
    /// </summary>
    /// <param name="reader">The token reader.</param>
    /// <returns><see langword="true"/> for a Container declaration.</returns>
    internal static bool IsContainerDeclarationStart(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind is not (TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract))
        {
            return false;
        }

        var next = reader.PeekKind(1);
        return next switch
        {
            TokenKind.Separator => reader.PeekKind(2) == TokenKind.StartBlock,
            TokenKind.EndBlock => false,
            _ => InfixLeftBindingPower[(byte)next] == 0 && !IsPostfixOperator[(byte)next],
        };
    }

    private static bool IntroducesDeclaration(ref TokenReader reader)
        => IsContainerDeclarationStart(ref reader) ||
            reader.CurrentTokenKind is TokenKind.Let or TokenKind.Var or TokenKind.Init or TokenKind.Drop or TokenKind.Computed or TokenKind.Property or TokenKind.Associate or TokenKind.Alias ||
            IsFunctionDefinitionStart(ref reader) ||
            (reader.IsCurrentIdentifier("specialize") && reader.PeekKind(1) == TokenKind.Func);

    /// <summary>Whether a named function definition starts here (Appendix F.6.1 FunctionDeclaration, F.10 LocalFunction): <c>func</c>
    /// before a Name. <c>func</c> before a parameter or generic parameter list is an anonymous function, an expression (SPEC 9.4).</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns><see langword="true"/> at a named function definition.</returns>
    internal static bool IsFunctionDefinitionStart(ref TokenReader reader)
        => reader.CurrentTokenKind == TokenKind.Func && reader.PeekKind(1) is not (TokenKind.OpenParenthesis or TokenKind.OpenBracket);

    /// <summary>Parses a named function definition of the execution scope or of a body, with its Body.</summary>
    /// <param name="reader">The token reader at <c>func</c> (<see cref="IsFunctionDefinitionStart"/>).</param>
    /// <returns>The function, or <see langword="null"/> when its header failed.</returns>
    internal static FunctionKoto? ParseFunctionItem(ref TokenReader reader)
    {
        reader.Advance();
        var function = ParseFuncDeclaration(ref reader);
        if (function is null)
        {
            return null;
        }

        ParseNamedFunctionBody(ref reader, function);
        return function;
    }

    internal static Koto? ParseBlockItem(ref TokenReader reader)
    {
        if (reader.ModifierKind != ModifierKind.NoModifier && !IntroducesDeclaration(ref reader))
        {
            // Modifiers introduce a declaration; the line and the body it would have introduced are skipped as one.
            var cause = reader.Expect(SyntaxForm.Declaration);
            if (!reader.InExcludedSyntax)
            {
                // Excluded syntax records no omission for the selected program (SPEC 19.4).
                reader.CodeContext.Kotonoha.RecordOmission(cause);
            }

            SkipDeclarationLine(ref reader);
            return null;
        }

        if (reader.AttributeKoto is { } attribute && !IsFunctionDefinitionStart(ref reader))
        {
            // Only a named function among executable items takes attributes (SPEC 6.7, F.10); a misplaced one attaches to nothing,
            // so the item it precedes is checked on its own.
            reader.Unexpected(SyntaxForm.Attribute, attribute.Span);
            reader.AttributeKoto = null;
        }

        if (reader.CurrentTokenKind == TokenKind.Underscore && reader.PeekKind(1) == TokenKind.Equals)
        {
            var discardToken = reader.Read();
            reader.Advance();
            var value = ParseRequiredExpression(ref reader);
            return new DiscardKoto(ref reader, SourceSpan.FromBounds(discardToken.Span.Start, Math.Max(discardToken.Span.End, value.Span.End)), value);
        }

        if (reader.CurrentTokenKind == TokenKind.Dollar && reader.PeekKind(1) is TokenKind.Identifier or TokenKind.Require)
        {
            var probe = reader;
            var start = probe.Read();
            var require = probe.CurrentTokenKind == TokenKind.Require;
            if (require || probe.IsCurrentIdentifier("expect"))
            {
                reader = probe;
                reader.Advance();
                if (!reader.TryConsume(TokenKind.OpenParenthesis))
                {
                    reader.Expect(SyntaxForm.OpenParenthesis);
                    return reader.NewErrorKoto();
                }

                var arguments = ParseArgumentList(ref reader, out var labels);
                var trailing = reader.PeekKind(-1) == TokenKind.Comma;
                var closed = reader.TryConsume(TokenKind.CloseParenthesis);
                DiagnosticKey? malformed = null;

                // "(" Expression ("," "message" ":" Expression)? ")" (SPEC F.10.1): an unlabeled condition and an optional message.
                var wellFormed = closed && !trailing && arguments.Length switch
                {
                    1 => labels is null or [] or [null, ..],
                    2 => labels is [null, "message", ..],
                    _ => false,
                };
                if (!wellFormed)
                {
                    malformed = reader.Unexpected(SyntaxForm.TestVerificationArguments, start.Span);
                }

                // A malformed list keeps its condition; a second argument stands as a recovery, so no message check repeats the Error.
                var message = arguments.Length > 1 ? (malformed is { } cause ? new ErrorKoto(ref reader, arguments[1].Span) { Cause = cause } : arguments[1]) : null;
                return new TestVerificationKoto(ref reader, SourceSpan.FromBounds(start.Span.Start, reader.PreviousSyntaxEnd), require, arguments.Length > 0 ? arguments[0] : reader.NewErrorKoto(), message);
            }
        }

        if (reader.IsCurrentIdentifier("specialize") && reader.PeekKind(1) == TokenKind.Func)
        {
            return ParseSpecialization(ref reader);
        }

        if (reader.CurrentTokenKind == TokenKind.Require)
        {
            return ParseRequire(ref reader);
        }

        if (IsBlockStatementStart(ref reader))
        {
            return ParseBlockStatement(ref reader);
        }

        var token = reader.CurrentToken;
        if (IsContainerDeclarationStart(ref reader))
        {
            // No Container nests in an executable block (SPEC 6.1.1); the declaration is skipped with its body.
            OmitDeclaration(ref reader, reader.Unexpected(token.Kind == TokenKind.Extension ? SyntaxForm.ExtensionDeclaration : SyntaxForm.ContainerDeclaration, token.Span));
            return null;
        }

        switch (token.Kind)
        {
            case TokenKind.Let:
            case TokenKind.Var:
                reader.Advance();
                return ParseField(ref reader, token);

            case TokenKind.Func:
                if (!IsFunctionDefinitionStart(ref reader))
                {
                    return ParseExpression(ref reader);
                }

                // A local function takes no access word (SPEC 9.3, F.10 LocalFunction).
                RejectAccessWord(ref reader);
                return ParseFunctionItem(ref reader);

            default:
                return ParseExpression(ref reader);
        }
    }

    // unsafe is contextual: it introduces a statement only before a Body (or an obsolete body colon/missing body
    // at statement start). A following operator, call, index, or member access keeps it an ordinary Name (SPEC F.3).
    private static bool IsBlockStatementStart(ref TokenReader reader, bool expressionPosition = false)
        => reader.CurrentTokenKind == TokenKind.Defer ||
            (reader.IsCurrentIdentifier(Constants.UnsafeKeyword) &&
                (reader.PeekKind(1) is TokenKind.EqualsGreaterThan or TokenKind.StartBlock ||
                    (!expressionPosition && reader.PeekKind(1) == TokenKind.Colon) ||
                    (reader.PeekKind(1) == TokenKind.Separator && reader.PeekKind(2) == TokenKind.StartBlock) ||
                    (!expressionPosition && reader.PeekKind(1) is TokenKind.Separator or TokenKind.EndBlock or TokenKind.Invalid)));

    private static BlockStatementKoto ParseBlockStatement(ref TokenReader reader)
    {
        var isUnsafe = reader.IsCurrentIdentifier(Constants.UnsafeKeyword);
        var header = reader.Read();
        var context = reader.TakeContext();
        var body = ParseRequiredBody(ref reader);
        reader.RestoreContext(context);
        var span = SourceSpan.FromBounds(header.Span.Start, Math.Max(header.Span.End, body.Span.End));
        return isUnsafe
            ? new UnsafeBlockKoto(ref reader, span, body, body.IsExpressionBody)
            : new DeferredBlockKoto(ref reader, span, body, body.IsExpressionBody);
    }

    // The required indented body after a construct's header, parsed as its owning function's block when it has one (a function expression's).
    internal static CodeBlockKoto ParseRequiredBlock(ref TokenReader reader, FunctionKoto? function = null)
    {
        // Inside a single-item body, every construct with a Body in the same region, an anonymous function included, uses one
        // (SPEC F.4.4); the indented body is still parsed.
        if (reader.SingleBodyRegion)
        {
            reader.Expect(SyntaxForm.ArrowBody);
        }

        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return ParseFunctionBlock(ref reader, function);
        }

        // A token left on the header's line, before the indented body that follows, is where the header's line should end, as
        // after a function header; the body is still the header's.
        if (reader.IndentedBodyFollows())
        {
            reader.Expect(SyntaxForm.LineEnd);
            reader.SkipHeaderLine();
            return ParseFunctionBlock(ref reader, function);
        }

        // An empty body is supplied where the body is missing. It is a recovery: a check that reads its Type or its normal
        // completion reads the parser's guess and rests on the Error (DIAGNOSTICS.md §4.3). The rest of the header's line goes
        // with it, so an aligned else on the next line still continues the header.
        var cause = reader.Expect(SyntaxForm.Body);
        var supplied = new CodeBlockKoto(ref reader, new SourceSpan(reader.PreviousSyntaxEnd, 0), []);
        reader.CodeContext.RecordRecovery(supplied, cause);
        reader.SkipHeaderLine();
        return supplied;
    }

    internal static CodeBlockKoto ParseRequiredBody(ref TokenReader reader, bool ifBody = false)
    {
        if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
        {
            return ParseRequiredBlock(ref reader);
        }

        var expression = ParseSingleBodyItem(ref reader, ifBody);
        return new CodeBlockKoto(ref reader, expression.Span, [expression]) { IsExpressionBody = true };
    }

    internal static Koto ParseSingleBodyItem(ref TokenReader reader, bool ifBody = false)
    {
        var headerEnd = reader.PreviousEnd;
        var arrow = reader.Read();
        if (!reader.SameLine(headerEnd, arrow.Span.Start) || !reader.SameLine(arrow.Span.End, reader.CurrentTokenRange.Start) || IsExpressionBoundary(ref reader))
        {
            // The expression body belongs on the header's line, right after its arrow. The Error expression is supplied for the
            // missing body, a recovery like the empty block of a missing indented body.
            var cause = reader.Expect(SyntaxForm.Expression);
            var supplied = reader.NewErrorKoto();
            reader.CodeContext.RecordRecovery(supplied, cause);
            return supplied;
        }

        var region = reader.EnterSingleBody(ifBody);
        try
        {
            if (reader.CurrentTokenKind == TokenKind.Dollar ||
                (reader.CurrentTokenKind == TokenKind.Underscore && reader.PeekKind(1) == TokenKind.Equals))
            {
                return ParseBlockItem(ref reader) ?? reader.NewErrorKoto();
            }

            if (reader.CurrentTokenKind == TokenKind.Require)
            {
                return ParseRequire(ref reader);
            }

            if (IsEffectStart(ref reader, specification: false))
            {
                // A single item is an expression or a statement and has no leading region (SPEC F.10.1, 7.5), so an effect item
                // there is misplaced in every body; the body holds the Error it rests on, as a missing body does.
                var misplaced = ParseBodyEffect(ref reader, null);
                var supplied = reader.NewErrorKoto();
                if (misplaced is { } cause)
                {
                    reader.CodeContext.RecordRecovery(supplied, cause);
                }

                return supplied;
            }

            return IsBlockStatementStart(ref reader) ? ParseBlockStatement(ref reader) : ParseRequiredExpression(ref reader);
        }
        finally
        {
            reader.RestoreRegion(region);
        }
    }
}
