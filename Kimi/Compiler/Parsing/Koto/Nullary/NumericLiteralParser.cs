using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

public static class NumericLiteralParser
{
    private const int StackallocThreshold = 256;

    /// <summary>
    /// Parses a numeric literal.
    /// </summary>
    /// <param name="source">
    /// The complete numeric literal, excluding a leading unary plus or minus.
    /// </param>
    /// <param name="targetPointerSize">
    /// The target pointer size in bytes. The value must be 4 or 8.
    /// </param>
    /// <param name="kind">
    /// The parsed numeric literal kind.
    /// </param>
    /// <param name="uv">
    /// The parsed integer value or the bit representation of a floating-point value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the literal was parsed successfully;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool TryParse(ReadOnlySpan<char> source, int targetPointerSize, out NumericLiteralKind kind, out UInt128 uv)
    {
        kind = NumericLiteralKind.Invalid;
        uv = 0;

        if (targetPointerSize is not 4 and not 8)
        {
            return false;
        }

        if (source.IsEmpty || source[0] is '+' or '-')
        {
            return false;
        }

        if (source.Length >= 2 && source[0] == '0')
        {
            switch (source[1])
            {
                case 'b':
                    return TryParseRadixInteger(source, prefixLength: 2, radix: 2, targetPointerSize, ref kind, ref uv);

                case 'o':
                    return TryParseRadixInteger(source, prefixLength: 2, radix: 8, targetPointerSize, ref kind, ref uv);

                case 'x':
                    return TryParseRadixInteger(source, prefixLength: 2, radix: 16, targetPointerSize, ref kind, ref uv);
            }
        }

        return TryParseDecimal(source, targetPointerSize, ref kind, ref uv);
    }

    public static bool TryParse(ReadOnlySpan<char> source, out NumericLiteralKind kind, out UInt128 uv)
        => TryParse(source, IntPtr.Size, out kind, out uv);

    private static bool TryParseDecimal(ReadOnlySpan<char> source, int targetPointerSize, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        if (!TryScanDecimalBody(source, out var bodyLength, out var hasFloatSyntax, out var integerValue, out var integerOverflow))
        {
            return false;
        }

        var suffix = source[bodyLength..];
        if (!TryGetSuffixKind(suffix, hasFloatSyntax, out var requestedKind))
        {
            return false;
        }

        if (requestedKind is
            NumericLiteralKind.Float or
            NumericLiteralKind.F32 or
            NumericLiteralKind.F64)
        {
            return TryParseFloat(source[..bodyLength], requestedKind, ref kind, ref uv);
        }

        if (hasFloatSyntax || integerOverflow)
        {
            return false;
        }

        return TryStoreInteger(integerValue, requestedKind, targetPointerSize, ref kind, ref uv);
    }

    private static bool TryParseRadixInteger(ReadOnlySpan<char> source, int prefixLength, uint radix, int targetPointerSize, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        if ((uint)prefixLength >= (uint)source.Length)
        {
            return false;
        }

        var firstDigit = GetDigit(source[prefixLength]);

        if ((uint)firstDigit >= radix)
        {
            return false;
        }

        var radixValue = (UInt128)radix;
        var maximumBeforeMultiply = UInt128.MaxValue / radixValue;
        var maximumLastDigit = UInt128.MaxValue % radixValue;

        var index = prefixLength;
        var value = (UInt128)0;

        while ((uint)index < (uint)source.Length)
        {
            var c = source[index];

            if (c == '_')
            {
                index++;
                continue;
            }

            var digit = GetDigit(c);

            if ((uint)digit >= radix)
            {
                break;
            }

            var digitValue = (UInt128)(uint)digit;

            if (value > maximumBeforeMultiply ||
                (value == maximumBeforeMultiply &&
                 digitValue > maximumLastDigit))
            {
                return false;
            }

            value = (value * radixValue) + digitValue;
            index++;
        }

        var suffix = source[index..];

        if (!TryGetIntegerSuffixKind(suffix, out var requestedKind))
        {
            return false;
        }

        return TryStoreInteger(value, requestedKind, targetPointerSize, ref kind,
            ref uv);
    }

    private static bool TryScanDecimalBody(ReadOnlySpan<char> source, out int bodyLength, out bool hasFloatSyntax, out UInt128 integerValue, out bool integerOverflow)
    {
        bodyLength = 0;
        hasFloatSyntax = false;
        integerValue = 0;
        integerOverflow = false;

        if (source.IsEmpty || !IsDecimalDigit(source[0]))
        {
            return false;
        }

        const uint Radix = 10;

        var maximumBeforeMultiply = UInt128.MaxValue / Radix;

        var maximumLastDigit = UInt128.MaxValue % Radix;

        var index = 0;

        while ((uint)index < (uint)source.Length)
        {
            var c = source[index];

            if (IsDecimalDigit(c))
            {
                if (!integerOverflow)
                {
                    var digit = (UInt128)(uint)(c - '0');

                    if (integerValue > maximumBeforeMultiply ||
                        (integerValue == maximumBeforeMultiply &&
                         digit > maximumLastDigit))
                    {
                        integerOverflow = true;
                    }
                    else
                    {
                        integerValue =
                            (integerValue * Radix) + digit;
                    }
                }

                index++;
                continue;
            }

            if (c == '_')
            {
                index++;
                continue;
            }

            break;
        }

        if ((uint)index < (uint)source.Length && source[index] == '.')
        {
            var next = index + 1;

            if ((uint)next >= (uint)source.Length || IsDecimalDigit(source[next]))
            {
                hasFloatSyntax = true;
                index++;

                while ((uint)index < (uint)source.Length)
                {
                    var c = source[index];
                    if (IsDecimalDigit(c) || c == '_')
                    {
                        index++;
                        continue;
                    }

                    break;
                }
            }
        }

        if ((uint)index < (uint)source.Length && source[index] is 'e' or 'E')
        {
            hasFloatSyntax = true;
            index++;

            if ((uint)index < (uint)source.Length && source[index] is '+' or '-')
            {
                index++;
            }

            if ((uint)index >= (uint)source.Length ||
                !IsDecimalDigit(source[index]))
            {
                return false;
            }

            index++;
            while ((uint)index < (uint)source.Length)
            {
                var c = source[index];

                if (IsDecimalDigit(c) || c == '_')
                {
                    index++;
                    continue;
                }

                break;
            }
        }

        bodyLength = index;
        return true;
    }

    private static bool TryStoreInteger(UInt128 value, NumericLiteralKind requestedKind, int targetPointerSize, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        switch (requestedKind)
        {
            case NumericLiteralKind.Integer:
                break;

            case NumericLiteralKind.I8:
                if (value > (UInt128)sbyte.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.I16:
                if (value > (UInt128)short.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.I32:
                if (value > int.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.I64:
                if (value > long.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.I128:
                if (value > (UInt128)Int128.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.ISize:
                if (targetPointerSize == 8)
                {
                    if (value > long.MaxValue)
                    {
                        return false;
                    }
                }
                else if (value > int.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.U8:
                if (value > byte.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.U16:
                if (value > ushort.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.U32:
                if (value > uint.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.U64:
                if (value > ulong.MaxValue)
                {
                    return false;
                }

                break;

            case NumericLiteralKind.U128:
                break;

            case NumericLiteralKind.USize:
                if (targetPointerSize == 8)
                {
                    if (value > ulong.MaxValue)
                    {
                        return false;
                    }
                }
                else if (value > uint.MaxValue)
                {
                    return false;
                }

                break;

            default:
                return false;
        }

        uv = value;
        kind = requestedKind;
        return true;
    }

    private static bool TryParseFloat(ReadOnlySpan<char> source, NumericLiteralKind requestedKind, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        if (source.IndexOf('_') < 0)
        {
            return TryParseNormalizedFloat(source, requestedKind, ref kind, ref uv);
        }

        char[]? rented = null;

        try
        {
            Span<char> buffer = source.Length <= StackallocThreshold
                ? stackalloc char[source.Length]
                : (rented = ArrayPool<char>.Shared.Rent(source.Length));

            var written = 0;

            foreach (var c in source)
            {
                if (c != '_')
                {
                    buffer[written++] = c;
                }
            }

            return TryParseNormalizedFloat(buffer[..written], requestedKind, ref kind, ref uv);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private static unsafe bool TryParseNormalizedFloat(ReadOnlySpan<char> source, NumericLiteralKind requestedKind, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        const NumberStyles Styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

        switch (requestedKind)
        {
            case NumericLiteralKind.F32:
                if (!float.TryParse(source, Styles, CultureInfo.InvariantCulture, out var f32) || !float.IsFinite(f32))
                {
                    return false;
                }

                {
                    UInt128 bits = 0;
                    *(float*)&bits = f32;

                    uv = bits;
                    kind = NumericLiteralKind.F32;
                    return true;
                }

            case NumericLiteralKind.Float:
            case NumericLiteralKind.F64:
                if (!double.TryParse(source, Styles, CultureInfo.InvariantCulture,
                    out var f64) || !double.IsFinite(f64))
                {
                    return false;
                }

                {
                    UInt128 bits = 0;
                    *(double*)&bits = f64;

                    uv = bits;
                    kind = requestedKind;
                    return true;
                }

            default:
                return false;
        }
    }

    private static bool TryGetSuffixKind(ReadOnlySpan<char> suffix, bool hasFloatSyntax, out NumericLiteralKind kind)
    {
        if (suffix.IsEmpty)
        {
            kind = hasFloatSyntax
                ? NumericLiteralKind.Float
                : NumericLiteralKind.Integer;

            return true;
        }

        kind = GetSuffixKind(suffix);

        if (kind == NumericLiteralKind.Invalid)
        {
            return false;
        }

        return !hasFloatSyntax ||
            kind is NumericLiteralKind.F32 or NumericLiteralKind.F64;
    }

    private static bool TryGetIntegerSuffixKind(ReadOnlySpan<char> suffix, out NumericLiteralKind kind)
    {
        if (suffix.IsEmpty)
        {
            kind = NumericLiteralKind.Integer;
            return true;
        }

        kind = GetSuffixKind(suffix);

        return kind is >= NumericLiteralKind.I8 and <= NumericLiteralKind.USize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericLiteralKind GetSuffixKind(
        ReadOnlySpan<char> suffix)
        => suffix.Length switch
        {
            2 => GetSuffix2(suffix),
            3 => GetSuffix3(suffix),
            4 => GetSuffix4(suffix),
            5 => GetSuffix5(suffix),
            _ => NumericLiteralKind.Invalid,
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericLiteralKind GetSuffix2(
        ReadOnlySpan<char> suffix)
    {
        if (suffix[1] != '8')
        {
            return NumericLiteralKind.Invalid;
        }

        return suffix[0] switch
        {
            'i' => NumericLiteralKind.I8,
            'u' => NumericLiteralKind.U8,
            _ => NumericLiteralKind.Invalid,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericLiteralKind GetSuffix3(ReadOnlySpan<char> suffix)
    {
        var c0 = suffix[0];
        var c1 = suffix[1];
        var c2 = suffix[2];

        if (c0 == 'f')
        {
            if (c1 == '3' && c2 == '2')
            {
                return NumericLiteralKind.F32;
            }

            if (c1 == '6' && c2 == '4')
            {
                return NumericLiteralKind.F64;
            }

            return NumericLiteralKind.Invalid;
        }

        if (c1 == '1' && c2 == '6')
        {
            return c0 switch
            {
                'i' => NumericLiteralKind.I16,
                'u' => NumericLiteralKind.U16,
                _ => NumericLiteralKind.Invalid,
            };
        }

        if (c1 == '3' && c2 == '2')
        {
            return c0 switch
            {
                'i' => NumericLiteralKind.I32,
                'u' => NumericLiteralKind.U32,
                _ => NumericLiteralKind.Invalid,
            };
        }

        if (c1 == '6' && c2 == '4')
        {
            return c0 switch
            {
                'i' => NumericLiteralKind.I64,
                'u' => NumericLiteralKind.U64,
                _ => NumericLiteralKind.Invalid,
            };
        }

        return NumericLiteralKind.Invalid;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericLiteralKind GetSuffix4(ReadOnlySpan<char> suffix)
    {
        if (suffix[1] != '1' ||
            suffix[2] != '2' ||
            suffix[3] != '8')
        {
            return NumericLiteralKind.Invalid;
        }

        return suffix[0] switch
        {
            'i' => NumericLiteralKind.I128,
            'u' => NumericLiteralKind.U128,
            _ => NumericLiteralKind.Invalid,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NumericLiteralKind GetSuffix5(ReadOnlySpan<char> suffix)
    {
        if (suffix[1] != 's' ||
            suffix[2] != 'i' ||
            suffix[3] != 'z' ||
            suffix[4] != 'e')
        {
            return NumericLiteralKind.Invalid;
        }

        return suffix[0] switch
        {
            'i' => NumericLiteralKind.ISize,
            'u' => NumericLiteralKind.USize,
            _ => NumericLiteralKind.Invalid,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDecimalDigit(char c)
        => (uint)(c - '0') <= 9;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDigit(char c)
    {
        if ((uint)(c - '0') <= 9)
        {
            return c - '0';
        }

        if ((uint)(c - 'a') <= 5)
        {
            return c - 'a' + 10;
        }

        if ((uint)(c - 'A') <= 5)
        {
            return c - 'A' + 10;
        }

        return -1;
    }
}
