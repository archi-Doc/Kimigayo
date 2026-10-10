// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The scalar value-plan subset shared by ownership and emission, independent of storage layout.</summary>
internal static class ScalarTypes
{
    internal static bool Supports(BoundType? type) => ReferenceEquals(type, BoundType.Boolean) || ReferenceEquals(type, BoundType.Char) || type is { IsFloatingPoint: true } || Width(type) != 0;

    // Character storage uses i32, but does not grant integer arithmetic or conversions.
    internal static bool IsCharacterValue(Int128 value) => value >= 0 && value <= 0x10FFFF && !(value >= 0xD800 && value <= 0xDFFF);

    // A wrapping integer Type answers every width and signedness query by its integer argument (SPEC 3.1.1.1).
    internal static int Width(BoundType? type, int pointerWidth = 64) => type?.Kind != BoundTypeKind.Primitive ? 0 : type.Underlying.Name switch
    {
        "i8" or "u8" => 8,
        "i16" or "u16" => 16,
        "i32" or "u32" => 32,
        "i64" or "u64" => 64,
        "i128" or "u128" => 128,
        "isize" or "usize" => pointerWidth,
        _ => 0,
    };

    internal static bool Signed(BoundType type) => type.Underlying.Name[0] == 'i';

    // Canonical signed extension of the N-bit payload, also for unsigned language Types.
    internal static Int128 Normalize(Int128 bits, int width) => width <= 64
        ? (unchecked((long)bits) << (64 - width)) >> (64 - width) : bits;

    internal static bool TryLiteral(BoundType? type, UInt128 magnitude, bool negative, int pointerWidth, out Int128 bits)
    {
        bits = 0;
        var width = Width(type, pointerWidth);
        if (width == 0)
        {
            return false;
        }

        var signed = Signed(type!);
        var limit = signed ? ((UInt128)1 << (width - 1)) - (negative ? 0U : 1U) : width == 128 ? UInt128.MaxValue : ((UInt128)1 << width) - 1;
        if (magnitude > limit || (negative && !signed && magnitude != 0))
        {
            return false;
        }

        bits = Normalize(unchecked((Int128)(negative ? (UInt128)0 - magnitude : magnitude)), width);
        return true;
    }

    // Selected-precision literal bits, independent of integer fitting and arithmetic.
    internal static bool TryFloatLiteral(Koto source, out long bits)
    {
        var number = KotoHelper.SignedNumber(source, out var negative);
        bits = 0;
        return number is not null && TryFloatLiteral(number, source.BoundType, negative, out bits);
    }

    internal static bool TryFloatLiteral(NumberLiteralKoto number, BoundType? type, bool negative, out long bits)
    {
        if (!number.IsInteger)
        {
            return TryFloatLiteral(number.SourceSpelling, type, negative, out bits);
        }

        bits = 0;
        if (type is not { IsFloatingPoint: true } || !number.TryGetIntegerMagnitude(out var magnitude))
        {
            return false;
        }

        // Format the exact magnitude into stack storage and parse at the destination
        // precision. UInt128 -> double -> float would round twice for some integers.
        Span<char> digits = stackalloc char[39];
        return magnitude.TryFormat(digits, out var written, provider: CultureInfo.InvariantCulture) &&
            TryFloatLiteral(digits[..written], type, negative && magnitude != 0, out bits);
    }

    internal static bool TryFloatLiteral(ReadOnlySpan<char> source, BoundType? type, bool negative, out long bits)
    {
        bits = 0;
        if (type is not { IsFloatingPoint: true })
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
