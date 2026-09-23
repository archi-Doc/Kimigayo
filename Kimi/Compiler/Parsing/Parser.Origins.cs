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
        if (!equality)
        {
            if (reader.IsCurrentIdentifier("outlives"))
            {
                reader.Advance();
            }
            else
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "expected == or outlives in Origin relation");
            }
        }

        var right = ParseOriginExpression(ref reader);
        var relation = new OriginRelationKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(left.Span.End, right.Span.End)), left, right, equality);
        reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
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
            else
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "expected declaration-attached Origin relation");
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.SkipCurrentBlock(false);
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
            reader.Diagnostic.Add(keyword.Span, DiagnosticCode.UnexpectedToken_Kd, "borrow annotations use 'during', not 'from'");
        }

        var expression = ParseOriginAtom(ref reader);
        if (reader.CurrentTokenKind == TokenKind.And && !reader.ConstraintRequirement)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "an Origin intersection requires parentheses: 'during (a and b)'");
        }

        if (type is not TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false } target)
        {
            reader.Diagnostic.Add(keyword.Span, DiagnosticCode.UnexpectedToken_Kd, "during requires the first explicit Semantics in the same Type; use 'ref/T? during a', not '(ref/T)? during a'");
        }
        else
        {
            target.SetBorrowOrigin(expression, SourceSpan.FromBounds(keyword.Span.Start, Math.Max(keyword.Span.End, expression.Span.End)));
            if (target.SemanticsParameter is null && target.SemanticsKind is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.Unsafe)
            {
                reader.Diagnostic.Add(keyword.Span, DiagnosticCode.UnexpectedToken_Kd, "during requires safe borrow Semantics");
            }
        }

        // Consume malformed repetitions locally without changing the first annotation.
        while (reader.IsCurrentIdentifier("during") || reader.CurrentTokenKind == TokenKind.Question)
        {
            var extra = reader.Read();
            var detail = extra.Kind == TokenKind.Question
                ? "optional '?' must precede 'during'; group the annotated Type for a later '?'"
                : "only one 'during' annotation is permitted in this Type";
            reader.Diagnostic.Add(extra.Span, DiagnosticCode.UnexpectedToken_Kd, detail);
            if (extra.Kind != TokenKind.Question)
            {
                _ = ParseOriginAtom(ref reader);
            }
        }
    }

    private static List<string>? RejectCallableOriginList(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.OpenBrace)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "callable Origin lists were removed; introduce names in borrow annotations");
            _ = ParseOriginParameters(ref reader);
        }

        return null;
    }
}
