// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202

public static class Parser
{
    private const int PrefixBindingPower = 90;

    public static bool ResolveIfAttribute(Compilation compilation, AttributeKoto attributeKoto)
    {
        Debug.Assert(attributeKoto.IsIfAttribute);

        // #If()
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
                {// false
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

    public static string UnparseAttribute(AttributeKoto? a0)
    {
        if (a0 is null)
        {
            return string.Empty;
        }

        var a1 = a0.AttributeChain;
        if (a1 is null)
        {
            return $"{a0.ToString()} ";
        }

        var a2 = a1.AttributeChain;
        if (a2 is null)
        {
            return $"{a1.ToString()} {a0.ToString()} ";
        }

        var a3 = a2.AttributeChain;
        if (a3 is null)
        {
            return $"{a2.ToString()} {a1.ToString()} {a0.ToString()} ";
        }

        var a4 = a3.AttributeChain;
        if (a4 is null)
        {
            return $"{a3.ToString()} {a2.ToString()} {a1.ToString()} {a0.ToString()} ";
        }

        var list = new List<Koto>();
        var sb = new StringBuilder();
        var x = a0;
        while (x is not null)
        {
            list.Add(x);
            x = x.AttributeChain;
        }

        list.Reverse();
        foreach (var y in list)
        {
            sb.Append(y.ToString());
            sb.Append(' ');
        }

        return sb.ToString();
    }

    public static FunctionKoto? ParseFuncDeclaration(ref TokenReader reader)
    {// public func Method1<T>(external => internal: type) -> returnType
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
        {// <s/T, s/T2>
            genericArguments = ParseGenericArguments(ref reader);
        }

        if (!reader.TryConsume(TokenKind.OpenParenthesis, out _, true))
        {
            goto Exit;
        }

        var parameters = new List<FunctionParameterKoto>();
        while (reader.CanRead && reader.CurrentTokenKind != TokenKind.CloseParenthesis)
        {
            if (reader.CurrentTokenKind == TokenKind.Separator)
            {
                reader.Advance();
                continue;
            }

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
                defaultValue));

NextParameter:
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

    public static (string Name, List<TypeKoto>? GenericArguments, List<string>? Origins) ParseGroupDeclaration(ref TokenReader reader)
    {// public open struct TestStruct<s/C, D> origin a, b
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

        if (reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = ParseGenericArguments(ref reader);
        }

        if (reader.IsIdentifierToken(reader.CurrentToken, Constants.OriginKeyword))
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

    public static FieldKoto? ParseField(ref TokenReader reader, ref Token token)
    {// var x: i32 = 1
        var variableContext = reader.TakeContext();

        // Field name
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
        {// var x: i32
            Parser.ConsumeAttributeAndModifier(ref reader, out isEnd);
            if (isEnd)
            {
                return default;
            }

            typeKoto = ParseType(ref reader);
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out _, false))
        {// var x = 1 + 2
            initializerKoto = ParseExpression(ref reader);
        }

        reader.RestoreContext(variableContext);

        var fieldKoto = new FieldKoto(ref reader, ref token, nameKoto, typeKoto, initializerKoto);

        reader.SkipUntil(TokenKind.EndBlock, TokenKind.Separator, DiagnosticCode.UnexpectedTrailingToken_Kd);

        return fieldKoto;
    }

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
        {// "public static "
            builder.EnsureTrailingSpace();
            if (kind.HasFlag(ModifierKind.Open))
            {// "public static open "
                builder.Append(Constants.StaticKeyword);
                builder.AppendSpace();
                builder.Append(Constants.OpenKeyword);
            }
            else
            {// "public static "
                builder.Append(Constants.StaticKeyword);
            }
        }
        else
        {// "public "
            if (kind.HasFlag(ModifierKind.Open))
            {// public open "
                builder.EnsureTrailingSpace();
                builder.Append(Constants.OpenKeyword);
            }
            else
            {// "public "
            }
        }

        builder.AppendTrailingSpaceOrLineFeed(writeOptions);
    }

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
        {// "public "
            if (kind.HasFlag(ModifierKind.Static))
            {// "public static "
                if (kind.HasFlag(ModifierKind.Open))
                {// "public static open "
                    return $"{accText} {Constants.StaticKeyword} {Constants.OpenKeyword} ";
                }
                else
                {// "public static "
                    return $"{accText} {Constants.StaticKeyword} ";
                }
            }
            else
            {// "public "
                if (kind.HasFlag(ModifierKind.Open))
                {// public open "
                    return $"{accText} {Constants.OpenKeyword} ";
                }
                else
                {// "public "
                    return $"{accText} ";
                }
            }
        }
        else
        {// "public"
            if (kind.HasFlag(ModifierKind.Static))
            {// "public static"
                if (kind.HasFlag(ModifierKind.Open))
                {// "public static open"
                    return $"{accText} {Constants.StaticKeyword} {Constants.OpenKeyword}";
                }
                else
                {// "public static"
                    return $"{accText} {Constants.StaticKeyword}";
                }
            }
            else
            {// "public"
                if (kind.HasFlag(ModifierKind.Open))
                {// public open"
                    return $"{accText} {Constants.OpenKeyword}";
                }
                else
                {// "public"
                    return $"{accText}";
                }
            }
        }
    }

    public static void ConsumeAttributeAndModifier(ref TokenReader reader, out bool isEnd)
    {// Consume Attributes and Modifiers
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
                    {// Duplicate
                        reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, ModifierKind.Static.ToString());
                    }

                    reader.ModifierKind |= ModifierKind.Static;
                    reader.Advance();
                    continue;

                case TokenKind.Open:
                    if (reader.ModifierKind.HasFlag(ModifierKind.Open))
                    {// Duplicate
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
                {// Duplicate
                    reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, kind.ToText());
                }
                else
                {// More than one accessibility modifier
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

    public static Koto ParseType(ref TokenReader reader)
        => ParseType(ref reader, true);

    private static Koto ParseType(ref TokenReader reader, bool parseOrigin)
    {// semantics/A.B<C>
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
            {// Class.Nested
                reader.TryRead(out var token);

                var accessor = ParseTypeInternal(ref reader);
                accessor ??= reader.NewErrorKoto();
                left = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, accessor.Span.End), left, accessor);
                continue;
            }
            else if (tokenKind == TokenKind.LessThan)
            {// Generics<T>
                reader.TryRead(out var token); // <
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

                reader.TryConsume(TokenKind.GreaterThan, out var range, true); // >
                left = new GenericsKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, range.End), left, typeList);
                continue;
            }
            else
            {
                break;
            }
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
            if (left is TypeKoto typeKoto)
            {
                typeKoto.SetOrigin(originName, originToken.Span.End);
            }
            else
            {
                left = new TypeKoto(
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
            {// semantics/
                var semantics = reader.GetSpan(token);
                if (!CompilerHelper.TryParse(semantics, out semanticsKind))
                {// semanticsParameter/
                    semanticsParameter = semantics.ToString();
                }

                hasSemantics = true;
                reader.Advance();
            }

            if (hasSemantics)
            {
                var attribute = reader.PopAttribute();
                var type = ParseType(ref reader, false);
                var semantics = new TypeKoto(
                    ref reader,
                    SourceSpan.FromBounds(start, type.Span.End),
                    type,
                    semanticsKind,
                    semanticsParameter);
                semantics.AttributeChain = attribute;
                return semantics;
            }

            if (token.Kind.IsPrimitiveType() || token.Kind == TokenKind.Identifier)
            {
                return new TypeKoto(ref reader, token);
            }

            return null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AttributeKoto? ParseAttributeKoto(ref TokenReader reader)
    {
        var previousAttribute = reader.PopAttribute();

        reader.TryRead(out var attributeToken);

        var operand = ParsePrimaryExpression(ref reader);
        TryParsePostfixExpression(ref reader, ref operand);

        if (previousAttribute is not null)
        {
            reader.PushAttribute(previousAttribute);
        }

        var attributeKoto = new AttributeKoto(ref reader, attributeToken.Span, operand);
        if (attributeKoto.IsIfAttribute)
        {// #If
            reader.IsExcluded = !Parser.ResolveIfAttribute(reader.CodeContext.Compilation, attributeKoto);
            return null;
        }
        else
        {// Other attribute
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
            {// Process attribute
                _ = ParseAttributeKoto(ref reader);

                /*var attributeKoto = ParseAttributeKoto(ref reader);
                if (!ResolveIfAttribute(reader.CodeContext.Compilation, attributeKoto))
                {
                    reader.IsExcluded = true;
                }*/

                continue;
            }

            if (tokenKind == TokenKind.At)
            {// A@B
                reader.TryRead(out var token2);
                var typeKoto = ParseType(ref reader);
                left = new ConversionKoto(ref reader, token2.Span, left, typeKoto);
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
            // left = new BinaryKoto(ref reader, token.Range, left, right);
        }

        return left;
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader)
    {
ProcessPrefix:
        var tokenKind = reader.CurrentTokenKind;
        if (tokenKind == TokenKind.Sharp)
        {// Process attribute
            _ = ParseAttributeKoto(ref reader);
            /*var attributeKoto = ParseAttributeKoto(ref reader);
            if (!ResolveIfAttribute(reader.CodeContext.Compilation, attributeKoto))
            {
                reader.IsExcluded = true;
            }*/

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
                {// Class.Member
                    reader.Advance(); // .

                    var accessor = ParsePrimaryExpression(ref reader);
                    left = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, accessor.Span.End), left, accessor);
                    return true;
                }

            case TokenKind.OpenParenthesis:
                {// Method(A, B)
                    reader.TryRead(out var token); // (
                    var arguments = ParseArgumentList(ref reader);
                    reader.TryConsume(TokenKind.CloseParenthesis, out var range, true); // )

                    left = new InvocationKoto(ref reader, left, arguments);
                    return true;
                }

            case TokenKind.LessThan:
                {// Generics<T>
                    if (!IsGenericPostfix(ref reader, left))
                    {
                        return false;
                    }

                    reader.TryRead(out var token); // <
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

                    reader.TryConsume(TokenKind.GreaterThan, out var range, true); // >
                    left = new GenericsKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, range.End), left, typeList);
                    return true;
                }

            case TokenKind.OpenBracket:
                {// Array[index]
                    reader.TryRead(out var token); // [
                    var index = ParseExpression(ref reader);
                    reader.TryConsume(TokenKind.CloseBracket, out var range, true); // ]

                    left = new IndexKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, range.End), left, index);
                    return true;
                }

            case TokenKind.PlusPlus:
                {
                    reader.TryRead(out var token);
                    left = new PostfixIncrementKoto(ref reader, token.Span, left);
                    return true;
                }

            case TokenKind.MinusMinus:
                {
                    reader.TryRead(out var token);
                    left = new PostfixDecrementKoto(ref reader, token.Span, left);
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

    private static List<Koto> ParseArgumentList(ref TokenReader reader)
    {// (arg0, arg1, )
        var tokenKind = reader.CurrentTokenKind;
        if (tokenKind == TokenKind.CloseParenthesis)
        {
            return [];
        }

        SourceSpan range;
        var arguments = new List<Koto>();

        while (tokenKind != TokenKind.Invalid &&
               tokenKind != TokenKind.CloseParenthesis)
        {
            arguments.Add(ParseExpression(ref reader));

            tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Comma)
            {
                reader.Advance();
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

        // reader.TryConsume(TokenKind.CloseParenthesis, out range);
        return arguments;
    }

    private static Koto ParsePrimaryExpression(ref TokenReader reader)
    {
Loop:
        var tokenKind = reader.CurrentTokenKind;
        switch (tokenKind)
        {
            case TokenKind.Identifier:
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

            case TokenKind.OpenParenthesis:
                {
                    reader.TryRead(out var token);

                    var expression = ParseExpression(ref reader);
                    reader.TryConsume(TokenKind.CloseParenthesis, out var range, true);

                    return new ParenthesizedKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, range.End), expression);
                }

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

    private static int GetPrefixBindingPower(TokenKind kind)
        => kind switch
        {
            TokenKind.Sharp => PrefixBindingPower,
            TokenKind.Dollar => PrefixBindingPower,
            TokenKind.Ampersand => PrefixBindingPower,
            TokenKind.Asterisk => PrefixBindingPower,
            // TokenKind.At => PrefixBindingPower,
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
            // TokenKind.AmpersandAmpersand => (20, 21),
            TokenKind.And => (20, 21),
            // TokenKind.BarBar => (10, 11),
            TokenKind.Or => (10, 11),

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

            reader.TryConsume(TokenKind.CloseParenthesis, out var closeRange, true);
            type = new TupleTypeKoto(ref reader, SourceSpan.FromBounds(openRange.Start, closeRange.End), elements);
            foreach (var element in elements)
            {
                element.Parent = type;
            }
        }
        else
        {
            type = ParseType(ref reader);
        }

        if (reader.CurrentTokenKind != TokenKind.MinusGreaterThan)
        {
            return type;
        }

        reader.Advance();
        var returnType = ParseDeclarationType(ref reader);
        var functionType = new FunctionTypeKoto(
            ref reader,
            SourceSpan.FromBounds(type.Span.Start, returnType.Span.End),
            type,
            returnType);
        type.Parent = functionType;
        returnType.Parent = functionType;
        return functionType;
    }

    private static List<TypeKoto>? ParseGenericArguments(ref TokenReader reader)
    {// <s/T, T2>
        Debug.Assert(reader.CurrentTokenKind == TokenKind.LessThan);
        reader.Advance();

        List<TypeKoto>? list = default;
        while (reader.CanRead)
        {
            if (reader.CurrentTokenKind == TokenKind.GreaterThan)
            {// >
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
