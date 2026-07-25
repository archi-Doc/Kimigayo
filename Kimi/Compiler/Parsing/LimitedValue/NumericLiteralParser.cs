// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Parsing;

public static class NumericLiteralParser
{
    private const int StackallocThreshold = 256;

    public static bool TryParse(ReadOnlySpan<char> source, out LimitedValue value, out NumericLiteralError error)
    {
        value = default;
        error = NumericLiteralError.None;

        if (source.IsEmpty)
        {
            error = NumericLiteralError.Empty;
            return false;
        }

        if (source[0] is '+' or '-')
        {
            error = NumericLiteralError.SignNotAllowed;
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

        SplitSuffix(source, prefixLength, out var literalPart, out var suffix);

        if (!TryClassifySuffix(suffix, out var suffixKind))
        {
            error = NumericLiteralError.InvalidSuffix;
            return false;
        }

        var hasFloatSyntax = radix == 10 && ContainsFloatMarker(literalPart);

        var isFloat = hasFloatSyntax || suffixKind is NumericSuffixKind.F32 or NumericSuffixKind.F64;

        if (isFloat)
        {
            if (radix != 10)
            {
                error = NumericLiteralError.FloatWithRadixPrefix;
                return false;
            }

            if (suffixKind is not (NumericSuffixKind.None or NumericSuffixKind.F32 or NumericSuffixKind.F64))
            {
                error = NumericLiteralError.InvalidSuffix;
                return false;
            }

            return TryParseFloat(                literalPart,                suffixKind,                out value,                out error);
        }

        if (suffixKind is NumericSuffixKind.F32 or NumericSuffixKind.F64)
        {
            error = NumericLiteralError.InvalidSuffix;
            return false;
        }

        return TryParseInteger(            literalPart[prefixLength..],            radix,            suffixKind,            out value,            out error);
    }

    private static bool TryParseInteger(        ReadOnlySpan<char> digits,        int radix,        NumericSuffixKind suffix,        out LimitedValue value,        out NumericLiteralError error)
    {
        value = default;
        error = NumericLiteralError.None;

        if (!IsIntegerSuffix(suffix))
        {
            error = NumericLiteralError.InvalidSuffix;
            return false;
        }

        if (!TryParseUInt128(digits, radix, out var parsed, out error))
        {
            return false;
        }

        /*
         * LimitedValueにはlongしかないため、Rustのu64/u128などの全範囲は
         * 表現できない。longに収まる値だけを受理する。
         *
         * サフィックス固有の範囲検査も行う。
         */
        if (!FitsSuffix(parsed, suffix))
        {
            error = NumericLiteralError.OutOfRange;
            return false;
        }

        if (parsed > long.MaxValue)
        {
            error = NumericLiteralError.NotRepresentable;
            return false;
        }

        value = new LimitedValue((long)parsed);
        return true;
    }

    private static bool TryParseFloat(
        ReadOnlySpan<char> literal,
        NumericSuffixKind suffix,
        out LimitedValue value,
        out NumericLiteralError error)
    {
        value = default;
        error = NumericLiteralError.None;

        char[]? rented = null;

        try
        {
            Span<char> buffer = literal.Length <= StackallocThreshold
                ? stackalloc char[literal.Length]
                : (rented = ArrayPool<char>.Shared.Rent(literal.Length));

            if (!TryRemoveUnderscores(
                literal,
                buffer,
                out var written,
                out error))
            {
                return false;
            }

            var normalized = buffer[..written];

            if (suffix == NumericSuffixKind.F32)
            {
                if (!float.TryParse(
                    normalized,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
                {
                    error = NumericLiteralError.InvalidFloat;
                    return false;
                }

                if (float.IsInfinity(parsed))
                {
                    error = NumericLiteralError.OutOfRange;
                    return false;
                }

                // f32として丸められた値をdouble領域へ格納する。
                value = new LimitedValue((double)parsed);
                return true;
            }
            else
            {
                if (!double.TryParse(
                    normalized,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
                {
                    error = NumericLiteralError.InvalidFloat;
                    return false;
                }

                if (double.IsInfinity(parsed))
                {
                    error = NumericLiteralError.OutOfRange;
                    return false;
                }

                value = new LimitedValue(parsed);
                return true;
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private static bool TryParseUInt128(
        ReadOnlySpan<char> source,
        int radix,
        out UInt128 value,
        out NumericLiteralError error)
    {
        value = 0;
        error = NumericLiteralError.None;

        if (source.IsEmpty)
        {
            error = NumericLiteralError.MissingDigits;
            return false;
        }

        var hasDigit = false;

        foreach (var c in source)
        {
            if (c == '_')
            {
                continue;
            }

            var digit = GetDigitValue(c);

            if ((uint)digit >= (uint)radix)
            {
                error = NumericLiteralError.InvalidDigit;
                return false;
            }

            hasDigit = true;

            var radixValue = (UInt128)(uint)radix;
            var digitValue = (UInt128)(uint)digit;

            if (value > (UInt128.MaxValue - digitValue) / radixValue)
            {
                error = NumericLiteralError.OutOfRange;
                return false;
            }

            value = (value * radixValue) + digitValue;
        }

        if (!hasDigit)
        {
            error = NumericLiteralError.MissingDigits;
            return false;
        }

        return true;
    }

    private static bool TryRemoveUnderscores(
        ReadOnlySpan<char> source,
        Span<char> destination,
        out int written,
        out NumericLiteralError error)
    {
        written = 0;
        error = NumericLiteralError.None;

        foreach (var c in source)
        {
            if (c != '_')
            {
                destination[written++] = c;
            }
        }

        if (written == 0)
        {
            error = NumericLiteralError.MissingDigits;
            return false;
        }

        return true;
    }

    private static void SplitSuffix(
        ReadOnlySpan<char> source,
        int prefixLength,
        out ReadOnlySpan<char> literal,
        out ReadOnlySpan<char> suffix)
    {
        /*
         * Rustの数値サフィックスは識別子形式で末尾に続く。
         *
         * 指数部のe/Eは数値本体なので、単純に最初の英字で
         * 分割してはいけない。
         *
         * ここでは既知のサフィックスを末尾から判定する。
         */
        foreach (var knownSuffix in Suffixes)
        {
            if (!source.EndsWith(
                knownSuffix,
                StringComparison.Ordinal))
            {
                continue;
            }

            var suffixStart = source.Length - knownSuffix.Length;

            if (suffixStart <= prefixLength)
            {
                continue;
            }

            /*
             * 例えば "123i32" と "123_i32" の両方を許可する。
             * '_'は数値本体に残っても後で除去される。
             */
            literal = source[..suffixStart];
            suffix = source[suffixStart..];
            return;
        }

        /*
         * 既知でない識別子が末尾に続いている場合も、
         * InvalidSuffixとして検出する。
         */
        var index = FindUnknownSuffixStart(source, prefixLength);

        if (index >= 0)
        {
            literal = source[..index];
            suffix = source[index..];
            return;
        }

        literal = source;
        suffix = default;
    }

    private static int FindUnknownSuffixStart(
        ReadOnlySpan<char> source,
        int prefixLength)
    {
        for (var i = prefixLength; i < source.Length; i++)
        {
            var c = source[i];

            if (!IsIdentifierStart(c))
            {
                continue;
            }

            /*
             * 10進浮動小数点の指数記号はサフィックスではない。
             */
            if (c is 'e' or 'E')
            {
                var next = i + 1;

                if (next < source.Length &&
                    source[next] is '+' or '-')
                {
                    next++;
                }

                if (next < source.Length &&
                    IsDecimalDigitOrUnderscore(source[next]))
                {
                    i = next;
                    continue;
                }
            }

            /*
             * 16進数のa-f/A-Fは数字。
             */
            if (prefixLength == 2 &&
                source[1] == 'x' &&
                GetDigitValue(c) is >= 10 and < 16)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private static bool TryClassifySuffix(
        ReadOnlySpan<char> suffix,
        out NumericSuffixKind kind)
    {
        if (suffix.IsEmpty)
        {
            kind = NumericSuffixKind.None;
            return true;
        }

        kind = suffix switch
        {
            "i8" => NumericSuffixKind.I8,
            "i16" => NumericSuffixKind.I16,
            "i32" => NumericSuffixKind.I32,
            "i64" => NumericSuffixKind.I64,
            "i128" => NumericSuffixKind.I128,
            "isize" => NumericSuffixKind.Isize,

            "u8" => NumericSuffixKind.U8,
            "u16" => NumericSuffixKind.U16,
            "u32" => NumericSuffixKind.U32,
            "u64" => NumericSuffixKind.U64,
            "u128" => NumericSuffixKind.U128,
            "usize" => NumericSuffixKind.Usize,

            "f32" => NumericSuffixKind.F32,
            "f64" => NumericSuffixKind.F64,

            _ => NumericSuffixKind.Invalid,
        };

        return kind != NumericSuffixKind.Invalid;
    }

    private static bool FitsSuffix(
        UInt128 value,
        NumericSuffixKind suffix)
        => suffix switch
        {
            NumericSuffixKind.None => value <= int.MaxValue,

            NumericSuffixKind.I8 => value <= sbyte.MaxValue,
            NumericSuffixKind.I16 => value <= short.MaxValue,
            NumericSuffixKind.I32 => value <= int.MaxValue,
            NumericSuffixKind.I64 => value <= long.MaxValue,
            NumericSuffixKind.I128 => value <= (UInt128)Int128.MaxValue,

            NumericSuffixKind.U8 => value <= byte.MaxValue,
            NumericSuffixKind.U16 => value <= ushort.MaxValue,
            NumericSuffixKind.U32 => value <= uint.MaxValue,
            NumericSuffixKind.U64 => value <= ulong.MaxValue,
            NumericSuffixKind.U128 => true,

            /*
             * Kimigayoのターゲットが64bitと仮定。
             * 32bitも対象にするなら、ターゲット情報を引数で渡す。
             */
            NumericSuffixKind.Isize => value <= long.MaxValue,
            NumericSuffixKind.Usize => value <= ulong.MaxValue,

            _ => false,
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsFloatMarker(ReadOnlySpan<char> source)
    {
        foreach (var c in source)
        {
            if (c is '.' or 'e' or 'E')
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIntegerSuffix(NumericSuffixKind suffix)
        => suffix is >= NumericSuffixKind.None and <= NumericSuffixKind.Usize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetDigitValue(char c)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierStart(char c)
        => c == '_' ||
            (uint)(c - 'a') <= 'z' - 'a' ||
            (uint)(c - 'A') <= 'Z' - 'A';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDecimalDigitOrUnderscore(char c)
        => c == '_' || (uint)(c - '0') <= 9;

    private static ReadOnlySpan<string> Suffixes =>
    [
        // 長いものを先に並べる。
        "isize",
        "usize",
        "i128",
        "u128",
        "i16",
        "u16",
        "i32",
        "u32",
        "i64",
        "u64",
        "i8",
        "u8",
        "f32",
        "f64",
    ];

    private enum NumericSuffixKind : byte
    {
        Invalid,

        None,
        I8,
        I16,
        I32,
        I64,
        I128,
        Isize,
        U8,
        U16,
        U32,
        U64,
        U128,
        Usize,

        F32,
        F64,
    }
}

public enum NumericLiteralError
{
    None,
    Empty,
    SignNotAllowed,
    MissingDigits,
    InvalidDigit,
    InvalidSuffix,
    InvalidFloat,
    FloatWithRadixPrefix,
    OutOfRange,

    /// <summary>
    /// The literal is valid for its Rust type, but cannot be represented
    /// by LimitedValue.
    /// </summary>
    NotRepresentable,
}
