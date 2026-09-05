// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Helper;

/// <summary>Scans and decodes single-scalar char literals.</summary>
internal static class CharLiteralHelper
{
    // Keep the complete token, including delimiters. A malformed literal stops before
    // a line break so recovery cannot consume a declaration on the next line.
    internal static bool Scan(ReadOnlySpan<char> text, out int length)
    {
        length = 0;
        if (text.IsEmpty || text[0] != '\'')
        {
            return false;
        }

        var escaped = false;
        for (length = 1; length < text.Length; length++)
        {
            var c = text[length];
            if (c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')
            {
                return false;
            }

            if (!escaped && c == '\'')
            {
                length++;
                return true;
            }

            escaped = !escaped && c == '\\';
        }

        return false;
    }

    internal static Rune? Decode(ReadOnlySpan<char> literal, Koto koto)
    {
        var content = literal[1..^1];
        Rune value;
        if (!content.IsEmpty && content[0] == '\\')
        {
            content = content[1..];
            if (!StringLiteralHelper.TryReadCharacterEscape(ref content, koto, out var scalar))
            {
                return null;
            }

            value = new Rune((int)scalar);
        }
        else
        {
            if (Rune.DecodeFromUtf16(content, out value, out var consumed) != OperationStatus.Done ||
                value.Value is <= 0x1F or >= 0x7F and <= 0x9F or 0x2028 or 0x2029 or 0x27 or 0x5C)
            {
                koto.AddDiagnostic(DiagnosticCode.InvalidCharLiteral_Kd);
                return null;
            }

            content = content[consumed..];
        }

        if (!content.IsEmpty)
        {
            koto.AddDiagnostic(DiagnosticCode.InvalidCharLiteral_Kd);
            return null;
        }

        return value;
    }
}
