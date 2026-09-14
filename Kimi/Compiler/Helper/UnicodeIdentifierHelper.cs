// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;

namespace Kimi.Compiler.Helper;

internal static class UnicodeIdentifierHelper
{
    internal static bool IsUppercase(ReadOnlySpan<char> text)
        => Rune.DecodeFromUtf16(text, out var rune, out _) == OperationStatus.Done && (GetProperties(rune.Value) & 4096) != 0;

    internal static bool IsValid(ReadOnlySpan<char> text)
    {
        var index = 0;
        var previousClass = 0;
        var needsNormalization = false;
        while (index < text.Length)
        {
            if (Rune.DecodeFromUtf16(text[index..], out var rune, out var count) != OperationStatus.Done)
            {
                return false;
            }

            var properties = GetProperties(rune.Value);
            if (rune.Value == '_')
            {
                properties |= 1;
            }

            if ((properties & (index == 0 ? 1 : 2)) == 0 || (properties & 1024) != 0)
            {
                return false;
            }

            var combiningClass = (properties >> 2) & 255;
            if (combiningClass != 0 && previousClass > combiningClass)
            {
                return false;
            }

            previousClass = combiningClass;
            needsNormalization |= (properties & 2048) != 0;
            index += count;
        }

        return !needsNormalization || IsNormalized(text);
    }

    private static int GetProperties(int scalar)
    {
        var data = UnicodeIdentifierData.Properties;
        var low = 0;
        var high = (data.Length / 3) - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var offset = middle * 3;
            if (scalar < data[offset])
            {
                high = middle - 1;
            }
            else if (scalar > data[offset + 1])
            {
                low = middle + 1;
            }
            else
            {
                return (int)data[offset + 2];
            }
        }

        return 0;
    }

    private static bool IsNormalized(ReadOnlySpan<char> text)
    {
        // Canonical decomposition expands a scalar to at most four scalars.
        int[]? rented = null;
        var capacity = checked(text.Length * 4);
        Span<int> buffer = capacity <= 256 ? stackalloc int[256] : (rented = ArrayPool<int>.Shared.Rent(capacity));
        try
        {
            var count = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                Decompose(rune.Value, buffer, ref count);
            }

            var written = 1;
            var starter = 0;
            var lastClass = 0;
            for (var i = 1; i < count; i++)
            {
                var scalar = buffer[i];
                var combiningClass = (GetProperties(scalar) >> 2) & 255;
                var composed = Compose(buffer[starter], scalar);
                if (composed != 0 && (lastClass == 0 || lastClass < combiningClass))
                {
                    buffer[starter] = composed;
                }
                else
                {
                    if (combiningClass == 0)
                    {
                        starter = written;
                    }

                    buffer[written++] = scalar;
                    lastClass = combiningClass;
                }
            }

            var index = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (index >= written || buffer[index++] != rune.Value)
                {
                    return false;
                }
            }

            return index == written;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        }
    }

    private static void Decompose(int scalar, Span<int> buffer, ref int count)
    {
        var syllable = scalar - 0xAC00;
        if ((uint)syllable < 11172)
        {
            Append(0x1100 + (syllable / 588), buffer, ref count);
            Append(0x1161 + ((syllable % 588) / 28), buffer, ref count);
            if (syllable % 28 != 0)
            {
                Append(0x11A7 + (syllable % 28), buffer, ref count);
            }

            return;
        }

        var data = UnicodeIdentifierData.Decompositions;
        var low = 0;
        var high = (data.Length / 3) - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var offset = middle * 3;
            if (scalar < data[offset])
            {
                high = middle - 1;
            }
            else if (scalar > data[offset])
            {
                low = middle + 1;
            }
            else
            {
                Decompose((int)data[offset + 1], buffer, ref count);
                if (data[offset + 2] != 0)
                {
                    Decompose((int)data[offset + 2], buffer, ref count);
                }

                return;
            }
        }

        Append(scalar, buffer, ref count);
    }

    private static void Append(int scalar, Span<int> buffer, ref int count)
    {
        var combiningClass = (GetProperties(scalar) >> 2) & 255;
        var index = count++;
        if (combiningClass != 0)
        {
            while (index > 0 && ((GetProperties(buffer[index - 1]) >> 2) & 255) > combiningClass)
            {
                buffer[index] = buffer[index - 1];
                index--;
            }
        }

        buffer[index] = scalar;
    }

    private static int Compose(int first, int second)
    {
        if ((uint)(first - 0x1100) < 19 && (uint)(second - 0x1161) < 21)
        {
            return 0xAC00 + (((((first - 0x1100) * 21) + second) - 0x1161) * 28);
        }

        if ((uint)(first - 0xAC00) < 11172 && (first - 0xAC00) % 28 == 0 && (uint)(second - 0x11A8) < 27)
        {
            return (first + second) - 0x11A7;
        }

        var key = ((ulong)first << 21) | (uint)second;
        var data = UnicodeIdentifierData.Compositions;
        var low = 0;
        var high = data.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var entry = data[middle];
            var pair = entry >> 21;
            if (key < pair)
            {
                high = middle - 1;
            }
            else if (key > pair)
            {
                low = middle + 1;
            }
            else
            {
                return (int)(entry & 0x1FFFFF);
            }
        }

        return 0;
    }
}
