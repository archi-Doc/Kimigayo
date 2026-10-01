// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// SPEC 19: compile-time directives. A directive body is parsed by the item loop of its enclosing owner; selected
// syntax goes into that owner and excluded syntax is parsed with the same grammar but registers nothing (SPEC 19.5).
public static partial class Parser
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsCompileTimeSwitchStart(ref TokenReader reader)
        => reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) == TokenKind.Switch;

    /// <summary>Enters the excluded syntax of the pending target, or of a <c>#switch</c> arm.</summary>
    /// <param name="reader">The token reader positioned at the excluded syntax.</param>
    /// <param name="directive">The innermost excluding directive.</param>
    /// <param name="documentationStart">Where documentation exclusion begins.</param>
    /// <returns>The state that <see cref="EndExcludedRegion"/> restores.</returns>
    internal static ExcludedRegion BeginExcludedRegion(ref TokenReader reader, SourceSpan directive, int documentationStart)
    {
        var region = new ExcludedRegion(reader.CurrentTokenRange.Start, documentationStart, reader.ExcludingDirective);
        reader.ExclusionDepth++;
        reader.ExcludingDirective = directive;
        return region;
    }

    /// <summary>Enters the excluded syntax that the current item's directive prefixes selected out.</summary>
    /// <param name="reader">The token reader positioned at the excluded item.</param>
    /// <returns>The state that <see cref="EndExcludedRegion"/> restores.</returns>
    internal static ExcludedRegion BeginExcludedRegion(ref TokenReader reader)
        => BeginExcludedRegion(ref reader, reader.PendingExclusion, reader.DocumentationExcludedStart);

    /// <summary>Leaves excluded syntax: its documentation is excluded and its range is recorded for <c>excludedBy</c> (SPEC 19.5, 23.3.6.2).</summary>
    /// <param name="reader">The token reader positioned after the excluded syntax.</param>
    /// <param name="region">The state returned by <see cref="BeginExcludedRegion(ref TokenReader, SourceSpan, int)"/>.</param>
    internal static void EndExcludedRegion(ref TokenReader reader, in ExcludedRegion region)
    {
        var end = reader.PreviousSyntaxEnd;
        reader.CodeContext.Documentation?.Exclude(region.DocumentationStart, end, reader.CurrentTokenRange.Start);
        if (end > region.Start)
        {
            reader.CodeContext.RecordExcludedRange(SourceSpan.FromBounds(region.Start, end), reader.ExcludingDirective);
        }

        reader.ExclusionDepth--;
        reader.ExcludingDirective = region.PreviousDirective;
    }

    /// <summary>Validates every arm header and Condition of a <c>#switch</c> before any arm body is parsed (SPEC 19.3).</summary>
    /// <param name="reader">The token reader positioned at <c>#switch</c>; it is not advanced.</param>
    /// <returns>The arm that the <c>#switch</c> would select, independent of enclosing exclusion.</returns>
    /// <remarks>
    /// Header, structure and Condition diagnostics are reported here exactly once; the arm walk that follows
    /// (<see cref="BeginCompileTimeSwitchArms"/>, <see cref="TryNextCompileTimeSwitchArm"/>) skips headers silently.
    /// </remarks>
    internal static CompileTimeSwitchSelection ScanCompileTimeSwitch(ref TokenReader reader)
    {
        var scan = reader; // A copy: the arms are walked again to parse their bodies.
        var groupStart = scan.CurrentTokenRange.Start;
        var header = scan.CurrentTokenRange;
        scan.Advance(2); // #switch has no subject or condition on its header.
        var groupEnd = scan.CurrentTokenRange.Start;
        var invalidSyntax = false;
        if (scan.CanRead && scan.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
        {
            scan.Expect(SyntaxForm.LineEnd);
            SkipCompileTimeHeaderRemainder(ref scan);
            invalidSyntax = true;
        }

        if (!scan.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            scan.Diagnostic.Add(header, DiagnosticCode.EmptyCompileTimeSwitch_Kd);
            return new(-1, -1);
        }

        scan.Advance(); // The arm list is not a new lookup scope.
        var armCount = 0;
        var selected = -1;
        var invalidCondition = false;
        var fallbackSeen = false;
        var fallbackMustBeLastReported = false;
        var fallbackSpan = default(SourceSpan);
        while (scan.CanRead)
        {
            scan.SkipSeparators();
            if (scan.CurrentTokenKind == TokenKind.EndBlock)
            {
                groupEnd = scan.Read().Span.End;
                break;
            }

            if (!scan.CanRead)
            {
                break;
            }

            if (!IsCompileTimeCaseStart(ref scan))
            {
                scan.AddDiagnostic(DiagnosticCode.InvalidCompileTimeSwitchItem_Kd);
                invalidSyntax = true;
                SkipCompileTimeHeaderRemainder(ref scan);
                if (scan.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    groupEnd = scan.CurrentTokenRange.End;
                    scan.SkipCurrentBlock(false);
                }

                continue;
            }

            if (fallbackSeen && !fallbackMustBeLastReported)
            {
                scan.Diagnostic.Add(fallbackSpan, DiagnosticCode.CompileTimeCaseFallbackMustBeLast_Kd);
                fallbackMustBeLastReported = true;
                invalidSyntax = true;
            }

            var sharp = scan.Read();
            scan.Advance(); // The loop saw '#' and 'case'.
            CompileTimeConditionResult result;
            if (scan.CurrentTokenKind == TokenKind.Underscore)
            {
                var fallbackToken = scan.Read();
                if (fallbackSeen)
                {
                    scan.Diagnostic.Add(fallbackToken.Span, DiagnosticCode.DuplicateCompileTimeCaseFallback_Kd);
                }
                else
                {
                    fallbackSeen = true;
                    fallbackSpan = SourceSpan.FromBounds(sharp.Span.Start, fallbackToken.Span.End);
                }

                result = CompileTimeConditionResult.True;
            }
            else
            {
                var condition = ParseRequiredCompileTimeCondition(ref scan);
                result = CompileTimeConditionEvaluator.Evaluate(scan.CodeContext.Compilation, condition);
            }

            if (scan.CanRead && scan.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
            {
                scan.Expect(SyntaxForm.LineEnd);
                SkipCompileTimeHeaderRemainder(ref scan);
                invalidSyntax = true;
            }

            groupEnd = Math.Max(groupEnd, scan.PreviousEnd);
            if (scan.TrySkipSeparatorsTo(TokenKind.StartBlock))
            {
                scan.SkipCurrentBlock(false);
                groupEnd = Math.Max(groupEnd, scan.PreviousEnd);
            }

            invalidCondition |= result == CompileTimeConditionResult.Error;
            if (selected < 0 && result == CompileTimeConditionResult.True)
            {
                selected = armCount;
            }

            armCount++;
        }

        if (armCount == 0)
        {
            scan.Diagnostic.Add(header, DiagnosticCode.EmptyCompileTimeSwitch_Kd);
            return new(-1, -1);
        }

        if (invalidSyntax || invalidCondition)
        {
            return new(-1, armCount);
        }

        if (selected < 0 && !reader.InExcludedSyntax && !reader.IsExcluded)
        {
            // A valid #switch with no True arm and no catch-all, where selection actually happens (SPEC 19.3).
            scan.Diagnostic.Add(SourceSpan.FromBounds(groupStart, groupEnd), DiagnosticCode.NonExhaustiveCompileTimeCase_Kd);
        }

        return new(selected, armCount);
    }

    /// <summary>Consumes a <c>#switch</c> header whose diagnostics <see cref="ScanCompileTimeSwitch"/> reported.</summary>
    /// <param name="reader">The token reader positioned at <c>#switch</c>.</param>
    /// <returns><see langword="true"/> when the arm list follows.</returns>
    internal static bool BeginCompileTimeSwitchArms(ref TokenReader reader)
    {
        reader.Advance(2);
        SkipCompileTimeHeaderRemainder(ref reader);
        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            return false;
        }

        reader.Advance();
        return true;
    }

    /// <summary>Moves to the next <c>#case</c> arm of a <c>#switch</c> body, skipping invalid items silently.</summary>
    /// <param name="reader">The token reader inside the arm list.</param>
    /// <param name="header">The arm header: the <c>#case</c> keyword through its Condition.</param>
    /// <returns><see langword="true"/> when an arm header was consumed; <see langword="false"/> after the arm list.</returns>
    internal static bool TryNextCompileTimeSwitchArm(ref TokenReader reader, out SourceSpan header)
    {
        while (true)
        {
            reader.SkipSeparators();
            if (reader.CurrentTokenKind == TokenKind.EndBlock)
            {
                reader.Advance();
                header = default;
                return false;
            }

            if (!reader.CanRead)
            {
                header = default;
                return false;
            }

            if (!IsCompileTimeCaseStart(ref reader))
            {
                SkipCompileTimeHeaderRemainder(ref reader);
                if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    reader.SkipCurrentBlock(false);
                }

                continue;
            }

            var start = reader.CurrentTokenRange.Start;
            reader.Advance(2);
            SkipCompileTimeHeaderRemainder(ref reader);
            header = SourceSpan.FromBounds(start, reader.PreviousEnd);
            return true;
        }
    }

    /// <summary>Creates the marker of a <c>#switch</c> outside excluded syntax that selected no arm.</summary>
    /// <param name="reader">The token reader positioned after the Case Group.</param>
    /// <param name="start">The start of the <c>#switch</c> keyword.</param>
    /// <param name="selection">The selection of the Case Group.</param>
    /// <returns>The marker, or <see langword="null"/> when an arm was selected or the Case Group is excluded syntax.</returns>
    internal static CompileTimeSwitchKoto? UnselectedCompileTimeSwitch(ref TokenReader reader, int start, CompileTimeSwitchSelection selection)
        => selection.Selected < 0 && !reader.InExcludedSyntax
            ? new CompileTimeSwitchKoto(ref reader, SourceSpan.FromBounds(start, Math.Max(start, reader.PreviousSyntaxEnd)))
            : null;

    /// <summary>Reports Attributes left before a directive Block or <c>#switch</c>: no declaration at their indentation follows (SPEC 6.5).</summary>
    /// <param name="reader">The token reader.</param>
    internal static void RejectDirectiveBlockAttributes(ref TokenReader reader)
    {
        if (reader.AttributeKoto is not null)
        {
            reader.Expect(SyntaxForm.Declaration);
            _ = reader.PopAttribute();
        }
    }

    private static bool IsCompileTimeCaseStart(ref TokenReader reader)
        => reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) == TokenKind.Case;

    private static void SkipCompileTimeHeaderRemainder(ref TokenReader reader)
    {
        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
        {
            reader.Advance();
        }
    }

    private static void ParseCompileTimeIfPrefix(ref TokenReader reader)
    {
        // Every Condition is validated, including inside excluded syntax (SPEC 19.3).
        var attributes = reader.PopAttribute();
        reader.Advance(2); // The caller saw '#' and 'if'.
        var condition = ParseRequiredCompileTimeCondition(ref reader);
        var invalidHeader = reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock);
        if (invalidHeader)
        {
            reader.Expect(SyntaxForm.LineEnd);
            SkipCompileTimeHeaderRemainder(ref reader);
        }

        if (attributes is not null)
        {
            reader.PushAttribute(attributes);
        }

        var result = CompileTimeConditionEvaluator.Evaluate(reader.CodeContext.Compilation, condition);
        if (invalidHeader || result != CompileTimeConditionResult.True)
        {
            ExcludeTarget(ref reader, condition.Span, condition.Span.End);
        }
    }

    /// <summary>Validates a <c>#case</c> outside a <c>#switch</c> body and makes its body an excluded target.</summary>
    /// <param name="reader">The token reader positioned at <c>#case</c>.</param>
    /// <returns><see langword="true"/> when an indented body follows and is now the pending excluded target.</returns>
    private static bool ParseOrphanCompileTimeCase(ref TokenReader reader)
    {
        var start = reader.CurrentTokenRange.Start;
        reader.AddDiagnostic(DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd);
        reader.Advance(2); // '#' 'case'
        if (!reader.TryConsume(TokenKind.Underscore))
        {
            var condition = ParseRequiredCompileTimeCondition(ref reader);
            _ = CompileTimeConditionEvaluator.Evaluate(reader.CodeContext.Compilation, condition);
        }

        if (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
        {
            reader.Expect(SyntaxForm.LineEnd);
            SkipCompileTimeHeaderRemainder(ref reader);
        }

        var header = SourceSpan.FromBounds(start, reader.PreviousEnd);
        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.Expect(SyntaxForm.Body);
            return false;
        }

        ExcludeTarget(ref reader, header, start);
        return true;
    }

    /// <summary>Marks the pending target as excluded syntax because of the directive at <paramref name="directive"/>.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="directive">The excluding Condition or <c>#case</c> header: the innermost excluding directive.</param>
    /// <param name="documentationStart">Where documentation exclusion begins when this is the outermost exclusion of the target.</param>
    private static void ExcludeTarget(ref TokenReader reader, SourceSpan directive, int documentationStart)
    {
        if (!reader.IsExcluded)
        {
            reader.DocumentationExcludedStart = documentationStart;
        }

        reader.IsExcluded = true;
        reader.PendingExclusion = directive;
    }

    /// <summary>Consumes one syntax item for error recovery, without constructing Koto nodes.</summary>
    /// <param name="reader">The token reader positioned at the item.</param>
    private static void SkipItemForRecovery(ref TokenReader reader)
    {
        if (IsCompileTimeSwitchStart(ref reader) || IsCompileTimeCaseStart(ref reader))
        {
            reader.Advance(2);
            SkipCompileTimeHeaderRemainder(ref reader);
            if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
            {
                reader.SkipCurrentBlock(false);
            }

            return;
        }

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.SkipCurrentBlock(false);
            return;
        }

        var startsWithPrefix = reader.CurrentTokenKind == TokenKind.Sharp;
        _ = reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.SkipCurrentBlock(false);
            return;
        }

        reader.SkipSeparators();
        if (startsWithPrefix && reader.CanRead && reader.CurrentTokenKind != TokenKind.EndBlock)
        {
            SkipItemForRecovery(ref reader);
        }
    }

    /// <summary>The state that leaving excluded syntax restores.</summary>
    /// <param name="Start">The start of the excluded syntax.</param>
    /// <param name="DocumentationStart">Where documentation exclusion begins.</param>
    /// <param name="PreviousDirective">The enclosing excluding directive.</param>
    internal readonly record struct ExcludedRegion(int Start, int DocumentationStart, SourceSpan PreviousDirective);

    /// <summary>The arm a <c>#switch</c> would select.</summary>
    /// <param name="Selected">The selected arm index, or -1 when the <c>#switch</c> selects nothing.</param>
    /// <param name="ArmCount">The number of <c>#case</c> arms, or -1 without an arm list.</param>
    internal readonly record struct CompileTimeSwitchSelection(int Selected, int ArmCount);

    /// <summary>The Constraint-prefix state of one executable body, shared by its directive targets and arms (SPEC 7.4, 19.5).</summary>
    /// <param name="function">The function whose body this is, if any.</param>
    /// <param name="originOwner">The declaration that owns leading Origin relations, if not the function.</param>
    private struct ExecutableItemState(FunctionKoto? function, Koto? originOwner)
    {
        public readonly FunctionKoto? Function = function;

        public readonly Koto? OriginOwner = originOwner;

        public bool SeenExecutableItem;
    }
}
