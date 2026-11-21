// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the native-toolchain identity of the windows-x64 profile, read from the compiler's embedded backend record (profile.json, the
// resource Kimi.WindowsBackendProfile.json; DECISIONS R5-M165, R5-M174): the pinned LLVM release, the backend archive's package id, ABI
// version, version, SHA-256 and file name, the kernel32 definition and library hashes, and the compiler's supplies (IMPL 20.4's
// reserved names): one row per name, its kind, the manifest fields that identify it, and its bytes, which the compiler carries as an
// embedded resource (Kimi.Kernel32.lib, Kimi.WindowsBackend.lib, committed beside the catalog) whose SHA-256 is the catalog's.
// Input: the embedded record and libraries. Output: those values, for the native build, publication's manifest and toolchain verify's
// self-check (P13). Invariants: the record matches the target profile (WindowsProfile's name, backend
// library and provided symbols) and pins a major.minor.patch LLVM version, and the supplies' rows are WindowsProfile's two reserved names
// in Ordinal order (publication interleaves a program's supplies by name), else the type initializer throws InvalidDataException; each
// resource's SHA-256 is its row's (NativeToolchainTest's catalog row; not checked at a start, stated by toolchain verify).
// Must not be read by check (Binding, project configuration, LSP) or code generation: those read WindowsProfile.

using System.Text.Json;
using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>The pinned native-toolchain identity of the windows-x64 profile, from the compiler's embedded backend record.</summary>
public static class ToolchainIdentity
{
    public const string BackendFile = "kimi_backend_windows_x64_v1.lib";

    // Backend supply identity (SPEC 21.4.7).
    internal const string BackendPackageId = "kimi-backend-windows-x64";
    internal const int BackendAbiVersion = 2;

    public static string LlvmVersion { get; }

    public static string BackendVersion { get; }

    public static string BackendSha256 { get; }

    internal static string Kernel32DefinitionSha256 { get; }

    internal static string Kernel32Sha256 { get; }

    /// <summary>Gets the compiler's supplies, one per reserved name in Ordinal name order.</summary>
    internal static CompilerSupply[] Supplies { get; }

    static ToolchainIdentity()
    {
        using var stream = typeof(ToolchainIdentity).Assembly.GetManifestResourceStream("Kimi.WindowsBackendProfile.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        LlvmVersion = root.GetProperty("llvmVersion").GetString()!;
        if (!Version.TryParse(LlvmVersion, out var llvmVersion) || llvmVersion.Build < 0 || llvmVersion.Revision >= 0 || llvmVersion.ToString() != LlvmVersion)
        {
            throw new InvalidDataException("The embedded profile requires a major.minor.patch LLVM version.");
        }

        if (root.GetProperty("packageId").GetString() != BackendPackageId || root.GetProperty("abiVersion").GetInt32() != BackendAbiVersion ||
            root.GetProperty("profile").GetString() != WindowsProfile.Name ||
            root.GetProperty("library").GetString() != WindowsProfile.BackendLibrary)
        {
            throw new InvalidDataException("The embedded native supply catalog does not match windows-x64-v1.");
        }

        BackendVersion = CompilerRelease.Version;
        BackendSha256 = root.GetProperty("artifactSha256").GetString()!;
        var kernel = root.GetProperty("kernel32");
        Kernel32DefinitionSha256 = kernel.GetProperty("definitionSha256").GetString()!;
        Kernel32Sha256 = kernel.GetProperty("sha256").GetString()!;
        if (!ArtifactFiles.SequenceEqual(root.GetProperty("providedSymbols"), WindowsProfile.ProvidedSymbols))
        {
            throw new InvalidDataException("The embedded native supply catalog has incompatible provided symbols.");
        }

        Supplies =
        [
            new(
                WindowsProfile.Kernel32Library,
                "import",
                [("generator", Kernel32Imports.Generator), ("dll", Kernel32Imports.Dll), ("definitionSha256", Kernel32DefinitionSha256)],
                "Kimi.Kernel32.lib",
                Kernel32Sha256),
            new(WindowsProfile.BackendLibrary, "static", [("resolution", "toolchain")], "Kimi.WindowsBackend.lib", BackendSha256),
        ];
        for (var i = 1; i < Supplies.Length; i++)
        {
            if (string.CompareOrdinal(Supplies[i - 1].Name, Supplies[i].Name) >= 0)
            {
                throw new InvalidDataException("The compiler's supplies are not in Ordinal name order.");
            }
        }
    }
}

/// <summary>One of the compiler's supplies (IMPL 20.4's reserved names): its name and kind, the link manifest's fields that identify it
/// after them (in publication's order), its embedded resource and that resource's SHA-256, the catalog's.</summary>
/// <param name="Name">The reserved library name.</param>
/// <param name="Kind">The library's kind.</param>
/// <param name="Entry">The manifest entry's identifying fields, in writing order.</param>
/// <param name="Resource">The embedded resource holding the library's bytes.</param>
/// <param name="Sha256">The catalog's SHA-256 of those bytes.</param>
internal sealed record CompilerSupply(string Name, string Kind, (string Name, string Value)[] Entry, string Resource, string Sha256)
{
    /// <summary>Opens the library's bytes.</summary>
    /// <returns>The embedded resource's stream, which the caller disposes.</returns>
    internal Stream Open() => typeof(CompilerSupply).Assembly.GetManifestResourceStream(this.Resource)!;
}
