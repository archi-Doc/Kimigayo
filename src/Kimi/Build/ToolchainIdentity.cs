// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the native-toolchain identity of the windows-x64 profile, read from the compiler's embedded backend record (profile.json, the
// resource Kimi.WindowsBackendProfile.json; DECISIONS R5-M165, R5-M174): the pinned LLVM release, the backend archive's package id, ABI
// version, version, SHA-256 and file name, the kernel32 definition and library hashes, the backend supply (Backend: the profile's
// reserved supply the record's library names), and the compiler's supplies (IMPL 20.4's reserved supplies): one row per supply of the
// target profile (TargetProfile.Windows.Supplies: its name, kind and symbols), with the manifest fields that identify it and its bytes, which
// the compiler carries as an embedded resource (Kimi.Kernel32.lib, Kimi.WindowsBackend.lib, committed beside the catalog) whose SHA-256
// is the catalog's; this material is keyed by the supply's name.
// Input: the embedded record and libraries. Output: those values, for the native build, publication's manifest and toolchain verify's
// self-check (P13). Invariants: the record matches the target profile (TargetProfile.Windows's name; its library names a reserved supply, whose
// symbols are its provided symbols) and pins a major.minor.patch LLVM version, and the material's keys are the profile's supplies' names
// in the profile's order, else the type initializer throws InvalidDataException; each resource's SHA-256 is its row's
// (NativeToolchainTest's catalog row; not checked at a start, stated by toolchain verify).
// Must not be read by check (Binding, project configuration, LSP) or code generation: those read TargetProfile.Windows.

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

    /// <summary>Gets the backend supply: the target profile's reserved supply that the embedded record's library names.</summary>
    internal static TargetSupply Backend { get; }

    /// <summary>Gets the compiler's supplies, one per reserved supply of the target profile, in the profile's order.</summary>
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
            root.GetProperty("profile").GetString() != TargetProfile.Windows.Name ||
            TargetProfile.Windows.Supply(root.GetProperty("library").GetString()) is not { } backend)
        {
            throw new InvalidDataException("The embedded native supply catalog does not match windows-x64-v1.");
        }

        Backend = backend;
        BackendVersion = CompilerRelease.Version;
        BackendSha256 = root.GetProperty("artifactSha256").GetString()!;
        const string kernel32 = "kernel32";
        var kernel = root.GetProperty(kernel32);
        Kernel32DefinitionSha256 = kernel.GetProperty("definitionSha256").GetString()!;
        Kernel32Sha256 = kernel.GetProperty("sha256").GetString()!;
        if (!ArtifactFiles.SequenceEqual(root.GetProperty("providedSymbols"), Backend.Symbols))
        {
            throw new InvalidDataException("The embedded native supply catalog has incompatible provided symbols.");
        }

        // Each supply's material, keyed by its name: kernel32's under the record's kernel32 member, the backend's under its library.
        (string Name, (string Name, string Value)[] Entry, string Resource, string Sha256)[] material =
        [
            (
                kernel32,
                [("generator", Kernel32Imports.Generator), ("dll", Kernel32Imports.Dll), ("definitionSha256", Kernel32DefinitionSha256)],
                "Kimi.Kernel32.lib",
                Kernel32Sha256),
            (Backend.Name, [("resolution", "toolchain")], "Kimi.WindowsBackend.lib", BackendSha256),
        ];
        var row = TargetProfile.Windows.Supplies;
        var supplies = new CompilerSupply[material.Length];
        for (var i = 0; i < material.Length; i++)
        {
            var (name, entry, resource, sha256) = material[i];
            if (material.Length != row.Length || name != row[i].Name)
            {
                throw new InvalidDataException("The compiler's supplies are not the target profile's reserved supplies.");
            }

            supplies[i] = new(row[i], entry, resource, sha256);
        }

        Supplies = supplies;
    }
}

/// <summary>One of the compiler's supplies (IMPL 20.4's reserved supplies): the target profile's supply (its name, kind and symbols),
/// the link manifest's fields that identify it after its name and kind (in publication's order), its embedded resource and that
/// resource's SHA-256, the catalog's.</summary>
/// <param name="Supply">The target profile's reserved supply.</param>
/// <param name="Entry">The manifest entry's identifying fields, in writing order.</param>
/// <param name="Resource">The embedded resource holding the library's bytes.</param>
/// <param name="Sha256">The catalog's SHA-256 of those bytes.</param>
internal sealed record CompilerSupply(TargetSupply Supply, (string Name, string Value)[] Entry, string Resource, string Sha256)
{
    /// <summary>Opens the library's bytes.</summary>
    /// <returns>The embedded resource's stream, which the caller disposes.</returns>
    internal Stream Open() => typeof(CompilerSupply).Assembly.GetManifestResourceStream(this.Resource)!;
}
