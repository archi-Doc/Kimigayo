// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// What a failure skips or keeps, after the Error is reported: a list item before skipped text (RecoverItem), a written Type outside its
// position's grammar (RecoverWrittenType), and a declaration no header introduces, with its line and body (OmitDeclaration,
// SkipDeclarationLine, SkipUnavailableDeclaration).
// Input: a reader at the failure and the key of the Error. Output: a recovery or omission record, so a check over the item, the Type or the
// member set rests on the Error (DIAGNOSTICS.md §4.3, §4.4); the reader at the line boundary, with the pending header cleared.
// Invariants: it reports nothing, and excluded syntax records no omission (SPEC 19.4).
// Prohibitions: skipping an item that may be a directive is SkipItemForRecovery's, in Parser.Directives, which reads directive syntax.
public static partial class Parser
{
    internal static void SkipUnavailableDeclaration(ref TokenReader reader)
    {
        SkipItemForRecovery(ref reader);
        reader.ClearContext();
    }

    /// <summary>Skips the rest of a declaration's line and the indented body it would have introduced, then clears the pending header.</summary>
    /// <param name="reader">The token reader.</param>
    internal static void SkipDeclarationLine(ref TokenReader reader)
    {
        // The reader stays at the line boundary, so the enclosing item ends there and the next line keeps its own statement.
        if (reader.CurrentTokenKind != TokenKind.StartBlock)
        {
            reader.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
        }

        if (reader.TrySkipSeparatorsTo(TokenKind.StartBlock))
        {
            reader.SkipCurrentBlock();
        }

        reader.ClearContext();
    }

    /// <summary>
    /// Skips a declaration that no header introduces after its Error, with the rest of its line and its body, and records the
    /// omission, so a check over the member set rests on the Error (DIAGNOSTICS.md §4.3, §4.4).
    /// </summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="cause">The key of the Error.</param>
    internal static void OmitDeclaration(ref TokenReader reader, DiagnosticKey? cause)
    {
        if (cause is { } key && !reader.InExcludedSyntax)
        {
            // Excluded syntax records no omission for the selected program (SPEC 19.4).
            reader.CodeContext.Kotonoha.RecordOmission(key);
        }

        SkipDeclarationLine(ref reader);
    }

    // The item before text that a list's recovery skipped is a recovery, as the last argument of a call is (DIAGNOSTICS.md §4.4):
    // what was written there is not known, so every check that reads the item rests on the Error, while the other items keep
    // the context of the list.
    private static void RecoverItem(ref TokenReader reader, Koto item, DiagnosticKey? cause)
    {
        if (cause is { } recovery && item is not ErrorKoto && reader.CodeContext.RecoveryCause(item) is null)
        {
            reader.CodeContext.RecordRecovery(item, recovery);
        }
    }

    // A written Type outside its position's grammar, after its record: a Type after a rejected prefix (RecoverRejectedPrefix); an
    // adaptation target's borrow layer with an Origin or its Core's path headed by a bound Container qualifier (13.5.1); a base or
    // Contract parent that is no InheritanceReference (F.6.1, F.6.2). The one recovery of each such position: the Type stays where it was written and is bound, so
    // every independent failure inside it keeps its record (SPEC 23.1.2); the Type is a recovery, so a check that consults it
    // (IsRecoveredType, Binding's prerequisites) rests on the record. A group that is no PathQualifier (ContinuesGroupedType) is no such
    // Type: it is a complete Type whose end is the grammar's, and the record at the "." is the enclosing construct's.
    private static Koto RecoverWrittenType(ref TokenReader reader, Koto type, DiagnosticKey? cause)
    {
        if (cause is { } key)
        {
            reader.CodeContext.RecordRecovery(type, key);
        }

        return type;
    }

    // A prefix the grammar rejects before a complete Type, after its record: "::" before a group, a fixed array or a second "::"
    // (SPEC F.5), a move or copy prefix (13.5.1), a legacy borrow prefix X{a}/. The written Type is the prefix and the Type: it stays
    // as a transparent layer over the Type, recovered (RecoverWrittenType), so the Type and its parts bind and keep their own records
    // while the prefix's meaning is dropped.
    private static Koto RecoverRejectedPrefix(ref TokenReader reader, int start, Koto type, DiagnosticKey? cause)
        => RecoverWrittenType(ref reader, new TypeSemanticsKoto(ref reader, SourceSpan.FromBounds(start, type.Span.End), type), cause);

    // Whether a Type is an Error or a written Type outside its grammar (RecoverWrittenType): a check of a production over it reads a
    // failed input and adds no record of its own (SPEC 23.1.2).
    private static bool IsRecoveredType(ref TokenReader reader, Koto type)
        => type is ErrorKoto || reader.CodeContext.RecoveryCause(type) is not null;
}
