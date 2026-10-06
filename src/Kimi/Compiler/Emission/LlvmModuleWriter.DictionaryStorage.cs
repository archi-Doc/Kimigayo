// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // ABI bridges only. Ordered-link algorithms are compiled from DictionaryStorage.kimi.
    private static void WriteDictionaryStorage(EmissionModule module, TextWriter output)
    {
        if (module.DictionaryAppendSlot is null ||
            module.DictionaryInitialize is not { } initialize || module.DictionaryClearLinks is null)
        {
            throw new InvalidOperationException("Dictionary storage sources were not compiled.");
        }

        output.Write(WindowsLowering.DictionaryInit.GetDefinition(false));
        output.Write("entry:\n  call void @");
        output.Write(initialize.Name);
        output.Write("(ptr %handle)\n  ret void\n}\n\n");
    }
}
