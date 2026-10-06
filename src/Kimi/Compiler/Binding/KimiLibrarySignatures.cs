// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // Build once; validation reads one catalog slot without searching or allocating.
    private static T?[] IndexSignatures<T>(T[] signatures, Func<T, KimiDeclarationId> identity)
        where T : struct
    {
        var indexed = new T?[KimiLibraryCatalog.Entries.Length];
        foreach (var signature in signatures)
        {
            var index = KimiLibraryCatalog.Index(identity(signature));
            if (index < 0 || indexed[index].HasValue)
            {
                throw new InvalidOperationException("Invalid or duplicate Kimi signature ID.");
            }

            indexed[index] = signature;
        }

        return indexed;
    }
}
