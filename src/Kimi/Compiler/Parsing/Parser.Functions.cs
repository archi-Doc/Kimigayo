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
// Input: a reader after the function keyword with the item's prefix pending; the production of the caller's position as a FunctionForm.
// Output: a FunctionKoto without its body, which takes the prefix, or null when nothing of the header can be kept.
// Invariants: a declaration whose header failed is omitted with its body; a function expression leaves the rest to its expression. A
// specialization publishes no modifier: each it wrote is reported here. An override's Type parameters, `!` boundary and parameter
// defaults or attributes are one OverrideHeader record, and the override rests on it. The form selects what these readers judge: the
// attribute prefix, the captures, the Name, the generic list's production, the `!` boundary, an AnonymousParameter, the receiver
// shorthand, what follows the list and the node's form facts; their one other selector, the prefix's `override` read once, turns a
// Function into F.6.3's OverrideDeclaration and selects the override's rules (no Access, OverrideHeader) for every header under it that
// is no Constructor, Specialization or Expression, so for a Requirement under `override` too (PLAN G557: F.6.2 admits none). Judged
// elsewhere: a RequirementParameter's default and attribute (Parser.ParseRequirementBody); a SpecializationParameter's attribute and
// default (Binding.Specializations, SPEC 8.13.2). PLAN G557's parameter part makes the requirement's rules, the specialization's default
// and every parameter attribute row tests here.
// Prohibitions: a body's items belong to Parser.Blocks, the clauses after a header to Parser.Syntax.
public static partial class Parser
{
    /// <summary>The production a function header is read under (SPEC F.6.2, F.6.3, F.9.1; 6.4.1's override header). Each reader of a
    /// header part tests the row where it reads that part; the rules no row carries yet are named in this file's contract.</summary>
    internal enum FunctionForm
    {
        /// <summary>FunctionHeader (F.6.3), of a FunctionDefinition or a BodilessFunctionDeclaration: a Name, GenericParameters and
        /// ParameterList&lt;Parameter&gt;.</summary>
        Function,

        /// <summary>OverrideDeclaration (F.6.3, 6.4.1; the owner's O-222 (b)): read as a FunctionHeader, whose Type parameters, <c>!</c>
        /// boundary and parameter defaults or attributes are one OverrideHeader record. The header reads a Function under the prefix's
        /// modifier as this row; it takes no attribute prefix (DECISIONS R9-M4).</summary>
        Override,

        /// <summary>FunctionRequirement (F.6.2): ParameterList&lt;RequirementParameter&gt;, whose defaults and attributes
        /// Parser.ParseRequirementBody judges after the header.</summary>
        Requirement,

        /// <summary>ConstructorDeclaration (F.6.3): <c>init</c>, ParameterList&lt;Parameter&gt; without a receiver (SPEC 7.3), a BaseInitializer.</summary>
        Constructor,

        /// <summary>SpecializationDeclaration (F.6.3): a Name, TypeArguments and SpecializationParameters without the <c>!</c> boundary,
        /// whose restated attributes and defaults Binding judges (SPEC 8.13.2).</summary>
        Specialization,

        /// <summary>FunctionExpression (F.9.1): a CaptureList, no Name, and AnonymousParameters without the <c>!</c> boundary.</summary>
        Expression,
    }

    /// <summary>A parameter list as read (Appendix F ParameterList&lt;P&gt;, or the boundary-less list of a Specialization or an Expression).</summary>
    /// <param name="Parameters">The parameters, or <see langword="null"/> when none was kept.</param>
    /// <param name="NameBoundary">The index of the first named parameter, or -1 without a boundary.</param>
    /// <param name="Close">The closing parenthesis.</param>
    private readonly record struct ParameterList(List<FunctionParameterKoto>? Parameters, int NameBoundary, SourceSpan Close);

    /// <summary>Parses a function header after its keyword: the Name, Type parameters, parameters and result (SPEC 7.1, Appendix F FunctionHeader).</summary>
    /// <param name="reader">The token reader; at <c>init</c> for a Constructor.</param>
    /// <param name="form">The production of the caller's position; a Function under <c>override</c> is read as an Override.</param>
    /// <returns>The parsed function without its body, or <see langword="null"/> after an error that leaves no header to keep.</returns>
    internal static FunctionKoto? ParseFuncDeclaration(ref TokenReader reader, FunctionForm form)
    {
        // The header reads the prefix's override once: a Function under it is F.6.3's OverrideDeclaration (6.4.1), and the override's
        // rules (no Access, OverrideHeader) judge every header under it that is no Constructor, Specialization or Expression.
        var overrides = form is not (FunctionForm.Constructor or FunctionForm.Specialization or FunctionForm.Expression) &&
            (reader.ModifierKind & ModifierKind.Override) != 0;
        if (form == FunctionForm.Function && overrides)
        {
            form = FunctionForm.Override;
        }

        // A constructor, a specialization and an override take no attributes (SPEC 6.3, 8.13.2, 6.4.1): the prefix's are reported
        // and attach to nothing. A requirement's were reported by its Contract, as every Contract item's are; a function expression
        // is never read under a pending override. An OverrideDeclaration has no Access either (F.6.3).
        if (form is FunctionForm.Constructor or FunctionForm.Specialization or FunctionForm.Override)
        {
            ReportPendingAttributes(ref reader);
        }

        if (overrides)
        {
            RejectAccessWord(ref reader);
        }

        var context = reader.TakeContext();
        var captures = form == FunctionForm.Expression && reader.CurrentTokenKind == TokenKind.OpenBracket ? ParseCaptures(ref reader) : null;
        var methodToken = reader.CurrentToken;
        if (!TryParseFunctionName(ref reader, form, out var methodName))
        {
            return Omitted(ref reader, form);
        }

        // The header's form selects the list's production: a specialization's TypeArguments, else GenericParameters (SPEC F.6.3).
        List<TypeKoto>? genericArguments = null;
        if (reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = form == FunctionForm.Specialization
                ? ParseSpecializationArguments(ref reader)
                : ParseGenericParameters(ref reader, allowLength: true);
        }

        RejectCallableOriginList(ref reader);
        if (!reader.TryConsume(TokenKind.OpenParenthesis, out _, true) || ParseParameterList(ref reader, form) is not { } list)
        {
            return Omitted(ref reader, form);
        }

        Koto? returnType = null;
        var end = list.Close.End;
        if (reader.TryConsume(TokenKind.MinusGreaterThan, out var returnArrowRange, false))
        {
            returnType = ParseFunctionReturnType(ref reader, returnArrowRange);
            end = returnType.Span.End;
        }

        // A specialization header redeclares no modifier (SPEC 8.13.2). The parser reports each one it reads (its access word is
        // rejected before this header, Parser.RejectAccessWord; unsafe, virtual and override are SpecializationHeader below; then
        // StaticModifier, OpenModifier), so the specialization publishes none and no declaration check judges one again.
        var header = form == FunctionForm.Specialization ? context with { ModifierKind = ModifierKind.NoModifier, DispatchModifierSpan = default } : context;
        var functionKoto = new FunctionKoto(
            ref reader,
            header,
            SourceSpan.FromBounds(methodToken.Span.Start, end),
            methodName,
            genericArguments,
            list.Parameters,
            returnType)
        {
            NameBoundaryIndex = list.NameBoundary,
            HeaderEnd = list.Close.End,
            IsAnonymous = form == FunctionForm.Expression,
            IsConstructor = form == FunctionForm.Constructor,
            IsRequirement = form == FunctionForm.Requirement,
            IsSpecialization = form == FunctionForm.Specialization,
        };
        functionKoto.SetCaptures(captures);
        if (form == FunctionForm.Expression)
        {
            if (methodName.Length != 0)
            {
                reader.Unexpected(SyntaxForm.FunctionExpressionName, methodToken.Span);
            }
        }
        else
        {
            reader.Document(functionKoto, functionKoto.Span.End, context.AttributeKoto);
            reader.Hover(functionKoto, methodToken.Span);
        }

        if (form == FunctionForm.Constructor)
        {
            if (reader.TryConsume(TokenKind.Colon))
            {
                ParseConstructorInitializer(ref reader, functionKoto);
            }

            // A constructor accepts an access modifier and a common Body (SPEC 6.3).
            if (genericArguments is not null || returnType is not null ||
                context.ModifierKind.Judged() != context.ModifierKind.ExtractAccessibilityModifiers())
            {
                functionKoto.Unexpected(SyntaxForm.ConstructorHeader);
            }
        }

        if (form == FunctionForm.Specialization && (genericArguments is null || context.ModifierKind.Judged() != 0))
        {
            // Without Type arguments the specialization cannot be matched, so its Binding rests on this Error; a decoration leaves it checkable.
            var key = functionKoto.Unexpected(SyntaxForm.SpecializationHeader);
            if (genericArguments is null && key is { } cause)
            {
                reader.CodeContext.RecordRecovery(functionKoto, cause);
            }
        }

        // F.3.3: a FunctionHeader and an OverrideDeclaration write at most one of unsafe, virtual and override (a constructor's and a
        // specialization's header rows judge each modifier they take none of). The record is at the second word, as the prefix's other
        // modifier records are; the function's declaration checks rest on it.
        if (form is not (FunctionForm.Constructor or FunctionForm.Specialization or FunctionForm.Expression) && context.SecondModifierSpan != default)
        {
            reader.CodeContext.RecordRecovery(functionKoto, reader.Unexpected(SyntaxForm.SecondModifier, context.SecondModifierSpan));
        }

        if (overrides && (genericArguments is not null || list.NameBoundary >= 0 ||
            list.Parameters?.Exists(static x => x.DefaultValue is not null || x.AttributeChain is not null) == true) &&
            functionKoto.Unexpected(SyntaxForm.OverrideHeader) is { } overrideCause)
        {
            // F.6.3's OverrideDeclaration takes no Type parameters, no "!" boundary and no parameter default or attribute: the override
            // inherits them (SPEC 6.4.1). Its slot matching and declaration checks rest on this Error.
            reader.CodeContext.RecordRecovery(functionKoto, overrideCause);
        }

        // A declaration's header ends its line, since the declaration may be bodyless. A function expression's required body judges its
        // header's line (FunctionKoto.Parse) and stops at an enclosing grouping's closer; an attribute continuing it is misplaced (SPEC 6.7).
        if (form == FunctionForm.Expression)
        {
            reader.ReportMisplacedAttributes();
        }
        else if (reader.CurrentTokenKind != TokenKind.EqualsGreaterThan)
        {
            reader.ExpectLineEnd(bodyMayFollow: true);
        }

        return functionKoto;

        // A declaration whose header failed is skipped with its body; a function expression leaves the rest to its expression.
        static FunctionKoto? Omitted(ref TokenReader reader, FunctionForm form)
        {
            if (form != FunctionForm.Expression)
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
    private static bool TryParseFunctionName(ref TokenReader reader, FunctionForm form, [NotNullWhen(true)] out string? name)
    {
        // An attribute precedes the declaration (SPEC 6.7); one before the Name, or before a function expression's parameter
        // list, is reported and the header continues after it.
        if (reader.CurrentTokenKind == TokenKind.Sharp)
        {
            reader.ReportMisplacedAttributes(form == FunctionForm.Expression ? TokenKind.OpenParenthesis : TokenKind.Invalid);
        }

        var token = reader.CurrentToken;
        if (form == FunctionForm.Expression && token.Kind == TokenKind.OpenParenthesis)
        {
            name = string.Empty;
            return true;
        }

        if (form == FunctionForm.Expression && !(token.Kind.IsIdentifierOrContextualKeyword() && reader.PeekKind(1) == TokenKind.OpenParenthesis))
        {
            // A function expression continues with its parameter list; only a Name before one is parsed further, to be reported as misplaced.
            reader.Expect(SyntaxForm.OpenParenthesis);
            name = null;
            return false;
        }

        if (form == FunctionForm.Constructor && token.Kind == TokenKind.Init)
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
    /// Parses the parameters after the opening parenthesis through the closing one (Appendix F ParameterList&lt;P&gt;): attributes before
    /// each parameter, the one <c>!</c> boundary before the named section, and the separating commas. A Specialization's and an
    /// Expression's lists are ParameterList&lt;P&gt; without the boundary, which is reported there.
    /// </summary>
    /// <param name="reader">The token reader positioned after <c>(</c>.</param>
    /// <param name="form">The header's production, which decides whether the list has a boundary and is passed to each parameter.</param>
    /// <returns>The list, or <see langword="null"/> when it is not closed; the header is then dropped.</returns>
    private static ParameterList? ParseParameterList(ref TokenReader reader, FunctionForm form)
    {
        List<FunctionParameterKoto>? parameters = null;
        var nameBoundary = -1;
        var afterComma = false;
        var precedingComma = default(SourceSpan);
        while (reader.CanRead)
        {
            // Attributes precede a parameter's Name, and the one boundary precedes the named section; either may follow the other.
            var start = reader.Position;
            AttributeKoto? attribute = null;
            reader.SkipSeparators();
            while (true)
            {
                if (reader.CurrentTokenKind == TokenKind.Sharp)
                {
                    attribute = ParseAttributeKoto(ref reader, attribute);
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

                // A function expression's and a specialization's lists are written without ParameterList's boundary (F.9.1, F.6.3).
                if (form is FunctionForm.Expression or FunctionForm.Specialization)
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
                if (attribute is not null)
                {
                    // Attributes before the closer precede no parameter: its Name was expected there, and they attach to nothing,
                    // neither to the result Type nor to the body.
                    reader.Expect(SyntaxForm.Name);
                }

                break;
            }

            if (!reader.CanRead)
            {
                break;
            }

            if (TryParseParameter(ref reader, form, parameters, attribute, out var parameter))
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

        if (reader.ExpectCloser(TokenKind.CloseParenthesis, out var close) is not null && close == default)
        {
            return null; // The list ended without a closer, written or supplied by the tokenizer.
        }

        if (nameBoundary >= 0 && nameBoundary == (parameters?.Count ?? 0))
        {
            reader.Diagnostic.Add(close, DiagnosticCode.EmptyNamedParameterSection_Kd);
        }

        return new(parameters, nameBoundary, close);
    }

    /// <summary>
    /// Parses one parameter: its external Name, optional internal Name, Type and default, the union of Appendix F's Parameter,
    /// RequirementParameter, SpecializationParameter and AnonymousParameter, so a part its row's production lacks is kept for its judge.
    /// The form judges an AnonymousParameter and the receiver shorthand here; the other rows' parts are judged where this file's
    /// contract names.
    /// </summary>
    /// <returns><see langword="false"/> after an error, with the rest of the parameter skipped.</returns>
    private static bool TryParseParameter(
        ref TokenReader reader,
        FunctionForm form,
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

        if (form != FunctionForm.Expression && previous is not null)
        {
            foreach (var earlier in previous)
            {
                if (earlier.ExternalName == externalName)
                {
                    var first = reader.Diagnostic.Relate("declaration", earlier.ExternalNameSpan, reader.Diagnostic.Document, "first external parameter name");
                    reader.Diagnostic.AddSyntax(externalNameSpan, DiagnosticCode.DuplicateExternalParameterName_Kd, externalName, related: [first]);
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

        // ReceiverShorthand: every production but AnonymousParameter has it, and a constructor has no receiver (SPEC 7.3).
        var allowsReceiverShorthand = form is not (FunctionForm.Expression or FunctionForm.Constructor) && externalName == "self" && internalName == "self";
        Koto parameterType;
        if (reader.TryConsume(TokenKind.Colon))
        {
            parameterType = ParseType(ref reader, TypeContext.Type);
        }
        else if (form == FunctionForm.Expression || allowsReceiverShorthand)
        {
            parameterType = new SyntaxFormKoto(ref reader, externalNameSpan, KotoKind.InferredType, TokenKind.Underscore.ToText(), []);
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

        // An AnonymousParameter has no internal Name, default or attribute. A requirement's default and attribute are judged after its
        // header (ParseRequirementBody); a specialization's restated ones are Binding's requirement (SPEC 8.13.2) and an override's
        // are its header's OverrideHeader (F.6.3); the parser keeps them (PLAN G557's parameter part). A misplaced attribute already
        // reported before the list is kept here for the tree, not written on the parameter.
        if (form == FunctionForm.Expression && (internalName != externalName || defaultValue is not null || attribute is not null))
        {
            reader.Unexpected(SyntaxForm.FunctionExpressionParameter, externalNameSpan);
        }

        parameter = new(externalName, internalName, parameterType, defaultValue, attribute)
        {
            ExternalNameSpan = externalNameSpan,
            InternalNameSpan = internalNameSpan,
        };
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

        return ParseType(ref reader, TypeContext.Type);
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

        var target = new SyntaxFormKoto(ref reader, baseSpan, KotoKind.ConstructorReference, TokenKind.Base.ToText(), []);
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
