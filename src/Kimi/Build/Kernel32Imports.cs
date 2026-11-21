// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;

namespace Kimi.Build;

/// <summary>The project-owned kernel32 import definition, embedded, whose SHA-256 the type initializer compares with the catalog's
/// (toolchain verify reads it for that), and the generator and DLL its supply's manifest entry names (SPEC 20.4); the compiler's kernel32
/// supply is ToolchainIdentity's.</summary>
internal static class Kernel32Imports
{
    internal const string Dll = "KERNEL32.dll";
    internal const string Generator = "llvm-dlltool";

    internal static readonly string Definition;
    internal static readonly string DefinitionSha256;

    static Kernel32Imports()
    {
        using var stream = typeof(Kernel32Imports).Assembly.GetManifestResourceStream("Kimi.Kernel32.def")!;
        using var reader = new StreamReader(stream);
        Definition = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        DefinitionSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Definition)));
        if (ToolchainIdentity.Kernel32DefinitionSha256 != DefinitionSha256)
        {
            throw new InvalidDataException("Kernel32 definition does not match the profile.");
        }
    }
}
