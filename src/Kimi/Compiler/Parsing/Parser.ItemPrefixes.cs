// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

#pragma warning disable SA1202 // Parsing helpers are grouped by syntax.

// What comes before an item: attributes (SPEC 6.7), declaration modifiers (Appendix F.3.3) and the compile-time directives before it.
// Input: a reader at an item's start. Output: the pending prefix in the reader, which the item takes (TokenReader.TakeContext), and where
// its written header starts (TokenReader.HeaderPosition); whether an unavailable declaration header was recognized, which its caller skips;
// an attribute chain's nodes.
// Invariants: a modifier shares the line of the word that introduces its declaration, and modifiers that end their line are dropped with one
// report; a static, and an open anywhere but before struct, is reported where it is read and kept only for recovery (Judged); the prefix's
// attributes attach only to the declaration that takes the prefix, and a misplaced or rejected attribute attaches to nothing.
// Prohibitions: a directive's selection belongs to Parser.Directives; this part parses no item.
public static partial class Parser
{
    /// <summary>The words of a declaration header line (Appendix F.6), per token kind (<see cref="HeaderWords"/>).</summary>
    private enum HeaderWord : byte
    {
        /// <summary>No header word: the line is no modifier header.</summary>
        None,

        /// <summary>An access word, <c>open</c> or <c>static</c>.</summary>
        Modifier,

        /// <summary>A word that introduces a declaration.</summary>
        DeclarationIntroducer,

        /// <summary>A word that introduces an accessor (<c>get</c>, <c>set</c>).</summary>
        AccessorIntroducer,
    }

    /// <summary>What a header line is (<see cref="ScanHeaderModifiers"/>).</summary>
    private enum HeaderScan
    {
        /// <summary>No modifier header: its words, if any, are ordinary Names and the item reads it.</summary>
        Ordinary,

        /// <summary>A function header whose <c>virtual</c>, <c>override</c> and <c>unsafe</c> words are modifiers.</summary>
        FunctionModifiers,

        /// <summary>An unavailable modifier or a dispatch modifier off a function was reported; the reader stands at the introducer.</summary>
        Reported,
    }

    /// <summary>Reports the pending attributes before an item whose production takes none (SPEC 6.7, Appendix F), such as a Contract
    /// item, a <c>drop</c>, an override or a Constraint; each attaches to nothing, as a misplaced attribute does, so no node holds an
    /// attribute its production does not take.</summary>
    /// <param name="reader">The token reader.</param>
    internal static void ReportPendingAttributes(ref TokenReader reader)
    {
        for (var attribute = reader.AttributeKoto; attribute is not null; attribute = attribute.AttributeChain)
        {
            reader.Unexpected(SyntaxForm.Attribute, attribute.Span);
        }

        reader.AttributeKoto = null;
    }

    /// <summary>Rejects the pending access word before an item whose production has no Access (Appendix F; SPEC 6.1.1, 9.3), such as
    /// an alias, a root function other than <c>public main</c>, a local function or binding, a Contract requirement, a <c>drop</c>, a
    /// Case, an <c>associate</c>, a specialization, or a Constraint, Origin relation or effect clause. The written word is misplaced
    /// where it stands (the first one of a two-word form), once, and is dropped: nothing is recovered, so no node holds an access word
    /// its production does not take and no later check judges it. Called before the item takes the prefix.</summary>
    /// <param name="reader">The token reader.</param>
    internal static void RejectAccessWord(ref TokenReader reader)
    {
        var access = reader.ModifierKind.ExtractAccessibilityModifiers();
        if (access != ModifierKind.NoModifier)
        {
            reader.Unexpected(SyntaxForm.Decoration, reader.AccessModifierSpan);
            reader.ModifierKind ^= access;
            reader.AccessModifierSpan = default;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ModifierKind GetAccessibilityModifier(TokenKind tokenKind)
        => tokenKind switch
        {
            TokenKind.Public => ModifierKind.Public,
            TokenKind.Protected => ModifierKind.Protected,
            TokenKind.Private => ModifierKind.Private,
            TokenKind.Internal => ModifierKind.Internal,
            _ => ModifierKind.NoModifier,
        };

    /// <summary>Gets the modifiers a declaration check judges: each 'static', and an 'open' anywhere but directly before 'struct', is
    /// reported where it is read and kept only for recovery (SPEC 6.1, 6.2.2), so no declaration check reports it again.</summary>
    /// <param name="kind">The written modifiers.</param>
    /// <returns>The modifiers other than static and open.</returns>
    internal static ModifierKind Judged(this ModifierKind kind)
        => kind & ~(ModifierKind.Static | ModifierKind.Open);

    /// <summary>Gets the access part of a modifier set, its low four bits (public through private protected).</summary>
    /// <param name="kind">The modifier set.</param>
    /// <returns>The access modifiers.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ModifierKind ExtractAccessibilityModifiers(this ModifierKind kind)
        => kind & (ModifierKind)15;

    /// <summary>Consumes attributes, modifiers, and optional compile-time directives before a declaration.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="isEnd">Whether the declaration sequence has ended.</param>
    /// <param name="allowCompileTimeDirectives">Whether lowercase compile-time directives are accepted.</param>
    /// <param name="stopAtCaseGroupBoundary">Whether invalid Case Group items must leave the next arm or list end for its owner.</param>
    /// <returns>Whether an unavailable declaration header was recognized and needs recovery.</returns>
    public static bool ConsumeAttributeAndModifier(
        ref TokenReader reader,
        out bool isEnd,
        bool allowCompileTimeDirectives = false,
        bool stopAtCaseGroupBoundary = false)
    {
        reader.ClearContext();

        var inspectHeader = true;
        var functionModifiers = false;
        var openSpan = default(SourceSpan);
        while (reader.CanRead)
        {
            if (stopAtCaseGroupBoundary && (reader.CurrentTokenKind == TokenKind.EndBlock || IsCompileTimeCaseStart(ref reader)))
            {
                // A misplaced prefix cannot own an arm. Its placement already failed; do not invent a missing target or
                // reinterpret this valid arm as an orphan #case while source-checking the preceding invalid segment.
                reader.ClearContext();
                isEnd = true;
                return false;
            }

            if (inspectHeader)
            {
                // A line of the prefix, or what follows a `#` form, may start the header: the item's is the last one (Appendix F.6).
                reader.HeaderPosition = reader.Position;
                if (allowCompileTimeDirectives)
                {
                    var header = ScanHeaderModifiers(ref reader, HeaderWord.DeclarationIntroducer);
                    if (header == HeaderScan.Reported)
                    {
                        isEnd = false;
                        return true;
                    }

                    functionModifiers = header == HeaderScan.FunctionModifiers;
                }
            }

            inspectHeader = false;
            var tokenKind = reader.CurrentTokenKind;
            if (functionModifiers && tokenKind == TokenKind.Identifier)
            {
                var flag = reader.GetSpan(reader.CurrentToken) switch
                {
                    "virtual" => ModifierKind.Virtual,
                    "override" => ModifierKind.Override,
                    Constants.UnsafeKeyword => ModifierKind.Unsafe,
                    _ => ModifierKind.NoModifier,
                };
                if (flag != ModifierKind.NoModifier)
                {
                    if ((flag & (ModifierKind.Virtual | ModifierKind.Override)) != 0 && reader.DispatchModifierSpan == default)
                    {
                        reader.DispatchModifierSpan = reader.CurrentTokenRange;
                    }

                    // F.3.3: the word that writes a second function modifier, which the function header reports.
                    if ((reader.ModifierKind & ~flag & (ModifierKind.Unsafe | ModifierKind.Virtual | ModifierKind.Override)) != 0 &&
                        reader.SecondModifierSpan == default)
                    {
                        reader.SecondModifierSpan = reader.CurrentTokenRange;
                    }

                    ReadModifierFlag(ref reader, flag);
                    continue;
                }
            }

            if (tokenKind == TokenKind.Identifier && reader.PeekKind(1) == TokenKind.Func && reader.IsCurrentIdentifier(Constants.UnsafeKeyword))
            {
                ReadModifierFlag(ref reader, ModifierKind.Unsafe);
                continue;
            }

            switch (tokenKind)
            {
                case TokenKind.Separator:
                    EndModifierLine(ref reader);
                    inspectHeader = true;
                    continue;

                case TokenKind.Static:
                    // static is not a declaration modifier; each is reported here and kept only for recovery (SPEC 6.1), so
                    // neither a repetition nor a declaration check reports it again (Judged).
                    reader.Unexpected(SyntaxForm.StaticModifier);
                    reader.ModifierKind |= ModifierKind.Static;
                    reader.Advance();
                    continue;

                case TokenKind.Open:
                    openSpan = reader.CurrentTokenRange;
                    ReadModifierFlag(ref reader, ModifierKind.Open);
                    continue;

                case TokenKind.Public:
                case TokenKind.Protected:
                case TokenKind.Private:
                case TokenKind.Internal:
                    ReadAccessibilityModifier(ref reader, GetAccessibilityModifier(tokenKind));
                    continue;

                case TokenKind.Sharp:
                    inspectHeader = true;
                    if (ConsumeSharpPrefix(ref reader, allowCompileTimeDirectives))
                    {
                        continue;
                    }

                    isEnd = false;
                    return false;

                default:
                    if (tokenKind == TokenKind.EndBlock)
                    {
                        ReportDanglingHeader(ref reader);
                    }
                    else if (reader.ModifierKind.HasFlag(ModifierKind.Open) && (tokenKind != TokenKind.Struct || reader.PeekKind(-1) != TokenKind.Open))
                    {
                        // open applies only to structures and immediately precedes struct (SPEC 6.2.2, F.6). It is reported here
                        // only, so the declaration's own checks do not judge it again (Judged).
                        reader.Unexpected(SyntaxForm.OpenModifier, openSpan);
                    }

                    isEnd = false;
                    return false;
            }
        }

        ReportDanglingHeader(ref reader);
        isEnd = true;
        return false;
    }

    /// <summary>Ends a line of the item prefix. Modifiers share the line of the word that introduces their declaration (Appendix F.6), so
    /// modifiers that end their line introduce nothing: the declaration is missing after them and they are dropped, while attributes before
    /// them still precede the next declaration. A static, already reported, is no modifier that needs one. An indented body after them
    /// would be the missing declaration's, so it is skipped with the header, as after a header that fails (OmitDeclaration).</summary>
    /// <param name="reader">The token reader at the line's Separator.</param>
    private static void EndModifierLine(ref TokenReader reader)
    {
        if (reader.ModifierKind != ModifierKind.NoModifier)
        {
            if ((reader.ModifierKind & ~ModifierKind.Static) != ModifierKind.NoModifier)
            {
                var cause = reader.Expect(SyntaxForm.Declaration);
                if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    OmitDeclaration(ref reader, cause);
                    return;
                }
            }

            reader.ModifierKind = ModifierKind.NoModifier;
            reader.DispatchModifierSpan = default;
            reader.AccessModifierSpan = default;
            reader.SecondModifierSpan = default;
        }

        reader.Advance();
    }

    /// <summary>Consumes a <c>#</c> form of the item prefix: a compile-time directive prefix (Appendix F.11) or an attribute (F.6.1).</summary>
    /// <param name="reader">The token reader at <c>#</c>.</param>
    /// <param name="allowCompileTimeDirectives">Whether lowercase compile-time directives are accepted.</param>
    /// <returns><see langword="false"/> when a <c>#switch</c> starts: it is the item itself; otherwise the prefix continues.</returns>
    private static bool ConsumeSharpPrefix(ref TokenReader reader, bool allowCompileTimeDirectives)
    {
        if (reader.ModifierKind != ModifierKind.NoModifier)
        {
            if (reader.PeekKind(1) is not (TokenKind.If or TokenKind.Switch or TokenKind.Case))
            {
                // Attributes precede a declaration's modifiers (Appendix F.6): one after them is misplaced, and it is kept for the
                // declaration as that Error's recovery.
                reader.ReportMisplacedAttributes();
                return true;
            }

            // A directive is an item of its own (Appendix F.11): the modifiers before it introduce nothing.
            if ((reader.ModifierKind & ~ModifierKind.Static) != ModifierKind.NoModifier)
            {
                reader.Expect(SyntaxForm.Declaration);
            }

            reader.ModifierKind = ModifierKind.NoModifier;
            reader.DispatchModifierSpan = default;
            reader.AccessModifierSpan = default;
            reader.SecondModifierSpan = default;
        }

        if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.If)
        {
            ParseCompileTimeIfPrefix(ref reader);
            reader.HasCompileTimeIfPrefix = true;
            return true;
        }

        if (allowCompileTimeDirectives && reader.PeekKind(1) == TokenKind.Switch)
        {
            return false;
        }

        if (reader.PeekKind(1) == TokenKind.Case)
        {
            if (allowCompileTimeDirectives)
            {
                // Its Condition is validated and its body is parsed as excluded syntax (SPEC 19.4).
                reader.HasCompileTimeIfPrefix |= ParseOrphanCompileTimeCase(ref reader);
                return true;
            }

            reader.AddDiagnostic(DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd);
            SkipItemForRecovery(ref reader);
            reader.ClearContext();
            return true;
        }

        reader.AttributeKoto = ParseAttributeKoto(ref reader, reader.AttributeKoto);
        return true;
    }

    // Attributes, modifiers or a compile-time prefix at the end of a body or of the source introduce no declaration (SPEC 6.7): the
    // declaration is missing after them, and the pending header is dropped.
    private static void ReportDanglingHeader(ref TokenReader reader)
    {
        if (reader.HasCompileTimeIfPrefix || reader.AttributeKoto is not null || reader.ModifierKind != ModifierKind.NoModifier)
        {
            reader.Expect(SyntaxForm.Declaration);
            reader.ClearContext();
        }
    }

    private static void ReadModifierFlag(ref TokenReader reader, ModifierKind flag)
    {
        if (reader.ModifierKind.HasFlag(flag))
        {
            reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, reader.GetSpan(reader.CurrentToken).ToString());
        }

        reader.ModifierKind |= flag;
        reader.Advance();
    }

    private static void ReadAccessibilityModifier(ref TokenReader reader, ModifierKind kind)
    {
        var acc = reader.ModifierKind.ExtractAccessibilityModifiers();
        if (acc == default)
        {
            reader.ModifierKind |= kind;
            reader.AccessModifierSpan = reader.CurrentTokenRange;
        }
        else if ((acc == ModifierKind.Protected && kind == ModifierKind.Internal) || (acc == ModifierKind.Private && kind == ModifierKind.Protected))
        {
            reader.ModifierKind = (reader.ModifierKind ^ acc) |
                (acc == ModifierKind.Protected ? ModifierKind.ProtectedOrInternal : ModifierKind.ProtectedAndInternal);
        }
        else if (acc == kind)
        {
            reader.AddDiagnostic(DiagnosticCode.DuplicateModifier_Kd, kind.ToText());
        }
        else
        {
            reader.AddDiagnostic(DiagnosticCode.MultipleAccessibilityModifiers_Kd);
        }

        reader.Advance();
    }

    /// <summary>Scans a header line for modifier words before its introducer (Appendix F.6): a word among <c>unsafe</c>,
    /// <c>virtual</c>, <c>override</c> and <c>abstract</c> is a modifier only when access words, <c>open</c>, <c>static</c> and such
    /// words follow it on the same line before an introducer; elsewhere these spellings are ordinary Names, in calls and declaration
    /// Names too. The reader is not advanced unless the header is reported.</summary>
    /// <param name="reader">The token reader at the start of the header.</param>
    /// <param name="introducer">The introducers the header admits: a declaration's or an accessor's.</param>
    /// <returns>What the line is; for <see cref="HeaderScan.Reported"/> the reader stands at the introducer.</returns>
    private static HeaderScan ScanHeaderModifiers(ref TokenReader reader, HeaderWord introducer)
    {
        var unavailable = default(Token);
        var dispatch = default(Token);
        var previousEnd = reader.CurrentTokenRange.Start;
        for (var offset = 0; offset < reader.Remaining; offset++)
        {
            var token = reader.PeekToken(offset);
            if (!reader.SameLine(previousEnd, token.Span.Start))
            {
                return HeaderScan.Ordinary;
            }

            previousEnd = token.Span.End;
            if (token.Kind == TokenKind.Identifier)
            {
                var text = reader.GetSpan(token);
                if (text is "abstract")
                {
                    if (unavailable.Kind == TokenKind.Invalid)
                    {
                        unavailable = token;
                    }

                    continue;
                }

                if (text is "virtual" or "override")
                {
                    if (dispatch.Kind == TokenKind.Invalid)
                    {
                        dispatch = token;
                    }

                    continue;
                }

                // A specialization's `specialize` precedes `func`, which no accessor list admits.
                if (text is Constants.UnsafeKeyword || (text is "specialize" && reader.PeekKind(offset + 1) == TokenKind.Func))
                {
                    continue;
                }
            }

            var word = HeaderWords[(byte)token.Kind];
            if (word == HeaderWord.Modifier)
            {
                continue;
            }

            if (word != introducer)
            {
                return HeaderScan.Ordinary;
            }

            if (unavailable.Kind != TokenKind.Invalid)
            {
                reader.Diagnostic.Add(unavailable.Span, DiagnosticCode.UnavailableFeature_Kd, reader.GetSpan(unavailable).ToString());
            }
            else if (token.Kind == TokenKind.Func)
            {
                return HeaderScan.FunctionModifiers;
            }
            else if (dispatch.Kind != TokenKind.Invalid)
            {
                reader.Unexpected(SyntaxForm.VirtualModifier, dispatch.Span);
            }
            else
            {
                return HeaderScan.Ordinary;
            }

            reader.Advance(offset);
            return HeaderScan.Reported;
        }

        return HeaderScan.Ordinary;
    }

    /// <summary>Parses an attribute expression and puts it before the attributes read so far.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="previous">The attributes read before it, last first.</param>
    /// <param name="beforeList">Whether the attribute is misplaced where the grammar expects <c>(</c>, such as before a parameter
    /// list. SPEC 6.7 sets no adjacency rule for the argument list, so there the attribute takes a list only when another
    /// <c>(</c> follows it: the last list is the one the grammar expects, as in <c>func f #Test () => ()</c>.</param>
    /// <returns>The attributes with the parsed one first, or <paramref name="previous"/> after an error: a malformed attribute
    /// attaches to nothing.</returns>
    public static AttributeKoto? ParseAttributeKoto(ref TokenReader reader, AttributeKoto? previous, bool beforeList = false)
    {
        reader.TryRead(out var attributeToken);

        var nameToken = reader.CurrentToken;
        var wellFormed = nameToken.Kind.IsIdentifierOrContextualKeyword() && IdentifierHelper.IsUppercase(reader.GetSpan(nameToken));
        if (!wellFormed && nameToken.Kind.IsKeyword())
        {
            if (nameToken.Kind is TokenKind.If or TokenKind.Switch or TokenKind.Case)
            {
                // A directive selects whole items (SPEC 19.1), so here it selects nothing: it is one problem, its Condition is read
                // and set aside, and what follows it is parsed in place.
                reader.Unexpected(SyntaxForm.CompileTimeDirective, SourceSpan.FromBounds(attributeToken.Span.Start, nameToken.Span.End));
                reader.Advance();
                if (nameToken.Kind != TokenKind.Switch && !reader.TryConsume(TokenKind.Underscore) && !IsExpressionBoundary(ref reader))
                {
                    _ = ParseRequiredCompileTimeCondition(ref reader);
                }
            }
            else
            {
                // A reserved word is no attribute Name, and the construct it would start is not read: `#match` or `#for` is one
                // problem at the word, and what follows it on the line is parsed in place. An indented body after a word that ends
                // its line would be that construct's, so it is skipped with it.
                reader.Expect(SyntaxForm.AttributeName);
                reader.Advance();
                if (reader.CurrentTokenKind == TokenKind.Separator && reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
                {
                    reader.SkipCurrentBlock();
                }
            }

            return previous;
        }

        if (!wellFormed)
        {
            reader.Expect(SyntaxForm.AttributeName);
        }

        var operand = ParsePrimaryExpression(ref reader);
        if (reader.CurrentTokenKind is TokenKind.Dot or TokenKind.LessThan or TokenKind.OpenBracket)
        {
            reader.Unexpected(SyntaxForm.AttributeSuffix);
            wellFormed = false;
        }

        while ((!beforeList || reader.CurrentTokenKind != TokenKind.OpenParenthesis || IsListFollowedByList(ref reader)) &&
            TryParsePostfixExpression(ref reader, ref operand))
        {
        }

        if (!wellFormed)
        {
            // A malformed attribute attaches to nothing: its syntax Error explains it, and the declaration it precedes is checked on its own.
            return previous;
        }

        var attributeKoto = new AttributeKoto(
            ref reader,
            SourceSpan.FromBounds(attributeToken.Span.Start, Math.Max(attributeToken.Span.End, operand.Span.End)),
            operand);
        attributeKoto.SetAttributeChain(previous);
        reader.CodeContext.Documentation?.Suppress(attributeKoto.Span);
        return attributeKoto;
    }

    // Whether the parenthesized list at the current token is directly followed by another one. Every list is closed: the
    // tokenizer supplies a missing closer.
    private static bool IsListFollowedByList(ref TokenReader reader)
    {
        var depth = 0;
        for (var offset = 0; ; offset++)
        {
            switch (reader.PeekKind(offset))
            {
                case TokenKind.OpenParenthesis:
                    depth++;
                    break;
                case TokenKind.CloseParenthesis:
                    if (--depth == 0)
                    {
                        return reader.PeekKind(offset + 1) == TokenKind.OpenParenthesis;
                    }

                    break;
                case TokenKind.Invalid:
                    return false;
            }
        }
    }
}
