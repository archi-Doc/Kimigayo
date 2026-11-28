// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public static partial class Parser
{
    // Retain lists even on non-Callable clauses: Binding owns the single InvalidEffectBound diagnostic.
    internal static void ParseCallableEffects(ref TokenReader reader, IsKoto constraint)
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

            if (IsEffectStart(ref reader, specification: true))
            {
                var effect = ParseEffectBound(ref reader);
                constraint.AddEffectBound(effect);
                constraint.Span = SourceSpan.FromBounds(constraint.Span.Start, effect.Span.End);
            }
            else
            {
                reader.Expect(SyntaxForm.EffectBound);
                if (reader.CurrentTokenKind == TokenKind.StartBlock)
                {
                    reader.SkipCurrentBlock();
                }
                else
                {
                    reader.Advance();
                    reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
                }
            }
        }
    }

    /// <summary>Determines whether the reader stands at an effect item (SPEC 8.11.1).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="specification">Whether a Contract selector may follow <c>effect</c>, as among a container's items. A body item
    /// takes a Name only, so <c>effect(x)</c> stays a call there.</param>
    /// <returns><see langword="true"/> when <c>effect</c> starts an effect item rather than an expression or a Constraint.</returns>
    internal static bool IsEffectStart(ref TokenReader reader, bool specification)
    {
        if (!reader.IsCurrentIdentifier(Constants.EffectKeyword))
        {
            return false;
        }

        var next = reader.PeekKind(1);
        return next.IsIdentifierOrContextualKeyword() || (specification && next is TokenKind.OpenParenthesis or TokenKind.ColonColon or TokenKind.OpenBracket);
    }

    /// <summary>Parses an effect clause or an effect specification; the reader stands at <c>effect</c> (SPEC 8.11.1). The item
    /// ends with its line in an indented list; in a single-item body it ends where the enclosing construct's own syntax, such as
    /// a closer, <c>,</c> or <c>else</c>, continues the line (SPEC 14.2.1, F.4.4).</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The item. A missing or unknown bound reports the Error and leaves the item as its recovery.</returns>
    internal static EffectBoundKoto ParseEffectBound(ref TokenReader reader)
    {
        var single = reader.SingleBodyRegion;
        var start = reader.Read().Span.Start;
        Koto? selector = null;
        Koto? name = null;

        // A path continued by '.' or '<', or a parenthesized, root-qualified or fixed-array selector, names a requirement; a lone word
        // is the bound, so a Contract named like a bound word is still a selector.
        DiagnosticKey? cause = null;
        if (reader.CurrentTokenKind is TokenKind.OpenParenthesis or TokenKind.ColonColon or TokenKind.OpenBracket || reader.PeekKind(1) is TokenKind.Dot or TokenKind.LessThan)
        {
            selector = ParseEffectTarget(ref reader, out name);
            cause = (name as ErrorKoto)?.Cause;
        }

        if (reader.IsCurrentIdentifier(Constants.ConfinedKeyword))
        {
            reader.Advance();
        }
        else
        {
            // The vocabulary is closed; the rest of the item belongs to the rejected bound, with every grouping it opens, closer
            // included. Only a boundary at the item's own depth, a closer it did not open among them, ends a single item.
            var missing = reader.Expect(SyntaxForm.EffectBound);
            cause ??= missing;
            for (var depth = 0; reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock); reader.Advance())
            {
                if (reader.CurrentTokenKind is TokenKind.OpenParenthesis or TokenKind.OpenBracket or TokenKind.OpenBrace)
                {
                    depth++;
                }
                else if (depth > 0 && reader.CurrentTokenKind is TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace)
                {
                    depth--;
                }
                else if (single && depth == 0 && IsExpressionBoundary(ref reader))
                {
                    break;
                }
            }
        }

        var effect = new EffectBoundKoto(ref reader, SourceSpan.FromBounds(start, reader.PreviousSyntaxEnd), selector, name);
        if (cause is { } recovery)
        {
            reader.CodeContext.RecordRecovery(effect, recovery);
        }

        if (!single)
        {
            reader.ExpectLineEnd();
        }

        return effect;
    }

    /// <summary>Parses an effect item that starts an item of an executable body (SPEC 8.11.1, 7.5, F.3), never read as an
    /// expression: in a function's leading region it is kept for Binding to judge; anywhere else, including the top level, it is
    /// misplaced and, as a misplaced <c>origin</c> clause, introduces nothing.</summary>
    /// <param name="reader">The token reader, at <c>effect</c>.</param>
    /// <param name="leading">The function whose leading region holds the item, or <see langword="null"/>.</param>
    /// <returns>The key of the misplaced item's Error, or <see langword="null"/> when the item is kept or reports nowhere.</returns>
    internal static DiagnosticKey? ParseBodyEffect(ref TokenReader reader, FunctionKoto? leading)
    {
        ReportPendingAttributes(ref reader);
        RejectAccessWord(ref reader);
        var effect = ParseEffectBound(ref reader);
        if (leading is null)
        {
            return effect.Unexpected(SyntaxForm.EffectItem);
        }

        if (reader.ExcludingMarker is { } marker)
        {
            marker.AddSource(effect); // Excluded syntax keeps the item in its directive's marker (SPEC 19.4).
        }
        else
        {
            leading.AddEffectBound(effect);
        }

        return null;
    }

    // ContractSelector "." Name (SPEC F.6.2), read by the one ContractSelector reader (ParseContractSelectorPath, shared with an
    // AssociatedTypeName) and judged once complete (RecoverContractSelector). The last segment of the path is the requirement Name.
    // Returns the ContractSelector; name is the requirement Name, an Error where the path names none.
    private static Koto ParseEffectTarget(ref TokenReader reader, out Koto name)
    {
        var path = ParseContractSelectorPath(ref reader);
        if (IsRecoveredType(ref reader, path))
        {
            // A path that failed (its record is reported) names no requirement; the item rests on that record.
            name = new ErrorKoto(ref reader, reader.InsertionSpan) { Cause = reader.CodeContext.RecoveryCause(path) ?? (path as ErrorKoto)?.Cause };
            return path;
        }

        if (path is MemberAccessKoto member)
        {
            RecoverContractSelector(ref reader, member.Left);
            name = member.Right;
            return member.Left;
        }

        // The selector names no requirement; the Name is missing where the bound would follow.
        reader.Expect(SyntaxForm.Name);
        name = reader.NewErrorKoto();
        return path;
    }
}
