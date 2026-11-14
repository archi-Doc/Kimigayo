// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// Primary expressions (SPEC 12.4, Appendix F.9.1): the dispatch by first token over Names, literals and the constructs of Parser.ControlFlow,
// interpolated strings, parenthesized expressions, Units and Tuples, and Array and Dictionary literals.
// Input: a reader at the operand. Output: the operand's node; an ErrorKoto where none could be read.
// Invariants: a string's escapes are validated while parsing, whether or not its value is read; an item before text that a list's recovery
// skipped is a recovery (Parser.Recovery).
// Prohibitions: a literal's Type belongs to Binding.
public static partial class Parser
{
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
                return ParseRootName(ref reader);

            case TokenKind.Dot:
                return ParseInferredCase(ref reader);

            case TokenKind.Base:
                return new BaseReferenceKoto(ref reader, reader.Read());

            case TokenKind.Self when reader.PeekKind(1) == TokenKind.Dot:
                // A Self-qualified member keeps the enclosing complete Type, including its bindings.
                return new TypeSemanticsKoto(ref reader, reader.Read());

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
                    var function = ParseFuncDeclaration(ref reader, FunctionForm.Expression);
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
                return IsBoundContainerQualifier(ref reader, out var borrowOrigin) && !borrowOrigin
                    ? ParseType(ref reader, TypeContext.Qualifier)
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

    // ArrayExpression := "[" TrailingList<Expression>? "]" | "[" ArrayLength "of" Expression "]" and DictionaryExpression := "[" ":" "]" |
    // "[" TrailingList<DictionaryEntry> "]" (F.9.1). The empty forms and the fill form are read here; after the first element a ":"
    // selects the Dictionary's list, anything else the Array's.
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
            CheckArrayLengthSyntax(ref reader, first);

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
    }

    // ArrayExpression's TrailingList<Expression> after its first element, to the "]" (F.9.1).
    private static ArrayLiteralKoto ParseArrayLiteral(ref TokenReader reader, SourceSpan openRange, Koto first)
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

    // DictionaryExpression's TrailingList<DictionaryEntry> from its first key's ":", to the "]"; DictionaryEntry := Expression ":" Expression
    // (F.9.1). An entry without its ":" is reported and skipped.
    private static DictionaryLiteralKoto ParseDictionaryLiteral(ref TokenReader reader, SourceSpan openRange, Koto firstKey)
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

    // The Expression of an Array element or of a DictionaryEntry's key or value (F.9.1); an Error where a "," or "]" stands in its place
    // or the input ends.
    private static Koto ParseLiteralElement(ref TokenReader reader)
    {
        if (!reader.CanRead || reader.CurrentTokenKind is TokenKind.Comma or TokenKind.CloseBracket)
        {
            reader.Expect(SyntaxForm.Expression);
            return reader.NewErrorKoto();
        }

        return ParseExpression(ref reader);
    }
}
