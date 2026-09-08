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
            var member = ParseName(ref reader);
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
                reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
                break;
            }

            reader.Advance();
            string? operation = null;
            var end = token.Span.End;
            if (reader.TryConsume(TokenKind.At))
            {
                var op = reader.Read();
                operation = reader.GetSpan(op) switch
                {
                    "move" => "move",
                    "ref" when !mutable => "ref",
                    "uniq" when !mutable => "uniq",
                    _ => null,
                };
                if (operation is null)
                {
                    reader.Diagnostic.Add(op.Span, DiagnosticCode.UnexpectedToken_Kd, "capture operation");
                }

                end = op.Span.End;
            }

            captures.Add(new CaptureKoto(name, mutable, operation, SourceSpan.FromBounds(start, end)));
            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }
        }

        reader.TryConsume(TokenKind.CloseBracket, out _, true);
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

    internal static void ParseRequirementBody(ref TokenReader reader, FunctionKoto function)
    {
        function.IsRequirement = true;
        if (function.Modifier is not (ModifierKind.NoModifier or ModifierKind.Unsafe) || function.AttributeChain is not null)
        {
            function.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "requirement modifiers");
        }

        foreach (var parameter in function.Parameters)
        {
            if (parameter.IsOptional || parameter.DefaultValue is not null || parameter.AttributeChain is not null)
            {
                parameter.Type.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "requirement parameter");
            }
        }

        if (reader.CurrentTokenKind == TokenKind.EqualsGreaterThan)
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "requirement body");
            reader.Advance();
            _ = ParseRequiredExpression(ref reader);
        }

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

            if (!IsTypeConstraintStart(ref reader))
            {
                reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "constraint");
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
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

        reader.AddDiagnostic(DiagnosticCode.IdentifierExpected_Kd);
        if (reader.CanRead && !IsExpressionBoundary(ref reader))
        {
            reader.Advance();
        }

        return new ErrorKoto(ref reader, token.Span);
    }

    private static Koto ParseRootName(ref TokenReader reader, bool type)
    {
        var start = reader.Read().Span.Start;
        var name = type ? ParseType(ref reader, false) : ParseName(ref reader);
        return new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(start, name.Span.End), KotoKind.RootName, "::", [name]);
    }

    private static Koto ParseFixedArrayType(ref TokenReader reader, bool allowOrigins)
    {
        var start = reader.Read().Span.Start;
        var length = ParseArrayLength(ref reader);
        if (reader.IsCurrentIdentifier("of"))
        {
            reader.Advance();
        }
        else
        {
            reader.AddDiagnostic(DiagnosticCode.UnexpectedToken_Kd, "of");
        }

        Koto element;
        if (reader.AllowArrayElementInference && reader.IsCurrentIdentifier("_"))
        {
            element = new TypeSemanticsKoto(ref reader, reader.Read());
            reader.HasInferredArrayElement = true;
        }
        else
        {
            element = ParseDeclarationType(ref reader, parseOrigin: allowOrigins, allowNestedOrigins: allowOrigins);
        }

        var end = element.Span.End;
        if (reader.TryConsume(TokenKind.CloseBracket, out var close, true))
        {
            end = close.End;
        }

        return new FixedArrayTypeKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, end)), length, element);
    }

    private static Koto ParseArrayLength(ref TokenReader reader, int precedence = 0, bool expression = false)
    {
        var token = reader.CurrentToken;
        Koto left;
        if (reader.TryConsume(TokenKind.OpenParenthesis))
        {
            var value = ParseArrayLength(ref reader, expression: true);
            reader.TryConsume(TokenKind.CloseParenthesis, out var close, true);
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
            }

            left = number;
        }
        else
        {
            left = reader.CurrentTokenKind == TokenKind.ColonColon ? ParseRootName(ref reader, false) : ParseName(ref reader);
            while (reader.TryConsume(TokenKind.Dot))
            {
                var right = ParseName(ref reader);
                left = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(left.Span.Start, right.Span.End), left, right);
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

    private static Koto ParseTypeArgument(ref TokenReader reader, bool allowOrigins)
    {
        if (reader.CurrentTokenKind == TokenKind.NumericLiteral || IsLengthExpression(ref reader))
        {
            return ParseArrayLength(ref reader);
        }

        return ParseDeclarationType(ref reader, parseOrigin: allowOrigins, allowNestedOrigins: allowOrigins);
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
            else if (kind is TokenKind.NumericLiteral or TokenKind.Plus or TokenKind.Minus or TokenKind.Asterisk or TokenKind.Percent)
            {
                return true;
            }
            else if (kind is TokenKind.Comma or TokenKind.MinusGreaterThan or TokenKind.EndBlock or TokenKind.Invalid)
            {
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
            return new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, name.Span.End), KotoKind.BindingPattern, token.Kind == TokenKind.Let ? "let " : "var ", [name]);
        }

        if (reader.TryConsume(TokenKind.OpenParenthesis))
        {
            var items = default(TemporaryList<Koto>);
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

            reader.TryConsume(TokenKind.CloseParenthesis, out var close, true);
            var children = items.ToArray();
            var span = SourceSpan.FromBounds(token.Span.Start, Math.Max(token.Span.End, close.End));
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
                reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, "Pattern literal");
            }

            return negative ? KotoHelper.NewUnaryKoto(ref reader, token, literal) : literal;
        }

        if (reader.IsCurrentIdentifier("_"))
        {
            return ParseName(ref reader);
        }

        Koto reference;
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
                reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, "Case Pattern");
            }
        }

        if (!reader.TryConsume(TokenKind.OpenParenthesis))
        {
            return new SyntaxFormKoto(ref reader, reference.Span, KotoKind.CasePattern, string.Empty, [reference]);
        }

        var patterns = default(TemporaryList<Koto>);
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
            reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
        }

        reader.TryConsume(TokenKind.CloseParenthesis, out var end, true);
        var payload = new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, Math.Max(token.Span.End, end.End)), KotoKind.TuplePattern, "(", patterns.ToArray(), suffix: ")");
        return new SyntaxFormKoto(ref reader, payload.Span, KotoKind.CasePattern, string.Empty, [reference, payload], separator: string.Empty);
    }

    internal static Koto ParseEnumCase(ref TokenReader reader)
    {
        var name = ParseName(ref reader);
        var fields = default(TemporaryList<Koto>);
        var end = name.Span.End;
        var payload = reader.TryConsume(TokenKind.OpenParenthesis);
        if (payload)
        {
            do
            {
                fields.Add(ParseDeclarationType(ref reader));
            }
            while (reader.TryConsume(TokenKind.Comma) && reader.CurrentTokenKind != TokenKind.CloseParenthesis);
            if (reader.TryConsume(TokenKind.CloseParenthesis, out var close, true))
            {
                end = close.End;
            }
        }

        var tuple = new SyntaxFormKoto(ref reader, SourceSpan.FromBounds(name.Span.Start, end), KotoKind.EnumCase, payload ? "(" : string.Empty, fields.ToArray(), suffix: payload ? ")" : string.Empty);
        return new SyntaxFormKoto(ref reader, tuple.Span, KotoKind.EnumCase, string.Empty, [name, tuple], separator: string.Empty);
    }

    private static Koto ParseRequire(ref TokenReader reader)
    {
        var start = reader.Read().Span.Start;
        var condition = ParseRequiredExpression(ref reader);
        reader.TrySkipSeparatorsTo(TokenKind.Else);
        reader.TryConsume(TokenKind.Else, out _, true);
        Koto body;
        if (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
        {
            var inline = reader.CreateInlineReader(out var count);
            body = ParseBlockItem(ref inline) ?? inline.NewErrorKoto();
            if (inline.CanRead)
            {
                inline.AddDiagnostic(DiagnosticCode.InvalidInlineStatement_Kd);
            }

            reader.Advance(count);
        }
        else
        {
            body = ParseRequiredBlock(ref reader);
        }

        return new RequireKoto(ref reader, SourceSpan.FromBounds(start, body.Span.End), condition, body);
    }

    private static Koto ParseMemberName(ref TokenReader reader)
    {
        if (reader.CurrentTokenKind == TokenKind.NumericLiteral)
        {
            var token = reader.Read();
            var text = reader.GetSpan(token);
            foreach (var c in text)
            {
                if (c is < '0' or > '9')
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, "Tuple index");
                    break;
                }
            }

            return new NumberLiteralKoto(ref reader, token);
        }

        if (reader.CurrentTokenKind == TokenKind.Init)
        {
            var token = reader.Read();
            if (reader.CurrentTokenKind != TokenKind.OpenParenthesis)
            {
                reader.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
            }

            return new SyntaxFormKoto(ref reader, token.Span, KotoKind.ConstructorReference, "init", []);
        }

        return ParseName(ref reader);
    }
}
