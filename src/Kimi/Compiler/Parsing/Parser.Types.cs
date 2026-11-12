// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The Type grammar (SPEC 3, Appendix F.5): a Type in the grammar context of its position, and the angle-bracket lists of Type arguments
// and parameters.
// Input: a reader at a Type and its TypeContext. Output: a TypeKoto, an ArrayLength where the context admits one, or an ErrorKoto where no
// Type was read.
// Invariants: every Type is read by ParseType, and its context alone decides which productions follow the head; where `<` could also
// compare values, it applies Type arguments only when adjacent to what it follows, with its matching `>` found by the tokenizer (SPEC
// 12.5.2).
// Prohibitions: Origin annotations belong to Parser.Origins, Names and fixed Array Types to Parser.Syntax, Container references and Type
// suffixes to Parser.ContainerReferences.
public static partial class Parser
{
    /// <summary>The grammar context of a Type (SPEC F.5, F.9.4): which productions its position admits around the head.</summary>
    internal enum TypeContext
    {
        /// <summary>A complete Type, a Function Type or an AnnotatedType, in a declaration slot.</summary>
        Type,

        /// <summary>A complete Type inside a delimiter, where an <c>and</c> continues a BorrowOrigin, never an enclosing requirement (SPEC F.6.2).</summary>
        Element,

        /// <summary>A GenericArgument: an ArrayLength, or else an Element.</summary>
        Argument,

        /// <summary>An AnnotatedType, which leaves a following <c>-&gt;</c> to its enclosing construct.</summary>
        AnnotatedType,

        /// <summary>An expression's grouped Type, such as a BoundContainerQualifier: Origin and suffix, no PathSuffix (the expression's).</summary>
        Qualifier,

        /// <summary>An adaptation's OpenTarget (SPEC F.9.4): <c>&lt;</c> takes Type arguments only adjacent; no OriginApplication or BorrowOrigin.</summary>
        Target,

        /// <summary>The SemanticsType after <c>Semantics /</c>, whose <c>?</c> and BorrowOrigin belong to the enclosing AnnotatedType.</summary>
        SemanticsOperand,

        /// <summary>A SemanticsOperand inside a Target.</summary>
        TargetOperand,

        /// <summary>The ContainerPath after <c>::</c>, whose Origin and <c>?</c> belong to the enclosing Type (SPEC 3.2.3, 3.3.2).</summary>
        RootPath,

        /// <summary>A RootPath inside a Target.</summary>
        TargetRootPath,
    }

    /// <summary>Parses a Type in the grammar context of its position (SPEC F.5: Type := FunctionType | AnnotatedType).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="context">The grammar context of the Type's position.</param>
    /// <returns>The parsed Type, an ArrayLength in an Argument, or an <see cref="ErrorKoto"/>.</returns>
    internal static Koto ParseType(ref TokenReader reader, TypeContext context)
    {
        if (context == TypeContext.Argument && (reader.CurrentTokenKind == TokenKind.NumericLiteral || IsLengthExpression(ref reader)))
        {
            return ParseArrayLength(ref reader);
        }

        if (context is TypeContext.Element or TypeContext.Argument)
        {
            var requirement = reader.ConstraintRequirement;
            reader.ConstraintRequirement = false;
            var element = ParseType(ref reader, TypeContext.Type);
            reader.ConstraintRequirement = requirement;
            return element;
        }

        var type = ParseAnnotatedType(ref reader, context, out var parameterList);
        if (context != TypeContext.Type || reader.CurrentTokenKind != TokenKind.MinusGreaterThan)
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
        var returnType = ParseType(ref reader, TypeContext.Type);
        var functionType = new FunctionTypeKoto(
            ref reader,
            SourceSpan.FromBounds(type.Span.Start, Math.Max(arrowRange.End, returnType.Span.End)),
            type,
            returnType);
        reader.Hover(functionType, arrowRange);
        return functionType;
    }

    // SPEC F.5: AnnotatedType := SemanticsType "?"* BorrowOrigin?, with the parts its context admits; parameterList reports a bare group,
    // which can be a Function Type's FunctionParameters.
    private static Koto ParseAnnotatedType(ref TokenReader reader, TypeContext context, out bool parameterList)
    {
        var start = reader.CurrentTokenRange.Start;
        var target = context is TypeContext.Target or TypeContext.TargetOperand or TypeContext.TargetRootPath;
        Koto type;
        parameterList = false;
        var grouped = reader.CurrentTokenKind == TokenKind.OpenParenthesis;
        if (grouped)
        {
            // SPEC F.5: a grouped head takes suffixes only as ContinuesGroupedType allows; an expression continues a Qualifier itself.
            var qualifier = context != TypeContext.Qualifier && IsBoundContainerQualifier(ref reader, out _);
            type = ParseBareParenthesizedType(ref reader);
            parameterList = true;
            if (context != TypeContext.Qualifier && ContinuesGroupedType(ref reader, qualifier))
            {
                type = ParseTypeSuffixes(ref reader, type, target);
                parameterList = false;
            }
        }
        else
        {
            type = ParseTypeHead(ref reader, target);
            if (type is not ErrorKoto)
            {
                type = ParseTypeSuffixes(ref reader, type, target);
            }
        }

        // After a group whose member Name failed, a complete Type still reads an Origin, '?' and BorrowOrigin onto the Error; every other
        // position ends at the Error. One recovery for every position is a Parsing decision of its own, outside R9-F2.
        if (type is ErrorKoto && !(grouped && context == TypeContext.Type))
        {
            return type;
        }

        if (type is not (TypeKoto or ErrorKoto))
        {
            type = new TypeSemanticsKoto(ref reader, SourceSpan.FromBounds(start, type.Span.End), type);
        }

        if (context is TypeContext.RootPath or TypeContext.TargetRootPath)
        {
            return type;
        }

        var annotated = ParseTypeOrigin(ref reader, type, reportLegacyBorrow: !target);
        // An Origin annotation or a preceding Container suffix both stop this
        // parenthesized list from acting as a Function Parameter List (SPEC 3.2).
        parameterList &= ReferenceEquals(annotated, type);
        if (context is TypeContext.SemanticsOperand or TypeContext.TargetOperand)
        {
            return annotated;
        }

        parameterList &= !(reader.CurrentTokenKind == TokenKind.Question || reader.IsCurrentIdentifier("during") || reader.IsCurrentIdentifier("from"));
        return ParseOptionalSuffix(ref reader, annotated, parseBorrowOrigin: !target);
    }

    // A Type's head (SPEC F.5): a root-qualified path, a fixed array, a Name, a primitive Type or Self, or a Semantics prefix and its
    // operand; a group is ParseAnnotatedType's. A Target's head reads the Target's inner rows.
    private static Koto ParseTypeHead(ref TokenReader reader, bool target)
    {
        if (reader.CurrentTokenKind == TokenKind.ColonColon)
        {
            // A root-qualified Type is the path after '::'; its Origin and '?' belong to the enclosing Type (SPEC 3.2.3, 3.3.2).
            var rootStart = reader.Read().Span.Start;
            var path = ParseAnnotatedType(ref reader, target ? TypeContext.TargetRootPath : TypeContext.RootPath, out _);
            return new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(rootStart, path.Span.End), KotoKind.RootName, "::", [path]);
        }

        if (reader.CurrentTokenKind == TokenKind.OpenBracket)
        {
            return ParseFixedArrayType(ref reader);
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
            if (!SemanticsMaskHelper.TryParse(semantics, out SemanticsKind semanticsKind))
            {
                if (semantics.SequenceEqual(Constants.MoveOperation) || semantics.SequenceEqual(Constants.CopyOperation))
                {
                    // @move and @copy are operations, never Semantics prefixes (SPEC §13.5.1); the Type rests on the Error.
                    cause = reader.Unexpected(SyntaxForm.OperationAsSemanticsPrefix, token.Span);
                }

                semanticsParameter = reader.GetIdentifier(token);
            }

            reader.Advance(); // The slash that selected this path.
            var type = ParseAnnotatedType(ref reader, target ? TypeContext.TargetOperand : TypeContext.SemanticsOperand, out _);
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
            reader.Hover(result, token.Span);
            return result;
        }

        return new TypeSemanticsKoto(ref reader, token);
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
            type = ParseType(ref reader, TypeContext.Argument);
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

    /// <summary>
    /// Parses a parenthesized Type or Tuple Type by itself: no Origin annotation, Function Type arrow, Container suffix or
    /// optional suffix follows it here, because the caller continues the enclosing form (an AnnotatedType, or a
    /// Contract-qualified projection such as <c>I.(LendingIterator).LentItem</c>, SPEC 8.5).
    /// </summary>
    private static Koto ParseBareParenthesizedType(ref TokenReader reader)
    {
        var openRange = reader.CurrentTokenRange;
        reader.Advance();

        var elements = default(TemporaryKotoList);
        Koto? firstElement = null;
        var lastEnd = openRange.End;
        var hasComma = false;
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseParenthesis or TokenKind.Separator or TokenKind.EndBlock))
        {
            var element = ParseType(ref reader, TypeContext.Element);
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
        if (elements.Count == 1 && !hasComma)
        {
            return new ParenthesizedTypeKoto(ref reader, range, firstElement!);
        }

        var tuple = new TupleTypeKoto(ref reader, range, elements.ToArray());
        reader.Hover(tuple, openRange);
        return tuple;
    }

    /// <summary>Parses a declaration's Type parameters at their <c>&lt;</c> (SPEC F.5 GenericParameters).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="allowLength">Whether the declaration takes length slots: a function does (SPEC 4.4), a Container does not.</param>
    /// <returns>The parameters read, or <see langword="null"/> when there is none; a failed parameter is reported and left out.</returns>
    private static List<TypeKoto>? ParseGenericParameters(ref TokenReader reader, bool allowLength = false)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();

        List<TypeKoto>? list = default;
        var skipped = false;
        while (reader.CanRead)
        {
            var itemStart = reader.Position;
            if (TryEndGenericList(ref reader, list is null && !skipped ? SyntaxForm.Name : null))
            {
                return list;
            }

            if (ParseGenericParameter(ref reader) is not { } parameter)
            {
                // The failed parameter is skipped within the list, which continues after its comma.
                reader.SkipListItem(typeArguments: true);
                reader.TryConsume(TokenKind.Comma);
                skipped = true;
            }
            else
            {
                if (!allowLength && parameter is LengthParameterKoto && parameter.Unexpected(SyntaxForm.LengthParameter) is { } cause)
                {
                    // The slot stays in the list as a recovery: Binding keeps it out of the Type's components and rests on the Error.
                    reader.CodeContext.RecordRecovery(parameter, cause);
                }

                (list ??= new(2)).Add(parameter);
                ContinueGenericList(ref reader);
            }

            if (reader.Position == itemStart)
            {
                break; // A token that neither starts nor ends a parameter, such as '!'.
            }
        }

        reader.Expect(SyntaxForm.CloseAngleBracket);
        return list;
    }

    /// <summary>Parses one Type parameter (SPEC F.5 GenericParameter): a Name, a pair <c>Name / Name</c> or a length slot <c>length Name</c>.</summary>
    /// <returns>The parameter, or <see langword="null"/> where its Name is missing; nothing is reported here, and the list skips the item.</returns>
    private static TypeKoto? ParseGenericParameter(ref TokenReader reader)
    {
        if (reader.IsCurrentIdentifier("length") && reader.PeekKind(1).IsIdentifierOrContextualKeyword())
        {
            var start = reader.Read().Span.Start;
            var slot = ParseName(ref reader);
            var slotName = (slot as IdentifierNameKoto)?.IdentifierName ?? string.Empty;
            return new LengthParameterKoto(ref reader, SourceSpan.FromBounds(start, slot.Span.End), slotName);
        }

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
            return null;
        }

        var parameter = new GenericParameterKoto(ref reader, SourceSpan.FromBounds(first.Start, reader.PreviousSyntaxEnd), name, semantics);
        reader.Hover(parameter, nameSpan);
        return parameter;
    }

    /// <summary>
    /// Parses a specialization's Type arguments at their <c>&lt;</c> (SPEC F.6.3 SpecializationDeclaration, F.5 TypeArguments) as the header's
    /// list: each GenericArgument a <see cref="TypeKoto"/>, an ArrayLength wrapped in one. A Type's own arguments are read by
    /// <see cref="ParseGenericsPostfix"/>, whose recovery differs.
    /// </summary>
    private static List<TypeKoto>? ParseSpecializationArguments(ref TokenReader reader)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();

        List<TypeKoto>? list = default;
        while (reader.CanRead)
        {
            var itemStart = reader.Position;
            if (TryEndGenericList(ref reader, list is null ? SyntaxForm.Type : null))
            {
                return list;
            }

            var argument = ParseType(ref reader, TypeContext.Argument);
            (list ??= new(2)).Add(argument as TypeKoto ?? new TypeSemanticsKoto(ref reader, argument.Span, argument));
            ContinueGenericList(ref reader);
            if (reader.Position == itemStart)
            {
                break; // A token that neither starts nor ends an argument, such as '!'.
            }
        }

        reader.Expect(SyntaxForm.CloseAngleBracket);
        return list;
    }

    /// <summary>
    /// Ends a declaration header's generic list where no item starts: at its closer, which is consumed, or without one at a token of
    /// the code after the list, where the missing closer is reported and nothing is consumed.
    /// </summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="missingItem">The item's form, reported at the closer of a list with no item read or skipped; otherwise <see langword="null"/>.</param>
    /// <returns>Whether the list ended.</returns>
    private static bool TryEndGenericList(ref TokenReader reader, SyntaxForm? missingItem)
    {
        if (IsTypeClose(reader.CurrentTokenKind))
        {
            if (missingItem is { } form)
            {
                reader.Expect(form);
            }

            reader.TryConsumeTypeClose(out _);
            return true;
        }

        var unclosed = reader.CurrentTokenKind switch
        {
            TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock => true, // The line or a body ends.
            TokenKind.Equals or TokenKind.EqualsGreaterThan or TokenKind.Colon => true, // The header goes on after the list.
            TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace => !reader.CurrentToken.ClosesNothing, // An enclosing grouping closes.
            _ => false,
        };
        if (unclosed)
        {
            // The list ends without its closer; the caller consumes nothing more of it.
            reader.Expect(SyntaxForm.CloseAngleBracket);
        }

        return unclosed;
    }

    /// <summary>
    /// Reads what follows an item of a declaration header's generic list: its comma, or nothing before the closer; anything else is
    /// reported and skipped with the next comma.
    /// </summary>
    private static void ContinueGenericList(ref TokenReader reader)
    {
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
    }

    private static bool IsTypeClose(TokenKind kind)
        => kind is TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan or TokenKind.GreaterThanEquals or TokenKind.GreaterThanGreaterThanEquals;
}
