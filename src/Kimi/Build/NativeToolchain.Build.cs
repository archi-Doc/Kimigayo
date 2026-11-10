// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Diagnostics;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

internal static partial class NativeToolchain
{
    private static readonly string[] ToolNames = ["opt", "llc", "lld-link", "llvm-nm", "llvm-readobj"];

    // Actual object dependencies may only be backend helpers or the runtime's kernel32 imports (SPEC 21.4.7, 21.4.7).
    private static readonly HashSet<string> AllowedUndefined = CreateAllowedUndefined();

    internal static async Task Build(Project project, ArtifactPaths paths, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.Manifest, cancellationToken));
        ValidateManifest(document.RootElement);
        var configuredBin = project.KimiOptions.LlvmBin ?? project.ProjectFile.LlvmBin;
        var projectBin = project.KimiOptions.LlvmBin is null && project.Directory.Length != 0; // A project's own setting is relative to the project.
        var bin = configuredBin is null ? null : ResolvePath(configuredBin, projectBin ? Path.GetFullPath(project.Directory) : Directory.GetCurrentDirectory());
        var optimization = project.ProjectFile.Optimization;
        var build = new NativeBuild(project, paths, document.RootElement, project.Directory, optimization, report, cancellationToken);
        await build.Run(project.KimiOptions.ToolchainRoot, bin);
    }

    // Both CLI entry points consume the same checked artifact pair and native pipeline. A standalone
    // manifest carries expanded user-library assertions; no project reload or compiler initialization is needed.
    internal static async Task BuildManifest(
        string path, string? toolchainRoot, string? llvmBin, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        var record = Path.ChangeExtension(path, ".build.json");
        WriteRecord(record, CreateBuildRecord());
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken));
        var manifest = document.RootElement;
        ValidateManifest(manifest);
        var ir = ResolvePath(manifest.GetProperty("irFile").GetString()!, directory);
        var optimization = manifest.GetProperty("codegen").GetProperty("optimization").GetString()!;
        var paths = new ArtifactPaths(ir, path, record, Path.Combine(directory, Path.GetFileNameWithoutExtension(ir) + "." + optimization));
        var bin = llvmBin is not null ? ResolvePath(llvmBin, Directory.GetCurrentDirectory()) :
            manifest.TryGetProperty("toolchain", out var configured) && configured.TryGetProperty("llvmBin", out var value) ?
                ResolvePath(value.GetString()!, directory) : null;
        await new NativeBuild(null, paths, manifest, directory, optimization, report, cancellationToken).Run(toolchainRoot, bin);
    }

    internal static void ValidateManifest(JsonElement root)
    {
        if (root.GetProperty("schemaVersion").GetInt32() != 3)
        {
            throw new InvalidDataException("Link manifest schema 3 is required. Re-emit the manifest for automatic backend toolchain resolution.");
        }

        var code = root.GetProperty("codegen");
        var support = root.GetProperty("backendSupport");
        var application = States(
            root,
            ("target", WindowsProfile.Target),
            ("outputKind", "Application"),
            ("entry", WindowsProfile.EntrySymbol),
            ("subsystem", WindowsProfile.Subsystem));
        if (!application || !StatesCodegen(code))
        {
            throw new InvalidDataException("Manifest does not describe the supported Windows Application profile.");
        }

        var valid = StatesSupport(support) && SequenceEqual(root.GetProperty("providedRuntimeSymbols"), [WindowsProfile.FloatMarker]);
        foreach (var dependency in root.GetProperty("expectedUndefinedSymbols").EnumerateArray())
        {
            valid &= dependency.GetProperty("provider").GetString() == WindowsProfile.BackendLibrary &&
                Array.IndexOf(WindowsProfile.ProvidedSymbols, dependency.GetProperty("symbol").GetString()) >= 0;
        }

        if (!valid)
        {
            throw new InvalidDataException("Invalid backend supply identity or runtime dependency.");
        }
    }

    // SPEC 20.4: the linker receives a fixed snapshot named by its content hash, never the mutable
    // original; a staged copy is reused only when its bytes still have that hash.
    internal static string StageInput(string input, string stem, out string hash)
    {
        var directory = stem + ".native";
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(input, temporary);
            hash = Hash(temporary);
            var staged = Path.Combine(directory, hash + ".lib");
            if (!File.Exists(staged) || Hash(staged) != hash)
            {
                Replace(temporary, staged);
            }

            return staged;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // Whether a manifest's codegen section restates the profile's code generation settings and the pinned LLVM release, at O0 or O2.
    private static bool StatesCodegen(JsonElement code)
        => States(code, ("profile", WindowsProfile.Name), ("llvmVersion", ToolchainIdentity.LlvmVersion), ("cpu", WindowsProfile.Cpu)) &&
            SequenceEqual(code.GetProperty("features"), [WindowsProfile.Features]) &&
            States(
                code,
                ("relocationModel", WindowsProfile.RelocationModel),
                ("codeModel", WindowsProfile.CodeModel),
                ("unwindTables", WindowsProfile.UnwindTables)) &&
            code.GetProperty("optimization").GetString() is "O0" or "O2";

    // Whether a manifest's backendSupport section is the adopted backend supply's identity (SPEC 21.4.7).
    private static bool StatesSupport(JsonElement support)
        => States(support, ("packageId", ToolchainIdentity.BackendPackageId)) &&
            support.GetProperty("abiVersion").GetInt32() == ToolchainIdentity.BackendAbiVersion &&
            States(
                support,
                ("packageVersion", ToolchainIdentity.BackendVersion),
                ("artifactSha256", ToolchainIdentity.BackendSha256),
                ("library", WindowsProfile.BackendLibrary)) &&
            SequenceEqual(support.GetProperty("providedSymbols"), WindowsProfile.ProvidedSymbols);

    // The external functions of the hash-verified pre-optimization IR, keyed by symbol, with their dllimport form.
    private static Dictionary<string, bool> ReadExternalDeclarations(string ir)
    {
        var declared = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(ir))
        {
            // A quoted name escapes every quote, so its closing quote is the next one.
            if (line.StartsWith("declare ", StringComparison.Ordinal) && line.IndexOf('@') is var at and >= 0 &&
                (at + 1 < line.Length && line[at + 1] == '"' ? line.IndexOf('"', at + 2) + 1 : line.IndexOf('(', at)) is var end and > 0)
            {
                declared[LlvmModuleText.SymbolFromName(line.AsSpan(at + 1, end - at - 1))] = line.StartsWith("declare dllimport ", StringComparison.Ordinal);
            }
        }

        return declared;
    }

    private static HashSet<string> CreateAllowedUndefined()
    {
        var allowed = new HashSet<string>(WindowsProfile.ProvidedSymbols, StringComparer.Ordinal);
        foreach (var (symbol, _) in WindowsProfile.RuntimeDeclarations)
        {
            allowed.Add("__imp_" + symbol);
        }

        return allowed;
    }

    [GeneratedRegex(@"(?m)^(?:; ModuleID = .*|source_filename = .*|!\d+ = .*!DIFile\(.*)$")]
    private static partial Regex ModulePathPattern();

    [GeneratedRegex("(?m)^" + WindowsProfile.FloatMarker + " [BD] ")]
    private static partial Regex FloatMarkerPattern();

    /// <summary>One native build of a checked IR/manifest pair: its inputs, its record and its tools. The constructor takes the build's
    /// fixed inputs: the project whose supplies it checks (none for a standalone manifest), the pair, the root its records redact, the
    /// optimization and the host's report and cancellation. <see cref="Run"/> takes the toolchain selection, which it resolves after the
    /// pair's check, and runs the steps once, in order: the pair's identity, the tools, the libraries, the object, its inspection and the
    /// link; a step that fails throws and leaves the record at the last state written.</summary>
    private sealed class NativeBuild
    {
        private readonly Project? project;
        private readonly ArtifactPaths paths;
        private readonly JsonElement manifest;
        private readonly string directory; // The root that published records and files redact.
        private readonly string outputDirectory;
        private readonly string optimization;
        private readonly Action<DiagnosticSeverity, string> report;
        private readonly CancellationToken cancellationToken;
        private readonly JsonObject record = CreateBuildRecord();
        private readonly Dictionary<string, string> tools = new(StringComparer.Ordinal);

        internal NativeBuild(
            Project? project,
            ArtifactPaths paths,
            JsonElement manifest,
            string directory,
            string optimization,
            Action<DiagnosticSeverity, string> report,
            CancellationToken cancellationToken)
        {
            this.project = project;
            this.paths = paths;
            this.manifest = manifest;
            this.directory = directory;
            this.outputDirectory = Path.GetDirectoryName(paths.Manifest)!;
            this.optimization = optimization;
            this.report = report;
            this.cancellationToken = cancellationToken;
        }

        internal async Task Run(string? configuredRoot, string? configuredBin)
        {
            var irHash = this.CheckPair();
            var toolchainRoot = ToolchainResolver.ResolveRoot(configuredRoot);
            var identities = new JsonObject();
            this.record["tools"] = identities;
            this.record["irSha256"] = irHash;
            this.record["optimization"] = this.optimization;
            WriteRecord(this.paths.Record, this.record);
            this.ResolveTools(configuredBin ?? toolchainRoot, identities);
            var libraries = this.ResolveLibraries(toolchainRoot);
            WriteRecord(this.paths.Record, this.record);
            var obj = await this.Compile();
            var undefined = await this.Inspect(obj);
            await this.Link(obj, libraries, undefined);
            this.report(DiagnosticSeverity.Information, "Built: " + this.paths.Executable);
        }

        // SPEC 20.4-2: another supply must be this project's own, with its expanded Kind and the
        // requirement/supply hash assertions checked against the actual bytes that are linked.
        private static void CheckSupply(Project project, string name, string? kind, string? hash)
        {
            var settings = project.ProjectFile;
            var supply = settings.NativeLibraries.TryGetValue(WindowsProfile.Target, out var supplies) ? supplies.GetValueOrDefault(name) : null;
            var (expectedKind, expectedHash) = supply is null ? (null, null) : NativeConfiguration.Expand(settings, WindowsProfile.Target, name, supply);
            if (supply is null || expectedKind != kind || (expectedHash is not null && !expectedHash.Equals(hash, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Native library '{name}' does not match its NativeLibraries Kind or Sha256 assertion.");
            }
        }

        // The pair's identity: the manifest names this IR by path, hash and optimization, on a platform the tools run on.
        private string CheckPair()
        {
            var irHash = Hash(this.paths.Ir);
            if (ResolvePath(this.manifest.GetProperty("irFile").GetString()!, this.outputDirectory) != this.paths.Ir ||
                irHash != this.manifest.GetProperty("irSha256").GetString())
            {
                throw new InvalidDataException("IR/manifest SHA-256 or path mismatch.");
            }

            if (this.optimization != this.manifest.GetProperty("codegen").GetProperty("optimization").GetString())
            {
                throw new InvalidDataException("Project and manifest optimization differ; re-emit the inputs.");
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Native build currently requires Windows.");
            }

            return irHash;
        }

        // The build's tools, from the selected bin directory, each identified in the record.
        private void ResolveTools(string bin, JsonObject identities)
        {
            foreach (var name in ToolNames)
            {
                var tool = RequireInstalledFile(Path.Combine(bin, name + ".exe"));
                this.tools.Add(name, tool);
                identities.Add(name, new JsonObject { ["path"] = Redact(tool, this.directory) });
            }
        }

        // The manifest's libraries in its order, each the file the linker receives, checked against its assertions and identified in
        // the record (SPEC 20.4).
        private List<string> ResolveLibraries(string toolchainRoot)
        {
            var libraries = new List<string>();
            var libraryIdentities = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in this.manifest.GetProperty("libraries").EnumerateArray())
            {
                var name = item.GetProperty("name").GetString()!;
                var kind = item.GetProperty("kind").GetString();
                if (!seen.Add(name) || kind is not ("import" or "static"))
                {
                    throw new InvalidDataException("Invalid or duplicate native library.");
                }

                var path = this.ResolveLibrary(item, name, toolchainRoot, out var hash);
                if ((name == WindowsProfile.Kernel32Library && kind != "import") || (name == WindowsProfile.BackendLibrary && kind != "static"))
                {
                    throw new InvalidDataException("Native library kind mismatch.");
                }

                if (this.project is not null && name is not (WindowsProfile.Kernel32Library or WindowsProfile.BackendLibrary))
                {
                    CheckSupply(this.project, name, kind, hash);
                }

                if (item.TryGetProperty("sha256", out var assertion) &&
                    (assertion.GetString() is not { Length: 64 } expected || !expected.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException($"Native library '{name}' does not match its manifest Sha256 assertion.");
                }

                libraries.Add(path);
                libraryIdentities.Add((JsonNode)new JsonObject { ["name"] = name, ["path"] = Redact(path, this.directory), ["sha256"] = hash });
            }

            if (!seen.Contains(WindowsProfile.Kernel32Library) || !seen.Contains(WindowsProfile.BackendLibrary))
            {
                throw new InvalidDataException("Missing kernel32 or kimi_backend library.");
            }

            this.record["libraries"] = libraryIdentities;
            return libraries;
        }

        // One library's file by its supply: the toolchain's generated kernel32 import library; the backend archive, the toolchain's or
        // an explicit input; or a project's supply. An input is staged, and its hash is the staged bytes' (SPEC 20.4).
        private string ResolveLibrary(JsonElement item, string name, string toolchainRoot, out string? stagedHash)
        {
            stagedHash = null;
            if (name == WindowsProfile.Kernel32Library)
            {
                Kernel32Imports.ValidateManifest(item);
                var kernel = RequireInstalledFile(Path.Combine(toolchainRoot, "windows_x64", "kernel32.lib"));
                this.record["kernel32"] = new JsonObject
                {
                    ["generator"] = Kernel32Imports.Generator, ["dll"] = Kernel32Imports.Dll, ["definitionSha256"] = Kernel32Imports.DefinitionSha256,
                    ["path"] = Redact(kernel, this.directory),
                };
                return kernel;
            }

            if (name != WindowsProfile.BackendLibrary)
            {
                return StageInput(ResolvePath(item.GetProperty("input").GetString()!, this.outputDirectory), this.paths.Stem, out stagedHash);
            }

            var backend = ToolchainResolver.ResolveBackend(item, toolchainRoot, this.outputDirectory);
            if (!File.Exists(backend))
            {
                throw new InvalidDataException(
                    $"Backend archive not found: {backend}. Run src/backend/windows-x64/build.ps1 for this toolchain, " +
                    "or install the matching compiler toolchain package.");
            }

            if (item.TryGetProperty("input", out _))
            {
                backend = StageInput(backend, this.paths.Stem, out stagedHash);
                if (stagedHash != ToolchainIdentity.BackendSha256)
                {
                    throw new InvalidDataException("Explicit backend input SHA-256 mismatch.");
                }
            }

            return backend;
        }

        // The object: the IR verified, for O2 optimized, its module paths redacted and verified again, then compiled.
        private async Task<string> Compile()
        {
            await this.Tool("opt", "-passes=verify", "-disable-output", this.paths.Ir);
            var selectedIr = this.paths.Ir;
            if (this.optimization == "O2")
            {
                selectedIr = this.paths.Stem + ".ll";
                await this.Tool("opt", "-S", "-passes=default<O2>", "-mtriple=" + WindowsProfile.Target, this.paths.Ir, "-o", selectedIr);
                var ir = await File.ReadAllTextAsync(selectedIr, this.cancellationToken);
                ir = ModulePathPattern().Replace(ir, m => Redact(m.Value, this.directory));
                await File.WriteAllTextAsync(selectedIr, ir, Utf8, this.cancellationToken);
                await this.Tool("opt", "-passes=verify", "-disable-output", selectedIr);
            }

            var obj = this.paths.Stem + ".obj";
            string[] arguments =
            [
                "-" + this.optimization, "-filetype=obj", "-mtriple=" + WindowsProfile.Target, "-mcpu=" + WindowsProfile.Cpu,
                "-mattr=" + WindowsProfile.Features, "-relocation-model=" + WindowsProfile.RelocationModel,
                "-code-model=" + WindowsProfile.CodeModel, selectedIr, "-o", Path.GetFileName(obj),
            ];
            await this.Tool("llc", arguments);
            return obj;
        }

        // The object's dependencies and shape: an undefined symbol is a backend helper, a runtime import or an external the IR declares
        // in the same form; one _fltused definition, unwind information and no default library. Returns the undefined symbols' listing.
        private async Task<string> Inspect(string obj)
        {
            var undefined = await this.Tool("llvm-nm", "--undefined-only", "--format=posix", obj);
            Dictionary<string, bool>? declared = null;
            foreach (var line in undefined.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.AsSpan().IndexOfAny(' ', '\t');
                var symbol = separator < 0 ? line : line[..separator];
                if (AllowedUndefined.Contains(symbol))
                {
                    continue;
                }

                // SPEC 22.6: a foreign import is referenced through __imp_ when dllimport and directly otherwise.
                declared ??= ReadExternalDeclarations(this.paths.Ir);
                var imported = symbol.StartsWith("__imp_", StringComparison.Ordinal);
                if (!declared.TryGetValue(imported ? symbol[6..] : symbol, out var dllimport) || dllimport != imported)
                {
                    throw new InvalidDataException("Unsupported actual object dependency: " + line);
                }
            }

            var defined = await this.Tool("llvm-nm", "--defined-only", "--extern-only", "--format=posix", obj);
            var inspection = await this.Tool("llvm-readobj", "--unwind", "--coff-directives", obj);
            if (FloatMarkerPattern().Count(defined) != 1 || !inspection.Contains("RuntimeFunction", StringComparison.Ordinal) ||
                inspection.Contains("DEFAULTLIB", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Invalid _fltused definition, unwind information or hidden default library.");
            }

            await File.WriteAllTextAsync(this.paths.Stem + ".inspection.txt", Redact(inspection, this.directory), this.cancellationToken);
            return undefined;
        }

        // The executable: linked to a fresh file, so a failed link never publishes a partially written one, then the record's linked state.
        private async Task Link(string obj, List<string> libraries, string undefined)
        {
            var temporary = this.paths.Stem + "." + Guid.NewGuid().ToString("N") + ".exe";
            try
            {
                var link = new string[libraries.Count + 6];
                link[0] = obj;
                libraries.CopyTo(link, 1);
                link[^5] = "/entry:" + WindowsProfile.EntrySymbol;
                link[^4] = "/subsystem:" + WindowsProfile.Subsystem;
                link[^3] = "/nodefaultlib";
                link[^2] = "/Brepro";
                link[^1] = "/out:" + temporary;
                await this.Tool("lld-link", link);
                this.cancellationToken.ThrowIfCancellationRequested();
                Replace(temporary, this.paths.Executable);
                this.record["status"] = "linked";
                this.record["executable"] = Path.GetFileName(this.paths.Executable);
                this.record["executableSha256"] = Hash(this.paths.Executable);
                this.record["objectUndefinedSymbols"] = undefined.Trim();
                WriteRecord(this.paths.Record, this.record);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private Task<string> Tool(string name, params string[] args)
            => ExecuteTool(this.tools[name], args, this.outputDirectory, this.report, this.cancellationToken);
    }
}
