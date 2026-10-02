// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static class OwnershipStorage
{
    internal const long DefaultByteLimit = 64L * 1024 * 1024;

    [ThreadStatic]
    private static long byteLimit;

    // The thread-local override makes boundary tests small; the compiler profile uses the default.
    internal static long ByteLimit
    {
        get => byteLimit > 0 ? byteLimit : DefaultByteLimit;
        set => byteLimit = value;
    }

    internal static int Cells(int rows, int columns, int bits, string table)
    {
        var cells = (long)rows * columns;
        var bytes = cells > (long.MaxValue - 7) / bits ? long.MaxValue : ((cells * bits) + 7) / 8;
        if (cells > Array.MaxLength || bytes > ByteLimit)
        {
            throw new OwnershipStorageLimitException(table, bytes, ByteLimit);
        }

        return (int)cells;
    }
}
