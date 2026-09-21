// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

public static partial class Parser
{
    private static bool IsBoundContainerQualifier(ref TokenReader reader)
    {
        var depth = 0;
        var genericDepth = 0;
        for (var i = 0; ; i++)
        {
            var kind = reader.PeekKind(i);
            if (kind is TokenKind.Invalid or TokenKind.EndBlock or TokenKind.Separator)
            {
                return false;
            }

            depth += kind == TokenKind.OpenParenthesis ? 1 : kind == TokenKind.CloseParenthesis ? -1 : 0;
            if (depth == 0)
            {
                return false;
            }

            genericDepth += kind == TokenKind.LessThan ? 1 : kind == TokenKind.GreaterThan ? -1 : kind == TokenKind.GreaterThanGreaterThan ? -2 : 0;
            if (depth == 1 && genericDepth == 0 && i > 0)
            {
                if (i > 1 && kind == TokenKind.OpenBrace)
                {
                    return true;
                }

                if (!(kind.IsIdentifierOrContextualKeyword() || kind is TokenKind.ColonColon or TokenKind.Dot or TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan))
                {
                    return false;
                }
            }
        }
    }

    private static Koto ParseGroupedContainerSuffix(ref TokenReader reader, Koto type, bool allowOrigins)
    {
        while (reader.CurrentTokenKind == TokenKind.Dot)
        {
            reader.Advance();
            var member = reader.CurrentTokenKind == TokenKind.OpenParenthesis
                ? ParseDeclarationType(ref reader, parseOrigin: true, parseFunctionType: false, allowNestedOrigins: allowOrigins, parseContainerSuffix: false)
                : ParseMemberName(ref reader);
            type = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(type.Span.Start, member.Span.End), type, member);
            if (reader.CurrentTokenKind == TokenKind.LessThan)
            {
                type = ParseGenericsPostfix(ref reader, type, allowOrigins);
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
