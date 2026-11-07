// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

// Bodies and their items (SPEC 14.2.1, Appendix F.4.1, F.4.4, F.10.1): the item loop of an executable body with its directive targets and
// arms and its Constraint prefix (SPEC 7.5, 19.4), one block item's dispatch, a required indented block, a single-item body, and a directive's
// body among declarations.
// Input: a reader at a body or an item; an ExecutableItemState for the enclosing body. Output: a CodeBlockKoto, or an item's node, or null
// when the item was omitted. Selected items only: excluded syntax is parsed with the same grammar and its items are dropped.
// Invariants: an indented body's items are regions of their own; a block ends at its last written token; an item that read nothing is
// stepped over, so the loop ends.
// Prohibitions: the forms of an item belong to their own parts; the selection of a directive to Parser.Directives.
public static partial class Parser
{
    internal static CodeBlockKoto ParseDeclarationDirectiveBody(ref TokenReader reader, DeclarationContainerKoto declarationContext)
    {
        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return ParseRequiredBlock(ref reader);
        }

        var start = reader.CurrentTokenRange.Start;
        var temporary = declarationContext.CreateDetached(reader.CodeContext, reader.CurrentTokenRange);
        temporary.Parse(ref reader);
        var items = new List<Koto>();
        items.AddRange(temporary.TypeConstraints);
        items.AddRange(temporary.Members);
        items.AddRange(temporary.NestedDeclarationContainers);
        var block = new CodeBlockKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, reader.PreviousSyntaxEnd)), items)
        {
            DeclarationContext = declarationContext.TokenKind,
        };
        return block;
    }

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
        var items = default(TemporaryKotoList);
        var state = new ExecutableItemState(function, originOwner);
        var end = ParseExecutableBlockItems(ref reader, ref items, ref state);
        reader.RestoreContext(blockContext);
        reader.RestoreRegion(region);
        return new CodeBlockKoto(
            ref reader,
            SourceSpan.FromBounds(start.Start, Math.Max(start.End, end)),
            items.ToArray());
    }

    /// <summary>Parses one indented block, from its StartBlock through its EndBlock, appending its selected items.</summary>
    /// <param name="reader">The token reader positioned at <see cref="TokenKind.StartBlock"/>.</param>
    /// <param name="items">The selected items of the enclosing executable body.</param>
    /// <param name="state">The Constraint-prefix state of the enclosing body, shared by its directive targets and arms.</param>
    /// <returns>The end offset of the block.</returns>
    /// <remarks>
    /// Directive targets and arms are parsed by this same loop (SPEC 19.4): a selected one adds its items directly; excluded
    /// syntax is parsed with the same grammar and recovery, extends the source-order Constraint prefix, and is dropped.
    /// </remarks>
    private static int ParseExecutableBlockItems(ref TokenReader reader, ref TemporaryKotoList items, ref ExecutableItemState state)
    {
        reader.Advance(); // StartBlock
        return ParseExecutableItems(ref reader, ref items, ref state);
    }

    private static int ParseExecutableItems(ref TokenReader reader, ref TemporaryKotoList items, ref ExecutableItemState state, bool stopAtCaseGroupBoundary = false)
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

                if (!hasSourceItem)
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
                var region = BeginExcludedRegion(ref reader);
                ParseExecutableDirectiveOrItem(ref reader, ref items, ref state);
                EndExcludedRegion(ref reader, region);
            }
            else if (reader.HasCompileTimeIfPrefix || IsCompileTimeSwitchStart(ref reader))
            {
                ParseExecutableDirectiveOrItem(ref reader, ref items, ref state);
            }
            else
            {
                ParseExecutableItem(ref reader, ref items, ref state);
            }
        }

        // The tokenizer closes every block at the end of the source; the loop ends here only after the block's EndBlock.
        return reader.PreviousSyntaxEnd;
    }

    private static void ParseExecutableDirectiveOrItem(ref TokenReader reader, ref TemporaryKotoList items, ref ExecutableItemState state)
    {
        if (IsCompileTimeSwitchStart(ref reader))
        {
            var arms = CompileTimeSwitchArms.Begin(ref reader);
            while (arms.TryNext(ref reader, out var selected, out var header, out var invalidItems))
            {
                if (invalidItems)
                {
                    var start = BeginInvalidSwitchItems(ref reader);
                    ParseExecutableItems(ref reader, ref items, ref state, stopAtCaseGroupBoundary: true);
                    EndInvalidSwitchItems(ref reader, start);
                }
                else if (selected)
                {
                    ParseExecutableBlockItems(ref reader, ref items, ref state);
                }
                else
                {
                    var region = BeginExcludedRegion(ref reader, header, header.Start);
                    ParseExecutableBlockItems(ref reader, ref items, ref state);
                    EndExcludedRegion(ref reader, region);
                }
            }

            if (arms.Unselected(ref reader) is { } unselected)
            {
                items.Add(unselected);
            }

            return;
        }

        if (reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            RejectDirectiveBlockAttributes(ref reader);
            ParseExecutableBlockItems(ref reader, ref items, ref state);
            return;
        }

        ParseExecutableItem(ref reader, ref items, ref state);
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
            (reader.CurrentTokenKind == TokenKind.Func && reader.PeekKind(1) is not (TokenKind.OpenParenthesis or TokenKind.OpenBracket)) ||
            (reader.IsCurrentIdentifier("specialize") && reader.PeekKind(1) == TokenKind.Func);

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

        if (reader.AttributeKoto is { } attribute && (reader.CurrentTokenKind != TokenKind.Func || reader.PeekKind(1) is TokenKind.OpenParenthesis or TokenKind.OpenBracket))
        {
            // Only a named function among executable items takes attributes (SPEC 6.7, F.10); a misplaced one attaches to nothing,
            // so the item it precedes is checked on its own.
            reader.Unexpected(SyntaxForm.Attribute, attribute.Span);
            reader.PopAttribute();
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
                if (!closed || arguments.Length is < 1 or > 2 || trailing ||
                    (labels is not null && labels.Length > 0 && labels[0] is not null) ||
                    (arguments.Length == 2 && (labels is null || labels.Length < 2 || labels[1] != "message")))
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
        switch (token.Kind)
        {
            case TokenKind.Let:
            case TokenKind.Var:
                reader.Advance();
                return ParseField(ref reader, token);

            case TokenKind.Func:
                if (reader.PeekKind(1) is TokenKind.OpenParenthesis or TokenKind.OpenBracket)
                {
                    return ParseExpression(ref reader);
                }

                reader.Advance();
                var function = ParseFuncDeclaration(ref reader);
                if (function is null)
                {
                    return null;
                }

                ParseNamedFunctionBody(ref reader, function);
                return function;

            case TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract when IsContainerDeclarationStart(ref reader):
                // No Container nests in an executable block (SPEC 6.1.1); the declaration is skipped with its body.
                OmitDeclaration(ref reader, reader.Unexpected(token.Kind == TokenKind.Extension ? SyntaxForm.ExtensionDeclaration : SyntaxForm.ContainerDeclaration, token.Span));
                return null;

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

    // The required indented body of a construct, parsed as its owning function's block when it has one (a function expression's).
    internal static CodeBlockKoto ParseRequiredBlock(ref TokenReader reader, bool statementHeader = false, FunctionKoto? function = null)
    {
        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return ParseFunctionBlock(ref reader, function);
        }

        // A token left on a statement header's line, before the indented body that follows, is where the header's line should
        // end, as after a function header; the body is still the header's.
        if (statementHeader && reader.IndentedBodyFollows())
        {
            reader.Expect(SyntaxForm.LineEnd);
            reader.SkipHeaderLine();
            return ParseFunctionBlock(ref reader, function);
        }

        // An empty body is supplied where the body is missing. It is a recovery: a check that reads its Type or its normal
        // completion reads the parser's guess and rests on the Error (DIAGNOSTICS.md §4.3). The rest of a statement header's line
        // goes with it, so an aligned else on the next line still continues the header.
        var cause = reader.Expect(SyntaxForm.Body);
        var supplied = new CodeBlockKoto(ref reader, new SourceSpan(reader.PreviousSyntaxEnd, 0), []);
        reader.CodeContext.RecordRecovery(supplied, cause);
        if (statementHeader)
        {
            reader.SkipHeaderLine();
        }

        return supplied;
    }

    /// <summary>Reports an indented body where its region requires a single-item body: inside a single-item body, every construct
    /// with a Body in the same region, an anonymous function included, uses one (SPEC F.4.4). The body is still parsed.</summary>
    /// <param name="reader">The token reader, where the construct's body starts.</param>
    internal static void CheckIndentedBodyRegion(ref TokenReader reader)
    {
        if (reader.SingleBodyRegion)
        {
            reader.Expect(SyntaxForm.ArrowBody);
        }
    }

    internal static CodeBlockKoto ParseRequiredBody(ref TokenReader reader, bool ifBody = false)
    {
        if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
        {
            CheckIndentedBodyRegion(ref reader);
            return ParseRequiredBlock(ref reader, true);
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
