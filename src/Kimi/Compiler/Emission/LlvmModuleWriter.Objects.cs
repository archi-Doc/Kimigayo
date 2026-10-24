// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

internal static partial class LlvmModuleWriter
{
    // Decides from the old module whether it uses objects and supplies its creation records; ObjectRuntime writes the runtime
    // text and each object from those syntax-free physical records (DECISIONS R5-M14, R5-M69).
    private static void WriteObjects(EmissionModule module, TextWriter output)
    {
        var usesObjects = module.NeedsObjectRuntime || module.Objects.Count != 0;
        foreach (var layout in module.Aggregates)
        {
            usesObjects |= layout.ObjectHandle is not null;
        }

        if (!usesObjects)
        {
            return;
        }

        ObjectRuntime.WriteRuntime(output);
        foreach (var item in module.Objects)
        {
            ObjectRuntime.WriteObject(output, item);
        }
    }
}
