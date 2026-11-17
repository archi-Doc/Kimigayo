// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// SPEC 19: compile-time directives. A directive body is parsed by the item loop of its enclosing owner; selected
// syntax goes into that owner and excluded syntax is parsed with the same grammar but registers nothing (SPEC 19.4).
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
    {
        var region = BeginExcludedRegion(ref reader, reader.PendingExclusion, reader.DocumentationExcludedStart);
        // The target starts after the excluding header, so later prefixes and modifiers belong to it too. Once entered,
        // ExclusionDepth owns this interval; clearing an item's context must not finish or leak the pending exclusion.
        reader.IsExcluded = false;
        return region with { Start = reader.PendingExclusionStart };
    }

    /// <summary>Leaves excluded syntax: its documentation is excluded and its range is recorded for <c>excludedBy</c> (SPEC 19.4, 23.3).</summary>
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

    /// <summary>
    /// The arms of one <c>#switch</c> (SPEC 19.3), walked by the item loop of the enclosing owner. <see cref="Begin"/> validates the
    /// Case Group and enters its arm list; <see cref="TryNext"/> leaves the reader at each arm body or invalid item segment,
    /// and the owner parses it with its own item grammar, retaining nothing from excluded or invalid syntax (SPEC 19.4).
    /// </summary>
    internal struct CompileTimeSwitchArms
    {
        private readonly int start;
        private readonly CompileTimeSwitchSelection selection;
        private bool inArmList;
        private int next;

        private CompileTimeSwitchArms(int start, CompileTimeSwitchSelection selection, bool inArmList)
        {
            this.start = start;
            this.selection = selection;
            this.inArmList = inArmList;
        }

        /// <summary>Scans the <c>#switch</c> at the reader, reports Attributes dangling before it and enters its arm list.</summary>
        /// <param name="reader">The token reader positioned at <c>#switch</c>.</param>
        /// <returns>The arm walk.</returns>
        internal static CompileTimeSwitchArms Begin(ref TokenReader reader)
        {
            // Attributes before the Case Group are reported first: no Condition takes them (SPEC 6.7).
            RejectDirectiveBlockAttributes(ref reader);
            var start = reader.CurrentTokenRange.Start;
            var selection = ScanCompileTimeSwitch(ref reader);
            return new(start, selection, BeginCompileTimeSwitchArms(ref reader));
        }

        /// <summary>Moves to the next arm body or invalid item segment; the scan reported invalid placement and missing bodies.</summary>
        /// <param name="reader">The token reader inside the arm list.</param>
        /// <param name="arm">The arm or invalid item segment at the reader.</param>
        /// <returns><see langword="false"/> after the arm list, whose <see cref="TokenKind.EndBlock"/> is then consumed.</returns>
        internal bool TryNext(ref TokenReader reader, out CompileTimeSwitchArm arm)
        {
            while (this.inArmList)
            {
                reader.SkipSeparators();
                if (reader.CurrentTokenKind == TokenKind.EndBlock)
                {
                    reader.Advance();
                    break;
                }

                if (!reader.CanRead)
                {
                    break;
                }

                if (!IsCompileTimeCaseStart(ref reader))
                {
                    arm = new(CompileTimeSwitchArmKind.InvalidItems, default);
                    return true;
                }

                var start = reader.CurrentTokenRange.Start;
                reader.Advance(2);
                SkipCompileTimeHeaderRemainder(ref reader);
                var header = SourceSpan.FromBounds(start, reader.PreviousEnd);
                var index = this.next++;
                if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    continue;
                }

                arm = new(index == this.selection.Selected ? CompileTimeSwitchArmKind.Selected : CompileTimeSwitchArmKind.Excluded, header);
                return true;
            }

            this.inArmList = false;
            arm = default;
            return false;
        }

        /// <summary>Gets the marker of a <c>#switch</c> outside excluded syntax that selected no arm, once its arms are parsed.</summary>
        /// <param name="reader">The token reader positioned after the Case Group.</param>
        /// <returns>The marker, or <see langword="null"/> when an arm was selected or the Case Group is excluded syntax.</returns>
        /// <remarks>The marker is the recovery of the Error that kept the Case Group from selecting an arm, so no later check of it
        /// is reported again (DIAGNOSTICS.md §4.3).</remarks>
        internal readonly CompileTimeSwitchKoto? Unselected(ref TokenReader reader)
        {
            if (this.selection.Selected >= 0 || reader.InExcludedSyntax)
            {
                return null;
            }

            var marker = new CompileTimeSwitchKoto(ref reader, SourceSpan.FromBounds(this.start, Math.Max(this.start, reader.PreviousSyntaxEnd)));
            if (this.selection.Cause is { } cause)
            {
                reader.CodeContext.RecordRecovery(marker, cause);
            }

            return marker;
        }
    }

    /// <summary>Starts recovery of misplaced Case Group items. They establish nothing, without inventing an excluding arm.</summary>
    /// <param name="reader">The reader at the first misplaced item.</param>
    /// <returns>The start of the syntax whose documentation must be discarded.</returns>
    internal static int BeginInvalidSwitchItems(ref TokenReader reader)
    {
        reader.ExclusionDepth++;
        return reader.CurrentTokenRange.Start;
    }

    /// <summary>Finishes recovery without consuming the next arm or Case Group end.</summary>
    /// <param name="reader">The reader at that boundary.</param>
    /// <param name="start">The start returned by <see cref="BeginInvalidSwitchItems"/>.</param>
    internal static void EndInvalidSwitchItems(ref TokenReader reader, int start)
    {
        reader.ClearContext();
        reader.CodeContext.Documentation?.Exclude(start, reader.PreviousSyntaxEnd, reader.CurrentTokenRange.Start);
        reader.ExclusionDepth--;
    }

    /// <summary>Reports Attributes left before a directive Block or <c>#switch</c>: no declaration at their indentation follows (SPEC 6.7).</summary>
    /// <param name="reader">The token reader.</param>
    internal static void RejectDirectiveBlockAttributes(ref TokenReader reader)
    {
        if (reader.AttributeKoto is not null)
        {
            reader.Expect(SyntaxForm.Declaration);
            reader.AttributeKoto = null;
        }
    }

    /// <summary>Validates every arm header and Condition of a <c>#switch</c> before any arm body is parsed (SPEC 19.3).</summary>
    /// <param name="reader">The token reader positioned at <c>#switch</c>; it is not advanced.</param>
    /// <returns>The arm that the <c>#switch</c> would select, independent of enclosing exclusion.</returns>
    /// <remarks>
    /// Header, structure and Condition diagnostics are reported here exactly once; the arm walk that follows
    /// (<see cref="BeginCompileTimeSwitchArms"/>, <see cref="CompileTimeSwitchArms.TryNext"/>) skips arm headers silently.
    /// </remarks>
    private static CompileTimeSwitchSelection ScanCompileTimeSwitch(ref TokenReader reader)
    {
        var scan = reader; // A copy: the arms are walked again to parse their bodies.
        var groupStart = scan.CurrentTokenRange.Start;
        var header = scan.CurrentTokenRange;
        scan.Advance(2); // #switch has no subject or condition on its header.
        var invalid = !ExpectCompileTimeHeaderEnd(ref scan);
        if (!scan.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            scan.Diagnostic.Add(header, DiagnosticCode.EmptyCompileTimeSwitch_Kd);
            return new(-1, scan.Diagnostic.LastError);
        }

        scan.Advance(); // The arm list is not a new lookup scope.
        var armCount = 0;
        var selected = -1;
        var fallback = default(CaseFallbackRule);
        while (scan.CanRead)
        {
            scan.SkipSeparators();
            if (scan.CurrentTokenKind == TokenKind.EndBlock)
            {
                scan.Advance();
                break;
            }

            if (!scan.CanRead)
            {
                break;
            }

            if (!IsCompileTimeCaseStart(ref scan))
            {
                scan.AddDiagnostic(DiagnosticCode.InvalidCompileTimeSwitchItem_Kd);
                invalid = true;
                SkipCompileTimeHeaderRemainder(ref scan);
                if (scan.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    scan.SkipCurrentBlock();
                }

                continue;
            }

            // SwitchArm := "#" "case" (CompileCondition | "_") IndentedList<Item> (SPEC F.11), part by part.
            var result = ScanCompileTimeCaseCondition(ref scan, ref fallback);
            invalid |= result == CompileTimeConditionResult.Error;
            invalid |= !ExpectCompileTimeHeaderEnd(ref scan);
            if (scan.TrySkipSeparatorsTo(TokenKind.StartBlock))
            {
                scan.SkipCurrentBlock();
            }
            else
            {
                // An arm without a body leaves the Case Group invalid, as a malformed header does (SPEC 19.3).
                scan.Expect(SyntaxForm.Body);
                invalid = true;
            }

            if (selected < 0 && result == CompileTimeConditionResult.True)
            {
                selected = armCount;
            }

            armCount++;
        }

        if (armCount == 0)
        {
            scan.Diagnostic.Add(header, DiagnosticCode.EmptyCompileTimeSwitch_Kd);
            return new(-1, scan.Diagnostic.LastError);
        }

        if (invalid || fallback.Broken)
        {
            return new(-1, scan.Diagnostic.LastError);
        }

        if (selected < 0 && !reader.InExcludedSyntax && !reader.IsExcluded)
        {
            // A valid #switch with no True arm and no catch-all, where selection actually happens (SPEC 19.3). The Case Group ends
            // at its last written token, before the blank and comment lines that precede the dedented line.
            scan.Diagnostic.Add(SourceSpan.FromBounds(groupStart, scan.PreviousSyntaxEnd), DiagnosticCode.NonExhaustiveCompileTimeCase_Kd);
            return new(-1, scan.Diagnostic.LastError);
        }

        return new(selected, null);
    }

    /// <summary>Consumes a <c>#switch</c> header whose diagnostics <see cref="ScanCompileTimeSwitch"/> reported.</summary>
    /// <param name="reader">The token reader positioned at <c>#switch</c>.</param>
    /// <returns><see langword="true"/> when the arm list follows.</returns>
    private static bool BeginCompileTimeSwitchArms(ref TokenReader reader)
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

    private static bool IsCompileTimeCaseStart(ref TokenReader reader)
        => reader.CurrentTokenKind == TokenKind.Sharp && reader.PeekKind(1) == TokenKind.Case;

    /// <summary>Scans the <c>"#" "case" (CompileCondition | "_")</c> of an arm (SPEC F.11) and evaluates it.</summary>
    /// <param name="scan">The scanning reader positioned at <c>#case</c>.</param>
    /// <param name="fallback">The <c>#case _</c> rule of the arm list.</param>
    /// <returns>The Condition's result; a <c>_</c> is True.</returns>
    private static CompileTimeConditionResult ScanCompileTimeCaseCondition(ref TokenReader scan, ref CaseFallbackRule fallback)
    {
        if (fallback.TryRead(ref scan))
        {
            return CompileTimeConditionResult.True;
        }

        var condition = ParseRequiredCompileTimeCondition(ref scan);
        return CompileTimeConditionEvaluator.Evaluate(scan.CodeContext.Compilation, condition);
    }

    // A directive header ends with its line: a NEWLINE or an IndentedList follows it (SPEC F.11).
    private static bool IsCompileTimeHeaderEnd(ref TokenReader reader)
        => !reader.CanRead || reader.CurrentTokenKind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock;

    /// <summary>Ends a directive header: tokens left on its line are reported once and skipped.</summary>
    /// <param name="reader">The token reader positioned after the header's last part.</param>
    /// <returns><see langword="false"/> when tokens were left on the line.</returns>
    private static bool ExpectCompileTimeHeaderEnd(ref TokenReader reader)
    {
        if (IsCompileTimeHeaderEnd(ref reader))
        {
            return true;
        }

        reader.Expect(SyntaxForm.LineEnd);
        SkipCompileTimeHeaderRemainder(ref reader);
        return false;
    }

    private static void SkipCompileTimeHeaderRemainder(ref TokenReader reader)
    {
        while (!IsCompileTimeHeaderEnd(ref reader))
        {
            reader.Advance();
        }
    }

    // A Condition is a header expression: the indented body after it is the directive's target, never part of it (SPEC 19.1).
    private static Koto ParseRequiredCompileTimeCondition(ref TokenReader reader)
    {
        var previous = reader.IsParsingCompileTimeCondition;
        reader.IsParsingCompileTimeCondition = true;
        try
        {
            return ParseHeaderExpression(ref reader);
        }
        finally
        {
            reader.IsParsingCompileTimeCondition = previous;
        }
    }

    private static void ParseCompileTimeIfPrefix(ref TokenReader reader)
    {
        // Every Condition is validated, including inside excluded syntax (SPEC 19.3).
        reader.Advance(2); // The caller saw '#' and 'if'.
        var condition = ParseRequiredCompileTimeCondition(ref reader);
        var invalidHeader = !ExpectCompileTimeHeaderEnd(ref reader);
        var result = CompileTimeConditionEvaluator.Evaluate(reader.CodeContext.Compilation, condition);
        if (invalidHeader || result != CompileTimeConditionResult.True)
        {
            ExcludeTarget(ref reader, condition.Span, condition.Span.End);
        }
    }

    /// <summary>Validates a <c>#case</c> outside a <c>#switch</c> body and makes its body an excluded target.</summary>
    /// <param name="reader">The token reader positioned at <c>#case</c>.</param>
    /// <returns><see langword="true"/> when an indented body follows and is now the pending excluded target.</returns>
    /// <remarks>Attributes before it are reported first, as before a <c>#switch</c>: its body is a block and its Condition takes
    /// none. Without a body, the <c>#case</c> line is the whole item, and an enclosing prefix's exclusion ends with it.</remarks>
    private static bool ParseOrphanCompileTimeCase(ref TokenReader reader)
    {
        RejectDirectiveBlockAttributes(ref reader);
        var start = reader.CurrentTokenRange.Start;
        reader.AddDiagnostic(DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd);
        reader.Advance(2); // '#' 'case'
        if (!reader.TryConsume(TokenKind.Underscore))
        {
            var condition = ParseRequiredCompileTimeCondition(ref reader);
            _ = CompileTimeConditionEvaluator.Evaluate(reader.CodeContext.Compilation, condition);
        }

        _ = ExpectCompileTimeHeaderEnd(ref reader);
        var header = SourceSpan.FromBounds(start, reader.PreviousEnd);
        if (!reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.Expect(SyntaxForm.Body);
            reader.ClearContext();
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
        if (reader.IsExcluded)
        {
            // This header is the previous directive's target, never its own. Finish that segment before changing cause.
            reader.RecordPendingExclusion(reader.PreviousSyntaxEnd);
        }
        else
        {
            reader.DocumentationExcludedStart = documentationStart;
        }

        reader.IsExcluded = true;
        reader.PendingExclusion = directive;
        reader.PendingExclusionStart = reader.PreviousSyntaxEnd + 1;
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
                reader.SkipCurrentBlock();
            }

            return;
        }

        if (reader.CurrentTokenKind == TokenKind.StartBlock)
        {
            reader.SkipCurrentBlock();
            return;
        }

        var startsWithPrefix = reader.CurrentTokenKind == TokenKind.Sharp;
        _ = reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.SkipCurrentBlock();
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

    /// <summary>One step of the arm walk: a SwitchArm (SPEC F.11) with its selection, or the items misplaced between arms.</summary>
    /// <param name="Kind">What the owner parses at the reader.</param>
    /// <param name="Header">The arm header, the excluding directive of an excluded arm; empty for invalid items.</param>
    internal readonly record struct CompileTimeSwitchArm(CompileTimeSwitchArmKind Kind, SourceSpan Header);

    /// <summary>What the arm walk leaves at the reader for the owner's item grammar (SPEC 19.4).</summary>
    internal enum CompileTimeSwitchArmKind : byte
    {
        /// <summary>The body of the arm the <c>#switch</c> selects.</summary>
        Selected,

        /// <summary>The body of an arm the <c>#switch</c> does not select: excluded syntax of its header.</summary>
        Excluded,

        /// <summary>Items misplaced in the arm list, up to the next arm or the list's end: source-checked and discarded.</summary>
        InvalidItems,
    }

    /// <summary>The arm a <c>#switch</c> would select.</summary>
    /// <param name="Selected">The selected arm index, or -1 when the <c>#switch</c> selects nothing.</param>
    /// <param name="Cause">The Error that keeps the <c>#switch</c> from selecting an arm, if one was reported.</param>
    private readonly record struct CompileTimeSwitchSelection(int Selected, DiagnosticKey? Cause);

    /// <summary>The <c>#case _</c> rule over one arm list: it occurs at most once, as the last arm (SPEC F.11).</summary>
    private struct CaseFallbackRule
    {
        private SourceSpan? first;

        /// <summary>Gets a value indicating whether an arm followed <c>#case _</c>, which leaves the Case Group invalid.</summary>
        public bool Broken { get; private set; }

        /// <summary>Reads the <c>"#" "case"</c> of an arm and its <c>_</c>, if written.</summary>
        /// <param name="scan">The scanning reader positioned at <c>#case</c>.</param>
        /// <returns><see langword="true"/> for <c>#case _</c>; otherwise the reader is at the arm's Condition.</returns>
        /// <remarks>The first <c>#case _</c> is reported once when an arm follows it, and each later one as a duplicate.</remarks>
        public bool TryRead(ref TokenReader scan)
        {
            if (this.first is { } fallback && !this.Broken)
            {
                scan.Diagnostic.Add(fallback, DiagnosticCode.CompileTimeCaseFallbackMustBeLast_Kd);
                this.Broken = true;
            }

            var sharp = scan.Read();
            scan.Advance(); // The arm list saw '#' and 'case'.
            if (scan.CurrentTokenKind != TokenKind.Underscore)
            {
                return false;
            }

            var underscore = scan.Read();
            if (this.first is null)
            {
                this.first = SourceSpan.FromBounds(sharp.Span.Start, underscore.Span.End);
            }
            else
            {
                scan.Diagnostic.Add(underscore.Span, DiagnosticCode.DuplicateCompileTimeCaseFallback_Kd);
            }

            return true;
        }
    }

    /// <summary>The Constraint-prefix state of one executable body, shared by its directive targets and arms (SPEC 7.5, 19.4).</summary>
    /// <param name="function">The function whose body this is, if any.</param>
    /// <param name="originOwner">The declaration that owns leading Origin relations, if not the function.</param>
    private struct ExecutableItemState(FunctionKoto? function, Koto? originOwner)
    {
        public readonly FunctionKoto? Function = function;

        public readonly Koto? OriginOwner = originOwner;

        public bool SeenExecutableItem;
    }
}
