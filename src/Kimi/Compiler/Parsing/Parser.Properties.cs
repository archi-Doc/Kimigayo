// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// A Property and its accessors (SPEC 11, Appendix F.7): the declaration, the inline accessor list, the accessor block, and one accessor's
// header, parameters, result and body.
// Input: a reader after the Property keyword, with the prefix pending. Output: a PropertyKoto with its accessors, or null after an error.
// Invariants: after an accessor header without its get or set keyword the accessor set is unknown, so its accessor checks rest on the Error;
// an access modifier on an accessor that takes none is the one Error, with the accessor and its Property kept as recoveries.
public static partial class Parser
{
    /// <summary>Parses a Property declaration and its optional accessor list.</summary>
    /// <param name="reader">The token reader positioned after the declaration keyword.</param>
    /// <param name="token">The Property declaration keyword token.</param>
    /// <returns>The parsed Property, or <see langword="null"/> after an error.</returns>
    public static PropertyKoto? ParseProperty(ref TokenReader reader, ref Token token)
    {
        var propertyContext = reader.TakeContext();
        if (ParseDeclarationName(ref reader) is not { } nameKoto)
        {
            return null;
        }

        var computed = token.Kind is TokenKind.Computed or TokenKind.Property;
        Koto? typeKoto = default;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {
            reader.ReportMisplacedAttributes();
            typeKoto = ParseType(ref reader, TypeContext.Type);
        }
        else if (computed)
        {
            // A computed Property or requirement states its Type; the recovery Type stands for the missing annotation (SPEC 23.1.2).
            typeKoto = new ErrorKoto(ref reader, new SourceSpan(nameKoto.Span.End, 0)) { Cause = reader.Expect(SyntaxForm.TypeAnnotation) };
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out var equalsSpan, false))
        {
            initializerKoto = ParseVariableInitializer(ref reader);
            if (computed)
            {
                // The value of a computed Property comes from get; the written initializer is discarded.
                reader.Unexpected(SyntaxForm.ComputedInitializer, SourceSpan.FromBounds(equalsSpan.Start, initializerKoto.Span.End));
                initializerKoto = null;
            }
        }

        if (!computed && typeKoto is null && initializerKoto is null)
        {
            // A stored Property states its Type or has an initializer; the recovery Type stands for the missing one (SPEC 23.1.2).
            typeKoto = new ErrorKoto(ref reader, new SourceSpan(nameKoto.Span.End, 0)) { Cause = reader.Expect(SyntaxForm.TypeOrInitializer) };
        }

        var hasInlineAccessors = reader.TryConsume(TokenKind.Has, out var hasSpan, false);

        reader.RestoreContext(propertyContext);
        var property = new PropertyKoto(ref reader, token, nameKoto, typeKoto, initializerKoto, hasInlineAccessors);
        reader.Document(property, SourceSpan.FromBounds(token.Span.Start, nameKoto.Span.End), propertyContext.AttributeKoto);
        reader.Hover(property, nameKoto.Span);

        var unavailableAccessor = false;
        if (hasInlineAccessors)
        {
            if (!property.IsContractRequirement)
            {
                reader.Unexpected(SyntaxForm.InlineAccessors, hasSpan);
            }

            unavailableAccessor = ParseInlinePropertyAccessors(ref reader, property);
        }

        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            if (hasInlineAccessors)
            {
                reader.Unexpected(SyntaxForm.IndentedBody);
                reader.SkipCurrentBlock();
            }
            else
            {
                unavailableAccessor = ParsePropertyAccessorBlock(ref reader, property);
            }
        }
        else
        {
            reader.ExpectLineEnd();
        }

        if (unavailableAccessor)
        {
            return null;
        }

        if (property.DeclarationKind is PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement &&
            property.GetAccessor(PropertyAccessorKind.Get) is null && reader.CodeContext.RecoveryCause(property) is null)
        {
            reader.CodeContext.RecordRecovery(property, reader.Expect(SyntaxForm.Getter));
        }

        return property;
    }

    private static bool ParseInlinePropertyAccessors(ref TokenReader reader, PropertyKoto property)
    {
        var parsedAny = false;
        var unavailableAccessor = false;
        while (reader.CanRead)
        {
            if (TryConsumeUnavailableModifiers(ref reader, out _, accessor: true))
            {
                unavailableAccessor = true;
                parsedAny = true;
                reader.SkipUntil(TokenKind.Comma, TokenKind.Separator, TokenKind.EndBlock);
                if (reader.TryConsume(TokenKind.Comma))
                {
                    continue;
                }

                break;
            }

            parsedAny = true;
            if (!TryParseAccessorHeader(ref reader, property, out var header))
            {
                reader.SkipUntil(TokenKind.Comma, TokenKind.Separator, TokenKind.EndBlock);
                break;
            }

            var accessor = new PropertyAccessorKoto(
                ref reader,
                SourceSpan.FromBounds(header.Start, header.Keyword.Span.End),
                header.Modifier,
                header.Kind,
                default);
            if (header.Modifier != ModifierKind.NoModifier)
            {
                RejectAccessorModifier(ref reader, property, accessor, header);
            }

            AddPropertyAccessor(ref reader, property, accessor, header.Keyword);

            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }

            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.EndBlock)
            {
                reader.Expect(SyntaxForm.Accessor);
                break;
            }
        }

        if (!parsedAny)
        {
            reader.Expect(SyntaxForm.Accessor);
        }

        return unavailableAccessor;
    }

    private static bool ParsePropertyAccessorBlock(ref TokenReader reader, PropertyKoto property)
    {
        var unavailableAccessor = false;
        var seenAccessor = false;
        var blockStart = reader.CurrentTokenRange;
        reader.Advance();
        while (reader.CanRead)
        {
            reader.SkipSeparators();

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                var blockEnd = reader.PreviousSyntaxEnd;
                reader.Advance();
                property.CompleteSpan(blockEnd);
                return unavailableAccessor;
            }

            if (IsOriginRelationStart(ref reader))
            {
                var relation = ParseOriginRelation(ref reader);
                if (seenAccessor)
                {
                    // The misplaced clause is not a relation of the Property; Binding judges only the clauses kept.
                    relation.Unexpected(SyntaxForm.OriginClause);
                }
                else
                {
                    OriginClauses.Add(property, relation);
                }

                continue;
            }

            seenAccessor = true;
            if (TryConsumeUnavailableModifiers(ref reader, out _, accessor: true))
            {
                unavailableAccessor = true;
                SkipItemForRecovery(ref reader);
                continue;
            }

            if (!TryParseAccessorHeader(ref reader, property, out var header))
            {
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                continue;
            }

            ParseAccessor(ref reader, property, header);
        }

        property.CompleteSpan(Math.Max(property.Span.End, blockStart.End));
        reader.Expect(SyntaxForm.Accessor);
        return unavailableAccessor;
    }

    // One accessor of an indented accessor list after its header (SPEC F.7): its signature, its body and the line's end.
    private static void ParseAccessor(ref TokenReader reader, PropertyKoto property, in AccessorHeader header)
    {
        var accessorToken = header.Keyword;
        var accessorKind = header.Kind;
        RejectCallableOriginList(ref reader);
        var hasSignature = reader.CurrentTokenKind == TokenKind.OpenParenthesis;

        Koto? receiverType = null;
        Koto? valueType = null;
        var signatureEnd = accessorToken.Span.End;
        if (hasSignature)
        {
            ParseAccessorParameters(ref reader, accessorKind, out receiverType, out valueType, out signatureEnd);
        }

        var returnType = ParseAccessorReturnType(ref reader);
        if (hasSignature && returnType is null)
        {
            reader.Expect(SyntaxForm.AccessorResult);
        }

        if (accessorKind == PropertyAccessorKind.Set && returnType is not null &&
            returnType is not TupleTypeKoto { ElementNodes.Count: 0 })
        {
            // The recovery keeps the setter's Unit result, so its body is checked as a setter body.
            returnType.Unexpected(SyntaxForm.SetterResult);
            returnType = new TupleTypeKoto(ref reader, returnType.Span, []);
        }

        var accessor = new PropertyAccessorKoto(
            ref reader,
            SourceSpan.FromBounds(header.Start, Math.Max(signatureEnd, returnType?.Span.End ?? accessorToken.Span.End)),
            header.Modifier,
            accessorKind,
            null,
            returnType,
            hasSignature,
            receiverType,
            valueType);
        Koto? body = default;
        if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan)
        {
            body = ParseSingleBodyItem(ref reader);
        }
        else if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            var block = ParseFunctionBlock(ref reader, null, accessor);
            body = property.IsContractRequirement && block.Items.Count == 0 ? null : block;
        }

        // An accessor that needs a signature, or that carries a result Type or a body, lacks its parameter list right after the keyword.
        if (!hasSignature && (returnType is not null || body is not null || property.IsContractRequirement || property.DeclarationKind == PropertyDeclarationKind.Computed))
        {
            reader.Missing(SyntaxForm.AccessorParameters, accessorToken.Span.End);
        }

        if (property.IsContractRequirement)
        {
            // A requirement accessor is a bare signature: no access modifier and no body.
            if (header.Modifier != ModifierKind.NoModifier)
            {
                RejectAccessorModifier(ref reader, property, accessor, header);
            }

            if (body is not null)
            {
                body.Unexpected(SyntaxForm.RequirementBody);
            }
        }
        else if (hasSignature && body is null)
        {
            reader.Expect(SyntaxForm.Body);
        }

        accessor.SetBody(body);
        AddPropertyAccessor(ref reader, property, accessor, accessorToken);

        if (body is not CodeBlockKoto)
        {
            reader.ExpectLineEnd();
        }
    }

    private static void ParseAccessorParameters(
        ref TokenReader reader,
        PropertyAccessorKind kind,
        out Koto? receiverType,
        out Koto? valueType,
        out int end)
    {
        end = reader.Read().Span.End;
        receiverType = null;
        valueType = null;
        if (reader.IsCurrentIdentifier("self"))
        {
            reader.Advance();
            if (!reader.Expect(TokenKind.Colon))
            {
                goto CloseParameters;
            }

            receiverType = ParseType(ref reader, TypeContext.Type);
            end = Math.Max(end, receiverType.Span.End);
            if (kind == PropertyAccessorKind.Set && !reader.Expect(TokenKind.Comma))
            {
                goto CloseParameters;
            }
        }

        if (kind == PropertyAccessorKind.Set)
        {
            if (reader.IsCurrentIdentifier("value"))
            {
                reader.Advance();
                if (!reader.Expect(TokenKind.Colon))
                {
                    goto CloseParameters;
                }

                valueType = ParseType(ref reader, TypeContext.Type);
                end = Math.Max(end, valueType.Span.End);
            }
            else
            {
                reader.Expect(SyntaxForm.SetterValueParameter);
            }
        }

CloseParameters:
        // Whatever remains of the list is skipped after the one report at its first token.
        reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);

        end = Math.Max(end, close.End);
    }

    private static Koto? ParseAccessorReturnType(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind != TokenKind.MinusGreaterThan)
        {
            return null;
        }

        var arrow = reader.Read().Span;
        if (IsExpressionBoundary(ref reader))
        {
            reader.Diagnostic.Add(arrow, DiagnosticCode.MissingReturnType_Kd);
            return new ErrorKoto(ref reader, arrow) { Cause = reader.Diagnostic.LastError };
        }

        return ParseType(ref reader, TypeContext.Type);
    }

    /// <summary>The header of one accessor: an optional access modifier and the get or set keyword.</summary>
    /// <param name="Start">The start of the header.</param>
    /// <param name="Modifier">The access modifier, or none.</param>
    /// <param name="ModifierEnd">The end of the modifier.</param>
    /// <param name="Keyword">The get or set keyword.</param>
    /// <param name="Kind">The accessor kind.</param>
    private readonly record struct AccessorHeader(int Start, ModifierKind Modifier, int ModifierEnd, Token Keyword, PropertyAccessorKind Kind);

    /// <summary>
    /// Reads an accessor's optional access modifier and its get or set keyword. Without the keyword the Property's accessor set is
    /// unknown after this, so its accessor checks rest on the Error; the caller skips the rest of the item.
    /// </summary>
    private static bool TryParseAccessorHeader(ref TokenReader reader, PropertyKoto property, out AccessorHeader header)
    {
        // An accessor list takes no attributes (SPEC 6.7): one is reported and kept for the accessor, which is still read.
        reader.ReportMisplacedAttributes();
        var start = reader.CurrentTokenRange.Start;
        var modifier = ParseAccessorAccessibility(ref reader);
        var modifierEnd = reader.PreviousEnd;
        var keyword = reader.CurrentToken;
        if (!TryGetPropertyAccessorKind(keyword.Kind, out var kind))
        {
            reader.CodeContext.RecordRecovery(property, reader.Expect(SyntaxForm.Accessor));
            header = default;
            return false;
        }

        reader.Advance();
        header = new(start, modifier, modifierEnd, keyword, kind);
        return true;
    }

    /// <summary>
    /// Reports an access modifier on an accessor that takes none (an inline accessor, or a requirement accessor). The accessor and
    /// its Property are checked as recoveries: the modifier is the one Error.
    /// </summary>
    private static void RejectAccessorModifier(ref TokenReader reader, PropertyKoto property, PropertyAccessorKoto accessor, in AccessorHeader header)
    {
        var decorated = reader.Unexpected(SyntaxForm.AccessorAccessibility, SourceSpan.FromBounds(header.Start, header.ModifierEnd));
        reader.CodeContext.RecordRecovery(accessor, decorated);
        reader.CodeContext.RecordRecovery(property, decorated);
    }

    private static ModifierKind ParseAccessorAccessibility(ref TokenReader reader)
    {
        var modifier = GetAccessibilityModifier(reader.CurrentTokenKind);
        if (modifier != ModifierKind.NoModifier)
        {
            reader.Advance();
            if (modifier == ModifierKind.Protected && reader.TryConsume(TokenKind.Internal))
            {
                modifier = ModifierKind.ProtectedOrInternal;
            }
            else if (modifier == ModifierKind.Private && reader.TryConsume(TokenKind.Protected))
            {
                modifier = ModifierKind.ProtectedAndInternal;
            }
        }

        return modifier;
    }

    private static bool TryGetPropertyAccessorKind(TokenKind tokenKind, out PropertyAccessorKind accessorKind)
    {
        accessorKind = tokenKind == TokenKind.Get ? PropertyAccessorKind.Get : PropertyAccessorKind.Set;
        return tokenKind is TokenKind.Get or TokenKind.Set;
    }

    private static void AddPropertyAccessor(
        ref TokenReader reader,
        PropertyKoto property,
        PropertyAccessorKoto accessor,
        Token accessorToken)
    {
        if (property.DeclarationKind == PropertyDeclarationKind.Let && accessor.AccessorKind == PropertyAccessorKind.Set)
        {
            reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.LetPropertyCannotHaveSetter_Kd);
        }

        if (!property.TryAddAccessor(accessor))
        {
            reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.DuplicatePropertyAccessor_Kd, accessor.AccessorText);
        }
    }
}
