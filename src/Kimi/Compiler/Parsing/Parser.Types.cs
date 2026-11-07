// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The Type grammar (SPEC 3, Appendix F.5): a Type's head and optional suffix, and the angle-bracket lists of Type arguments and parameters.
// Input: a reader at a Type; the caller's position as the entries' arguments. Output: a TypeKoto, or an ErrorKoto where no Type was read.
// Invariants: where `<` could also compare values, it applies Type arguments only when adjacent to what it follows, with its matching `>`
// found by the tokenizer (SPEC 12.5.2).
// Prohibitions: Origin annotations belong to Parser.Origins, Names and fixed Array Types to Parser.Syntax, Container references and Type
// suffixes to Parser.ContainerReferences.
public static partial class Parser
{
    /// <summary>Parses a type expression.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed type node.</returns>
    public static Koto ParseType(ref TokenReader reader)
        => ParseDeclarationType(ref reader);

    private static Koto ParseType(ref TokenReader reader, bool parseOrigin, bool disambiguateGenerics = false, bool optionalSuffix = true)
    {
        // SPEC F.5: a grouped head takes suffixes only as ContinuesGroupedType allows.
        var start = reader.CurrentTokenRange.Start;
        var grouped = reader.CurrentTokenKind == TokenKind.OpenParenthesis;
        var qualifier = grouped && IsBoundContainerQualifier(ref reader, out _);
        var left = ParseTypeInternal(ref reader, disambiguateGenerics);
        if (left is not ErrorKoto && (!grouped || ContinuesGroupedType(ref reader, qualifier)))
        {
            left = ParseTypeSuffixes(ref reader, left, disambiguateGenerics);
        }

        if (left is ErrorKoto)
        {
            return left;
        }

        if (left is not TypeKoto)
        {
            left = new TypeSemanticsKoto(ref reader, SourceSpan.FromBounds(start, left.Span.End), left);
        }

        if (parseOrigin)
        {
            left = ParseTypeOrigin(ref reader, left, reportLegacyBorrow: !disambiguateGenerics);
        }

        return optionalSuffix ? ParseOptionalSuffix(ref reader, left, parseBorrowOrigin: !disambiguateGenerics) : left;

        static Koto ParseTypeInternal(ref TokenReader reader, bool disambiguateGenerics)
        {
            if (reader.CurrentTokenKind == TokenKind.ColonColon)
            {
                return ParseRootName(ref reader, true, disambiguateGenerics);
            }

            if (reader.CurrentTokenKind == TokenKind.OpenBracket)
            {
                return ParseFixedArrayType(ref reader);
            }

            if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
            {
                return ParseBareParenthesizedType(ref reader);
            }

            var token = reader.CurrentToken;
            if (!(token.Kind.IsPrimitiveType() || token.Kind.IsIdentifierOrContextualKeyword() || token.Kind == TokenKind.Self))
            {
                // A token that starts no Type is where the Type was expected. A literal, '_' or a token the lexer rejected stands where
                // the Type was written and is consumed with the report; any other token is left to end the enclosing construct.
                var cause = reader.Expect(SyntaxForm.Type);
                var span = reader.InsertionSpan;
                if (token.Kind is TokenKind.NumericLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral or TokenKind.Underscore or TokenKind.Invalid && reader.CanRead)
                {
                    reader.Advance();
                }

                return new ErrorKoto(ref reader, span) { Cause = cause };
            }

            reader.Advance();

            if (token.Kind.IsIdentifierOrContextualKeyword() && reader.CurrentTokenKind == TokenKind.Slash)
            {// Attachment is chosen by the enclosing AnnotatedType, not this recursion.
                var semantics = reader.GetSpan(token);
                string? semanticsParameter = default;
                DiagnosticKey? cause = null;
                if (!CompilerHelper.TryParse(semantics, out var semanticsKind))
                {
                    if (semantics.SequenceEqual(Constants.MoveOperation) || semantics.SequenceEqual(Constants.CopyOperation))
                    {
                        // @move and @copy are operations, never Semantics prefixes (SPEC §13.5.1); the Type rests on the Error.
                        cause = reader.Unexpected(SyntaxForm.OperationAsSemanticsPrefix, token.Span);
                    }

                    semanticsParameter = reader.GetIdentifier(token);
                }

                reader.Advance(); // The slash that selected this path.
                var attribute = reader.PopAttribute();
                var type = ParseType(ref reader, parseOrigin: true, disambiguateGenerics: disambiguateGenerics, optionalSuffix: false);
                if (cause is { } operationCause)
                {
                    return new ErrorKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, type.Span.End)) { Cause = operationCause };
                }

                if (type is TypeSemanticsKoto { IsTransparentWrapper: true, Type: not null, OriginName: null, OriginExpression: null, OriginArguments: null } transparentType)
                {
                    type = transparentType.Type;
                }

                var result = new TypeSemanticsKoto(
                    ref reader,
                    SourceSpan.FromBounds(token.Span.Start, type.Span.End),
                    type,
                    semanticsKind,
                    semanticsParameter);
                result.SetAttributeChain(attribute);
                reader.Hover(result, token.Span);
                return result;
            }

            return new TypeSemanticsKoto(ref reader, token);
        }
    }

    private static Koto ParseOptionalSuffix(ref TokenReader reader, Koto type, bool parseBorrowOrigin = false)
    {
        var count = 0;
        var suffixPosition = reader.Position;
        var end = type.Span.End;
        while (reader.CurrentTokenKind == TokenKind.Question)
        {
            end = reader.Read().Span.End;
            count++;
        }

        if (parseBorrowOrigin)
        {
            ParseBorrowOriginSuffix(ref reader, type);
        }

        // Build Optional only after attaching the suffix to the explicit head. All
        // parent ranges include the annotation; no wrapper search or tree copy is needed.
        end = Math.Max(end, type.Span.End);
        for (var i = 0; i < count; i++)
        {
            type = new OptionalTypeKoto(ref reader, SourceSpan.FromBounds(type.Span.Start, end), type);
            reader.Hover(type, reader.TokenSpanAt(suffixPosition + i));
        }

        return type;
    }

    /// <summary>Parses <c>&lt;T1, T2&gt;</c> after an identifier and wraps the identifier in a generic node.</summary>
    private static GenericsKoto ParseGenericsPostfix(ref TokenReader reader, Koto left)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();
        var typeList = default(TemporaryKotoList);
        var end = reader.CurrentTokenRange.End;
        Koto type;
        while (true)
        {
            // A line boundary ends an unrecognized list: the tokenizer joins the lines of a recognized one (SPEC F.4.1).
            type = ParseTypeArgument(ref reader);
            typeList.Add(type);
            end = type.Span.End;
            if (!reader.TryConsume(TokenKind.Comma) || IsTypeClose(reader.CurrentTokenKind))
            {
                break;
            }
        }

        // The closer is checked only after a complete last argument; a failed argument explains an absent closer (DIAGNOSTICS.md §4.3).
        if (reader.TryConsumeTypeClose(out var range, report: type is not ErrorKoto))
        {
            end = Math.Max(end, range.End);
        }
        else
        {
            // The argument before what the closer's report blamed is a recovery, as a list item is (DIAGNOSTICS.md §4.4).
            RecoverItem(ref reader, type, reader.Diagnostic.LastError);
        }

        return new GenericsKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(left.Span.End, end)), left, typeList.ToArray());
    }

    /// <summary>
    /// Determines whether the current <c>&lt;</c> applies Type arguments to the syntax that ends at <paramref name="targetSpan"/>: it is
    /// adjacent to it, and the tokenizer found its matching <c>&gt;</c> (SPEC 12.5.2, <see cref="Token.OpensTypeArguments"/>). Otherwise
    /// it compares values, so spaces around a comparison avoid the ambiguity.
    /// </summary>
    private static bool OpensAdjacentTypeArguments(ref TokenReader reader, SourceSpan targetSpan)
        => reader.CurrentToken.OpensTypeArguments && reader.CurrentTokenRange.Start == targetSpan.End;

    private static Koto ParseDeclarationType(ref TokenReader reader, bool parseOrigin = true, bool parseFunctionType = true, bool parseContainerSuffix = true, bool parseSuffix = true)
    {
        Koto type;
        var parameterList = false;
        if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
        {
            // SPEC F.5: only a BoundContainerQualifier takes a PathSuffix .Name.
            var qualifier = parseContainerSuffix && IsBoundContainerQualifier(ref reader, out _);
            var openRange = reader.CurrentTokenRange;
            reader.Advance();

            var elements = default(TemporaryKotoList);
            Koto? firstElement = null;
            var lastEnd = openRange.End;
            var hasComma = false;
            while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseParenthesis or TokenKind.Separator or TokenKind.EndBlock))
            {
                var element = ParseDelimitedType(ref reader);
                firstElement ??= element;
                lastEnd = element.Span.End;
                elements.Add(element);
                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    hasComma = true;
                    reader.Advance();
                }
                else if (reader.CurrentTokenKind != TokenKind.CloseParenthesis)
                {
                    reader.Expect(SyntaxForm.Comma);
                    reader.SkipListItem();
                    if (!reader.TryConsume(TokenKind.Comma))
                    {
                        break;
                    }
                }
            }

            reader.ExpectCloser(TokenKind.CloseParenthesis, out var closeRange);
            var end = Math.Max(Math.Max(openRange.End, lastEnd), closeRange.End);

            var range = SourceSpan.FromBounds(openRange.Start, end);
            type = elements.Count == 1 && !hasComma
                ? new ParenthesizedTypeKoto(ref reader, range, firstElement!)
                : new TupleTypeKoto(ref reader, range, elements.ToArray());
            if (type is TupleTypeKoto)
            {
                reader.Hover(type, openRange);
            }

            parameterList = true;
            if (parseContainerSuffix && ContinuesGroupedType(ref reader, qualifier))
            {
                type = ParseTypeSuffixes(ref reader, type, disambiguateGenerics: false);
                parameterList = false;
            }

            if (parseOrigin)
            {
                var annotated = ParseTypeOrigin(ref reader, type);
                // An Origin annotation or a preceding Container suffix both stop this
                // parenthesized list from acting as a Function Parameter List (SPEC 3.2).
                parameterList &= ReferenceEquals(annotated, type);
                type = annotated;
            }

            if (parseSuffix)
            {
                var hasSuffix = reader.CurrentTokenKind == TokenKind.Question || reader.IsCurrentIdentifier("during") || reader.IsCurrentIdentifier("from");
                type = ParseOptionalSuffix(ref reader, type, parseBorrowOrigin: true);
                parameterList &= !hasSuffix;
            }
        }
        else
        {
            type = ParseType(ref reader, parseOrigin);
        }

        if (!parseFunctionType || reader.CurrentTokenKind != TokenKind.MinusGreaterThan)
        {
            return type;
        }

        var arrowRange = reader.CurrentTokenRange;
        if (!parameterList)
        {
            // A bare or Semantics-applied Type cannot replace the Function Parameter List: ref/(T) -> U is invalid (SPEC 3.2).
            type.Expected(SyntaxForm.FunctionParameterList);
        }

        reader.Advance();
        var returnType = ParseDeclarationType(ref reader);
        var functionType = new FunctionTypeKoto(
            ref reader,
            SourceSpan.FromBounds(type.Span.Start, Math.Max(arrowRange.End, returnType.Span.End)),
            type,
            returnType);
        reader.Hover(functionType, arrowRange);
        return functionType;
    }

    /// <summary>
    /// Parses a parenthesized Type or Tuple Type by itself: no Origin annotation, Function Type arrow, Container suffix or
    /// optional suffix follows it here, because the caller continues the enclosing form (a Semantics prefix, or a
    /// Contract-qualified projection such as <c>I.(LendingIterator).LentItem</c>, SPEC 8.5).
    /// </summary>
    private static Koto ParseBareParenthesizedType(ref TokenReader reader)
        => ParseDeclarationType(ref reader, parseOrigin: false, parseFunctionType: false, parseContainerSuffix: false, parseSuffix: false);

    private static List<TypeKoto>? ParseGenericArguments(ref TokenReader reader, bool allowLength = false, bool specialization = false)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();

        List<TypeKoto>? list = default;
        var skipped = false;
        while (reader.CanRead)
        {
            var itemStart = reader.Position;
            if (IsTypeClose(reader.CurrentTokenKind))
            {
                if (list is null && !skipped)
                {
                    reader.Expect(specialization ? SyntaxForm.Type : SyntaxForm.Name);
                }

                reader.TryConsumeTypeClose(out _);
                return list;
            }

            if (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock or TokenKind.Equals or TokenKind.EqualsGreaterThan or TokenKind.Colon ||
                (reader.CurrentTokenKind is TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace && !reader.CurrentToken.ClosesNothing))
            {
                // The list ends without its closer; the caller consumes nothing more of it.
                reader.Expect(SyntaxForm.CloseAngleBracket);
                return list;
            }

            TypeKoto typeKoto;
            if (reader.IsCurrentIdentifier("length") && !specialization && reader.PeekKind(1).IsIdentifierOrContextualKeyword())
            {
                var start = reader.Read().Span.Start;
                var name = ParseName(ref reader);
                typeKoto = new LengthParameterKoto(ref reader, SourceSpan.FromBounds(start, name.Span.End), (name as IdentifierNameKoto)?.IdentifierName ?? string.Empty);
                if (!allowLength && typeKoto.Unexpected(SyntaxForm.LengthParameter) is { } cause)
                {
                    // The slot stays in the list as a recovery: Binding keeps it out of the Type's components and rests on the Error.
                    reader.CodeContext.RecordRecovery(typeKoto, cause);
                }
            }
            else if (specialization)
            {
                var argument = ParseTypeArgument(ref reader);
                typeKoto = argument as TypeKoto ?? new TypeSemanticsKoto(ref reader, argument.Span, argument);
            }
            else
            {
                string? semantics = null;
                var nameSpan = reader.CurrentTokenRange;
                if (reader.TryReadName(out var name, out var first) && reader.TryConsume(TokenKind.Slash))
                {
                    semantics = name;
                    nameSpan = reader.CurrentTokenRange;
                    name = reader.TryReadName(out var semanticsName, out _) ? semanticsName : null;
                }

                if (name is null)
                {
                    // The failed parameter is skipped within the list, which continues after its comma.
                    reader.SkipListItem(typeArguments: true);
                    reader.TryConsume(TokenKind.Comma);
                    skipped = true;
                    goto Next;
                }

                typeKoto = new GenericParameterKoto(ref reader, SourceSpan.FromBounds(first.Start, reader.PreviousSyntaxEnd), name, semantics);
                reader.Hover(typeKoto, nameSpan);
            }

            (list ??= new(2)).Add(typeKoto);

            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                reader.Advance();
            }
            else if (!IsTypeClose(reader.CurrentTokenKind))
            {
                reader.Expect(SyntaxForm.Comma);
                reader.SkipListItem(typeArguments: true);
                reader.TryConsume(TokenKind.Comma);
            }

Next:
            if (reader.Position == itemStart)
            {
                break; // A token that neither starts nor ends a parameter, such as '!'.
            }
        }

        reader.Expect(SyntaxForm.CloseAngleBracket);
        return list;
    }

    private static bool IsTypeClose(TokenKind kind)
        => kind is TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan or TokenKind.GreaterThanEquals or TokenKind.GreaterThanGreaterThanEquals;
}
