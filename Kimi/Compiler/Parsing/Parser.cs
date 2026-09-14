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
public static partial class Parser
{
    private const int PrefixBindingPower = 100;
    private const int ComparisonBindingPower = 30;
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
                TokenKind.Separator, TokenKind.StartBlock, TokenKind.EndBlock, TokenKind.Else,
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

    /// <summary>Writes the qualified name of a declaration container.</summary>
    /// <param name="koto">The innermost declaration container.</param>
    /// <param name="builder">The destination builder.</param>
    public static void WriteQualifiedNameTo(DeclarationContainerKoto? koto, ref IndentedStringBuilder builder)
    {
        if (koto is null || koto.IsRoot)
        {
            return;
        }

        if (koto.Parent is DeclarationContainerKoto parent && !parent.IsRoot)
        {
            WriteQualifiedNameTo(parent, ref builder);
            builder.Append(Constants.DotChar);
        }

        builder.Append(koto.Name);
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
    /// <param name="constructor">Whether this is an init declaration.</param>
    /// <param name="specialization">Whether the generic list contains specialization arguments.</param>
    /// <returns>The parsed function, or <see langword="null"/> after an error.</returns>
    public static FunctionKoto? ParseFuncDeclaration(ref TokenReader reader, bool anonymous = false, bool constructor = false, bool specialization = false)
    {
        var context = reader.TakeContext();

        var captures = anonymous && reader.CurrentTokenKind == TokenKind.OpenBracket ? ParseCaptures(ref reader) : null;
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

            if (!methodToken.Kind.IsIdentifierOrContextualKeyword() && !(constructor && methodToken.Kind == TokenKind.Init))
            {
                reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                goto SkipAndExit;
            }

            if (constructor)
            {
                methodName = "init";
            }
            else if (!reader.TryGetIdentifier(methodToken, out methodName))
            {
                goto SkipAndExit;
            }
        }

        while (reader.TryConsume(TokenKind.Dot))
        {
            reader.Diagnostic.Add(methodToken.Span, DiagnosticCode.UnexpectedToken_Kd, "qualified function declaration");
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
            genericArguments = ParseGenericArguments(ref reader, allowLength: true, specialization: specialization);
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

            var hasType = reader.TryConsume(TokenKind.Colon, out _, !anonymous);
            if (!hasType && !anonymous)
            {
                goto Exit;
            }

            var parameterType = hasType ? ParseDeclarationType(ref reader) : new SyntaxFormKoto(ref reader, externalNameToken.Span, KotoKind.InferredType, "_", []);
            Koto? defaultValue = default;
            if (reader.TryConsume(TokenKind.Equals))
            {
                defaultValue = ParseRequiredExpression(ref reader);
            }

            if ((anonymous && (isOptional || internalName != externalName || defaultValue is not null || parameterAttribute is not null)) ||
                (specialization && (isOptional || defaultValue is not null || parameterAttribute is not null)) ||
                (isOptional && defaultValue is null))
            {
                reader.Diagnostic.Add(externalNameToken.Span, DiagnosticCode.UnexpectedToken_Kd, "parameter");
            }

            (parameters ??= new(4)).Add(new(
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
            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock or TokenKind.EqualsGreaterThan)
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
        if (anonymous && methodName.Length != 0)
        {
            functionKoto.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "named function expression");
        }

        functionKoto.IsAnonymous = anonymous;
        functionKoto.IsConstructor = constructor;
        functionKoto.IsSpecialization = specialization;
        functionKoto.SetCaptures(captures);

        if (constructor && reader.TryConsume(TokenKind.Colon))
        {
            reader.TryConsume(TokenKind.Base, out var baseSpan, true);
            reader.TryConsume(TokenKind.OpenParenthesis, out _, true);
            var arguments = ParseArgumentList(ref reader, out var labels);
            reader.TryConsume(TokenKind.CloseParenthesis, out var close, true);
            var target = new SyntaxFormKoto(ref reader, baseSpan, KotoKind.ConstructorReference, "base", []);
            functionKoto.SetBaseInitializer(new InvocationKoto(ref reader, SourceSpan.FromBounds(baseSpan.Start, Math.Max(baseSpan.End, close.End)), target, arguments, labels));
        }

        // A constructor accepts only an access modifier and an executable Block (SPEC 6.2.3, F.3).
        if (constructor && (genericArguments is not null || origins is not null || returnType is not null || reader.CurrentTokenKind == TokenKind.EqualsGreaterThan ||
            context.AttributeKoto is not null || context.ModifierKind != context.ModifierKind.ExtractAccessibilityModifiers()))
        {
            functionKoto.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "constructor header");
        }

        if (specialization && (genericArguments is null || origins is not null || context.AttributeKoto is not null || context.ModifierKind != 0))
        {
            functionKoto.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "specialization header");
        }

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
    {
        var header = ParseDeclarationContainerHeader(ref reader, true, true);
        return (header.Name, header.GenericArguments, header.Origins);
    }

    /// <summary>Parses a Declaration Container header according to the capabilities of its kind.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="supportsGenerics">Whether generic parameters are accepted.</param>
    /// <param name="supportsOrigins">Whether an Origin list is accepted.</param>
    /// <param name="declarationKind">The enclosing declaration kind.</param>
    /// <returns>The name, generic parameters, and origin names.</returns>
    internal static (string Name, List<TypeKoto>? GenericArguments, List<string>? Origins, Koto[]? Bases) ParseDeclarationContainerHeader(
        ref TokenReader reader,
        bool supportsGenerics,
        bool supportsOrigins,
        TokenKind declarationKind = TokenKind.Struct)
    {
        string name = string.Empty;
        List<TypeKoto>? genericArguments = default;
        List<string>? origins = default;
        Koto[]? bases = null;
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

        if (declarationKind is TokenKind.Struct or TokenKind.Contract && reader.TryConsume(TokenKind.Colon))
        {
            var types = default(TemporaryKotoList);
            do
            {
                types.Add(ParseDeclarationType(ref reader, parseOrigin: false));
            }
            while (reader.TryConsume(TokenKind.Comma));
            bases = types.ToArray();
            if (declarationKind == TokenKind.Struct && bases.Length != 1)
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "base clause");
            }
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
        return (name, genericArguments, origins, bases);
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
            OriginNameList? list = default;
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

                (list ??= new()).Add(reader.GetIdentifier(token), token.Span);
                if (reader.TryConsume(TokenKind.Colon))
                {// OriginParameter := Name (":" OriginBound)?; OriginBound := Name | "static" (SPEC F.3, 15.3).
                    var target = reader.CanRead ? reader.Read() : default;
                    if (!target.Kind.IsIdentifierOrContextualKeyword() || !IdentifierHelper.IsValidIdentifier(reader.GetSpan(target)))
                    {
                        reader.Diagnostic.Add(target.Kind == TokenKind.Invalid && !reader.CanRead ? originRange : target.Span, DiagnosticCode.IdentifierExpected_Kd);
                        return list;
                    }

                    list.SetLastBound(reader.GetIdentifier(target), target.Span);
                }

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
        var inferredArrayElement = false;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {
            ConsumeAttributeAndModifier(ref reader, out isEnd);
            if (isEnd)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return default;
            }

            // [N of _] may be the annotation itself or a directly nested array element (SPEC 4.3).
            reader.AllowArrayElementInference = reader.CurrentTokenKind == TokenKind.OpenBracket;
            reader.HasInferredArrayElement = false;
            try
            {
                typeKoto = ParseType(ref reader);
                inferredArrayElement = reader.HasInferredArrayElement;
            }
            finally
            {
                reader.AllowArrayElementInference = false;
                reader.HasInferredArrayElement = false;
            }
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out _, false))
        {
            initializerKoto = ParseRequiredExpression(ref reader);
        }

        reader.RestoreContext(variableContext);

        var fieldKoto = new FieldKoto(ref reader, token, nameKoto, typeKoto, initializerKoto);
        if (inferredArrayElement && initializerKoto is null)
        {
            fieldKoto.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

        if (!allowParenthesizedTerminator ||
            reader.CurrentTokenKind is not (TokenKind.CloseParenthesis or TokenKind.Yield))
        {
            reader.SkipUntil(TokenKind.EndBlock, TokenKind.Separator, DiagnosticCode.UnexpectedTrailingToken_Kd);
        }

        return fieldKoto;
    }

    /// <summary>Parses a Property declaration and its optional accessor list.</summary>
    /// <param name="reader">The token reader positioned after the declaration keyword.</param>
    /// <param name="token">The Property declaration keyword token.</param>
    /// <returns>The parsed Property, or <see langword="null"/> after an error.</returns>
    public static PropertyKoto? ParseProperty(ref TokenReader reader, ref Token token)
    {
        var propertyContext = reader.TakeContext();
        while (reader.CurrentTokenKind == TokenKind.Sharp)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "attribute placement");
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
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "attribute placement");
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

        if (property.Modifier != property.Modifier.ExtractAccessibilityModifiers())
        {
            property.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "property declaration modifier");
        }

        if (property.DeclarationKind is PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement)
        {
            if (typeKoto is null || initializerKoto is not null)
            {
                property.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "property type or initializer");
            }
        }
        else if (typeKoto is null && initializerKoto is null)
        {
            property.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

        if (hasInlineAccessors)
        {
            if (!property.IsContractRequirement)
            {
                property.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "has is only permitted on property requirements");
            }

            ParseInlinePropertyAccessors(ref reader, property);
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
        }
        else if (reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock) && reader.CanRead)
        {
            reader.SkipUntil(TokenKind.EndBlock, TokenKind.Separator, DiagnosticCode.UnexpectedTrailingToken_Kd);
        }

        if (property.DeclarationKind is PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement &&
            property.GetAccessor(PropertyAccessorKind.Get) is null)
        {
            property.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
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
            if (modifier != ModifierKind.NoModifier)
            {
                reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.UnexpectedToken_Kd, "requirement accessor accessibility");
            }

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

            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.EndBlock)
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
            var hasSignature = reader.CurrentTokenKind == TokenKind.OpenParenthesis;
            Koto? receiverType = null;
            Koto? valueType = null;
            var signatureEnd = accessorToken.Span.End;
            if (hasSignature)
            {
                ParseAccessorParameters(ref reader, accessorKind, out receiverType, out valueType, out signatureEnd);
            }

            var returnType = ParseAccessorReturnType(ref reader);
            if (hasSignature != (returnType is not null))
            {
                reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.UnexpectedToken_Kd, "accessor requires a parameter list and result type");
            }

            if (accessorKind == PropertyAccessorKind.Set && returnType is not null &&
                returnType is not TupleTypeKoto { ElementNodes.Count: 0 })
            {
                reader.Diagnostic.Add(returnType.Span, DiagnosticCode.UnexpectedToken_Kd, "setter result must be ()");
            }

            Koto? body = default;
            if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan)
            {
                body = ParseSingleBodyItem(ref reader);
            }
            else if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
            {
                body = ParseBlock(ref reader);
            }

            if (property.IsContractRequirement)
            {
                if (!hasSignature || body is not null || modifier != ModifierKind.NoModifier)
                {
                    reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.UnexpectedToken_Kd, "bodyless requirement signature");
                }
            }
            else if (hasSignature != (body is not null) ||
                (property.DeclarationKind == PropertyDeclarationKind.Computed && body is null))
            {
                reader.Diagnostic.Add(accessorToken.Span, DiagnosticCode.UnexpectedToken_Kd, "custom accessor requires an explicit signature and body");
            }

            var end = Math.Max(Math.Max(accessorToken.Span.End, signatureEnd), Math.Max(returnType?.Span.End ?? 0, body?.Span.End ?? 0));
            var accessor = new PropertyAccessorKoto(
                ref reader,
                SourceSpan.FromBounds(start, end),
                modifier,
                accessorKind,
                body,
                returnType,
                hasSignature,
                receiverType,
                valueType);
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
            if (!reader.TryConsume(TokenKind.Colon))
            {
                reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, ":");
                goto CloseParameters;
            }

            receiverType = ParseDeclarationType(ref reader);
            end = Math.Max(end, receiverType.Span.End);
            if (kind == PropertyAccessorKind.Set)
            {
                if (!reader.TryConsume(TokenKind.Comma))
                {
                    reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, ",");
                    goto CloseParameters;
                }
            }
        }

        if (kind == PropertyAccessorKind.Set)
        {
            if (reader.IsCurrentIdentifier("value"))
            {
                reader.Advance();
                if (!reader.TryConsume(TokenKind.Colon))
                {
                    reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, ":");
                    goto CloseParameters;
                }

                valueType = ParseDeclarationType(ref reader);
                end = Math.Max(end, valueType.Span.End);
            }
            else
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "setter value parameter");
            }
        }

CloseParameters:
        if (reader.CurrentTokenKind != TokenKind.CloseParenthesis)
        {
            reader.SkipUntil(TokenKind.CloseParenthesis, TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedToken_Kd);
        }

        if (reader.TryConsume(TokenKind.CloseParenthesis, out var close, false))
        {
            end = close.End;
        }
        else
        {
            reader.AddDiagnostic(DiagnosticCode.TokenMismatch_Kd, ")");
        }
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
            return new ErrorKoto(ref reader, arrow);
        }

        return ParseDeclarationType(ref reader);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ModifierKind GetAccessibilityModifier(TokenKind tokenKind)
        => tokenKind switch
        {
            TokenKind.Public => ModifierKind.Public,
            TokenKind.Protected => ModifierKind.Protected,
            TokenKind.Private => ModifierKind.Private,
            TokenKind.Internal => ModifierKind.Internal,
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
        if (property.DeclarationKind == PropertyDeclarationKind.Let && accessor.AccessorKind == PropertyAccessorKind.Set)
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

        if (kind.HasFlag(ModifierKind.Unsafe))
        {
            builder.EnsureTrailingSpace();
            builder.Append(Constants.UnsafeKeyword);
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
            ModifierKind.ProtectedOrInternal => "protected internal",
            ModifierKind.ProtectedAndInternal => "private protected",
            _ => string.Empty,
        };

        if ((kind & (ModifierKind.Static | ModifierKind.Open | ModifierKind.Unsafe)) == 0)
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
            if (tokenKind == TokenKind.Identifier && reader.PeekKind(1) == TokenKind.Func && reader.IsCurrentIdentifier(Constants.UnsafeKeyword))
            {
                ReadFlag(ref reader, ModifierKind.Unsafe);
                continue;
            }

            switch (tokenKind)
            {
                case TokenKind.Separator:
                    reader.Advance();
                    continue;

                case TokenKind.Static:
                    // static is not a declaration modifier; it is kept only for recovery (SPEC 6.1).
                    if (!reader.IsExcluded)
                    {
                        reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, Constants.StaticKeyword);
                    }

                    ReadFlag(ref reader, ModifierKind.Static);
                    continue;

                case TokenKind.Open:
                    ReadFlag(ref reader, ModifierKind.Open);
                    continue;

                case TokenKind.Public:
                case TokenKind.Protected:
                case TokenKind.Private:
                case TokenKind.Internal:
                    ReadAccessibility(ref reader, GetAccessibilityModifier(tokenKind));
                    continue;

                case TokenKind.Sharp:
                    if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.If)
                    {
                        ParseCompileTimeIfPrefix(ref reader);
                        reader.HasCompileTimeIfPrefix = true;
                        continue;
                    }

                    if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.Switch)
                    {
                        isEnd = false;
                        return;
                    }

                    if (reader.PeekKind(1) == TokenKind.Case)
                    {
                        reader.AddDiagnostic(DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd);
                        SkipExcludedSyntaxCore(ref reader);
                        reader.ClearContext();
                        continue;
                    }

                    _ = ParseAttributeKoto(ref reader);
                    continue;

                default:
                    if (reader.ModifierKind.HasFlag(ModifierKind.Open) && tokenKind != TokenKind.Struct && !reader.IsExcluded)
                    {
                        // open applies only to structures (SPEC 6.2.2, F.3).
                        reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, Constants.OpenKeyword);
                    }

                    isEnd = false;
                    return;
            }
        }

        if (reader.HasCompileTimeIfPrefix)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

        if (reader.AttributeKoto is not null)
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
            else if ((acc == ModifierKind.Protected && kind == ModifierKind.Internal) || (acc == ModifierKind.Private && kind == ModifierKind.Protected))
            {
                reader.ModifierKind = (reader.ModifierKind & ~(ModifierKind)CompilerHelper.AccessibilityModifierMask) |
                    (acc == ModifierKind.Protected ? ModifierKind.ProtectedOrInternal : ModifierKind.ProtectedAndInternal);
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

    private static Koto ParseType(ref TokenReader reader, bool parseOrigin, bool disambiguateGenerics = false, bool allowNestedOrigins = true)
    {
        var start = reader.CurrentTokenRange.Start;
        var left = ParseTypeInternal(ref reader, disambiguateGenerics, allowNestedOrigins);
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

                var accessor = ParseTypeInternal(ref reader, disambiguateGenerics, allowNestedOrigins) ?? reader.NewErrorKoto();
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
                if (disambiguateGenerics && !HasAdjacentGenericArguments(ref reader, left.Span))
                {
                    break;
                }

                left = ParseGenericsPostfix(ref reader, left, allowNestedOrigins);
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

        static Koto? ParseTypeInternal(ref TokenReader reader, bool disambiguateGenerics, bool allowNestedOrigins)
        {
            if (reader.IsCurrentIdentifier("_"))
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "_");
            }

            if (reader.CurrentTokenKind == TokenKind.ColonColon)
            {
                return ParseRootName(ref reader, true);
            }

            if (reader.CurrentTokenKind == TokenKind.OpenBracket)
            {
                return ParseFixedArrayType(ref reader, allowNestedOrigins);
            }

            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.EndBlock or TokenKind.StartBlock or TokenKind.Comma or TokenKind.CloseParenthesis or TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan or TokenKind.Equals)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return null;
            }

            if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
            {
                return ParseDeclarationType(ref reader, parseOrigin: false, parseFunctionType: false, allowNestedOrigins: allowNestedOrigins);
            }

            var token = reader.CurrentToken;
            reader.Advance();

            if (token.Kind.IsIdentifierOrContextualKeyword() &&
                reader.CurrentTokenKind == TokenKind.Slash)
            {// Semantics applies to the next type head; arrows and this layer's Origin remain outside.
                var semantics = reader.GetSpan(token);
                string? semanticsParameter = default;
                if (!CompilerHelper.TryParse(semantics, out var semanticsKind))
                {
                    semanticsParameter = reader.GetIdentifier(token);
                }

                reader.Advance();

                var attribute = reader.PopAttribute();
                var type = ParseType(ref reader, parseOrigin: false, disambiguateGenerics: disambiguateGenerics, allowNestedOrigins: allowNestedOrigins);
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
                return result;
            }

            if (token.Kind.IsPrimitiveType() || token.Kind.IsIdentifierOrContextualKeyword() || token.Kind == TokenKind.Self)
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
    private static GenericsKoto ParseGenericsPostfix(ref TokenReader reader, Koto left, bool allowOrigins = true)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();
        var typeList = default(TemporaryKotoList);
        var end = reader.CurrentTokenRange.End;
        reader.TrySkipSeparatorsTo(TokenKind.GreaterThan);
        while (true)
        {
            var type = ParseTypeArgument(ref reader, allowOrigins);
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

        var nameToken = reader.CurrentToken;
        if (!nameToken.Kind.IsIdentifierOrContextualKeyword() || !UnicodeIdentifierHelper.IsUppercase(reader.GetSpan(nameToken)))
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "Attribute name");
        }

        var operand = ParsePrimaryExpression(ref reader);
        if (reader.CurrentTokenKind is TokenKind.Dot or TokenKind.LessThan or TokenKind.OpenBracket)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "Attribute arguments");
        }

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
        if (reader.IsExcluded)
        {
            reader.Advance(2);
            SkipCompileTimeHeaderRemainder(ref reader);
            return;
        }

        var attributes = reader.PopAttribute();
        reader.Advance();
        _ = reader.TryConsume(TokenKind.If, out _, true);
        var condition = ParseRequiredCompileTimeCondition(ref reader);
        var invalidHeader = reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock);
        if (invalidHeader)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedTrailingToken_Kd);
            SkipCompileTimeHeaderRemainder(ref reader);
        }

        if (attributes is not null)
        {
            reader.PushAttribute(attributes);
        }

        var result = CompileTimeConditionEvaluator.Evaluate(reader.CodeContext.Compilation, condition);
        if (invalidHeader || result != CompileTimeConditionResult.True)
        {
            reader.IsExcluded = true;
        }
    }

    /// <summary>Parses the arms in one explicit compile-time <c>#switch</c> body.</summary>
    /// <param name="reader">The token reader positioned at <c>#switch</c>.</param>
    /// <param name="declarationContext">The enclosing Declaration Container, when applicable.</param>
    /// <returns>The selected body or an invalid Case Group retained for error recovery.</returns>
    internal static Koto ParseCompileTimeSwitch(ref TokenReader reader, DeclarationContainerKoto? declarationContext = null)
    {
        var context = reader.TakeContext();
        var header = reader.CurrentTokenRange;
        var groupStart = reader.CurrentTokenRange.Start;
        reader.Advance(2); // #switch has no subject or condition on its header.
        var groupEnd = reader.CurrentTokenRange.Start;
        var arms = new List<CompileTimeCaseArmKoto>();
        var selectedIndex = -1;
        var invalidCondition = false;
        var fallbackSeen = false;
        var fallbackMustBeLastReported = false;
        var fallbackSpan = default(SourceSpan);
        var invalidSyntax = false;

        if (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedTrailingToken_Kd);
            SkipCompileTimeHeaderRemainder(ref reader);
            invalidSyntax = true;
        }

        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.Diagnostic.Add(header, DiagnosticCode.EmptyCompileTimeSwitch_Kd);
            reader.RestoreContext(context);
            return new CompileTimeSwitchKoto(ref reader, SourceSpan.FromBounds(groupStart, groupEnd), arms);
        }

        reader.Advance(); // The #switch arm list is not a new lookup scope.
        var closed = false;

        while (reader.CanRead)
        {
            reader.SkipSeparators();
            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                groupEnd = reader.Read().Span.End;
                closed = true;
                break;
            }

            if (!reader.CanRead)
            {
                break;
            }

            if (!IsCompileTimeCaseStart(ref reader))
            {
                reader.AddDiagnostic(DiagnosticCode.InvalidCompileTimeSwitchItem_Kd);
                invalidSyntax = true;
                SkipCompileTimeHeaderRemainder(ref reader);
                if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    reader.SkipCurrentBlock(false);
                }

                continue;
            }

            if (fallbackSeen && !fallbackMustBeLastReported)
            {
                reader.Diagnostic.Add(fallbackSpan, DiagnosticCode.CompileTimeCaseFallbackMustBeLast_Kd);
                fallbackMustBeLastReported = true;
                invalidSyntax = true;
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

            if (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedTrailingToken_Kd);
                SkipCompileTimeHeaderRemainder(ref reader);
                invalidSyntax = true;
            }

            var body = declarationContext is null || declarationContext.IsRoot
                ? ParseRequiredBlock(ref reader)
                : ParseDeclarationDirectiveBody(ref reader, declarationContext);
            arms.Add(new CompileTimeCaseArmKoto(condition, body));
            invalidCondition |= result == CompileTimeConditionResult.Error;
            if (selectedIndex < 0 && result == CompileTimeConditionResult.True)
            {
                selectedIndex = arms.Count - 1;
            }

            groupEnd = Math.Max(groupEnd, body.Span.End);
        }

        if (!closed)
        {
            reader.Diagnostic.Add(header, DiagnosticCode.IncompleteSyntax_Kd);
            invalidSyntax = true;
        }

        if (arms.Count == 0)
        {
            reader.Diagnostic.Add(header, DiagnosticCode.EmptyCompileTimeSwitch_Kd);
            invalidSyntax = true;
        }

        reader.RestoreContext(context);
        if (selectedIndex >= 0 && !invalidSyntax && !invalidCondition)
        {
            var selectedBody = arms[selectedIndex].Body;
            selectedBody.SetAttributeChain(reader.PopAttribute());
            return selectedBody;
        }

        var group = new CompileTimeSwitchKoto(
            ref reader,
            SourceSpan.FromBounds(groupStart, groupEnd),
            arms);
        if (!invalidSyntax && !invalidCondition && !fallbackSeen)
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
        var block = new CodeBlockKoto(ref reader, SourceSpan.FromBounds(start, reader.CurrentTokenRange.Start), items)
        {
            DeclarationContext = declarationContext.TokenKind,
        };
        return block;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsCompileTimeSwitchStart(ref TokenReader reader)
        => reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) == TokenKind.Switch;

    private static bool IsCompileTimeCaseStart(ref TokenReader reader)
        => reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) == TokenKind.Case;

    private static void SkipCompileTimeHeaderRemainder(ref TokenReader reader)
    {
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
        {
            reader.Advance();
        }
    }

    /// <summary>Consumes one syntax node controlled by an early-false directive without constructing Koto nodes.</summary>
    /// <param name="reader">The token reader positioned at the controlled syntax.</param>
    /// <param name="executableContext">Whether a directive's body is executable.</param>
    internal static void SkipExcludedSyntax(ref TokenReader reader, bool executableContext = false)
    {
        var source = reader;
        SkipExcludedSyntaxCore(ref reader);
        ValidateExcludedBodyStructure(ref source, reader.Position, executableContext, HasLibraryImport(source.AttributeKoto));
    }

    private static void SkipExcludedSyntaxCore(ref TokenReader reader)
    {
        if (IsCompileTimeSwitchStart(ref reader) || IsCompileTimeCaseStart(ref reader))
        {
            reader.Advance(2);
            SkipCompileTimeHeaderRemainder(ref reader);
            if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
            {
                reader.SkipCurrentBlock(false);
            }

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
            SkipExcludedSyntaxCore(ref reader);
        }
    }

    // Inspect only source structure in excluded syntax. Do not bind, evaluate conditions,
    // construct Koto nodes, or infer emptiness from the selected tree.
    private static void ValidateExcludedBodyStructure(
        ref TokenReader reader,
        int end,
        bool executableContext,
        bool importedDeclaration = false,
        bool isSwitchBody = false,
        SourceSpan switchHeader = default)
    {
        var caseCount = 0;
        while (reader.Position < end && reader.CanRead && reader.CurrentTokenKind != TokenKind.EndBlock)
        {
            if (reader.CurrentTokenKind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }

            var header = reader.CurrentTokenRange;
            var first = reader.CurrentTokenKind;
            var isCase = IsCompileTimeCaseStart(ref reader);
            var isSwitchHeader = IsCompileTimeSwitchStart(ref reader);
            if (isSwitchBody)
            {
                if (isCase)
                {
                    caseCount++;
                }
                else
                {
                    reader.AddDiagnostic(DiagnosticCode.InvalidCompileTimeSwitchItem_Kd);
                }
            }
            else if (isCase)
            {
                reader.AddDiagnostic(DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd);
            }

            var directive = first == TokenKind.Sharp && reader.PeekKind(1) is TokenKind.If or TokenKind.Case or TokenKind.Switch;
            var prefix = directive && reader.PeekKind(1) == TokenKind.If;
            var bodyIsExecutable = executableContext;
            var requiresBody = directive && !prefix;
            var hasAssignment = false;
            var hasArrow = false;
            var hasFunction = false;
            var hasContainer = false;
            var last = TokenKind.Invalid;
            if (first != TokenKind.StartBlock)
            {
                while (reader.Position < end && reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
                {
                    var kind = reader.CurrentTokenKind;
                    if (last == TokenKind.Sharp && reader.IsCurrentIdentifier("LibraryImport"))
                    {
                        importedDeclaration = true;
                    }

                    hasFunction |= kind == TokenKind.Func;
                    hasContainer |= kind is TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract;
                    if ((!directive && kind is TokenKind.If or TokenKind.Else or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Func or TokenKind.Do or TokenKind.Defer) ||
                        (!directive && reader.IsCurrentIdentifier(Constants.UnsafeKeyword) && reader.PeekKind(1) != TokenKind.Slash))
                    {
                        requiresBody = true;
                        bodyIsExecutable = true;
                    }

                    if (kind is TokenKind.Match or TokenKind.Switch)
                    {
                        // The arm list is not itself an executable Block.
                        bodyIsExecutable = false;
                    }

                    hasAssignment |= kind == TokenKind.Equals;
                    hasArrow |= kind == TokenKind.EqualsGreaterThan;
                    last = kind;
                    reader.Advance();
                }

                if (hasContainer ||
                    (first is TokenKind.Let or TokenKind.Var && !hasAssignment))
                {
                    bodyIsExecutable = false;
                }
                else if (first is TokenKind.Get or TokenKind.Set)
                {
                    bodyIsExecutable = true;
                }

                if ((hasArrow && last is not (TokenKind.EqualsGreaterThan or TokenKind.Else)) || (hasFunction && importedDeclaration))
                {
                    requiresBody = false; // A complete Expression body on the header line.
                }

                if (last == TokenKind.EqualsGreaterThan ||
                    (last == TokenKind.Colon && (first is not (TokenKind.Let or TokenKind.Var) || hasAssignment)))
                {
                    requiresBody = true;
                    bodyIsExecutable = true;
                }

                if (directive && last is TokenKind.If or TokenKind.Case)
                {
                    reader.Diagnostic.Add(header, DiagnosticCode.IncompleteSyntax_Kd);
                }

                if (isSwitchHeader && last != TokenKind.Switch)
                {
                    reader.Diagnostic.Add(header, DiagnosticCode.UnexpectedTrailingToken_Kd);
                }

                reader.SkipSeparators();
            }

            if (reader.Position < end && reader.CurrentTokenKind == TokenKind.StartBlock)
            {
                reader.Advance();
                while (reader.Position < end && reader.CurrentTokenKind == TokenKind.Separator)
                {
                    reader.Advance();
                }

                if (bodyIsExecutable && (reader.Position >= end || !reader.CanRead || reader.CurrentTokenKind == TokenKind.EndBlock))
                {
                    reader.Diagnostic.Add(header, DiagnosticCode.EmptyExecutableBlock_Kd);
                }

                ValidateExcludedBodyStructure(
                    ref reader,
                    end,
                    isSwitchHeader ? executableContext : bodyIsExecutable,
                    isSwitchBody: isSwitchHeader,
                    switchHeader: header);
                reader.TryConsume(TokenKind.EndBlock);
            }
            else if (requiresBody)
            {
                reader.Diagnostic.Add(header, isSwitchHeader ? DiagnosticCode.EmptyCompileTimeSwitch_Kd : DiagnosticCode.EmptyExecutableBlock_Kd);
            }
            else if (prefix && (reader.Position >= end || !reader.CanRead || reader.CurrentTokenKind == TokenKind.EndBlock))
            {
                reader.Diagnostic.Add(header, DiagnosticCode.IncompleteSyntax_Kd);
            }

            if (hasFunction || first != TokenKind.Sharp)
            {
                importedDeclaration = false;
            }
        }

        if (isSwitchBody && caseCount == 0)
        {
            reader.Diagnostic.Add(switchHeader, DiagnosticCode.EmptyCompileTimeSwitch_Kd);
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
    /// <param name="finishLine">Whether to diagnose and consume trailing tokens on the clause's line.</param>
    /// <returns>The parsed constraint, or <see langword="null"/> when its required prefix is invalid.</returns>
    public static IsKoto? ParseTypeConstraint(ref TokenReader reader, bool finishLine = true)
    {
        var parsesSemantics = reader.IsCurrentIdentifier(Constants.SemanticsKeyword);
        var subject = ParseConstraintSubject(ref reader);

        if (!reader.TryConsume(TokenKind.Is, out var isRange, true))
        {
            return null;
        }

        var condition = ParseCondition(ref reader, parsesSemantics);
        var constraint = new IsKoto(ref reader, SourceSpan.FromBounds(subject.Span.Start, Math.Max(isRange.End, condition.Span.End)), subject, condition);

        if (finishLine)
        {
            reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
        }

        return constraint;

        static Koto ParseCondition(ref TokenReader reader, bool parsesSemantics)
        {
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                var notToken = reader.Read();
                return KotoHelper.NewUnaryKoto(ref reader, notToken, ParseOr(ref reader, parsesSemantics));
            }

            return ParseOr(ref reader, parsesSemantics);
        }

        static Koto ParseOr(ref TokenReader reader, bool parsesSemantics)
        {
            var left = ParseAnd(ref reader, parsesSemantics);
            while (reader.CurrentTokenKind == TokenKind.Or)
            {
                var token = reader.Read();
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, ParseAnd(ref reader, parsesSemantics));
            }

            return left;
        }

        static Koto ParseAnd(ref TokenReader reader, bool parsesSemantics)
        {
            var left = ParsePrimary(ref reader, parsesSemantics);
            while (reader.CurrentTokenKind == TokenKind.And)
            {
                var token = reader.Read();
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, ParsePrimary(ref reader, parsesSemantics));
            }

            return left;
        }

        static Koto ParsePrimary(ref TokenReader reader, bool parsesSemantics)
        {
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                var token = reader.Read();
                return KotoHelper.NewUnaryKoto(ref reader, token, ParsePrimary(ref reader, parsesSemantics));
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

            if (parsesSemantics)
            {
                var token2 = reader.Read();
                var text = reader.GetSpan(token2);
                if (token2.Kind == TokenKind.Identifier &&
                    SemanticsMaskHelper.TryParse(text, out var mask))
                {
                    return new SemanticsMaskKoto(ref reader, token2.Span, mask);
                }

                reader.Diagnostic.Add(token2.Span, DiagnosticCode.InvalidSemanticsConstraint_Kd, text.ToString());
                return new ErrorKoto(ref reader, token2.Span);
            }

            if (reader.CurrentTokenKind.IsPrimitiveType() || reader.PeekKind(1) == TokenKind.Slash || reader.CurrentTokenKind is TokenKind.OpenBracket or TokenKind.Self)
            {
                return ParseDeclarationType(ref reader);
            }

            var name = ParseConstraintSubject(ref reader);
            return reader.CurrentTokenKind == TokenKind.LessThan ? ParseGenericsPostfix(ref reader, name) : name;
        }
    }

    /// <summary>
    /// Determines whether the reader is positioned at the start of a type constraint.
    /// </summary>
    /// <param name="reader">The token reader to inspect.</param>
    /// <returns><see langword="true"/> for an identifier followed by <c>is</c>.</returns>
    public static bool IsTypeConstraintStart(ref TokenReader reader)
    {
        var offset = reader.CurrentTokenKind == TokenKind.ColonColon ? 1 : 0;
        if (!reader.PeekKind(offset).IsIdentifierOrContextualKeyword() && reader.PeekKind(offset) != TokenKind.Self)
        {
            return false;
        }

        offset++;
        while (reader.PeekKind(offset) == TokenKind.Dot && reader.PeekKind(offset + 1).IsIdentifierOrContextualKeyword())
        {
            offset += 2;
        }

        return reader.PeekKind(offset) == TokenKind.Is;
    }

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
            reader.AddDiagnostic(DiagnosticCode.EmptyExecutableBlock_Kd);
            return new CodeBlockKoto(ref reader, start, []);
        }

        var blockContext = reader.TakeContext();
        reader.Advance();
        var items = default(TemporaryKotoList);
        var seenExecutableItem = false;
        var hasSourceItem = false;

        while (reader.CanRead)
        {
            reader.SkipSeparators();

            if (!reader.CanRead)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                if (!hasSourceItem)
                {
                    reader.Diagnostic.Add(start, DiagnosticCode.EmptyExecutableBlock_Kd);
                }

                var end = reader.CurrentTokenRange.End;
                reader.Advance();
                reader.RestoreContext(blockContext);
                return new CodeBlockKoto(
                    ref reader,
                    SourceSpan.FromBounds(start.Start, end),
                    items.ToArray());
            }

            ConsumeAttributeAndModifier(ref reader, out var isEnd, allowCompileTimeDirectives: true);
            if (isEnd)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                if (reader.HasCompileTimeIfPrefix || reader.AttributeKoto is not null || reader.ModifierKind != ModifierKind.NoModifier)
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                continue;
            }

            // Count source syntax before early conditional selection can discard its nodes.
            hasSourceItem = true;
            var isExcluded = reader.IsExcluded;
            if (isExcluded)
            {
                SkipExcludedSyntax(ref reader, executableContext: true);
                continue;
            }

            if (IsCompileTimeSwitchStart(ref reader))
            {
                var caseGroup = ParseCompileTimeSwitch(ref reader);
                AddSelectedItems(ref items, caseGroup);
                seenExecutableItem = true;
                continue;
            }

            if (function is not null && function.GenericArguments.Count > 0 && IsTypeConstraintStart(ref reader))
            {
                // A root-qualified subject is never a generic parameter; do not read "::" as an identifier.
                var rootQualified = reader.CurrentTokenKind == TokenKind.ColonColon;
                var subject = rootQualified ? null : reader.GetIdentifier(reader.CurrentToken);
                var isGenericParameter = subject is not null && function.IsGenericParameter(subject);
                if (!seenExecutableItem || isGenericParameter)
                {
                    if (seenExecutableItem || !isGenericParameter)
                    {
                        reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.UnexpectedToken_Kd, subject ?? "::");
                    }

                    var constraint = ParseTypeConstraint(ref reader);
                    if (constraint is not null)
                    {
                        function.AddTypeConstraint(constraint);
                    }

                    continue;
                }
            }

            seenExecutableItem = true;
            var oldPosition = reader.Position;
            var directiveBlock = reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.StartBlock;
            var item = directiveBlock
                ? ParseBlock(ref reader) : ParseBlockItem(ref reader);
            if (item is not null)
            {
                if (directiveBlock)
                {
                    AddSelectedItems(ref items, item);
                }
                else
                {
                    items.Add(item);
                }
            }

            if (reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock))
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
            items.ToArray());
    }

    private static void AddSelectedItems(ref TemporaryKotoList items, Koto selected)
    {
        if (selected is CodeBlockKoto block)
        {
            for (var i = 0; i < block.Items.Count; i++)
            {
                items.Add(block.Items[i]);
            }
        }
        else
        {
            items.Add(selected); // Invalid switches retain recovery syntax and diagnostics.
        }
    }

    internal static Koto? ParseBlockItem(ref TokenReader reader)
    {
        if (reader.AttributeKoto is not null && (reader.CurrentTokenKind != TokenKind.Func || reader.PeekKind(1) is TokenKind.OpenParenthesis or TokenKind.OpenBracket))
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "attribute placement");
        }

        if (reader.IsCurrentIdentifier("specialize") && reader.PeekKind(1) == TokenKind.Func)
        {
            return ParseSpecialization(ref reader);
        }

        if (reader.CurrentTokenKind == TokenKind.Require)
        {
            return ParseRequire(ref reader);
        }

        if (IsBlockStatementStart(ref reader))
        {
            return ParseBlockStatement(ref reader);
        }

        var token = reader.CurrentToken;
        switch (token.Kind)
        {
            case TokenKind.Let:
            case TokenKind.Var:
                reader.Advance();
                return ParseField(ref reader, token, false);

            case TokenKind.Func:
                if (reader.PeekKind(1) is TokenKind.OpenParenthesis or TokenKind.OpenBracket)
                {
                    return ParseExpression(ref reader);
                }

                reader.Advance();
                var function = ParseFuncDeclaration(ref reader);
                if (function is null)
                {
                    return null;
                }

                ParseNamedFunctionBody(ref reader, function);
                return function;

            case TokenKind.Group:
            case TokenKind.Struct:
            case TokenKind.Enum:
            case TokenKind.Extension:
            case TokenKind.Contract:
                reader.Advance();
                var supportsGenericHeader = token.Kind is TokenKind.Struct or TokenKind.Enum;
                if (token.Kind == TokenKind.Extension)
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, "extension");
                }

                var declaration = ParseDeclarationContainerHeader(
                    ref reader,
                    supportsGenericHeader,
                    supportsGenericHeader,
                    token.Kind);
                var state = reader.TakeContext();
                var container = DeclarationContainerKoto.CreateStandalone(
                    reader.CodeContext,
                    token.Kind,
                    state,
                    token.Span,
                    declaration.Name);
                container.AddHeader(declaration.GenericArguments, declaration.Origins);
                container.SetBases(declaration.Bases);

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    container.Parse(ref reader);
                }
                else if (token.Kind == TokenKind.Enum)
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                return container;

            default:
                return ParseExpression(ref reader);
        }
    }

    /// <summary>Parses a named function body, allowing bodyless imported declarations.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="function">The parsed declaration.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ParseNamedFunctionBody(ref TokenReader reader, FunctionKoto function)
    {
        if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan || reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            function.Parse(ref reader);
            return;
        }

        if (function.ReturnType is ErrorKoto)
        {
            return; // Avoid cascading diagnostics after an incomplete signature.
        }

        if (!HasLibraryImport(function.AttributeChain))
        {
            reader.Diagnostic.Add(function.Span, DiagnosticCode.EmptyExecutableBlock_Kd);
        }
    }

    internal static bool HasLibraryImport(AttributeKoto? attribute)
    {
        for (; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (attribute.IdentifierKoto is IdentifierNameKoto { IdentifierName: "LibraryImport" })
            {
                return true; // An imported declaration has no executable body in source.
            }
        }

        return false;
    }

    // unsafe is contextual: it introduces a statement only before a Body (or an obsolete body colon/missing body
    // at statement start). A following operator, call, index, or member access keeps it an ordinary Name (SPEC 2.5.1).
    private static bool IsBlockStatementStart(ref TokenReader reader, bool expressionPosition = false)
        => reader.CurrentTokenKind == TokenKind.Defer ||
            (reader.IsCurrentIdentifier(Constants.UnsafeKeyword) &&
                (reader.PeekKind(1) is TokenKind.EqualsGreaterThan or TokenKind.StartBlock or TokenKind.Colon ||
                    (reader.PeekKind(1) == TokenKind.Separator && reader.PeekKind(2) == TokenKind.StartBlock) ||
                    (!expressionPosition && reader.PeekKind(1) is TokenKind.Separator or TokenKind.EndBlock or TokenKind.Invalid)));

    private static BlockStatementKoto ParseBlockStatement(ref TokenReader reader)
    {
        var isUnsafe = reader.IsCurrentIdentifier(Constants.UnsafeKeyword);
        var header = reader.Read();
        var context = reader.TakeContext();
        var body = ParseRequiredBody(ref reader);
        reader.RestoreContext(context);
        var span = SourceSpan.FromBounds(header.Span.Start, Math.Max(header.Span.End, body.Span.End));
        return isUnsafe
            ? new UnsafeBlockKoto(ref reader, span, body, body.IsExpressionBody)
            : new DeferredBlockKoto(ref reader, span, body, body.IsExpressionBody);
    }

    private static IfKoto ParseIfExpression(ref TokenReader reader)
    {
        if (reader.IfBodyRegion)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "group the nested if expression");
        }

        var ifToken = reader.Read();
        var branches = new List<ConditionalBranchKoto>(2);
        CodeBlockKoto? elseBody = null;
        int end;

        while (true)
        {
            var condition = ParseHeaderExpression(ref reader);
            var body = ParseRequiredBody(ref reader, true);
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

            elseBody = ParseRequiredBody(ref reader, true);
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
        var condition = ParseHeaderExpression(ref reader);
        var body = ParseRequiredBody(ref reader);
        return new WhileKoto(
            ref reader,
            SourceSpan.FromBounds(token.Span.Start, body.Span.End),
            condition,
            body);
    }

    private static LoopKoto ParseLoopExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var body = ParseRequiredBody(ref reader);
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

        var iterable = ParseHeaderExpression(ref reader);
        var body = ParseRequiredBody(ref reader);
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
                // An empty list or a trailing comma is not a ForBinding form (SPEC F.5).
                if (expectsBinding)
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
        var expression = ParseHeaderExpression(ref reader);
        var arms = new List<MatchArmKoto>(4);
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
            var pattern = ParsePattern(ref reader);
            var region = (reader.SingleBodyRegion, reader.IfBodyRegion);
            reader.SingleBodyRegion = reader.IfBodyRegion = false;
            var guard = reader.TryConsume(TokenKind.If) ? ParseHeaderExpression(ref reader) : null;
            var parsedBody = ParseRequiredBody(ref reader);
            Koto body = parsedBody.IsExpressionBody ? parsedBody.Items[0] : parsedBody;
            arms.Add(new MatchArmKoto(pattern, body) { Guard = guard });
            end = body.Span.End;
            (reader.SingleBodyRegion, reader.IfBodyRegion) = region;

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
        var end = token.Span.End;
        string? label = null;
        Koto? expression = null;
        var sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
        if (sameLine && token.Kind != TokenKind.Return && reader.IsCurrentIdentifier("to"))
        {
            reader.Advance();
            if (!reader.SameLine(end, reader.CurrentTokenRange.Start))
            {
                reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
            }

            label = ParseTransferLabel(ref reader, ref end);
            if (token.Kind != TokenKind.Continue && reader.TryConsume(TokenKind.Colon))
            {
                if (!reader.SameLine(token.Span.End, reader.CurrentTokenRange.Start))
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                expression = ParseRequiredTransferOperand(ref reader);
                end = expression.Span.End;
            }
        }
        else if (sameLine && token.Kind != TokenKind.Continue && !IsExpressionBoundary(ref reader))
        {
            expression = ParseRequiredTransferOperand(ref reader);
            end = expression.Span.End;
        }

        var range = SourceSpan.FromBounds(token.Span.Start, end);
        return token.Kind switch
        {
            TokenKind.Return => new ReturnKoto(ref reader, range, expression),
            TokenKind.Exit => new ExitKoto(ref reader, range, expression, label),
            TokenKind.Yield => new YieldKoto(ref reader, range, expression, label),
            TokenKind.Continue => new ContinueKoto(ref reader, range, label),
            _ => throw new InvalidOperationException(),
        };
    }

    private static Koto ParseRequiredTransferOperand(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.Colon)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "parenthesize a labeled transfer operand");
        }

        return ParseRequiredExpression(ref reader);
    }

    /// <summary>Reports a labeled expression whose colon conflicts with an argument name or dictionary separator.</summary>
    /// <param name="reader">The reader positioned at the value.</param>
    private static void DiagnoseUngroupedLabel(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.Colon)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "parenthesize a labeled expression");
        }
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

        reader.AddDiagnostic(DiagnosticCode.EmptyExecutableBlock_Kd);
        return new CodeBlockKoto(ref reader, reader.CurrentTokenRange, []);
    }

    internal static CodeBlockKoto ParseRequiredBody(ref TokenReader reader, bool ifBody = false)
    {
        if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
        {
            if (reader.SingleBodyRegion)
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "a nested body in this region requires =>");
            }

            return ParseRequiredBlock(ref reader);
        }

        var expression = ParseSingleBodyItem(ref reader, ifBody);
        return new CodeBlockKoto(ref reader, expression.Span, [expression]) { IsExpressionBody = true };
    }

    internal static Koto ParseSingleBodyItem(ref TokenReader reader, bool ifBody = false)
    {
        var headerEnd = reader.PreviousEnd;
        var arrow = reader.Read();
        if (!reader.SameLine(headerEnd, arrow.Span.Start) || !reader.SameLine(arrow.Span.End, reader.CurrentTokenRange.Start) || IsExpressionBoundary(ref reader))
        {
            reader.AddDiagnostic(DiagnosticCode.EmptyExecutableBlock_Kd);
            return reader.NewErrorKoto();
        }

        var region = (reader.SingleBodyRegion, reader.IfBodyRegion);
        reader.SingleBodyRegion = true;
        reader.IfBodyRegion |= ifBody;
        try
        {
            if (reader.CurrentTokenKind == TokenKind.Require)
            {
                return ParseRequire(ref reader);
            }

            return IsBlockStatementStart(ref reader) ? ParseBlockStatement(ref reader) : ParseRequiredExpression(ref reader);
        }
        finally
        {
            (reader.SingleBodyRegion, reader.IfBodyRegion) = region;
        }
    }

    private static Koto ParseHeaderExpression(ref TokenReader reader)
    {
        var previous = reader.HeaderRegion;
        reader.HeaderRegion = true;
        try
        {
            return ParseRequiredExpression(ref reader);
        }
        finally
        {
            reader.HeaderRegion = previous;
        }
    }

    private static DoKoto ParseDoExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var body = ParseRequiredBody(ref reader);
        return new DoKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, body.Span.End), body);
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
        if (allowLabel && IsBlockStatementStart(ref reader, expressionPosition: true))
        {
            reader.AddDiagnostic(DiagnosticCode.BlockStatementInExpression_Kd);
            return ParseBlockStatement(ref reader);
        }

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
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "attribute placement");
                // Attributes following an expression are attached to the next parsed node.
                _ = ParseAttributeKoto(ref reader);
                continue;
            }

            if (tokenKind == TokenKind.At)
            {
                // A conversion takes a Type, but still obeys infix precedence:
                // -value@T -> (-value)@T, value * other@T -> value * (other@T).
                if (minBindingPower > InfixLeftBindingPower[(byte)tokenKind])
                {
                    break;
                }

                var token2 = reader.Read();
                Koto typeKoto;
                if (IsExpressionBoundary(ref reader))
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                    typeKoto = reader.NewErrorKoto();
                }
                else
                {
                    // Origins in executable conversions are inferred, not declared here.
                    typeKoto = ParseType(ref reader, parseOrigin: false, disambiguateGenerics: true, allowNestedOrigins: false);
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
            if (leftBindingPower == ComparisonBindingPower && IsNonAssociativeComparison(ref reader, tokenKind, left))
            {
                // Keep parsing for recovery, but require explicit parentheses for all combinations
                // of ordering, equality, and runtime is comparisons (SPEC 13.1).
                reader.Diagnostic.Add(token.Span, DiagnosticCode.ChainedComparison_Kd);
            }

            Koto right;
            if (IsExpressionBoundary(ref reader))
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                right = reader.NewErrorKoto();
            }
            else if (token.Kind == TokenKind.Is && !reader.IsParsingCompileTimeCondition)
            {
                var negated = reader.TryConsume(TokenKind.Not);
                right = ParseConstraintSubject(ref reader);
                if (reader.CurrentTokenKind == TokenKind.LessThan && HasAdjacentGenericArguments(ref reader, right.Span))
                {
                    right = ParseGenericsPostfix(ref reader, right);
                }

                var test = (IsKoto)KotoHelper.NewBinaryKoto(ref reader, token, left, right);
                test.IsRuntimeTest = true;
                test.IsNegated = negated;
                left = test;
                continue;
            }
            else
            {
                right = ParseExpression(ref reader, rightBindingPower);
            }

            left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
        }

        return left;
    }

    private static bool IsNonAssociativeComparison(ref TokenReader reader, TokenKind tokenKind, Koto left)
    {
        // Directive conditions reject every is test separately; do not report it twice as a chain.
        var runtimeIs = !reader.IsParsingCompileTimeCondition;
        return (tokenKind != TokenKind.Is || runtimeIs) &&
            (left is LessThanKoto or LessThanEqualsKoto or GreaterThanKoto or GreaterThanEqualsKoto or EqualsEqualsKoto or ExclamationEqualsKoto ||
            (runtimeIs && left is IsKoto));
    }

    private static Koto ParseLabeledExpression(ref TokenReader reader)
    {
        if (reader.HeaderRegion)
        {
            // A labeled construct is body-bearing; grouping keeps its body separate from the header (SPEC 2.2.1).
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "group body-bearing header expressions");
        }

        var token = reader.Read();
        reader.TryGetIdentifier(token, out var label);
        reader.Advance(); // Colon.
        if (!reader.SameLine(token.Span.End, reader.CurrentTokenRange.Start))
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

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
            case TokenKind.If:
                target = ParseIfExpression(ref reader);
                break;
            case TokenKind.Match:
                target = ParseMatchExpression(ref reader);
                break;
            case TokenKind.Do:
                target = ParseDoExpression(ref reader);
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
        if (reader.HeaderRegion && tokenKind is TokenKind.If or TokenKind.Match or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Do or TokenKind.Func)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "group body-bearing header expressions");
        }

        if (tokenKind == TokenKind.Sharp)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "attribute placement");
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

            // $abort accepts exactly one positional Expression, without a label or trailing comma (SPEC F.4).
            if (tokenKind == TokenKind.Dollar &&
                (operand is not InvocationKoto { Method: IdentifierNameKoto { IdentifierName: "abort" }, ArgumentNodes.Count: 1 } abort || abort.GetArgumentLabel(0) is not null ||
                (reader.PeekKind(-1) == TokenKind.CloseParenthesis && reader.PeekKind(-2) == TokenKind.Comma)))
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, "$abort(Expression)");
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

                    var accessor = ParseMemberName(ref reader);
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
                        var region = (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion);
                        reader.SingleBodyRegion = reader.IfBodyRegion = reader.HeaderRegion = false;
                        try
                        {
                            index = ParseExpression(ref reader);
                        }
                        finally
                        {
                            (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion) = region;
                        }
                    }

                    reader.TrySkipSeparatorsTo(TokenKind.CloseBracket);
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
        => left is IdentifierNameKoto or MemberAccessKoto or GenericsKoto &&
            HasAdjacentGenericArguments(ref reader, left.Span);

    private static bool HasAdjacentGenericArguments(ref TokenReader reader, SourceSpan targetSpan)
    {
        if (reader.CurrentTokenRange.Start != targetSpan.End)
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
    {
        var region = (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion);
        reader.SingleBodyRegion = reader.IfBodyRegion = reader.HeaderRegion = false;
        try
        {
            return ParseArgumentListCore(ref reader, out argumentLabels);
        }
        finally
        {
            (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion) = region;
        }
    }

    private static Koto[] ParseArgumentListCore(ref TokenReader reader, out string?[]? argumentLabels)
    {// (arg0, arg1, )
        var arguments = default(TemporaryKotoList);
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
                DiagnoseUngroupedLabel(ref reader);
            }
            else if (hasLabels)
            {
                labels.Add(null);
            }

            arguments.Add(ParseRequiredExpression(ref reader));

            // A block-valued argument (including match) can leave a dedent separator
            // before the next comma. Keep the comma mandatory for the argument list.
            reader.TrySkipSeparatorsTo(TokenKind.Comma);
            reader.TrySkipSeparatorsTo(TokenKind.CloseParenthesis);
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
            case TokenKind.ColonColon:
                return ParseRootName(ref reader, false);

            case TokenKind.Dot:
                return ParseInferredCase(ref reader);

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

            case TokenKind.Null:
                return new NullLiteralKoto(ref reader, reader.Read().Span);

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
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "standalone indented body");
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

            case TokenKind.Do:
                return ParseDoExpression(ref reader);

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
        var region = (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion);
        reader.SingleBodyRegion = reader.IfBodyRegion = reader.HeaderRegion = false;
        try
        {
            return ParseGroupedExpression(ref reader);
        }
        finally
        {
            (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion) = region;
        }
    }

    private static Koto ParseGroupedExpression(ref TokenReader reader)
    {
        var openToken = reader.Read();

        if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
        {
            var unitEnd = reader.Read().Span;
            return new UnitLiteralKoto(ref reader, SourceSpan.FromBounds(openToken.Span.Start, unitEnd.End));
        }

        var operand = ParseRequiredExpression(ref reader);

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

    private static Koto ParseCollectionLiteral(ref TokenReader reader)
    {
        var region = (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion);
        reader.SingleBodyRegion = reader.IfBodyRegion = reader.HeaderRegion = false;
        try
        {
            return ParseCollectionLiteralCore(ref reader);
        }
        finally
        {
            (reader.SingleBodyRegion, reader.IfBodyRegion, reader.HeaderRegion) = region;
        }
    }

    private static Koto ParseCollectionLiteralCore(ref TokenReader reader)
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
            var elements = new List<Koto>(4) { first };
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
            var entries = new List<DictionaryLiteralEntry>(4);
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

            if (allowLabel)
            {
                DiagnoseUngroupedLabel(ref reader);
            }

            return ParseExpression(ref reader, allowLabel: allowLabel);
        }
    }

    private static (int Left, int Right) GetInfixBindingPower(TokenKind kind)
        => kind switch
        {
            // Conversion (below prefix operators, above multiplication).
            // "@" parses its right operand as a Type in ParseExpression.
            TokenKind.At => (90, 91),

            // Multiplicative
            TokenKind.Asterisk => (80, 81),
            TokenKind.Slash => (80, 81),
            TokenKind.Percent => (80, 81),

            // Additive
            TokenKind.Plus => (70, 71),
            TokenKind.Minus => (70, 71),

            // Shift
            TokenKind.LessThanLessThan => (60, 61),
            TokenKind.GreaterThanGreaterThan => (60, 61),

            // Bitwise
            TokenKind.Ampersand => (50, 51),
            TokenKind.Caret => (45, 46),
            TokenKind.Bar => (40, 41),

            // Comparisons share one non-associative level. ParseExpression
            // diagnoses chains while retaining their nodes for error recovery.
            TokenKind.LessThan => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.LessThanEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.GreaterThan => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.GreaterThanEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.EqualsEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.ExclamationEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.Is => (ComparisonBindingPower, ComparisonBindingPower + 1),

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

    private static Koto ParseDeclarationType(ref TokenReader reader, bool parseOrigin = true, bool parseFunctionType = true, bool allowNestedOrigins = true)
    {
        Koto type;
        var parameterList = false;
        if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
        {
            var openRange = reader.CurrentTokenRange;
            reader.Advance();

            var elements = default(TemporaryKotoList);
            Koto? firstElement = null;
            var lastEnd = openRange.End;
            var hasComma = false;
            while (reader.CanRead && reader.CurrentTokenKind != TokenKind.CloseParenthesis)
            {
                if (reader.CurrentTokenKind == TokenKind.Separator)
                {
                    reader.Advance();
                    continue;
                }

                var element = ParseDeclarationType(ref reader, parseOrigin: allowNestedOrigins, allowNestedOrigins: allowNestedOrigins);
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
                    reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                    reader.SkipUntil(TokenKind.Comma, TokenKind.CloseParenthesis);
                    reader.TryConsume(TokenKind.Comma);
                }
            }

            var end = Math.Max(openRange.End, lastEnd);
            if (reader.TryConsume(TokenKind.CloseParenthesis, out var closeRange, true))
            {
                end = Math.Max(end, closeRange.End);
            }

            var range = SourceSpan.FromBounds(openRange.Start, end);
            type = elements.Count == 1 && !hasComma
                ? new ParenthesizedTypeKoto(ref reader, range, firstElement!)
                : new TupleTypeKoto(ref reader, range, elements.ToArray());
            parameterList = true;
            if (parseOrigin)
            {
                var annotated = ParseTypeOrigin(ref reader, type);
                parameterList = ReferenceEquals(annotated, type);
                type = annotated;
            }
        }
        else
        {
            type = ParseType(ref reader, parseOrigin, allowNestedOrigins: allowNestedOrigins);
        }

        if (!parseFunctionType || reader.CurrentTokenKind != TokenKind.MinusGreaterThan)
        {
            return type;
        }

        var arrowRange = reader.CurrentTokenRange;
        if (!parameterList)
        {
            // A bare or Semantics-applied Type cannot replace the Function Parameter List: ref/(T) -> U is invalid (SPEC 3.2).
            reader.Diagnostic.Add(arrowRange, DiagnosticCode.UnexpectedToken_Kd, "function parameter list");
        }

        reader.Advance();
        var returnType = ParseDeclarationType(ref reader, parseOrigin: allowNestedOrigins, allowNestedOrigins: allowNestedOrigins);
        return new FunctionTypeKoto(
            ref reader,
            SourceSpan.FromBounds(type.Span.Start, Math.Max(arrowRange.End, returnType.Span.End)),
            type,
            returnType);
    }

    private static List<TypeKoto>? ParseGenericArguments(ref TokenReader reader, bool allowLength = false, bool specialization = false)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();

        List<TypeKoto>? list = default;
        while (reader.CanRead)
        {
            if (IsTypeClose(reader.CurrentTokenKind))
            {
                if (list is null)
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                reader.TryConsumeTypeClose(out _);
                return list;
            }

            if (reader.CurrentTokenKind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }

            TypeKoto typeKoto;
            if (reader.IsCurrentIdentifier("length") && !specialization)
            {
                var start = reader.Read().Span.Start;
                var name = ParseName(ref reader);
                typeKoto = new LengthParameterKoto(ref reader, SourceSpan.FromBounds(start, name.Span.End), (name as IdentifierNameKoto)?.IdentifierName ?? string.Empty);
                if (!allowLength)
                {
                    typeKoto.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "length");
                }
            }
            else if (specialization)
            {
                var argument = ParseTypeArgument(ref reader, true);
                typeKoto = argument as TypeKoto ?? new TypeSemanticsKoto(ref reader, argument.Span, argument);
            }
            else
            {
                var first = reader.Read();
                if (!reader.TryGetIdentifier(first, out var name))
                {
                    return list;
                }

                string? semantics = null;
                var end = first.Span.End;
                if (reader.TryConsume(TokenKind.Slash))
                {
                    semantics = name;
                    var second = reader.Read();
                    if (!reader.TryGetIdentifier(second, out name))
                    {
                        return list;
                    }

                    end = second.Span.End;
                }

                typeKoto = new GenericParameterKoto(ref reader, SourceSpan.FromBounds(first.Span.Start, end), name, semantics);
            }

            (list ??= new(2)).Add(typeKoto);

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
