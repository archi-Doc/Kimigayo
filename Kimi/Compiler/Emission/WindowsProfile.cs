// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;

namespace Kimi.Compiler;

/// <summary>The pinned, module-local Windows execution profile. This is a partial language implementation.</summary>
public static class WindowsProfile
{
    public const string Target = "x86_64-pc-windows-msvc";
    public const string Name = "windows-x64-v1";
    public const string DataLayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128";
    public const string BackendFile = "kimi_backend_windows_x64_v1.lib";

    public static string LlvmVersion { get; }

    public static string BackendVersion { get; }

    public static string BackendSha256 { get; }

    internal static string Kernel32DefinitionSha256 { get; }

    internal static string DlltoolSha256 { get; }

    static WindowsProfile()
    {
        using var stream = typeof(WindowsProfile).Assembly.GetManifestResourceStream("Kimi.WindowsBackendProfile.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        LlvmVersion = root.GetProperty("llvmVersion").GetString()!;
        if (!Version.TryParse(LlvmVersion, out var llvmVersion) || llvmVersion.Build < 0 || llvmVersion.Revision >= 0 || llvmVersion.ToString() != LlvmVersion)
        {
            throw new InvalidDataException("The embedded profile requires a major.minor.patch LLVM version.");
        }

        if (root.GetProperty("packageId").GetString() != "kimi-backend-windows-x64" || root.GetProperty("abiVersion").GetInt32() != 1 ||
            root.GetProperty("profile").GetString() != Name ||
            root.GetProperty("library").GetString() != "kimi_backend")
        {
            throw new InvalidDataException("The embedded native supply catalog does not match windows-x64-v1.");
        }

        BackendVersion = CompilerRelease.Version;
        BackendSha256 = root.GetProperty("artifactSha256").GetString()!;
        var kernel = root.GetProperty("kernel32");
        Kernel32DefinitionSha256 = kernel.GetProperty("definitionSha256").GetString()!;
        DlltoolSha256 = kernel.GetProperty("dlltoolSha256").GetString()!;
        var symbols = root.GetProperty("providedSymbols");
        if (symbols.GetArrayLength() != 4 || symbols[0].GetString() != "__chkstk" || symbols[1].GetString() != "memcpy" ||
            symbols[2].GetString() != "memmove" || symbols[3].GetString() != "memset")
        {
            throw new InvalidDataException("The embedded native supply catalog has incompatible provided symbols.");
        }
    }
}
