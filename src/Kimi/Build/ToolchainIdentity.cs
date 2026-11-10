// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the native-toolchain identity of the windows-x64 profile, read from the compiler's embedded backend record (profile.json, the
// resource Kimi.WindowsBackendProfile.json; DECISIONS R5-M165, R5-M174): the pinned LLVM release, the backend archive's package id, ABI
// version, version, SHA-256 and file name, and the kernel32 definition and dlltool hashes.
// Input: the embedded record. Output: those values, for the native build, publication's manifest, toolchain verification and the
// kernel32 import library's generation (P13). Invariants: the record matches the target profile (WindowsProfile's name, backend
// library and provided symbols) and pins a major.minor.patch LLVM version, else the type initializer throws InvalidDataException.
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

    internal static string DlltoolSha256 { get; }

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
        DlltoolSha256 = kernel.GetProperty("dlltoolSha256").GetString()!;
        if (!ArtifactFiles.SequenceEqual(root.GetProperty("providedSymbols"), WindowsProfile.ProvidedSymbols))
        {
            throw new InvalidDataException("The embedded native supply catalog has incompatible provided symbols.");
        }
    }
}
