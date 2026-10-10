// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Creates and manipulates Koto syntax-tree nodes.
/// </summary>
public static partial class KotoHelper
{
    /// <summary>Creates a unary node for an operator token.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The operator token.</param>
    /// <param name="operand">The operand.</param>
    /// <returns>The created unary node.</returns>
    public static Koto NewUnaryKoto(ref TokenReader reader, Token token, Koto operand)
    {
        var range = SourceSpan.FromBounds(token.Span.Start, Math.Max(token.Span.End, operand.Span.End));
        return token.Kind switch
        {
            TokenKind.Sharp => new AttributeKoto(ref reader, range, operand),
            TokenKind.Dollar => new MacroKoto(ref reader, range, operand),
            TokenKind.Asterisk => new DereferenceKoto(ref reader, range, operand),
            TokenKind.Plus => new UnaryKoto(ref reader, range, KotoKind.PrefixPlus, operand),
            TokenKind.Minus => new UnaryKoto(ref reader, range, KotoKind.PrefixMinus, operand),
            TokenKind.Not => new UnaryKoto(ref reader, range, KotoKind.Not, operand),
            TokenKind.Caret => new FromEndIndexKoto(ref reader, range, operand),
            TokenKind.PlusPlus => new UnaryKoto(ref reader, range, KotoKind.PrefixPlusPlus, operand),
            TokenKind.MinusMinus => new UnaryKoto(ref reader, range, KotoKind.PrefixMinusMinus, operand),
            _ => throw new InvalidOperationException(),
        };
    }

    /// <summary>Creates a binary node for an operator token.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The operator token.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The created binary node.</returns>
    public static Koto NewBinaryKoto(ref TokenReader reader, Token token, Koto left, Koto right)
    {
        var range = SourceSpan.FromBounds(
            left.Span.Start,
            Math.Max(Math.Max(left.Span.End, token.Span.End), right.Span.End));
        return token.Kind switch
        {
            TokenKind.At => new ConversionKoto(ref reader, range, left, right),
            TokenKind.Is => new IsKoto(ref reader, range, left, right),
            _ => new BinaryKoto(ref reader, range, BinaryOperator(token.Kind), left, right),
        };
    }

    /// <summary>Replaces a child node while preserving its source metadata.</summary>
    /// <param name="parent">The parent node.</param>
    /// <param name="oldKoto">The child to replace.</param>
    /// <param name="newKoto">The replacement child.</param>
    /// <returns><see langword="true"/> when the child was replaced.</returns>
    public static bool Replace(Koto parent, Koto oldKoto, Koto newKoto)
    {
        if (parent.ReplaceChild(oldKoto, newKoto))
        {
            if (newKoto is NumberLiteralKoto number &&
                (newKoto.Span != oldKoto.Span || !ReferenceEquals(newKoto.CodeContext, oldKoto.CodeContext)))
            {
                number.PreserveSourceSpelling();
            }

            // Preserve the source metadata associated with the replaced expression.
            newKoto.Span = oldKoto.Span;
            newKoto.CodeContext = oldKoto.CodeContext;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the qualified Name of a root group: Names separated by dots, whitespace allowed between the parts. A reserved word or
    /// any other token where a Name belongs is where the Name was expected (SPEC 2.5.1, DIAGNOSTICS.md §4.4); the caller then
    /// skips the declaration with its body.
    /// </summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The qualified Name, or an empty span after the Error; the separator after the Name is consumed.</returns>
    public static ReadOnlySpan<char> ValidateAndGetNamespace(ref TokenReader reader)
    {
        // Qualified names alternate between Names and dots.
        var expectsIdentifier = true;
        var isContiguous = true;
        Token first = default;
        Token last = default;
        StringBuilder? fallback = default;
        // A line ends at a separator or at the layout token of a dedent or body, which the caller handles.
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock or TokenKind.StartBlock))
        {
            var token = reader.CurrentToken;
            if (expectsIdentifier)
            {
                if (!token.Kind.IsIdentifierOrContextualKeyword())
                {
                    reader.Expect(SyntaxForm.Name);
                    return default;
                }

                reader.Advance();
                if (!reader.TryGetIdentifier(token, out _))
                {
                    return default; // The invalid identifier is reported once.
                }

                fallback?.Append(reader.GetSpan(token));
            }
            else if (token.Kind == TokenKind.Dot)
            {
                reader.Advance();
                fallback?.Append(Constants.DotChar);
            }
            else
            {
                reader.Expect(SyntaxForm.LineEnd);
                reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                break;
            }

            if (first.Kind == TokenKind.Invalid)
            {
                first = token;
            }
            else if (isContiguous && token.Start != last.Span.End)
            {
                // Whitespace inside the name: materialize the text collected so far.
                isContiguous = false;
                fallback = new StringBuilder();
                fallback.Append(reader.GetSpan(new(TokenKind.Identifier, SourceSpan.FromBounds(first.Start, last.Span.End))));
                fallback.Append(reader.GetSpan(token));
            }

            last = token;
            expectsIdentifier = !expectsIdentifier;
        }

        if (expectsIdentifier)
        {
            reader.Expect(SyntaxForm.Name);
            return default;
        }

        if (reader.CurrentTokenKind == TokenKind.Separator)
        {
            reader.Advance();
        }

        return fallback is not null
            ? fallback.ToString()
            : reader.GetSpan(new(TokenKind.Identifier, SourceSpan.FromBounds(first.Start, last.Span.End)));
    }

    /// <summary>
    /// Parses the dot-separated qualified Name of an alias target. A reserved word or any other token where a Name belongs is
    /// where the Name was expected (SPEC 2.5.1, DIAGNOSTICS.md §4.4); the caller then skips the declaration.
    /// </summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed name segments, or <see langword="null"/> after the Error; the separator after the Name is consumed.</returns>
    public static List<string>? ParseQualifiedNameSegments(ref TokenReader reader)
    {
        var list = new List<string>(4);
        while (true)
        {
            var token = reader.CurrentToken;
            if (!token.Kind.IsIdentifierOrContextualKeyword())
            {
                reader.Expect(SyntaxForm.Name);
                return null;
            }

            reader.Advance();
            if (!reader.TryGetIdentifier(token, out var segment))
            {
                return null; // The invalid identifier is reported once.
            }

            list.Add(segment);
            if (!reader.TryConsume(TokenKind.Dot))
            {
                break;
            }
        }

        // The line ends after the Name: a separator is consumed, a dedent or a body is left for the caller.
        if (reader.CurrentTokenKind == TokenKind.Separator)
        {
            reader.Advance();
        }
        else if (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.EndBlock or TokenKind.StartBlock))
        {
            reader.Expect(SyntaxForm.LineEnd);
            reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
        }

        return list;
    }
}
