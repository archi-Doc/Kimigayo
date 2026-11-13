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

    // SPEC F.5: whether the "." after a grouped Type continues it, ContainerPath := PathQualifier PathSuffix+: the group is a
    // PathQualifier, a BoundContainerQualifier (qualifier, which the scan decided before the group was read), a Unit or Tuple Type, or a
    // grouped Function Type, and either PathSuffix follows. No other group is a PathQualifier: `(S).Item` and `(S?).(C).Item` end the Type
    // at the ".", which the enclosing construct reports.
    private static bool ContinuesGroupedType(ref TokenReader reader, Koto group, bool qualifier)
        => reader.CurrentTokenKind == TokenKind.Dot && (qualifier || group is TupleTypeKoto or ParenthesizedTypeKoto { Type: FunctionTypeKoto });

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

    // SPEC F.5: a Type head's PathSuffixes, Type arguments and OriginApplication (NamedType := NamedReference OriginApplication?), the
    // one loop for every head; a grouped head enters it only where ContinuesGroupedType holds. A Semantics prefix's Type has read its
    // own suffixes, so a "." after it belongs to the enclosing construct.
    private static Koto ParseTypeSuffixes(ref TokenReader reader, Koto left, bool disambiguateGenerics)
    {
        while (reader.CanRead)
        {
            var tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Dot)
            {
                if (left is TypeSemanticsKoto { Type: not null })
                {
                    break;
                }

                var operatorRange = reader.CurrentTokenRange;
                reader.Advance();

                var accessor = ParseTypeMember(ref reader);
                if (accessor is ErrorKoto failed)
                {
                    // The member Name failed, so the qualified Type is the Error's recovery, as a failed head is.
                    return new ErrorKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, failed.Span.End))) { Cause = failed.Cause };
                }

                left = new MemberAccessKoto(
                    ref reader,
                    SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, accessor.Span.End)),
                    left,
                    accessor);
            }
            else if (tokenKind == TokenKind.LessThan)
            {
                // In a conversion, a following '<' may start a comparison.
                // Type declarations and nested type arguments have no such ambiguity.
                if (disambiguateGenerics && !OpensAdjacentTypeArguments(ref reader, left.Span))
                {
                    break;
                }

                left = ParseGenericsPostfix(ref reader, left);
            }
            else if (tokenKind == TokenKind.OpenParenthesis && !disambiguateGenerics &&
                left is TypeSemanticsKoto { Type: null } or MemberAccessKoto or GenericsKoto or SyntaxFormKoto { Akind: KotoKind.RootName })
            {
                left = ParseOriginApplication(ref reader, left);
            }
            else
            {
                break;
            }
        }

        return left;

        // After a '.', a member is a Name, whose Type arguments the loop reads, or a parenthesized Contract (SPEC 8.5); a fixed
        // array or a root name there is where the Name was expected. A '/' after the member is left to the enclosing construct.
        static Koto ParseTypeMember(ref TokenReader reader)
        {
            if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
            {
                return ParseBareParenthesizedType(ref reader);
            }

            if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
            {
                var cause = reader.Expect(SyntaxForm.Name);
                return new ErrorKoto(ref reader, reader.InsertionSpan) { Cause = cause };
            }

            return new TypeSemanticsKoto(ref reader, reader.Read());
        }
    }

    // SPEC F.5: NamedReference := ContainerPath OriginBindingSet?, the reference of a BaseClause and of a ContractReference (F.6.1,
    // F.6.2): a path from a TypeSegment or a root name, or a PathQualifier with its PathSuffixes, and at most a binding set; no Semantics
    // prefix, bare group, Tuple, fixed array, '?', BorrowOrigin, OriginApplication or Function Type. What is written there is read as
    // the Type it writes, so nothing of it is left to the enclosing construct; a Type that is no NamedReference is one record over it,
    // and the reference rests on the Error.
    private static Koto ParseNamedReference(ref TokenReader reader)
    {
        var type = ParseType(ref reader, TypeContext.Type);
        var path = type is TypeSemanticsKoto { IsTransparentWrapper: true, IsSlotBinding: false, Type: { } wrapped } ? wrapped : type;
        var head = ContainerPathHead(path);
        if (type is ErrorKoto || head is TypeSemanticsKoto { Type: null, IsSlotBinding: false } or SyntaxFormKoto { Akind: KotoKind.RootName } ||
            (!ReferenceEquals(head, path) && head is ParenthesizedTypeKoto or TupleTypeKoto or FixedArrayTypeKoto))
        {
            return type;
        }

        return new ErrorKoto(ref reader, type.Span) { Cause = type.Expected(SyntaxForm.NamedReference) };
    }

    // The head of a ContainerPath read as a Type (SPEC F.5): the node its PathSuffixes and Type arguments continue.
    private static Koto ContainerPathHead(Koto path)
    {
        while (path is MemberAccessKoto or GenericsKoto { Identifier: not null })
        {
            path = path is MemberAccessKoto member ? member.Left : ((GenericsKoto)path).Identifier!;
        }

        return path;
    }

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
