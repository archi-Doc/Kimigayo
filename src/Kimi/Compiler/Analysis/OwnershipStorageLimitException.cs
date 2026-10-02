// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed class OwnershipStorageLimitException(string table, long requiredBytes, long limitBytes)
    : Exception($"Ownership analysis requires {requiredBytes} bytes for {table}; the limit is {limitBytes} bytes.")
{
    internal string Table { get; } = table;

    internal long RequiredBytes { get; } = requiredBytes;

    internal long LimitBytes { get; } = limitBytes;
}
