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
    // one loop for every head, the requirement reader's head word included (an IdentifierNameKoto leaf); a grouped head enters it only
    // where ContinuesGroupedType holds. A Semantics prefix's Type has read its
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
                    // The member Name failed: the path read so far stays the written Type beside the rejected suffix (SPEC 23.1.2), so its
                    // head binds and keeps its own record, and every check over the Type rests on the record.
                    return RecoverRejectedPart(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, failed.Span.End)), left, failed.Cause);
                }

                left = new MemberAccessKoto(
                    ref reader,
                    SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, accessor.Span.End)),
                    left,
                    accessor);
                if (accessor is ParenthesizedTypeKoto && !ContinuesContractMember(ref reader, ref left, accessor))
                {
                    return left;
                }
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
                left is TypeSemanticsKoto { Type: null } or IdentifierNameKoto or MemberAccessKoto or GenericsKoto or SyntaxFormKoto { Akind: KotoKind.RootName })
            {
                // SPEC F.5, F.8: NamedType := NamedReference OriginApplication?: the OriginApplication is the NamedType's last part, so
                // no PathSuffix, Type arguments or second application follows it; what does is the enclosing construct's.
                return ParseOriginApplication(ref reader, left);
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
                return ParseContractGroup(ref reader);
            }

            if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
            {
                var cause = reader.Expect(SyntaxForm.Name);
                return new ErrorKoto(ref reader, reader.InsertionSpan) { Cause = cause };
            }

            return new TypeSemanticsKoto(ref reader, reader.Read());
        }
    }

    // SPEC F.6.1, F.6.2: InheritanceReference := NamedReference | ContainerPath BorrowOrigin, the reference of a BaseClause and of a
    // ContractParentList, with F.5's NamedReference := ContainerPath OriginBindingSet?: a path from a TypeSegment or a root name, or a
    // PathQualifier with its PathSuffixes, and at most a binding set or a BorrowOrigin (the single-slot binding, whose slot count
    // Binding judges, SPEC 15.3.1); no Semantics prefix, bare group, Tuple, fixed array, '?', OriginApplication or Function Type. What
    // is written there is read as the Type it writes, so nothing of it is left to the enclosing construct; a Type that is no
    // InheritanceReference is one record over it, a written Type outside its grammar.
    private static Koto ParseInheritanceReference(ref TokenReader reader)
    {
        var type = ParseType(ref reader, TypeContext.Type);
        if (IsRecoveredType(ref reader, type) || IsContainerPath(OriginLayerType(type)))
        {
            return type;
        }

        return RecoverWrittenType(ref reader, type, type.Expected(SyntaxForm.InheritanceReference));
    }

    // SPEC F.5 ContainerPath := "::"? TypeSegment PathSuffix* | PathQualifier PathSuffix+, read as a Type: its head is a path leaf, or a
    // PathQualifier (a group, a Tuple or Unit, a fixed array) that its PathSuffixes continue; it never ends at a grouped Contract, whose
    // PathSuffix carries its ". Name" (a path split before that Name, as a selector's final ". Name" can split it, is none).
    private static bool IsContainerPath(Koto path)
    {
        var head = ContainerPathHead(path);
        return path is not MemberAccessKoto { Right: ParenthesizedTypeKoto } &&
            (IsPathLeaf(head) || (!ReferenceEquals(head, path) && head is ParenthesizedTypeKoto or TupleTypeKoto or FixedArrayTypeKoto));
    }

    // SPEC F.5 PathSuffix := "." TypeSegment | "." "(" ContractReference ")" "." Name: a grouped Contract after "." is one suffix with its
    // ". Name", read by F.5's suffix loop (ParseTypeSuffixes) and, until G489 merges it into that loop, ParseConstraintSubject's. Without
    // the Name, the path read so far is a written Type outside its grammar: one record at the group, the path recovered.
    private static bool ContinuesContractMember(ref TokenReader reader, ref Koto path, Koto group)
    {
        if (reader.CurrentTokenKind == TokenKind.Dot && reader.PeekKind(1).IsIdentifierOrContextualKeyword())
        {
            return true;
        }

        path = RecoverWrittenType(ref reader, path, group.Expected(SyntaxForm.ContractMember));
        return false;
    }

    // The first TypeSegment of a ContainerPath (F.5) or a root name: a Name as the Type reader builds it (TypeSemanticsKoto, Type: null)
    // or as the requirement reader builds it (IdentifierNameKoto), the two leaves of one Name.
    private static bool IsPathLeaf(Koto head)
        => head is TypeSemanticsKoto { Type: null } or IdentifierNameKoto or SyntaxFormKoto { Akind: KotoKind.RootName };

    // The Type below the transparent layer that carries a path's Origin (a binding set or a BorrowOrigin), or the Type itself.
    private static Koto OriginLayerType(Koto type)
        => type is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } wrapped } ? wrapped : type;

    // SPEC F.5 NamedReference := ContainerPath OriginBindingSet?, read as a Type or by the requirement reader: a ContainerPath with at
    // most a binding set (a BorrowOrigin is an InheritanceReference's alternative, F.6.1, no NamedReference).
    private static bool IsNamedReference(Koto type)
        => type is not TypeSemanticsKoto { HasOrigin: true, BindingSetName: null } && IsContainerPath(OriginLayerType(type));

    // SPEC F.6.2 ContractReference := ContainerReference := NamedReference, the one rule of every position that writes one (the grouped
    // Contract, a ContractSelector's group, a ConditionalConformance's head): a reference that is no NamedReference is one record over
    // it, a written Type outside its grammar (RecoverWrittenType); a failed reference judges nothing.
    private static Koto RecoverContractReference(ref TokenReader reader, Koto reference)
        => IsRecoveredType(ref reader, reference) || IsNamedReference(reference)
            ? reference
            : RecoverWrittenType(ref reader, reference, reference.Expected(SyntaxForm.ContractReference));

    // SPEC F.6.2: "ContractSelector "." Name" of an AssociatedTypeName and an EffectSpecification, the one reader of both heads: a "(" is
    // the group reader below, "::" a root name, any other token the Type head, then F.5's one suffix loop. The selector is judged once
    // complete (RecoverContractSelector): the caller splits the final ". Name".
    private static Koto ParseContractSelectorPath(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
        {
            return ParseContractSelectorGroup(ref reader);
        }

        var head = reader.CurrentTokenKind == TokenKind.ColonColon ? ParseRootName(ref reader) : ParseTypeHead(ref reader, target: false);
        return ParseTypeSuffixes(ref reader, head, disambiguateGenerics: false);
    }

    // A ContractSelector's "(" (SPEC F.6.2 ContractSelector := ContainerPath | "(" ContractReference ")"): a PathQualifier that F.5's owner
    // (ContinuesGroupedType) continues reads its PathSuffixes in the one suffix loop; any other group is "(" ContractReference ")" and
    // only its one ". Name" follows, so a further suffix is the enclosing construct's.
    private static Koto ParseContractSelectorGroup(ref TokenReader reader)
    {
        var qualifier = IsBoundContainerQualifier(ref reader, out _);
        var group = ParseBareParenthesizedType(ref reader);
        if (ContinuesGroupedType(ref reader, group, qualifier))
        {
            return ParseTypeSuffixes(ref reader, group, disambiguateGenerics: false);
        }

        if (!reader.TryConsume(TokenKind.Dot))
        {
            return group;
        }

        var name = ParseName(ref reader);
        return new MemberAccessKoto(ref reader, SourceSpan.FromBounds(group.Span.Start, name.Span.End), group, name);
    }

    // SPEC F.6.2 ContractSelector := ContainerPath | "(" ContractReference ")", judged once complete, for both heads (a whitelist): a
    // ContainerPath (F.5; a path leaf, or a PathQualifier its PathSuffixes continued) is valid; a bare group is "(" ContractReference ")",
    // its one reference judged; anything else (a PathQualifier without its PathSuffix, an OriginApplication) is one record over it.
    private static void RecoverContractSelector(ref TokenReader reader, Koto selector)
    {
        if (IsRecoveredType(ref reader, selector) || IsContainerPath(selector))
        {
            return;
        }

        if (selector is ParenthesizedTypeKoto { Type: { } reference })
        {
            RecoverContractReference(ref reader, reference);
            return;
        }

        RecoverWrittenType(ref reader, selector, selector.Expected(SyntaxForm.ContractReference));
    }

    // SPEC F.6.2: "(" ContractReference ")", a grouped Contract, the one reader of every position that writes it (F.5's PathSuffix in a
    // Type, a requirement or a subject, F.9.1's PostfixSuffix and BoundContainerExpression, a ContractSelector's group): one reference,
    // judged by the ContractReference rule, and its closer, so an empty group or a list is a syntax Error there; the node Binding reads
    // (WrittenReference).
    private static Koto ParseContractGroup(ref TokenReader reader)
    {
        var open = reader.Read().Span.Start;
        var reference = RecoverContractReference(ref reader, ParseType(ref reader, TypeContext.Element));
        reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);
        return new ParenthesizedTypeKoto(ref reader, SourceSpan.FromBounds(open, Math.Max(reference.Span.End, close.End)), reference);
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
