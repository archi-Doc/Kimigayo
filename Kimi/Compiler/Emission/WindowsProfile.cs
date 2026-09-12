// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;

namespace Kimi.Compiler;

/// <summary>The pinned, module-local Windows execution profile. This is a partial language implementation.</summary>
public static class WindowsProfile
{
    public const string Target = "x86_64-pc-windows-msvc";
    public const string Name = "windows-x64-v1";
    public const string LlvmVersion = "22.1.8";
    public const string DataLayout = "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128";
    public const string BackendFile = "kimi_backend_windows_x64_v1.lib";

    public static string BackendVersion { get; }

    public static string BackendSha256 { get; }

    static WindowsProfile()
    {
        using var stream = typeof(WindowsProfile).Assembly.GetManifestResourceStream("Kimi.WindowsBackendProfile.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        if (root.GetProperty("packageId").GetString() != "kimi-backend-windows-x64" || root.GetProperty("abiVersion").GetInt32() != 1 ||
            root.GetProperty("profile").GetString() != Name || root.GetProperty("llvmVersion").GetString() != LlvmVersion ||
            root.GetProperty("library").GetString() != "kimi_backend")
        {
            throw new InvalidDataException("The embedded native supply catalog does not match windows-x64-v1.");
        }

        BackendVersion = root.GetProperty("packageVersion").GetString()!;
        BackendSha256 = root.GetProperty("artifactSha256").GetString()!;
        var symbols = root.GetProperty("providedSymbols");
        if (symbols.GetArrayLength() != 4 || symbols[0].GetString() != "__chkstk" || symbols[1].GetString() != "memcpy" ||
            symbols[2].GetString() != "memmove" || symbols[3].GetString() != "memset")
        {
            throw new InvalidDataException("The embedded native supply catalog has incompatible provided symbols.");
        }
    }
}
