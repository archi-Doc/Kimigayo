// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Keep related parsing helpers together.

public static partial class Parser
{
    private static bool IsParenthesizedTypeRequirement(ref TokenReader reader)
    {
        // A comma at this level identifies a Tuple; an arrow after the closing
        // parenthesis identifies a Function Parameter List. Nested Types own their commas.
        if (reader.PeekKind() == TokenKind.CloseParenthesis)
        {
            return true;
        }

        var parentheses = 1;
        var brackets = 0;
        var arguments = 0;
        for (var offset = 1; ; offset++)
        {
            var kind = reader.PeekKind(offset);
            if (kind is TokenKind.Invalid or TokenKind.EndBlock or TokenKind.StartBlock)
            {
                return false;
            }

            if (kind == TokenKind.OpenParenthesis)
            {
                parentheses++;
            }
            else if (kind == TokenKind.CloseParenthesis && --parentheses == 0)
            {
                return reader.PeekKind(offset + 1) == TokenKind.MinusGreaterThan;
            }
            else if (kind == TokenKind.OpenBracket)
            {
                brackets++;
            }
            else if (kind == TokenKind.CloseBracket)
            {
                brackets--;
            }
            else if (brackets == 0)
            {
                if (kind == TokenKind.LessThan)
                {
                    arguments++;
                }
                else if (kind == TokenKind.GreaterThan)
                {
                    arguments--;
                }
                else if (kind == TokenKind.GreaterThanGreaterThan)
                {
                    arguments -= 2;
                }
                else if (parentheses == 1 && arguments == 0)
                {
                    if (kind == TokenKind.Comma)
                    {
                        return true;
                    }

                    if (kind is TokenKind.And or TokenKind.Or or TokenKind.Not)
                    {
                        return false;
                    }
                }
            }
        }
    }

    private static Koto ParseConstraintSubject(ref TokenReader reader)
    {
        Koto subject;
        if (reader.CurrentTokenKind == TokenKind.Self)
        {
            var token = reader.Read();
            IdentifierNameKoto.TryCreate(ref reader, new Token(TokenKind.Identifier, token.Span), out var self);
            subject = self!;
        }
        else
        {
            subject = reader.CurrentTokenKind == TokenKind.ColonColon ? ParseRootName(ref reader, false) : ParseName(ref reader);
        }

        while (reader.TryConsume(TokenKind.Dot))
        {
            // SPEC 8.4.3: a Contract-qualified projection such as I.(LendingIterator).LentItem(step) names its Contract in parentheses.
            var member = reader.CurrentTokenKind == TokenKind.OpenParenthesis
                ? ParseBareParenthesizedType(ref reader)
                : ParseName(ref reader);
            subject = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(subject.Span.Start, member.Span.End), subject, member);
        }

        return subject;
    }

    private static CaptureKoto[] ParseCaptures(ref TokenReader reader)
    {
        reader.Advance();
        var captures = default(TemporaryList<CaptureKoto>);
        while (reader.CanRead && reader.CurrentTokenKind != TokenKind.CloseBracket)
        {
            var start = reader.CurrentTokenRange.Start;
            var mutable = reader.TryConsume(TokenKind.Var);
            var token = reader.CurrentToken;
            if (!token.Kind.IsIdentifierOrContextualKeyword() || !reader.TryGetIdentifier(token, out var name))
            {
                reader.Expect(SyntaxForm.Name);
                break;
            }

            reader.Advance();
            string? operation = null;
            var end = token.Span.End;
            if (reader.TryConsume(TokenKind.At))
            {
                var op = reader.CurrentToken;
                operation = op.Kind.IsIdentifierOrContextualKeyword() ? reader.GetSpan(op) switch
                {
                    // SPEC 7.6.2: var changes only the mutability of the environment binding, with every operation.
                    Constants.MoveOperation => Constants.MoveOperation,
                    Constants.RefKeyword => Constants.RefKeyword,
                    Constants.UniqKeyword => Constants.UniqKeyword,
                    _ => null,
                } : null;
                if (operation is null)
                {
                    // A Name that is no operation is consumed with the report; punctuation still belongs to the list.
                    reader.Expect(SyntaxForm.CaptureOperation, op);
                    if (op.Kind.IsIdentifierOrContextualKeyword())
                    {
                        reader.Advance();
                        end = op.Span.End;
                    }
                }
                else
                {
                    reader.Advance();
                    end = op.Span.End;
                }
            }

            captures.Add(new CaptureKoto(name, mutable, operation, SourceSpan.FromBounds(start, end)));
            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }
        }

        reader.ExpectCloser(TokenKind.CloseBracket, out _);
        return captures.ToArray();
    }

    internal static FunctionKoto? ParseSpecialization(ref TokenReader reader)
    {
        reader.Advance(2);
        var function = ParseFuncDeclaration(ref reader, specialization: true);
        if (function is not null)
        {
            ParseNamedFunctionBody(ref reader, function);
        }

        return function;
    }

    /// <summary>Checks receiver syntax of a function declared directly in a struct, enum, or Contract.</summary>
    /// <param name="function">The member function.</param>
    /// <remarks>At most one parameter has the internal Name self, without rename or default (SPEC 7.3).</remarks>
    internal static void ValidateReceiverParameters(FunctionKoto function)
    {
        var hasReceiver = false;
        foreach (var parameter in function.Parameters)
        {
            if (parameter.InternalName != "self")
            {
                continue;
            }

            // A repeated self is already a duplicate external name; a renamed or defaulted one is a misplaced receiver, and the
            // function's own checks rest on it.
            if (!hasReceiver && (parameter.ExternalName != "self" || parameter.DefaultValue is not null) && parameter.Type.Unexpected(SyntaxForm.ReceiverParameter) is { } cause)
            {
                function.CodeContext.RecordRecovery(function, cause);
            }

            hasReceiver = true;
            if (function.NameBoundaryIndex >= 0 && function.NameBoundaryIndex == function.Parameters.Count - 1 &&
                ReferenceEquals(parameter, function.Parameters[^1]))
            {
                parameter.Type.AddDiagnostic(DiagnosticCode.EmptyNamedParameterSection_Kd);
            }
        }
    }

    internal static void ParseRequirementBody(ref TokenReader reader, FunctionKoto function)
    {
        function.IsRequirement = true;
        if ((function.Modifier.Judged() & ~(ModifierKind.Virtual | ModifierKind.Override)) is not (ModifierKind.NoModifier or ModifierKind.Unsafe) || function.AttributeChain is not null)
        {
            function.Unexpected(SyntaxForm.Decoration);
        }

        foreach (var parameter in function.Parameters)
        {
            if (parameter.DefaultValue is not null || HasWrittenAttribute(parameter.AttributeChain))
            {
                parameter.Type.Unexpected(SyntaxForm.RequirementParameterDefault);
            }
        }

        if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan)
        {
            reader.Unexpected(SyntaxForm.RequirementBody);
            reader.Advance();
            _ = ParseRequiredExpression(ref reader);
        }

        ParseSignatureClauses(ref reader, function);
    }

    internal static void ParseSignatureClauses(ref TokenReader reader, FunctionKoto function)
    {
        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return;
        }

        reader.Advance();
        while (reader.CanRead)
        {
            reader.SkipSeparators();
            if (reader.TryConsume(TokenKind.EndBlock))
            {
                return;
            }

            if (IsOriginRelationStart(ref reader))
            {
                OriginClauses.Add(function, ParseOriginRelation(ref reader));
                continue;
            }

            if (IsEffectStart(ref reader, specification: true))
            {
                function.AddEffectBound(ParseEffectBound(ref reader));
                continue;
            }

            if (!IsTypeConstraintStart(ref reader, declarationContext: true))
            {
                reader.Expect(SyntaxForm.Constraint);
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                continue;
            }

            var constraint = ParseTypeConstraint(ref reader);
            if (constraint is not null)
            {
                function.AddTypeConstraint(constraint);
            }
        }
    }

    private static Koto ParseName(ref TokenReader reader)
    {
        var token = reader.CurrentToken;
        if (token.Kind.IsIdentifierOrContextualKeyword() && IdentifierNameKoto.TryCreate(ref reader, token, out var name))
        {
            reader.Advance();
            return name;
        }

        // A Name missing at a boundary stands at an insertion point after the preceding token; any other token is consumed for recovery.
        var cause = reader.Expect(SyntaxForm.Name);
        var boundary = !reader.CanRead || IsExpressionBoundary(ref reader);
        var missing = boundary ? new SourceSpan(reader.PreviousSyntaxEnd, 0) : token.Span;
        if (!boundary)
        {
            reader.Advance();
        }

        return new ErrorKoto(ref reader, missing) { Cause = cause };
    }

    private static Koto ParseRootName(ref TokenReader reader, bool type, bool disambiguateGenerics = false)
    {
        // A root-qualified Type is the path after '::'; its Origin and '?' belong to the enclosing Type (SPEC 3.2.3, 3.3.6).
        var start = reader.Read().Span.Start;
        var name = type ? ParseType(ref reader, parseOrigin: false, disambiguateGenerics, optionalSuffix: false) : ParseName(ref reader);
        return new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(start, name.Span.End), KotoKind.RootName, "::", [name]);
    }

    private static Koto ParseFixedArrayType(ref TokenReader reader)
    {
        var start = reader.Read().Span.Start;
        Koto length;
        if (reader.AllowArrayElementInference && reader.CurrentTokenKind == TokenKind.Underscore)
        {
            length = new TypeSemanticsKoto(ref reader, reader.Read());
            reader.HasInferredArrayElement = true;
        }
        else
        {
            // Read the complete arithmetic shape once, then require grouping for a compound length. This preserves
            // 'of' and the independent element Type even when the written length omitted its parentheses.
            length = ParseArrayLength(ref reader, expression: true);
            CheckArrayLengthSyntax(ref reader, length);
        }

        DiagnosticKey? cause = null;
        Koto element;
        if (reader.IsCurrentIdentifier("of"))
        {
            reader.Advance();
        }
        else if (length is ErrorKoto failed)
        {
            // The head supplied no length, so do not guess another interpretation of the remainder or report
            // 'of'/element failures caused by that guess. Recovery stays inside this bracket pair.
            reader.SkipListItem();
            element = new ErrorKoto(ref reader, reader.InsertionSpan) { Cause = failed.Cause };
            reader.ExpectCloser(TokenKind.CloseBracket, out var end);
            return new FixedArrayTypeKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, end.End)), length, element);
        }
        else
        {
            cause = reader.Expect(SyntaxForm.OfKeyword);
        }

        if (reader.AllowArrayElementInference && reader.CurrentTokenKind == TokenKind.Underscore)
        {
            element = new TypeSemanticsKoto(ref reader, reader.Read());
            reader.HasInferredArrayElement = true;
        }
        else
        {
            // The placeholder is forbidden in generic arguments and other compound element Types (SPEC 4.3).
            var allowInference = reader.AllowArrayElementInference;
            reader.AllowArrayElementInference = allowInference && reader.CurrentTokenKind == TokenKind.OpenBracket;
            element = ParseDelimitedType(ref reader);
            reader.AllowArrayElementInference = allowInference;
        }

        var unclosed = reader.ExpectCloser(TokenKind.CloseBracket, out var close);
        var array = new FixedArrayTypeKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, Math.Max(element.Span.End, close.End))), length, element);
        RecoverItem(ref reader, array, cause ?? unclosed);
        return array;
    }

    // Types and fill literals use the same surface grammar (SPEC 4.2); Binding checks constant eligibility and value.
    private static void CheckArrayLengthSyntax(ref TokenReader reader, Koto length)
    {
        if (length is not (NumberLiteralKoto { IsInteger: true } or IdentifierNameKoto or MemberAccessKoto or ParenthesizedKoto or ErrorKoto) &&
            length.CodeContext.RecoveryCause(length) is null)
        {
            RecoverItem(ref reader, length, length.Unexpected(SyntaxForm.CompoundArrayLength));
        }
    }

    private static Koto ParseArrayLength(ref TokenReader reader, int precedence = 0, bool expression = false)
    {
        var token = reader.CurrentToken;
        Koto left;
        if (reader.TryConsume(TokenKind.OpenParenthesis))
        {
            var value = ParseArrayLength(ref reader, expression: true);
            RecoverItem(ref reader, value, reader.ExpectCloser(TokenKind.CloseParenthesis, out var close));
            left = new ParenthesizedKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, Math.Max(value.Span.End, close.End)), value);
        }
        else if (expression && token.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            reader.Advance();
            left = KotoHelper.NewUnaryKoto(ref reader, token, ParseArrayLength(ref reader, 100, true));
        }
        else if (token.Kind == TokenKind.NumericLiteral)
        {
            reader.Advance();
            var number = new NumberLiteralKoto(ref reader, token);
            if (!number.IsInteger)
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.InvalidNumericLiteral_Kd);
                RecoverItem(ref reader, number, reader.Diagnostic.LastError);
            }

            left = number;
        }
        else if ((token.Kind.IsIdentifierOrContextualKeyword() && !reader.IsCurrentIdentifier("of")) || token.Kind == TokenKind.ColonColon)
        {
            left = reader.CurrentTokenKind == TokenKind.ColonColon ? ParseRootName(ref reader, false) : ParseName(ref reader);
            while (reader.TryConsume(TokenKind.Dot))
            {
                var right = ParseName(ref reader);
                left = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, right.Span.End), left, right);
            }
        }
        else
        {
            var cause = reader.Expect(SyntaxForm.ArrayLength);
            left = new ErrorKoto(ref reader, reader.InsertionSpan) { Cause = cause };
            if (reader.CanRead && !IsExpressionBoundary(ref reader) && !reader.IsCurrentIdentifier("of"))
            {
                reader.Advance();
            }
        }

        while (expression)
        {
            var kind = reader.CurrentTokenKind;
            var power = kind is TokenKind.Plus or TokenKind.Minus ? 10 : kind is TokenKind.Asterisk or TokenKind.Slash or TokenKind.Percent ? 20 : 0;
            if (power == 0 || power < precedence)
            {
                break;
            }

            var op = reader.Read();
            left = KotoHelper.NewBinaryKoto(ref reader, op, left, ParseArrayLength(ref reader, power + 1, true));
        }

        return left;
    }

    private static Koto ParseTypeArgument(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.NumericLiteral || IsLengthExpression(ref reader))
        {
            return ParseArrayLength(ref reader);
        }

        return ParseDelimitedType(ref reader);
    }

    private static Koto ParseDelimitedType(ref TokenReader reader)
    {
        var requirement = reader.ConstraintRequirement;
        reader.ConstraintRequirement = false;
        var type = ParseDeclarationType(ref reader);
        reader.ConstraintRequirement = requirement;
        return type;
    }

    private static bool IsLengthExpression(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind != TokenKind.OpenParenthesis)
        {
            return false;
        }

        var depth = 0;
        for (var i = 0; i < reader.Remaining; i++)
        {
            var kind = reader.PeekKind(i);
            if (kind == TokenKind.OpenParenthesis)
            {
                depth++;
            }
            else if (kind == TokenKind.CloseParenthesis && --depth == 0)
            {
                return false;
            }
            else if (kind is TokenKind.NumericLiteral or TokenKind.Plus or TokenKind.Minus or TokenKind.Asterisk or TokenKind.Percent ||
                (kind == TokenKind.Slash && i >= 2 && reader.PeekKind(i - 2) == TokenKind.Dot))
            {
                return true;
            }
            else if (kind is TokenKind.OpenBracket or TokenKind.Comma or TokenKind.MinusGreaterThan or TokenKind.EndBlock or TokenKind.Invalid)
            {
                // A bracket starts a fixed-array Type, not a length expression. Its numeric
                // length must not classify an enclosing grouped Type argument as a value.
                return false;
            }
        }

        return false;
    }

    private static Koto ParseInferredCase(ref TokenReader reader)
    {
        var start = reader.Read().Span.Start;
        var name = ParseName(ref reader);
        return new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(start, name.Span.End), KotoKind.InferredCase, ".", [name]);
    }

    private static Koto ParsePattern(ref TokenReader reader)
    {
        var token = reader.CurrentToken;
        if (token.Kind is TokenKind.Let or TokenKind.Var)
        {
            reader.Advance();
            var name = ParseName(ref reader);
            if (name is ErrorKoto)
            {
                // '_' is not a Name (SPEC 2.5): 'let _' and 'var _' are reported as a missing Name, and the arm's Pattern rests on it.
                return name;
            }

            return new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, name.Span.End), KotoKind.BindingPattern, token.Kind == TokenKind.Let ? "let " : "var ", [name]);
        }

        if (reader.TryConsume(TokenKind.OpenParenthesis))
        {
            var items = default(TemporaryKotoList);
            var comma = false;
            while (reader.CanRead && reader.CurrentTokenKind != TokenKind.CloseParenthesis)
            {
                items.Add(ParsePattern(ref reader));
                if (!reader.TryConsume(TokenKind.Comma))
                {
                    break;
                }

                comma = true;
            }

            reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);
            var children = items.ToArray();
            var span = SourceSpan.FromBounds(token.Span.Start, Math.Max(token.Span.End, Math.Max(close.End, children.Length == 0 ? 0 : children[^1].Span.End)));
            if (children.Length == 0)
            {
                return new UnitLiteralKoto(ref reader, span);
            }

            return children.Length == 1 && !comma
                ? new ParenthesizedKoto(ref reader, span, children[0])
                : new SyntaxFormKoto(ref reader, span, KotoKind.TuplePattern, "(", children, suffix: children.Length == 1 ? ",)" : ")");
        }

        if (token.Kind is TokenKind.True or TokenKind.False or TokenKind.CharLiteral or TokenKind.StringLiteral or TokenKind.NumericLiteral or TokenKind.Minus)
        {
            var negative = reader.TryConsume(TokenKind.Minus);
            var literal = ParsePrimaryExpression(ref reader);
            if ((negative && literal is not NumberLiteralKoto { IsInteger: true }) || literal is NumberLiteralKoto { IsInteger: false })
            {
                // The value matches nothing; the arm's Pattern rests on the Error.
                return new ErrorKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, literal.Span.End)) { Cause = reader.Expect(SyntaxForm.PatternLiteral, token) };
            }

            return negative ? KotoHelper.NewUnaryKoto(ref reader, token, literal) : literal;
        }

        if (reader.CurrentTokenKind == TokenKind.Underscore)
        {
            return new IdentifierNameKoto(ref reader, reader.Read(), "_");
        }

        Koto reference;
        DiagnosticKey? cause = null; // A malformed case Pattern is read to its end and then rests on the Error.
        if (token.Kind == TokenKind.Dot)
        {
            reference = ParseInferredCase(ref reader);
        }
        else
        {
            reference = reader.CurrentTokenKind == TokenKind.ColonColon ? ParseRootName(ref reader, false) : ParseName(ref reader);
            var qualified = false;
            while (true)
            {
                if (reader.CurrentTokenKind == TokenKind.LessThan)
                {
                    reference = ParseGenericsPostfix(ref reader, reference);
                }
                else if (reader.TryConsume(TokenKind.Dot))
                {
                    var member = ParseName(ref reader);
                    reference = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(reference.Span.Start, member.Span.End), reference, member);
                    qualified = true;
                }
                else
                {
                    break;
                }
            }

            if (!qualified)
            {
                cause = reader.Expect(SyntaxForm.CasePattern, token); // A bare Name names no case.
            }
        }

        if (!reader.TryConsume(TokenKind.OpenParenthesis))
        {
            return cause is { } bare
                ? new ErrorKoto(ref reader, reference.Span) { Cause = bare }
                : new SyntaxFormKoto(ref reader, reference.Span, KotoKind.CasePattern, string.Empty, [reference]);
        }

        var patterns = default(TemporaryKotoList);
        while (reader.CanRead && reader.CurrentTokenKind != TokenKind.CloseParenthesis)
        {
            patterns.Add(ParsePattern(ref reader));
            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }
        }

        if (patterns.Count == 0)
        {
            cause ??= reader.Expect(SyntaxForm.Pattern);
        }

        reader.ExpectCloser(TokenKind.CloseParenthesis, out var end);
        var payload = new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, Math.Max(token.Span.End, end.End)), KotoKind.TuplePattern, "(", patterns.ToArray(), suffix: ")");
        return cause is { } malformed
            ? new ErrorKoto(ref reader, payload.Span) { Cause = malformed }
            : new SyntaxFormKoto(ref reader, payload.Span, KotoKind.CasePattern, string.Empty, [reference, payload], separator: string.Empty);
    }

    internal static Koto ParseEnumCase(ref TokenReader reader)
    {
        // A Case takes no attributes (SPEC 6.5).
        ReportPendingAttributes(ref reader);
        var name = ParseName(ref reader);
        var fields = default(TemporaryKotoList);
        var end = name.Span.End;
        var payload = reader.TryConsume(TokenKind.OpenParenthesis);
        if (payload)
        {
            do
            {
                fields.Add(ParseDeclarationType(ref reader));
            }
            while (reader.TryConsume(TokenKind.Comma) && reader.CurrentTokenKind != TokenKind.CloseParenthesis);
            reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);
            end = Math.Max(end, close.End);
        }

        var tuple = new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(name.Span.Start, end), KotoKind.EnumCase, payload ? "(" : string.Empty, fields.ToArray(), suffix: payload ? ")" : string.Empty);
        var declaration = new SyntaxFormKoto(ref reader, tuple.Span, KotoKind.EnumCase, string.Empty, [name, tuple], separator: string.Empty);
        reader.Document(declaration, tuple.Span);
        ParseAttachedOriginBlock(ref reader, declaration);
        return declaration;
    }

    private static Koto ParseRequire(ref TokenReader reader)
    {
        var start = reader.Read().Span.Start;
        var condition = ParseHeaderExpression(ref reader);
        reader.TrySkipSeparatorsTo(TokenKind.Else);
        reader.Expect(TokenKind.Else);
        var parsedBody = ParseRequiredBody(ref reader);
        Koto body = parsedBody.IsExpressionBody ? parsedBody.Items[0] : parsedBody;
        return new RequireKoto(ref reader, SourceSpan.FromBounds(start, body.Span.End), condition, body);
    }

    private static Koto ParseMemberName(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.NumericLiteral)
        {
            // The tokenizer ends a numeric literal after '.' at its digits, so the index is a digit sequence.
            return new NumberLiteralKoto(ref reader, reader.Read());
        }

        if (reader.CurrentTokenKind == TokenKind.Init)
        {
            var token = reader.Read();
            if (reader.CurrentTokenKind != TokenKind.OpenParenthesis)
            {
                // The reference is not a call; the member rests on the Error.
                return new ErrorKoto(ref reader, token.Span) { Cause = reader.Expect(SyntaxForm.OpenParenthesis) };
            }

            return new SyntaxFormKoto(ref reader, token.Span, KotoKind.ConstructorReference, "init", []);
        }

        return ParseName(ref reader);
    }
}
