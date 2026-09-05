// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202

/// <summary>
/// Parses tokens into Koto syntax-tree nodes and writes nodes as source text.
/// </summary>
public static class Parser
{
    private const int PrefixBindingPower = 90;
    private const int RangeLeftBindingPower = 8;
    private const int RangeRightBindingPower = 9;

    // Per-token-kind tables replace switch chains on the expression hot path.
    private static readonly byte[] InfixLeftBindingPower = new byte[TokenHelper.MaxTokens];
    private static readonly byte[] InfixRightBindingPower = new byte[TokenHelper.MaxTokens];
    private static readonly bool[] IsPrefixOperator = new bool[TokenHelper.MaxTokens];
    private static readonly bool[] IsPostfixOperator = new bool[TokenHelper.MaxTokens];
    private static readonly bool[] IsExpressionBoundaryKind = new bool[TokenHelper.MaxTokens];

    static Parser()
    {
        for (var i = 0; i < TokenHelper.MaxTokens; i++)
        {
            var (left, right) = GetInfixBindingPower((TokenKind)i);
            InfixLeftBindingPower[i] = (byte)left;
            InfixRightBindingPower[i] = (byte)right;
        }

        Mark(
            IsPostfixOperator,
            [
                TokenKind.Dot, TokenKind.OpenParenthesis, TokenKind.LessThan,
                TokenKind.OpenBracket, TokenKind.PlusPlus, TokenKind.MinusMinus,
            ]);

        Mark(
            IsPrefixOperator,
            [
                TokenKind.Dollar, TokenKind.Asterisk, TokenKind.Plus,
                TokenKind.Minus, TokenKind.Not, TokenKind.Caret, TokenKind.PlusPlus, TokenKind.MinusMinus,
            ]);

        Mark(
            IsExpressionBoundaryKind,
            [
                TokenKind.Separator, TokenKind.Semicolon, TokenKind.StartBlock, TokenKind.EndBlock, TokenKind.Else,
                TokenKind.EqualsGreaterThan, TokenKind.Comma, TokenKind.CloseParenthesis, TokenKind.CloseBracket,
            ]);

        static void Mark(bool[] table, ReadOnlySpan<TokenKind> kinds)
        {
            foreach (var kind in kinds)
            {
                table[(int)kind] = true;
            }
        }
    }

    /// <summary>Writes the qualified name of an identifiable node.</summary>
    /// <param name="koto">The innermost identifiable node.</param>
    /// <param name="builder">The destination builder.</param>
    public static void WriteQualifiedNameTo(IdentifiableKoto? koto, ref IndentedStringBuilder builder)
    {
        if (koto is null || koto.IsRoot)
        {
            return;
        }

        if (koto.Parent is IdentifiableKoto parent && !parent.IsRoot)
        {
            WriteQualifiedNameTo(parent, ref builder);
            builder.Append(Constants.DotChar);
        }

        builder.Append(koto.GetIdentifier());
    }

    /// <summary>Writes an attribute chain as source text, outermost attribute first.</summary>
    /// <param name="attribute">The first attribute in the chain.</param>
    /// <param name="builder">The destination builder.</param>
    /// <param name="options">The output options.</param>
    public static void UnparseAttribute(AttributeKoto? attribute, ref IndentedStringBuilder builder, KotoWriteOptions options)
    {
        if (attribute is null)
        {
            return;
        }

        WriteChain(attribute, ref builder);
        builder.AppendTrailingSpaceOrLineFeed(options);

        static void WriteChain(AttributeKoto attribute, ref IndentedStringBuilder builder)
        {
            if (attribute.AttributeChain is { } previous)
            {
                WriteChain(previous, ref builder);
                builder.Append(' ');
            }

            attribute.WriteTo(ref builder);
        }
    }

    /// <summary>Writes an attribute chain to a string.</summary>
    /// <param name="attribute">The first attribute in the chain.</param>
    /// <returns>The attribute source text.</returns>
    public static string UnparseAttribute(AttributeKoto? attribute)
    {
        if (attribute is null)
        {
            return string.Empty;
        }

        var builder = default(IndentedStringBuilder);
        try
        {
            UnparseAttribute(attribute, ref builder, KotoWriteOptions.AppendSpace);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>Parses a function declaration after its keyword.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="anonymous">Whether an omitted Name is allowed in expression position.</param>
    /// <returns>The parsed function, or <see langword="null"/> after an error.</returns>
    public static FunctionKoto? ParseFuncDeclaration(ref TokenReader reader, bool anonymous = false)
    {
        var context = reader.TakeContext();

        var methodToken = reader.CurrentToken;
        string? methodName;
        if (anonymous && methodToken.Kind == TokenKind.OpenParenthesis)
        {
            methodName = string.Empty;
        }
        else
        {
            if (!reader.TryRead(out methodToken))
            {
                return default;
            }

            if (!methodToken.Kind.IsIdentifierOrContextualKeyword())
            {
                reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                goto SkipAndExit;
            }

            if (!reader.TryGetIdentifier(methodToken, out methodName))
            {
                goto SkipAndExit;
            }
        }

        while (reader.TryConsume(TokenKind.Dot))
        {
            if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() ||
                !reader.TryGetIdentifier(reader.Read(), out var memberName))
            {
                reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                goto SkipAndExit;
            }

            methodName += "." + memberName;
        }

        List<TypeKoto>? genericArguments = default;
        if (reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = ParseGenericArguments(ref reader);
        }

        var origins = ParseOriginParameters(ref reader);

        if (!reader.TryConsume(TokenKind.OpenParenthesis, out _, true))
        {
            goto Exit;
        }

        List<FunctionParameterKoto>? parameters = default;
        while (reader.CanRead)
        {
            reader.SkipSeparators();
            while (reader.CurrentTokenKind == TokenKind.Sharp)
            {
                _ = ParseAttributeKoto(ref reader);
                reader.SkipSeparators();
            }

            if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
            {
                break;
            }

            var parameterAttribute = reader.PopAttribute();

            if (!reader.TryRead(out var externalNameToken))
            {
                goto Exit;
            }

            if (!reader.TryGetIdentifier(externalNameToken, out var externalName))
            {
                SkipParameter(ref reader);
                goto NextParameter;
            }

            var isOptional = reader.TryConsume(TokenKind.Question);

            var internalName = externalName;
            if (reader.TryConsume(TokenKind.EqualsGreaterThan))
            {
                if (!reader.TryRead(out var internalNameToken) ||
                    !reader.TryGetIdentifier(internalNameToken, out internalName))
                {
                    SkipParameter(ref reader);
                    goto NextParameter;
                }
            }

            if (!reader.TryConsume(TokenKind.Colon, out _, true))
            {
                goto Exit;
            }

            var parameterType = ParseDeclarationType(ref reader);
            Koto? defaultValue = default;
            if (reader.TryConsume(TokenKind.Equals))
            {
                defaultValue = ParseRequiredExpression(ref reader);
            }

            (parameters ??= []).Add(new(
                externalName,
                internalName,
                isOptional,
                parameterType,
                defaultValue,
                parameterAttribute));

NextParameter:
            reader.SkipSeparators();
            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                reader.Advance();
            }
            else if (reader.CurrentTokenKind != TokenKind.CloseParenthesis)
            {
                reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                SkipParameter(ref reader);
                reader.TryConsume(TokenKind.Comma);
            }
        }

        if (!reader.TryConsume(TokenKind.CloseParenthesis, out var closeParenthesisRange, true))
        {
            goto Exit;
        }

        Koto? returnType = default;
        var end = closeParenthesisRange.End;
        if (reader.TryConsume(TokenKind.MinusGreaterThan, out var returnArrowRange, false))
        {
            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock or TokenKind.EqualsGreaterThan or TokenKind.Semicolon)
            {
                reader.Diagnostic.Add(returnArrowRange, DiagnosticCode.MissingReturnType_Kd);
                returnType = new ErrorKoto(ref reader, returnArrowRange);
            }
            else
            {
                returnType = ParseDeclarationType(ref reader);
            }

            end = returnType.Span.End;
        }

        var functionKoto = new FunctionKoto(
            ref reader,
            context,
            SourceSpan.FromBounds(methodToken.Span.Start, end),
            methodName,
            genericArguments,
            parameters,
            returnType);
        functionKoto.SetOrigins(origins);

        if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan)
        {
            return functionKoto;
        }

        reader.SkipUntil(
            TokenKind.StartBlock,
            TokenKind.Separator,
            TokenKind.EndBlock,
            DiagnosticCode.UnexpectedTrailingToken_Kd);
        return functionKoto;

SkipAndExit:
        reader.SkipUntil(TokenKind.StartBlock, TokenKind.Separator, TokenKind.EndBlock);

Exit:
        return default;

        static void SkipParameter(ref TokenReader reader)
            => reader.SkipUntil(TokenKind.Comma, TokenKind.CloseParenthesis);
    }

    /// <summary>Parses a Declaration Container header.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The name, generic parameters, and origin names.</returns>
    public static (string Name, List<TypeKoto>? GenericArguments, List<string>? Origins) ParseDeclarationContainerHeader(ref TokenReader reader)
        => ParseDeclarationContainerHeader(ref reader, true, true);

    /// <summary>Parses a Declaration Container header according to the capabilities of its kind.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="supportsGenerics">Whether generic parameters are accepted.</param>
    /// <param name="supportsOrigins">Whether an Origin list is accepted.</param>
    /// <returns>The name, generic parameters, and origin names.</returns>
    internal static (string Name, List<TypeKoto>? GenericArguments, List<string>? Origins) ParseDeclarationContainerHeader(
        ref TokenReader reader,
        bool supportsGenerics,
        bool supportsOrigins)
    {
        string name = string.Empty;
        List<TypeKoto>? genericArguments = default;
        List<string>? origins = default;
        if (!reader.TryRead(out var token))
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            goto Exit;
        }

        if (!token.Kind.IsIdentifierOrContextualKeyword())
        {
            reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
            goto SkipAndExit;
        }

        if (reader.TryGetIdentifier(token, out var identifier))
        {
            name = identifier;
        }

        if (supportsGenerics && reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = ParseGenericArguments(ref reader);
        }

        if (supportsOrigins)
        {
            origins = ParseOriginParameters(ref reader);
        }

        reader.SkipUntilStartBlock();
        goto Exit;

SkipAndExit:
        reader.SkipUntilStartBlock(0);

Exit:
        return (name, genericArguments, origins);
    }

    private static List<string>? ParseOriginParameters(ref TokenReader reader)
    {
        if (!reader.IsCurrentIdentifier(Constants.OriginKeyword))
        {
            return null;
        }

        var originRange = reader.Read().Span;
        return ParseOrigins(ref reader, originRange);

        static List<string>? ParseOrigins(ref TokenReader reader, SourceSpan originRange)
        {
            List<string>? list = default;
            while (true)
            {
                if (!reader.CanRead)
                {
                    reader.Diagnostic.Add(originRange, DiagnosticCode.IncompleteSyntax_Kd);
                    return list;
                }

                if (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock or TokenKind.OpenParenthesis)
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                    return list;
                }

                var token = reader.Read();
                if (!token.Kind.IsIdentifierOrContextualKeyword())
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.IdentifierExpected_Kd);
                    return list;
                }

                var identifier = reader.GetSpan(token);
                if (!IdentifierHelper.IsValidIdentifier(identifier))
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, identifier.ToString());
                    return list;
                }

                (list ??= []).Add(reader.GetIdentifier(token));

                if (!reader.TryConsume(TokenKind.Comma))
                {
                    return list;
                }
            }
        }
    }

    /// <summary>Parses a local binding declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <returns>The parsed declaration, or <see langword="null"/> after an error.</returns>
    public static FieldKoto? ParseField(ref TokenReader reader, ref Token token)
        => ParseField(ref reader, token, false);

    private static FieldKoto? ParseField(ref TokenReader reader, Token token, bool allowParenthesizedTerminator)
    {
        var variableContext = reader.TakeContext();

        ConsumeAttributeAndModifier(ref reader, out var isEnd);
        if (isEnd)
        {
            return default;
        }

        var nameToken = reader.Read();
        if (!IdentifierNameKoto.TryCreate(ref reader, nameToken, out var nameKoto))
        {
            return default;
        }

        Koto? typeKoto = default;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {
            ConsumeAttributeAndModifier(ref reader, out isEnd);
            if (isEnd)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return default;
            }

            typeKoto = ParseType(ref reader);
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out _, false))
        {
            initializerKoto = ParseRequiredExpression(ref reader);
        }

        reader.RestoreContext(variableContext);

        var fieldKoto = new FieldKoto(ref reader, token, nameKoto, typeKoto, initializerKoto);

        if (reader.CurrentTokenKind == TokenKind.Semicolon)
        {
            reader.Advance();
        }
        else if (!allowParenthesizedTerminator ||
            reader.CurrentTokenKind is not (TokenKind.CloseParenthesis or TokenKind.Yield))
        {
            reader.SkipUntil(TokenKind.EndBlock, TokenKind.Separator, DiagnosticCode.UnexpectedTrailingToken_Kd);
        }

        return fieldKoto;
    }

    /// <summary>Parses a Property declaration and its optional accessor list.</summary>
    /// <param name="reader">The token reader positioned after <c>let</c> or <c>var</c>.</param>
    /// <param name="token">The Property declaration keyword token.</param>
    /// <returns>The parsed Property, or <see langword="null"/> after an error.</returns>
    public static PropertyKoto? ParseProperty(ref TokenReader reader, ref Token token)
    {
        var propertyContext = reader.TakeContext();
        while (reader.CurrentTokenKind == TokenKind.Sharp)
        {
            _ = ParseAttributeKoto(ref reader);
        }

        var nameToken = reader.Read();
        if (!IdentifierNameKoto.TryCreate(ref reader, nameToken, out var nameKoto))
        {
            return default;
        }

        Koto? typeKoto = default;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {
            while (reader.CurrentTokenKind == TokenKind.Sharp)
            {
                _ = ParseAttributeKoto(ref reader);
            }

            typeKoto = ParseType(ref reader);
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out _, false))
        {
            initializerKoto = ParseRequiredExpression(ref reader);
        }

        var hasInlineAccessors = reader.TryConsume(TokenKind.Has);

        reader.RestoreContext(propertyContext);
        var property = new PropertyKoto(ref reader, token, nameKoto, typeKoto, initializerKoto, hasInlineAccessors);

        if (hasInlineAccessors)
        {
            ParseInlinePropertyAccessors(ref reader, property);
        }

        if (reader.TryConsume(TokenKind.Semicolon))
        {
            return property;
        }

        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            if (hasInlineAccessors)
            {
                reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.UnexpectedToken_Kd, TokenKind.StartBlock.ToText());
                reader.SkipCurrentBlock(false);
            }
            else
            {
                ParsePropertyAccessorBlock(ref reader, property);
            }

            return property;
        }

        if (reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock) && reader.CanRead)
        {
            reader.SkipUntil(TokenKind.EndBlock, TokenKind.Separator, DiagnosticCode.UnexpectedTrailingToken_Kd);
        }

        return property;
    }

    private static void ParseInlinePropertyAccessors(ref TokenReader reader, PropertyKoto property)
    {
        var parsedAny = false;
        while (reader.CanRead)
        {
            var start = reader.CurrentTokenRange.Start;
            var modifier = ParseAccessorAccessibility(ref reader);
            var accessorToken = reader.CurrentToken;
            if (!TryGetPropertyAccessorKind(accessorToken.Kind, out var accessorKind))
            {
                reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.UnexpectedToken_Kd, reader.GetSpan(accessorToken).ToString());
                reader.SkipUntil(TokenKind.Comma, TokenKind.Separator, 0);
                break;
            }

            reader.Advance();
            parsedAny = true;
            var accessor = new PropertyAccessorKoto(
                ref reader,
                SourceSpan.FromBounds(start, accessorToken.Span.End),
                modifier,
                accessorKind,
                default);
            AddPropertyAccessor(ref reader, property, accessor, accessorToken);

            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }

            if (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.EndBlock)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                break;
            }
        }

        if (!parsedAny)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }
    }

    private static void ParsePropertyAccessorBlock(ref TokenReader reader, PropertyKoto property)
    {
        var blockStart = reader.CurrentTokenRange;
        reader.Advance();
        while (reader.CanRead)
        {
            reader.SkipSeparators();

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                var blockEnd = reader.CurrentTokenRange.End;
                reader.Advance();
                property.CompleteSpan(blockEnd);
                return;
            }

            var start = reader.CurrentTokenRange.Start;
            var modifier = ParseAccessorAccessibility(ref reader);
            var accessorToken = reader.CurrentToken;
            if (!TryGetPropertyAccessorKind(accessorToken.Kind, out var accessorKind))
            {
                reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.UnexpectedToken_Kd, reader.GetSpan(accessorToken).ToString());
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
                continue;
            }

            reader.Advance();
            Koto? body = default;
            if (reader.TryConsume(TokenKind.EqualsGreaterThan))
            {
                body = ParseRequiredExpression(ref reader);
            }
            else if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
            {
                body = ParseBlock(ref reader);
            }

            var end = Math.Max(accessorToken.Span.End, body?.Span.End ?? 0);
            var accessor = new PropertyAccessorKoto(
                ref reader,
                SourceSpan.FromBounds(start, end),
                modifier,
                accessorKind,
                body);
            AddPropertyAccessor(ref reader, property, accessor, accessorToken);

            if (body is not CodeBlockKoto &&
                reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock) &&
                reader.CanRead)
            {
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
            }
        }

        property.CompleteSpan(Math.Max(property.Span.End, blockStart.End));
        reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
    }

    private static ModifierKind ParseAccessorAccessibility(ref TokenReader reader)
    {
        var modifier = GetAccessibilityModifier(reader.CurrentTokenKind);
        if (modifier != ModifierKind.NoModifier)
        {
            reader.Advance();
        }

        return modifier;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ModifierKind GetAccessibilityModifier(TokenKind tokenKind)
        => tokenKind switch
        {
            TokenKind.Public => ModifierKind.Public,
            TokenKind.Protected => ModifierKind.Protected,
            TokenKind.Private => ModifierKind.Private,
            TokenKind.Internal => ModifierKind.Internal,
            TokenKind.ProtectedOrInternal => ModifierKind.ProtectedOrInternal,
            TokenKind.ProtectedAndInternal => ModifierKind.ProtectedAndInternal,
            _ => ModifierKind.NoModifier,
        };

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
        if (property.VariableKind == VariableKind.Let && accessor.AccessorKind == PropertyAccessorKind.Set)
        {
            reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.LetPropertyCannotHaveSetter_Kd);
        }

        if (!property.TryAddAccessor(accessor))
        {
            reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.DuplicatePropertyAccessor_Kd, accessor.AccessorText);
        }
    }

    /// <summary>Writes declaration modifiers as source text.</summary>
    /// <param name="kind">The modifiers to write.</param>
    /// <param name="builder">The destination builder.</param>
    /// <param name="writeOptions">The output options.</param>
    public static void WriteTo(this ModifierKind kind, ref IndentedStringBuilder builder, KotoWriteOptions writeOptions)
    {
        if (kind == ModifierKind.NoModifier)
        {
            return;
        }

        var accText = kind.ExtractAccessibilityModifiers().ToText();
        if (accText.Length > 0)
        {
            builder.Append(accText);
        }

        if (kind.HasFlag(ModifierKind.Static))
        {
            builder.EnsureTrailingSpace();
            builder.Append(Constants.StaticKeyword);
        }

        if (kind.HasFlag(ModifierKind.Open))
        {
            builder.EnsureTrailingSpace();
            builder.Append(Constants.OpenKeyword);
        }

        builder.AppendTrailingSpaceOrLineFeed(writeOptions);
    }

    /// <summary>Converts declaration modifiers to source text.</summary>
    /// <param name="kind">The modifiers to convert.</param>
    /// <param name="addSpace">Whether to append a trailing space.</param>
    /// <returns>The modifier source text.</returns>
    public static string ToText(this ModifierKind kind, bool addSpace = false)
    {
        var accText = kind.ExtractAccessibilityModifiers() switch
        {
            ModifierKind.Public => Constants.PublicKeyword,
            ModifierKind.Protected => Constants.ProtectedKeyword,
            ModifierKind.Private => Constants.PrivateKeyword,
            ModifierKind.Internal => Constants.InternalKeyword,
            ModifierKind.ProtectedOrInternal => Constants.ProtectedOrInternalKeyword,
            ModifierKind.ProtectedAndInternal => Constants.ProtectedAndInternalKeyword,
            _ => string.Empty,
        };

        if ((kind & (ModifierKind.Static | ModifierKind.Open)) == 0)
        {
            return addSpace && accText.Length > 0 ? accText + " " : accText;
        }

        var builder = default(IndentedStringBuilder);
        try
        {
            kind.WriteTo(ref builder, addSpace ? KotoWriteOptions.AppendSpace : KotoWriteOptions.None);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>Consumes attributes, modifiers, and optional compile-time directives before a declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="isEnd">Whether the declaration sequence has ended.</param>
    /// <param name="allowCompileTimeDirectives">Whether lowercase compile-time directives are accepted.</param>
    public static void ConsumeAttributeAndModifier(
        ref TokenReader reader,
        out bool isEnd,
        bool allowCompileTimeDirectives = false)
    {
        reader.ClearContext();

        while (reader.CanRead)
        {
            var tokenKind = reader.CurrentTokenKind;
            switch (tokenKind)
            {
                case TokenKind.Separator:
                    reader.Advance();
                    continue;

                case TokenKind.Static:
                    ReadFlag(ref reader, ModifierKind.Static);
                    continue;

                case TokenKind.Open:
                    ReadFlag(ref reader, ModifierKind.Open);
                    continue;

                case TokenKind.Public:
                case TokenKind.Protected:
                case TokenKind.Private:
                case TokenKind.Internal:
                case TokenKind.ProtectedOrInternal:
                case TokenKind.ProtectedAndInternal:
                    ReadAccessibility(ref reader, GetAccessibilityModifier(tokenKind));
                    continue;

                case TokenKind.Sharp:
                    if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.If)
                    {
                        ParseCompileTimeIfPrefix(ref reader);
                        reader.HasCompileTimeIfPrefix = true;
                        continue;
                    }

                    if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.Case)
                    {
                        isEnd = false;
                        return;
                    }

                    _ = ParseAttributeKoto(ref reader);
                    continue;

                default:
                    isEnd = false;
                    return;
            }
        }

        if (reader.HasCompileTimeIfPrefix)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

        isEnd = true;

        static void ReadFlag(ref TokenReader reader, ModifierKind flag)
        {
            if (reader.ModifierKind.HasFlag(flag))
            {
                reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, flag.ToString());
            }

            reader.ModifierKind |= flag;
            reader.Advance();
        }

        static void ReadAccessibility(ref TokenReader reader, ModifierKind kind)
        {
            var acc = reader.ModifierKind.ExtractAccessibilityModifiers();
            if (acc == default)
            {
                reader.ModifierKind |= kind;
            }
            else if (acc == kind)
            {
                reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, kind.ToText());
            }
            else
            {
                reader.AddDiagnostic(DiagnosticCode.MultipleAccessibilityModifiers_Kd);
            }

            reader.Advance();
        }
    }

    /// <summary>Parses a type expression.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed type node.</returns>
    public static Koto ParseType(ref TokenReader reader)
        => ParseDeclarationType(ref reader);

    private static Koto ParseType(ref TokenReader reader, bool parseOrigin)
    {
        var start = reader.CurrentTokenRange.Start;
        var left = ParseTypeInternal(ref reader);
        if (left is null)
        {
            return reader.NewErrorKoto();
        }

        while (reader.CanRead)
        {
            var tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Dot)
            {
                var operatorRange = reader.CurrentTokenRange;
                reader.Advance();

                var accessor = ParseTypeInternal(ref reader) ?? reader.NewErrorKoto();
                left = new MemberAccessKoto(
                    ref reader,
                    SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, accessor.Span.End)),
                    left,
                    accessor);
            }
            else if (tokenKind == TokenKind.LessThan)
            {
                left = ParseGenericsPostfix(ref reader, left);
            }
            else
            {
                break;
            }
        }

        if (left is not TypeKoto && left is not ErrorKoto)
        {
            left = new TypeSemanticsKoto(ref reader, SourceSpan.FromBounds(start, left.Span.End), left);
        }

        if (parseOrigin)
        {
            left = ParseTypeOrigin(ref reader, left);
        }

        return left;

        static Koto? ParseTypeInternal(ref TokenReader reader)
        {
            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.EndBlock or TokenKind.StartBlock or TokenKind.Comma or TokenKind.CloseParenthesis or TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan or TokenKind.Equals)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return null;
            }

            if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
            {
                return ParseDeclarationType(ref reader, false);
            }

            var token = reader.CurrentToken;
            reader.Advance();

            if (token.Kind.IsIdentifierOrContextualKeyword() &&
                reader.CurrentTokenKind == TokenKind.Slash)
            {// semantics/Type
                var semantics = reader.GetSpan(token);
                string? semanticsParameter = default;
                if (!CompilerHelper.TryParse(semantics, out var semanticsKind))
                {
                    semanticsParameter = reader.GetIdentifier(token);
                }

                reader.Advance();

                var attribute = reader.PopAttribute();
                var type = ParseType(ref reader, false);
                if (type is TypeSemanticsKoto { IsTransparentWrapper: true, Type: not null } transparentType)
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
                return result;
            }

            if (token.Kind.IsPrimitiveType() || token.Kind.IsIdentifierOrContextualKeyword())
            {
                return new TypeSemanticsKoto(ref reader, token);
            }

            reader.ReportUnexpectedToken(token);
            return null;
        }
    }

    private static Koto ParseTypeOrigin(ref TokenReader reader, Koto type)
    {
        if (!reader.IsCurrentIdentifier(Constants.FromKeyword))
        {
            return type;
        }

        var from = reader.Read();
        Koto? expression = null;
        OriginArgument[]? arguments = null;
        var end = from.Span.End;
        if (reader.TryConsume(TokenKind.OpenParenthesis))
        {
            var items = new List<OriginArgument>();
            reader.SkipSeparators();
            if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            }

            while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseParenthesis or TokenKind.EndBlock))
            {
                if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
                {
                    reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                    break;
                }

                var name = reader.GetIdentifier(reader.Read());
                if (!reader.TryConsume(TokenKind.EqualsGreaterThan, out _, true))
                {
                    break;
                }

                var value = ParseOriginExpression(ref reader);
                items.Add(new OriginArgument(name, value));
                end = Math.Max(end, value.Span.End);
                reader.SkipSeparators();
                if (!reader.TryConsume(TokenKind.Comma))
                {
                    break;
                }

                reader.SkipSeparators();
            }

            if (reader.TryConsume(TokenKind.CloseParenthesis, out var close, true))
            {
                end = close.End;
            }

            arguments = items.ToArray();
        }
        else
        {
            expression = ParseOriginExpression(ref reader);
            end = Math.Max(end, expression.Span.End);
        }

        var annotated = type as TypeSemanticsKoto ?? new TypeSemanticsKoto(ref reader, type.Span, type);
        annotated.SetOrigin(expression, arguments, end);
        return annotated;
    }

    private static Koto ParseOriginExpression(ref TokenReader reader)
    {
        var left = ParseQualifiedOrigin(ref reader);
        while (reader.CurrentTokenKind == TokenKind.And)
        {
            var op = reader.Read();
            var right = ParseQualifiedOrigin(ref reader);
            left = new AndKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(op.Span.End, right.Span.End)), left, right);
        }

        return left;

        static Koto ParseQualifiedOrigin(ref TokenReader reader)
        {
            if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
            {
                reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                return reader.NewErrorKoto();
            }

            var token = reader.Read();
            if (!IdentifierNameKoto.TryCreate(ref reader, token, out var name))
            {
                return new ErrorKoto(ref reader, token.Span);
            }

            Koto left = name;
            while (reader.TryConsume(TokenKind.Dot))
            {
                if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
                {
                    reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                    break;
                }

                var member = reader.Read();
                if (!IdentifierNameKoto.TryCreate(ref reader, member, out var right))
                {
                    return new ErrorKoto(ref reader, member.Span);
                }

                left = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, right.Span.End), left, right);
            }

            return left;
        }
    }

    /// <summary>Parses <c>&lt;T1, T2&gt;</c> after an identifier and wraps the identifier in a generic node.</summary>
    private static GenericsKoto ParseGenericsPostfix(ref TokenReader reader, Koto left)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();
        var typeList = default(TemporaryList<Koto>);
        var end = reader.CurrentTokenRange.End;
        reader.TrySkipSeparatorsTo(TokenKind.GreaterThan);
        while (true)
        {
            var type = ParseType(ref reader);
            typeList.Add(type);
            end = type.Span.End;
            reader.TrySkipSeparatorsTo(TokenKind.GreaterThan);
            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }

            reader.SkipSeparators();
            if (IsTypeClose(reader.CurrentTokenKind))
            {
                break;
            }
        }

        if (reader.TryConsumeTypeClose(out var range))
        {
            end = Math.Max(end, range.End);
        }

        return new GenericsKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(left.Span.End, end)), left, typeList.ToArray());
    }

    /// <summary>Parses an attribute expression.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed attribute, or <see langword="null"/> after an error.</returns>
    public static AttributeKoto? ParseAttributeKoto(ref TokenReader reader)
    {
        var previousAttribute = reader.PopAttribute();

        reader.TryRead(out var attributeToken);

        var operand = ParsePrimaryExpression(ref reader);
        while (TryParsePostfixExpression(ref reader, ref operand))
        {
        }

        if (previousAttribute is not null)
        {
            reader.PushAttribute(previousAttribute);
        }

        var attributeKoto = new AttributeKoto(
            ref reader,
            SourceSpan.FromBounds(attributeToken.Span.Start, Math.Max(attributeToken.Span.End, operand.Span.End)),
            operand);
        reader.PushAttribute(attributeKoto);
        return attributeKoto;
    }

    private static void ParseCompileTimeIfPrefix(ref TokenReader reader)
    {
        var attributes = reader.PopAttribute();
        var sharp = reader.Read();
        _ = reader.TryConsume(TokenKind.If, out _, true);
        var condition = ParseRequiredCompileTimeCondition(ref reader);
        if (attributes is not null)
        {
            reader.PushAttribute(attributes);
        }

        var span = SourceSpan.FromBounds(sharp.Span.Start, Math.Max(sharp.Span.End, condition.Span.End));

        switch (CompileTimeConditionEvaluator.Evaluate(reader.CodeContext.Compilation, condition))
        {
            case CompileTimeConditionResult.True:
                break;

            case CompileTimeConditionResult.False:
            case CompileTimeConditionResult.Error:
                reader.IsExcluded = true;
                reader.ClearCompileTimeIfPrefixes();
                break;

            case CompileTimeConditionResult.Deferred:
                if (!reader.IsExcluded)
                {
                    reader.AddCompileTimeIfPrefix(new CompileTimeIfPrefix(span, condition));
                }

                break;
        }
    }

    /// <summary>Parses consecutive compile-time Case Group arms.</summary>
    /// <param name="reader">The token reader positioned at the first <c>#case</c>.</param>
    /// <param name="declarationContext">The enclosing Declaration Container, when applicable.</param>
    /// <returns>The selected body or a deferred Case Group.</returns>
    internal static Koto ParseCompileTimeCaseGroup(ref TokenReader reader, DeclarationContainerKoto? declarationContext = null)
    {
        var groupStart = reader.CurrentTokenRange.Start;
        var groupEnd = reader.CurrentTokenRange.End;
        var arms = new List<CompileTimeCaseArmKoto>();
        var results = new List<CompileTimeConditionResult>();
        var fallbackSeen = false;
        var fallbackMustBeLastReported = false;
        var fallbackSpan = default(SourceSpan);

        while (IsCompileTimeCaseStart(ref reader))
        {
            if (fallbackSeen && !fallbackMustBeLastReported)
            {
                reader.Diagnostic.Add(fallbackSpan, DiagnosticCode.CompileTimeCaseFallbackMustBeLast_Kd);
                fallbackMustBeLastReported = true;
            }

            var sharp = reader.Read();
            _ = reader.TryConsume(TokenKind.Case, out _, true);

            Koto? condition;
            CompileTimeConditionResult result;
            if (reader.IsCurrentIdentifier("_"))
            {
                var fallbackToken = reader.Read();
                if (fallbackSeen)
                {
                    reader.Diagnostic.Add(fallbackToken.Span, DiagnosticCode.DuplicateCompileTimeCaseFallback_Kd);
                }
                else
                {
                    fallbackSeen = true;
                    fallbackSpan = SourceSpan.FromBounds(sharp.Span.Start, fallbackToken.Span.End);
                }

                condition = null;
                result = CompileTimeConditionResult.True;
            }
            else
            {
                condition = ParseRequiredCompileTimeCondition(ref reader);
                result = CompileTimeConditionEvaluator.Evaluate(reader.CodeContext.Compilation, condition);
            }

            var body = declarationContext is null || declarationContext.IsRoot
                ? ParseRequiredBlock(ref reader)
                : ParseDeclarationDirectiveBody(ref reader, declarationContext);
            arms.Add(new CompileTimeCaseArmKoto(condition, body));
            results.Add(result);
            groupEnd = Math.Max(groupEnd, body.Span.End);

            if (!TrySkipSeparatorsToCompileTimeCase(ref reader))
            {
                break;
            }
        }

        var selectionBlocked = false;
        var selectedIndex = -1;
        for (var i = 0; i < arms.Count; i++)
        {
            if (selectedIndex >= 0)
            {
                continue;
            }

            if (arms[i].Condition is null)
            {
                if (!selectionBlocked)
                {
                    selectedIndex = i;
                }

                continue;
            }

            switch (results[i])
            {
                case CompileTimeConditionResult.True when !selectionBlocked:
                    selectedIndex = i;
                    break;
                case CompileTimeConditionResult.Deferred:
                case CompileTimeConditionResult.Error:
                    selectionBlocked = true;
                    break;
            }
        }

        if (selectedIndex >= 0)
        {
            return arms[selectedIndex].Body;
        }

        var group = new CompileTimeCaseGroupKoto(
            ref reader,
            SourceSpan.FromBounds(groupStart, groupEnd),
            arms);
        if (!selectionBlocked && !fallbackSeen)
        {
            group.AddDiagnostic(DiagnosticCode.NonExhaustiveCompileTimeCase_Kd);
        }

        return group;
    }

    internal static CodeBlockKoto ParseDeclarationDirectiveBody(ref TokenReader reader, DeclarationContainerKoto declarationContext)
    {
        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return ParseRequiredBlock(ref reader);
        }

        var start = reader.CurrentTokenRange.Start;
        var temporary = DeclarationContainerKoto.CreateStandalone(reader.CodeContext, declarationContext.TokenKind, default, reader.CurrentTokenRange, string.Empty);
        temporary.Parse(ref reader);
        var items = new List<Koto>();
        items.AddRange(temporary.TypeConstraints);
        items.AddRange(temporary.Members);
        items.AddRange(temporary.NestedDeclarationContainers);
        return new CodeBlockKoto(ref reader, SourceSpan.FromBounds(start, reader.CurrentTokenRange.Start), items, false)
        {
            DeclarationContext = declarationContext.TokenKind,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsCompileTimeCaseStart(ref TokenReader reader)
        => reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) == TokenKind.Case;

    private static bool TrySkipSeparatorsToCompileTimeCase(ref TokenReader reader)
    {
        var offset = 0;
        while (reader.PeekKind(offset) == TokenKind.Separator)
        {
            offset++;
        }

        if (reader.PeekKind(offset) != TokenKind.Sharp || reader.PeekKind(offset + 1) != TokenKind.Case)
        {
            return false;
        }

        reader.Advance(offset);
        return true;
    }

    /// <summary>Wraps a syntax node in deferred compile-time directives, innermost first.</summary>
    /// <param name="codeContext">The owning code context.</param>
    /// <param name="prefixes">The deferred directive prefixes.</param>
    /// <param name="target">The controlled syntax node.</param>
    /// <returns>The outermost directive, or <paramref name="target"/> when no directive was deferred.</returns>
    internal static Koto ApplyCompileTimeIfPrefixes(
        CodeContext codeContext,
        List<CompileTimeIfPrefix>? prefixes,
        Koto target)
    {
        if (prefixes is null)
        {
            return target;
        }

        for (var i = prefixes.Count - 1; i >= 0; i--)
        {
            var prefix = prefixes[i];
            target = new CompileTimeIfKoto(
                codeContext,
                SourceSpan.FromBounds(prefix.Span.Start, Math.Max(prefix.Span.End, target.Span.End)),
                prefix.Condition,
                target);
        }

        return target;
    }

    /// <summary>Consumes one syntax node controlled by an early-false directive without constructing Koto nodes.</summary>
    /// <param name="reader">The token reader positioned at the controlled syntax.</param>
    internal static void SkipExcludedSyntax(ref TokenReader reader)
    {
        if (IsCompileTimeCaseStart(ref reader))
        {
            do
            {
                reader.Advance(2);
                _ = reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
                if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    reader.SkipCurrentBlock(false);
                }
            }
            while (TrySkipSeparatorsToCompileTimeCase(ref reader));

            return;
        }

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.SkipCurrentBlock(false);
            return;
        }

        var startsWithPrefix = reader.CurrentTokenKind == TokenKind.Sharp;
        _ = reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.SkipCurrentBlock(false);
            return;
        }

        reader.SkipSeparators();
        if (startsWithPrefix && reader.CanRead && reader.CurrentTokenKind != TokenKind.EndBlock)
        {
            SkipExcludedSyntax(ref reader);
        }
    }

    /// <summary>
    /// Parses a type constraint in the form <c>subject is condition</c>.
    /// </summary>
    /// <remarks>
    /// The special subject <c>semantics</c> accepts only named
    /// <see cref="SemanticsMask"/> values. Other subjects retain their operands as
    /// <see cref="IdentifierNameKoto"/> instances for later semantic analysis.
    /// </remarks>
    /// <param name="reader">The token reader positioned at the constraint subject.</param>
    /// <returns>The parsed constraint, or <see langword="null"/> when its required prefix is invalid.</returns>
    public static IsKoto? ParseTypeConstraint(ref TokenReader reader)
    {
        if (!reader.TryRead(out var subjectToken) ||
            !IdentifierNameKoto.TryCreate(ref reader, subjectToken, out var subject))
        {
            reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
            return null;
        }

        if (!reader.TryConsume(TokenKind.Is, out var isRange, true))
        {
            return null;
        }

        var parsesSemantics = subject.IdentifierName.AsSpan().SequenceEqual(Constants.SemanticsKeyword);
        var condition = ParseCondition(ref reader, parsesSemantics);
        var constraint = new IsKoto(ref reader, SourceSpan.FromBounds(subject.Span.Start, Math.Max(isRange.End, condition.Span.End)), subject, condition);

        reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
        return constraint;

        static Koto ParseCondition(ref TokenReader reader, bool parsesSemantics)
        {
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                var notToken = reader.Read();
                return new NotKoto(ref reader, notToken.Span, ParseOr(ref reader, parsesSemantics));
            }

            return ParseOr(ref reader, parsesSemantics);
        }

        static Koto ParseOr(ref TokenReader reader, bool parsesSemantics)
        {
            var left = ParseAnd(ref reader, parsesSemantics);
            while (reader.CurrentTokenKind == TokenKind.Or)
            {
                var token = reader.Read();
                left = new OrKoto(ref reader, token.Span, left, ParseAnd(ref reader, parsesSemantics));
            }

            return left;
        }

        static Koto ParseAnd(ref TokenReader reader, bool parsesSemantics)
        {
            var left = ParsePrimary(ref reader, parsesSemantics);
            while (reader.CurrentTokenKind == TokenKind.And)
            {
                var token = reader.Read();
                left = new AndKoto(ref reader, token.Span, left, ParsePrimary(ref reader, parsesSemantics));
            }

            return left;
        }

        static Koto ParsePrimary(ref TokenReader reader, bool parsesSemantics)
        {
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                var token = reader.Read();
                return new NotKoto(ref reader, token.Span, ParsePrimary(ref reader, parsesSemantics));
            }

            if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
            {
                var openRange = reader.CurrentTokenRange;
                reader.Advance();
                var operand = ParseCondition(ref reader, parsesSemantics);
                var range = openRange;
                if (reader.TryConsume(TokenKind.CloseParenthesis, out var closeRange, true))
                {
                    range = SourceSpan.FromBounds(openRange.Start, closeRange.End);
                }

                return new ParenthesizedKoto(ref reader, range, operand);
            }

            if (!reader.CanRead ||
                reader.CurrentTokenKind is TokenKind.Invalid or
                TokenKind.Separator or
                TokenKind.EndBlock or
                TokenKind.CloseParenthesis)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return reader.NewErrorKoto();
            }

            var token2 = reader.Read();
            if (parsesSemantics)
            {
                var text = reader.GetSpan(token2);
                if (token2.Kind == TokenKind.Identifier &&
                    SemanticsMaskHelper.TryParse(text, out var mask))
                {
                    return new SemanticsMaskKoto(ref reader, token2.Span, mask);
                }

                reader.Diagnostic.Add(token2.Span, DiagnosticCode.InvalidSemanticsConstraint_Kd, text.ToString());
                return new ErrorKoto(ref reader, token2.Span);
            }

            return IdentifierNameKoto.TryCreate(ref reader, token2, out var identifier)
                ? identifier
                : new ErrorKoto(ref reader, token2.Span);
        }
    }

    /// <summary>
    /// Determines whether the reader is positioned at the start of a type constraint.
    /// </summary>
    /// <param name="reader">The token reader to inspect.</param>
    /// <returns><see langword="true"/> for an identifier followed by <c>is</c>.</returns>
    public static bool IsTypeConstraintStart(ref TokenReader reader)
        => reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() &&
            reader.PeekKind(1) == TokenKind.Is;

    /// <summary>Parses an indentation-delimited expression block.</summary>
    /// <param name="reader">The token reader positioned at <see cref="TokenKind.StartBlock"/>.</param>
    /// <returns>The parsed block.</returns>
    public static CodeBlockKoto ParseBlock(ref TokenReader reader)
        => ParseFunctionBlock(ref reader, null);

    internal static CodeBlockKoto ParseFunctionBlock(ref TokenReader reader, FunctionKoto? function)
    {
        var start = reader.CurrentTokenRange;
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            return new CodeBlockKoto(ref reader, start, [], false);
        }

        var blockContext = reader.TakeContext();
        reader.Advance();
        var items = default(TemporaryList<Koto>);
        var hasTrailingExpression = false;
        var hasTrailingSemicolon = false;
        var seenExecutableItem = false;

        while (reader.CanRead)
        {
            while (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.Semicolon)
            {
                if (reader.CurrentTokenKind == TokenKind.Semicolon)
                {
                    hasTrailingExpression = false;
                    hasTrailingSemicolon = true;
                }

                reader.Advance();
            }

            if (!reader.CanRead)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                var end = reader.CurrentTokenRange.End;
                reader.Advance();
                reader.RestoreContext(blockContext);
                return new CodeBlockKoto(
                    ref reader,
                    SourceSpan.FromBounds(start.Start, end),
                    items.ToArray(),
                    hasTrailingExpression,
                    hasTrailingSemicolon);
            }

            ConsumeAttributeAndModifier(ref reader, out var isEnd, allowCompileTimeDirectives: true);
            if (isEnd)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                if (reader.HasCompileTimeIfPrefix)
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                continue;
            }

            var isExcluded = reader.IsExcluded;
            var compileTimeIfPrefixes = reader.TakeCompileTimeIfPrefixes();
            if (isExcluded)
            {
                SkipExcludedSyntax(ref reader);
                continue;
            }

            if (IsCompileTimeCaseStart(ref reader))
            {
                var caseGroup = ParseCompileTimeCaseGroup(ref reader);
                items.Add(ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, caseGroup));
                hasTrailingExpression = true;
                hasTrailingSemicolon = false;
                seenExecutableItem = true;
                continue;
            }

            if (function is not null && function.GenericArguments.Count > 0 && IsTypeConstraintStart(ref reader))
            {
                var subject = reader.GetIdentifier(reader.CurrentToken);
                var isGenericParameter = function.IsGenericParameter(subject);
                if (!seenExecutableItem || isGenericParameter)
                {
                    if (seenExecutableItem || !isGenericParameter)
                    {
                        reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.UnexpectedToken_Kd, subject);
                    }

                    var constraint = ParseTypeConstraint(ref reader);
                    if (constraint is not null)
                    {
                        function.AddTypeConstraint(ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, constraint));
                    }

                    continue;
                }
            }

            seenExecutableItem = true;
            var oldPosition = reader.Position;
            var item = ParseBlockItem(ref reader, out var isDeclaration);
            if (item is not null)
            {
                item = ApplyCompileTimeIfPrefixes(reader.CodeContext, compileTimeIfPrefixes, item);
                items.Add(item);
                hasTrailingExpression = !isDeclaration;
                hasTrailingSemicolon = false;
            }

            if (reader.CurrentTokenKind == TokenKind.Semicolon)
            {
                hasTrailingExpression = false;
                hasTrailingSemicolon = true;
                reader.Advance();
            }
            else if (reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock))
            {
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
            }

            if (reader.Position == oldPosition)
            {
                reader.Advance();
            }
        }

        var eof = reader.CurrentTokenRange.End;
        reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        reader.RestoreContext(blockContext);
        return new CodeBlockKoto(
            ref reader,
            SourceSpan.FromBounds(start.Start, Math.Max(start.End, eof)),
            items.ToArray(),
            hasTrailingExpression,
            hasTrailingSemicolon);
    }

    internal static Koto? ParseBlockItem(
        ref TokenReader reader,
        out bool isDeclaration,
        bool requiresFunctionBody = true)
    {
        var token = reader.CurrentToken;
        switch (token.Kind)
        {
            case TokenKind.Let:
            case TokenKind.Var:
                isDeclaration = true;
                reader.Advance();
                return ParseField(ref reader, token, false);

            case TokenKind.Func:
                if (reader.PeekKind(1) == TokenKind.OpenParenthesis)
                {
                    isDeclaration = false;
                    return ParseExpression(ref reader);
                }

                isDeclaration = true;
                reader.Advance();
                var function = ParseFuncDeclaration(ref reader);
                if (function is null)
                {
                    return null;
                }

                if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan || reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    function.Parse(ref reader);
                }
                else if (requiresFunctionBody)
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                return function;

            case TokenKind.Group:
            case TokenKind.Struct:
            case TokenKind.Enum:
            case TokenKind.Extension:
            case TokenKind.Contract:
                isDeclaration = true;
                reader.Advance();
                var supportsGenericHeader = token.Kind == TokenKind.Struct;
                var declaration = ParseDeclarationContainerHeader(
                    ref reader,
                    supportsGenericHeader,
                    supportsGenericHeader);
                var state = reader.TakeContext();
                var container = DeclarationContainerKoto.CreateStandalone(
                    reader.CodeContext,
                    token.Kind,
                    state,
                    token.Span,
                    declaration.Name);
                container.AddHeader(declaration.GenericArguments, declaration.Origins);

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    container.Parse(ref reader);
                }
                else
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                return container;

            default:
                isDeclaration = false;
                return ParseExpression(ref reader);
        }
    }

    private static IfKoto ParseIfExpression(ref TokenReader reader)
    {
        var ifToken = reader.Read();
        var branches = new List<ConditionalBranchKoto>();
        CodeBlockKoto? elseBody = null;
        int end;

        while (true)
        {
            var condition = ParseRequiredExpression(ref reader);
            var body = ParseRequiredConditionalBody(ref reader);
            branches.Add(new ConditionalBranchKoto(condition, body));
            end = body.Span.End;

            if (!reader.TrySkipSeparatorsTo(TokenKind.Else))
            {
                break;
            }

            reader.Advance();
            if (reader.TryConsume(TokenKind.If))
            {
                continue;
            }

            elseBody = ParseRequiredConditionalBody(ref reader);
            end = elseBody.Span.End;
            break;
        }

        return new IfKoto(
            ref reader,
            SourceSpan.FromBounds(ifToken.Span.Start, end),
            branches,
            elseBody);
    }

    private static WhileKoto ParseWhileExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var condition = ParseRequiredExpression(ref reader);
        var body = ParseRequiredBlock(ref reader);
        return new WhileKoto(
            ref reader,
            SourceSpan.FromBounds(token.Span.Start, body.Span.End),
            condition,
            body);
    }

    private static LoopKoto ParseLoopExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var body = ParseRequiredBlock(ref reader);
        return new LoopKoto(
            ref reader,
            SourceSpan.FromBounds(token.Span.Start, body.Span.End),
            body);
    }

    private static ForKoto ParseForExpression(ref TokenReader reader)
    {
        var forToken = reader.Read();
        var bindings = new List<IdentifierNameKoto>(2);
        var isTupleBinding = reader.CurrentTokenKind == TokenKind.OpenParenthesis;

        if (isTupleBinding)
        {
            ParseForTupleBindings(ref reader, bindings);
        }
        else if (TryParseForBinding(ref reader, out var binding))
        {
            bindings.Add(binding);
        }
        else
        {
            reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
            if (reader.CanRead &&
                reader.CurrentTokenKind != TokenKind.In &&
                !IsExpressionBoundary(ref reader))
            {
                reader.Advance();
            }
        }

        if (!reader.TryConsume(TokenKind.In))
        {
            reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, Constants.InKeyword);
        }

        var iterable = ParseRequiredExpression(ref reader);
        var body = ParseRequiredBlock(ref reader);
        return new ForKoto(
            ref reader,
            SourceSpan.FromBounds(forToken.Span.Start, body.Span.End),
            bindings,
            iterable,
            body,
            isTupleBinding);
    }

    private static void ParseForTupleBindings(ref TokenReader reader, List<IdentifierNameKoto> bindings)
    {
        reader.Advance();
        var expectsBinding = true;

        while (reader.CanRead)
        {
            if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
            {
                if (bindings.Count == 0)
                {
                    reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                }

                reader.Advance();
                return;
            }

            if (!expectsBinding)
            {
                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    reader.Advance();
                }
                else if (reader.CurrentTokenKind == TokenKind.In || IsExpressionBoundary(ref reader))
                {
                    reader.AddDiagnostic(
                        DiagnosticCode.TokenMismatch_Kd,
                        TokenKind.CloseParenthesis.ToText());
                    return;
                }
                else
                {
                    reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, TokenKind.Comma.ToText());
                }

                expectsBinding = true;
                continue;
            }

            if (reader.CurrentTokenKind == TokenKind.In || IsExpressionBoundary(ref reader))
            {
                reader.AddDiagnostic(
                    DiagnosticCode.TokenMismatch_Kd,
                    TokenKind.CloseParenthesis.ToText());
                return;
            }

            if (TryParseForBinding(ref reader, out var binding))
            {
                bindings.Add(binding);
                expectsBinding = false;
            }
            else
            {
                reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                reader.Advance();
            }
        }

        reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseForBinding(ref TokenReader reader, [NotNullWhen(true)] out IdentifierNameKoto? binding)
    {
        if (reader.CurrentTokenKind == TokenKind.In ||
            !reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
        {
            binding = default;
            return false;
        }

        var token = reader.Read();
        return IdentifierNameKoto.TryCreate(ref reader, token, out binding);
    }

    private static MatchKoto ParseMatchExpression(ref TokenReader reader)
    {
        var matchToken = reader.Read();
        var expression = ParseRequiredExpression(ref reader);
        var arms = new List<MatchArmKoto>();
        var end = expression.Span.End;

        reader.SkipSeparators();
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            return new MatchKoto(
                ref reader,
                SourceSpan.FromBounds(matchToken.Span.Start, end),
                expression,
                arms);
        }

        reader.Advance();
        while (reader.CanRead)
        {
            reader.SkipSeparators();
            if (!reader.CanRead)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                end = reader.CurrentTokenRange.End;
                reader.Advance();
                return new MatchKoto(
                    ref reader,
                    SourceSpan.FromBounds(matchToken.Span.Start, end),
                    expression,
                    arms);
            }

            var oldPosition = reader.Position;
            var pattern = ParseRequiredExpression(ref reader);
            if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
            {
                reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, TokenKind.EqualsGreaterThan.ToText());
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
            }
            else
            {
                reader.Advance();
                var body = reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock
                    ? ParseRequiredBlock(ref reader)
                    : ParseRequiredExpression(ref reader);

                var hasSemicolon = reader.TryConsume(TokenKind.Semicolon);
                arms.Add(new MatchArmKoto(pattern, body, hasSemicolon));
                end = body.Span.End;
            }

            reader.TryConsume(TokenKind.Semicolon);

            if (reader.Position == oldPosition)
            {
                reader.Advance();
            }
        }

        reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        return new MatchKoto(
            ref reader,
            SourceSpan.FromBounds(matchToken.Span.Start, end),
            expression,
            arms);
    }

    private static Koto ParseJumpExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        if (token.Kind == TokenKind.Continue)
        {
            var labelEnd = token.Span.End;
            var label = reader.CurrentTokenKind.IsIdentifierOrContextualKeyword()
                ? ParseTransferLabel(ref reader, ref labelEnd)
                : null;
            return new ContinueKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, labelEnd), label);
        }

        if (token.Kind == TokenKind.Yield)
        {
            var yieldExpression = ParseRequiredExpression(ref reader);
            return new YieldKoto(
                ref reader,
                SourceSpan.FromBounds(token.Span.Start, yieldExpression.Span.End),
                yieldExpression);
        }

        Koto? expression = default;
        var end = token.Span.End;
        if (!IsExpressionBoundary(ref reader) && !(token.Kind == TokenKind.Exit && reader.IsCurrentIdentifier(Constants.FromKeyword)))
        {
            expression = ParseExpression(ref reader);
            end = expression.Span.End;
        }

        string? targetLabel = null;
        if (token.Kind == TokenKind.Exit && reader.IsCurrentIdentifier(Constants.FromKeyword))
        {
            end = reader.Read().Span.End;
            targetLabel = ParseTransferLabel(ref reader, ref end);
        }

        var range = SourceSpan.FromBounds(token.Span.Start, end);
        return token.Kind switch
        {
            TokenKind.Return => new ReturnKoto(ref reader, range, expression),
            TokenKind.Exit => new ExitKoto(ref reader, range, expression, targetLabel),
            _ => throw new InvalidOperationException(),
        };
    }

    private static string? ParseTransferLabel(ref TokenReader reader, ref int end)
    {
        if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
        {
            reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
            return null;
        }

        var token = reader.Read();
        end = token.Span.End;
        return reader.TryGetIdentifier(token, out var label) ? label : null;
    }

    internal static Koto ParseRequiredExpression(ref TokenReader reader)
    {
        if (IsExpressionBoundary(ref reader))
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            return reader.NewErrorKoto();
        }

        return ParseExpression(ref reader);
    }

    private static Koto ParseRequiredCompileTimeCondition(ref TokenReader reader)
    {
        var previous = reader.IsParsingCompileTimeCondition;
        reader.IsParsingCompileTimeCondition = true;
        try
        {
            return ParseRequiredExpression(ref reader);
        }
        finally
        {
            reader.IsParsingCompileTimeCondition = previous;
        }
    }

    private static CodeBlockKoto ParseRequiredBlock(ref TokenReader reader)
    {
        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return ParseBlock(ref reader);
        }

        reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        return new CodeBlockKoto(ref reader, reader.CurrentTokenRange, [], false);
    }

    private static CodeBlockKoto ParseRequiredConditionalBody(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock)
        {
            return ParseRequiredBlock(ref reader);
        }

        if (!reader.TryConsume(TokenKind.EqualsGreaterThan))
        {
            reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, TokenKind.EqualsGreaterThan.ToText());
        }

        var expression = ParseRequiredExpression(ref reader);
        // An inline branch owns a semicolon only before else. A semicolon
        // after the entire if belongs to its surrounding statement or match arm.
        var lookahead = 1;
        while (reader.PeekKind(lookahead) == TokenKind.Separator)
        {
            lookahead++;
        }

        var hasSemicolon = reader.CurrentTokenKind == TokenKind.Semicolon &&
            reader.PeekKind(lookahead) == TokenKind.Else && reader.TryConsume(TokenKind.Semicolon);
        return new CodeBlockKoto(ref reader, expression.Span, [expression], !hasSemicolon, hasSemicolon)
        {
            IsExpressionBody = true,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExpressionBoundary(ref TokenReader reader)
        => !reader.CanRead || IsExpressionBoundaryKind[(byte)reader.CurrentTokenKind];

    /// <summary>Parses an expression using the requested minimum binding power.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="minBindingPower">The minimum accepted binding power.</param>
    /// <param name="allowLabel">Whether a leading Name and colon may introduce a Label rather than a dictionary key.</param>
    /// <returns>The parsed expression.</returns>
    public static Koto ParseExpression(ref TokenReader reader, int minBindingPower = 0, bool allowLabel = true)
    {
        var left = allowLabel && reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.Colon
            ? ParseLabeledExpression(ref reader)
            : ParsePrefixExpression(ref reader);
        while (true)
        {
            var tokenKind = reader.CurrentTokenKind;
            if (IsPostfixOperator[(byte)tokenKind] && TryParsePostfixExpression(ref reader, ref left))
            {
                continue;
            }

            if (tokenKind == TokenKind.Sharp)
            {
                // Attributes following an expression are attached to the next parsed node.
                _ = ParseAttributeKoto(ref reader);
                continue;
            }

            if (tokenKind == TokenKind.At)
            {
                var token2 = reader.Read();
                Koto typeKoto;
                if (IsExpressionBoundary(ref reader))
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                    typeKoto = reader.NewErrorKoto();
                }
                else
                {
                    // Origins in executable expressions are inferred. A following
                    // "from Label" belongs to an enclosing exit expression.
                    typeKoto = ParseType(ref reader, parseOrigin: false);
                }

                left = new ConversionKoto(
                    ref reader,
                    SourceSpan.FromBounds(left.Span.Start, Math.Max(token2.Span.End, typeKoto.Span.End)),
                    left,
                    typeKoto);
                continue;
            }

            if (tokenKind is TokenKind.DotDot or TokenKind.DotDotEquals)
            {
                if (minBindingPower > RangeLeftBindingPower)
                {
                    break;
                }

                var rangeToken = reader.Read();
                var end = ParseRangeEnd(ref reader, rangeToken);
                if (left is RangeKoto)
                {
                    reader.Diagnostic.Add(
                        rangeToken.Span,
                        DiagnosticCode.UnexpectedToken_Kd,
                        rangeToken.Kind.ToText());
                    continue;
                }

                left = new RangeKoto(
                    ref reader,
                    rangeToken.Span,
                    left,
                    end,
                    rangeToken.Kind == TokenKind.DotDotEquals);
                continue;
            }

            var leftBindingPower = InfixLeftBindingPower[(byte)tokenKind];
            if (leftBindingPower == 0 || leftBindingPower < minBindingPower)
            {
                break;
            }

            var rightBindingPower = InfixRightBindingPower[(byte)tokenKind];
            var token = reader.Read();
            Koto right;
            if (IsExpressionBoundary(ref reader))
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                right = reader.NewErrorKoto();
            }
            else if (token.Kind == TokenKind.Is && reader.CurrentTokenKind == TokenKind.Not)
            {
                var notToken = reader.Read();
                var condition = ParseExpression(ref reader, rightBindingPower);
                right = KotoHelper.NewUnaryKoto(ref reader, notToken, condition);
            }
            else
            {
                right = ParseExpression(ref reader, rightBindingPower);
            }

            left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
        }

        return left;
    }

    private static Koto ParseLabeledExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        reader.TryGetIdentifier(token, out var label);
        reader.Advance(); // Colon.
        Koto target;
        switch (reader.CurrentTokenKind)
        {
            case TokenKind.For:
                target = ParseForExpression(ref reader);
                break;
            case TokenKind.While:
                target = ParseWhileExpression(ref reader);
                break;
            case TokenKind.Loop:
                target = ParseLoopExpression(ref reader);
                break;
            case TokenKind.Separator:
            case TokenKind.StartBlock:
                target = ParseRequiredBlock(ref reader);
                break;
            default:
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                target = reader.NewErrorKoto();
                break;
        }

        return new LabeledKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, target.Span.End), label ?? string.Empty, target);
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader)
    {
ProcessPrefix:
        var tokenKind = reader.CurrentTokenKind;
        if (tokenKind == TokenKind.Sharp)
        {
            _ = ParseAttributeKoto(ref reader);
            goto ProcessPrefix;
        }

        if (IsPrefixOperator[(byte)tokenKind] && reader.CanRead)
        {
            var token = reader.Read();
            Koto operand;
            if (IsExpressionBoundary(ref reader))
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                operand = reader.NewErrorKoto();
            }
            else
            {
                operand = ParseExpression(ref reader, PrefixBindingPower);
            }

            return KotoHelper.NewUnaryKoto(ref reader, token, operand);
        }

        return ParsePrimaryExpression(ref reader);
    }

    private static bool TryParsePostfixExpression(ref TokenReader reader, ref Koto left)
    {
        switch (reader.CurrentTokenKind)
        {
            case TokenKind.Dot:
                {
                    var operatorRange = reader.CurrentTokenRange;
                    reader.Advance();

                    var accessor = ParsePrimaryExpression(ref reader);
                    left = new MemberAccessKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, accessor.Span.End)),
                        left,
                        accessor);
                    return true;
                }

            case TokenKind.OpenParenthesis:
                {
                    var openRange = reader.CurrentTokenRange;
                    reader.Advance();
                    var arguments = ParseArgumentList(ref reader, out var argumentLabels);
                    var end = arguments.Length == 0 ? openRange.End : Math.Max(openRange.End, arguments[^1].Span.End);
                    if (reader.TryConsume(TokenKind.CloseParenthesis, out var range, true))
                    {
                        end = Math.Max(end, range.End);
                    }

                    left = new InvocationKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, end),
                        left,
                        arguments,
                        argumentLabels);
                    return true;
                }

            case TokenKind.LessThan:
                {
                    if (!IsGenericPostfix(ref reader, left))
                    {
                        return false;
                    }

                    left = ParseGenericsPostfix(ref reader, left);
                    return true;
                }

            case TokenKind.OpenBracket:
                {
                    var openRange = reader.CurrentTokenRange;
                    reader.Advance();
                    Koto index;
                    if (IsExpressionBoundary(ref reader))
                    {
                        reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                        index = reader.NewErrorKoto();
                    }
                    else
                    {
                        index = ParseExpression(ref reader);
                    }

                    var end = Math.Max(openRange.End, index.Span.End);
                    if (reader.TryConsume(TokenKind.CloseBracket, out var range, true))
                    {
                        end = Math.Max(end, range.End);
                    }

                    left = new IndexKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, end), left, index);
                    return true;
                }

            case TokenKind.PlusPlus:
                {
                    var token = reader.Read();
                    left = new PostfixIncrementKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, token.Span.End),
                        left);
                    return true;
                }

            case TokenKind.MinusMinus:
                {
                    var token = reader.Read();
                    left = new PostfixDecrementKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, token.Span.End),
                        left);
                    return true;
                }
        }

        return false;
    }

    private static bool IsGenericPostfix(ref TokenReader reader, Koto left)
    {
        if (left is not (IdentifierNameKoto or MemberAccessKoto or GenericsKoto) ||
            reader.CurrentTokenRange.Start != left.Span.End)
        {
            return false;
        }

        // Require adjacent, balanced angle brackets to distinguish generics from comparisons.
        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            switch (reader.PeekKind(offset))
            {
                case TokenKind.LessThan:
                    depth++;
                    break;
                case TokenKind.GreaterThan:
                    if (--depth == 0)
                    {
                        return true;
                    }

                    break;
                case TokenKind.GreaterThanGreaterThan:
                    depth -= 2;
                    if (depth <= 0)
                    {
                        return true;
                    }

                    break;
                case TokenKind.Separator:
                case TokenKind.EndBlock:
                case TokenKind.Invalid:
                    return false;
            }
        }
    }

    private static Koto[] ParseArgumentList(ref TokenReader reader, out string?[]? argumentLabels)
    {// (arg0, arg1, )
        var arguments = default(TemporaryList<Koto>);
        var labels = default(TemporaryList<string?>);
        var hasLabels = false;
        argumentLabels = default;

        var tokenKind = reader.CurrentTokenKind;
        while (tokenKind != TokenKind.Invalid &&
               tokenKind != TokenKind.CloseParenthesis)
        {
            if (tokenKind.IsIdentifierOrContextualKeyword() &&
                reader.PeekKind(1) == TokenKind.Colon)
            {
                var label = reader.GetIdentifier(reader.CurrentToken);
                reader.Advance(2);

                if (!hasLabels)
                {
                    hasLabels = true;
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        labels.Add(null);
                    }
                }

                labels.Add(label);
            }
            else if (hasLabels)
            {
                labels.Add(null);
            }

            arguments.Add(ParseRequiredExpression(ref reader));

            tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Comma)
            {
                reader.Advance();
                tokenKind = reader.CurrentTokenKind;
                if (tokenKind == TokenKind.CloseParenthesis)
                {
                    break;
                }

                continue;
            }

            if (tokenKind != TokenKind.CloseParenthesis)
            {
                reader.TryConsume(TokenKind.Comma, out _);
                reader.SkipUntil(TokenKind.Comma, TokenKind.CloseParenthesis);

                if (reader.TryConsume(TokenKind.Comma))
                {
                    tokenKind = reader.CurrentTokenKind;
                    continue;
                }
            }

            break;
        }

        if (hasLabels)
        {
            argumentLabels = labels.ToArray();
        }

        return arguments.ToArray();
    }

    private static Koto ParsePrimaryExpression(ref TokenReader reader)
    {
Loop:
        var tokenKind = reader.CurrentTokenKind;
        if (reader.IsParsingCompileTimeCondition && tokenKind.IsPrimitiveType())
        {
            return new TypeSemanticsKoto(ref reader, reader.Read());
        }

        switch (tokenKind)
        {
            case TokenKind.Identifier:
            case TokenKind.In:
                {
                    var token = reader.Read();
                    if (IdentifierNameKoto.TryCreate(ref reader, token, out var koto))
                    {
                        return koto;
                    }

                    return reader.NewErrorKoto();
                }

            case TokenKind.NumericLiteral:
                return new NumberLiteralKoto(ref reader, reader.Read());

            case TokenKind.CharLiteral:
                return new CharLiteralKoto(ref reader, reader.Read());

            case TokenKind.StringLiteral:
                {
                    var literal = new StringLiteralKoto(ref reader, reader.Read());
                    _ = literal.Literal; // Validate escapes during parsing, even if the value is never requested.
                    return literal;
                }

            case TokenKind.InterpolatedStringLiteral:
                return ParseInterpolatedString(ref reader);

            case TokenKind.True:
            case TokenKind.False:
                return new BoolLiteralKoto(ref reader, reader.Read());

            case TokenKind.If:
                return ParseIfExpression(ref reader);

            case TokenKind.Func:
                {
                    reader.Advance();
                    var function = ParseFuncDeclaration(ref reader, anonymous: true);
                    if (function is null)
                    {
                        return reader.NewErrorKoto();
                    }

                    function.Parse(ref reader);
                    return function;
                }

            case TokenKind.StartBlock:
                return ParseBlock(ref reader);

            case TokenKind.DotDot:
            case TokenKind.DotDotEquals:
                {
                    var token = reader.Read();
                    var end = ParseRangeEnd(ref reader, token);
                    return new RangeKoto(
                        ref reader,
                        token.Span,
                        default,
                        end,
                        token.Kind == TokenKind.DotDotEquals);
                }

            case TokenKind.Match:
                return ParseMatchExpression(ref reader);

            case TokenKind.While:
                return ParseWhileExpression(ref reader);

            case TokenKind.For:
                return ParseForExpression(ref reader);

            case TokenKind.Loop:
                return ParseLoopExpression(ref reader);

            case TokenKind.Return:
            case TokenKind.Exit:
            case TokenKind.Continue:
            case TokenKind.Yield:
                return ParseJumpExpression(ref reader);

            case TokenKind.OpenParenthesis:
                return ParseParenthesizedExpression(ref reader);

            case TokenKind.OpenBracket:
                return ParseCollectionLiteral(ref reader);

            case TokenKind.Separator:
                reader.Advance();
                goto Loop;

            default:
                {
                    reader.TryRead(out var token);
                    if (token.Kind.IsIdentifierOrContextualKeyword() &&
                        IdentifierNameKoto.TryCreate(ref reader, token, out var identifier))
                    {
                        return identifier;
                    }

                    reader.ReportUnexpectedToken(token);

                    return new ErrorKoto(ref reader, token.Span);
                }
        }
    }

    private static Koto ParseInterpolatedString(ref TokenReader reader)
    {
        var token = reader.Read();
        var context = reader.TakeContext();
        var text = reader.GetSpan(token);
        var segments = new List<StringLiteralKoto>();
        var expressions = new List<Koto>();
        var segmentStart = 1;
        var offset = 1;
        while (offset < text.Length - 1)
        {
            if (text[offset++] != '\\')
            {
                continue;
            }

            if (text[offset++] != '(')
            {
                continue;
            }

            var open = offset - 1;
            var segment = new StringLiteralKoto(ref reader, new Token(TokenKind.StringLiteral, new SourceSpan(token.Span.Start + segmentStart, open - 1 - segmentStart)));
            _ = segment.Literal;
            segments.Add(segment);
            var close = open + StringLiteralHelper.FindInterpolationEnd(text[open..]);
            if (close < open)
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.IncompleteSyntax_Kd);
                break;
            }

            var tokenizer = new Tokenizer(reader.Diagnostic, reader.Diagnostic.SourceDocument!, SourceSpan.FromBounds(token.Span.Start + open, token.Span.Start + close + 1));
            try
            {
                tokenizer.ReadAll();
                var nested = new TokenReader(reader.CodeContext, ref tokenizer);
                nested.TryConsume(TokenKind.OpenParenthesis);
                var expression = ParseRequiredExpression(ref nested);
                nested.TryConsume(TokenKind.CloseParenthesis, out _, true);
                nested.SkipSeparators();
                if (nested.CanRead)
                {
                    nested.AddDiagnostic(DiagnosticCode.UnexpectedTrailingToken_Kd);
                }

                expressions.Add(expression);
            }
            finally
            {
                tokenizer.Dispose();
            }

            offset = segmentStart = close + 1;
        }

        var trailing = new StringLiteralKoto(ref reader, new Token(TokenKind.StringLiteral, new SourceSpan(token.Span.Start + segmentStart, text.Length - 1 - segmentStart)));
        _ = trailing.Literal;
        segments.Add(trailing);
        reader.RestoreContext(context);
        return new InterpolatedStringKoto(ref reader, token.Span, segments.ToArray(), expressions.ToArray());
    }

    private static Koto ParseParenthesizedExpression(ref TokenReader reader)
    {
        var openToken = reader.Read();

        if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
        {
            var unitEnd = reader.Read().Span;
            return new UnitLiteralKoto(ref reader, SourceSpan.FromBounds(openToken.Span.Start, unitEnd.End));
        }

        var operand = reader.CurrentTokenKind is TokenKind.Let or TokenKind.Var or TokenKind.Yield
            ? ParseParenthesizedBlock(ref reader)
            : ParseExpression(ref reader);

        var end = Math.Max(openToken.Span.End, operand.Span.End);
        if (reader.TryConsume(TokenKind.Comma))
        {
            var elements = new List<Koto> { operand };
            reader.SkipSeparators();
            while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseParenthesis or TokenKind.EndBlock))
            {
                elements.Add(ParseRequiredExpression(ref reader));
                end = Math.Max(end, elements[^1].Span.End);
                reader.SkipSeparators();
                if (!reader.TryConsume(TokenKind.Comma))
                {
                    break;
                }

                reader.SkipSeparators();
            }

            if (reader.TryConsume(TokenKind.CloseParenthesis, out var close, true))
            {
                end = close.End;
            }

            return new TupleLiteralKoto(ref reader, SourceSpan.FromBounds(openToken.Span.Start, end), elements);
        }

        reader.TrySkipSeparatorsTo(TokenKind.CloseParenthesis);
        if (reader.TryConsume(TokenKind.CloseParenthesis, out var closeRange, true))
        {
            end = Math.Max(end, closeRange.End);
        }

        return new ParenthesizedKoto(
            ref reader,
            SourceSpan.FromBounds(openToken.Span.Start, end),
            operand);
    }

    private static CodeBlockKoto ParseParenthesizedBlock(ref TokenReader reader)
    {
        var items = new List<Koto>();
        var hasTrailingExpression = false;

        while (reader.CurrentTokenKind is TokenKind.Let or TokenKind.Var)
        {
            var declarationToken = reader.Read();
            var field = ParseField(ref reader, declarationToken, allowParenthesizedTerminator: true);
            if (field is not null)
            {
                items.Add(field);
            }
        }

        if (reader.CurrentTokenKind == TokenKind.Yield)
        {
            items.Add(ParseJumpExpression(ref reader));
            hasTrailingExpression = true;
        }

        if (items.Count == 0)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            return new CodeBlockKoto(ref reader, reader.CurrentTokenRange, items, false);
        }

        return new CodeBlockKoto(
            ref reader,
            SourceSpan.FromBounds(items[0].Span.Start, items[^1].Span.End),
            items,
            hasTrailingExpression);
    }

    private static Koto ParseCollectionLiteral(ref TokenReader reader)
    {
        var openRange = reader.CurrentTokenRange;
        reader.Advance();
        reader.SkipSeparators();

        if (reader.TryConsume(TokenKind.CloseBracket, out var emptyArrayClose, false))
        {
            return new ArrayLiteralKoto(
                ref reader,
                SourceSpan.FromBounds(openRange.Start, emptyArrayClose.End),
                []);
        }

        if (reader.TryConsume(TokenKind.Colon))
        {
            reader.SkipSeparators();
            var end = openRange.End;
            if (reader.TryConsume(TokenKind.CloseBracket, out var emptyDictionaryClose, true))
            {
                end = emptyDictionaryClose.End;
            }

            return new DictionaryLiteralKoto(
                ref reader,
                SourceSpan.FromBounds(openRange.Start, end),
                []);
        }

        var first = ParseLiteralElement(ref reader, allowLabel: false);
        reader.SkipSeparators();
        return reader.CurrentTokenKind == TokenKind.Colon
            ? ParseDictionaryLiteral(ref reader, openRange, first)
            : ParseArrayLiteral(ref reader, openRange, first);

        static ArrayLiteralKoto ParseArrayLiteral(ref TokenReader reader, SourceSpan openRange, Koto first)
        {
            var elements = new List<Koto> { first };
            while (reader.CanRead && reader.CurrentTokenKind != TokenKind.CloseBracket)
            {
                if (reader.CurrentTokenKind != TokenKind.Comma)
                {
                    reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                    reader.SkipUntil(TokenKind.Comma, TokenKind.CloseBracket, 0);
                    if (reader.CurrentTokenKind == TokenKind.CloseBracket)
                    {
                        break;
                    }
                }

                reader.Advance();
                reader.SkipSeparators();
                if (reader.CurrentTokenKind == TokenKind.CloseBracket)
                {
                    break;
                }

                elements.Add(ParseLiteralElement(ref reader));
                reader.SkipSeparators();
            }

            var end = elements[^1].Span.End;
            if (reader.TryConsume(TokenKind.CloseBracket, out var closeRange, true))
            {
                end = closeRange.End;
            }

            return new ArrayLiteralKoto(
                ref reader,
                SourceSpan.FromBounds(openRange.Start, Math.Max(openRange.End, end)),
                elements);
        }

        static DictionaryLiteralKoto ParseDictionaryLiteral(ref TokenReader reader, SourceSpan openRange, Koto firstKey)
        {
            var entries = new List<DictionaryLiteralEntry>();
            var key = firstKey;
            while (reader.CanRead)
            {
                if (reader.CurrentTokenKind != TokenKind.Colon)
                {
                    reader.Diagnostic.Add(
                        reader.CurrentTokenRange,
                        DiagnosticCode.TokenMismatch_Kd,
                        TokenKind.Colon.ToText());
                    reader.SkipUntil(TokenKind.Comma, TokenKind.CloseBracket, 0);
                }
                else
                {
                    reader.Advance();
                    reader.SkipSeparators();
                    var value = ParseLiteralElement(ref reader);
                    entries.Add(new(key, value));
                    reader.SkipSeparators();
                }

                if (reader.CurrentTokenKind == TokenKind.CloseBracket)
                {
                    break;
                }

                if (reader.CurrentTokenKind != TokenKind.Comma)
                {
                    reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                    reader.SkipUntil(TokenKind.Comma, TokenKind.CloseBracket, 0);
                    if (reader.CurrentTokenKind == TokenKind.CloseBracket)
                    {
                        break;
                    }
                }

                reader.Advance();
                reader.SkipSeparators();
                if (reader.CurrentTokenKind == TokenKind.CloseBracket)
                {
                    break;
                }

                key = ParseLiteralElement(ref reader, allowLabel: false);
                reader.SkipSeparators();
            }

            var end = entries.Count == 0 ? firstKey.Span.End : entries[^1].Value.Span.End;
            if (reader.TryConsume(TokenKind.CloseBracket, out var closeRange, true))
            {
                end = closeRange.End;
            }

            return new DictionaryLiteralKoto(
                ref reader,
                SourceSpan.FromBounds(openRange.Start, Math.Max(openRange.End, end)),
                entries);
        }

        static Koto ParseLiteralElement(ref TokenReader reader, bool allowLabel = true)
        {
            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Comma or TokenKind.CloseBracket)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return reader.NewErrorKoto();
            }

            return ParseExpression(ref reader, allowLabel: allowLabel);
        }
    }

    private static (int Left, int Right) GetInfixBindingPower(TokenKind kind)
        => kind switch
        {
            // Multiplicative
            TokenKind.Asterisk => (80, 81),
            TokenKind.Slash => (80, 81),
            TokenKind.Percent => (80, 81),
            TokenKind.At => (80, 81),

            // Additive
            TokenKind.Plus => (70, 71),
            TokenKind.Minus => (70, 71),

            // Shift
            TokenKind.LessThanLessThan => (65, 66),
            TokenKind.GreaterThanGreaterThan => (65, 66),

            // Relational
            TokenKind.LessThan => (60, 61),
            TokenKind.LessThanEquals => (60, 61),
            TokenKind.GreaterThan => (60, 61),
            TokenKind.GreaterThanEquals => (60, 61),
            TokenKind.As => (60, 61),
            // Keep "is" relational on its left, but allow its right operand to be
            // a logical condition: X is not A or B -> X is not (A or B).
            TokenKind.Is => (60, 10),

            // Equality
            TokenKind.EqualsEquals => (50, 51),
            TokenKind.ExclamationEquals => (50, 51),

            // Bitwise
            TokenKind.Ampersand => (40, 41),
            TokenKind.Caret => (35, 36),
            TokenKind.Bar => (30, 31),

            // Logical
            TokenKind.And => (20, 21),
            TokenKind.Or => (10, 11),

            // Range (non-associative; parsed specially because either endpoint may be omitted)
            TokenKind.DotDot => (RangeLeftBindingPower, RangeRightBindingPower),
            TokenKind.DotDotEquals => (RangeLeftBindingPower, RangeRightBindingPower),

            // Assignment
            TokenKind.Equals => (5, 5),
            TokenKind.PlusEquals => (5, 5),
            TokenKind.MinusEquals => (5, 5),
            TokenKind.AsteriskEquals => (5, 5),
            TokenKind.SlashEquals => (5, 5),
            TokenKind.PercentEquals => (5, 5),
            TokenKind.AmpersandEquals => (5, 5),
            TokenKind.CaretEquals => (5, 5),
            TokenKind.BarEquals => (5, 5),
            TokenKind.LessThanLessThanEquals => (5, 5),
            TokenKind.GreaterThanGreaterThanEquals => (5, 5),

            _ => default,
        };

    private static Koto? ParseRangeEnd(ref TokenReader reader, Token rangeToken)
    {
        if (IsExpressionBoundary(ref reader) ||
            reader.CurrentTokenKind is TokenKind.DotDot or TokenKind.DotDotEquals)
        {
            if (rangeToken.Kind == TokenKind.DotDotEquals)
            {
                reader.Diagnostic.Add(rangeToken.Span, DiagnosticCode.IncompleteSyntax_Kd);
            }

            return default;
        }

        return ParseExpression(ref reader, RangeRightBindingPower);
    }

    private static Koto ParseDeclarationType(ref TokenReader reader, bool parseOrigin = true)
    {
        Koto type;
        if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
        {
            var openRange = reader.CurrentTokenRange;
            reader.Advance();

            var elements = new List<Koto>();
            while (reader.CanRead && reader.CurrentTokenKind != TokenKind.CloseParenthesis)
            {
                if (reader.CurrentTokenKind == TokenKind.Separator)
                {
                    reader.Advance();
                    continue;
                }

                elements.Add(ParseDeclarationType(ref reader));
                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    reader.Advance();
                }
                else if (reader.CurrentTokenKind != TokenKind.CloseParenthesis)
                {
                    reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                    reader.SkipUntil(TokenKind.Comma, TokenKind.CloseParenthesis);
                    reader.TryConsume(TokenKind.Comma);
                }
            }

            var end = elements.Count == 0 ? openRange.End : Math.Max(openRange.End, elements[^1].Span.End);
            if (reader.TryConsume(TokenKind.CloseParenthesis, out var closeRange, true))
            {
                end = Math.Max(end, closeRange.End);
            }

            type = new TupleTypeKoto(ref reader, SourceSpan.FromBounds(openRange.Start, end), elements);
            if (parseOrigin)
            {
                type = ParseTypeOrigin(ref reader, type);
            }
        }
        else
        {
            type = ParseType(ref reader, parseOrigin);
        }

        if (reader.CurrentTokenKind != TokenKind.MinusGreaterThan)
        {
            return type;
        }

        var arrowRange = reader.CurrentTokenRange;
        reader.Advance();
        var returnType = ParseDeclarationType(ref reader);
        return new FunctionTypeKoto(
            ref reader,
            SourceSpan.FromBounds(type.Span.Start, Math.Max(arrowRange.End, returnType.Span.End)),
            type,
            returnType);
    }

    private static List<TypeKoto>? ParseGenericArguments(ref TokenReader reader)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();

        List<TypeKoto>? list = default;
        while (reader.CanRead)
        {
            if (IsTypeClose(reader.CurrentTokenKind))
            {
                reader.TryConsumeTypeClose(out _);
                return list;
            }

            if (reader.CurrentTokenKind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }

            if (ParseType(ref reader) is not TypeKoto typeKoto)
            {
                return list;
            }

            (list ??= []).Add(typeKoto);

            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                reader.Advance();
            }
            else if (!IsTypeClose(reader.CurrentTokenKind))
            {
                reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                reader.SkipUntil(TokenKind.Comma, TokenKind.GreaterThan);
                reader.TryConsume(TokenKind.Comma);
            }
        }

        reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        return list;
    }

    private static bool IsTypeClose(TokenKind kind)
        => kind is TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan or TokenKind.GreaterThanEquals or TokenKind.GreaterThanGreaterThanEquals;
}
