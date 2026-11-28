// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The control constructs (SPEC 14.3-14.8, Appendix F.10.2): if, while, loop, for with its bindings, match, do, labels and control transfers,
// and the header expression they share.
// Input: a reader at the construct's keyword or label. Output: the construct's node.
// Invariants: a header expression has a layout region of its own (Appendix F.4.4), and a Body is parsed by Parser.Blocks.
// Prohibitions: Patterns belong to Parser.Syntax; whether a transfer's target exists, and what a Body yields, to Binding.
public static partial class Parser
{
    private static IfKoto ParseIfExpression(ref TokenReader reader)
    {
        // A nested if is parsed in full and recorded as a recovery, so its Type rests on the Error.
        var cause = reader.IfBodyRegion ? reader.Unexpected(SyntaxForm.NestedIfExpression) : default(DiagnosticKey?);
        var ifToken = reader.Read();
        var branches = new List<ConditionalBranchKoto>(2);
        CodeBlockKoto? elseBody = null;
        int end;

        while (true)
        {
            var condition = ParseHeaderExpression(ref reader);
            var body = ParseRequiredBody(ref reader, true);
            branches.Add(new ConditionalBranchKoto(condition, body));
            end = body.Span.End;

            if (!reader.TrySkipSeparatorsTo(TokenKind.Else))
            {
                break;
            }

            reader.Advance();
            if (reader.TryConsume(TokenKind.If))
            {
                continue;
            }

            elseBody = ParseRequiredBody(ref reader, true);
            end = elseBody.Span.End;
            break;
        }

        var result = new IfKoto(
            ref reader,
            SourceSpan.FromBounds(ifToken.Span.Start, end),
            branches,
            elseBody);
        if (cause is { } nested)
        {
            reader.CodeContext.RecordRecovery(result, nested);
        }

        return result;
    }

    private static WhileKoto ParseWhileExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var condition = ParseHeaderExpression(ref reader);
        var body = ParseRequiredBody(ref reader);
        return new WhileKoto(
            ref reader,
            SourceSpan.FromBounds(token.Span.Start, body.Span.End),
            condition,
            body);
    }

    private static LoopKoto ParseLoopExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var body = ParseRequiredBody(ref reader);
        return new LoopKoto(
            ref reader,
            SourceSpan.FromBounds(token.Span.Start, body.Span.End),
            body);
    }

    private static ForKoto ParseForExpression(ref TokenReader reader)
    {
        var forToken = reader.Read();
        var bindings = new List<IdentifierNameKoto>(2);
        var isTupleBinding = reader.CurrentTokenKind == TokenKind.OpenParenthesis;
        bool[]? mutableSlots = null;
        DiagnosticKey? malformed = null;

        if (isTupleBinding)
        {
            malformed = ParseForTupleBindings(ref reader, bindings, ref mutableSlots);
        }
        else if (ParseForSlot(ref reader, out var mutable) is { } binding)
        {
            bindings.Add(binding);
            mutableSlots = mutable ? [true] : null;
        }
        else
        {
            malformed = reader.Expect(SyntaxForm.Name);
            if (reader.CanRead &&
                reader.CurrentTokenKind != TokenKind.In &&
                !IsExpressionBoundary(ref reader))
            {
                reader.Advance();
            }
        }

        reader.Expect(TokenKind.In);

        var iterable = ParseHeaderExpression(ref reader);
        var body = ParseRequiredBody(ref reader);
        var loop = new ForKoto(
            ref reader,
            SourceSpan.FromBounds(forToken.Span.Start, body.Span.End),
            bindings,
            iterable,
            body,
            isTupleBinding,
            mutableSlots);
        if (malformed is { } cause)
        {
            // The slots the loop binds are unknown, so its iteration is not checked: the iterable and the body are, on their own.
            reader.CodeContext.RecordRecovery(loop, cause);
        }

        return loop;
    }

    /// <summary>Parses the slots of <c>(slot, ...)</c> (SPEC 14.6.1, F.10); a failed slot is skipped whole and the list continues.</summary>
    /// <returns>The key of the first Error, or <see langword="null"/> for a well-formed list.</returns>
    private static DiagnosticKey? ParseForTupleBindings(ref TokenReader reader, List<IdentifierNameKoto> bindings, ref bool[]? mutableSlots)
    {
        reader.Advance();
        DiagnosticKey? malformed = null;
        while (true)
        {
            var start = reader.Position;
            if (reader.CurrentTokenKind is TokenKind.CloseParenthesis or TokenKind.In || IsExpressionBoundary(ref reader))
            {
                // An empty list, a trailing comma or a list cut short: a ForBinding needs a slot here (SPEC F.10).
                malformed ??= reader.Expect(SyntaxForm.Name);
            }
            else if (ParseForSlot(ref reader, out var mutable) is { } binding)
            {
                if (mutable)
                {
                    Array.Resize(ref mutableSlots, bindings.Count + 1);
                    mutableSlots[bindings.Count] = true;
                }

                bindings.Add(binding);
            }
            else
            {
                // Not a slot, such as a nested list: the whole slot is skipped, so a following comma still continues the list.
                malformed ??= reader.Expect(SyntaxForm.Name);
                reader.SkipListItem();
            }

            if (reader.TryConsume(TokenKind.Comma))
            {
                continue;
            }

            if (reader.TryConsume(TokenKind.CloseParenthesis))
            {
                return malformed;
            }

            if (reader.CurrentTokenKind == TokenKind.In || IsExpressionBoundary(ref reader))
            {
                var missing = reader.Expect(SyntaxForm.CloseParenthesis);
                return malformed ?? missing;
            }

            // Two slots without a comma between them; the next one is read, unless nothing of this one could be.
            malformed ??= reader.Expect(SyntaxForm.Comma);
            if (reader.Position == start)
            {
                return malformed;
            }
        }
    }

    // SPEC 14.6.1, F.10.2: ForSlot := Name | "var" Name | "_"; a bare Name is an immutable let binding. Returns the slot's Name, or
    // null where no slot stands (nothing is reported for it; a "var" before it is consumed); mutable is whether "var" was written.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IdentifierNameKoto? ParseForSlot(ref TokenReader reader, out bool mutable)
    {
        mutable = false;
        if (reader.CurrentTokenKind == TokenKind.Var)
        {
            var varToken = reader.Read();
            mutable = true;
            if (reader.CurrentTokenKind == TokenKind.Underscore)
            {
                reader.Unexpected(SyntaxForm.WildcardBinding, varToken.Span);
            }
        }

        if (reader.CurrentTokenKind == TokenKind.Underscore)
        {
            return new IdentifierNameKoto(ref reader, reader.Read(), "_");
        }

        if (reader.CurrentTokenKind == TokenKind.In ||
            !reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
        {
            return null;
        }

        var token = reader.Read();
        return IdentifierNameKoto.TryCreate(ref reader, token, out var slot) ? slot : null;
    }

    private static MatchKoto ParseMatchExpression(ref TokenReader reader)
    {
        var matchToken = reader.Read();
        var expression = ParseHeaderExpression(ref reader);
        var arms = new List<MatchArmKoto>(4);
        var end = expression.Span.End;

        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            // A token left on the header's line, before the indented arms that follow, is where the line should end; the arms are
            // still the match's. Without them, the armless match stands for the missing arms, so its exhaustiveness rests on the
            // Error (DIAGNOSTICS.md §4.3).
            if (!reader.IndentedBodyFollows())
            {
                var cause = reader.Expect(SyntaxForm.MatchArms);
                reader.SkipHeaderLine();
                var armless = new MatchKoto(
                    ref reader,
                    SourceSpan.FromBounds(matchToken.Span.Start, end),
                    expression,
                    arms);
                reader.CodeContext.RecordRecovery(armless, cause);
                return armless;
            }

            reader.Expect(SyntaxForm.LineEnd);
            reader.SkipHeaderLine();
        }

        reader.Advance();
        while (reader.CanRead)
        {
            reader.SkipSeparators();
            if (!reader.CanRead)
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                // Like a block, the arm list ends at its last written token, not at the dedented line.
                end = reader.PreviousSyntaxEnd;
                reader.Advance();
                return new MatchKoto(
                    ref reader,
                    SourceSpan.FromBounds(matchToken.Span.Start, end),
                    expression,
                    arms);
            }

            if (reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) is TokenKind.If or TokenKind.Switch or TokenKind.Case)
            {
                // A directive selects whole items, and an arm is none (SPEC 19.1, Appendix F.11): it is one problem, and its line is
                // skipped with the indented arms of a block form or a #switch; the arms after it are read as usual.
                reader.Unexpected(SyntaxForm.CompileTimeDirective, SourceSpan.FromBounds(reader.CurrentTokenRange.Start, reader.PeekToken(1).Span.End));
                if (reader.SkipHeaderLine())
                {
                    reader.SkipCurrentBlock();
                }

                continue;
            }

            var oldPosition = reader.Position;
            var pattern = ParsePattern(ref reader);
            var region = reader.EnterRegion();
            var guard = reader.TryConsume(TokenKind.If) ? ParseHeaderExpression(ref reader) : null;
            var parsedBody = ParseRequiredBody(ref reader);
            Koto body = parsedBody.IsExpressionBody ? parsedBody.Items[0] : parsedBody;
            arms.Add(new MatchArmKoto(pattern, body) { Guard = guard });
            end = body.Span.End;
            reader.RestoreRegion(region);
            if (parsedBody.IsExpressionBody && reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.EndBlock))
            {
                // An arm ends with its line, as a statement does; what remains there is no next arm.
                reader.ExpectLineEnd();
            }

            if (reader.Position == oldPosition)
            {
                reader.Advance();
            }
        }

        // The arm list ends only through its EndBlock, which the tokenizer supplies at the end of the source.
        return new MatchKoto(
            ref reader,
            SourceSpan.FromBounds(matchToken.Span.Start, end),
            expression,
            arms);
    }

    private static Koto ParseJumpExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var end = token.Span.End;
        string? label = null;
        Koto? expression = null;
        DiagnosticKey? recovery = null;
        var sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
        var validTarget = true;
        if (sameLine && token.Kind != TokenKind.Return && reader.IsCurrentIdentifier("to"))
        {
            end = reader.Read().Span.End;
            sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
            if (!sameLine || !reader.CurrentTokenKind.IsIdentifierOrContextualKeyword())
            {
                recovery = reader.ReportOnce(sameLine ? reader.CurrentTokenRange : new SourceSpan(end, 0), DiagnosticCode.TransferTargetExpected_Kd);
                validTarget = false;
                label = string.Empty; // A malformed named target must not become an unnamed transfer.
            }
            else
            {
                var name = reader.Read();
                end = name.Span.End;
                reader.TryGetIdentifier(name, out label);
                sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
            }
        }

        // Named and unnamed transfers share omission and full-expression parsing. Colon is not a boundary here.
        if (validTarget && sameLine && token.Kind != TokenKind.Continue && !IsExpressionBoundary(ref reader))
        {
            var kind = reader.CurrentTokenKind;
            if (kind == TokenKind.Colon ||
                (InfixLeftBindingPower[(byte)kind] != 0 && !IsPrefixOperator[(byte)kind] && kind is not (TokenKind.DotDot or TokenKind.DotDotEquals)))
            {
                var invalid = reader.Read();
                reader.Diagnostic.Add(invalid.Span, DiagnosticCode.TransferOperandExpected_Kd, invalid.Kind.ToText());
                recovery = reader.Diagnostic.LastError;
                expression = new ErrorKoto(ref reader, invalid.Span) { Cause = recovery };
                end = invalid.Span.End;
                sameLine = reader.CanRead && reader.SameLine(end, reader.CurrentTokenRange.Start);
            }
            else if (label is not null && end == reader.CurrentTokenRange.Start)
            {
                reader.AddDiagnostic(DiagnosticCode.TransferOperandSeparation_Kd);
                recovery = reader.Diagnostic.LastError;
            }

            // Retain a same-line operand after a bad separator for recovery, without absorbing the next item.
            if (sameLine && !IsExpressionBoundary(ref reader))
            {
                expression = ParseRequiredExpression(ref reader);
                end = expression.Span.End;
            }
        }

        var range = SourceSpan.FromBounds(token.Span.Start, end);
        JumpKoto result = token.Kind switch
        {
            TokenKind.Return => new ReturnKoto(ref reader, range, expression),
            TokenKind.Exit => new ExitKoto(ref reader, range, expression, label),
            TokenKind.Yield => new YieldKoto(ref reader, range, expression, label),
            TokenKind.Continue => new ContinueKoto(ref reader, range, label),
            _ => throw new InvalidOperationException(),
        };
        if (recovery is { } cause)
        {
            reader.CodeContext.RecordRecovery(result, cause);
        }

        return result;
    }

    private static Koto ParseHeaderExpression(ref TokenReader reader)
    {
        var region = reader.EnterHeader();
        try
        {
            return ParseRequiredExpression(ref reader);
        }
        finally
        {
            reader.RestoreRegion(region);
        }
    }

    private static DoKoto ParseDoExpression(ref TokenReader reader)
    {
        var token = reader.Read();
        var body = ParseRequiredBody(ref reader);
        return new DoKoto(ref reader, SourceSpan.FromBounds(token.Span.Start, body.Span.End), body);
    }

    private static bool IsLabelPrefix(ref TokenReader reader)
        => reader.IsCurrentIdentifier("label") && reader.PeekKind(1).IsIdentifierOrContextualKeyword() &&
            reader.PeekKind(2) == TokenKind.Colon && reader.SameLine(reader.CurrentTokenRange.End, reader.PeekToken(2).Span.Start);

    // SPEC F.9.1's `LabelPrefix LabelableConstruct`, from the label: the target is a LabelableConstruct on the label's line (F.10.2).
    // Anything else is reported, and another item on that line is read in the target's place.
    private static Koto ParseLabeledExpression(ref TokenReader reader)
    {
        if (reader.HeaderRegion)
        {
            reader.Unexpected(SyntaxForm.HeaderBodyExpression);
        }

        var introducer = reader.Read();
        var name = reader.Read();
        reader.TryGetIdentifier(name, out var label);
        var colon = reader.Read();
        var sameLine = reader.CanRead && reader.SameLine(colon.Span.End, reader.CurrentTokenRange.Start);
        DiagnosticKey? recovery = null;
        Koto target;
        if (!sameLine || !IsLabelableConstruct[(byte)reader.CurrentTokenKind])
        {
            recovery = reader.ReportOnce(sameLine ? reader.CurrentTokenRange : new SourceSpan(colon.Span.End, 0), DiagnosticCode.LabelTargetExpected_Kd);
            target = new ErrorKoto(ref reader, colon.Span) { Cause = recovery };
            if (sameLine && !IsExpressionBoundary(ref reader))
            {
                target = ParseBlockItem(ref reader) ?? target;
            }
        }
        else
        {
            // Primary reads the construct by its first word (SPEC F.9.1: LabelPrefix? LabelableConstruct).
            target = ParsePrimaryExpression(ref reader);
        }

        var result = new LabeledKoto(ref reader, SourceSpan.FromBounds(introducer.Span.Start, target.Span.End), label ?? string.Empty, target);
        if (recovery is { } cause)
        {
            reader.CodeContext.RecordRecovery(result, cause);
        }

        return result;
    }
}
