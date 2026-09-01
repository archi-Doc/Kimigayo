// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

    /// <summary>Evaluates a conditional attribute.</summary>
    /// <param name="compilation">The active compilation.</param>
    /// <param name="attributeKoto">The attribute to evaluate.</param>
    /// <returns><see langword="true"/> when the attributed declaration is included.</returns>
    public static bool ResolveIfAttribute(Compilation compilation, AttributeKoto attributeKoto)
    {
        Debug.Assert(attributeKoto.IsIfAttribute);

        var arg = attributeKoto.Arguments;
        if (arg.Count != 1)
        {
            attributeKoto.AddDiagnostic(DiagnosticCode.InvalidIfAttributeArgumentCount_Kd);
        }
        else
        {
            var basicValue = BasicValueHelper.Evaluate(compilation, arg[0]);
            if (basicValue.Kind == BasicValueKind.Bool)
            {
                if (!basicValue.Bool)
                {
                    return false;
                }
            }
            else
            {
                arg[0].AddDiagnostic(DiagnosticCode.ConditionMustBeBool_Kd);
            }
        }

        return true;
    }

    /// <summary>Writes the qualified name of an identifiable node.</summary>
    /// <param name="a0">The innermost identifiable node.</param>
    /// <param name="builder">The destination builder.</param>
    public static void WriteQualifiedNameTo(IdentifiableKoto? a0, ref IndentedStringBuilder builder)
    {
        if (a0 is null)
        {
            return;
        }

        var a1 = a0.Parent as IdentifiableKoto;
        if (a1 is null || a1.IsRoot)
        {
            builder.Append(a0.GetIdentifier());
            return;
        }

        var a2 = a1.Parent as IdentifiableKoto;
        if (a2 is null || a2.IsRoot)
        {
            builder.Append(a1.GetIdentifier());
            builder.Append(Constants.DotChar);
            builder.Append(a0.GetIdentifier());
            return;
        }

        var a3 = a2.Parent as IdentifiableKoto;
        if (a3 is null || a3.IsRoot)
        {
            builder.Append(a2.GetIdentifier());
            builder.Append(Constants.DotChar);
            builder.Append(a1.GetIdentifier());
            builder.Append(Constants.DotChar);
            builder.Append(a0.GetIdentifier());
            return;
        }

        var a4 = a3.Parent as IdentifiableKoto;
        if (a4 is null || a4.IsRoot)
        {
            builder.Append(a3.GetIdentifier());
            builder.Append(Constants.DotChar);
            builder.Append(a2.GetIdentifier());
            builder.Append(Constants.DotChar);
            builder.Append(a1.GetIdentifier());
            builder.Append(Constants.DotChar);
            builder.Append(a0.GetIdentifier());
            return;
        }

        var list = new List<IdentifiableKoto>();
        var x = a0;
        while (x is not null && !x.IsRoot)
        {
            list.Add(x);
            x = x.Parent as IdentifiableKoto;
        }

        for (var i = list.Count - 1; i >= 0; i--)
        {
            builder.Append(list[i].GetIdentifier());
            if (i != 0)
            {
                builder.Append(Constants.DotChar);
            }
        }
    }

    /// <summary>Writes an attribute chain as source text.</summary>
    /// <param name="a0">The first attribute in the chain.</param>
    /// <param name="builder">The destination builder.</param>
    /// <param name="options">The output options.</param>
    public static void UnparseAttribute(AttributeKoto? a0, ref IndentedStringBuilder builder, KotoWriteOptions options)
    {
        if (a0 is null)
        {
            return;
        }

        var a1 = a0.AttributeChain;
        if (a1 is null)
        {
            a0.WriteTo(ref builder);
            builder.AppendTrailingSpaceOrLineFeed(options);
            return;
        }

        var a2 = a1.AttributeChain;
        if (a2 is null)
        {
            a1.WriteTo(ref builder);
            builder.Append(' ');
            a0.WriteTo(ref builder);
            builder.AppendTrailingSpaceOrLineFeed(options);
            return;
        }

        var a3 = a2.AttributeChain;
        if (a3 is null)
        {
            a2.WriteTo(ref builder);
            builder.Append(' ');
            a1.WriteTo(ref builder);
            builder.Append(' ');
            a0.WriteTo(ref builder);
            builder.AppendTrailingSpaceOrLineFeed(options);
            return;
        }

        var a4 = a3.AttributeChain;
        if (a4 is null)
        {
            a3.WriteTo(ref builder);
            builder.Append(' ');
            a2.WriteTo(ref builder);
            builder.Append(' ');
            a1.WriteTo(ref builder);
            builder.Append(' ');
            a0.WriteTo(ref builder);
            builder.AppendTrailingSpaceOrLineFeed(options);
            return;
        }

        var list = new List<Koto>();
        var x = a0;
        while (x is not null)
        {
            list.Add(x);
            x = x.AttributeChain;
        }

        for (var i = list.Count - 1; i >= 0; i--)
        {
            list[i].WriteTo(ref builder);
            if (i != 0)
            {
                builder.Append(' ');
            }
        }

        builder.AppendTrailingSpaceOrLineFeed(options);
    }

    /// <summary>Writes an attribute chain to a string.</summary>
    /// <param name="a0">The first attribute in the chain.</param>
    /// <returns>The attribute source text.</returns>
    public static string UnparseAttribute(AttributeKoto? a0)
    {
        if (a0 is null)
        {
            return string.Empty;
        }

        var builder = default(IndentedStringBuilder);
        try
        {
            UnparseAttribute(a0, ref builder, KotoWriteOptions.AppendSpace);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>Parses a function declaration after its keyword.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed function, or <see langword="null"/> after an error.</returns>
    public static FunctionKoto? ParseFuncDeclaration(ref TokenReader reader)
    {
        var context = reader.TakeContext();

        if (!reader.TryRead(out var methodToken))
        {
            return default;
        }

        if (methodToken.Kind != TokenKind.Identifier)
        {
            reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
            goto SkipAndExit;
        }

        var methodName = reader.GetSpan(methodToken);
        if (!IdentifierHelper.IsValidIdentifier(methodName))
        {
            reader.AddDiagnostic(DiagnosticCode.InvalidIdentifier_Kd, methodName.ToString());
            goto SkipAndExit;
        }

        List<TypeKoto>? genericArguments = default;
        if (reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = ParseGenericArguments(ref reader);
        }

        if (!reader.TryConsume(TokenKind.OpenParenthesis, out _, true))
        {
            goto Exit;
        }

        var parameters = new List<FunctionParameterKoto>();
        while (reader.CanRead)
        {
            ConsumeSeparators(ref reader);
            while (reader.CurrentTokenKind == TokenKind.Sharp)
            {
                _ = ParseAttributeKoto(ref reader);
                ConsumeSeparators(ref reader);
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

            if (!IdentifierNameKoto.TryCreate(ref reader, externalNameToken, out var externalNameKoto))
            {
                SkipParameter(ref reader);
                goto NextParameter;
            }

            var isOptional = reader.CurrentTokenKind == TokenKind.Question;
            if (isOptional)
            {
                reader.Advance();
            }

            var internalName = externalNameKoto.IdentifierName;
            if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan)
            {
                reader.Advance();

                if (!reader.TryRead(out var internalNameToken) ||
                    !IdentifierNameKoto.TryCreate(ref reader, internalNameToken, out var internalNameKoto))
                {
                    SkipParameter(ref reader);
                    goto NextParameter;
                }

                internalName = internalNameKoto.IdentifierName;
            }

            if (!reader.TryConsume(TokenKind.Colon, out _, true))
            {
                goto Exit;
            }

            var parameterType = ParseDeclarationType(ref reader);
            Koto? defaultValue = default;
            if (reader.CurrentTokenKind == TokenKind.Equals)
            {
                reader.Advance();
                defaultValue = ParseExpression(ref reader);
            }

            parameters.Add(new(
                externalNameKoto.IdentifierName,
                internalName,
                isOptional,
                parameterType,
                defaultValue,
                parameterAttribute));

NextParameter:
            ConsumeSeparators(ref reader);
            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                reader.Advance();
            }
            else if (reader.CurrentTokenKind != TokenKind.CloseParenthesis)
            {
                reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                SkipParameter(ref reader);
                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    reader.Advance();
                }
            }
        }

        if (!reader.TryConsume(TokenKind.CloseParenthesis, out var closeParenthesisRange, true))
        {
            goto Exit;
        }

        Koto? returnType = default;
        var end = closeParenthesisRange.End;
        if (reader.CurrentTokenKind == TokenKind.MinusGreaterThan)
        {
            reader.Advance();
            returnType = ParseDeclarationType(ref reader);
            end = returnType.Span.End;
        }

        var functionKoto = new FunctionKoto(
            ref reader,
            context,
            SourceSpan.FromBounds(methodToken.Span.Start, end),
            methodName.ToString(),
            genericArguments,
            parameters,
            returnType);

        reader.SkipUntil(TokenKind.StartBlock, TokenKind.Separator, DiagnosticCode.UnexpectedTrailingToken_Kd);
        return functionKoto;

SkipAndExit:
        reader.SkipUntil(TokenKind.StartBlock, TokenKind.Separator);

Exit:
        return default;

        static void SkipParameter(ref TokenReader reader)
            => reader.SkipUntil(TokenKind.Comma, TokenKind.CloseParenthesis);
    }

    /// <summary>Parses the header of a group or type declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The name, generic parameters, and origin names.</returns>
    public static (string Name, List<TypeKoto>? GenericArguments, List<string>? Origins) ParseGroupDeclaration(ref TokenReader reader)
        => ParseGroupDeclaration(ref reader, true, true);

    /// <summary>Parses a collection declaration header according to the capabilities of its kind.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="supportsGenerics">Whether generic parameters are accepted.</param>
    /// <param name="supportsOrigins">Whether an Origin list is accepted.</param>
    /// <returns>The name, generic parameters, and origin names.</returns>
    internal static (string Name, List<TypeKoto>? GenericArguments, List<string>? Origins) ParseGroupDeclaration(
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

        if (token.Kind != TokenKind.Identifier)
        {
            reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
            goto SkipAndExit;
        }

        var span = reader.GetSpan(token);
        if (IdentifierHelper.IsValidIdentifier(span))
        {
            name = span.ToString();
        }
        else
        {
            reader.AddDiagnostic(DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
        }

        if (supportsGenerics && reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = ParseGenericArguments(ref reader);
        }

        if (supportsOrigins && reader.IsIdentifierToken(reader.CurrentToken, Constants.OriginKeyword))
        {
            var originRange = reader.CurrentTokenRange;
            reader.Advance();
            origins = ParseOrigins(ref reader, originRange);
        }

        reader.SkipUntilStartBlock();
        goto Exit;

SkipAndExit:
        reader.SkipUntilStartBlock(0);

Exit:
        return (name, genericArguments, origins);

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

                if (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock)
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                    return list;
                }

                reader.TryRead(out var token);
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

                list ??= [];
                list.Add(identifier.ToString());

                if (reader.CurrentTokenKind != TokenKind.Comma)
                {
                    return list;
                }

                reader.Advance();
            }
        }
    }

    /// <summary>Parses a field or local variable declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <returns>The parsed declaration, or <see langword="null"/> after an error.</returns>
    public static FieldKoto? ParseField(ref TokenReader reader, ref Token token)
        => ParseField(ref reader, ref token, false);

    private static FieldKoto? ParseField(
        ref TokenReader reader,
        ref Token token,
        bool allowParenthesizedTerminator)
    {
        var variableContext = reader.TakeContext();

        Parser.ConsumeAttributeAndModifier(ref reader, out var isEnd);
        if (isEnd)
        {
            return default;
        }

        var nameToken = reader.CurrentToken;
        reader.Advance();
        if (!IdentifierNameKoto.TryCreate(ref reader, nameToken, out var nameKoto))
        {
            return default;
        }

        Koto? typeKoto = default;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {
            Parser.ConsumeAttributeAndModifier(ref reader, out isEnd);
            if (isEnd)
            {
                return default;
            }

            typeKoto = ParseType(ref reader);
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out _, false))
        {
            initializerKoto = ParseExpression(ref reader);
        }

        reader.RestoreContext(variableContext);

        var fieldKoto = new FieldKoto(ref reader, ref token, nameKoto, typeKoto, initializerKoto);

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

    /// <summary>Writes declaration modifiers as source text.</summary>
    /// <param name="kind">The modifiers to write.</param>
    /// <param name="builder">The destination builder.</param>
    /// <param name="writeOptions">The output options.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteTo(this ModifierKind kind, ref IndentedStringBuilder builder, KotoWriteOptions writeOptions)
    {
        var acc = kind.ExtractAccessibilityModifiers();
        var accText = acc switch
        {
            ModifierKind.Public => Constants.PublicKeyword,
            ModifierKind.Protected => Constants.ProtectedKeyword,
            ModifierKind.Private => Constants.PrivateKeyword,
            ModifierKind.Internal => Constants.InternalKeyword,
            ModifierKind.ProtectedOrInternal => Constants.ProtectedOrInternalKeyword,
            ModifierKind.ProtectedAndInternal => Constants.ProtectedAndInternalKeyword,
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(accText))
        {
            return;
        }

        builder.Append(accText);

        if (kind.HasFlag(ModifierKind.Static))
        {
            builder.EnsureTrailingSpace();
            if (kind.HasFlag(ModifierKind.Open))
            {
                builder.Append(Constants.StaticKeyword);
                builder.AppendSpace();
                builder.Append(Constants.OpenKeyword);
            }
            else
            {
                builder.Append(Constants.StaticKeyword);
            }
        }
        else
        {
            if (kind.HasFlag(ModifierKind.Open))
            {
                builder.EnsureTrailingSpace();
                builder.Append(Constants.OpenKeyword);
            }
            else
            {
            }
        }

        builder.AppendTrailingSpaceOrLineFeed(writeOptions);
    }

    /// <summary>Converts declaration modifiers to source text.</summary>
    /// <param name="kind">The modifiers to convert.</param>
    /// <param name="addSpace">Whether to append a trailing space.</param>
    /// <returns>The modifier source text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToText(this ModifierKind kind, bool addSpace = false)
    {
        var acc = kind.ExtractAccessibilityModifiers();
        var accText = acc switch
        {
            ModifierKind.Public => Constants.PublicKeyword,
            ModifierKind.Protected => Constants.ProtectedKeyword,
            ModifierKind.Private => Constants.PrivateKeyword,
            ModifierKind.Internal => Constants.InternalKeyword,
            ModifierKind.ProtectedOrInternal => Constants.ProtectedOrInternalKeyword,
            ModifierKind.ProtectedAndInternal => Constants.ProtectedAndInternalKeyword,
            _ => string.Empty,
        };

        if (addSpace)
        {
            if (kind.HasFlag(ModifierKind.Static))
            {
                if (kind.HasFlag(ModifierKind.Open))
                {
                    return $"{accText} {Constants.StaticKeyword} {Constants.OpenKeyword} ";
                }
                else
                {
                    return $"{accText} {Constants.StaticKeyword} ";
                }
            }
            else
            {
                if (kind.HasFlag(ModifierKind.Open))
                {
                    return $"{accText} {Constants.OpenKeyword} ";
                }
                else
                {
                    return $"{accText} ";
                }
            }
        }
        else
        {
            if (kind.HasFlag(ModifierKind.Static))
            {
                if (kind.HasFlag(ModifierKind.Open))
                {
                    return $"{accText} {Constants.StaticKeyword} {Constants.OpenKeyword}";
                }
                else
                {
                    return $"{accText} {Constants.StaticKeyword}";
                }
            }
            else
            {
                if (kind.HasFlag(ModifierKind.Open))
                {
                    return $"{accText} {Constants.OpenKeyword}";
                }
                else
                {
                    return $"{accText}";
                }
            }
        }
    }

    /// <summary>Consumes attributes and modifiers before a declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="isEnd">Whether the declaration sequence has ended.</param>
    public static void ConsumeAttributeAndModifier(ref TokenReader reader, out bool isEnd)
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
                    if (reader.ModifierKind.HasFlag(ModifierKind.Static))
                    {
                        reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, ModifierKind.Static.ToString());
                    }

                    reader.ModifierKind |= ModifierKind.Static;
                    reader.Advance();
                    continue;

                case TokenKind.Open:
                    if (reader.ModifierKind.HasFlag(ModifierKind.Open))
                    {
                        reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, ModifierKind.Open.ToString());
                    }

                    reader.ModifierKind |= ModifierKind.Open;
                    reader.Advance();
                    continue;

                case TokenKind.Public:
                    ReadAccessibility(ref reader, ModifierKind.Public);
                    continue;

                case TokenKind.Protected:
                    ReadAccessibility(ref reader, ModifierKind.Protected);
                    continue;

                case TokenKind.Private:
                    ReadAccessibility(ref reader, ModifierKind.Private);
                    continue;

                case TokenKind.Internal:
                    ReadAccessibility(ref reader, ModifierKind.Internal);
                    continue;

                case TokenKind.ProtectedOrInternal:
                    ReadAccessibility(ref reader, ModifierKind.ProtectedOrInternal);
                    continue;

                case TokenKind.ProtectedAndInternal:
                    ReadAccessibility(ref reader, ModifierKind.ProtectedAndInternal);
                    continue;
            }

            if (tokenKind != TokenKind.Sharp)
            {
                isEnd = reader.IsEnd;
                return;
            }

            _ = ParseAttributeKoto(ref reader);
            /*attributeKoto = ParseAttributeKoto(ref reader);
            if (!ResolveIfAttribute(reader.CodeContext.Compilation, attributeKoto))
            {
                reader.IsExcluded = true;
            }*/
        }

        isEnd = true;

        void ReadAccessibility(ref TokenReader reader, ModifierKind kind)
        {
            var acc = reader.ModifierKind.ExtractAccessibilityModifiers();
            if (acc != default)
            {
                if (acc == kind)
                {
                    reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, kind.ToText());
                }
                else
                {
                    reader.AddDiagnostic(DiagnosticCode.MultipleAccessibilityModifiers_Kd);
                }
            }
            else
            {
                reader.ModifierKind = reader.ModifierKind | kind;
            }

            reader.Advance();
        }
    }

    /// <summary>Parses a type expression.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed type node.</returns>
    public static Koto ParseType(ref TokenReader reader)
        => ParseType(ref reader, true);

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

                var accessor = ParseTypeInternal(ref reader);
                accessor ??= reader.NewErrorKoto();
                left = new MemberAccessKoto(
                    ref reader,
                    SourceSpan.FromBounds(left.Span.Start, Math.Max(operatorRange.End, accessor.Span.End)),
                    left,
                    accessor);
                continue;
            }
            else if (tokenKind == TokenKind.LessThan)
            {
                reader.Advance();
                var typeList = new List<Koto>();
                while (ParseType(ref reader) is { } typeKoto)
                {
                    typeList.Add(typeKoto);
                    if (reader.CurrentTokenKind != TokenKind.Comma)
                    {
                        break;
                    }
                    else
                    {
                        reader.Advance();
                    }
                }

                var end = typeList.Count == 0 ? left.Span.End : typeList[^1].Span.End;
                if (reader.TryConsume(TokenKind.GreaterThan, out var range, true))
                {
                    end = Math.Max(end, range.End);
                }

                left = new GenericsKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(left.Span.End, end)), left, typeList);
                continue;
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

        if (parseOrigin && reader.IsIdentifierToken(reader.CurrentToken, Constants.FromKeyword))
        {
            var fromRange = reader.CurrentTokenRange;
            reader.Advance();

            if (!reader.CanRead)
            {
                reader.Diagnostic.Add(fromRange, DiagnosticCode.IncompleteSyntax_Kd);
                return left;
            }

            if (reader.CurrentTokenKind is TokenKind.Separator or
                TokenKind.EndBlock or
                TokenKind.Comma or
                TokenKind.GreaterThan or
                TokenKind.CloseParenthesis or
                TokenKind.Equals or
                TokenKind.MinusGreaterThan)
            {
                reader.Diagnostic.Add(fromRange, DiagnosticCode.IncompleteSyntax_Kd);
                return left;
            }

            if (!reader.TryRead(out var originToken))
            {
                return left;
            }

            if (!originToken.Kind.IsIdentifierOrContextualKeyword())
            {
                reader.Diagnostic.Add(originToken.Span, DiagnosticCode.IdentifierExpected_Kd);
                return left;
            }

            var origin = reader.GetSpan(originToken);
            if (!IdentifierHelper.IsValidIdentifier(origin))
            {
                reader.Diagnostic.Add(originToken.Span, DiagnosticCode.InvalidIdentifier_Kd, origin.ToString());
                return left;
            }

            var originName = origin.ToString();
            if (left is TypeSemanticsKoto typeKoto)
            {
                typeKoto.SetOrigin(originName, originToken.Span.End);
            }
            else
            {
                left = new TypeSemanticsKoto(
                    ref reader,
                    SourceSpan.FromBounds(start, originToken.Span.End),
                    left,
                    originName);
            }
        }

        return left;

        static Koto? ParseTypeInternal(ref TokenReader reader)
        {
            var token = reader.CurrentToken;
            var start = token.Span.Start;
            var semanticsKind = SemanticsKind.Owner;
            var hasSemantics = false;
            string? semanticsParameter = default;

            reader.Advance();

            if (token.Kind == TokenKind.Identifier &&
                reader.CurrentTokenKind == TokenKind.Slash)
            {
                var semantics = reader.GetSpan(token);
                if (!CompilerHelper.TryParse(semantics, out semanticsKind))
                {
                    semanticsParameter = semantics.ToString();
                }

                hasSemantics = true;
                reader.Advance();
            }

            if (hasSemantics)
            {
                var attribute = reader.PopAttribute();
                var type = ParseType(ref reader, false);
                if (type is TypeSemanticsKoto { IsTransparentWrapper: true, Type: not null } transparentType)
                {
                    type = transparentType.Type;
                }

                var semantics = new TypeSemanticsKoto(
                    ref reader,
                    SourceSpan.FromBounds(start, type.Span.End),
                    type,
                    semanticsKind,
                    semanticsParameter);
                semantics.SetAttributeChain(attribute);
                return semantics;
            }

            if (token.Kind.IsPrimitiveType() || token.Kind == TokenKind.Identifier)
            {
                return new TypeSemanticsKoto(ref reader, token);
            }

            return null;
        }
    }

    /// <summary>Parses an attribute expression.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed attribute, or <see langword="null"/> after an error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        if (attributeKoto.IsIfAttribute)
        {
            reader.IsExcluded = !Parser.ResolveIfAttribute(reader.CodeContext.Compilation, attributeKoto);
            return null;
        }
        else
        {
            reader.PushAttribute(attributeKoto);
            return attributeKoto;
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
        var constraint = new IsKoto(ref reader, isRange, subject, condition);

        reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, DiagnosticCode.UnexpectedTrailingToken_Kd);
        return constraint;

        static Koto ParseCondition(ref TokenReader reader, bool parsesSemantics)
        {
            Token? notToken = default;
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                reader.TryRead(out var token);
                notToken = token;
            }

            var expression = ParseOr(ref reader, parsesSemantics);
            return notToken is { } token2
                ? new NotKoto(ref reader, token2.Span, expression)
                : expression;
        }

        static Koto ParseOr(ref TokenReader reader, bool parsesSemantics)
        {
            var left = ParseAnd(ref reader, parsesSemantics);
            while (reader.CurrentTokenKind == TokenKind.Or)
            {
                reader.TryRead(out var token);
                var right = ParseAnd(ref reader, parsesSemantics);
                left = new OrKoto(ref reader, token.Span, left, right);
            }

            return left;
        }

        static Koto ParseAnd(ref TokenReader reader, bool parsesSemantics)
        {
            var left = ParsePrimary(ref reader, parsesSemantics);
            while (reader.CurrentTokenKind == TokenKind.And)
            {
                reader.TryRead(out var token);
                var right = ParsePrimary(ref reader, parsesSemantics);
                left = new AndKoto(ref reader, token.Span, left, right);
            }

            return left;
        }

        static Koto ParsePrimary(ref TokenReader reader, bool parsesSemantics)
        {
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                reader.TryRead(out var token);
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

            if (reader.CurrentTokenKind is TokenKind.Invalid or
                TokenKind.Separator or
                TokenKind.EndBlock or
                TokenKind.CloseParenthesis)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return reader.NewErrorKoto();
            }

            if (!reader.TryRead(out var token2))
            {
                return reader.NewErrorKoto();
            }

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
    {
        if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
        {
            return false;
        }

        var lookahead = reader;
        lookahead.Advance();
        return lookahead.CurrentTokenKind == TokenKind.Is;
    }

    /// <summary>Parses an indentation-delimited expression block.</summary>
    /// <param name="reader">The token reader positioned at <see cref="TokenKind.StartBlock"/>.</param>
    /// <returns>The parsed block.</returns>
    public static CodeBlockKoto ParseBlock(ref TokenReader reader)
    {
        var start = reader.CurrentTokenRange;
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            return new CodeBlockKoto(ref reader, start, [], false);
        }

        var blockContext = reader.TakeContext();
        reader.Advance();
        var items = new List<Koto>();
        var hasTrailingExpression = false;

        while (reader.CanRead)
        {
            while (reader.CurrentTokenKind is TokenKind.Separator or TokenKind.Semicolon)
            {
                if (reader.CurrentTokenKind == TokenKind.Semicolon)
                {
                    hasTrailingExpression = false;
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
                    items,
                    hasTrailingExpression);
            }

            ConsumeAttributeAndModifier(ref reader, out var isEnd);
            if (isEnd)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                continue;
            }

            var oldPosition = reader.Position;
            var item = ParseBlockItem(ref reader, out var isDeclaration);
            if (item is not null)
            {
                items.Add(item);
                hasTrailingExpression = !isDeclaration;
            }

            if (reader.CurrentTokenKind == TokenKind.Semicolon)
            {
                hasTrailingExpression = false;
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
            items,
            hasTrailingExpression);
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
                return ParseField(ref reader, ref token);

            case TokenKind.Func:
                isDeclaration = true;
                reader.Advance();
                var function = ParseFuncDeclaration(ref reader);
                if (function is null)
                {
                    return null;
                }

                var functionBodyReader = reader;
                ConsumeSeparators(ref functionBodyReader);
                if (functionBodyReader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader = functionBodyReader;
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
                var declaration = ParseGroupDeclaration(
                    ref reader,
                    supportsGenericHeader,
                    supportsGenericHeader);
                var state = reader.TakeContext();
                var group = CollectionKoto.CreateStandalone(
                    reader.CodeContext,
                    token.Kind,
                    state,
                    token.Span,
                    declaration.Name);
                if (declaration.GenericArguments is not null)
                {
                    group.AddGenericArguments(declaration.GenericArguments);
                }

                if (declaration.Origins is not null)
                {
                    group.AddOrigins(declaration.Origins);
                }

                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    group.Parse(ref reader);
                }
                else
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                }

                return group;

            default:
                isDeclaration = false;
                return ParseExpression(ref reader);
        }
    }

    private static IfKoto ParseIfExpression(ref TokenReader reader)
    {
        reader.TryRead(out var ifToken);
        var branches = new List<ConditionalBranchKoto>();
        CodeBlockKoto? elseBody = null;
        var end = ifToken.Span.End;

        while (true)
        {
            var condition = ParseRequiredExpression(ref reader);
            var body = ParseRequiredConditionalBody(ref reader);
            branches.Add(new ConditionalBranchKoto(condition, body));
            end = body.Span.End;

            if (!TryConsumeElse(ref reader))
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.If)
            {
                reader.Advance();
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
        reader.TryRead(out var token);
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
        reader.TryRead(out var token);
        var body = ParseRequiredBlock(ref reader);
        return new LoopKoto(
            ref reader,
            SourceSpan.FromBounds(token.Span.Start, body.Span.End),
            body);
    }

    private static ForKoto ParseForExpression(ref TokenReader reader)
    {
        reader.TryRead(out var forToken);
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

        if (reader.CurrentTokenKind == TokenKind.In)
        {
            reader.Advance();
        }
        else
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

        reader.TryRead(out var token);
        return IdentifierNameKoto.TryCreate(ref reader, token, out binding);
    }

    private static MatchKoto ParseMatchExpression(ref TokenReader reader)
    {
        reader.TryRead(out var matchToken);
        var expression = ParseRequiredExpression(ref reader);
        var arms = new List<MatchArmKoto>();
        var end = expression.Span.End;

        ConsumeSeparators(ref reader);
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
            ConsumeSeparators(ref reader);
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
                Koto body;
                if (reader.CurrentTokenKind == TokenKind.Separator)
                {
                    body = ParseRequiredBlock(ref reader);
                }
                else
                {
                    body = ParseRequiredExpression(ref reader);
                }

                arms.Add(new MatchArmKoto(pattern, body));
                end = body.Span.End;
            }

            if (reader.CurrentTokenKind == TokenKind.Semicolon)
            {
                reader.Advance();
            }

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
        reader.TryRead(out var token);
        if (token.Kind == TokenKind.Continue)
        {
            return new ContinueKoto(ref reader, token.Span);
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
        if (!IsExpressionBoundary(ref reader))
        {
            expression = ParseExpression(ref reader);
            end = expression.Span.End;
        }

        var range = SourceSpan.FromBounds(token.Span.Start, end);
        return token.Kind switch
        {
            TokenKind.Return => new ReturnKoto(ref reader, range, expression),
            TokenKind.Break => new BreakKoto(ref reader, range, expression),
            _ => throw new InvalidOperationException(),
        };
    }

    private static Koto ParseRequiredExpression(ref TokenReader reader)
    {
        if (IsExpressionBoundary(ref reader))
        {
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            return reader.NewErrorKoto();
        }

        return ParseExpression(ref reader);
    }

    private static CodeBlockKoto ParseRequiredBlock(ref TokenReader reader)
    {
        var lookahead = reader;
        ConsumeSeparators(ref lookahead);
        if (lookahead.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader = lookahead;
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

        var expression = ParseRequiredExpression(ref reader);
        return new CodeBlockKoto(ref reader, expression.Span, [expression], true);
    }

    private static bool TryConsumeElse(ref TokenReader reader)
    {
        var lookahead = reader;
        ConsumeSeparators(ref lookahead);
        if (lookahead.CurrentTokenKind != TokenKind.Else)
        {
            return false;
        }

        lookahead.Advance();
        reader = lookahead;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsumeSeparators(ref TokenReader reader)
    {
        while (reader.CurrentTokenKind == TokenKind.Separator)
        {
            reader.Advance();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExpressionBoundary(ref TokenReader reader)
        => !reader.CanRead || reader.CurrentTokenKind is
            TokenKind.Separator or
            TokenKind.Semicolon or
            TokenKind.StartBlock or
            TokenKind.EndBlock or
            TokenKind.Else or
            TokenKind.EqualsGreaterThan or
            TokenKind.Comma or
            TokenKind.CloseParenthesis or
            TokenKind.CloseBracket;

    /// <summary>Parses an expression using the requested minimum binding power.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="minBindingPower">The minimum accepted binding power.</param>
    /// <returns>The parsed expression.</returns>
    public static Koto ParseExpression(ref TokenReader reader, int minBindingPower = 0)
    {
        var left = ParsePrefixExpression(ref reader);
        while (true)
        {
            if (TryParsePostfixExpression(ref reader, ref left))
            {
                continue;
            }

            var tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Sharp)
            {
                // Attributes following an expression are attached to the next parsed node.
                _ = ParseAttributeKoto(ref reader);

                continue;
            }

            if (tokenKind == TokenKind.At)
            {
                reader.TryRead(out var token2);
                Koto typeKoto;
                if (IsExpressionBoundary(ref reader))
                {
                    reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                    typeKoto = reader.NewErrorKoto();
                }
                else
                {
                    typeKoto = ParseType(ref reader);
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

                reader.TryRead(out var rangeToken);
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

            var bindingPower = GetInfixBindingPower(tokenKind);
            if (bindingPower == default || bindingPower.Left < minBindingPower)
            {
                break;
            }

            reader.TryRead(out var token);
            Koto right;
            if (token.Kind == TokenKind.Is && reader.CurrentTokenKind == TokenKind.Not)
            {
                reader.TryRead(out var notToken);
                var condition = ParseExpression(ref reader, bindingPower.Right);
                right = KotoHelper.NewUnaryKoto(ref reader, notToken, condition);
            }
            else
            {
                right = ParseExpression(ref reader, bindingPower.Right);
            }

            left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
        }

        return left;
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

        var bindingPower = GetPrefixBindingPower(tokenKind);
        if (bindingPower > 0)
        {
            reader.TryRead(out var token);
            var operand = ParseExpression(ref reader, bindingPower);
            var koto = KotoHelper.NewUnaryKoto(ref reader, token, operand);

            if (koto is AttributeKoto attributeKoto)
            {
                reader.PushAttribute(attributeKoto);
                goto ProcessPrefix;
            }

            return koto;
        }

        return ParsePrimaryExpression(ref reader);
    }

    private static bool TryParsePostfixExpression(ref TokenReader reader, ref Koto left)
    {
        var tokenKind = reader.CurrentTokenKind;
        switch (tokenKind)
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
                    var (arguments, argumentLabels) = ParseArgumentList(ref reader);
                    var end = arguments.Count == 0 ? openRange.End : Math.Max(openRange.End, arguments[^1].Span.End);
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

                    reader.Advance();
                    var typeList = new List<Koto>();
                    while (ParseType(ref reader) is { } typeKoto)
                    {
                        typeList.Add(typeKoto);
                        if (reader.CurrentTokenKind != TokenKind.Comma)
                        {
                            break;
                        }
                        else
                        {
                            reader.Advance();
                        }
                    }

                    var end = typeList.Count == 0 ? left.Span.End : typeList[^1].Span.End;
                    if (reader.TryConsume(TokenKind.GreaterThan, out var range, true))
                    {
                        end = Math.Max(end, range.End);
                    }

                    left = new GenericsKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(left.Span.End, end)), left, typeList);
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
                    reader.TryRead(out var token);
                    left = new PostfixIncrementKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, token.Span.End),
                        left);
                    return true;
                }

            case TokenKind.MinusMinus:
                {
                    reader.TryRead(out var token);
                    left = new PostfixDecrementKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, token.Span.End),
                        left);
                    return true;
                }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGenericPostfix(ref TokenReader reader, Koto left)
    {
        if (left is not (IdentifierNameKoto or MemberAccessKoto or GenericsKoto) ||
            reader.CurrentTokenRange.Start != left.Span.End)
        {
            return false;
        }

        // Require adjacent, balanced angle brackets to distinguish generics from comparisons.
        var lookahead = reader;
        var depth = 0;
        while (lookahead.CanRead)
        {
            switch (lookahead.CurrentTokenKind)
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
                case TokenKind.Separator:
                case TokenKind.EndBlock:
                    return false;
            }

            lookahead.Advance();
        }

        return false;
    }

    private static (List<Koto> Arguments, List<string?> Labels) ParseArgumentList(ref TokenReader reader)
    {// (arg0, arg1, )
        var tokenKind = reader.CurrentTokenKind;
        if (tokenKind == TokenKind.CloseParenthesis)
        {
            return ([], []);
        }

        SourceSpan range;
        var arguments = new List<Koto>();
        var labels = new List<string?>();

        while (tokenKind != TokenKind.Invalid &&
               tokenKind != TokenKind.CloseParenthesis)
        {
            string? label = default;
            if (tokenKind.IsIdentifierOrContextualKeyword())
            {
                var lookahead = reader;
                lookahead.Advance();
                if (lookahead.CurrentTokenKind == TokenKind.Colon)
                {
                    label = reader.GetSpan(reader.CurrentToken).ToString();
                    reader.Advance();
                    reader.Advance();
                }
            }

            arguments.Add(ParseExpression(ref reader));
            labels.Add(label);

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
                reader.TryConsume(TokenKind.Comma, out range);
                reader.SkipUntil(TokenKind.Comma, TokenKind.CloseParenthesis);

                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    reader.Advance();
                    continue;
                }
            }

            break;
        }

        return (arguments, labels);
    }

    private static Koto ParsePrimaryExpression(ref TokenReader reader)
    {
Loop:
        var tokenKind = reader.CurrentTokenKind;
        switch (tokenKind)
        {
            case TokenKind.Identifier:
            case TokenKind.In:
                {
                    reader.TryRead(out var token);
                    if (IdentifierNameKoto.TryCreate(ref reader, token, out var koto))
                    {
                        return koto;
                    }
                    else
                    {
                        return reader.NewErrorKoto();
                    }
                }

            case TokenKind.NumericLiteral:
                {
                    reader.TryRead(out var token);
                    return new NumberLiteralKoto(ref reader, token);
                }

            case TokenKind.StringLiteral:
                {
                    reader.TryRead(out var token);
                    return new StringLiteralKoto(ref reader, token);
                }

            case TokenKind.True:
            case TokenKind.False:
                {
                    reader.TryRead(out var token);
                    return new BoolLiteralKoto(ref reader, token);
                }

            case TokenKind.If:
                return ParseIfExpression(ref reader);

            case TokenKind.StartBlock:
                return ParseBlock(ref reader);

            case TokenKind.DotDot:
            case TokenKind.DotDotEquals:
                {
                    reader.TryRead(out var token);
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
            case TokenKind.Break:
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
                    reader.ReportUnexpectedToken(token);

                    return new ErrorKoto(ref reader, token.Span);
                }
        }
    }

    private static ParenthesizedKoto ParseParenthesizedExpression(ref TokenReader reader)
    {
        reader.TryRead(out var openToken);

        Koto operand;
        if (reader.CurrentTokenKind is TokenKind.Let or TokenKind.Var or TokenKind.Yield)
        {
            operand = ParseParenthesizedBlock(ref reader);
        }
        else
        {
            operand = ParseExpression(ref reader);
        }

        var end = Math.Max(openToken.Span.End, operand.Span.End);
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
            reader.TryRead(out var declarationToken);
            var field = ParseField(
                ref reader,
                ref declarationToken,
                allowParenthesizedTerminator: true);
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
            return new CodeBlockKoto(ref reader, reader.CurrentTokenRange, [], false);
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
        ConsumeSeparators(ref reader);

        if (reader.TryConsume(TokenKind.CloseBracket, out var emptyArrayClose, false))
        {
            return new ArrayLiteralKoto(
                ref reader,
                SourceSpan.FromBounds(openRange.Start, emptyArrayClose.End),
                []);
        }

        if (reader.CurrentTokenKind == TokenKind.Colon)
        {
            reader.Advance();
            ConsumeSeparators(ref reader);
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

        var first = ParseLiteralElement(ref reader);
        ConsumeSeparators(ref reader);
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
                ConsumeSeparators(ref reader);
                if (reader.CurrentTokenKind == TokenKind.CloseBracket)
                {
                    break;
                }

                elements.Add(ParseLiteralElement(ref reader));
                ConsumeSeparators(ref reader);
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
                    ConsumeSeparators(ref reader);
                    var value = ParseLiteralElement(ref reader);
                    entries.Add(new(key, value));
                    ConsumeSeparators(ref reader);
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
                ConsumeSeparators(ref reader);
                if (reader.CurrentTokenKind == TokenKind.CloseBracket)
                {
                    break;
                }

                key = ParseLiteralElement(ref reader);
                ConsumeSeparators(ref reader);
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

        static Koto ParseLiteralElement(ref TokenReader reader)
        {
            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Comma or TokenKind.CloseBracket)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
                return reader.NewErrorKoto();
            }

            return ParseExpression(ref reader);
        }
    }

    private static int GetPrefixBindingPower(TokenKind kind)
        => kind switch
        {
            TokenKind.Sharp => PrefixBindingPower,
            TokenKind.Dollar => PrefixBindingPower,
            TokenKind.Ampersand => PrefixBindingPower,
            TokenKind.Asterisk => PrefixBindingPower,
            TokenKind.Plus => PrefixBindingPower,
            TokenKind.Minus => PrefixBindingPower,
            TokenKind.Not => PrefixBindingPower,
            TokenKind.Caret => PrefixBindingPower,
            TokenKind.PlusPlus => PrefixBindingPower,
            TokenKind.MinusMinus => PrefixBindingPower,
            _ => 0,
        };

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
        if (IsRangeEndBoundary(ref reader))
        {
            if (rangeToken.Kind == TokenKind.DotDotEquals)
            {
                reader.Diagnostic.Add(rangeToken.Span, DiagnosticCode.IncompleteSyntax_Kd);
            }

            return default;
        }

        return ParseExpression(ref reader, RangeRightBindingPower);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRangeEndBoundary(ref TokenReader reader)
        => IsExpressionBoundary(ref reader) ||
            reader.CurrentTokenKind is TokenKind.DotDot or TokenKind.DotDotEquals;

    private static Koto ParseDeclarationType(ref TokenReader reader)
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
                    if (reader.CurrentTokenKind == TokenKind.Comma)
                    {
                        reader.Advance();
                    }
                }
            }

            var end = elements.Count == 0 ? openRange.End : Math.Max(openRange.End, elements[^1].Span.End);
            if (reader.TryConsume(TokenKind.CloseParenthesis, out var closeRange, true))
            {
                end = Math.Max(end, closeRange.End);
            }

            type = new TupleTypeKoto(ref reader, SourceSpan.FromBounds(openRange.Start, end), elements);
        }
        else
        {
            type = ParseType(ref reader);
        }

        if (reader.CurrentTokenKind != TokenKind.MinusGreaterThan)
        {
            return type;
        }

        var arrowRange = reader.CurrentTokenRange;
        reader.Advance();
        var returnType = ParseDeclarationType(ref reader);
        var functionType = new FunctionTypeKoto(
            ref reader,
            SourceSpan.FromBounds(type.Span.Start, Math.Max(arrowRange.End, returnType.Span.End)),
            type,
            returnType);
        return functionType;
    }

    private static List<TypeKoto>? ParseGenericArguments(ref TokenReader reader)
    {
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();

        List<TypeKoto>? list = default;
        while (reader.CanRead)
        {
            if (reader.CurrentTokenKind == TokenKind.GreaterThan)
            {
                reader.Advance();
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

            list ??= new();
            list.Add(typeKoto);

            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                reader.Advance();
            }
            else if (reader.CurrentTokenKind != TokenKind.GreaterThan)
            {
                reader.AddDiagnostic(DiagnosticCode.MissingComma_Kd);
                reader.SkipUntil(TokenKind.Comma, TokenKind.GreaterThan);
                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    reader.Advance();
                }
            }
        }

        _ = reader.TryConsume(TokenKind.GreaterThan, out _, true);
        return list;
    }
}
