// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

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
                // A misspelled relation operator is the cause; preserve the right operand for recovery.
                // A missing operator before an Origin atom does not consume that atom.
                if (reader.CurrentTokenKind is TokenKind.Equals or TokenKind.ExclamationEquals or
                    TokenKind.LessThan or TokenKind.LessThanEquals or TokenKind.GreaterThan or TokenKind.GreaterThanEquals)
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
            // SPEC 3.3.6, 23.3.6.9: the annotation keyword is during; the repair candidate replaces the former spelling.
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
            target.SetBorrowOrigin(expression, span);
            if (target.SemanticsParameter is null && target.SemanticsKind is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.Raw)
            {
                reader.Diagnostic.Add(keyword.Span, DiagnosticCode.BorrowOriginSemantics_Kd);
                RecoverItem(ref reader, type, reader.Diagnostic.LastError);
            }
        }
        else if (type is TypeSemanticsKoto { IsNamedType: true, HasOrigin: false } named)
        {
            // SPEC 3.3.6, 15.3.1: after a named Type, during binds the only slot of a one-slot schema; Binding checks the schema.
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
}
