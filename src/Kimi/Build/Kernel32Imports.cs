// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>The project-owned kernel32 import definition, its generation conditions and a generated library's inspection, which toolchain
/// verify checks (SPEC 20.4); the compiler's kernel32 supply is ToolchainIdentity's.</summary>
internal static partial class Kernel32Imports
{
    internal const string Dll = "KERNEL32.dll";
    internal const string Generator = "llvm-dlltool";

    internal static readonly string Definition;
    internal static readonly string DefinitionSha256;
    internal static readonly string DlltoolSha256;

    static Kernel32Imports()
    {
        using var stream = typeof(Kernel32Imports).Assembly.GetManifestResourceStream("Kimi.Kernel32.def")!;
        using var reader = new StreamReader(stream);
        Definition = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        DefinitionSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Definition)));
        DlltoolSha256 = ToolchainIdentity.DlltoolSha256;
        if (ToolchainIdentity.Kernel32DefinitionSha256 != DefinitionSha256 || DlltoolSha256.Length != 64)
        {
            throw new InvalidDataException("Kernel32 definition or tool identity does not match the profile.");
        }
    }

    internal static void ValidateInstallation(JsonElement entry, string actualHash)
    {
        var generated = ArtifactFiles.States(
            entry,
            ("generator", Generator),
            ("generatorSha256", DlltoolSha256),
            ("target", WindowsProfile.Target),
            ("dll", Dll),
            ("definitionSha256", DefinitionSha256),
            ("sha256", actualHash));
        if (!generated)
        {
            throw new InvalidDataException($"Installed kernel32 generation conditions or SHA-256 mismatch. Run {NativeToolchain.SetupCommand} again.");
        }
    }

    internal static void ValidateLibrary(string dll, string inspection)
    {
        var expected = new HashSet<string>(WindowsProfile.Kernel32Symbols.Length * 2, StringComparer.Ordinal);
        foreach (var symbol in WindowsProfile.Kernel32Symbols)
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
