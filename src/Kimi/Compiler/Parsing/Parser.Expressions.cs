// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

// Operator expressions (SPEC 12.5, 13, Appendix F.9): the binding-power loop, prefix and postfix operators, `@` operations, ranges and
// argument lists.
// Input: a reader at an expression and a minimum binding power. Output: the expression's node; an ErrorKoto stands for a missing operand.
// Invariants: precedence comes only from Parser.cs's tables and constants; a misplaced attribute is reported and discarded and the expression
// goes on.
// Prohibitions: what an operator means, and which operand Types it takes, belong to Binding; an operand's own form to Parser.PrimaryExpressions.
public static partial class Parser
{
    internal static Koto ParseRequiredExpression(ref TokenReader reader)
    {
        if (IsExpressionBoundary(ref reader))
        {
            reader.Expect(SyntaxForm.Expression);
            return reader.NewErrorKoto();
        }

        return ParseExpression(ref reader);
    }

    /// <summary>Parses an expression using the requested minimum binding power.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="minBindingPower">The minimum accepted binding power.</param>
    /// <returns>The parsed expression.</returns>
    public static Koto ParseExpression(ref TokenReader reader, int minBindingPower = 0)
    {
        if (IsBlockStatementStart(ref reader, expressionPosition: true))
        {
            reader.AddDiagnostic(DiagnosticCode.BlockStatementInExpression_Kd);
            return ParseBlockStatement(ref reader);
        }

        var left = IsLabelPrefix(ref reader)
            ? ParseLabeledExpression(ref reader)
            : ParsePrefixExpression(ref reader, minBindingPower);
        return ContinueExpression(ref reader, left, minBindingPower);
    }

    /// <summary>Continues an expression from its parsed left operand: its postfix and infix operators down to the binding power, the one
    /// loop of <see cref="ParseExpression(ref TokenReader, int)"/>.</summary>
    /// <param name="reader">The token reader after the left operand.</param>
    /// <param name="left">The parsed left operand.</param>
    /// <param name="minBindingPower">The minimum accepted binding power.</param>
    /// <returns>The parsed expression.</returns>
    private static Koto ContinueExpression(ref TokenReader reader, Koto left, int minBindingPower)
    {
        while (true)
        {
            var tokenKind = reader.CurrentTokenKind;
            if (IsPostfixOperator[(byte)tokenKind] && TryParsePostfixExpression(ref reader, ref left))
            {
                continue;
            }

            if (tokenKind == TokenKind.Sharp)
            {
                // An expression takes no attribute (SPEC 6.7); the misplaced one is discarded and the expression continues after it.
                reader.ReportMisplacedAttributes();
                continue;
            }

            if (tokenKind == TokenKind.At)
            {
                // A conversion takes a Type, but still obeys infix precedence:
                // -value@T -> (-value)@T, value * other@T -> value * (other@T).
                if (minBindingPower > InfixLeftBindingPower[(byte)tokenKind])
                {
                    break;
                }

                var token2 = reader.Read();
                var operationSpan = reader.CanRead ? reader.TokenSpanAt(reader.Position) : default;
                Koto typeKoto;
                var open = false;
                if (IsExpressionBoundary(ref reader))
                {
                    reader.Expect(SyntaxForm.Type);
                    typeKoto = reader.NewErrorKoto();
                }
                else if (IsBareOperationTarget(ref reader))
                {
                    // A bare built-in Semantics shorthand, @move or @copy completes the target (SPEC §13.5.1);
                    // a following '.', '(' or '[' continues the postfix chain: x@uniq.m(), f@move(), p@copy.x.
                    typeKoto = ParseBareOperationTarget(ref reader);
                }
                else
                {
                    // Direct borrow Origins are inferred. Complete payload Types retain their own annotations.
                    typeKoto = ParseType(ref reader, TypeContext.Target);
                    if (CheckAdaptationTarget(typeKoto) is { } annotated)
                    {
                        typeKoto = new ErrorKoto(ref reader, typeKoto.Span) { Cause = annotated }; // The target rests on the Error.
                    }

                    typeKoto = ConversionOperationTarget(ref reader, typeKoto);

                    // SPEC F.9.4: a target read as a Type is a ClosedTarget exactly when a DelimitedTarget's closer ends it; every
                    // OpenTarget ends at a Name, a '>', a binding set's '}' or a '?'. A failed target judges nothing.
                    open = typeKoto is not ErrorKoto && reader.PeekKind(-1) is not (TokenKind.CloseParenthesis or TokenKind.CloseBracket);
                }

                if (reader.IsCurrentIdentifier("during"))
                {
                    reader.Unexpected(SyntaxForm.AdaptationDuring);
                    reader.Advance();
                    _ = ParseOriginAtom(ref reader);
                }

                left = new ConversionKoto(
                    ref reader,
                    SourceSpan.FromBounds(left.Span.Start, Math.Max(token2.Span.End, typeKoto.Span.End)),
                    left,
                    typeKoto);
                if (typeKoto is TypeSemanticsKoto { HoverOperation: not null })
                {
                    reader.Hover(left, token2.Span);
                    reader.Hover(left, operationSpan);
                }

                // SPEC F.9.4: Adapted := Prefix ("@" (ClosedTarget PostfixSuffix* | OpenTarget))*: no PostfixSuffix follows an
                // OpenTarget. The suffix is read over the adaptation, the grouping the repair writes, so later checks judge that form.
                var suffix = reader.CurrentTokenRange;
                if (open && IsPostfixOperator[(byte)reader.CurrentTokenKind] && TryParsePostfixExpression(ref reader, ref left))
                {
                    reader.Unexpected(SyntaxForm.OpenTargetSuffix, suffix);
                }

                continue;
            }

            if (tokenKind is TokenKind.DotDot or TokenKind.DotDotEquals)
            {
                if (minBindingPower > RangeLeftBindingPower)
                {
                    break;
                }

                var rangeToken = reader.Read();
                var end = ParseRangeEnd(ref reader, rangeToken);
                if (left is RangeKoto)
                {
                    reader.Unexpected(SyntaxForm.ChainedRange, rangeToken.Span);
                    continue;
                }

                left = new RangeKoto(
                    ref reader,
                    rangeToken.Span,
                    left,
                    end,
                    rangeToken.Kind == TokenKind.DotDotEquals);
                continue;
            }

            var leftBindingPower = InfixLeftBindingPower[(byte)tokenKind];
            if (leftBindingPower == 0 || leftBindingPower < minBindingPower)
            {
                break;
            }

            var rightBindingPower = InfixRightBindingPower[(byte)tokenKind];
            var token = reader.Read();
            DiagnosticKey? chained = null;
            if (leftBindingPower == ComparisonBindingPower && IsNonAssociativeComparison(ref reader, tokenKind, left))
            {
                // Keep parsing for recovery, but require explicit parentheses for all combinations
                // of ordering, equality, and runtime is comparisons (SPEC 13.1). The combined node is
                // the parser's guess: it stands for this error, and checks of its form rest on it.
                if (reader.Diagnostic.Add(token.Span, DiagnosticCode.ChainedComparison_Kd))
                {
                    chained = reader.Diagnostic.LastError;
                }
            }

            Koto right;
            if (IsExpressionBoundary(ref reader))
            {
                reader.Expect(SyntaxForm.Expression);
                right = reader.NewErrorKoto();
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
            }
            else if (token.Kind == TokenKind.Is && !reader.IsParsingCompileTimeCondition)
            {
                left = ParseRuntimeTypeTest(ref reader, token, left);
            }
            else
            {
                right = ParseExpression(ref reader, rightBindingPower);
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
            }

            if (chained is { } cause)
            {
                reader.CodeContext.RecordRecovery(left, cause);
            }
        }

        return left;
    }

    private static bool IsNonAssociativeComparison(ref TokenReader reader, TokenKind tokenKind, Koto left)
    {
        // Directive conditions reject every is test separately; do not report it twice as a chain.
        var runtimeIs = !reader.IsParsingCompileTimeCondition;
        return (tokenKind != TokenKind.Is || runtimeIs) &&
            ((left is BinaryKoto comparison && IsComparisonKind[(int)comparison.Akind]) || (runtimeIs && left is IsKoto));
    }

    /// <summary>
    /// Determines whether the token after <c>@</c> is a bare built-in Semantics name, <c>move</c> or <c>copy</c> that
    /// completes the operation target by itself, so that no qualified Type parse is attempted (SPEC §13.5.1).
    /// </summary>
    private static bool IsBareOperationTarget(ref TokenReader reader)
    {
        var token = reader.CurrentToken;
        if (!token.Kind.IsIdentifierOrContextualKeyword())
        {
            return false;
        }

        var text = reader.GetSpan(token);
        if (!text.SequenceEqual(Constants.MoveOperation) && !text.SequenceEqual(Constants.CopyOperation) &&
            !SemanticsMaskHelper.TryParse(text, out SemanticsKind _))
        {
            return false; // follow is consumed as a postfix operation before this point (SPEC 13.1).
        }

        // A slash starts a full Semantics form, an Origin brace keeps the Type parser's diagnostics,
        // and adjacent generic arguments are not a shorthand. '?' cannot make an operation into a Type.
        return reader.PeekKind(1) switch
        {
            TokenKind.Slash or TokenKind.OpenBrace => false,
            TokenKind.LessThan => reader.PeekToken(1).Span.Start != token.Span.End,
            _ => true,
        };
    }

    private static Koto ParseBareOperationTarget(ref TokenReader reader)
    {
        var operation = reader.Read();
        if (reader.CurrentTokenKind != TokenKind.Question)
        {
            return new TypeSemanticsKoto(ref reader, operation);
        }

        // One rejected optional suffix chain belongs to this operation; it names no Type for Binding to resolve.
        var cause = reader.Unexpected(SyntaxForm.SemanticsShorthandSuffix);
        Token question;
        do
        {
            question = reader.Read();
        }
        while (reader.CurrentTokenKind == TokenKind.Question);

        return new ErrorKoto(ref reader, SourceSpan.FromBounds(operation.Span.Start, question.Span.End)) { Cause = cause };
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader, int minBindingPower = 0)
    {
ProcessPrefix:
        var tokenKind = reader.CurrentTokenKind;
        if (reader.HeaderRegion && tokenKind is TokenKind.If or TokenKind.Match or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Do or TokenKind.Func)
        {
            var cause = reader.Unexpected(SyntaxForm.HeaderBodyExpression);
            if (reader.IndentedBodyFollows())
            {
                // The indented body after the header is the statement's, so the expression is not read: the rest of the header's
                // line rests on this Error, and the statement keeps its body (DIAGNOSTICS.md §4.4).
                var start = reader.CurrentTokenRange.Start;
                _ = reader.SkipHeaderLine();
                return new ErrorKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, reader.PreviousSyntaxEnd))) { Cause = cause };
            }
        }

        if (tokenKind == TokenKind.Sharp)
        {
            reader.ReportMisplacedAttributes();
            goto ProcessPrefix;
        }

        if ((tokenKind == TokenKind.Try || IsPrefixOperator[(byte)tokenKind]) && reader.CanRead)
        {
            var token = reader.Read();
            if (tokenKind == TokenKind.Try && minBindingPower > TryBindingPower)
            {
                // A level-2 prefix operator cannot take a try expression directly: write -(try x) (SPEC §13.1).
                reader.Unexpected(SyntaxForm.TryOperand, token.Span);
            }

            Koto operand;
            if (IsExpressionBoundary(ref reader))
            {
                reader.Expect(SyntaxForm.Expression);
                operand = reader.NewErrorKoto();
            }
            else
            {
                operand = ParseExpression(ref reader, tokenKind == TokenKind.Try ? TryBindingPower : PrefixBindingPower);
            }

            // Formatting roots require literal syntax so their embedded evaluation can be deferred.
            if (tokenKind == TokenKind.Dollar &&
                (!((operand is InvocationKoto { Method: IdentifierNameKoto { IdentifierName: "abort" }, ArgumentNodes.Count: 1 } abort && abort.GetArgumentLabel(0) is null) ||
                   (operand is InvocationKoto { Method: IdentifierNameKoto { IdentifierName: "tryWrite" }, ArgumentNodes.Count: 2 } write &&
                   write.GetArgumentLabel(0) is null && write.GetArgumentLabel(1) is null && write.ArgumentNodes[1] is StringLiteralKoto or InterpolatedStringKoto)) ||
                 (reader.PeekKind(-1) == TokenKind.CloseParenthesis && reader.PeekKind(-2) == TokenKind.Comma)))
            {
                // The prefix marks nothing Binding can select; the node rests on the Error and its operand is checked on its own.
                var dollar = KotoHelper.NewUnaryKoto(ref reader, token, operand);
                if (operand.Expected(SyntaxForm.DollarOperand) is { } cause)
                {
                    reader.CodeContext.RecordRecovery(dollar, cause);
                }

                return dollar;
            }

            return tokenKind == TokenKind.Try ? new TryKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, operand.Span.End), operand) : KotoHelper.NewUnaryKoto(ref reader, token, operand);
        }

        return ParsePrimaryExpression(ref reader);
    }

    /// <summary>
    /// Recognizes the conversion operations <c>@wrap&lt;U&gt;</c> and <c>@bits&lt;U&gt;</c> (SPEC §F.3, §13.5.4): <c>wrap</c> or
    /// <c>bits</c> with adjacent Type arguments, parsed as a generic Type application and re-formed as an operation target.
    /// </summary>
    private static Koto ConversionOperationTarget(ref TokenReader reader, Koto target)
    {
        var inner = target is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } wrapped } ? wrapped : target;
        var generic = inner as GenericsKoto;
        var operation = generic is { TypeArguments.Count: > 0 } ? OperationName(generic.Identifier) : null;
        if (operation is not (Constants.WrapOperation or Constants.BitsOperation))
        {
            return target;
        }

        if (generic!.TypeArguments.Count != 1)
        {
            generic.Expected(SyntaxForm.ConversionTypeArgument);
        }

        return new TypeSemanticsKoto(ref reader, target.Span, operation == Constants.WrapOperation ? Constants.WrapOperation : Constants.BitsOperation, generic.TypeArguments[0]);

        static string? OperationName(Koto? identifier) => identifier switch
        {
            IdentifierNameKoto name => name.IdentifierName,
            TypeSemanticsKoto { Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null } simple => simple.Identifier,
            _ => null,
        };
    }

    /// <summary>
    /// Determines whether the current <c>@</c> starts the postfix follow operation <c>E@follow</c> (SPEC §13.5.5):
    /// the next token spells <c>follow</c>. A following slash is division, never a Semantics prefix.
    /// </summary>
    private static bool IsFollowOperation(ref TokenReader reader)
    {
        var next = reader.PeekToken(1);
        return next.Kind.IsIdentifierOrContextualKeyword() && reader.GetSpan(next).SequenceEqual(Constants.FollowOperation);
    }

    private static bool TryParsePostfixExpression(ref TokenReader reader, ref Koto left)
    {
        switch (reader.CurrentTokenKind)
        {
            case TokenKind.At:
                {
                    // SPEC 13.1: @follow is a level-1 postfix operation, so -r@follow.x is -((r@follow).x).
                    if (!IsFollowOperation(ref reader))
                    {
                        return false;
                    }

                    var operatorRange = reader.Read().Span;
                    var target = new TypeSemanticsKoto(ref reader, reader.Read());
                    left = new ConversionKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, target.Span.End), left, target);
                    reader.Hover(left, operatorRange);
                    reader.Hover(left, target.Span);
                    return true;
                }

            case TokenKind.Dot:
                {
                    var operatorRange = reader.CurrentTokenRange;
                    reader.Advance();

                    var accessor = reader.CurrentTokenKind == TokenKind.OpenParenthesis
                        ? ParseType(ref reader, TypeContext.Qualifier)
                        : ParseMemberName(ref reader);
                    left = new MemberAccessKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, accessor.Span.End)),
                        left,
                        accessor);
                    return true;
                }

            case TokenKind.OpenParenthesis:
                {
                    var openRange = reader.CurrentTokenRange;
                    reader.Advance();
                    var arguments = ParseArgumentList(ref reader, out var argumentLabels);
                    var end = arguments.Length == 0 ? openRange.End : Math.Max(openRange.End, arguments[^1].Span.End);
                    var unclosed = reader.ExpectCloser(TokenKind.CloseParenthesis, out var range);
                    if (arguments.Length > 0)
                    {
                        RecoverItem(ref reader, arguments[^1], unclosed);
                    }

                    end = Math.Max(end, range.End);

                    left = new InvocationKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, end),
                        left,
                        arguments,
                        argumentLabels);
                    reader.Hover(left, openRange);
                    return true;
                }

            case TokenKind.LessThan:
                {
                    if (!IsGenericPostfix(ref reader, left))
                    {
                        return false;
                    }

                    left = ParseGenericsPostfix(ref reader, left);
                    return true;
                }

            case TokenKind.OpenBracket:
                {
                    var openRange = reader.CurrentTokenRange;
                    reader.Advance();
                    Koto index;
                    if (IsExpressionBoundary(ref reader))
                    {
                        reader.Expect(SyntaxForm.Expression);
                        index = reader.NewErrorKoto();
                    }
                    else
                    {
                        var region = reader.EnterRegion();
                        index = ParseExpression(ref reader);
                        reader.RestoreRegion(region);
                    }

                    reader.TrySkipSeparatorsTo(TokenKind.CloseBracket);
                    RecoverItem(ref reader, index, reader.ExpectCloser(TokenKind.CloseBracket, out var range));
                    left = new IndexKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(Math.Max(openRange.End, index.Span.End), range.End)), left, index);

                    return true;
                }

            case TokenKind.PlusPlus:
                {
                    var token = reader.Read();
                    left = new UnaryKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, token.Span.End), KotoKind.PostfixIncrement, left);
                    return true;
                }

            case TokenKind.MinusMinus:
                {
                    var token = reader.Read();
                    left = new UnaryKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, token.Span.End), KotoKind.PostfixDecrement, left);
                    return true;
                }
        }

        return false;
    }

    private static bool IsGenericPostfix(ref TokenReader reader, Koto left)
        => left is IdentifierNameKoto or MemberAccessKoto or GenericsKoto &&
            OpensAdjacentTypeArguments(ref reader, left.Span);

    private static Koto[] ParseArgumentList(ref TokenReader reader, out string?[]? argumentLabels)
    {
        var region = reader.EnterRegion();
        var arguments = ParseArgumentListCore(ref reader, out argumentLabels);
        reader.RestoreRegion(region);
        return arguments;
    }

    private static Koto[] ParseArgumentListCore(ref TokenReader reader, out string?[]? argumentLabels)
    {// (arg0, arg1, )
        var arguments = default(TemporaryKotoList);
        var labels = default(TemporaryList<string?>);
        var hasLabels = false;
        Koto? last = null;
        argumentLabels = default;

        var tokenKind = reader.CurrentTokenKind;
        while (reader.CanRead &&
               tokenKind != TokenKind.CloseParenthesis)
        {
            if (tokenKind.IsIdentifierOrContextualKeyword() &&
                reader.PeekKind(1) == TokenKind.Colon)
            {
                var label = reader.GetIdentifier(reader.CurrentToken);
                reader.Advance(2);

                if (!hasLabels)
                {
                    hasLabels = true;
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        labels.Add(null);
                    }
                }

                labels.Add(label);
            }
            else if (hasLabels)
            {
                // The misplaced argument is kept as a recovery, so overload selection rests on this Error.
                var misplaced = reader.Unexpected(SyntaxForm.PositionalAfterNamed);
                labels.Add(null);
                var positional = ParseRequiredExpression(ref reader);
                reader.CodeContext.RecordRecovery(positional, misplaced);
                arguments.Add(positional);
                last = positional;
                goto Separator;
            }

            last = ParseRequiredExpression(ref reader);
            arguments.Add(last);

Separator:

            // A block-valued argument (including match) can leave a dedent separator
            // before the next comma. Keep the comma mandatory for the argument list.
            reader.TrySkipSeparatorsTo(TokenKind.Comma);
            reader.TrySkipSeparatorsTo(TokenKind.CloseParenthesis);
            tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Comma)
            {
                reader.Advance();
                tokenKind = reader.CurrentTokenKind;
                if (tokenKind == TokenKind.CloseParenthesis)
                {
                    break;
                }

                continue;
            }

            if (tokenKind != TokenKind.CloseParenthesis)
            {
                // The list's shape is uncertain from here: the last argument is a recovery, so overload selection rests on the
                // Error. The recovery stays on the list's line, up to its next comma or closer, never in a following statement.
                var cause = reader.Expect(SyntaxForm.Comma);
                if (last is not null)
                {
                    reader.CodeContext.RecordRecovery(last, cause);
                }

                reader.SkipListItem();
                if (reader.TryConsume(TokenKind.Comma))
                {
                    tokenKind = reader.CurrentTokenKind;
                    continue;
                }
            }

            break;
        }

        if (hasLabels)
        {
            argumentLabels = labels.ToArray();
        }

        return arguments.ToArray();
    }

    private static Koto? ParseRangeEnd(ref TokenReader reader, Token rangeToken)
    {
        if (IsExpressionBoundary(ref reader) ||
            reader.CurrentTokenKind is TokenKind.DotDot or TokenKind.DotDotEquals)
        {
            if (rangeToken.Kind == TokenKind.DotDotEquals)
            {
                // A closed range needs its end; the open form may omit it. The recovery node stands for the missing end.
                reader.Expect(SyntaxForm.Expression);
                return reader.NewErrorKoto();
            }

            return default;
        }

        return ParseExpression(ref reader, RangeRightBindingPower);
    }
}
