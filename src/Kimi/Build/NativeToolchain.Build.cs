// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kimi.Compiler;
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
        var ir = ResolvePath(LinkManifest.Text(manifest, "irFile"), directory);
        var optimization = LinkManifest.Text(manifest, "optimization");
        var paths = new ArtifactPaths(ir, path, record, Path.Combine(directory, Path.GetFileNameWithoutExtension(ir) + "." + optimization));
        var configured = llvmBin is null && manifest.TryGetProperty("toolchain", out var toolchain) ? LinkManifest.Text(toolchain, "toolchain.llvmBin") : null;
        var bin = llvmBin is not null ? ResolvePath(llvmBin, Directory.GetCurrentDirectory()) : configured is null ? null : ResolvePath(configured, directory);
        await new NativeBuild(null, paths, manifest, directory, optimization, report, cancellationToken).Run(toolchainRoot, bin);
    }

    // The manifest a native build takes: the fixed head as this compiler writes it (LinkManifest), then an Application's output at O0 or
    // O2 whose external declarations each state a nonempty symbol, once, and its dllimport form (the declarations' one rule, checked
    // before any tool). Every read is LinkManifest's, so a malformed manifest fails here, naming its field.
    internal static void ValidateManifest(JsonElement root)
    {
        LinkManifest.CheckFixed(root);
        if (LinkManifest.Text(root, "optimization") is not ("O0" or "O2") || LinkManifest.Text(root, "outputKind") != "Application" ||
            LinkManifest.Text(root, "entry") != WindowsProfile.EntrySymbol || LinkManifest.Text(root, "subsystem") != WindowsProfile.Subsystem)
        {
            throw new InvalidDataException("Manifest does not describe the supported Windows Application profile.");
        }

        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in LinkManifest.Items(root, "externalDeclarations"))
        {
            var symbol = LinkManifest.Text(declaration, "externalDeclarations.symbol");
            if (symbol.Length == 0)
            {
                throw new InvalidDataException("Invalid external declaration.");
            }

            _ = LinkManifest.Flag(declaration, "externalDeclarations.dllimport");
            if (!symbols.Add(symbol))
            {
                throw new InvalidDataException("Duplicate external declaration.");
            }
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

    // The external functions the module declares, keyed by symbol, with their dllimport form: the manifest's externalDeclarations, which
    // publication writes from the generation result (R9-B1); ValidateManifest checked their shape and that each symbol is declared once.
    private static Dictionary<string, bool> ReadExternalDeclarations(JsonElement manifest)
    {
        var declared = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var declaration in LinkManifest.Items(manifest, "externalDeclarations"))
        {
            declared.Add(LinkManifest.Text(declaration, "externalDeclarations.symbol"), LinkManifest.Flag(declaration, "externalDeclarations.dllimport"));
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
    /// pair's check, and runs the steps once, in order: the pair's identity, the tools, the libraries, the object, its inspection, the
    /// link and the report; the steps fill the record and <see cref="Run"/> writes each of its states, the success last, so a step that
    /// fails, the report included, throws and leaves the record at the last incomplete state written (IMPL 20.3.2).</summary>
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
            WriteRecord(this.paths.Record, this.record);
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
            if (ResolvePath(LinkManifest.Text(this.manifest, "irFile"), this.outputDirectory) != this.paths.Ir ||
                irHash != LinkManifest.Text(this.manifest, "irSha256"))
            {
                throw new InvalidDataException("IR/manifest SHA-256 or path mismatch.");
            }

            if (this.optimization != LinkManifest.Text(this.manifest, "optimization"))
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
            foreach (var item in LinkManifest.Items(this.manifest, "libraries"))
            {
                var name = LinkManifest.Text(item, "libraries.name");
                var kind = LinkManifest.Text(item, "libraries.kind");
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

                if (LinkManifest.OptionalText(item, "libraries.sha256") is { } assertion &&
                    (assertion.Length != 64 || !assertion.Equals(hash, StringComparison.OrdinalIgnoreCase)))
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
                return StageInput(ResolvePath(LinkManifest.Text(item, "libraries.input"), this.outputDirectory), this.paths.Stem, out stagedHash);
            }

            var backend = ToolchainResolver.ResolveBackend(item, toolchainRoot, this.outputDirectory);
            if (!File.Exists(backend))
            {
                throw new InvalidDataException(
                    $"Backend archive not found: {backend}. Run src/backend/windows-x64/build.ps1 for this toolchain, " +
                    "or install the matching compiler toolchain package.");
            }

            if (LinkManifest.OptionalText(item, "libraries.input") is not null)
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

        // The object's dependencies and shape: an undefined symbol is a backend helper, a runtime import or an external the module
        // declares (the manifest's) in the same form; one _fltused definition, unwind information and no default library. Returns the undefined
        // symbols' listing.
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
                declared ??= ReadExternalDeclarations(this.manifest);
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

            return undefined;
        }

        // The executable: linked to a fresh file, so a failed link never publishes a partially written one, then the record's linked
        // state, which Run publishes last.
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
