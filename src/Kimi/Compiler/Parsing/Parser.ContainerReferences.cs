// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

public static partial class Parser
{
    // SPEC F.5: whether the current "(" begins a BoundContainerQualifier, "(" ContainerPath OriginBindingSet ")" or
    // "(" ContainerPath BorrowOrigin ")", followed by the "." of its PathSuffix. This one decision covers the whole qualifier for every
    // position that reads one: a Type takes either form, and an expression only the binding set (borrowOrigin false). A grouped step of
    // the path (a nested qualifier, or the "(C)" of a ".(C)." selector) is checked by its own parse. "from" is the misspelled "during"
    // that ParseTypeOrigin repairs.
    private static bool IsBoundContainerQualifier(ref TokenReader reader, out bool borrowOrigin)
    {
        borrowOrigin = false;
        var i = reader.PeekKind(1) == TokenKind.ColonColon ? 2 : 1;
        while (true)
        {
            var kind = reader.PeekKind(i);
            if (kind == TokenKind.OpenParenthesis)
            {
                if ((i = SkipBalanced(ref reader, i, TokenKind.OpenParenthesis, TokenKind.CloseParenthesis)) < 0 || reader.PeekKind(i++) != TokenKind.Dot)
                {
                    return false;
                }

                continue;
            }

            if (!(kind.IsIdentifierOrContextualKeyword() || kind.IsPrimitiveType() || kind == TokenKind.Self))
            {
                return false;
            }

            if (reader.PeekKind(++i) == TokenKind.LessThan && (i = SkipBalanced(ref reader, i, TokenKind.LessThan, TokenKind.GreaterThan)) < 0)
            {
                return false;
            }

            if (reader.PeekKind(i) == TokenKind.Dot)
            {
                i++;
                continue;
            }

            if (reader.PeekKind(i) == TokenKind.OpenBrace && reader.PeekKind(i + 1).IsIdentifierOrContextualKeyword())
            {
                i += reader.PeekKind(i + 2) == TokenKind.Comma ? 3 : 2;
                if (reader.PeekKind(i++) != TokenKind.CloseBrace)
                {
                    return false;
                }
            }
            else if (reader.IsIdentifierToken(reader.PeekToken(i), "during") || reader.IsIdentifierToken(reader.PeekToken(i), "from"))
            {
                // OriginAtom := Name ("." Name)? | "static" | "(" OriginExpression ")".
                borrowOrigin = true;
                if (reader.PeekKind(++i) == TokenKind.OpenParenthesis)
                {
                    if ((i = SkipBalanced(ref reader, i, TokenKind.OpenParenthesis, TokenKind.CloseParenthesis)) < 0)
                    {
                        return false;
                    }
                }
                else if (!reader.PeekKind(i++).IsIdentifierOrContextualKeyword())
                {
                    return false;
                }
                else if (reader.PeekKind(i) == TokenKind.Dot && reader.PeekKind(i + 1).IsIdentifierOrContextualKeyword())
                {
                    i += 2;
                }
            }
            else
            {
                return false;
            }

            return reader.PeekKind(i) == TokenKind.CloseParenthesis && reader.PeekKind(i + 1) == TokenKind.Dot;
        }
    }

    // The offset after the group that opens at the given offset, or -1 where the line or the source ends first; ">>" closes two
    // Type-argument levels.
    private static int SkipBalanced(ref TokenReader reader, int offset, TokenKind open, TokenKind close)
    {
        for (var depth = 0; ; offset++)
        {
            var kind = reader.PeekKind(offset);
            if (kind is TokenKind.Invalid or TokenKind.EndBlock or TokenKind.StartBlock or TokenKind.Separator)
            {
                return -1;
            }

            depth += kind == open ? 1 : kind == close ? -1 : close == TokenKind.GreaterThan && kind == TokenKind.GreaterThanGreaterThan ? -2 : 0;
            if (depth <= 0)
            {
                return offset + 1;
            }
        }
    }

    private static Koto ParseGroupedContainerSuffix(ref TokenReader reader, Koto type)
    {
        while (reader.CurrentTokenKind == TokenKind.Dot)
        {
            reader.Advance();
            var member = reader.CurrentTokenKind == TokenKind.OpenParenthesis
                ? ParseDeclarationType(ref reader, parseOrigin: true, parseFunctionType: false, parseContainerSuffix: false)
                : ParseMemberName(ref reader);
            type = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(type.Span.Start, member.Span.End), type, member);
            if (reader.CurrentTokenKind == TokenKind.LessThan)
            {
                type = ParseGenericsPostfix(ref reader, type);
            }
        }

        return type;
    }

    internal static Koto ParseContainerReference(ref TokenReader reader)
        => ParseType(ref reader, parseOrigin: true);

    internal static bool IsBoundContainerReference(ref TokenReader reader)
    {
        for (var i = 0; ; i++)
        {
            var kind = reader.PeekKind(i);
            if (kind is TokenKind.Separator or TokenKind.EndBlock or TokenKind.StartBlock or TokenKind.Invalid)
            {
                return false;
            }

            if (kind is TokenKind.LessThan or TokenKind.OpenParenthesis or TokenKind.OpenBrace)
            {
                return true;
            }
        }
    }
}
