// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Build;

/// <summary>The generator and DLL the compiler's kernel32 supply's manifest entry names (SPEC 20.4), beside its definition's SHA-256, the
/// catalog's (ToolchainIdentity). The compiler carries the library, not the definition it was generated from (Tatara B6b6);
/// Kernel32ImportsTest compares the committed definition with the catalog.</summary>
internal static class Kernel32Imports
{
    internal const string Dll = "KERNEL32.dll";
    internal const string Generator = "llvm-dlltool";
}
