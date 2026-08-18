// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kimi.Compiler.Helper;

#pragma warning disable SA1611 // Element parameters should be documented
#pragma warning disable SA1615 // Element return value should be documented

/// <summary>
/// Provides utility methods for validating C# identifiers according to the language specification.
/// </summary>
public static class IdentifierHelper
{
    /// <summary>
    /// Determines whether the specified text is a valid identifier according to
    /// the C# identifier character rules.
    /// </summary>
    /// <param name="identifier">The text to validate as an identifier.</param>
    /// <returns><c>true</c> if the text is a valid identifier; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// A valid identifier must start with a letter or underscore, and can contain letters,
    /// digits, underscores, and certain Unicode characters. This method follows the C#
    /// language specification for identifier validation, including support for Unicode
    /// categories and surrogate pairs.
    /// </remarks>
    public static bool IsValidIdentifier(ReadOnlySpan<char> identifier)
    {
        if (identifier.IsEmpty)
        {
            return false;
        }

        var c = identifier[0];
        int index;

        if (c <= 0x7F)
        {
            if (!IsAsciiIdentifierStart(c))
            {
                return false;
            }

            index = 1;
        }
        else
        {
            if (!TryGetUnicodeCategory(identifier, 0, out var category, out var consumed) ||
                !IsIdentifierStartCategory(category))
            {
                return false;
            }

            index = consumed;
        }

        while ((uint)index < (uint)identifier.Length)
        {
            c = identifier[index];

            if (c <= 0x7F)
            {
                if (!IsAsciiIdentifierPart(c))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (!TryGetUnicodeCategory(identifier, index, out var category, out var consumed) ||
                !IsIdentifierPartCategory(category))
            {
                return false;
            }

            index += consumed;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiIdentifierStart(char c)
    {
        var lower = (uint)(c | 0x20);
        return lower - 'a' <= 'z' - 'a' || c == '_';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiIdentifierPart(char c)
    {
        var lower = (uint)(c | 0x20);
        return lower - 'a' <= 'z' - 'a' ||
            (uint)(c - '0') <= 9 ||
            c == '_';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierStartCategory(UnicodeCategory category)
    {
        return category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.LetterNumber;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierPartCategory(UnicodeCategory category)
    {
        return category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.Format;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetUnicodeCategory(ReadOnlySpan<char> text, int index, out UnicodeCategory category, out int consumed)
    {
        var c = text[index];

        if (!char.IsSurrogate(c))
        {
            category = CharUnicodeInfo.GetUnicodeCategory(c);
            consumed = 1;
            return true;
        }

        if (Rune.DecodeFromUtf16(text.Slice(index), out var rune, out consumed) != OperationStatus.Done)
        {
            category = default;
            return false;
        }

        category = Rune.GetUnicodeCategory(rune);
        return true;
    }
}
