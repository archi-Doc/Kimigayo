// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

// The function header (SPEC 6.3.1, 7.1, Appendix F.6.3): the Name or init, Type parameters, the parameter list with its `!` boundary, the
// result and a constructor's base call; and a named function's body, or its absence on an import.
// Input: a reader after the function keyword with the item's prefix pending; the header's form as ParseFuncDeclaration's arguments.
// Output: a FunctionKoto without its body, which takes the prefix, or null when nothing of the header can be kept.
// Invariants: a declaration whose header failed is omitted with its body; a function expression leaves the rest to its expression. A
// specialization publishes no modifier: each it wrote is reported here.
// Prohibitions: a body's items belong to Parser.Blocks, the clauses after a header to Parser.Syntax.
public static partial class Parser
{
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

        // A specialization header redeclares no modifier (SPEC 8.13.2). The parser reports each one it reads (SpecializationHeader
        // below, StaticModifier, OpenModifier), so the specialization publishes none and no declaration check judges one again.
        var functionKoto = new FunctionKoto(
            ref reader,
            specialization ? context with { ModifierKind = ModifierKind.NoModifier, DispatchModifierSpan = default } : context,
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

            // A constructor accepts an access modifier and a common Body (SPEC 6.3).
            if (genericArguments is not null || returnType is not null ||
                context.AttributeKoto is not null || context.ModifierKind.Judged() != context.ModifierKind.ExtractAccessibilityModifiers())
            {
                // The attributes it reports are kept as its recovery, so they mark nothing and are not resolved (SPEC 6.7).
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

        // A declaration's header ends its line, since the declaration may be bodyless. A function expression's required body judges its
        // header's line (FunctionKoto.Parse) and stops at an enclosing grouping's closer; an attribute continuing it is misplaced (SPEC 6.7).
        if (anonymous)
        {
            reader.ReportMisplacedAttributes();
        }
        else if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
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
        // An attribute precedes the declaration (SPEC 6.7); one before the Name, or before a function expression's parameter
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
        var internalNameSpan = externalNameSpan;
        if (reader.TryConsume(TokenKind.EqualsGreaterThan) && !reader.TryReadName(out internalName, out internalNameSpan))
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
            // The recovery Type stands for the missing annotation; the rest of the parameter is skipped (SPEC 23.1.2).
            parameterType = new ErrorKoto(ref reader, new SourceSpan(reader.PreviousSyntaxEnd, 0)) { Cause = reader.Expect(SyntaxForm.ParameterType) };
            SkipParameter(ref reader);
        }

        Koto? defaultValue = null;
        if (reader.TryConsume(TokenKind.Equals))
        {
            defaultValue = ParseRequiredExpression(ref reader);
        }

        // A specialization's restated defaults and attributes are Binding's requirement (SPEC 8.13.2); the parser keeps them. A
        // misplaced attribute already reported before the list is kept here for the tree, not written on the parameter.
        if (anonymous && (internalName != externalName || defaultValue is not null || HasWrittenAttribute(attribute)))
        {
            reader.Unexpected(SyntaxForm.FunctionExpressionParameter, externalNameSpan);
        }

        parameter = new(externalName, internalName, parameterType, defaultValue, attribute) { ExternalNameSpan = externalNameSpan, InternalNameSpan = internalNameSpan };
        return true;
    }

    private static void SkipParameter(ref TokenReader reader)
        => reader.SkipListItem();

    /// <summary>Parses the result Type after a function header's arrow; a line that ends at the arrow leaves a recovery Type.</summary>
    private static Koto ParseFunctionReturnType(ref TokenReader reader, SourceSpan arrow)
    {
        reader.ReportMisplacedAttributes(); // A Type takes no attribute (SPEC 6.7); the result Type may follow it.
        if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock or TokenKind.EqualsGreaterThan)
        {
            reader.Diagnostic.Add(arrow, DiagnosticCode.MissingReturnType_Kd);
            return new ErrorKoto(ref reader, arrow) { Cause = reader.Diagnostic.LastError };
        }

        return ParseDeclarationType(ref reader);
    }

    /// <summary>Parses <c>base(arguments)</c> after the colon of a constructor header (SPEC 6.3.1).</summary>
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

        if (function.FindAttribute(AttributeKind.LibraryImport) is null)
        {
            reader.Expect(SyntaxForm.Body);
            function.MissingBody = true;
        }
    }
}
