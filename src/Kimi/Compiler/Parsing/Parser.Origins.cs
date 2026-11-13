// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Origin syntax (SPEC 8.1.2, Appendix F.8): relations and attached relation blocks, a declaration's Origin parameters, a Type's Origin
// annotation and its braces, borrow suffixes, and the rejection of the lists a Callable or an adaptation target takes none of.
// Input: a reader at the Origin syntax, or a parsed Type it annotates. Output: the nodes of the annotation, or the annotated Type.
// Invariants: an item before text that a list's recovery skipped is a recovery (Parser.Recovery).
// Prohibitions: what an Origin Name denotes, and whether a relation holds, belong to Binding and the ownership analyses.
public static partial class Parser
{
    internal static bool IsOriginRelationStart(ref TokenReader reader)
    {
        if (!reader.IsCurrentIdentifier("origin"))
        {
            return false;
        }

        if (reader.PeekKind(1).IsIdentifierOrContextualKeyword())
        {
            return true;
        }

        // Keep ordinary origin(...) calls available, while accepting a grouped left side.
        if (reader.PeekKind(1) == TokenKind.OpenParenthesis)
        {
            var depth = 0;
            for (var offset = 1; offset < reader.Remaining; offset++)
            {
                var kind = reader.PeekKind(offset);
                if (kind == TokenKind.OpenParenthesis)
                {
                    depth++;
                }
                else if (kind == TokenKind.CloseParenthesis && --depth == 0)
                {
                    var next = reader.PeekToken(offset + 1);
                    return next.Kind is TokenKind.EqualsEquals or TokenKind.And || reader.GetSpan(next) is "outlives";
                }
                else if (kind is TokenKind.Separator or TokenKind.EndBlock or TokenKind.Invalid)
                {
                    break;
                }
            }
        }

        return false;
    }

    internal static OriginRelationKoto ParseOriginRelation(ref TokenReader reader)
    {
        var start = reader.Read().Span.Start;
        var left = ParseOriginExpression(ref reader);
        var equality = reader.TryConsume(TokenKind.EqualsEquals);
        DiagnosticKey? cause = null;
        if (!equality)
        {
            if (reader.IsCurrentIdentifier("outlives"))
            {
                reader.Advance();
            }
            else if (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock || !reader.CanRead)
            {
                // The line ends after the left operand: the operator is missing at its insertion point, and the right operand,
                // missing at the same point, rests on it (DIAGNOSTICS.md §4.4).
                var at = new SourceSpan(reader.PreviousSyntaxEnd, 0);
                reader.Diagnostic.Add(at, DiagnosticCode.OriginRelationOperator_Kd);
                var missing = new ErrorKoto(ref reader, at) { Cause = reader.Diagnostic.LastError };
                var incomplete = new OriginRelationKoto(ref reader, SourceSpan.FromBounds(start, left.Span.End), left, missing, false);
                RecoverItem(ref reader, incomplete, missing.Cause);
                reader.ExpectLineEnd();
                return incomplete;
            }
            else
            {
                reader.AddDiagnostic(DiagnosticCode.OriginRelationOperator_Kd);
                cause = reader.Diagnostic.LastError;
                // A misspelled relation operator, a comparison operator or `=`, is the cause; preserve the right operand for recovery.
                // A missing operator before an Origin atom does not consume that atom.
                if (IsComparisonOperator(reader.CurrentTokenKind) || reader.CurrentTokenKind == TokenKind.Equals)
                {
                    reader.Advance();
                }
            }
        }

        var right = ParseOriginExpression(ref reader);
        var relation = new OriginRelationKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(left.Span.End, right.Span.End)), left, right, equality);
        RecoverItem(ref reader, relation, cause);
        reader.ExpectLineEnd();
        return relation;
    }

    internal static void ParseAttachedOriginBlock(ref TokenReader reader, Koto owner)
    {
        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return;
        }

        reader.Advance();
        while (reader.CanRead)
        {
            reader.SkipSeparators();
            if (reader.TryConsume(TokenKind.EndBlock))
            {
                return;
            }

            if (IsOriginRelationStart(ref reader))
            {
                OriginClauses.Add(owner, ParseOriginRelation(ref reader));
            }
            else if (owner is IsKoto clause && IsEffectStart(ref reader, specification: true))
            {
                clause.AddEffectBound(ParseEffectBound(ref reader));
            }
            else
            {
                reader.AddDiagnostic(DiagnosticCode.AttachedOriginRelation_Kd);
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.SkipCurrentBlock();
                }
                else
                {
                    reader.Advance();
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                }
            }
        }
    }

    private static void ParseBorrowOriginSuffix(ref TokenReader reader, Koto type)
    {
        if (!reader.IsCurrentIdentifier("during") && !reader.IsCurrentIdentifier("from"))
        {
            return;
        }

        var keyword = reader.Read();
        if (reader.GetSpan(keyword) is "from")
        {
            // SPEC 3.3.2, 23.1.3: the annotation keyword is during; the repair candidate replaces the former spelling.
            reader.Diagnostic.AddSyntax(keyword.Span, DiagnosticCode.BorrowOriginKeyword_Kd, repairs: reader.ReplaceToken(keyword.Span, "during"));
            RecoverItem(ref reader, type, reader.Diagnostic.LastError);
        }

        var expression = ParseOriginAtom(ref reader);
        RecoverItem(ref reader, type, expression is ErrorKoto error ? error.Cause : reader.CodeContext.RecoveryCause(expression));
        if (reader.CurrentTokenKind == TokenKind.And && !reader.ConstraintRequirement)
        {
            reader.AddDiagnostic(DiagnosticCode.BorrowOriginIntersection_Kd);
            RecoverItem(ref reader, type, reader.Diagnostic.LastError);
            // Recover the whole malformed intersection here, retaining the first annotation. The enclosing
            // parameter/Type delimiter is still available and is not itself missing.
            while (reader.TryConsume(TokenKind.And))
            {
                _ = ParseOriginAtom(ref reader);
            }
        }

        var span = SourceSpan.FromBounds(keyword.Span.Start, Math.Max(keyword.Span.End, expression.Span.End));
        if (type is TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false } target)
        {
            // SPEC 3.3.2: during is accepted on the borrow Semantics, raw (an optional Origin, SPEC 5.7) and a pair binder.
            target.SetBorrowOrigin(expression, span);
            if (target.SemanticsParameter is null && target.SemanticsKind is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc)
            {
                reader.Diagnostic.Add(keyword.Span, DiagnosticCode.BorrowOriginSemantics_Kd);
                RecoverItem(ref reader, type, reader.Diagnostic.LastError);
            }
        }
        else if (type is TypeSemanticsKoto { IsNamedType: true, HasOrigin: false } named)
        {
            // SPEC 3.3.2, 15.3.1: after a named Type, during binds the only slot of a one-slot schema; Binding checks the schema.
            named.SetSlotBinding(expression, span);
        }
        else
        {
            var code = type is TypeSemanticsKoto { IsNamedType: true }
                ? DiagnosticCode.BorrowOriginBindingSet_Kd
                : DiagnosticCode.BorrowOriginTarget_Kd;
            reader.Diagnostic.Add(keyword.Span, code);
            RecoverItem(ref reader, type, reader.Diagnostic.LastError);
        }

        // Consume malformed repetitions locally without changing the first annotation.
        while (reader.IsCurrentIdentifier("during") || reader.CurrentTokenKind == TokenKind.Question)
        {
            var extra = reader.Read();
            var code = extra.Kind == TokenKind.Question
                ? DiagnosticCode.BorrowOriginSuffixOrder_Kd
                : DiagnosticCode.DuplicateBorrowOrigin_Kd;
            reader.Diagnostic.Add(extra.Span, code);
            RecoverItem(ref reader, type, reader.Diagnostic.LastError);
            if (extra.Kind != TokenKind.Question)
            {
                _ = ParseOriginAtom(ref reader);
            }
        }
    }

    /// <summary>Reports and consumes an Origin header written after a function or accessor Name; callables declare no Origin slots (SPEC 15.3.2).</summary>
    /// <param name="reader">The token reader.</param>
    private static void RejectCallableOriginList(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.OpenBrace)
        {
            reader.AddDiagnostic(DiagnosticCode.CallableOriginList_Kd);
            _ = ParseOriginParameters(ref reader);
        }
    }

    private static List<string>? ParseOriginParameters(ref TokenReader reader)
    {
        if (!reader.TryConsume(TokenKind.OpenBrace, out var open, false))
        {
            return null;
        }

        // SPEC 15.3.2: a header names one or more slots; a Type without own slots omits it, so empty braces are an error and
        // recover as an omitted header.
        var list = new OriginNameList();
        var reported = false;
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseBrace or TokenKind.EndBlock))
        {
            var token = reader.CurrentToken;
            if (!token.Kind.IsIdentifierOrContextualKeyword() || token.Kind == TokenKind.Static)
            {
                // Not a slot Name; the rest of the item is skipped, so a slot after the next comma is still read.
                reader.Expect(SyntaxForm.OriginSlotName, token);
                reader.SkipListItem();
                reported = true;
            }
            else
            {
                reader.Advance();
                list.Add(reader.GetIdentifier(token), token.Span);
                if (reader.TryConsume(TokenKind.Colon, out var relationToken, false))
                {
                    reader.Diagnostic.Add(relationToken, DiagnosticCode.OriginSchemaRelation_Kd);
                    reader.SkipListItem();
                }
            }

            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }
        }

        if (list.Count == 0 && !reported && reader.CurrentTokenKind == TokenKind.CloseBrace)
        {
            reader.Expect(SyntaxForm.OriginSlotName);
        }

        reader.ExpectCloser(TokenKind.CloseBrace, out var close);

        // The header is a related location of the diagnostics that name its slots (SPEC 15.3.2).
        list.HeaderSpan = new(open.Start, Math.Max(close.End, list.Spans.Count == 0 ? open.End : list.Spans[^1].End) - open.Start);
        return list.Count == 0 ? null : list;
    }

    /// <summary>Judges an adaptation target read as a Type by SPEC 13.5.1's two rules of its own chain, through grouping and Semantics
    /// layers down to its Core: no Origin annotation on a borrow layer, and no bound Container qualifier as the head of the Core's
    /// path (F.5). Types nested in the Core keep both forms.</summary>
    /// <param name="type">The parsed target.</param>
    /// <returns>The key of the first Error, or <see langword="null"/> when the chain writes neither form.</returns>
    private static DiagnosticKey? CheckAdaptationTarget(Koto type)
    {
        DiagnosticKey? cause = null;
        var outer = true;
        while (true)
        {
            if (type is ParenthesizedTypeKoto grouped)
            {
                type = grouped.Type;
            }
            else if (type is TypeSemanticsKoto { Type: { } inner } layer)
            {
                // SPEC 13.5.1: an outer raw layer states its Origin, a fit or a claim (SPEC 5.7); the chain's other Origins are inferred.
                if (!layer.IsTransparentWrapper && layer.HasOrigin && !(outer && layer is { SemanticsKind: SemanticsKind.Raw, SemanticsParameter: null }))
                {
                    cause ??= layer.Unexpected(SyntaxForm.AdaptationOrigin);
                }

                outer &= layer.IsTransparentWrapper;
                type = inner;
            }
            else
            {
                // Stop at a Core. Payloads and function contracts contain independent
                // complete Types; their annotations do not prescribe this operation's borrow.
                // SPEC 13.5.1, F.5: a bound Container qualifier is unavailable as the head of the target's own path, even through
                // grouping. A path's grouped head that is no Function Type is one (ContinuesGroupedType; a Tuple or Unit head is its own node).
                var head = type;
                while (head is MemberAccessKoto or GenericsKoto { Identifier: not null })
                {
                    head = head is MemberAccessKoto member ? member.Left : ((GenericsKoto)head).Identifier!;
                }

                if (!ReferenceEquals(head, type) && head is ParenthesizedTypeKoto { Type: not FunctionTypeKoto })
                {
                    cause ??= head.Unexpected(SyntaxForm.AdaptationQualifier);
                }

                return cause;
            }
        }
    }

    private static Koto ParseTypeOrigin(ref TokenReader reader, Koto type, bool reportLegacyBorrow = true)
    {
        if (reader.CurrentTokenKind != TokenKind.OpenBrace)
        {
            return type;
        }

        var origin = ParseOriginBraces(ref reader);
        var annotated = type as TypeSemanticsKoto ?? new TypeSemanticsKoto(ref reader, type.Span, type);
        RecoverItem(ref reader, annotated, origin.Cause);
        if (type is ParenthesizedTypeKoto or TupleTypeKoto or FunctionTypeKoto or FixedArrayTypeKoto ||
            type is TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false })
        {
            reader.Diagnostic.Add(origin.Span, DiagnosticCode.OriginBindingSetTarget_Kd);
            RecoverItem(ref reader, annotated, reader.Diagnostic.LastError);
        }

        annotated.SetOrigin(origin.Expression, origin.Arguments, origin.Span.End);
        if (origin.InvalidContent || origin.Arguments is not null ||
            (origin.Expression is not ErrorKoto && origin.Expression is not IdentifierNameKoto { IdentifierName: not ("static" or "_") }))
        {
            reader.Diagnostic.Add(origin.Span, DiagnosticCode.OriginBindingSetName_Kd);
            RecoverItem(ref reader, annotated, reader.Diagnostic.LastError);
        }

        annotated.MarkBindingSet(reader.CurrentTokenKind == TokenKind.Slash);
        if (reportLegacyBorrow && annotated.IsLegacyBorrowCandidate)
        {
            reader.Diagnostic.Add(origin.Span, DiagnosticCode.LegacyBorrowOrigin_Kd);
            var cause = reader.Diagnostic.LastError;
            reader.Advance(); // The slash belongs to the malformed Type, not to its enclosing parameter list.
            var target = ParseType(ref reader, TypeContext.Type);
            return new ErrorKoto(ref reader, SourceSpan.FromBounds(type.Span.Start, Math.Max(origin.Span.End, target.Span.End))) { Cause = cause };
        }

        return annotated;
    }

    private static (Koto? Expression, OriginArgument[]? Arguments, SourceSpan Span, bool InvalidContent, DiagnosticKey? Cause) ParseOriginBraces(ref TokenReader reader)
    {
        var open = reader.Read();
        Koto? expression = null;
        // Named lists are short; grow the result array directly instead of a list plus a copy.
        OriginArgument[]? arguments = null;
        var argumentCount = 0;
        var invalidContent = false;
        var end = open.Span.End;
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseBrace or TokenKind.EndBlock))
        {
            if (reader.CurrentTokenKind == TokenKind.Underscore)
            {
                invalidContent = true;
                reader.Advance();
            }
            else if (reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.EqualsGreaterThan)
            {
                var name = reader.GetIdentifier(reader.Read());
                reader.Advance();

                if (arguments is null)
                {
                    arguments = new OriginArgument[2];
                }
                else if (argumentCount == arguments.Length)
                {
                    Array.Resize(ref arguments, argumentCount * 2);
                }

                arguments[argumentCount++] = new(name, ParseOriginExpression(ref reader));
            }
            else
            {
                invalidContent |= expression is not null || arguments is not null;
                expression = ParseOriginExpression(ref reader);
            }

            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }
        }

        var cause = reader.ExpectCloser(TokenKind.CloseBrace, out var close);
        end = Math.Max(end, close.End);

        if (arguments is not null && argumentCount != arguments.Length)
        {
            Array.Resize(ref arguments, argumentCount);
        }

        return (expression, arguments, SourceSpan.FromBounds(open.Span.Start, end), invalidContent, cause);
    }

    private static Koto ParseOriginExpression(ref TokenReader reader)
    {
        var left = ParseOriginAtom(ref reader);
        while (reader.CurrentTokenKind == TokenKind.And)
        {
            var op = reader.Read();
            var right = ParseOriginAtom(ref reader);
            left = new BinaryKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(op.Span.End, right.Span.End)), KotoKind.And, left, right);
        }

        return left;
    }

    private static Koto ParseOriginAtom(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
        {
            var open = reader.Read().Span;
            var inner = ParseOriginExpression(ref reader);
            var cause = reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);
            var grouped = new ParenthesizedKoto(ref reader, SourceSpan.FromBounds(open.Start, Math.Max(inner.Span.End, close.End)), inner);
            RecoverItem(ref reader, grouped, cause);
            return grouped;
        }

        if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
        {
            reader.Expect(SyntaxForm.Name);
            return reader.NewErrorKoto();
        }

        var token = reader.Read();
        if (!IdentifierNameKoto.TryCreate(ref reader, token, out var name))
        {
            return new ErrorKoto(ref reader, token.Span) { Cause = reader.Diagnostic.LastError };
        }

        Koto left = name;
        if (reader.TryConsume(TokenKind.Dot))
        {
            if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
            {
                RecoverItem(ref reader, left, reader.Expect(SyntaxForm.Name));
                return left;
            }

            var member = reader.Read();
            if (!IdentifierNameKoto.TryCreate(ref reader, member, out var right))
            {
                return new ErrorKoto(ref reader, member.Span) { Cause = reader.Diagnostic.LastError };
            }

            left = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, right.Span.End), left, right);
        }

        return left;
    }
}
