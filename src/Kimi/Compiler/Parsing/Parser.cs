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

    /// <summary>
    /// Prefix <c>try</c> binds below <c>@</c> (90) and above the multiplicative operators (80):
    /// <c>try x@move</c> is <c>try (x@move)</c> and <c>try a * b</c> is <c>(try a) * b</c>.
    /// </summary>
    private const int TryBindingPower = 85;
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
                TokenKind.At, // Only @follow is postfix (SPEC 13.1); other @ operations keep their infix precedence.
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
                TokenKind.EqualsGreaterThan, TokenKind.Comma, TokenKind.Exclamation, TokenKind.CloseParenthesis, TokenKind.CloseBracket,
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
    /// <param name="mergeFragmentLayouts">Whether equivalent Layout specifications from separate container fragments are written once.</param>
    public static void UnparseAttribute(AttributeKoto? attribute, ref IndentedStringBuilder builder, KotoWriteOptions options, bool mergeFragmentLayouts = false)
    {
        if (attribute is null)
        {
            return;
        }

        if (mergeFragmentLayouts)
        {
            string? mode = null;
            var fragment = -1;
            for (var current = attribute; current is not null; current = current.AttributeChain)
            {
                if (current.IdentifierKoto is not IdentifierNameKoto { IdentifierName: "Layout" })
                {
                    continue;
                }

                if (current.LayoutMode is not { } currentMode || (mode is not null && mode != currentMode) || fragment == current.FragmentOrdinal)
                {
                    mergeFragmentLayouts = false;
                    break;
                }

                mode = currentMode;
                fragment = current.FragmentOrdinal;
            }
        }

        var layoutWritten = false;
        WriteChain(attribute, ref builder, mergeFragmentLayouts, ref layoutWritten);
        builder.AppendTrailingSpaceOrLineFeed(options);

        static void WriteChain(AttributeKoto attribute, ref IndentedStringBuilder builder, bool mergeFragmentLayouts, ref bool layoutWritten)
        {
            if (attribute.AttributeChain is { } previous)
            {
                WriteChain(previous, ref builder, mergeFragmentLayouts, ref layoutWritten);
                if (mergeFragmentLayouts && layoutWritten && attribute.LayoutMode is not null)
                {
                    return;
                }

                builder.Append(' ');
            }

            attribute.WriteTo(ref builder);
            if (mergeFragmentLayouts)
            {
                layoutWritten |= attribute.LayoutMode is not null;
            }
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

    /// <summary>Parses a function header after its keyword: the Name, Type parameters, parameters and result (SPEC 7.1, Appendix F FunctionHeader).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="anonymous">Whether this is a function expression, whose Name is omitted and whose parameters may omit their Types.</param>
    /// <param name="constructor">Whether this is an init declaration; the reader stands at <c>init</c>.</param>
    /// <param name="specialization">Whether the generic list contains specialization arguments.</param>
    /// <returns>The parsed function without its body, or <see langword="null"/> after an error that leaves no header to keep.</returns>
    public static FunctionKoto? ParseFuncDeclaration(ref TokenReader reader, bool anonymous = false, bool constructor = false, bool specialization = false)
    {
        var context = reader.TakeContext();
        var captures = anonymous && reader.CurrentTokenKind == TokenKind.OpenBracket ? ParseCaptures(ref reader) : null;
        var methodToken = reader.CurrentToken;
        if (!TryParseFunctionName(ref reader, anonymous, constructor, out var methodName))
        {
            return Omitted(ref reader, anonymous);
        }

        var genericArguments = reader.CurrentTokenKind == TokenKind.LessThan
            ? ParseGenericArguments(ref reader, allowLength: true, specialization: specialization)
            : null;
        RejectCallableOriginList(ref reader);
        if (!reader.TryConsume(TokenKind.OpenParenthesis, out _, true) ||
            !TryParseParameterList(ref reader, anonymous, constructor, specialization, out var parameters, out var nameBoundary, out var closeParenthesisRange))
        {
            return Omitted(ref reader, anonymous);
        }

        Koto? returnType = null;
        var end = closeParenthesisRange.End;
        if (reader.TryConsume(TokenKind.MinusGreaterThan, out var returnArrowRange, false))
        {
            returnType = ParseFunctionReturnType(ref reader, returnArrowRange);
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
        functionKoto.NameBoundaryIndex = nameBoundary;
        functionKoto.HeaderEnd = closeParenthesisRange.End;
        functionKoto.IsAnonymous = anonymous;
        functionKoto.IsConstructor = constructor;
        functionKoto.IsSpecialization = specialization;
        functionKoto.SetCaptures(captures);
        if (anonymous)
        {
            if (methodName.Length != 0)
            {
                reader.Unexpected(SyntaxForm.FunctionExpressionName, methodToken.Span);
            }
        }
        else
        {
            reader.Document(functionKoto, functionKoto.Span, context.AttributeKoto);
            reader.Hover(functionKoto, methodToken.Span);
        }

        if (constructor)
        {
            if (reader.TryConsume(TokenKind.Colon))
            {
                ParseConstructorInitializer(ref reader, functionKoto);
            }

            // A constructor accepts an access modifier and a common Body (SPEC 6.2.3).
            if (genericArguments is not null || returnType is not null ||
                context.AttributeKoto is not null || context.ModifierKind.Judged() != context.ModifierKind.ExtractAccessibilityModifiers())
            {
                // The attributes it reports are kept as its recovery, so they mark nothing and are not resolved (SPEC 6.5).
                if (functionKoto.Unexpected(SyntaxForm.ConstructorHeader) is { } decorated)
                {
                    for (var attribute = context.AttributeKoto; attribute is not null; attribute = attribute.AttributeChain)
                    {
                        reader.CodeContext.RecordRecovery(attribute, decorated);
                    }
                }
            }
        }

        if (specialization && (genericArguments is null || context.AttributeKoto is not null || context.ModifierKind.Judged() != 0))
        {
            // Without Type arguments the specialization cannot be matched, so its Binding rests on this Error; a decoration leaves it checkable.
            var key = functionKoto.Unexpected(SyntaxForm.SpecializationHeader);
            if (genericArguments is null && key is { } cause)
            {
                reader.CodeContext.RecordRecovery(functionKoto, cause);
            }
        }

        if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
        {
            reader.ExpectLineEnd(bodyMayFollow: true);
        }

        return functionKoto;

        // A declaration whose header failed is skipped with its body; a function expression leaves the rest to its expression.
        static FunctionKoto? Omitted(ref TokenReader reader, bool anonymous)
        {
            if (!anonymous)
            {
                OmitDeclaration(ref reader, reader.Diagnostic.LastError);
            }

            return null;
        }
    }

    /// <summary>
    /// Reads the Name of a function declaration, <c>init</c> of a constructor, or the omitted Name of a function expression; a
    /// qualified Name is read in full and reported. A failed Name ends the header, which the caller then skips with its body.
    /// </summary>
    private static bool TryParseFunctionName(ref TokenReader reader, bool anonymous, bool constructor, [NotNullWhen(true)] out string? name)
    {
        // An attribute precedes the declaration (SPEC 6.5); one before the Name, or before a function expression's parameter
        // list, is reported and the header continues after it.
        if (reader.CurrentTokenKind == TokenKind.Sharp)
        {
            reader.ReportMisplacedAttributes(anonymous ? TokenKind.OpenParenthesis : TokenKind.Invalid);
        }

        var token = reader.CurrentToken;
        if (anonymous && token.Kind == TokenKind.OpenParenthesis)
        {
            name = string.Empty;
            return true;
        }

        if (anonymous && !(token.Kind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.OpenParenthesis))
        {
            // A function expression continues with its parameter list; only a Name before one is parsed further, to be reported as misplaced.
            reader.Expect(SyntaxForm.OpenParenthesis);
            name = null;
            return false;
        }

        if (constructor && token.Kind == TokenKind.Init)
        {
            reader.Advance();
            name = "init";
        }
        else if (!reader.TryReadName(out name, out _))
        {
            return false;
        }

        while (reader.TryConsume(TokenKind.Dot))
        {
            reader.Unexpected(SyntaxForm.QualifiedFunctionName, token.Span);
            if (!reader.TryReadName(out var memberName, out _))
            {
                name = null;
                return false;
            }

            name += "." + memberName;
        }

        return true;
    }

    /// <summary>
    /// Parses the parameters after the opening parenthesis through the closing one (Appendix F ParameterList): attributes before each
    /// parameter, the one <c>!</c> boundary before the named section, and the separating commas.
    /// </summary>
    /// <param name="reader">The token reader positioned after <c>(</c>.</param>
    /// <param name="anonymous">Whether the list belongs to a function expression.</param>
    /// <param name="constructor">Whether the list belongs to a constructor.</param>
    /// <param name="specialization">Whether the list belongs to a specialization.</param>
    /// <param name="parameters">The parameters, or <see langword="null"/> when none was kept.</param>
    /// <param name="nameBoundary">The index of the first named parameter, or -1 without a boundary.</param>
    /// <param name="close">The closing parenthesis.</param>
    /// <returns><see langword="false"/> when the list is not closed; the header is then dropped.</returns>
    private static bool TryParseParameterList(
        ref TokenReader reader,
        bool anonymous,
        bool constructor,
        bool specialization,
        out List<FunctionParameterKoto>? parameters,
        out int nameBoundary,
        out SourceSpan close)
    {
        parameters = null;
        nameBoundary = -1;
        var afterComma = false;
        var precedingComma = default(SourceSpan);
        while (reader.CanRead)
        {
            // Attributes precede a parameter's Name, and the one boundary precedes the named section; either may follow the other.
            var start = reader.Position;
            var attributed = false;
            reader.SkipSeparators();
            while (true)
            {
                if (reader.CurrentTokenKind == TokenKind.Sharp)
                {
                    attributed |= ParseAttributeKoto(ref reader) is not null;
                    reader.SkipSeparators();
                    continue;
                }

                if (reader.CurrentTokenKind != TokenKind.Exclamation)
                {
                    break;
                }

                var boundarySpan = reader.Read().Span;
                if (afterComma)
                {
                    reader.Diagnostic.Add(precedingComma, DiagnosticCode.ArgumentBoundaryComma_Kd);
                }

                if (nameBoundary >= 0)
                {
                    reader.Diagnostic.Add(boundarySpan, DiagnosticCode.DuplicateArgumentBoundary_Kd);
                }

                if (anonymous || specialization)
                {
                    reader.Diagnostic.Add(boundarySpan, DiagnosticCode.ArgumentBoundaryContext_Kd);
                }

                if (nameBoundary < 0)
                {
                    nameBoundary = parameters?.Count ?? 0;
                }

                reader.SkipSeparators();
                if (reader.CurrentTokenKind == TokenKind.Comma)
                {
                    reader.Diagnostic.Add(reader.Read().Span, DiagnosticCode.ArgumentBoundaryComma_Kd);
                    reader.SkipSeparators();
                }
            }

            if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
            {
                if (attributed)
                {
                    // Attributes before the closer precede no parameter: its Name was expected there, and they attach to nothing,
                    // neither to the result Type nor to the body.
                    reader.Expect(SyntaxForm.Name);
                    _ = reader.PopAttribute();
                }

                break;
            }

            var attribute = reader.PopAttribute();
            if (!reader.CanRead)
            {
                break;
            }

            if (TryParseParameter(ref reader, anonymous, constructor, parameters, attribute, out var parameter))
            {
                (parameters ??= new(4)).Add(parameter);
            }

            reader.SkipSeparators();
            if (reader.CurrentTokenKind == TokenKind.Comma)
            {
                precedingComma = reader.Read().Span;
                afterComma = true;
            }
            else if (reader.CurrentTokenKind == TokenKind.Exclamation)
            {
                afterComma = false;
            }
            else if (reader.CurrentTokenKind != TokenKind.CloseParenthesis)
            {
                // The part of the parameter before what the comma's report blamed is a recovery, as a list item is (DIAGNOSTICS.md §4.4).
                var cause = reader.Expect(SyntaxForm.Comma);
                if (parameter is not null)
                {
                    RecoverItem(ref reader, parameter.DefaultValue ?? parameter.Type, cause);
                }

                SkipParameter(ref reader);
                reader.TryConsume(TokenKind.Comma);
            }

            if (reader.Position == start)
            {
                break; // Nothing of the list could be read here; the closer's recovery takes the rest.
            }
        }

        if (reader.ExpectCloser(TokenKind.CloseParenthesis, out close) is not null && close == default)
        {
            return false; // The list ended without a closer, written or supplied by the tokenizer.
        }

        if (nameBoundary >= 0 && nameBoundary == (parameters?.Count ?? 0))
        {
            reader.Diagnostic.Add(close, DiagnosticCode.EmptyNamedParameterSection_Kd);
        }

        return true;
    }

    /// <summary>Parses one parameter: its external Name, optional internal Name, Type and default (Appendix F ParameterCore).</summary>
    /// <returns><see langword="false"/> after an error, with the rest of the parameter skipped.</returns>
    private static bool TryParseParameter(
        ref TokenReader reader,
        bool anonymous,
        bool constructor,
        List<FunctionParameterKoto>? previous,
        AttributeKoto? attribute,
        [NotNullWhen(true)] out FunctionParameterKoto? parameter)
    {
        parameter = null;
        if (!reader.TryReadName(out var externalName, out var externalNameSpan))
        {
            SkipParameter(ref reader);
            return false;
        }

        if (reader.TryConsume(TokenKind.Question, out var markerSpan, false))
        {
            reader.Diagnostic.Add(markerSpan, DiagnosticCode.ParameterNameMarker_Kd);
        }

        if (!anonymous && previous is not null)
        {
            foreach (var earlier in previous)
            {
                if (earlier.ExternalName == externalName)
                {
                    reader.Diagnostic.AddSyntax(
                        externalNameSpan,
                        DiagnosticCode.DuplicateExternalParameterName_Kd,
                        externalName,
                        related: [reader.Diagnostic.Relate("declaration", earlier.ExternalNameSpan, reader.Diagnostic.Document, "first external parameter name")]);
                    break;
                }
            }
        }

        var internalName = externalName;
        if (reader.TryConsume(TokenKind.EqualsGreaterThan) && !reader.TryReadName(out internalName, out _))
        {
            SkipParameter(ref reader);
            return false;
        }

        var allowsReceiverShorthand = !anonymous && !constructor && externalName == "self" && internalName == "self";
        Koto parameterType;
        if (reader.TryConsume(TokenKind.Colon))
        {
            parameterType = ParseDeclarationType(ref reader);
        }
        else if (anonymous || allowsReceiverShorthand)
        {
            parameterType = new SyntaxFormKoto(ref reader, externalNameSpan, KotoKind.InferredType, "_", []);
        }
        else
        {
            // The recovery Type stands for the missing annotation; the rest of the parameter is skipped (SPEC 23.3.6.4).
            parameterType = new ErrorKoto(ref reader, new SourceSpan(reader.PreviousSyntaxEnd, 0)) { Cause = reader.Expect(SyntaxForm.ParameterType) };
            SkipParameter(ref reader);
        }

        Koto? defaultValue = null;
        if (reader.TryConsume(TokenKind.Equals))
        {
            defaultValue = ParseRequiredExpression(ref reader);
        }

        // A specialization's restated defaults and attributes are Binding's requirement (SPEC 8.8.2); the parser keeps them. A
        // misplaced attribute already reported before the list is kept here for the tree, not written on the parameter.
        if (anonymous && (internalName != externalName || defaultValue is not null || HasWrittenAttribute(attribute)))
        {
            reader.Unexpected(SyntaxForm.FunctionExpressionParameter, externalNameSpan);
        }

        parameter = new(externalName, internalName, parameterType, defaultValue, attribute) { ExternalNameSpan = externalNameSpan };
        return true;
    }

    /// <summary>Gets whether an attribute chain holds an attribute in its place: one reported as misplaced is kept only so the
    /// source round-trips, and it marks nothing (DIAGNOSTICS.md §4.4).</summary>
    /// <param name="attribute">The first attribute of the chain.</param>
    /// <returns><see langword="true"/> when an attribute of the chain was not reported as misplaced.</returns>
    internal static bool HasWrittenAttribute(AttributeKoto? attribute)
    {
        for (; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (attribute.CodeContext.RecoveryCause(attribute) is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reports the pending attributes before an item that takes none (SPEC 6.5), such as a Constraint, an Origin relation
    /// or an enum Case; each is kept for the item's first node as the recovery of its report, as a misplaced attribute is.</summary>
    /// <param name="reader">The token reader.</param>
    internal static void ReportPendingAttributes(ref TokenReader reader)
    {
        for (var attribute = reader.AttributeKoto; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (reader.CodeContext.RecoveryCause(attribute) is null)
            {
                reader.CodeContext.RecordRecovery(attribute, reader.Unexpected(SyntaxForm.Attribute, attribute.Span));
            }
        }
    }

    private static void SkipParameter(ref TokenReader reader)
        => reader.SkipListItem();

    /// <summary>Parses the result Type after a function header's arrow; a line that ends at the arrow leaves a recovery Type.</summary>
    private static Koto ParseFunctionReturnType(ref TokenReader reader, SourceSpan arrow)
    {
        reader.ReportMisplacedAttributes(); // A Type takes no attribute (SPEC 6.5); the result Type may follow it.
        if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock or TokenKind.EqualsGreaterThan)
        {
            reader.Diagnostic.Add(arrow, DiagnosticCode.MissingReturnType_Kd);
            return new ErrorKoto(ref reader, arrow) { Cause = reader.Diagnostic.LastError };
        }

        return ParseFunctionResult(ref reader);
    }

    /// <summary>Parses <c>base(arguments)</c> after the colon of a constructor header (SPEC 6.2.3.1).</summary>
    private static void ParseConstructorInitializer(ref TokenReader reader, FunctionKoto constructor)
    {
        var baseSpan = reader.CurrentTokenRange;
        if (!reader.Expect(TokenKind.Base) || !reader.Expect(TokenKind.OpenParenthesis))
        {
            // The initializer is skipped up to the body; the constructor is a recovery, so its own checks rest on the one
            // Error and its body is bound but not analyzed for ownership.
            reader.CodeContext.RecordRecovery(constructor, reader.Diagnostic.LastError!.Value);
            reader.SkipUntil(TokenKind.EqualsGreaterThan, TokenKind.Separator, TokenKind.EndBlock);
            return;
        }

        var arguments = ParseArgumentList(ref reader, out var labels);
        reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);

        var target = new SyntaxFormKoto(ref reader, baseSpan, KotoKind.ConstructorReference, "base", []);
        constructor.SetBaseInitializer(new InvocationKoto(ref reader, SourceSpan.FromBounds(baseSpan.Start, Math.Max(baseSpan.End, close.End)), target, arguments, labels));
    }

    /// <summary>Parses a Declaration Container header after its keyword, according to the capabilities of its kind (SPEC 6.1, 8.4).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="declarationKind">The declaration keyword kind; a struct, enum or Contract takes Type parameters, a struct or enum an Origin header, a struct or Contract a base list.</param>
    /// <returns>The name, generic parameters, origin names and bases; the reader stands at the body's <see cref="TokenKind.StartBlock"/> when one follows.
    /// The Name is <see langword="null"/> after an Error at it, and the caller skips the declaration with its body.</returns>
    internal static (string? Name, List<TypeKoto>? GenericArguments, List<string>? Origins, Koto[]? Bases) ParseDeclarationContainerHeader(ref TokenReader reader, TokenKind declarationKind)
    {
        var supportsGenerics = declarationKind is TokenKind.Struct or TokenKind.Enum or TokenKind.Contract;
        var supportsOrigins = declarationKind is TokenKind.Struct or TokenKind.Enum;
        List<TypeKoto>? genericArguments = default;
        List<string>? origins = default;
        Koto[]? bases = null;
        if (!reader.TryReadName(out var name, out _))
        {
            goto Exit;
        }

        if (supportsGenerics && reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = ParseGenericArguments(ref reader);
        }

        if (supportsOrigins)
        {
            origins = ParseOriginParameters(ref reader);
        }

        if (declarationKind is TokenKind.Struct or TokenKind.Contract && reader.TryConsume(TokenKind.Colon))
        {
            var types = default(TemporaryKotoList);
            do
            {
                types.Add(ParseDeclarationType(ref reader));
            }
            while (reader.TryConsume(TokenKind.Comma));
            bases = types.ToArray();
            if (declarationKind == TokenKind.Struct && bases.Length != 1)
            {
                // The recovery keeps the first base, so the struct is checked as derived from it alone.
                reader.Unexpected(SyntaxForm.BaseList, SourceSpan.FromBounds(bases[0].Span.Start, bases[^1].Span.End));
                bases = bases[..1];
            }
        }

        // The header ends with its line; a body may follow on indented lines.
        reader.ExpectLineEnd(bodyMayFollow: true);
        reader.TrySkipSeparatorsTo(TokenKind.StartBlock);

Exit:
        return (name, genericArguments, origins, bases);
    }

    private static List<string>? ParseOriginParameters(ref TokenReader reader)
    {
        if (!reader.TryConsume(TokenKind.OpenBrace, out var open, false))
        {
            return null;
        }

        // SPEC 15.3.2: a header names one or more slots; a Type without own slots omits it, so empty braces are an error and
        // recover as an omitted header.
        var list = new OriginNameList();
        var reported = false;
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseBrace or TokenKind.EndBlock))
        {
            var token = reader.CurrentToken;
            if (!token.Kind.IsIdentifierOrContextualKeyword() || token.Kind == TokenKind.Static)
            {
                // Not a slot Name; the rest of the item is skipped, so a slot after the next comma is still read.
                reader.Expect(SyntaxForm.OriginSlotName, token);
                reader.SkipListItem();
                reported = true;
            }
            else
            {
                reader.Advance();
                list.Add(reader.GetIdentifier(token), token.Span);
                if (reader.TryConsume(TokenKind.Colon, out var relationToken, false))
                {
                    reader.Diagnostic.Add(relationToken, DiagnosticCode.OriginSchemaRelation_Kd);
                    reader.SkipListItem();
                }
            }

            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }
        }

        if (list.Count == 0 && !reported && reader.CurrentTokenKind == TokenKind.CloseBrace)
        {
            reader.Expect(SyntaxForm.OriginSlotName);
        }

        reader.ExpectCloser(TokenKind.CloseBrace, out var close);

        // The header is a related location of the diagnostics that name its slots (SPEC 15.3.2).
        list.HeaderSpan = new(open.Start, Math.Max(close.End, list.Spans.Count == 0 ? open.End : list.Spans[^1].End) - open.Start);
        return list.Count == 0 ? null : list;
    }

    /// <summary>
    /// Reads the Name of a let, var, computed or property declaration after its keyword. A keyword or other token there is
    /// where the Name was expected, and an invalid spelling is its own lexical Error; either way the rest of the declaration's
    /// line and the body it would have introduced are skipped, so nothing of the declaration is kept.
    /// </summary>
    private static IdentifierNameKoto? ParseDeclarationName(ref TokenReader reader)
    {
        reader.ReportMisplacedAttributes();
        var token = reader.CurrentToken;
        if (!token.Kind.IsIdentifierOrContextualKeyword())
        {
            OmitDeclaration(ref reader, reader.Expect(SyntaxForm.Name));
            return null;
        }

        reader.Advance();
        if (IdentifierNameKoto.TryCreate(ref reader, token, out var name))
        {
            return name;
        }

        OmitDeclaration(ref reader, reader.Diagnostic.LastError); // The invalid identifier is reported once.
        return null;
    }

    // SPEC 4.3.4: only a complete, unparenthesized declaration initializer is the directive. Eligibility is a Binding
    // check; even an ineligible declaration never falls back to a same-named value.
    private static Koto ParseVariableInitializer(ref TokenReader reader)
    {
        var value = ParseRequiredExpression(ref reader);
        if (value is not IdentifierNameKoto { IdentifierName: "noinit" })
        {
            return value;
        }

        var directive = new NoInitKoto(ref reader, value.Span);
        directive.SetAttributeChain(value.AttributeChain);
        return directive;
    }

    /// <summary>Parses a local binding declaration after its keyword (SPEC 6.4).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <returns>The parsed declaration, or <see langword="null"/> after an error.</returns>
    private static FieldKoto? ParseField(ref TokenReader reader, Token token)
    {
        var variableContext = reader.TakeContext();
        if (ParseDeclarationName(ref reader) is not { } nameKoto)
        {
            return null;
        }

        Koto? typeKoto = default;
        var inferredArrayElement = false;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {
            reader.ReportMisplacedAttributes();

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
            initializerKoto = ParseVariableInitializer(ref reader);
        }

        // A binding states its Type or has an initializer, and an inferred element Type needs the initializer; the recovery
        // Type stands for what is missing, so the uses of the binding rest on this Error (SPEC 23.3.6.4).
        if (typeKoto is null && initializerKoto is null)
        {
            typeKoto = new ErrorKoto(ref reader, new SourceSpan(nameKoto.Span.End, 0)) { Cause = reader.Expect(SyntaxForm.TypeOrInitializer) };
        }
        else if (inferredArrayElement && initializerKoto is null)
        {
            typeKoto = new ErrorKoto(ref reader, typeKoto!.Span) { Cause = reader.Expect(SyntaxForm.Initializer) };
        }

        reader.RestoreContext(variableContext);

        var fieldKoto = new FieldKoto(ref reader, token, nameKoto, typeKoto, initializerKoto);
        reader.Document(fieldKoto, SourceSpan.FromBounds(token.Span.Start, nameKoto.Span.End), variableContext.AttributeKoto);

        ParseAttachedOriginBlock(ref reader, fieldKoto);
        reader.ExpectLineEnd();
        return fieldKoto;
    }

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
            typeKoto = ParseType(ref reader);
        }
        else if (computed)
        {
            // A computed Property or requirement states its Type; the recovery Type stands for the missing annotation (SPEC 23.3.6.4).
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
            // A stored Property states its Type or has an initializer; the recovery Type stands for the missing one (SPEC 23.3.6.4).
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
            if (TryConsumeUnavailableModifiers(ref reader, accessor: true))
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
            if (TryConsumeUnavailableModifiers(ref reader, accessor: true))
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

        property.CompleteSpan(Math.Max(property.Span.End, blockStart.End));
        reader.Expect(SyntaxForm.Accessor);
        return unavailableAccessor;
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

            receiverType = ParseDeclarationType(ref reader);
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

                valueType = ParseDeclarationType(ref reader);
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

        return ParseDeclarationType(ref reader);
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
        // An accessor list takes no attributes (SPEC 6.5): one is reported and kept for the accessor, which is still read.
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

    /// <summary>Gets the modifiers a declaration check judges: each 'static', and an 'open' anywhere but directly before 'struct', is
    /// reported where it is read and kept only for recovery (SPEC 6.1, 6.2.2), so no declaration check reports it again.</summary>
    /// <param name="kind">The written modifiers.</param>
    /// <returns>The modifiers other than static and open.</returns>
    internal static ModifierKind Judged(this ModifierKind kind)
        => kind & ~(ModifierKind.Static | ModifierKind.Open);

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
    /// <returns>Whether an unavailable declaration header was recognized and needs recovery.</returns>
    public static bool ConsumeAttributeAndModifier(
        ref TokenReader reader,
        out bool isEnd,
        bool allowCompileTimeDirectives = false)
    {
        reader.ClearContext();

        var inspectHeader = true;
        var openSpan = default(SourceSpan);
        while (reader.CanRead)
        {
            if (inspectHeader && allowCompileTimeDirectives && TryConsumeUnavailableModifiers(ref reader))
            {
                isEnd = false;
                return true;
            }

            inspectHeader = false;
            var tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Identifier && reader.PeekKind(1) == TokenKind.Func && reader.IsCurrentIdentifier(Constants.UnsafeKeyword))
            {
                ReadFlag(ref reader, ModifierKind.Unsafe);
                continue;
            }

            switch (tokenKind)
            {
                case TokenKind.Separator:
                    if (reader.ModifierKind != ModifierKind.NoModifier)
                    {
                        // Modifiers share the line of the word that introduces their declaration (Appendix F.3), so modifiers
                        // that end their line introduce nothing: the declaration is missing after them and they are dropped,
                        // while attributes before them still precede the next declaration. A static, already reported, is
                        // no modifier that needs one. An indented body after them would be the missing declaration's, so it is
                        // skipped with the header, as after a header that fails (OmitDeclaration).
                        if ((reader.ModifierKind & ~ModifierKind.Static) != ModifierKind.NoModifier)
                        {
                            var cause = reader.Expect(SyntaxForm.Declaration);
                            if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                            {
                                OmitDeclaration(ref reader, cause);
                                inspectHeader = true;
                                continue;
                            }
                        }

                        reader.ModifierKind = ModifierKind.NoModifier;
                    }

                    reader.Advance();
                    inspectHeader = true;
                    continue;

                case TokenKind.Static:
                    // static is not a declaration modifier; each is reported here and kept only for recovery (SPEC 6.1), so
                    // neither a repetition nor a declaration check reports it again (Judged).
                    reader.Unexpected(SyntaxForm.StaticModifier);
                    reader.ModifierKind |= ModifierKind.Static;
                    reader.Advance();
                    continue;

                case TokenKind.Open:
                    openSpan = reader.CurrentTokenRange;
                    ReadFlag(ref reader, ModifierKind.Open);
                    continue;

                case TokenKind.Public:
                case TokenKind.Protected:
                case TokenKind.Private:
                case TokenKind.Internal:
                    ReadAccessibility(ref reader, GetAccessibilityModifier(tokenKind));
                    continue;

                case TokenKind.Sharp:
                    inspectHeader = true;
                    if (reader.ModifierKind != ModifierKind.NoModifier)
                    {
                        if (reader.PeekKind(1) is TokenKind.If or TokenKind.Switch or TokenKind.Case)
                        {
                            // A directive is an item of its own (Appendix F.8): the modifiers before it introduce nothing.
                            if ((reader.ModifierKind & ~ModifierKind.Static) != ModifierKind.NoModifier)
                            {
                                reader.Expect(SyntaxForm.Declaration);
                            }

                            reader.ModifierKind = ModifierKind.NoModifier;
                        }
                        else
                        {
                            // Attributes precede a declaration's modifiers (Appendix F.3): one after them is misplaced, and it
                            // is kept for the declaration as that Error's recovery.
                            reader.ReportMisplacedAttributes();
                            continue;
                        }
                    }

                    if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.If)
                    {
                        ParseCompileTimeIfPrefix(ref reader);
                        reader.HasCompileTimeIfPrefix = true;
                        continue;
                    }

                    if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.Switch)
                    {
                        isEnd = false;
                        return false;
                    }

                    if (reader.PeekKind(1) == TokenKind.Case)
                    {
                        if (allowCompileTimeDirectives)
                        {
                            // Its Condition is validated and its body is parsed as excluded syntax (SPEC 19.5).
                            reader.HasCompileTimeIfPrefix |= ParseOrphanCompileTimeCase(ref reader);
                            continue;
                        }

                        reader.AddDiagnostic(DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd);
                        SkipItemForRecovery(ref reader);
                        reader.ClearContext();
                        continue;
                    }

                    _ = ParseAttributeKoto(ref reader);
                    continue;

                default:
                    if (tokenKind == TokenKind.EndBlock)
                    {
                        ReportDanglingHeader(ref reader);
                    }
                    else if (reader.ModifierKind.HasFlag(ModifierKind.Open) && (tokenKind != TokenKind.Struct || reader.PeekKind(-1) != TokenKind.Open))
                    {
                        // open applies only to structures and immediately precedes struct (SPEC 6.2.2, F.3). It is reported here
                        // only, so the declaration's own checks do not judge it again (Judged).
                        reader.Unexpected(SyntaxForm.OpenModifier, openSpan);
                    }

                    isEnd = false;
                    return false;
            }
        }

        ReportDanglingHeader(ref reader);
        isEnd = true;
        return false;

        // Attributes, modifiers or a compile-time prefix at the end of a body or of the source introduce no declaration (SPEC 6.5):
        // the declaration is missing after them, and the pending header is dropped.
        static void ReportDanglingHeader(ref TokenReader reader)
        {
            if (reader.HasCompileTimeIfPrefix || reader.AttributeKoto is not null || reader.ModifierKind != ModifierKind.NoModifier)
            {
                reader.Expect(SyntaxForm.Declaration);
                reader.ClearContext();
            }
        }

        static void ReadFlag(ref TokenReader reader, ModifierKind flag)
        {
            if (reader.ModifierKind.HasFlag(flag))
            {
                reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, reader.GetSpan(reader.CurrentToken).ToString());
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

    // Look only through a same-header modifier sequence. These spellings remain
    // ordinary identifiers everywhere else, including calls and declaration names.
    private static bool TryConsumeUnavailableModifiers(ref TokenReader reader, bool accessor = false)
    {
        var unavailable = default(Token);
        var previousEnd = reader.CurrentTokenRange.Start;
        for (var offset = 0; offset < reader.Remaining; offset++)
        {
            var token = reader.PeekToken(offset);
            if (!reader.SameLine(previousEnd, token.Span.Start))
            {
                return false;
            }

            previousEnd = token.Span.End;
            if (token.Kind == TokenKind.Identifier)
            {
                var text = reader.GetSpan(token);
                if (text is "virtual" or "override" or "abstract")
                {
                    if (unavailable.Kind == TokenKind.Invalid)
                    {
                        unavailable = token;
                    }

                    continue;
                }

                if (text is "unsafe")
                {
                    continue;
                }

                if (!accessor && text is "specialize" && reader.PeekKind(offset + 1) == TokenKind.Func)
                {
                    continue;
                }
            }

            if (token.Kind is TokenKind.Public or TokenKind.Internal or TokenKind.Private or
                TokenKind.Protected or TokenKind.Open or TokenKind.Static)
            {
                continue;
            }

            var introducer = accessor
                ? token.Kind is TokenKind.Get or TokenKind.Set
                : token.Kind is TokenKind.RootGroup or TokenKind.Group or TokenKind.Struct or TokenKind.Enum or
                    TokenKind.Contract or TokenKind.Extension or TokenKind.Func or TokenKind.Init or TokenKind.Drop or
                    TokenKind.Let or TokenKind.Var or TokenKind.Computed or TokenKind.Property or TokenKind.Associate;
            if (!introducer || unavailable.Kind == TokenKind.Invalid)
            {
                return false;
            }

            reader.Diagnostic.Add(unavailable.Span, DiagnosticCode.UnavailableFeature_Kd, reader.GetSpan(unavailable).ToString());
            reader.Advance(offset);
            return true;
        }

        return false;
    }

    internal static void SkipUnavailableDeclaration(ref TokenReader reader)
    {
        SkipItemForRecovery(ref reader);
        reader.ClearContext();
    }

    /// <summary>Parses a type expression.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed type node.</returns>
    public static Koto ParseType(ref TokenReader reader)
        => ParseDeclarationType(ref reader);

    private static Koto ParseType(ref TokenReader reader, bool parseOrigin, bool disambiguateGenerics = false, bool optionalSuffix = true)
    {
        var start = reader.CurrentTokenRange.Start;
        var left = ParseTypeInternal(ref reader, disambiguateGenerics);
        if (left is ErrorKoto)
        {
            return left;
        }

        while (reader.CanRead)
        {
            var tokenKind = reader.CurrentTokenKind;
            if (tokenKind == TokenKind.Dot)
            {
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

        if (left is not TypeKoto && left is not ErrorKoto)
        {
            left = new TypeSemanticsKoto(ref reader, SourceSpan.FromBounds(start, left.Span.End), left);
        }

        if (parseOrigin)
        {
            left = ParseTypeOrigin(ref reader, left, reportLegacyBorrow: !disambiguateGenerics);
        }

        if (disambiguateGenerics && reader.CurrentTokenKind == TokenKind.Question &&
            left is TypeSemanticsKoto { Type: null } shorthand && CompilerHelper.TryParse(shorthand.Identifier, out _))
        {
            // The shorthand names no Type to make optional; the target rests on the Error.
            var cause = reader.Unexpected(SyntaxForm.SemanticsShorthandSuffix);
            var question = reader.Read();
            return new ErrorKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, question.Span.End)) { Cause = cause };
        }

        return optionalSuffix ? ParseOptionalSuffix(ref reader, left, parseBorrowOrigin: !disambiguateGenerics) : left;

        // After a '.', a member is a Name, whose Type arguments the loop reads, or a parenthesized Contract (SPEC 8.4.3); a fixed
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

    /// <summary>Reports an Origin annotation on a borrow layer of an adaptation target.</summary>
    /// <param name="type">The parsed target.</param>
    /// <returns>The key of the first Error, or <see langword="null"/> when every layer is unannotated.</returns>
    private static DiagnosticKey? CheckAdaptationOrigins(Koto type)
    {
        DiagnosticKey? cause = null;
        while (true)
        {
            if (type is ParenthesizedTypeKoto grouped)
            {
                type = grouped.Type;
            }
            else if (type is TypeSemanticsKoto { Type: { } inner } layer)
            {
                if (!layer.IsTransparentWrapper && layer.HasOrigin)
                {
                    cause ??= layer.Unexpected(SyntaxForm.AdaptationOrigin);
                }

                type = inner;
            }
            else
            {
                // Stop at a Core. Payloads and function contracts contain independent
                // complete Types; their annotations do not prescribe this operation's borrow.
                return cause;
            }
        }
    }

    private static Koto ParseTypeOrigin(ref TokenReader reader, Koto type, bool reportLegacyBorrow = true)
    {
        if (reader.CurrentTokenKind != TokenKind.OpenBrace)
        {
            return type;
        }

        var origin = ParseOriginBraces(ref reader);
        if (type is ParenthesizedTypeKoto or TupleTypeKoto or FunctionTypeKoto or FixedArrayTypeKoto ||
            type is TypeSemanticsKoto { Type: not null, IsTransparentWrapper: false })
        {
            reader.Diagnostic.Add(origin.Span, DiagnosticCode.OriginBindingSetTarget_Kd);
        }

        var annotated = type as TypeSemanticsKoto ?? new TypeSemanticsKoto(ref reader, type.Span, type);
        annotated.SetOrigin(origin.Expression, origin.Arguments, origin.Span.End);
        if (origin.InvalidContent || origin.Arguments is not null ||
            (origin.Expression is not ErrorKoto && origin.Expression is not IdentifierNameKoto { IdentifierName: not ("static" or "_") }))
        {
            reader.Diagnostic.Add(origin.Span, DiagnosticCode.OriginBindingSetName_Kd);
        }

        annotated.MarkBindingSet(reader.CurrentTokenKind == TokenKind.Slash);
        if (reportLegacyBorrow && annotated.IsLegacyBorrowCandidate)
        {
            reader.Diagnostic.Add(origin.Span, DiagnosticCode.LegacyBorrowOrigin_Kd);
            var cause = reader.Diagnostic.LastError;
            reader.Advance(); // The slash belongs to the malformed Type, not to its enclosing parameter list.
            var target = ParseType(ref reader);
            return new ErrorKoto(ref reader, SourceSpan.FromBounds(type.Span.Start, Math.Max(origin.Span.End, target.Span.End))) { Cause = cause };
        }

        return annotated;
    }

    private static (Koto? Expression, OriginArgument[]? Arguments, SourceSpan Span, bool InvalidContent) ParseOriginBraces(ref TokenReader reader)
    {
        var open = reader.Read();
        Koto? expression = null;
        // Named lists are short; grow the result array directly instead of a list plus a copy.
        OriginArgument[]? arguments = null;
        var argumentCount = 0;
        var invalidContent = false;
        var end = open.Span.End;
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseBrace or TokenKind.EndBlock))
        {
            if (reader.CurrentTokenKind == TokenKind.Underscore)
            {
                invalidContent = true;
                reader.Advance();
            }
            else if (reader.CurrentTokenKind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.EqualsGreaterThan)
            {
                var name = reader.GetIdentifier(reader.Read());
                reader.Advance();

                if (arguments is null)
                {
                    arguments = new OriginArgument[2];
                }
                else if (argumentCount == arguments.Length)
                {
                    Array.Resize(ref arguments, argumentCount * 2);
                }

                arguments[argumentCount++] = new(name, ParseOriginExpression(ref reader));
            }
            else
            {
                invalidContent |= expression is not null || arguments is not null;
                expression = ParseOriginExpression(ref reader);
            }

            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }
        }

        reader.ExpectCloser(TokenKind.CloseBrace, out var close);
        end = Math.Max(end, close.End);

        if (arguments is not null && argumentCount != arguments.Length)
        {
            Array.Resize(ref arguments, argumentCount);
        }

        return (expression, arguments, SourceSpan.FromBounds(open.Span.Start, end), invalidContent);
    }

    private static Koto ParseOriginExpression(ref TokenReader reader)
    {
        var left = ParseOriginAtom(ref reader);
        while (reader.CurrentTokenKind == TokenKind.And)
        {
            var op = reader.Read();
            var right = ParseOriginAtom(ref reader);
            left = new AndKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(op.Span.End, right.Span.End)), left, right);
        }

        return left;
    }

    private static Koto ParseOriginAtom(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
        {
            var open = reader.Read().Span;
            var inner = ParseOriginExpression(ref reader);
            reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);
            return new ParenthesizedKoto(ref reader, SourceSpan.FromBounds(open.Start, Math.Max(inner.Span.End, close.End)), inner);
        }

        if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
        {
            reader.Expect(SyntaxForm.Name);
            return reader.NewErrorKoto();
        }

        var token = reader.Read();
        if (!IdentifierNameKoto.TryCreate(ref reader, token, out var name))
        {
            return new ErrorKoto(ref reader, token.Span) { Cause = reader.Diagnostic.LastError };
        }

        Koto left = name;
        if (reader.TryConsume(TokenKind.Dot))
        {
            if (!reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
            {
                reader.Expect(SyntaxForm.Name);
                return left;
            }

            var member = reader.Read();
            if (!IdentifierNameKoto.TryCreate(ref reader, member, out var right))
            {
                return new ErrorKoto(ref reader, member.Span) { Cause = reader.Diagnostic.LastError };
            }

            left = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, right.Span.End), left, right);
        }

        return left;
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
            // A line boundary ends an unrecognized list: the tokenizer joins the lines of a recognized one (SPEC 2.2.1).
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

    /// <summary>Parses an attribute expression.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="beforeList">Whether the attribute is misplaced where the grammar expects <c>(</c>, such as before a parameter
    /// list. SPEC 6.5 sets no adjacency rule for the argument list, so there the attribute takes a list only when another
    /// <c>(</c> follows it: the last list is the one the grammar expects, as in <c>func f #Test () => ()</c>.</param>
    /// <returns>The parsed attribute, or <see langword="null"/> after an error.</returns>
    public static AttributeKoto? ParseAttributeKoto(ref TokenReader reader, bool beforeList = false)
    {
        var previousAttribute = reader.PopAttribute();

        reader.TryRead(out var attributeToken);

        var nameToken = reader.CurrentToken;
        var wellFormed = nameToken.Kind.IsIdentifierOrContextualKeyword() && UnicodeIdentifierHelper.IsUppercase(reader.GetSpan(nameToken));
        if (!wellFormed && nameToken.Kind.IsKeyword())
        {
            if (nameToken.Kind is TokenKind.If or TokenKind.Switch or TokenKind.Case)
            {
                // A directive selects whole items (SPEC 19.1), so here it selects nothing: it is one problem, its Condition is read
                // and set aside, and what follows it is parsed in place.
                reader.Unexpected(SyntaxForm.CompileTimeDirective, SourceSpan.FromBounds(attributeToken.Span.Start, nameToken.Span.End));
                reader.Advance();
                if (nameToken.Kind != TokenKind.Switch && !reader.TryConsume(TokenKind.Underscore) && !IsExpressionBoundary(ref reader))
                {
                    _ = ParseRequiredCompileTimeCondition(ref reader);
                }
            }
            else
            {
                // A reserved word is no attribute Name, and the construct it would start is not read: `#match` or `#for` is one
                // problem at the word, and what follows it on the line is parsed in place. An indented body after a word that ends
                // its line would be that construct's, so it is skipped with it.
                reader.Expect(SyntaxForm.AttributeName);
                reader.Advance();
                if (reader.CurrentTokenKind == TokenKind.Separator && reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    reader.SkipCurrentBlock();
                }
            }

            if (previousAttribute is not null)
            {
                reader.PushAttribute(previousAttribute);
            }

            return null;
        }

        if (!wellFormed)
        {
            reader.Expect(SyntaxForm.AttributeName);
        }

        var operand = ParsePrimaryExpression(ref reader);
        if (reader.CurrentTokenKind is TokenKind.Dot or TokenKind.LessThan or TokenKind.OpenBracket)
        {
            reader.Unexpected(SyntaxForm.AttributeSuffix);
            wellFormed = false;
        }

        while ((!beforeList || reader.CurrentTokenKind != TokenKind.OpenParenthesis || IsListFollowedByList(ref reader)) &&
            TryParsePostfixExpression(ref reader, ref operand))
        {
        }

        if (previousAttribute is not null)
        {
            reader.PushAttribute(previousAttribute);
        }

        if (!wellFormed)
        {
            // A malformed attribute attaches to nothing: its syntax Error explains it, and the declaration it precedes is checked on its own.
            return null;
        }

        var attributeKoto = new AttributeKoto(
            ref reader,
            SourceSpan.FromBounds(attributeToken.Span.Start, Math.Max(attributeToken.Span.End, operand.Span.End)),
            operand);
        reader.PushAttribute(attributeKoto);
        reader.CodeContext.Documentation?.Suppress(attributeKoto.Span);
        return attributeKoto;
    }

    // Whether the parenthesized list at the current token is directly followed by another one. Every list is closed: the
    // tokenizer supplies a missing closer.
    private static bool IsListFollowedByList(ref TokenReader reader)
    {
        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            switch (reader.PeekKind(offset))
            {
                case TokenKind.OpenParenthesis:
                    depth++;
                    break;
                case TokenKind.CloseParenthesis:
                    if (--depth == 0)
                    {
                        return reader.PeekKind(offset + 1) == TokenKind.OpenParenthesis;
                    }

                    break;
                case TokenKind.Invalid:
                    return false;
            }
        }
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
        var block = new CodeBlockKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, reader.PreviousSyntaxEnd)), items)
        {
            DeclarationContext = declarationContext.TokenKind,
        };
        return block;
    }

    /// <summary>
    /// Parses a type constraint in the form <c>subject is condition</c>.
    /// </summary>
    /// <remarks>
    /// Requirement operands are retained as <see cref="IdentifierNameKoto"/> and Type
    /// instances for later semantic analysis; there is no special constraint subject.
    /// </remarks>
    /// <param name="reader">The token reader positioned at the constraint subject.</param>
    /// <param name="finishLine">Whether to diagnose and consume trailing tokens on the clause's line.</param>
    /// <returns>The parsed constraint, or <see langword="null"/> when its required prefix is invalid.</returns>
    public static IsKoto? ParseTypeConstraint(ref TokenReader reader, bool finishLine = true)
    {
        var lastError = reader.Diagnostic.LastError;
        var subject = HasSimpleConstraintSubject(ref reader) ? ParseConstraintSubject(ref reader) : ParseDeclarationType(ref reader);

        if (!reader.TryConsume(TokenKind.Is, out var isRange, false))
        {
            // Every caller saw the keyword through IsTypeConstraintStart; a subject that consumed it is parsed as no Constraint.
            return null;
        }

        var previousRequirement = reader.ConstraintRequirement;
        reader.ConstraintRequirement = true;
        var condition = ParseCondition(ref reader);
        reader.ConstraintRequirement = previousRequirement;
        var constraint = new IsKoto(ref reader, SourceSpan.FromBounds(subject.Span.Start, Math.Max(isRange.End, condition.Span.End)), subject, condition);
        if (reader.Diagnostic.LastError is { } cause && (lastError is not { } previous || !cause.Equals(previous)))
        {
            // A part of the Constraint failed: the Constraint is a recovery, so Binding judges none of its parts (DIAGNOSTICS.md §4.3).
            // The keys are compared as values; comparing with the nullable key would box it for every Constraint after an Error.
            reader.CodeContext.RecordRecovery(constraint, cause);
        }

        if (finishLine)
        {
            reader.ExpectLineEnd();
            ParseCallableEffects(ref reader, constraint);
        }

        return constraint;

        static Koto ParseCondition(ref TokenReader reader)
        {
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                var notToken = reader.Read();
                return KotoHelper.NewUnaryKoto(ref reader, notToken, ParseOr(ref reader));
            }

            return ParseOr(ref reader);
        }

        static Koto ParseOr(ref TokenReader reader)
        {
            var left = ParseAnd(ref reader);
            while (reader.CurrentTokenKind == TokenKind.Or)
            {
                var token = reader.Read();
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, ParseAnd(ref reader));
            }

            return left;
        }

        static Koto ParseAnd(ref TokenReader reader)
        {
            var left = ParsePrimary(ref reader);
            while (reader.CurrentTokenKind == TokenKind.And)
            {
                var token = reader.Read();
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, ParsePrimary(ref reader));
            }

            return left;
        }

        static Koto ParsePrimary(ref TokenReader reader)
        {
            if (reader.CurrentTokenKind == TokenKind.Not)
            {
                var token = reader.Read();
                return KotoHelper.NewUnaryKoto(ref reader, token, ParsePrimary(ref reader));
            }

            if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
            {
                if (IsParenthesizedTypeRequirement(ref reader))
                {
                    return ParseDeclarationType(ref reader);
                }

                var openRange = reader.CurrentTokenRange;
                reader.Advance();
                var operand = ParseCondition(ref reader);
                reader.ExpectCloser(TokenKind.CloseParenthesis, out var closeRange);
                return new ParenthesizedKoto(ref reader, SourceSpan.FromBounds(openRange.Start, Math.Max(operand.Span.End, closeRange.End)), operand);
            }

            if (!reader.CanRead ||
                reader.CurrentTokenKind is TokenKind.Invalid or
                TokenKind.Separator or
                TokenKind.EndBlock or
                TokenKind.CloseParenthesis)
            {
                reader.Expect(SyntaxForm.Expression);
                return reader.NewErrorKoto();
            }

            if (reader.CurrentTokenKind.IsPrimitiveType() || reader.PeekKind(1) is TokenKind.Slash or TokenKind.OpenBrace || reader.CurrentTokenKind is TokenKind.OpenBracket or TokenKind.Self)
            {
                return ParseDeclarationType(ref reader);
            }

            var name = ParseConstraintSubject(ref reader);
            while (true)
            {
                if (reader.CurrentTokenKind == TokenKind.LessThan)
                {
                    name = ParseGenericsPostfix(ref reader, name);
                }
                else if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
                {
                    name = ParseOriginApplication(ref reader, name);
                }
                else if (reader.TryConsume(TokenKind.Dot))
                {
                    // SPEC 8.4.3: a requirement Type may be a Contract-qualified projection such as I.(LendingIterator).LentItem(step).
                    var member = reader.CurrentTokenKind == TokenKind.OpenParenthesis
                        ? ParseBareParenthesizedType(ref reader)
                        : ParseName(ref reader);
                    name = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(name.Span.Start, member.Span.End), name, member);
                }
                else
                {
                    name = ParseTypeOrigin(ref reader, name);
                    if (name is not TypeSemanticsKoto && (reader.CurrentTokenKind == TokenKind.Question || reader.IsCurrentIdentifier("during") || reader.IsCurrentIdentifier("from")))
                    {
                        name = new TypeSemanticsKoto(ref reader, name.Span, name);
                    }

                    return ParseOptionalSuffix(ref reader, name, parseBorrowOrigin: true);
                }
            }
        }
    }

    /// <summary>
    /// Determines whether the reader is positioned at the start of a type constraint.
    /// </summary>
    /// <param name="reader">The token reader to inspect.</param>
    /// <param name="declarationContext">Whether grouped Type subjects are allowed in this dedicated declaration region.</param>
    /// <returns><see langword="true"/> for Type subject syntax followed by <c>is</c>.</returns>
    public static bool IsTypeConstraintStart(ref TokenReader reader, bool declarationContext = false)
    {
        if (HasSimpleConstraintSubject(ref reader))
        {
            return true;
        }

        var first = reader.CurrentTokenKind;
        // Preserve executable prefix recognition, including call results and parenthesized
        // expressions. Complete Type subjects belong to dedicated declaration regions.
        if (!declarationContext)
        {
            return first.IsPrimitiveType() && reader.PeekKind(1) == TokenKind.Is;
        }

        // An associated specification owns its following "is"; its introducer is not
        // part of the Type subject. Preserve contextual uses such as associate<T>.
        if (first == TokenKind.Associate && reader.PeekKind(1).IsIdentifierOrContextualKeyword())
        {
            return false;
        }

        if (!(first.IsIdentifierOrContextualKeyword() || first.IsPrimitiveType() || first is TokenKind.Self or TokenKind.ColonColon or TokenKind.OpenParenthesis or TokenKind.OpenBracket))
        {
            return false;
        }

        var parentheses = 0;
        var brackets = 0;
        var arguments = 0;
        var braces = 0;
        for (var offset = 0; ; offset++)
        {
            var kind = reader.PeekKind(offset);
            if (kind is TokenKind.Invalid or TokenKind.StartBlock or TokenKind.EndBlock || (kind == TokenKind.Separator && parentheses == 0 && brackets == 0 && arguments == 0 && braces == 0))
            {
                return false;
            }

            if (kind == TokenKind.Is)
            {
                return parentheses == 0 && brackets == 0 && arguments == 0 && braces == 0;
            }

            if (kind == TokenKind.OpenBrace)
            {
                braces++;
            }
            else if (kind == TokenKind.CloseBrace)
            {
                if (--braces < 0)
                {
                    return false;
                }
            }
            else if (braces > 0)
            {
                continue;
            }
            else if (kind == TokenKind.OpenParenthesis)
            {
                parentheses++;
            }
            else if (kind == TokenKind.CloseParenthesis)
            {
                if (--parentheses < 0)
                {
                    return false;
                }
            }
            else if (kind == TokenKind.OpenBracket)
            {
                brackets++;
            }
            else if (kind == TokenKind.CloseBracket)
            {
                if (--brackets < 0)
                {
                    return false;
                }
            }
            else if (brackets == 0 && kind == TokenKind.LessThan)
            {
                arguments++;
            }
            else if (brackets == 0 && kind is TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan)
            {
                arguments -= kind == TokenKind.GreaterThan ? 1 : 2;
                if (arguments < 0)
                {
                    return false;
                }
            }
            else if (parentheses == 0 && brackets == 0 && arguments == 0 &&
                !(kind.IsIdentifierOrContextualKeyword() || kind.IsPrimitiveType() || kind is TokenKind.Self or TokenKind.Slash or TokenKind.Dot or TokenKind.ColonColon or TokenKind.MinusGreaterThan))
            {
                return false;
            }
        }
    }

    private static bool HasSimpleConstraintSubject(ref TokenReader reader)
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

    private static bool IsFunctionConstraintStart(ref TokenReader reader, FunctionKoto? function)
    {
        if (IsTypeConstraintStart(ref reader))
        {
            return true;
        }

        // Only a declared Type-parameter root selects the extended Type grammar here. Value calls remain expressions.
        if (function is null || !reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
        {
            return false;
        }

        // The token's text is compared without interning it: the statement that follows reads and validates the identifier itself.
        var name = reader.GetSpan(reader.CurrentToken);
        return (function.IsGenericParameter(name) || function.IsDeclaringTypeParameter(name)) && IsTypeConstraintStart(ref reader, declarationContext: true);
    }

    /// <summary>Parses an indentation-delimited expression block.</summary>
    /// <param name="reader">The token reader positioned at <see cref="TokenKind.StartBlock"/>.</param>
    /// <returns>The parsed block.</returns>
    public static CodeBlockKoto ParseBlock(ref TokenReader reader)
        => ParseFunctionBlock(ref reader, null);

    internal static CodeBlockKoto ParseFunctionBlock(ref TokenReader reader, FunctionKoto? function, Koto? originOwner = null)
    {
        var start = reader.CurrentTokenRange;
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.Expect(SyntaxForm.Body);
            return new CodeBlockKoto(ref reader, start, []);
        }

        var blockContext = reader.TakeContext();
        var items = default(TemporaryKotoList);
        var state = new ExecutableItemState(function, originOwner);
        var end = ParseExecutableBlockItems(ref reader, ref items, ref state);
        reader.RestoreContext(blockContext);
        return new CodeBlockKoto(
            ref reader,
            SourceSpan.FromBounds(start.Start, Math.Max(start.End, end)),
            items.ToArray());
    }

    /// <summary>Parses one indented block, from its StartBlock through its EndBlock, appending its selected items.</summary>
    /// <param name="reader">The token reader positioned at <see cref="TokenKind.StartBlock"/>.</param>
    /// <param name="items">The selected items of the enclosing executable body.</param>
    /// <param name="state">The Constraint-prefix state of the enclosing body, shared by its directive targets and arms.</param>
    /// <returns>The end offset of the block.</returns>
    /// <remarks>
    /// Directive targets and arms are parsed by this same loop (SPEC 19.5): a selected one adds its items directly; excluded
    /// syntax is parsed with the same grammar and recovery, extends the source-order Constraint prefix, and is dropped.
    /// </remarks>
    private static int ParseExecutableBlockItems(ref TokenReader reader, ref TemporaryKotoList items, ref ExecutableItemState state)
    {
        reader.Advance(); // StartBlock
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
                    reader.Expect(SyntaxForm.Body);
                }

                // The block ends at its last written token. The EndBlock stands at the next dedented line, after the
                // blank and comment lines and that line's indentation, none of which belongs to the block.
                var end = reader.PreviousSyntaxEnd;
                reader.Advance();
                return end;
            }

            var unavailableDeclaration = ConsumeAttributeAndModifier(ref reader, out var isEnd, allowCompileTimeDirectives: true);
            if (unavailableDeclaration)
            {
                hasSourceItem = true;
                SkipUnavailableDeclaration(ref reader);
                continue;
            }

            if (isEnd)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                continue; // A header left before the EndBlock was reported with it.
            }

            // Source items count before selection; a directive counts with its required syntax (SPEC 14.2.1).
            hasSourceItem = true;
            if (reader.IsExcluded)
            {
                var region = BeginExcludedRegion(ref reader);
                ParseExecutableDirectiveOrItem(ref reader, ref items, ref state);
                EndExcludedRegion(ref reader, region);
            }
            else if (reader.HasCompileTimeIfPrefix || IsCompileTimeSwitchStart(ref reader))
            {
                ParseExecutableDirectiveOrItem(ref reader, ref items, ref state);
            }
            else
            {
                ParseExecutableItem(ref reader, ref items, ref state);
            }
        }

        // The tokenizer closes every block at the end of the source; the loop ends here only after the block's EndBlock.
        return reader.PreviousSyntaxEnd;
    }

    private static void ParseExecutableDirectiveOrItem(ref TokenReader reader, ref TemporaryKotoList items, ref ExecutableItemState state)
    {
        if (IsCompileTimeSwitchStart(ref reader))
        {
            var arms = CompileTimeSwitchArms.Begin(ref reader);
            while (arms.TryNextBody(ref reader, out var selected, out var header))
            {
                if (selected)
                {
                    ParseExecutableBlockItems(ref reader, ref items, ref state);
                }
                else
                {
                    var region = BeginExcludedRegion(ref reader, header, header.Start);
                    ParseExecutableBlockItems(ref reader, ref items, ref state);
                    EndExcludedRegion(ref reader, region);
                }
            }

            if (arms.Unselected(ref reader) is { } unselected)
            {
                items.Add(unselected);
            }

            return;
        }

        if (reader.HasCompileTimeIfPrefix && reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            RejectDirectiveBlockAttributes(ref reader);
            ParseExecutableBlockItems(ref reader, ref items, ref state);
            return;
        }

        ParseExecutableItem(ref reader, ref items, ref state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseExecutableItem(ref TokenReader reader, ref TemporaryKotoList items, ref ExecutableItemState state)
    {
        // Excluded syntax is parsed like selected syntax but registers nothing with the enclosing declarations (SPEC 19.5).
        var excluded = reader.InExcludedSyntax;
        var function = state.Function;
        if (IsOriginRelationStart(ref reader))
        {
            ReportPendingAttributes(ref reader);
            var relation = ParseOriginRelation(ref reader);
            if ((function ?? state.OriginOwner) is { } owner && !state.SeenExecutableItem && function is not { IsAnonymous: true } and not { IsDestructor: true } and not { IsSpecialization: true })
            {
                if (!excluded)
                {
                    OriginClauses.Add(owner, relation);
                }
            }
            else
            {
                relation.Unexpected(SyntaxForm.OriginClause);
            }

            return;
        }

        // SPEC 8.4.10.1: an effect item belongs to a requirement's Constraint region. At a body's Constraint prefix it is
        // kept for Binding to reject, never read as an expression.
        if (function is not null && !state.SeenExecutableItem && IsEffectStart(ref reader, specification: false))
        {
            ReportPendingAttributes(ref reader);
            var effect = ParseEffectBound(ref reader);
            if (!excluded)
            {
                function.AddEffectBound(effect);
            }

            return;
        }

        // SPEC 7.4: every function, constructor, destructor and accessor body begins with its Constraint prefix, whatever
        // the declaration's generic parameters; a leading `value is Dog` is never an expression statement.
        if ((function is not null || state.OriginOwner is PropertyAccessorKoto) && IsFunctionConstraintStart(ref reader, function))
        {
            // A root-qualified subject is never a generic parameter; do not read "::" as an identifier. A destructor or
            // accessor is never a conditional member, so no subject is permitted in its body.
            var rootQualified = reader.CurrentTokenKind == TokenKind.ColonColon;
            var subject = rootQualified ? default : reader.GetSpan(reader.CurrentToken);
            var isGenericParameter = !rootQualified && function is { IsDestructor: false } && (function.IsGenericParameter(subject) || function.IsDeclaringTypeParameter(subject));
            if (!state.SeenExecutableItem || isGenericParameter)
            {
                ReportPendingAttributes(ref reader);
                var misplaced = state.SeenExecutableItem || !isGenericParameter ? reader.Unexpected(SyntaxForm.ConstraintPrefix) : default(DiagnosticKey?);
                var constraint = ParseTypeConstraint(ref reader);
                if (constraint is not null && function is { IsDestructor: false } && !excluded)
                {
                    // The prefix stays a Constraint of the function (SPEC 7.4); a misplaced one is a recovery, so Binding's own judgement of it is derived.
                    function.AddTypeConstraint(constraint);
                    if (misplaced is { } cause)
                    {
                        reader.CodeContext.RecordRecovery(constraint, cause);
                    }
                }

                return;
            }
        }

        state.SeenExecutableItem = true;
        var oldPosition = reader.Position;
        var item = ParseBlockItem(ref reader);
        if (item is not null && !excluded)
        {
            items.Add(item);
        }

        if (reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock))
        {
            if (item is IdentifierNameKoto { IdentifierName: "during" })
            {
                // The line is a detached borrow annotation, not a statement; Binding does not resolve 'during' as a Name.
                reader.CodeContext.RecordRecovery(item, reader.Unexpected(SyntaxForm.DetachedDuring, item.Span));
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
            }
            else
            {
                reader.ExpectLineEnd();
            }
        }

        if (reader.Position == oldPosition)
        {
            reader.Advance();
        }
    }

    /// <summary>Skips the rest of a declaration's line and the indented body it would have introduced, then clears the pending header.</summary>
    /// <param name="reader">The token reader.</param>
    internal static void SkipDeclarationLine(ref TokenReader reader)
    {
        // The reader stays at the line boundary, so the enclosing item ends there and the next line keeps its own statement.
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
        }

        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.SkipCurrentBlock();
        }

        reader.ClearContext();
    }

    /// <summary>
    /// Skips a declaration that no header introduces after its Error, with the rest of its line and its body, and records the
    /// omission, so a check over the member set rests on the Error (DIAGNOSTICS.md §4.3, §4.4).
    /// </summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="cause">The key of the Error.</param>
    internal static void OmitDeclaration(ref TokenReader reader, DiagnosticKey? cause)
    {
        if (cause is { } key && !reader.InExcludedSyntax)
        {
            // Excluded syntax records no omission for the selected program (SPEC 19.5).
            reader.CodeContext.Kotonoha.RecordOmission(key);
        }

        SkipDeclarationLine(ref reader);
    }

    /// <summary>
    /// Gets whether a Container keyword at the current token declares a Container. The contextual keyword is a Name (SPEC 2.5.1)
    /// where an expression continues after it, through an operator, a member access, a call or an index, and where its line ends
    /// without an indented body; anything else, its Name or a token no expression takes there, makes it a declaration.
    /// </summary>
    /// <param name="reader">The token reader.</param>
    /// <returns><see langword="true"/> for a Container declaration.</returns>
    internal static bool IsContainerDeclarationStart(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind is not (TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract))
        {
            return false;
        }

        var next = reader.PeekKind(1);
        return next switch
        {
            TokenKind.Separator => reader.PeekKind(2) == TokenKind.StartBlock,
            TokenKind.EndBlock => false,
            _ => InfixLeftBindingPower[(byte)next] == 0 && !IsPostfixOperator[(byte)next],
        };
    }

    private static bool IntroducesDeclaration(ref TokenReader reader)
        => IsContainerDeclarationStart(ref reader) ||
            reader.CurrentTokenKind is TokenKind.Let or TokenKind.Var or TokenKind.Init or TokenKind.Drop or TokenKind.Computed or TokenKind.Property or TokenKind.Associate or TokenKind.Alias ||
            (reader.CurrentTokenKind == TokenKind.Func && reader.PeekKind(1) is not (TokenKind.OpenParenthesis or TokenKind.OpenBracket)) ||
            (reader.IsCurrentIdentifier("specialize") && reader.PeekKind(1) == TokenKind.Func);

    internal static Koto? ParseBlockItem(ref TokenReader reader)
    {
        if (reader.ModifierKind != ModifierKind.NoModifier && !IntroducesDeclaration(ref reader))
        {
            // Modifiers introduce a declaration; the line and the body it would have introduced are skipped as one.
            var cause = reader.Expect(SyntaxForm.Declaration);
            if (!reader.InExcludedSyntax)
            {
                // Excluded syntax records no omission for the selected program (SPEC 19.5).
                reader.CodeContext.Kotonoha.RecordOmission(cause);
            }

            SkipDeclarationLine(ref reader);
            return null;
        }

        if (reader.AttributeKoto is { } attribute && (reader.CurrentTokenKind != TokenKind.Func || reader.PeekKind(1) is TokenKind.OpenParenthesis or TokenKind.OpenBracket))
        {
            // Only a named function among executable items takes attributes (SPEC 6.5, F.5); a misplaced one attaches to nothing,
            // so the item it precedes is checked on its own.
            reader.Unexpected(SyntaxForm.Attribute, attribute.Span);
            reader.PopAttribute();
        }

        if (reader.CurrentTokenKind == TokenKind.Underscore && reader.PeekKind(1) == TokenKind.Equals)
        {
            var discardToken = reader.Read();
            reader.Advance();
            var value = ParseRequiredExpression(ref reader);
            return new DiscardKoto(ref reader, SourceSpan.FromBounds(discardToken.Span.Start, Math.Max(discardToken.Span.End, value.Span.End)), value);
        }

        if (reader.CurrentTokenKind == TokenKind.Dollar && reader.PeekKind(1) is TokenKind.Identifier or TokenKind.Require)
        {
            var probe = reader;
            var start = probe.Read();
            var require = probe.CurrentTokenKind == TokenKind.Require;
            if (require || probe.IsCurrentIdentifier("expect"))
            {
                reader = probe;
                reader.Advance();
                if (!reader.TryConsume(TokenKind.OpenParenthesis))
                {
                    reader.Expect(SyntaxForm.OpenParenthesis);
                    return reader.NewErrorKoto();
                }

                var arguments = ParseArgumentList(ref reader, out var labels);
                var trailing = reader.PeekKind(-1) == TokenKind.Comma;
                var closed = reader.TryConsume(TokenKind.CloseParenthesis);
                DiagnosticKey? malformed = null;
                if (!closed || arguments.Length is < 1 or > 2 || trailing ||
                    (labels is not null && labels.Length > 0 && labels[0] is not null) ||
                    (arguments.Length == 2 && (labels is null || labels.Length < 2 || labels[1] != "message")))
                {
                    malformed = reader.Unexpected(SyntaxForm.TestVerificationArguments, start.Span);
                }

                // A malformed list keeps its condition; a second argument stands as a recovery, so no message check repeats the Error.
                var message = arguments.Length > 1 ? (malformed is { } cause ? new ErrorKoto(ref reader, arguments[1].Span) { Cause = cause } : arguments[1]) : null;
                return new TestVerificationKoto(ref reader, SourceSpan.FromBounds(start.Span.Start, reader.PreviousSyntaxEnd), require, arguments.Length > 0 ? arguments[0] : reader.NewErrorKoto(), message);
            }
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
                return ParseField(ref reader, token);

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

            case TokenKind.Group or TokenKind.Struct or TokenKind.Enum or TokenKind.Extension or TokenKind.Contract when IsContainerDeclarationStart(ref reader):
                // No Container nests in an executable block (SPEC 6.1.1); the declaration is skipped with its body.
                OmitDeclaration(ref reader, reader.Unexpected(token.Kind == TokenKind.Extension ? SyntaxForm.ExtensionDeclaration : SyntaxForm.ContainerDeclaration, token.Span));
                return null;

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
            function.MissingBody = true; // Avoid cascading diagnostics after an incomplete signature.
            return;
        }

        if (!HasLibraryImport(function.AttributeChain))
        {
            reader.Expect(SyntaxForm.Body);
            function.MissingBody = true;
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
                (reader.PeekKind(1) is TokenKind.EqualsGreaterThan or TokenKind.StartBlock ||
                    (!expressionPosition && reader.PeekKind(1) == TokenKind.Colon) ||
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
        // A nested if is parsed in full and recorded as a recovery, so its Type rests on the Error.
        var cause = reader.IfBodyRegion ? reader.Unexpected(SyntaxForm.NestedIfExpression) : default(DiagnosticKey?);
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

        var result = new IfKoto(
            ref reader,
            SourceSpan.FromBounds(ifToken.Span.Start, end),
            branches,
            elseBody);
        if (cause is { } nested)
        {
            reader.CodeContext.RecordRecovery(result, nested);
        }

        return result;
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
        ulong mutableSlots = 0;
        DiagnosticKey? malformed = null;

        if (isTupleBinding)
        {
            malformed = ParseForTupleBindings(ref reader, bindings, ref mutableSlots);
        }
        else if (TryParseForBinding(ref reader, out var binding, out var mutable))
        {
            bindings.Add(binding);
            mutableSlots = mutable ? 1UL : 0UL;
        }
        else
        {
            malformed = reader.Expect(SyntaxForm.Name);
            if (reader.CanRead &&
                reader.CurrentTokenKind != TokenKind.In &&
                !IsExpressionBoundary(ref reader))
            {
                reader.Advance();
            }
        }

        reader.Expect(TokenKind.In);

        var iterable = ParseHeaderExpression(ref reader);
        var body = ParseRequiredBody(ref reader);
        var loop = new ForKoto(
            ref reader,
            SourceSpan.FromBounds(forToken.Span.Start, body.Span.End),
            bindings,
            iterable,
            body,
            isTupleBinding,
            mutableSlots);
        if (malformed is { } cause)
        {
            // The slots the loop binds are unknown, so its iteration is not checked: the iterable and the body are, on their own.
            reader.CodeContext.RecordRecovery(loop, cause);
        }

        return loop;
    }

    /// <summary>Parses the slots of <c>(slot, ...)</c> (SPEC 14.6.1, F.5); a failed slot is skipped whole and the list continues.</summary>
    /// <returns>The key of the first Error, or <see langword="null"/> for a well-formed list.</returns>
    private static DiagnosticKey? ParseForTupleBindings(ref TokenReader reader, List<IdentifierNameKoto> bindings, ref ulong mutableSlots)
    {
        reader.Advance();
        DiagnosticKey? malformed = null;
        while (true)
        {
            var start = reader.Position;
            if (reader.CurrentTokenKind is TokenKind.CloseParenthesis or TokenKind.In || IsExpressionBoundary(ref reader))
            {
                // An empty list, a trailing comma or a list cut short: a ForBinding needs a slot here (SPEC F.5).
                malformed ??= reader.Expect(SyntaxForm.Name);
            }
            else if (TryParseForBinding(ref reader, out var binding, out var mutable))
            {
                if (mutable)
                {
                    if (bindings.Count < 64)
                    {
                        mutableSlots |= 1UL << bindings.Count;
                    }
                    else
                    {
                        reader.Diagnostic.Add(binding.Span, DiagnosticCode.ForBindingLimit_Kd);
                    }
                }

                bindings.Add(binding);
            }
            else
            {
                // Not a slot, such as a nested list: the whole slot is skipped, so a following comma still continues the list.
                malformed ??= reader.Expect(SyntaxForm.Name);
                reader.SkipListItem();
            }

            if (reader.TryConsume(TokenKind.Comma))
            {
                continue;
            }

            if (reader.TryConsume(TokenKind.CloseParenthesis))
            {
                return malformed;
            }

            if (reader.CurrentTokenKind == TokenKind.In || IsExpressionBoundary(ref reader))
            {
                var missing = reader.Expect(SyntaxForm.CloseParenthesis);
                return malformed ?? missing;
            }

            // Two slots without a comma between them; the next one is read, unless nothing of this one could be.
            malformed ??= reader.Expect(SyntaxForm.Comma);
            if (reader.Position == start)
            {
                return malformed;
            }
        }
    }

    // SPEC 14.6.1: ForSlot := Name | "var" Name | "_"; a bare Name is an immutable let binding.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseForBinding(ref TokenReader reader, [NotNullWhen(true)] out IdentifierNameKoto? binding, out bool mutable)
    {
        mutable = false;
        if (reader.CurrentTokenKind == TokenKind.Var)
        {
            var varToken = reader.Read();
            mutable = true;
            if (reader.CurrentTokenKind == TokenKind.Underscore)
            {
                reader.Unexpected(SyntaxForm.WildcardBinding, varToken.Span);
            }
        }

        if (reader.CurrentTokenKind == TokenKind.Underscore)
        {
            binding = new IdentifierNameKoto(ref reader, reader.Read(), "_");
            return true;
        }

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

        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            // A token left on the header's line, before the indented arms that follow, is where the line should end; the arms are
            // still the match's. Without them, the armless match stands for the missing arms, so its exhaustiveness rests on the
            // Error (DIAGNOSTICS.md §4.3).
            if (!reader.IndentedBodyFollows())
            {
                var cause = reader.Expect(SyntaxForm.MatchArms);
                reader.SkipHeaderLine();
                var armless = new MatchKoto(
                    ref reader,
                    SourceSpan.FromBounds(matchToken.Span.Start, end),
                    expression,
                    arms);
                reader.CodeContext.RecordRecovery(armless, cause);
                return armless;
            }

            reader.Expect(SyntaxForm.LineEnd);
            reader.SkipHeaderLine();
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
                // Like a block, the arm list ends at its last written token, not at the dedented line.
                end = reader.PreviousSyntaxEnd;
                reader.Advance();
                return new MatchKoto(
                    ref reader,
                    SourceSpan.FromBounds(matchToken.Span.Start, end),
                    expression,
                    arms);
            }

            if (reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) is TokenKind.If or TokenKind.Switch or TokenKind.Case)
            {
                // A directive selects whole items, and an arm is none (SPEC 19.1, Appendix F.8): it is one problem, and its line is
                // skipped with the indented arms of a block form or a #switch; the arms after it are read as usual.
                reader.Unexpected(SyntaxForm.CompileTimeDirective, SourceSpan.FromBounds(reader.CurrentTokenRange.Start, reader.PeekToken(1).Span.End));
                if (reader.SkipHeaderLine())
                {
                    reader.SkipCurrentBlock();
                }

                continue;
            }

            var oldPosition = reader.Position;
            var pattern = ParsePattern(ref reader);
            var region = reader.EnterRegion();
            var guard = reader.TryConsume(TokenKind.If) ? ParseHeaderExpression(ref reader) : null;
            var parsedBody = ParseRequiredBody(ref reader);
            Koto body = parsedBody.IsExpressionBody ? parsedBody.Items[0] : parsedBody;
            arms.Add(new MatchArmKoto(pattern, body) { Guard = guard });
            end = body.Span.End;
            reader.RestoreRegion(region);
            if (parsedBody.IsExpressionBody && reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock))
            {
                // An arm ends with its line, as a statement does; what remains there is no next arm.
                reader.ExpectLineEnd();
            }

            if (reader.Position == oldPosition)
            {
                reader.Advance();
            }
        }

        // The arm list ends only through its EndBlock, which the tokenizer supplies at the end of the source.
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
        DiagnosticKey? recovery = null;
        var sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
        var validTarget = true;
        if (sameLine && token.Kind != TokenKind.Return && reader.IsCurrentIdentifier("to"))
        {
            end = reader.Read().Span.End;
            sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
            if (!sameLine || !reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
            {
                recovery = reader.ReportOnce(sameLine ? reader.CurrentTokenRange : new SourceSpan(end, 0), DiagnosticCode.TransferTargetExpected_Kd);
                validTarget = false;
                label = string.Empty; // A malformed named target must not become an unnamed transfer.
            }
            else
            {
                var name = reader.Read();
                end = name.Span.End;
                reader.TryGetIdentifier(name, out label);
                sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
            }
        }

        // Named and unnamed transfers share omission and full-expression parsing. Colon is not a boundary here.
        if (validTarget && sameLine && token.Kind != TokenKind.Continue && !IsExpressionBoundary(ref reader))
        {
            var kind = reader.CurrentTokenKind;
            if (kind == TokenKind.Colon ||
                (InfixLeftBindingPower[(byte)kind] != 0 && !IsPrefixOperator[(byte)kind] && kind is not (TokenKind.DotDot or TokenKind.DotDotEquals)))
            {
                var invalid = reader.Read();
                reader.Diagnostic.Add(invalid.Span, DiagnosticCode.TransferOperandExpected_Kd, invalid.Kind.ToText());
                recovery = reader.Diagnostic.LastError;
                expression = new ErrorKoto(ref reader, invalid.Span) { Cause = recovery };
                end = invalid.Span.End;
                sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
            }
            else if (label is not null && end == reader.CurrentTokenRange.Start)
            {
                reader.AddDiagnostic(DiagnosticCode.TransferOperandSeparation_Kd);
                recovery = reader.Diagnostic.LastError;
            }

            // Retain a same-line operand after a bad separator for recovery, without absorbing the next item.
            if (sameLine && !IsExpressionBoundary(ref reader))
            {
                expression = ParseRequiredExpression(ref reader);
                end = expression.Span.End;
            }
        }

        var range = SourceSpan.FromBounds(token.Span.Start, end);
        JumpKoto result = token.Kind switch
        {
            TokenKind.Return => new ReturnKoto(ref reader, range, expression),
            TokenKind.Exit => new ExitKoto(ref reader, range, expression, label),
            TokenKind.Yield => new YieldKoto(ref reader, range, expression, label),
            TokenKind.Continue => new ContinueKoto(ref reader, range, label),
            _ => throw new InvalidOperationException(),
        };
        if (recovery is { } cause)
        {
            reader.CodeContext.RecordRecovery(result, cause);
        }

        return result;
    }

    internal static Koto ParseRequiredExpression(ref TokenReader reader)
    {
        if (IsExpressionBoundary(ref reader))
        {
            reader.Expect(SyntaxForm.Expression);
            return reader.NewErrorKoto();
        }

        return ParseExpression(ref reader);
    }

    // A Condition is a header expression: the indented body after it is the directive's target, never part of it (SPEC 19.1).
    private static Koto ParseRequiredCompileTimeCondition(ref TokenReader reader)
    {
        var previous = reader.IsParsingCompileTimeCondition;
        reader.IsParsingCompileTimeCondition = true;
        try
        {
            return ParseHeaderExpression(ref reader);
        }
        finally
        {
            reader.IsParsingCompileTimeCondition = previous;
        }
    }

    private static CodeBlockKoto ParseRequiredBlock(ref TokenReader reader, bool statementHeader = false)
    {
        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return ParseBlock(ref reader);
        }

        // A token left on a statement header's line, before the indented body that follows, is where the header's line should
        // end, as after a function header; the body is still the header's.
        if (statementHeader && reader.IndentedBodyFollows())
        {
            reader.Expect(SyntaxForm.LineEnd);
            reader.SkipHeaderLine();
            return ParseBlock(ref reader);
        }

        // An empty body is supplied where the body is missing. It is a recovery: a check that reads its Type or its normal
        // completion reads the parser's guess and rests on the Error (DIAGNOSTICS.md §4.3). The rest of a statement header's line
        // goes with it, so an aligned else on the next line still continues the header.
        var cause = reader.Expect(SyntaxForm.Body);
        var supplied = new CodeBlockKoto(ref reader, new SourceSpan(reader.PreviousSyntaxEnd, 0), []);
        reader.CodeContext.RecordRecovery(supplied, cause);
        if (statementHeader)
        {
            reader.SkipHeaderLine();
        }

        return supplied;
    }

    internal static CodeBlockKoto ParseRequiredBody(ref TokenReader reader, bool ifBody = false)
    {
        if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
        {
            if (reader.SingleBodyRegion)
            {
                reader.Expect(SyntaxForm.ArrowBody);
            }

            // The items of an indented body are regions of their own (SPEC 2.2.1).
            var region = reader.EnterRegion();
            var block = ParseRequiredBlock(ref reader, true);
            reader.RestoreRegion(region);
            return block;
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
            // The expression body belongs on the header's line, right after its arrow. The Error expression is supplied for the
            // missing body, a recovery like the empty block of a missing indented body.
            var cause = reader.Expect(SyntaxForm.Expression);
            var supplied = reader.NewErrorKoto();
            reader.CodeContext.RecordRecovery(supplied, cause);
            return supplied;
        }

        var region = reader.EnterSingleBody(ifBody);
        try
        {
            if (reader.CurrentTokenKind == TokenKind.Dollar ||
                (reader.CurrentTokenKind == TokenKind.Underscore && reader.PeekKind(1) == TokenKind.Equals))
            {
                return ParseBlockItem(ref reader) ?? reader.NewErrorKoto();
            }

            if (reader.CurrentTokenKind == TokenKind.Require)
            {
                return ParseRequire(ref reader);
            }

            return IsBlockStatementStart(ref reader) ? ParseBlockStatement(ref reader) : ParseRequiredExpression(ref reader);
        }
        finally
        {
            reader.RestoreRegion(region);
        }
    }

    private static Koto ParseHeaderExpression(ref TokenReader reader)
    {
        var region = reader.EnterHeader();
        try
        {
            return ParseRequiredExpression(ref reader);
        }
        finally
        {
            reader.RestoreRegion(region);
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
    /// <returns>The parsed expression.</returns>
    public static Koto ParseExpression(ref TokenReader reader, int minBindingPower = 0)
    {
        if (IsBlockStatementStart(ref reader, expressionPosition: true))
        {
            reader.AddDiagnostic(DiagnosticCode.BlockStatementInExpression_Kd);
            return ParseBlockStatement(ref reader);
        }

        var left = IsLabelPrefix(ref reader)
            ? ParseLabeledExpression(ref reader)
            : ParsePrefixExpression(ref reader, minBindingPower);
        while (true)
        {
            var tokenKind = reader.CurrentTokenKind;
            if (IsPostfixOperator[(byte)tokenKind] && TryParsePostfixExpression(ref reader, ref left))
            {
                continue;
            }

            if (tokenKind == TokenKind.Sharp)
            {
                // An expression takes no attribute (SPEC 6.5); the misplaced one is discarded and the expression continues after it.
                reader.ReportMisplacedAttributes();
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
                    reader.Expect(SyntaxForm.Type);
                    typeKoto = reader.NewErrorKoto();
                }
                else if (IsBareOperationTarget(ref reader))
                {
                    // A bare built-in Semantics shorthand, @move or @copy completes the target (SPEC §13.5.1);
                    // a following '.', '(' or '[' continues the postfix chain: x@uniq.m(), f@move(), p@copy.x.
                    typeKoto = new TypeSemanticsKoto(ref reader, reader.Read());
                }
                else
                {
                    // Direct borrow Origins are inferred. Complete payload Types retain their own annotations.
                    typeKoto = ParseType(ref reader, parseOrigin: true, disambiguateGenerics: true);
                    if (CheckAdaptationOrigins(typeKoto) is { } annotated)
                    {
                        typeKoto = new ErrorKoto(ref reader, typeKoto.Span) { Cause = annotated }; // The target rests on the Error.
                    }

                    typeKoto = ConversionOperationTarget(ref reader, typeKoto);
                }

                if (reader.IsCurrentIdentifier("during"))
                {
                    reader.Unexpected(SyntaxForm.AdaptationDuring);
                    reader.Advance();
                    _ = ParseOriginAtom(ref reader);
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
                    reader.Unexpected(SyntaxForm.ChainedRange, rangeToken.Span);
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
            DiagnosticKey? chained = null;
            if (leftBindingPower == ComparisonBindingPower && IsNonAssociativeComparison(ref reader, tokenKind, left))
            {
                // Keep parsing for recovery, but require explicit parentheses for all combinations
                // of ordering, equality, and runtime is comparisons (SPEC 13.1). The combined node is
                // the parser's guess: it stands for this error, and checks of its form rest on it.
                if (reader.Diagnostic.Add(token.Span, DiagnosticCode.ChainedComparison_Kd))
                {
                    chained = reader.Diagnostic.LastError;
                }
            }

            Koto right;
            if (IsExpressionBoundary(ref reader))
            {
                reader.Expect(SyntaxForm.Expression);
                right = reader.NewErrorKoto();
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
            }
            else if (token.Kind == TokenKind.Is && !reader.IsParsingCompileTimeCondition)
            {
                var negated = reader.TryConsume(TokenKind.Not);
                right = ParseConstraintSubject(ref reader);
                if (OpensAdjacentTypeArguments(ref reader, right.Span))
                {
                    right = ParseGenericsPostfix(ref reader, right);
                }

                var test = (IsKoto)KotoHelper.NewBinaryKoto(ref reader, token, left, right);
                test.IsRuntimeTest = true;
                test.IsNegated = negated;
                left = test;
            }
            else
            {
                right = ParseExpression(ref reader, rightBindingPower);
                left = KotoHelper.NewBinaryKoto(ref reader, token, left, right);
            }

            if (chained is { } cause)
            {
                reader.CodeContext.RecordRecovery(left, cause);
            }
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

    private static bool IsLabelPrefix(ref TokenReader reader)
        => reader.IsCurrentIdentifier("label") && reader.PeekKind(1).IsIdentifierOrContextualKeyword() &&
            reader.PeekKind(2) == TokenKind.Colon && reader.SameLine(reader.CurrentTokenRange.End, reader.PeekToken(2).Span.Start);

    private static Koto ParseLabeledExpression(ref TokenReader reader)
    {
        if (reader.HeaderRegion)
        {
            reader.Unexpected(SyntaxForm.HeaderBodyExpression);
        }

        var introducer = reader.Read();
        var name = reader.Read();
        reader.TryGetIdentifier(name, out var label);
        var colon = reader.Read();
        var sameLine = reader.CanRead && reader.SameLine(colon.Span.End, reader.CurrentTokenRange.Start);
        DiagnosticKey? recovery = null;
        Koto target;
        if (!sameLine || reader.CurrentTokenKind is not (TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.If or TokenKind.Match or TokenKind.Do))
        {
            recovery = reader.ReportOnce(sameLine ? reader.CurrentTokenRange : new SourceSpan(colon.Span.End, 0), DiagnosticCode.LabelTargetExpected_Kd);
            target = new ErrorKoto(ref reader, colon.Span) { Cause = recovery };
            if (sameLine && !IsExpressionBoundary(ref reader))
            {
                target = ParseBlockItem(ref reader) ?? target;
            }
        }
        else
        {
            target = reader.CurrentTokenKind switch
            {
                TokenKind.For => ParseForExpression(ref reader),
                TokenKind.While => ParseWhileExpression(ref reader),
                TokenKind.Loop => ParseLoopExpression(ref reader),
                TokenKind.If => ParseIfExpression(ref reader),
                TokenKind.Match => ParseMatchExpression(ref reader),
                _ => ParseDoExpression(ref reader),
            };
        }

        var result = new LabeledKoto(ref reader, SourceSpan.FromBounds(introducer.Span.Start, target.Span.End), label ?? string.Empty, target);
        if (recovery is { } cause)
        {
            reader.CodeContext.RecordRecovery(result, cause);
        }

        return result;
    }

    /// <summary>
    /// Determines whether the token after <c>@</c> is a bare built-in Semantics name, <c>move</c> or <c>copy</c> that
    /// completes the operation target by itself, so that no qualified Type parse is attempted (SPEC §13.5.1).
    /// </summary>
    private static bool IsBareOperationTarget(ref TokenReader reader)
    {
        var token = reader.CurrentToken;
        if (!token.Kind.IsIdentifierOrContextualKeyword())
        {
            return false;
        }

        var text = reader.GetSpan(token);
        if (!text.SequenceEqual(Constants.MoveOperation) && !text.SequenceEqual(Constants.CopyOperation) && !CompilerHelper.TryParse(text, out _))
        {
            return false; // follow is consumed as a postfix operation before this point (SPEC 13.1).
        }

        // A slash starts a full Semantics form, an Origin brace or an optional suffix keeps the diagnostics of
        // the Type parser, and adjacent generic arguments are not a shorthand.
        return reader.PeekKind(1) switch
        {
            TokenKind.Slash or TokenKind.OpenBrace or TokenKind.Question => false,
            TokenKind.LessThan => reader.PeekToken(1).Span.Start != token.Span.End,
            _ => true,
        };
    }

    private static Koto ParsePrefixExpression(ref TokenReader reader, int minBindingPower = 0)
    {
ProcessPrefix:
        var tokenKind = reader.CurrentTokenKind;
        if (reader.HeaderRegion && tokenKind is TokenKind.If or TokenKind.Match or TokenKind.For or TokenKind.While or TokenKind.Loop or TokenKind.Do or TokenKind.Func)
        {
            var cause = reader.Unexpected(SyntaxForm.HeaderBodyExpression);
            if (reader.IndentedBodyFollows())
            {
                // The indented body after the header is the statement's, so the expression is not read: the rest of the header's
                // line rests on this Error, and the statement keeps its body (DIAGNOSTICS.md §4.4).
                var start = reader.CurrentTokenRange.Start;
                _ = reader.SkipHeaderLine();
                return new ErrorKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, reader.PreviousSyntaxEnd))) { Cause = cause };
            }
        }

        if (tokenKind == TokenKind.Sharp)
        {
            reader.ReportMisplacedAttributes();
            goto ProcessPrefix;
        }

        if ((tokenKind == TokenKind.Try || IsPrefixOperator[(byte)tokenKind]) && reader.CanRead)
        {
            var token = reader.Read();
            if (tokenKind == TokenKind.Try && minBindingPower > TryBindingPower)
            {
                // A level-2 prefix operator cannot take a try expression directly: write -(try x) (SPEC §13.1).
                reader.Unexpected(SyntaxForm.TryOperand, token.Span);
            }

            Koto operand;
            if (IsExpressionBoundary(ref reader))
            {
                reader.Expect(SyntaxForm.Expression);
                operand = reader.NewErrorKoto();
            }
            else
            {
                operand = ParseExpression(ref reader, tokenKind == TokenKind.Try ? TryBindingPower : PrefixBindingPower);
            }

            // Formatting roots require literal syntax so their embedded evaluation can be deferred.
            if (tokenKind == TokenKind.Dollar &&
                (!((operand is InvocationKoto { Method: IdentifierNameKoto { IdentifierName: "abort" }, ArgumentNodes.Count: 1 } abort && abort.GetArgumentLabel(0) is null) ||
                   (operand is InvocationKoto { Method: IdentifierNameKoto { IdentifierName: "tryWrite" }, ArgumentNodes.Count: 2 } write &&
                   write.GetArgumentLabel(0) is null && write.GetArgumentLabel(1) is null && write.ArgumentNodes[1] is StringLiteralKoto or InterpolatedStringKoto)) ||
                 (reader.PeekKind(-1) == TokenKind.CloseParenthesis && reader.PeekKind(-2) == TokenKind.Comma)))
            {
                // The prefix marks nothing Binding can select; the node rests on the Error and its operand is checked on its own.
                var dollar = KotoHelper.NewUnaryKoto(ref reader, token, operand);
                if (operand.Expected(SyntaxForm.DollarOperand) is { } cause)
                {
                    reader.CodeContext.RecordRecovery(dollar, cause);
                }

                return dollar;
            }

            return tokenKind == TokenKind.Try ? new TryKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, operand.Span.End), operand) : KotoHelper.NewUnaryKoto(ref reader, token, operand);
        }

        return ParsePrimaryExpression(ref reader);
    }

    /// <summary>
    /// Recognizes the conversion operations <c>@wrap&lt;U&gt;</c> and <c>@bits&lt;U&gt;</c> (SPEC §2.5.1, §13.5.4): <c>wrap</c> or
    /// <c>bits</c> with adjacent Type arguments, parsed as a generic Type application and re-formed as an operation target.
    /// </summary>
    private static Koto ConversionOperationTarget(ref TokenReader reader, Koto target)
    {
        var inner = target is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } wrapped } ? wrapped : target;
        var generic = inner as GenericsKoto;
        var operation = generic is { TypeArguments.Count: > 0 } ? OperationName(generic.Identifier) : null;
        if (operation is not (Constants.WrapOperation or Constants.BitsOperation))
        {
            return target;
        }

        if (generic!.TypeArguments.Count != 1)
        {
            generic.Expected(SyntaxForm.ConversionTypeArgument);
        }

        return new TypeSemanticsKoto(ref reader, target.Span, operation == Constants.WrapOperation ? Constants.WrapOperation : Constants.BitsOperation, generic.TypeArguments[0]);

        static string? OperationName(Koto? identifier) => identifier switch
        {
            IdentifierNameKoto name => name.IdentifierName,
            TypeSemanticsKoto { Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null } simple => simple.Identifier,
            _ => null,
        };
    }

    /// <summary>
    /// Determines whether the current <c>@</c> starts the postfix follow operation <c>E@follow</c> (SPEC §13.5.5.1):
    /// the next token spells <c>follow</c>. A following slash is division, never a Semantics prefix.
    /// </summary>
    private static bool IsFollowOperation(ref TokenReader reader)
    {
        var next = reader.PeekToken(1);
        return next.Kind.IsIdentifierOrContextualKeyword() && reader.GetSpan(next).SequenceEqual(Constants.FollowOperation);
    }

    private static bool TryParsePostfixExpression(ref TokenReader reader, ref Koto left)
    {
        switch (reader.CurrentTokenKind)
        {
            case TokenKind.At:
                {
                    // SPEC 13.1: @follow is a level-1 postfix operation, so -r@follow.x is -((r@follow).x).
                    if (!IsFollowOperation(ref reader))
                    {
                        return false;
                    }

                    reader.Advance();
                    var target = new TypeSemanticsKoto(ref reader, reader.Read());
                    left = new ConversionKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, target.Span.End), left, target);
                    return true;
                }

            case TokenKind.Dot:
                {
                    var operatorRange = reader.CurrentTokenRange;
                    reader.Advance();

                    var accessor = reader.CurrentTokenKind == TokenKind.OpenParenthesis
                        ? ParseDeclarationType(ref reader, parseFunctionType: false, parseContainerSuffix: false)
                        : ParseMemberName(ref reader);
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
                    var unclosed = reader.ExpectCloser(TokenKind.CloseParenthesis, out var range);
                    if (arguments.Length > 0)
                    {
                        RecoverItem(ref reader, arguments[^1], unclosed);
                    }

                    end = Math.Max(end, range.End);

                    left = new InvocationKoto(
                        ref reader,
                        SourceSpan.FromBounds(left.Span.Start, end),
                        left,
                        arguments,
                        argumentLabels);
                    reader.Hover(left, openRange);
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
                        reader.Expect(SyntaxForm.Expression);
                        index = reader.NewErrorKoto();
                    }
                    else
                    {
                        var region = reader.EnterRegion();
                        index = ParseExpression(ref reader);
                        reader.RestoreRegion(region);
                    }

                    reader.TrySkipSeparatorsTo(TokenKind.CloseBracket);
                    RecoverItem(ref reader, index, reader.ExpectCloser(TokenKind.CloseBracket, out var range));
                    left = new IndexKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, Math.Max(Math.Max(openRange.End, index.Span.End), range.End)), left, index);

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
            OpensAdjacentTypeArguments(ref reader, left.Span);

    /// <summary>
    /// Determines whether the current <c>&lt;</c> applies Type arguments to the syntax that ends at <paramref name="targetSpan"/>: it is
    /// adjacent to it, and the tokenizer found its matching <c>&gt;</c> (SPEC 12.4.2, <see cref="Token.OpensTypeArguments"/>). Otherwise
    /// it compares values, so spaces around a comparison avoid the ambiguity.
    /// </summary>
    private static bool OpensAdjacentTypeArguments(ref TokenReader reader, SourceSpan targetSpan)
        => reader.CurrentToken.OpensTypeArguments && reader.CurrentTokenRange.Start == targetSpan.End;

    private static Koto[] ParseArgumentList(ref TokenReader reader, out string?[]? argumentLabels)
    {
        var region = reader.EnterRegion();
        var arguments = ParseArgumentListCore(ref reader, out argumentLabels);
        reader.RestoreRegion(region);
        return arguments;
    }

    private static Koto[] ParseArgumentListCore(ref TokenReader reader, out string?[]? argumentLabels)
    {// (arg0, arg1, )
        var arguments = default(TemporaryKotoList);
        var labels = default(TemporaryList<string?>);
        var hasLabels = false;
        Koto? last = null;
        argumentLabels = default;

        var tokenKind = reader.CurrentTokenKind;
        while (reader.CanRead &&
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
                // The misplaced argument is kept as a recovery, so overload selection rests on this Error.
                var misplaced = reader.Unexpected(SyntaxForm.PositionalAfterNamed);
                labels.Add(null);
                var positional = ParseRequiredExpression(ref reader);
                reader.CodeContext.RecordRecovery(positional, misplaced);
                arguments.Add(positional);
                last = positional;
                goto Separator;
            }

            last = ParseRequiredExpression(ref reader);
            arguments.Add(last);

Separator:

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
                // The list's shape is uncertain from here: the last argument is a recovery, so overload selection rests on the
                // Error. The recovery stays on the list's line, up to its next comma or closer, never in a following statement.
                var cause = reader.Expect(SyntaxForm.Comma);
                if (last is not null)
                {
                    reader.CodeContext.RecordRecovery(last, cause);
                }

                reader.SkipListItem();
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
                    // A function expression spans from its keyword, so its captures belong to it.
                    var start = reader.CurrentToken.Span.Start;
                    reader.Advance();
                    var function = ParseFuncDeclaration(ref reader, anonymous: true);
                    if (function is null)
                    {
                        return reader.NewErrorKoto();
                    }

                    function.Parse(ref reader);
                    function.Span = SourceSpan.FromBounds(start, function.Span.End);
                    return function;
                }

            case TokenKind.StartBlock:
                reader.Unexpected(SyntaxForm.IndentedBody);
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
                return IsBoundContainerQualifier(ref reader)
                    ? ParseDeclarationType(ref reader, parseFunctionType: false, parseContainerSuffix: false)
                    : ParseParenthesizedExpression(ref reader);

            case TokenKind.OpenBracket:
                return ParseCollectionLiteral(ref reader);

            case TokenKind.Separator:
            case TokenKind.EndBlock:
            case TokenKind.Invalid when !reader.CanRead:
                // The line, the body or the source ended where an expression was expected; the boundary stays for its owner.
                return new ErrorKoto(ref reader, new SourceSpan(reader.PreviousSyntaxEnd, 0)) { Cause = reader.Expect(SyntaxForm.Expression) };

            default:
                {
                    // The token that cannot start an expression is consumed for recovery; an invalid identifier was already reported.
                    reader.TryRead(out var token);
                    if (token.Kind.IsIdentifierOrContextualKeyword() &&
                        IdentifierNameKoto.TryCreate(ref reader, token, out var identifier))
                    {
                        return identifier;
                    }

                    if (token.Kind == TokenKind.Else)
                    {
                        // An else that no if body precedes introduces a body of its own; the body is read as part of the recovery.
                        var orphan = reader.Unexpected(SyntaxForm.ElseWithoutIf, token.Span);
                        var end = token.Span.End;
                        if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan || reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                        {
                            end = ParseRequiredBody(ref reader).Span.End;
                        }

                        return new ErrorKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, end)) { Cause = orphan };
                    }

                    return new ErrorKoto(ref reader, token.Span) { Cause = reader.Expect(SyntaxForm.Expression, token) };
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
            var segment = new StringLiteralKoto(ref reader, new SourceSpan(token.Span.Start + segmentStart, open - 1 - segmentStart));
            _ = segment.Literal;
            segments.Add(segment);
            var close = open + StringLiteralHelper.FindInterpolationEnd(text[open..]);
            if (close < open)
            {
                // The tokenizer ends an interpolated literal only after the matching ')'.
                break;
            }

            var tokenizer = new Tokenizer(reader.Diagnostic, reader.CodeContext.SourceDocument!, SourceSpan.FromBounds(token.Span.Start + open, token.Span.Start + close + 1))
            {
                CollectDocumentation = reader.CodeContext.Compilation.CollectDocumentation,
                OptionalHoverOwner = reader.CodeContext.Compilation.CollectHover ? reader.CodeContext.Compilation : null,
            };
            try
            {
                tokenizer.ReadAll();
                if (tokenizer.Documentation is { } documentation)
                {
                    if (reader.CodeContext.Documentation is { } existing)
                    {
                        existing.Merge(documentation);
                    }
                    else
                    {
                        reader.CodeContext.Documentation = documentation;
                    }
                }

                var nested = new TokenReader(reader.CodeContext, ref tokenizer);
                nested.TryConsume(TokenKind.OpenParenthesis);
                var expression = ParseRequiredExpression(ref nested);
                nested.TryConsume(TokenKind.CloseParenthesis, out _, true); // The nested source ends at the matching ')'; a failed consume skips to it.
                expressions.Add(expression);
            }
            finally
            {
                tokenizer.Dispose();
            }

            offset = segmentStart = close + 1;
        }

        var trailing = new StringLiteralKoto(ref reader, new SourceSpan(token.Span.Start + segmentStart, text.Length - 1 - segmentStart));
        _ = trailing.Literal;
        segments.Add(trailing);
        reader.RestoreContext(context);
        return new InterpolatedStringKoto(ref reader, token.Span, segments.ToArray(), expressions.ToArray());
    }

    private static Koto ParseParenthesizedExpression(ref TokenReader reader)
    {
        var region = reader.EnterRegion();
        var grouped = ParseGroupedExpression(ref reader);
        reader.RestoreRegion(region);
        return grouped;
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

            RecoverItem(ref reader, elements[^1], reader.ExpectCloser(TokenKind.CloseParenthesis, out var close));
            return new TupleLiteralKoto(ref reader, SourceSpan.FromBounds(openToken.Span.Start, Math.Max(end, close.End)), elements);
        }

        reader.TrySkipSeparatorsTo(TokenKind.CloseParenthesis);
        RecoverItem(ref reader, operand, reader.ExpectCloser(TokenKind.CloseParenthesis, out var closeRange));
        return new ParenthesizedKoto(ref reader, SourceSpan.FromBounds(openToken.Span.Start, Math.Max(end, closeRange.End)), operand);
    }

    private static Koto ParseCollectionLiteral(ref TokenReader reader)
    {
        var region = reader.EnterRegion();
        var literal = ParseCollectionLiteralCore(ref reader);
        reader.RestoreRegion(region);
        return literal;
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
            reader.ExpectCloser(TokenKind.CloseBracket, out var emptyDictionaryClose);
            return new DictionaryLiteralKoto(ref reader, SourceSpan.FromBounds(openRange.Start, Math.Max(openRange.End, emptyDictionaryClose.End)), []);
        }

        var first = ParseLiteralElement(ref reader);
        reader.SkipSeparators();
        if (reader.IsCurrentIdentifier("of"))
        {
            // SPEC 4.2: compound lengths must be parenthesized. Binding checks the constant grammar.
            if (first is not (NumberLiteralKoto { IsInteger: true } or IdentifierNameKoto or MemberAccessKoto or ParenthesizedKoto))
            {
                first.Unexpected(SyntaxForm.CompoundArrayLength);
            }

            reader.Advance();
            reader.SkipSeparators();
            var value = ParseLiteralElement(ref reader);
            reader.SkipSeparators();
            RecoverItem(ref reader, value, reader.ExpectCloser(TokenKind.CloseBracket, out var close));
            return new ArrayLiteralKoto(ref reader, SourceSpan.FromBounds(openRange.Start, Math.Max(value.Span.End, close.End)), [value], first);
        }

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
                    RecoverItem(ref reader, elements[^1], reader.Expect(SyntaxForm.Comma));
                    reader.SkipListItem();
                    if (reader.CurrentTokenKind != TokenKind.Comma)
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

            RecoverItem(ref reader, elements[^1], reader.ExpectCloser(TokenKind.CloseBracket, out var closeRange));
            var end = Math.Max(elements[^1].Span.End, closeRange.End);
            return new ArrayLiteralKoto(ref reader, SourceSpan.FromBounds(openRange.Start, Math.Max(openRange.End, end)), elements);
        }

        static DictionaryLiteralKoto ParseDictionaryLiteral(ref TokenReader reader, SourceSpan openRange, Koto firstKey)
        {
            var entries = new List<DictionaryLiteralEntry>(4);
            var key = firstKey;
            while (reader.CanRead)
            {
                if (reader.CurrentTokenKind != TokenKind.Colon)
                {
                    reader.Expect(SyntaxForm.Colon);
                    reader.SkipListItem();
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
                    var cause = reader.Expect(SyntaxForm.Comma);
                    if (entries.Count > 0)
                    {
                        RecoverItem(ref reader, entries[^1].Value, cause);
                    }

                    reader.SkipListItem();
                    if (reader.CurrentTokenKind != TokenKind.Comma)
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

                key = ParseLiteralElement(ref reader);
                reader.SkipSeparators();
            }

            var unclosed = reader.ExpectCloser(TokenKind.CloseBracket, out var closeRange);
            if (entries.Count > 0)
            {
                RecoverItem(ref reader, entries[^1].Value, unclosed);
            }

            var end = Math.Max(entries.Count == 0 ? firstKey.Span.End : entries[^1].Value.Span.End, closeRange.End);
            return new DictionaryLiteralKoto(ref reader, SourceSpan.FromBounds(openRange.Start, Math.Max(openRange.End, end)), entries);
        }

        static Koto ParseLiteralElement(ref TokenReader reader)
        {
            if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Comma or TokenKind.CloseBracket)
            {
                reader.Expect(SyntaxForm.Expression);
                return reader.NewErrorKoto();
            }

            return ParseExpression(ref reader);
        }
    }

    // The item before text that a list's recovery skipped is a recovery, as the last argument of a call is (DIAGNOSTICS.md §4.4):
    // what was written there is not known, so every check that reads the item rests on the Error, while the other items keep
    // the context of the list.
    private static void RecoverItem(ref TokenReader reader, Koto item, DiagnosticKey? cause)
    {
        if (cause is { } recovery && item is not ErrorKoto && reader.CodeContext.RecoveryCause(item) is null)
        {
            reader.CodeContext.RecordRecovery(item, recovery);
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
                // A closed range needs its end; the open form may omit it. The recovery node stands for the missing end.
                reader.Expect(SyntaxForm.Expression);
                return reader.NewErrorKoto();
            }

            return default;
        }

        return ParseExpression(ref reader, RangeRightBindingPower);
    }

    private static Koto ParseDeclarationType(ref TokenReader reader, bool parseOrigin = true, bool parseFunctionType = true, bool parseContainerSuffix = true, bool parseSuffix = true)
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
            if (parseContainerSuffix && reader.CurrentTokenKind == TokenKind.Dot)
            {
                type = ParseGroupedContainerSuffix(ref reader, type);
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
        var returnType = ParseFunctionResult(ref reader);
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
    /// Contract-qualified projection such as <c>I.(LendingIterator).LentItem</c>, SPEC 8.4.3).
    /// </summary>
    private static Koto ParseBareParenthesizedType(ref TokenReader reader)
        => ParseDeclarationType(ref reader, parseOrigin: false, parseFunctionType: false, parseContainerSuffix: false, parseSuffix: false);

    /// <summary>
    /// Parses a function result: an ordinary Type, or a Place result when an unqualified <c>place</c> is followed by
    /// <c>ref</c> or <c>uniq</c> and a slash (SPEC §7.1.1). The reference Type after <c>place</c> keeps its own
    /// <c>during</c>, which is the Place result's Origin.
    /// </summary>
    private static Koto ParseFunctionResult(ref TokenReader reader)
    {
        if (!reader.IsCurrentIdentifier("place") || !reader.PeekKind(1).IsIdentifierOrContextualKeyword() || reader.PeekKind(2) != TokenKind.Slash)
        {
            return ParseDeclarationType(ref reader);
        }

        var mode = reader.GetSpan(reader.PeekToken(1));
        if (!mode.SequenceEqual("ref") && !mode.SequenceEqual("uniq"))
        {
            return ParseDeclarationType(ref reader);
        }

        var keyword = reader.Read();
        var type = ParseType(ref reader, parseOrigin: true, optionalSuffix: false);
        DiagnosticKey? optional = null;
        if (reader.CurrentTokenKind == TokenKind.Question)
        {
            // A Place is never optional (SPEC 7.1.1): the '?' is reported and the result rests on the Error.
            optional = reader.Unexpected(SyntaxForm.PlaceResultSuffix);
            while (reader.TryConsume(TokenKind.Question))
            {
            }
        }

        ParseBorrowOriginSuffix(ref reader, type);
        var place = new PlaceResultKoto(ref reader, SourceSpan.FromBounds(keyword.Span.Start, type.Span.End), type);
        if (optional is { } cause)
        {
            reader.CodeContext.RecordRecovery(place, cause);
        }

        return place;
    }

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
