// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Helper;
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
            TokenKind.Asterisk => new UnwrapKoto(ref reader, range, operand),
            TokenKind.Plus => new PrefixPlusKoto(ref reader, range, operand),
            TokenKind.Minus => new PrefixMinusKoto(ref reader, range, operand),
            TokenKind.Not => new NotKoto(ref reader, range, operand),
            TokenKind.Caret => new FromEndIndexKoto(ref reader, range, operand),
            TokenKind.PlusPlus => new PrefixPlusPlusKoto(ref reader, range, operand),
            TokenKind.MinusMinus => new PrefixMinusMinusKoto(ref reader, range, operand),
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
            TokenKind.Asterisk => new AsteriskKoto(ref reader, range, left, right),
            TokenKind.At => new ConversionKoto(ref reader, range, left, right),
            TokenKind.Slash => new SlashKoto(ref reader, range, left, right),
            TokenKind.Percent => new PercentKoto(ref reader, range, left, right),
            TokenKind.Plus => new PlusKoto(ref reader, range, left, right),
            TokenKind.Minus => new MinusKoto(ref reader, range, left, right),
            TokenKind.LessThanLessThan => new LessThanLessThanKoto(ref reader, range, left, right),
            TokenKind.GreaterThanGreaterThan => new GreaterThanGreaterThanKoto(ref reader, range, left, right),
            TokenKind.LessThan => new LessThanKoto(ref reader, range, left, right),
            TokenKind.LessThanEquals => new LessThanEqualsKoto(ref reader, range, left, right),
            TokenKind.GreaterThan => new GreaterThanKoto(ref reader, range, left, right),
            TokenKind.GreaterThanEquals => new GreaterThanEqualsKoto(ref reader, range, left, right),
            TokenKind.As => new AsKoto(ref reader, range, left, right),
            TokenKind.Is => new IsKoto(ref reader, range, left, right),
            TokenKind.EqualsEquals => new EqualsEqualsKoto(ref reader, range, left, right),
            TokenKind.ExclamationEquals => new ExclamationEqualsKoto(ref reader, range, left, right),
            TokenKind.Ampersand => new AmpersandKoto(ref reader, range, left, right),
            TokenKind.Caret => new CaretKoto(ref reader, range, left, right),
            TokenKind.Bar => new BarKoto(ref reader, range, left, right),
            TokenKind.And => new AndKoto(ref reader, range, left, right),
            TokenKind.Or => new OrKoto(ref reader, range, left, right),
            TokenKind.Equals => new EqualsKoto(ref reader, range, left, right),
            TokenKind.PlusEquals => new PlusEqualsKoto(ref reader, range, left, right),
            TokenKind.MinusEquals => new MinusEqualsKoto(ref reader, range, left, right),
            TokenKind.AsteriskEquals => new AsteriskEqualsKoto(ref reader, range, left, right),
            TokenKind.SlashEquals => new SlashEqualsKoto(ref reader, range, left, right),
            TokenKind.PercentEquals => new PercentEqualsKoto(ref reader, range, left, right),
            TokenKind.AmpersandEquals => new AmpersandEqualsKoto(ref reader, range, left, right),
            TokenKind.CaretEquals => new CaretEqualsKoto(ref reader, range, left, right),
            TokenKind.BarEquals => new BarEqualsKoto(ref reader, range, left, right),
            TokenKind.LessThanLessThanEquals => new LessThanLessThanEqualsKoto(ref reader, range, left, right),
            TokenKind.GreaterThanGreaterThanEquals => new GreaterThanGreaterThanEqualsKoto(ref reader, range, left, right),

            _ => throw new InvalidOperationException(),
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
            // Preserve the source metadata associated with the replaced expression.
            newKoto.Span = oldKoto.Span;
            newKoto.CodeContext = oldKoto.CodeContext;
            return true;
        }

        return false;
    }

    /// <summary>Parses and validates a dot-separated namespace name.</summary>
    /// <remarks>
    /// When the name is written without interior whitespace, the returned span aliases the
    /// source text and no string is allocated.
    /// </remarks>
    /// <param name="reader">The token reader.</param>
    /// <returns>The validated namespace name.</returns>
    public static ReadOnlySpan<char> ValidateAndGetNamespace(ref TokenReader reader)
    {
        if (reader.IsEnd)
        {
            return default;
        }

        // Qualified names alternate between identifiers and dots.
        var expectsIdentifier = true;
        var isContiguous = true;
        Token first = default;
        Token last = default;
        StringBuilder? fallback = default;
        while (reader.CanRead && reader.CurrentTokenKind != TokenKind.Separator)
        {
            var token = reader.Read();
            if (expectsIdentifier)
            {
                var span = reader.GetSpan(token);
                if (!IdentifierHelper.IsValidIdentifier(span))
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
                    break;
                }

                fallback?.Append(span);
            }
            else if (token.Kind == TokenKind.Dot)
            {
                fallback?.Append(Constants.DotChar);
            }
            else
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, token.Kind);
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
            reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.IdentifierExpected_Kd);
        }

        if (reader.CurrentTokenKind == TokenKind.Separator)
        {
            reader.Advance();
        }

        if (fallback is not null)
        {
            return fallback.ToString();
        }

        return first.Kind == TokenKind.Invalid
            ? default
            : reader.GetSpan(new(TokenKind.Identifier, SourceSpan.FromBounds(first.Start, last.Span.End)));
    }

    /// <summary>Parses a dot-separated qualified name.</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The parsed name segments.</returns>
    public static List<string> ParseQualifiedNameSegments(ref TokenReader reader)
    {
        var list = new List<string>();
        if (reader.IsEnd)
        {
            return list;
        }

        // Qualified names alternate between identifiers and dots.
        var expectsIdentifier = true;
        while (reader.CanRead)
        {
            var token = reader.Read();
            if (token.Kind == TokenKind.Separator)
            {
                break;
            }

            if (expectsIdentifier)
            {
                var span = reader.GetSpan(token);
                if (!IdentifierHelper.IsValidIdentifier(span))
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
                    break;
                }

                list.Add(reader.GetIdentifier(token));
            }
            else if (token.Kind != TokenKind.Dot)
            {
                reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, token);
                break;
            }

            expectsIdentifier = !expectsIdentifier;
        }

        if (expectsIdentifier)
        {
            reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.IdentifierExpected_Kd);
        }

        return list;
    }
}
