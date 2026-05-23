// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Kimigayo.Lsp;

internal class LspHelper
{
    public const byte Cr = (byte)'\r';
    public const byte Lf = (byte)'\n';

    public static int ToLspSeverity(string severity)
        => severity switch
        {
            "error" => 1,
            "warning" => 2,
            "info" => 3,
            "hint" => 4,
            _ => 1,
        };

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
