// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Lsp;

internal static class LspHelper
{
    public const byte Cr = (byte)'\r';
    public const byte Lf = (byte)'\n';

    public static readonly byte[] ContentHeader = "content-length: "u8.ToArray();

    public static bool StartsWithIgnoreAsciiCase(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prefix)
    {
        if (source.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            var c = source[i];
            if ((uint)(c - 'A') <= 'Z' - 'A')
            {
                c = (byte)(c + 0x20);
            }

            if (c != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
