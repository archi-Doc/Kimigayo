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
