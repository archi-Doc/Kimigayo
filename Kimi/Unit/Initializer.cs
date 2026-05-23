// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimigayo;

internal static class Initializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        try
        {
            HashedString.LoadAssembly(null, asm, "Misc.Language.strings-en.tinyhand");
            HashedString.LoadAssembly("ja", asm, "Misc.Language.strings-ja.tinyhand");
        }
        catch
        {
        }
    }
}
