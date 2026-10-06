// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Diagnostics;

/// <summary>
/// The limits of explanations, in one place (SPEC 23.3.6.5). They apply per record and never change a problem's identity,
/// category, survival, location or the acceptance of its result.
/// </summary>
internal static class DiagnosticLimits
{
    /// <summary>The lines of an excerpt: the first ones and the last one are kept.</summary>
    internal const int ExcerptLines = 4;

    /// <summary>The display width of an excerpt line; a longer line is clipped around its underline.</summary>
    internal const int ExcerptWidth = 160;

    /// <summary>The columns kept before the underline when an excerpt line is clipped.</summary>
    internal const int ExcerptLead = 40;

    /// <summary>The characters of one displayed Type, Constraint or name.</summary>
    internal const int ValueLength = 96;

    /// <summary>The characters of a Note formed from facts, such as the text of an input failure.</summary>
    internal const int NoteLength = 320;

    /// <summary>The related locations of one record.</summary>
    internal const int Related = 8;

    /// <summary>The repair candidates of one record; later candidates are omitted whole (SPEC 23.3.6.9).</summary>
    internal const int Repairs = 4;

    /// <summary>The edits of one repair candidate; a candidate with more is omitted whole, never truncated.</summary>
    internal const int RepairEdits = 64;

    /// <summary>The characters of replacement text in one repair candidate; a candidate with more is omitted whole.</summary>
    internal const int RepairText = 2048;
}

/// <summary>The one bounded form of displayed Types, Constraints and names (SPEC 23.3.6.5).</summary>
internal static class DiagnosticText
{
    /// <summary>The elision mark.</summary>
    internal const string Elision = "…";

    /// <summary>Bounds one display value, keeping its head and tail around an elision mark.</summary>
    /// <param name="text">The full text.</param>
    /// <param name="limit">The characters kept, including the elision mark.</param>
    /// <returns>The bounded text and whether part of it was elided.</returns>
    internal static (string Text, bool Elided) Bound(string text, int limit = DiagnosticLimits.ValueLength)
    {
        if (text.Length <= limit)
        {
            return (text, false);
        }

        var head = (limit * 2) / 3;
        var tail = limit - head - Elision.Length;
        return (string.Concat(text.AsSpan(0, HeadLength(text, head)), Elision, text.AsSpan(TailStart(text, text.Length - tail))), true);
    }

    /// <summary>Shortens a kept head so that it never ends inside a surrogate pair; bounded text stays valid UTF-16.</summary>
    /// <param name="text">The text.</param>
    /// <param name="length">The intended head length.</param>
    /// <returns>The head length.</returns>
    internal static int HeadLength(string text, int length)
        => length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]) ? length - 1 : length;

    /// <summary>Moves the start of a kept tail so that it never begins inside a surrogate pair.</summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The intended tail start.</param>
    /// <returns>The tail start.</returns>
    internal static int TailStart(string text, int start)
        => start > 0 && start < text.Length && char.IsHighSurrogate(text[start - 1]) && char.IsLowSurrogate(text[start]) ? start + 1 : start;

    /// <summary>Bounds two Types of one mismatch: when either is too long, their common head and tail are elided so the
    /// differing parts stay visible, and each is then bounded.</summary>
    /// <param name="first">The first Type.</param>
    /// <param name="second">The second Type.</param>
    /// <returns>The bounded texts and whether each was elided.</returns>
    internal static ((string Text, bool Elided) First, (string Text, bool Elided) Second) BoundPair(string first, string second)
    {
        if (first.Length <= DiagnosticLimits.ValueLength && second.Length <= DiagnosticLimits.ValueLength)
        {
            return ((first, false), (second, false));
        }

        var shorter = Math.Min(first.Length, second.Length);
        var prefix = first.AsSpan(0, shorter).CommonPrefixLength(second.AsSpan(0, shorter));
        var suffix = 0;
        while (suffix < shorter - prefix && first[first.Length - 1 - suffix] == second[second.Length - 1 - suffix])
        {
            suffix++;
        }

        // Do not trim the shared 'i' from i32/i64, or the shared name from Kimi.Start/Start. The differing Types must
        // remain readable as names, rather than isolated digits or qualification punctuation.
        while (prefix > 0 && NameCharacter(first[prefix - 1]))
        {
            prefix--;
        }

        while (suffix > 0 && NameCharacter(first[first.Length - suffix]))
        {
            suffix--;
        }

        // Nor split a surrogate pair between a common part and a differing one.
        if (prefix > 0 && char.IsHighSurrogate(first[prefix - 1]))
        {
            prefix--;
        }

        if (suffix > 0 && char.IsLowSurrogate(first[first.Length - suffix]))
        {
            suffix--;
        }

        // A Type that lies wholly inside the common head and tail, as T in ref/T, has no differing part of its own; eliding the common
        // parts would show it as a bare elision mark, so each Type is then bounded on its own.
        if (prefix + suffix >= shorter)
        {
            prefix = 0;
            suffix = 0;
        }

        var common = prefix != 0 || suffix != 0;
        var (firstText, firstElided) = Bound(Differing(first, prefix, suffix));
        var (secondText, secondElided) = Bound(Differing(second, prefix, suffix));
        return ((firstText, common || firstElided), (secondText, common || secondElided));

        static string Differing(string text, int prefix, int suffix)
            => prefix == 0 && suffix == 0 ? text :
                string.Concat(prefix == 0 ? string.Empty : Elision, text.AsSpan(prefix, text.Length - prefix - suffix), suffix == 0 ? string.Empty : Elision);

        static bool NameCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';
    }
}
