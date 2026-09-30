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
        return (string.Concat(text.AsSpan(0, head), Elision, text.AsSpan(text.Length - tail)), true);
    }

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
