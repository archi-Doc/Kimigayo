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

    // Code generation settings shared by function attributes, the link manifest and llc (SPEC 21.5.1).
    internal const string Cpu = "x86-64";
    internal const string Features = "+sse2";
    internal const string RelocationModel = "pic";
    internal const string CodeModel = "small";
    internal const string UnwindTables = "async";

    // Backend supply (SPEC 21.5.7) and Application entry (SPEC 22.2.3) identities.
    internal const string BackendPackageId = "kimi-backend-windows-x64";
    internal const int BackendAbiVersion = 1;
    internal const string BackendLibrary = "kimi_backend";
    internal const string EntrySymbol = "__kimi_start";
    internal const string Subsystem = "console";
    internal const string FloatMarker = "_fltused";

    /// <summary>Gets the helper symbols supplied by the backend archive, in catalog order.</summary>
    internal static readonly string[] ProvidedSymbols = ["__chkstk", "memcpy", "memmove", "memset"];

    /// <summary>Gets the seven Windows APIs imported by the generated runtime (SPEC 22.5.6).</summary>
    internal static readonly string[] RuntimeImports = ["GetProcessHeap", "HeapAlloc", "HeapFree", "GetStdHandle", "WriteFile", "GetLastError", "ExitProcess"];

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

        if (root.GetProperty("packageId").GetString() != BackendPackageId || root.GetProperty("abiVersion").GetInt32() != BackendAbiVersion ||
            root.GetProperty("profile").GetString() != Name ||
            root.GetProperty("library").GetString() != BackendLibrary)
        {
            throw new InvalidDataException("The embedded native supply catalog does not match windows-x64-v1.");
        }

        BackendVersion = CompilerRelease.Version;
        BackendSha256 = root.GetProperty("artifactSha256").GetString()!;
        var kernel = root.GetProperty("kernel32");
        Kernel32DefinitionSha256 = kernel.GetProperty("definitionSha256").GetString()!;
        DlltoolSha256 = kernel.GetProperty("dlltoolSha256").GetString()!;
        if (!SequenceEqual(root.GetProperty("providedSymbols"), ProvidedSymbols))
        {
            throw new InvalidDataException("The embedded native supply catalog has incompatible provided symbols.");
        }
    }

    /// <summary>Compares a JSON string array with an expected ordered list without allocating.</summary>
    /// <param name="array">The JSON array.</param>
    /// <param name="expected">The expected strings.</param>
    /// <returns>Whether both contain the same strings in order.</returns>
    internal static bool SequenceEqual(JsonElement array, ReadOnlySpan<string> expected)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != expected.Length)
        {
            return false;
        }

        var i = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !item.ValueEquals(expected[i++]))
            {
                return false;
            }
        }

        return true;
    }
}
