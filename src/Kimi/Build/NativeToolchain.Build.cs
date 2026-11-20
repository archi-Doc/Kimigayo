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
    // Actual object dependencies may only be backend helpers or the runtime's kernel32 imports (SPEC 21.4.7, 21.4.7).
    private static readonly HashSet<string> AllowedUndefined = CreateAllowedUndefined();

    // A project's native build. A build reads no LlvmBin: the command line's names the tools toolchain verify checks, and a project's is
    // accepted and not read (Tatara design A4 §2.1; removed in B6).
    internal static async Task Build(Project project, ArtifactPaths paths, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.Manifest, cancellationToken));
        ValidateManifest(document.RootElement);
        var build = new NativeBuild(project, paths, document.RootElement, project.Directory, project.ProjectFile.Optimization, report, cancellationToken);
        await build.Run(project.KimiOptions.ToolchainRoot);
    }

    // Both CLI entry points consume the same checked artifact pair and native pipeline. A standalone
    // manifest carries expanded user-library assertions; no project reload or compiler initialization is needed. Its toolchain.llvmBin is
    // accepted and not read, as a project's LlvmBin.
    internal static async Task BuildManifest(string path, string? toolchainRoot, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
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
        await new NativeBuild(null, paths, manifest, directory, optimization, report, cancellationToken).Run(toolchainRoot);
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

    /// <summary>One native build of a checked IR/manifest pair: its inputs, its record and its LLVM and LLD work, which Tatara's calls do in
    /// process (no tool is launched). The constructor takes the build's fixed inputs: the project whose supplies it checks (none for a
    /// standalone manifest), the pair, the root its records redact, the optimization and the host's report and cancellation, which Tatara's
    /// calls take with the target values (<see cref="TataraContext"/>). <see cref="Run"/> takes the toolchain root's selection, which it
    /// resolves after the pair's check, and runs the steps once, in order: the pair's identity, Tatara (the root's tatara.dll, loaded and
    /// checked, identified in the record's tools), the libraries, the object, its inspection, the link and the report; the steps fill the
    /// record and <see cref="Run"/> writes each of its states, the success last, so a step that fails, the report included, throws and
    /// leaves the record at the last incomplete state written (IMPL 20.3.2).</summary>
    private sealed class NativeBuild
    {
        private readonly Project? project;
        private readonly ArtifactPaths paths;
        private readonly JsonElement manifest;
        private readonly string directory; // The root that published records and files redact.
        private readonly string outputDirectory;
        private readonly string optimization;
        private readonly TataraContext context; // The host's report and cancellation, with the target values.
        private readonly JsonObject record = CreateBuildRecord();

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
            this.context = new TataraContext(
                report, cancellationToken, WindowsProfile.Target, WindowsProfile.Cpu, WindowsProfile.Features, WindowsProfile.RelocationModel, WindowsProfile.CodeModel);
        }

        internal async Task Run(string? configuredRoot)
        {
            var irHash = this.CheckPair();
            var toolchainRoot = ToolchainResolver.ResolveRoot(configuredRoot);
            var tools = new JsonObject();
            this.record["tools"] = tools;
            this.record["irSha256"] = irHash;
            this.record["optimization"] = this.optimization;
            WriteRecord(this.paths.Record, this.record);
            var tatara = Tatara.Load(toolchainRoot);
            tools.Add("tatara", new JsonObject
            {
                ["path"] = Redact(tatara.Path, this.directory), ["build"] = tatara.Build, ["abi"] = tatara.Abi, ["llvmVersion"] = tatara.LlvmVersion,
            });
            var libraries = this.ResolveLibraries(toolchainRoot);
            WriteRecord(this.paths.Record, this.record);
            var obj = await this.Compile(tatara);
            var undefined = this.Inspect(tatara, obj);
            this.Link(tatara, obj, libraries, undefined);
            this.context.Report(DiagnosticSeverity.Information, "Built: " + this.paths.Executable);
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

        // The pair's identity: the manifest names this IR by path, hash and optimization, on a platform Tatara runs on.
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
        private async Task<string> Compile(Tatara tatara)
        {
            var selectedIr = this.paths.Ir;
            if (this.optimization == "O2")
            {
                selectedIr = this.paths.Stem + ".ll";
                tatara.Optimize(this.context, this.paths.Ir, selectedIr);
                var ir = await File.ReadAllTextAsync(selectedIr, this.context.CancellationToken);
                ir = ModulePathPattern().Replace(ir, m => Redact(m.Value, this.directory));
                await File.WriteAllTextAsync(selectedIr, ir, Utf8, this.context.CancellationToken);
            }

            var obj = this.paths.Stem + ".obj";
            tatara.Emit(this.context, selectedIr, obj, this.optimization[1] - '0');
            return obj;
        }

        // The object's dependencies and shape: an undefined symbol is a backend helper, a runtime import or an external the module
        // declares (the manifest's) in the same form; one _fltused definition, unwind information and no default library. Returns the
        // record's form of the undefined symbols: llvm-nm's posix lines, in which an undefined symbol's value and size are 0.
        private string Inspect(Tatara tatara, string obj)
        {
            var facts = tatara.Inspect(this.context, obj);
            Dictionary<string, bool>? declared = null;
            var lines = new string[facts.Undefined.Count];
            for (var i = 0; i < lines.Length; i++)
            {
                var symbol = facts.Undefined[i].Name;
                lines[i] = symbol + " " + facts.Undefined[i].Kind + " 0 0";
                if (AllowedUndefined.Contains(symbol))
                {
                    continue;
                }

                // SPEC 22.6: a foreign import is referenced through __imp_ when dllimport and directly otherwise.
                declared ??= ReadExternalDeclarations(this.manifest);
                var imported = symbol.StartsWith("__imp_", StringComparison.Ordinal);
                if (!declared.TryGetValue(imported ? symbol[6..] : symbol, out var dllimport) || dllimport != imported)
                {
                    throw new InvalidDataException("Unsupported actual object dependency: " + lines[i]);
                }
            }

            var markers = facts.DefinedExternal.Count(s => s.Name == WindowsProfile.FloatMarker && s.Kind is 'B' or 'D');
            if (markers != 1 || !facts.HasUnwind || facts.HasDefaultLibrary)
            {
                throw new InvalidDataException("Invalid _fltused definition, unwind information or hidden default library.");
            }

            return string.Join('\n', lines);
        }

        // The executable: linked to a fresh file, so a failed link never publishes a partially written one, then the record's linked
        // state, which Run publishes last.
        private void Link(Tatara tatara, string obj, List<string> libraries, string undefined)
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
                tatara.Link(this.context, link);
                this.context.CancellationToken.ThrowIfCancellationRequested();
                Replace(temporary, this.paths.Executable);
                this.record["status"] = "linked";
                this.record["executable"] = Path.GetFileName(this.paths.Executable);
                this.record["executableSha256"] = Hash(this.paths.Executable);
                this.record["objectUndefinedSymbols"] = undefined;
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }
}
