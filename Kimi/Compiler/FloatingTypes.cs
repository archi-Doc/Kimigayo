// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Selected-precision literal bits, independent of integer fitting and arithmetic.</summary>
internal static class FloatingTypes
{
    internal static bool Supports(BoundType? type) => ReferenceEquals(type, BoundType.F32) || ReferenceEquals(type, BoundType.F64);

    internal static bool TryLiteral(Koto source, out long bits)
    {
        var number = source is PrefixMinusKoto or PrefixPlusKoto ? ((UnaryKoto)source).Operand as NumberLiteralKoto : source as NumberLiteralKoto;
        bits = 0;
        return number is { IsInteger: false } && TryLiteral(number.SourceSpelling, source.BoundType, source is PrefixMinusKoto, out bits);
    }

    internal static bool TryLiteral(ReadOnlySpan<char> source, BoundType? type, bool negative, out long bits)
    {
        bits = 0;
        if (!Supports(type))
        {
            return false;
        }

        char[]? rented = null;
        try
        {
            if (source.Contains('_'))
            {
                rented = ArrayPool<char>.Shared.Rent(source.Length);
                var length = 0;
                foreach (var c in source)
                {
                    if (c != '_')
                    {
                        rented[length++] = c;
                    }
                }

                source = rented.AsSpan(0, length);
            }

            // Parse directly into the selected format: f64 then f32 could double-round.
            if (ReferenceEquals(type, BoundType.F32))
            {
                if (!float.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                {
                    return false;
                }

                bits = BitConverter.SingleToUInt32Bits(value) ^ (negative ? 0x80000000U : 0U);
            }
            else
            {
                if (!double.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                {
                    return false;
                }

                bits = BitConverter.DoubleToInt64Bits(value) ^ (negative ? long.MinValue : 0);
            }

            return true;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}
