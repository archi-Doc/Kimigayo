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
// (src/backend/linux-x64/profile.json, the resource Kimi.LinuxBackendProfile.json), whose files are listed in link order; the staged
// none-x64 row's: kimi_backend, compiler-rt 22.1.8's soft-float archive (Tatara B9e), read from the embedded none record in the Linux
// record's form (src/backend/none-x64/profile.json, the resource Kimi.NoneBackendProfile.json). Each row's backend package (Packages,
// SPEC 21.4.7: its package id, ABI version and archive hash, which the row's link manifest head states; the Tatara plan's B9f) and each
// row's linker script (Scripts: the catalog's linkerScript, one file in SupplyFile's form held by the row, not by a supply, IMPL 20.4;
// the Tatara plan's decision 31).
// Input: the embedded records and libraries. Output: those values, for the native build, publication's manifest and toolchain verify's
// self-check (P13), and each file's stored facts, the resource beside its own (SupplyFile.OpenFacts; SupplyFacts). Invariants: the
// backend record matches the Windows row (its name; its library names a reserved supply, whose symbols are its provided symbols) and
// pins a major.minor.patch LLVM version; the Linux and none records match their rows (ReadCatalog: the name; the library names a
// reserved supply, whose symbols are the provided symbols) and place each file start, library or end and their linkerScript script
// (ReadFile, ReadCatalog); a record of a row with a package identity (PackageIdentities) states that identity and the pinned LLVM
// version, and a row has a package exactly when it reserves the backend supply; a row's script is its catalog's linkerScript (every
// catalog's), named as its LinkerScript column, and a row has one exactly when that column is not null; the supplies are
// every row's reserved supplies (AllRows) in the rows' order, each row's in its order, else the type initializer throws
// InvalidDataException; each resource's SHA-256 is its file's (NativeToolchainTest's catalog row; not checked at a start, stated by
// toolchain verify); a file's facts' first line is its bytes' SHA-256 and their body Tatara's inspection of them (NativeToolchainTest's
// facts row; not checked at a start).
// Must not be read by check (Binding, project configuration, LSP) or code generation: those read TargetProfile.

using System.Text.Json;
using Kimi.Compiler;

namespace Kimi.Build;

/// <summary>The pinned native-toolchain identity of the windows-x64 profile, from the compiler's embedded backend record.</summary>
public static class ToolchainIdentity
{
    // Each backend package's identity (SPEC 21.4.7), by row: the package id and ABI version its record must state.
    private static readonly (TargetProfile Row, string PackageId, int AbiVersion)[] PackageIdentities =
    [
        (TargetProfile.Windows, "kimi-backend-windows-x64", 3),
        (TargetProfile.None, "kimi-backend-none-x64", 1),
    ];

    public static string LlvmVersion { get; }

    public static string BackendVersion { get; }

    public static string BackendSha256 { get; }

    internal static string Kernel32DefinitionSha256 { get; }

    internal static string Kernel32Sha256 { get; }

    /// <summary>Gets the backend supply: the target profile's reserved supply that the embedded record's library names.</summary>
    internal static TargetSupply Backend { get; }

    /// <summary>Gets the compiler's supplies, one per reserved supply of each row, in the rows' order and each row's.</summary>
    internal static CompilerSupply[] Supplies { get; }

    /// <summary>Gets each backend package, one per row reserving the backend supply, in the rows' order: its row, package id, ABI version
    /// and archive's SHA-256, the identity the row's link manifest head states.</summary>
    internal static (TargetProfile Row, string PackageId, int AbiVersion, string ArtifactSha256)[] Packages { get; }

    /// <summary>Gets each row's linker script, one per row whose <see cref="TargetProfile.LinkerScript"/> is not null, in the rows'
    /// order: its catalog name, resource and SHA-256 (its place <see cref="SupplyPlace.Script"/>).</summary>
    internal static (TargetProfile Row, SupplyFile File)[] Scripts { get; }

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

        if (!StatesPackage(root, TargetProfile.Windows) || root.GetProperty("profile").GetString() != TargetProfile.Windows.Name ||
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
        // under its library, each one file; the Linux row's c and the none row's kimi_backend, their catalogs' files (each catalog also
        // gives its row's script).
        (TargetProfile Row, TargetSupply Supply, SupplyFile[] Files, SupplyFile? Script)[] catalogs = [ReadCatalog(TargetProfile.Linux, "Linux"), ReadCatalog(TargetProfile.None, "None")];
        (TargetProfile Row, string Name, (string Name, string Value)[] Entry, SupplyFile[] Files)[] material =
        [
            (
                TargetProfile.Windows,
                kernel32,
                [("generator", Kernel32Imports.Generator), ("dll", Kernel32Imports.Dll), ("definitionSha256", Kernel32DefinitionSha256)],
                [new("kernel32.lib", "Kimi.Kernel32.lib", Kernel32Sha256, SupplyPlace.Library)]),
            (TargetProfile.Windows, Backend.Name, [("resolution", "toolchain")], [new("kimi_backend_windows_x64_v1.lib", "Kimi.WindowsBackend.lib", BackendSha256, SupplyPlace.Library)]),
            .. catalogs.Select(static x => (x.Row, x.Supply.Name, new[] { ("resolution", "toolchain") }, x.Files)),
        ];
        var supplies = new List<CompilerSupply>();
        foreach (var row in TargetProfile.AllRows)
        {
            supplies.AddRange(material.Where(x => x.Row == row).Select(static x => new CompilerSupply(x.Row, x.Row.Supply(x.Name)!, x.Entry, x.Files)));
            if (!supplies.Where(x => x.Row == row).Select(static x => x.Supply).SequenceEqual(row.Supplies))
            {
                throw new InvalidDataException("The compiler's supplies are not the target profile's reserved supplies.");
            }
        }

        Supplies = [.. supplies];
        Scripts = [.. catalogs.Where(static x => x.Script is not null).Select(static x => (x.Row, x.Script!))];
        foreach (var row in TargetProfile.AllRows)
        {
            if (Array.Exists(PackageIdentities, x => x.Row == row) != (row.Supply(Backend.Name) is not null) || row.LinkerScript != Script(row)?.Name)
            {
                throw new InvalidDataException($"The embedded native supply catalog does not match {row.Name}'s backend package or linker script.");
            }
        }

        Packages = [.. PackageIdentities.Select(static x => (x.Row, x.PackageId, x.AbiVersion, Of(x.Row, Backend.Name)!.Files.Single().Sha256))];
    }

    /// <summary>Gets the compiler's supply of a row's reserved supply, keyed by (row, supply name).</summary>
    /// <param name="row">A target row.</param>
    /// <param name="name">A library name.</param>
    /// <returns>The compiler's supply of the row's reserved supply of that name, or null when the row reserves none.</returns>
    internal static CompilerSupply? Of(TargetProfile row, string name) => Array.Find(Supplies, x => x.Row == row && x.Supply.Name == name);

    /// <summary>Gets a row's linker script.</summary>
    /// <param name="row">A target row.</param>
    /// <returns>The row's script, or null for a row linking in the linker's own layout.</returns>
    internal static SupplyFile? Script(TargetProfile row) => Array.Find(Scripts, x => x.Row == row).File;

    /// <summary>Reads a file of a row catalog, a supply's or the row's script: its catalog name, its resource (<c>Kimi.</c>, the catalog's
    /// stem, <c>.</c> and the name), its SHA-256 and its place, the one read of a place (T's rulings 79 and 160).</summary>
    /// <param name="file">The record's file entry or its <c>linkerScript</c>.</param>
    /// <param name="stem">The catalog's resource stem (<c>Linux</c>, <c>None</c>).</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidDataException">The entry's place is missing or none of start, library, end and script.</exception>
    internal static SupplyFile ReadFile(JsonElement file, string stem)
    {
        var name = file.GetProperty("name").GetString()!;
        var place = (file.TryGetProperty("place", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null) switch
        {
            "start" => SupplyPlace.Start,
            "library" => SupplyPlace.Library,
            "end" => SupplyPlace.End,
            "script" => SupplyPlace.Script,
            _ => throw new InvalidDataException($"The embedded native supply catalog's file '{name}' has no place on an ELF link line (start, library, end or script)."),
        };
        return new(name, $"Kimi.{stem}.{name}", file.GetProperty("sha256").GetString()!, place);
    }

    // A row's catalog (src/backend/<row>/profile.json, the resource Kimi.<stem>BackendProfile.json; B7b4's form): its files in link
    // order and its linkerScript, when present, each read by ReadFile (resource Kimi.<stem>.<name>); its profile is the row's name, its
    // library one of the row's reserved supplies, whose symbols are its providedSymbols, its package the row's (StatesPackage), no file
    // is placed script and its script is (T's ruling 160: a supply file never takes a place the ELF line does not emit), else
    // InvalidDataException.
    private static (TargetProfile Row, TargetSupply Supply, SupplyFile[] Files, SupplyFile? Script) ReadCatalog(TargetProfile row, string stem)
    {
        using var stream = typeof(ToolchainIdentity).Assembly.GetManifestResourceStream($"Kimi.{stem}BackendProfile.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        SupplyFile[] files = [.. root.GetProperty("files").EnumerateArray().Select(x => ReadFile(x, stem))];
        var script = root.TryGetProperty("linkerScript", out var entry) ? ReadFile(entry, stem) : null;
        if (root.GetProperty("profile").GetString() != row.Name || row.Supply(root.GetProperty("library").GetString()) is not { } supply ||
            !ArtifactFiles.SequenceEqual(root.GetProperty("providedSymbols"), supply.Symbols) || !StatesPackage(root, row) ||
            Array.Exists(files, static x => x.Place == SupplyPlace.Script) || script is { Place: not SupplyPlace.Script })
        {
            throw new InvalidDataException($"The embedded native supply catalog does not match {row.Name}.");
        }

        return (row, supply, files, script);
    }

    // Whether a record states its row's package identity (PackageIdentities) and the pinned LLVM version, or, for a row without one,
    // no package id.
    private static bool StatesPackage(JsonElement root, TargetProfile row)
    {
        var identity = Array.Find(PackageIdentities, x => x.Row == row);
        return identity.Row is null ? !root.TryGetProperty("packageId", out _) :
            root.GetProperty("packageId").GetString() == identity.PackageId && root.GetProperty("abiVersion").GetInt32() == identity.AbiVersion &&
            root.GetProperty("llvmVersion").GetString() == LlvmVersion;
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

/// <summary>A compiler's file's place on an ELF link line (design §3.11; the catalog's <c>start</c>, <c>library</c> and <c>end</c>):
/// before the object, after the program's libraries, or last; a row's linker script's is <c>Script</c> (the catalog's
/// <c>linkerScript</c>, placed <c>script</c>, -T's argument). A COFF file's is <c>Library</c>; a program library has none.</summary>
internal enum SupplyPlace : byte
{
    /// <summary>Before the object.</summary>
    Start,

    /// <summary>After the program's libraries.</summary>
    Library,

    /// <summary>Last.</summary>
    End,

    /// <summary>The linker script, -T's argument (a row's file, never a supply's).</summary>
    Script,
}
