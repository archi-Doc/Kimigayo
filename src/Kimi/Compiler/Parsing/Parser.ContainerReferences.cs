// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

public static partial class Parser
{
    // SPEC F.5: whether the current "(" begins a BoundContainerQualifier, "(" ContainerPath OriginBindingSet ")" or
    // "(" ContainerPath BorrowOrigin ")", followed by the "." of its PathSuffix. This one decision covers the whole qualifier, a nested
    // qualifier included, for every position that reads one: a Type takes either form, and an expression only binding sets
    // (borrowOrigin false, also in a nested qualifier). The scan never passes the qualifier's closing ")" but to read the ".".
    // "from" is the misspelled "during" that ParseTypeOrigin repairs.
    private static bool IsBoundContainerQualifier(ref TokenReader reader, out bool borrowOrigin)
    {
        borrowOrigin = false;
        return QualifierDot(ref reader, 0, ref borrowOrigin) > 0;
    }

    // SPEC F.5 and 8.5.2: whether the "." after a grouped Type continues it: any PathSuffix after a BoundContainerQualifier, and only a
    // Contract-qualified projection .(C).Name after another group.
    private static bool ContinuesGroupedType(ref TokenReader reader, bool qualifier)
        => reader.CurrentTokenKind == TokenKind.Dot && (qualifier || reader.PeekKind() == TokenKind.OpenParenthesis);

    // The offset of the PathSuffix "." after the qualifier whose "(" is at the offset, or -1.
    private static int QualifierDot(ref TokenReader reader, int offset, ref bool borrowOrigin)
    {
        // ContainerPath := "::"? TypeSegment PathSuffix* | BoundContainerQualifier PathSuffix+.
        var i = offset + 1;
        if (reader.PeekKind(i) == TokenKind.OpenParenthesis)
        {
            i = QualifierDot(ref reader, i, ref borrowOrigin);
        }
        else
        {
            i = SegmentEnd(ref reader, reader.PeekKind(i) == TokenKind.ColonColon ? i + 1 : i);
        }

        // PathSuffix := "." TypeSegment | "." "(" ContractReference ")" "." Name.
        while (i > 0 && reader.PeekKind(i) == TokenKind.Dot)
        {
            if (reader.PeekKind(++i) != TokenKind.OpenParenthesis)
            {
                i = SegmentEnd(ref reader, i);
            }
            else if ((i = SkipBalanced(ref reader, i, TokenKind.OpenParenthesis, TokenKind.CloseParenthesis)) > 0)
            {
                i = reader.PeekKind(i) == TokenKind.Dot && reader.PeekKind(i + 1).IsIdentifierOrContextualKeyword() ? i + 2 : -1;
            }
        }

        if (i < 0)
        {
            return -1;
        }

        if (reader.PeekKind(i) == TokenKind.OpenBrace && reader.PeekKind(i + 1).IsIdentifierOrContextualKeyword())
        {
            // OriginBindingSet := "{" Name ","? "}".
            i += reader.PeekKind(i + 2) == TokenKind.Comma ? 3 : 2;
            if (reader.PeekKind(i++) != TokenKind.CloseBrace)
            {
                return -1;
            }
        }
        else if (reader.IsIdentifierToken(reader.PeekToken(i), "during") || reader.IsIdentifierToken(reader.PeekToken(i), "from"))
        {
            // BorrowOrigin := "during" OriginAtom; OriginAtom := Name ("." Name)? | "static" | "(" OriginExpression ")".
            borrowOrigin = true;
            if (reader.PeekKind(++i) == TokenKind.OpenParenthesis)
            {
                i = SkipBalanced(ref reader, i, TokenKind.OpenParenthesis, TokenKind.CloseParenthesis);
            }
            else if (!reader.PeekKind(i++).IsIdentifierOrContextualKeyword())
            {
                return -1;
            }
            else if (reader.PeekKind(i) == TokenKind.Dot && reader.PeekKind(i + 1).IsIdentifierOrContextualKeyword())
            {
                i += 2;
            }
        }
        else
        {
            return -1;
        }

        return i > 0 && reader.PeekKind(i) == TokenKind.CloseParenthesis && reader.PeekKind(i + 1) == TokenKind.Dot ? i + 1 : -1;
    }

    // TypeSegment := TypeName TypeArguments?: the offset after it, or -1.
    private static int SegmentEnd(ref TokenReader reader, int offset)
    {
        var kind = reader.PeekKind(offset);
        if (!(kind.IsIdentifierOrContextualKeyword() || kind.IsPrimitiveType() || kind == TokenKind.Self))
        {
            return -1;
        }

        return reader.PeekKind(offset + 1) == TokenKind.LessThan ? SkipBalanced(ref reader, offset + 1, TokenKind.LessThan, TokenKind.GreaterThan) : offset + 1;
    }

    // The offset after the group that opens at the offset, or -1 where an unmatched ")" (the qualifier's own closer) or the end of the
    // line comes first; ">>" closes two Type-argument levels.
    private static int SkipBalanced(ref TokenReader reader, int offset, TokenKind open, TokenKind close)
    {
        for (var (depth, parentheses) = (0, 0); ; offset++)
        {
            var kind = reader.PeekKind(offset);
            parentheses += kind == TokenKind.OpenParenthesis ? 1 : kind == TokenKind.CloseParenthesis ? -1 : 0;
            if (kind is TokenKind.Invalid or TokenKind.EndBlock or TokenKind.StartBlock or TokenKind.Separator || parentheses < 0)
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
