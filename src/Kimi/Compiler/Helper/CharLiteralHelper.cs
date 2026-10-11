// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Helper;

/// <summary>Scans and decodes single-scalar char literals.</summary>
internal static class CharLiteralHelper
{
    // Keep the complete token, including delimiters. A malformed literal stops before
    // a physical line break (LF, CRLF, or CR; SPEC 2.2) so recovery cannot consume a declaration
    // on the next line. Other excluded scalars such as U+2028 are content errors diagnosed by Decode.
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
            if (c is '\r' or '\n')
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
        var sourceEnd = koto.Span.End - 1;
        var validEscapes = true;
        var validContent = true;
        var count = 0;
        Rune value = default;
        while (!content.IsEmpty)
        {
            if (content[0] == '\\')
            {
                var start = sourceEnd - content.Length;
                content = content[1..];
                if (!StringLiteralHelper.TryReadCharacterEscape(ref content, koto, start, out var scalar))
                {
                    validEscapes = false;
                    continue;
                }

                value = new Rune((int)scalar);
            }
            else
            {
                var status = Rune.DecodeFromUtf16(content, out value, out var consumed);
                validContent &= status == OperationStatus.Done &&
                    value.Value is not (<= 0x1F or >= 0x7F and <= 0x9F or 0x2028 or 0x2029 or 0x27 or 0x5C);
                content = content[Math.Max(1, consumed)..];
            }

            count++;
        }

        // The scalar count depends on every escape decoding successfully; each malformed escape owns its own failure.
        if (!validContent || (validEscapes && count != 1))
        {
            koto.AddDiagnostic(DiagnosticCode.InvalidCharLiteral_Kd);
            return null;
        }

        return validEscapes ? value : null;
    }
}
