// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kimi.Compiler;

internal static class Kernel32Imports
{
    internal const string Dll = "KERNEL32.dll";
    internal const string Generator = "llvm-dlltool";
    internal static readonly string[] Symbols = ["GetProcessHeap", "HeapAlloc", "HeapFree", "GetStdHandle", "WriteFile", "GetLastError", "ExitProcess", "VirtualAlloc", "VirtualProtect", "VirtualFree"];
    internal static readonly string Definition;
    internal static readonly string DefinitionSha256;
    internal static readonly string DlltoolSha256;

    static Kernel32Imports()
    {
        using var stream = typeof(Kernel32Imports).Assembly.GetManifestResourceStream("Kimi.Kernel32.def")!;
        using var reader = new StreamReader(stream);
        Definition = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        DefinitionSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Definition)));
        using var profile = typeof(Kernel32Imports).Assembly.GetManifestResourceStream("Kimi.WindowsBackendProfile.json")!;
        using var json = JsonDocument.Parse(profile);
        var kernel = json.RootElement.GetProperty("kernel32");
        DlltoolSha256 = kernel.GetProperty("dlltoolSha256").GetString()!;
        if (kernel.GetProperty("definitionSha256").GetString() != DefinitionSha256 || DlltoolSha256.Length != 64)
        {
            throw new InvalidDataException("Kernel32 definition or tool identity does not match the profile.");
        }
    }

    internal static void ValidateManifest(JsonElement library)
    {
        if (library.GetProperty("kind").GetString() != "import" ||
            !library.TryGetProperty("generator", out var generator) || generator.GetString() != Generator ||
            !library.TryGetProperty("dll", out var dll) || dll.GetString() != Dll ||
            !library.TryGetProperty("definitionSha256", out var hash) || hash.GetString() != DefinitionSha256 ||
            library.TryGetProperty("input", out _))
        {
            throw new InvalidDataException("Invalid generated kernel32 identity. Re-emit the link manifest; external kernel32 paths are no longer supported.");
        }
    }

    internal static void ValidateLibrary(string dll, string inspection)
    {
        var symbols = Regex.Matches(inspection, @"(?m)^Symbol: (\S+)\r?$").Select(x => x.Groups[1].Value).ToArray();
        var expected = Symbols.Concat(Symbols.Select(x => "__imp_" + x)).ToHashSet(StringComparer.Ordinal);
        var formats = Regex.Matches(inspection, @"(?m)^Format: (\S+)\r?$").Select(x => x.Groups[1].Value).ToArray();
        if (dll.Trim() != Dll || symbols.Length != expected.Count || !expected.SetEquals(symbols) ||
            formats.Length == 0 || formats.Any(x => x is not ("COFF-x86-64" or "COFF-import-file-x86-64")))
        {
            throw new InvalidDataException("Generated kernel32 library has an unexpected DLL, architecture or import symbol set.");
        }
    }
}
