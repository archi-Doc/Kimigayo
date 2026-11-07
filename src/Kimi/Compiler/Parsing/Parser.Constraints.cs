// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

// Constraint clauses (SPEC 8.2, Appendix F.6): `subject is requirement` in a `when` list, a function's Constraint prefix and an associated
// Type's specification, with the lookahead that tells a Constraint from an expression statement (SPEC 7.5).
// Input: a reader at the subject, or at an item whose start the lookahead classifies. Output: an IsKoto; the lookahead consumes nothing.
// Invariants: a Constraint a part of which failed stays a recovery, so Binding judges none of its parts (DIAGNOSTICS.md §4.3).
// Prohibitions: what a subject or a requirement means belongs to Binding; a requirement's Type parts to Parser.Types and Parser.Syntax.
public static partial class Parser
{
    /// <summary>
    /// Parses a type constraint in the form <c>subject is condition</c>.
    /// </summary>
    /// <remarks>
    /// Requirement operands are retained as <see cref="IdentifierNameKoto"/> and Type
    /// instances for later semantic analysis.
    /// </remarks>
    /// <param name="reader">The token reader positioned at the constraint subject.</param>
    /// <param name="finishLine">Whether to diagnose and consume trailing tokens on the clause's line.</param>
    /// <returns>The parsed constraint, retained as a recovery when any required part is invalid.</returns>
    public static IsKoto ParseTypeConstraint(ref TokenReader reader, bool finishLine = true)
    {
        var lastError = reader.Diagnostic.LastError;
        var simple = HasSimpleConstraintSubject(ref reader);
        var subject = simple ? ParseConstraintSubject(ref reader) : ParseDeclarationType(ref reader);
        if (!simple && !IsConstraintSubject(subject) && !ReportedSince(ref reader, lastError, out _) && subject.Expected(SyntaxForm.Constraint) is { } rejected)
        {
            // The owner's O-42 (a): a constructed Type is no subject. The Constraint rests on this Error, and its requirement stays
            // independent (BindConstraint); a failed Type has its own Error.
            reader.CodeContext.RecordRecovery(subject, rejected);
        }

        return FinishConstraint(ref reader, subject, lastError, finishLine);
    }

    /// <summary>Parses an associated Type's specification after its <c>associate</c>: <c>AssociatedTypeName OriginParameters? is requirement</c>.</summary>
    /// <param name="reader">The token reader positioned at the Name.</param>
    /// <returns>The parsed specification, retained as a recovery when any required part is invalid.</returns>
    internal static IsKoto ParseAssociatedConstraint(ref TokenReader reader)
    {
        var lastError = reader.Diagnostic.LastError;
        var name = HasSimpleConstraintSubject(ref reader) ? ParseConstraintSubject(ref reader) : ParseDeclarationType(ref reader);
        return FinishConstraint(ref reader, name, lastError, finishLine: false);
    }

    // SPEC F.6's ConstraintSubject, over what HasSimpleConstraintSubject leaves to the Type path: a Type with no Type arguments (a primitive
    // Type, or a Name no `is` follows) or an AssociatedTypeReference, rooted or not, whose qualifier may be a constructed Type, ending in a
    // Name with its Origins.
    private static bool IsConstraintSubject(Koto type)
    {
        var rooted = false;
        while (true)
        {
            if (type is TypeSemanticsKoto { IsTransparentWrapper: true, HasOrigin: false, Type: { } inner })
            {
                type = inner;
            }
            else if (type is SyntaxFormKoto { Akind: KotoKind.RootName } root)
            {
                rooted = true;
                type = root.Operands[0];
            }
            else
            {
                break;
            }
        }

        return type is TypeSemanticsKoto { Type: null, HasOrigin: false } name ? !rooted || name.IsNamedType :
            (type is OriginApplicationKoto application ? application.Type : type) is MemberAccessKoto { Right: TypeSemanticsKoto { Type: null } };
    }

    // The keys are compared as values; comparing with the nullable key would box it for every Constraint after an Error.
    private static bool ReportedSince(ref TokenReader reader, DiagnosticKey? before, out DiagnosticKey cause)
    {
        var last = reader.Diagnostic.LastError;
        cause = last.GetValueOrDefault();
        return last.HasValue && (before is not { } previous || !cause.Equals(previous));
    }

    private static IsKoto FinishConstraint(ref TokenReader reader, Koto subject, DiagnosticKey? lastError, bool finishLine)
    {
        if (!reader.TryConsume(TokenKind.Is, out var isRange, false))
        {
            // A `when` list calls directly, without prefix lookahead. Keep the failed premise rather than weakening the list.
            reader.Expect(SyntaxForm.IsKeyword);
        }

        var previousRequirement = reader.ConstraintRequirement;
        reader.ConstraintRequirement = true;
        var condition = ParseCondition(ref reader);
        reader.ConstraintRequirement = previousRequirement;
        var constraint = new IsKoto(ref reader, SourceSpan.FromBounds(subject.Span.Start, Math.Max(isRange.End, condition.Span.End)), subject, condition);
        if (ReportedSince(ref reader, lastError, out var cause))
        {
            // A part of the Constraint failed: the Constraint is a recovery, so Binding judges none of its parts (DIAGNOSTICS.md §4.3).
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
                    // SPEC 8.5: a requirement Type may be a Contract-qualified projection such as I.(LendingIterator).LentItem(step).
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
    /// <param name="declarationContext">Whether any Type before <c>is</c> starts a Constraint in this dedicated declaration region, so that one that is no subject is reported as such.</param>
    /// <returns><see langword="true"/> for Type subject syntax followed by <c>is</c>.</returns>
    public static bool IsTypeConstraintStart(ref TokenReader reader, bool declarationContext = false)
    {
        if (HasSimpleConstraintSubject(ref reader))
        {
            return true;
        }

        var first = reader.CurrentTokenKind;
        // Preserve executable prefix recognition, including call results and parenthesized
        // expressions. Only a dedicated declaration region reads a Type before `is`.
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
}
