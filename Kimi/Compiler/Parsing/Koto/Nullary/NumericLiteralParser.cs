using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

public static class NumericLiteralParser
{
    private const int StackallocThreshold = 256;

    public static unsafe bool TryParse(ReadOnlySpan<char> source, out NumericLiteralKind kind, out UInt128 uv)
    {
        kind = NumericLiteralKind.Invalid;
        uv = 0;

        if (source.IsEmpty || source[0] is '+' or '-')
        {
            return false;
        }

        var radix = 10;
        var prefixLength = 0;
        if (source.Length >= 2 && source[0] == '0')
        {
            switch (source[1])
            {
                case 'b':
                    radix = 2;
                    prefixLength = 2;
                    break;

                case 'o':
                    radix = 8;
                    prefixLength = 2;
                    break;

                case 'x':
                    radix = 16;
                    prefixLength = 2;
                    break;
            }
        }

        if (radix == 10)
        {
            return TryParseDecimal(source, ref kind, ref uv);
        }

        return TryParseRadixInteger(source, prefixLength, radix, ref kind, ref uv);
    }

    private static unsafe bool TryParseDecimal(ReadOnlySpan<char> source, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        if (!TryScanDecimalBody(source, out var bodyLength, out var hasFloatSyntax))
        {
            return false;
        }

        var suffix = source[bodyLength..];

        if (!TryGetSuffixKind(suffix, hasFloatSyntax, out var requestedKind))
        {
            return false;
        }

        if (requestedKind is NumericLiteralKind.F32 or NumericLiteralKind.F64)
        {
            return TryParseFloat(source[..bodyLength], requestedKind, ref kind, ref uv);
        }

        if (hasFloatSyntax)
        {
            return false;
        }

        if (!TryParseUInt128(source[..bodyLength], 10, out var value))
        {
            return false;
        }

        return TryStoreInteger(value, requestedKind, ref kind, ref uv);
    }

    private static bool TryParseRadixInteger(ReadOnlySpan<char> source, int prefixLength, int radix, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        var index = prefixLength;
        var hasDigit = false;
        while ((uint)index < (uint)source.Length)
        {
            var c = source[index];

            if (c == '_')
            {
                index++;
                continue;
            }

            var digit = GetDigit(c);

            if ((uint)digit >= (uint)radix)
            {
                break;
            }

            hasDigit = true;
            index++;
        }

        if (!hasDigit)
        {
            return false;
        }

        var suffix = source[index..];

        if (!TryGetIntegerSuffixKind(suffix, out var requestedKind))
        {
            return false;
        }

        if (!TryParseUInt128(
            source.Slice(prefixLength, index - prefixLength),
            radix,
            out var value))
        {
            return false;
        }

        return TryStoreInteger(value, requestedKind, ref kind, ref uv);
    }

    private static unsafe bool TryStoreInteger(UInt128 value, NumericLiteralKind requestedKind, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        kind = NumericLiteralKind.Invalid;
        uv = 0;

        switch (requestedKind)
        {
            case NumericLiteralKind.I8:
                if (value > (UInt128)sbyte.MaxValue)
                {
                    return false;
                }

                *(sbyte*)Unsafe.AsPointer(ref uv) = (sbyte)value;
                break;

            case NumericLiteralKind.I16:
                if (value > (UInt128)short.MaxValue)
                {
                    return false;
                }

                *(short*)Unsafe.AsPointer(ref uv) = (short)value;
                break;

            case NumericLiteralKind.I32:
                if (value > int.MaxValue)
                {
                    return false;
                }

                *(int*)Unsafe.AsPointer(ref uv) = (int)value;
                break;

            case NumericLiteralKind.I64:
                if (value > long.MaxValue)
                {
                    return false;
                }

                *(long*)Unsafe.AsPointer(ref uv) = (long)value;
                break;

            case NumericLiteralKind.I128:
                if (value > (UInt128)Int128.MaxValue)
                {
                    return false;
                }

                *(Int128*)Unsafe.AsPointer(ref uv) = (Int128)value;
                break;

            case NumericLiteralKind.ISize:
                if (IntPtr.Size == 8)
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

                *(nuint*)Unsafe.AsPointer(ref uv) = (nuint)value;
                break;

            case NumericLiteralKind.U8:
                if (value > byte.MaxValue)
                {
                    return false;
                }

                *(byte*)Unsafe.AsPointer(ref uv) = (byte)value;
                break;

            case NumericLiteralKind.U16:
                if (value > ushort.MaxValue)
                {
                    return false;
                }

                *(ushort*)Unsafe.AsPointer(ref uv) = (ushort)value;
                break;

            case NumericLiteralKind.U32:
                if (value > uint.MaxValue)
                {
                    return false;
                }

                *(uint*)Unsafe.AsPointer(ref uv) = (uint)value;
                break;

            case NumericLiteralKind.U64:
                if (value > ulong.MaxValue)
                {
                    return false;
                }

                *(ulong*)Unsafe.AsPointer(ref uv) = (ulong)value;
                break;

            case NumericLiteralKind.U128:
                uv = value;
                break;

            case NumericLiteralKind.USize:
                if (IntPtr.Size == 8)
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

                *(nuint*)Unsafe.AsPointer(ref uv) = (nuint)value;
                break;

            default:
                return false;
        }

        kind = requestedKind;
        return true;
    }

    private static unsafe bool TryParseFloat(ReadOnlySpan<char> source, NumericLiteralKind requestedKind, ref NumericLiteralKind kind, ref UInt128 uv)
    {
        var underscoreIndex = source.IndexOf('_');
        if (underscoreIndex < 0)
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
        kind = NumericLiteralKind.Invalid;
        uv = 0;

        const NumberStyles Styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

        switch (requestedKind)
        {
            case NumericLiteralKind.F32:
                if (!float.TryParse(source, Styles, CultureInfo.InvariantCulture, out var f32) || !float.IsFinite(f32))
                {
                    return false;
                }

                *(float*)Unsafe.AsPointer(ref uv) = f32;
                kind = NumericLiteralKind.F32;
                return true;

            case NumericLiteralKind.F64:
                if (!double.TryParse(
                    source,
                    Styles,
                    CultureInfo.InvariantCulture,
                    out var f64) ||
                    !double.IsFinite(f64))
                {
                    return false;
                }

                *(double*)Unsafe.AsPointer(ref uv) = f64;
                kind = NumericLiteralKind.F64;
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseUInt128(ReadOnlySpan<char> source, int radix, out UInt128 value)
    {
        value = 0;

        var hasDigit = false;
        var radixValue = (UInt128)(uint)radix;

        foreach (var c in source)
        {
            if (c == '_')
            {
                continue;
            }

            var digit = GetDigit(c);

            if ((uint)digit >= (uint)radix)
            {
                return false;
            }

            hasDigit = true;

            var digitValue = (UInt128)(uint)digit;

            if (value > (UInt128.MaxValue - digitValue) / radixValue)
            {
                return false;
            }

            value = (value * radixValue) + digitValue;
        }

        return hasDigit;
    }

    private static bool TryScanDecimalBody(ReadOnlySpan<char> source, out int bodyLength, out bool hasFloatSyntax)
    {
        bodyLength = 0;
        hasFloatSyntax = false;

        var index = 0;
        var hasDigit = false;

        while ((uint)index < (uint)source.Length)
        {
            var c = source[index];

            if (IsDecimalDigit(c))
            {
                hasDigit = true;
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

        if (!hasDigit)
        {
            return false;
        }

        if ((uint)index < (uint)source.Length && source[index] == '.')
        {
            var next = index + 1;

            if ((uint)next >= (uint)source.Length ||
                IsDecimalDigit(source[next]))
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

        if ((uint)index < (uint)source.Length &&
            source[index] is 'e' or 'E')
        {
            hasFloatSyntax = true;
            index++;

            if ((uint)index < (uint)source.Length &&
                source[index] is '+' or '-')
            {
                index++;
            }

            var hasExponentDigit = false;

            while ((uint)index < (uint)source.Length)
            {
                var c = source[index];

                if (IsDecimalDigit(c))
                {
                    hasExponentDigit = true;
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

            if (!hasExponentDigit)
            {
                return false;
            }
        }

        bodyLength = index;
        return true;
    }

    private static bool TryGetSuffixKind(ReadOnlySpan<char> suffix, bool hasFloatSyntax, out NumericLiteralKind kind)
    {
        if (suffix.IsEmpty)
        {
            kind = hasFloatSyntax ? NumericLiteralKind.F64 : NumericLiteralKind.I32;

            return true;
        }

        kind = GetSuffixKind(suffix);

        if (kind == NumericLiteralKind.Invalid)
        {
            return false;
        }

        return !hasFloatSyntax || kind is NumericLiteralKind.F32 or NumericLiteralKind.F64;
    }

    private static bool TryGetIntegerSuffixKind(ReadOnlySpan<char> suffix, out NumericLiteralKind kind)
    {
        if (suffix.IsEmpty)
        {
            kind = NumericLiteralKind.I32;
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
    private static NumericLiteralKind GetSuffix3(
        ReadOnlySpan<char> suffix)
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
    private static NumericLiteralKind GetSuffix4(
        ReadOnlySpan<char> suffix)
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
    private static NumericLiteralKind GetSuffix5(
        ReadOnlySpan<char> suffix)
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
