// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the native-toolchain identity of the windows-x64 profile, read from the compiler's embedded backend record (profile.json, the
// resource Kimi.WindowsBackendProfile.json; DECISIONS R5-M165, R5-M174): the pinned LLVM release, the backend archive's package id, ABI
// version, version and SHA-256, the kernel32 definition and library hashes, the backend supply (Backend: the profile's
// reserved supply the record's library names), and the compiler's supplies (IMPL 20.4's reserved supplies) in one representation,
// keyed by (row, supply) (CompilerSupply; C-469 n1, the Tatara plan's B7b7): one per reserved supply of each row (its TargetSupply: name,
// kind and symbols), with the manifest fields that identify it and its files, each with its catalog name, its embedded resource (committed
// beside the catalog), the catalog's SHA-256 and its place on an ELF link line (SupplyPlace, read once here; a COFF file's is Library). The
// windows-x64 row's: kernel32 (Kimi.Kernel32.lib) and the backend archive (Kimi.WindowsBackend.lib), from the backend record; the
// linux-x64 row's: c, musl 1.2.6's startup files and C library (Tatara B7b4), read from the embedded Linux record
// (src/backend/linux-x64/profile.json, the resource Kimi.LinuxBackendProfile.json), whose files are listed in link order.
// Input: the embedded records and libraries. Output: those values, for the native build, publication's manifest and toolchain verify's
// self-check (P13), and each file's stored facts, the resource beside its own (SupplyFile.OpenFacts; SupplyFacts). Invariants: the
// backend record matches the Windows row (its name; its library names a reserved supply, whose symbols are its provided symbols) and
// pins a major.minor.patch LLVM version; the Linux record matches the Linux row (its name; its library names a reserved supply, whose
// symbols are its provided symbols) and places each file start, library or end (ReadFile); the supplies are the rows' reserved supplies
// in the rows' order, each row's in its order, else the type initializer throws InvalidDataException; each resource's SHA-256 is its
// file's (NativeToolchainTest's catalog row; not checked at a start, stated by toolchain verify); a file's facts' first line is its
// bytes' SHA-256 and their body Tatara's inspection of them (NativeToolchainTest's facts row; not checked at a start).
// Must not be read by check (Binding, project configuration, LSP) or code generation: those read TargetProfile.

using System.Text.Json;
using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>The pinned native-toolchain identity of the windows-x64 profile, from the compiler's embedded backend record.</summary>
public static class ToolchainIdentity
{
    // Backend supply identity (SPEC 21.4.7).
    internal const string BackendPackageId = "kimi-backend-windows-x64";
    internal const int BackendAbiVersion = 3;

    public static string LlvmVersion { get; }

    public static string BackendVersion { get; }

    public static string BackendSha256 { get; }

    internal static string Kernel32DefinitionSha256 { get; }

    internal static string Kernel32Sha256 { get; }

    /// <summary>Gets the backend supply: the target profile's reserved supply that the embedded record's library names.</summary>
    internal static TargetSupply Backend { get; }

    /// <summary>Gets the compiler's supplies, one per reserved supply of each row, in the rows' order and each row's.</summary>
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

        // Each supply's material, keyed by (row, supply): the Windows row's kernel32 under the record's kernel32 member and its backend
        // under its library, each one file; the Linux row's c, its record's files.
        using var linuxStream = typeof(ToolchainIdentity).Assembly.GetManifestResourceStream("Kimi.LinuxBackendProfile.json")!;
        using var linux = JsonDocument.Parse(linuxStream);
        var c = linux.RootElement;
        if (c.GetProperty("profile").GetString() != TargetProfile.Linux.Name || TargetProfile.Linux.Supply(c.GetProperty("library").GetString()) is not { } musl ||
            !ArtifactFiles.SequenceEqual(c.GetProperty("providedSymbols"), musl.Symbols))
        {
            throw new InvalidDataException($"The embedded native supply catalog does not match {TargetProfile.Linux.Name}.");
        }

        (TargetProfile Row, string Name, (string Name, string Value)[] Entry, SupplyFile[] Files)[] material =
        [
            (
                TargetProfile.Windows,
                kernel32,
                [("generator", Kernel32Imports.Generator), ("dll", Kernel32Imports.Dll), ("definitionSha256", Kernel32DefinitionSha256)],
                [new("kernel32.lib", "Kimi.Kernel32.lib", Kernel32Sha256, SupplyPlace.Library)]),
            (TargetProfile.Windows, Backend.Name, [("resolution", "toolchain")], [new("kimi_backend_windows_x64_v1.lib", "Kimi.WindowsBackend.lib", BackendSha256, SupplyPlace.Library)]),
            (TargetProfile.Linux, musl.Name, [("resolution", "toolchain")], [.. c.GetProperty("files").EnumerateArray().Select(static x => ReadFile(x))]),
        ];
        var supplies = new List<CompilerSupply>();
        foreach (var row in TargetProfile.Rows)
        {
            supplies.AddRange(material.Where(x => x.Row == row).Select(static x => new CompilerSupply(x.Row, x.Row.Supply(x.Name)!, x.Entry, x.Files)));
            if (!supplies.Where(x => x.Row == row).Select(static x => x.Supply).SequenceEqual(row.Supplies))
            {
                throw new InvalidDataException("The compiler's supplies are not the target profile's reserved supplies.");
            }
        }

        Supplies = [.. supplies];
    }

    /// <summary>Gets the compiler's supply of a row's reserved supply.</summary>
    /// <param name="supply">A row's reserved supply, or null.</param>
    /// <returns>The compiler's supply whose row's supply is <paramref name="supply"/>, or null for null.</returns>
    internal static CompilerSupply? Of(TargetSupply? supply) => Array.Find(Supplies, x => x.Supply == supply);

    /// <summary>Reads a file of the Linux record's supply: its catalog name, its resource (<c>Kimi.Linux.</c> and the name), its SHA-256
    /// and its place, the one read of a place (T's ruling 79).</summary>
    /// <param name="file">The record's file entry.</param>
    /// <returns>The supply file.</returns>
    /// <exception cref="InvalidDataException">The entry's place is missing or none of start, library and end.</exception>
    internal static SupplyFile ReadFile(JsonElement file)
    {
        var name = file.GetProperty("name").GetString()!;
        var place = (file.TryGetProperty("place", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null) switch
        {
            "start" => SupplyPlace.Start,
            "library" => SupplyPlace.Library,
            "end" => SupplyPlace.End,
            _ => throw new InvalidDataException($"The embedded native supply catalog's file '{name}' has no place on an ELF link line (start, library or end)."),
        };
        return new(name, "Kimi.Linux." + name, file.GetProperty("sha256").GetString()!, place);
    }
}

/// <summary>One of the compiler's supplies (IMPL 20.4's reserved supplies), keyed by (row, supply): the row, its reserved supply (name,
/// kind and symbols), the link manifest's fields that identify it after its name and kind (in publication's order) and its files, in
/// the catalog's order.</summary>
/// <param name="Row">The target row.</param>
/// <param name="Supply">The row's reserved supply.</param>
/// <param name="Entry">The manifest entry's identifying fields, in writing order.</param>
/// <param name="Files">The supply's files.</param>
internal sealed record CompilerSupply(TargetProfile Row, TargetSupply Supply, (string Name, string Value)[] Entry, SupplyFile[] Files);

/// <summary>One embedded file of a compiler's supply: its catalog name, whose extension its staged copy keeps, its embedded resource,
/// that resource's SHA-256, the catalog's, and its place on an ELF link line, which only the ELF link writer reads; its stored facts,
/// Tatara's inspection of its bytes read at adoption, are the resource of its resource's name and <c>.facts</c>.</summary>
/// <param name="Name">The file's catalog name.</param>
/// <param name="Resource">The embedded resource holding the file's bytes.</param>
/// <param name="Sha256">The catalog's SHA-256 of those bytes.</param>
/// <param name="Place">The file's place on an ELF link line.</param>
internal sealed record SupplyFile(string Name, string Resource, string Sha256, SupplyPlace Place)
{
    /// <summary>Opens the file's bytes.</summary>
    /// <returns>The embedded resource's stream, which the caller disposes.</returns>
    internal Stream Open() => typeof(SupplyFile).Assembly.GetManifestResourceStream(this.Resource)!;

    /// <summary>Opens the file's stored facts, in <c>Tatara.Inspection</c>'s stored form.</summary>
    /// <returns>The embedded resource's stream, which the caller disposes.</returns>
    internal Stream OpenFacts() => typeof(SupplyFile).Assembly.GetManifestResourceStream(this.Resource + ".facts")!;
}

/// <summary>A compiler's supply file's place on an ELF link line (design §3.11; the catalog's <c>start</c>, <c>library</c> and
/// <c>end</c>): before the object, after the program's libraries, or last. A COFF file's is <c>Library</c>; a program library has
/// none.</summary>
internal enum SupplyPlace : byte
{
    /// <summary>Before the object.</summary>
    Start,

    /// <summary>After the program's libraries.</summary>
    Library,

    /// <summary>Last.</summary>
    End,
}
