// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kimi.Compiler;

/// <summary>The project-owned kernel32 import definition and its generated-library identity (SPEC 20.8.2).</summary>
internal static partial class Kernel32Imports
{
    internal const string LibraryName = "kernel32";
    internal const string Dll = "KERNEL32.dll";
    internal const string Generator = "llvm-dlltool";

    /// <summary>Gets the exports: the seven runtime APIs plus the VirtualAlloc family used only by native tests.</summary>
    internal static readonly string[] Symbols = [.. WindowsProfile.RuntimeImports, "VirtualAlloc", "VirtualProtect", "VirtualFree"];

    internal static readonly string Definition;
    internal static readonly string DefinitionSha256;
    internal static readonly string DlltoolSha256;

    static Kernel32Imports()
    {
        using var stream = typeof(Kernel32Imports).Assembly.GetManifestResourceStream("Kimi.Kernel32.def")!;
        using var reader = new StreamReader(stream);
        Definition = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        DefinitionSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Definition)));
        DlltoolSha256 = WindowsProfile.DlltoolSha256;
        if (WindowsProfile.Kernel32DefinitionSha256 != DefinitionSha256 || DlltoolSha256.Length != 64)
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
        var expected = new HashSet<string>(Symbols.Length * 2, StringComparer.Ordinal);
        foreach (var symbol in Symbols)
        {
            expected.Add(symbol);
            expected.Add("__imp_" + symbol);
        }

        // Exactly the expected symbol set, each once, in x64 COFF members only.
        var valid = dll.AsSpan().Trim().SequenceEqual(Dll);
        var seen = new HashSet<string>(expected.Count, StringComparer.Ordinal);
        foreach (Match match in SymbolPattern().Matches(inspection))
        {
            valid &= expected.Contains(match.Groups[1].Value) && seen.Add(match.Groups[1].Value);
        }

        var formats = 0;
        foreach (Match match in FormatPattern().Matches(inspection))
        {
            formats++;
            valid &= match.Groups[1].Value is "COFF-x86-64" or "COFF-import-file-x86-64";
        }

        if (!valid || seen.Count != expected.Count || formats == 0)
        {
            throw new InvalidDataException("Generated kernel32 library has an unexpected DLL, architecture or import symbol set.");
        }
    }

    [GeneratedRegex(@"(?m)^Symbol: (\S+)\r?$")]
    private static partial Regex SymbolPattern();

    [GeneratedRegex(@"(?m)^Format: (\S+)\r?$")]
    private static partial Regex FormatPattern();
}
