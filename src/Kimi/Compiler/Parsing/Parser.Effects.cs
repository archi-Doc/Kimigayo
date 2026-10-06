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

    /// <summary>Determines whether the reader stands at an effect item (SPEC 8.4.10.1).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="specification">Whether a Contract selector may follow <c>effect</c>, as among a container's items. A body's
    /// Constraint prefix takes a Name only, so <c>effect(x)</c> stays a call there.</param>
    /// <returns><see langword="true"/> when <c>effect</c> starts an effect item rather than an expression or a Constraint.</returns>
    internal static bool IsEffectStart(ref TokenReader reader, bool specification)
    {
        if (!reader.IsCurrentIdentifier(Constants.EffectKeyword))
        {
            return false;
        }

        var next = reader.PeekKind(1);
        return next.IsIdentifierOrContextualKeyword() || (specification && next is TokenKind.OpenParenthesis or TokenKind.ColonColon);
    }

    /// <summary>Parses an effect clause or an effect specification; the reader stands at <c>effect</c> (SPEC 8.4.10.1).</summary>
    /// <param name="reader">The token reader.</param>
    /// <returns>The item. A missing or unknown bound reports the Error and leaves the item as its recovery.</returns>
    internal static EffectBoundKoto ParseEffectBound(ref TokenReader reader)
    {
        var start = reader.Read().Span.Start;
        Koto? selector = null;
        Koto? name = null;

        // A path continued by '.' or '<', or a parenthesized or root-qualified selector, names a requirement; a lone word
        // is the bound, so a Contract named like a bound word is still a selector.
        DiagnosticKey? cause = null;
        if (reader.CurrentTokenKind is TokenKind.OpenParenthesis or TokenKind.ColonColon || reader.PeekKind(1) is TokenKind.Dot or TokenKind.LessThan)
        {
            ParseEffectTarget(ref reader, out selector, out name);
            cause = (name as ErrorKoto)?.Cause;
        }

        var bound = EffectBoundKind.Confined;
        if (reader.IsCurrentIdentifier(Constants.ConfinedKeyword))
        {
            reader.Advance();
        }
        else if (reader.IsCurrentIdentifier(Constants.PreservesKeyword) && reader.IsIdentifierToken(reader.PeekToken(1), Constants.ResultsKeyword))
        {
            reader.Advance(2);
            bound = EffectBoundKind.PreservesResults;
        }
        else
        {
            // The vocabulary is closed; the rest of the line belongs to the rejected bound.
            var missing = reader.Expect(SyntaxForm.EffectBound);
            cause ??= missing;
            reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
        }

        var effect = new EffectBoundKoto(ref reader, SourceSpan.FromBounds(start, reader.PreviousSyntaxEnd), bound, selector, name);
        if (cause is { } recovery)
        {
            reader.CodeContext.RecordRecovery(effect, recovery);
        }

        reader.ExpectLineEnd();
        return effect;
    }

    // ContractSelector "." Name, where ContractSelector is a ContainerPath or "(" ContractReference ")" (Appendix F). The last
    // segment of the path is the requirement Name.
    private static void ParseEffectTarget(ref TokenReader reader, out Koto selector, out Koto name)
    {
        Koto path;
        if (reader.CurrentTokenKind == TokenKind.OpenParenthesis)
        {
            var open = reader.Read().Span.Start;
            var reference = ParseType(ref reader);
            reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);
            path = new ParenthesizedKoto(ref reader, SourceSpan.FromBounds(open, Math.Max(reference.Span.End, close.End)), reference);
        }
        else
        {
            path = reader.CurrentTokenKind == TokenKind.ColonColon ? ParseRootName(ref reader, false) : ParseName(ref reader);
        }

        while (true)
        {
            if (reader.CurrentTokenKind == TokenKind.LessThan && path is not ParenthesizedKoto)
            {
                path = ParseGenericsPostfix(ref reader, path);
            }
            else if (reader.TryConsume(TokenKind.Dot))
            {
                var segment = ParseName(ref reader);
                path = new MemberAccessKoto(ref reader, SourceSpan.FromBounds(path.Span.Start, segment.Span.End), path, segment);
            }
            else
            {
                break;
            }
        }

        if (path is MemberAccessKoto member)
        {
            selector = member.Left;
            name = member.Right;
            return;
        }

        // The selector names no requirement; the Name is missing where the bound would follow.
        selector = path;
        reader.Expect(SyntaxForm.Name);
        name = reader.NewErrorKoto();
    }
}
