// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // ABI bridges only. Ordered-link algorithms are compiled from DictionaryStorage.kimi.
    private static void WriteDictionaryStorage(LegacyRuntimeHelpers helpers, TextWriter output)
    {
        if (helpers.DictionaryAppendSlot is null ||
            helpers.DictionaryInitialize is not { } initialize || helpers.DictionaryClearLinks is null)
        {
            throw new InvalidOperationException("Dictionary storage sources were not compiled.");
        }

        output.Write(RuntimeAbi.DictionaryInit.GetDefinition(false));
        output.Write("entry:\n  call void @");
        output.Write(initialize.Name);
        output.Write("(ptr %handle)\n  ret void\n}\n\n");
    }
}
