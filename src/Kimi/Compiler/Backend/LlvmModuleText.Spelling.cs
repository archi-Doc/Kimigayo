// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Compiler.Backend;

// The spellings of LlvmModuleText (DECISIONS R5-M14): an external symbol's spelling after @ and its recovery, numbers formatted on the
// stack or once as text, numbered names and the bits of a floating constant's hexadecimal spelling.
internal static partial class LlvmModuleText
{
    // The LLVM spelling after @ of an external symbol: the plain identifier, or a quoted
    // name whose UTF-8 bytes other than printable ASCII, " and \ are written as \XX.
    internal static string ExternalName(string symbol)
    {
        var plain = symbol.Length != 0 && !char.IsAsciiDigit(symbol[0]);
        foreach (var c in symbol)
        {
            plain &= char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '$' or '-';
        }

        if (plain)
        {
            return symbol;
        }

        var text = new StringBuilder(symbol.Length + 8).Append('"');
        foreach (var b in Encoding.UTF8.GetBytes(symbol))
        {
            if (b is >= 0x20 and < 0x7F and not (byte)'"' and not (byte)'\\')
            {
                text.Append((char)b);
            }
            else
            {
                text.Append('\\').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return text.Append('"').ToString();
    }

    // Recovers the external symbol from its ExternalName spelling.
    internal static string SymbolFromName(ReadOnlySpan<char> name)
    {
        if (name.Length < 2 || name[0] != '"' || name[^1] != '"')
        {
            return name.ToString();
        }

        name = name[1..^1];
        var bytes = new byte[name.Length];
        var count = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\' && i + 2 < name.Length &&
                byte.TryParse(name.Slice(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var escaped))
            {
                bytes[count++] = escaped;
                i += 2;
            }
            else
            {
                bytes[count++] = (byte)name[i];
            }
        }

        return Encoding.UTF8.GetString(bytes, 0, count);
    }

    // A number's text for a runtime template filled once at startup; writing formats through WriteNumber instead.
    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    // TextWriter.Write(long) formats through a temporary string; format on the stack instead.
    internal static void WriteNumber(TextWriter output, long value)
    {
        Span<char> digits = stackalloc char[20];
        value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        output.Write(digits[..length]);
    }

    internal static void WriteNumber(TextWriter output, Int128 value)
    {
        if (value >= long.MinValue && value <= long.MaxValue)
        {
            WriteNumber(output, (long)value);
            return;
        }

        Span<char> digits = stackalloc char[40];
        value.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture);
        output.Write(digits[..length]);
    }

    // The bits LLVM's hexadecimal spelling of a floating constant writes, from the constant's IEEE bits of size 4 or 8 bytes: a double's
    // own bits, or a float's exactly widened double (SPEC 13.5.4 keeps NaN payloads and the sign of zero). A finite float widens exactly
    // through the hardware; an infinity or NaN keeps its sign, takes the all-ones exponent and its payload shifted by 29, so a signaling
    // NaN stays signaling where a hardware widening sets its quiet bit.
    internal static ulong Hexadecimal(Int128 bits, int size)
    {
        if (size == 8)
        {
            return unchecked((ulong)bits);
        }

        var single = unchecked((uint)bits);
        if ((single & 0x7F800000) != 0x7F800000)
        {
            return BitConverter.DoubleToUInt64Bits(BitConverter.UInt32BitsToSingle(single));
        }

        return ((ulong)(single >> 31) << 63) | (0x7FFUL << 52) | ((ulong)(single & 0x7FFFFF) << 29);
    }

    // A numbered name (a symbol, label or SSA value): its prefix, then the number formatted on the stack.
    internal static void Name(TextWriter output, string prefix, int id)
    {
        output.Write(prefix);
        WriteNumber(output, id);
    }
}
