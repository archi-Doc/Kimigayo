// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Parser records beside their construct.

// Backslash escapes and character references (CommonMark 2.4, 2.5): decoded as text nodes in inline content, and inside link
// destinations, titles and info strings by Decode.
internal ref partial struct DocumentationMarkdownParser
{
    private static readonly SearchValues<char> DecodeCharacters = SearchValues.Create("\\&\0");

    // A backslash before ASCII punctuation is that character; before a line ending it is a hard break.
    private int ParseEscape(int parent, ReadOnlySpan<char> input, int position)
    {
        if (position + 1 < input.Length && input[position + 1] == '\n')
        {
            this.AddNode(DocumentationMarkdownKind.HardBreak, parent, this.MapStart(position), this.MapEnd(position + 2));
            return position + 2;
        }

        return IsBackslashEscape(input, position) ? this.AddDecoded(parent, position, position + 2, input[position + 1]) : position;
    }

    private int ParseCharacterReference(int parent, ReadOnlySpan<char> input, int position)
    {
        return TryCharacterReference(input[position..], out var reference)
            ? this.AddDecoded(parent, position, position + reference.Length, reference.Scalar)
            : position;
    }

    // Whether the backslash at position escapes the next character.
    private static bool IsBackslashEscape(ReadOnlySpan<char> input, int position)
        => input[position] == '\\' && position + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[position + 1]);

    // A value without escapes or references is a source slice until a string is needed.
    private MarkdownValue DecodeValue(ReadOnlySpan<char> input, int start, int end)
    {
        return input[start..end].IndexOfAny(DecodeCharacters) < 0 ? this.SliceValue(input, start, end) : new(this.Decode(input[start..end]));
    }

    // Decoded text is never longer than its source, so pooled scratch suffices.
    private string Decode(ReadOnlySpan<char> input)
    {
        var special = input.IndexOfAny(DecodeCharacters);
        if (special < 0)
        {
            return input.ToString();
        }

        EnsureBuffer(ref this.decodeScratch, input.Length);
        var output = this.decodeScratch.AsSpan();
        input[..special].CopyTo(output);
        var written = special;
        for (var i = special; i < input.Length; i++)
        {
            if (IsBackslashEscape(input, i))
            {
                output[written++] = input[++i];
            }
            else if (input[i] == '&' && TryCharacterReference(input[i..], out var reference))
            {
                if (reference.Scalar <= char.MaxValue)
                {
                    output[written++] = (char)reference.Scalar;
                }
                else
                {
                    output[written++] = (char)(0xD800 + ((reference.Scalar - 0x10000) >> 10));
                    output[written++] = (char)(0xDC00 + ((reference.Scalar - 0x10000) & 1023));
                }

                i += reference.Length - 1;
            }
            else
            {
                output[written++] = input[i] == '\0' ? '\uFFFD' : input[i];
            }
        }

        return new string(output[..written]);
    }

    // A reference at the start of the input: a named entity of the profile, or a decimal or hexadecimal numeric reference.
    private static bool TryCharacterReference(ReadOnlySpan<char> input, out CharacterReference reference)
    {
        reference = default;
        if (input.Length < 3)
        {
            return false;
        }

        reference = input[1] == '#' ? NumericReference(input) : NamedReference(input);
        return reference.Length != 0;
    }

    private static CharacterReference NamedReference(ReadOnlySpan<char> input)
    {
        var semicolon = input[..Math.Min(input.Length, 6)].IndexOf(';');
        if (semicolon < 0)
        {
            return default;
        }

        int scalar = input[1..semicolon] switch
        {
            "amp" => '&',
            "lt" => '<',
            "gt" => '>',
            "quot" => '"',
            "apos" => '\'',
            _ => 0,
        };
        return scalar == 0 ? default : new(scalar, semicolon + 1);
    }

    // A numeric reference to an invalid or surrogate code point, or to U+0000, stands for U+FFFD.
    private static CharacterReference NumericReference(ReadOnlySpan<char> input)
    {
        var position = 2;
        var hex = input[position] is 'x' or 'X';
        if (hex)
        {
            position++;
        }

        var first = position;
        var maximum = hex ? 6 : 7;
        var scalar = 0;
        while (position < input.Length && position - first < maximum)
        {
            var digit = input[position] switch
            {
                >= '0' and <= '9' => input[position] - '0',
                >= 'a' and <= 'f' when hex => input[position] - 'a' + 10,
                >= 'A' and <= 'F' when hex => input[position] - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
            {
                break;
            }

            scalar = (scalar * (hex ? 16 : 10)) + digit;
            position++;
        }

        if (position == first || position == input.Length || input[position] != ';')
        {
            return default;
        }

        if (scalar == 0 || scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF)
        {
            scalar = 0xFFFD;
        }

        return new(scalar, position + 1);
    }

    // A decoded reference: its scalar value and the length of its source text (0 when none).
    private readonly record struct CharacterReference(int Scalar, int Length);
}
