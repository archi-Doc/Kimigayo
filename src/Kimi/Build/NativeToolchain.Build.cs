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

    // A project's native build. A build reads no toolchain root and no LlvmBin: the options --ToolchainRoot and --LlvmBin (B6b4), a
    // project's LlvmBin and a link manifest's toolchain member (B6b5) are errors, and Kimi reads KIMI_TOOLCHAIN_ROOT nowhere (B6b3)
    // (Tatara designs A4 §2.1, B6b1).
    internal static async Task Build(Project project, ArtifactPaths paths, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.Manifest, cancellationToken));
        ValidateManifest(document.RootElement);
        await new NativeBuild(project, paths, document.RootElement, project.Directory, project.ProjectFile.Optimization, report, cancellationToken).Run();
    }

    // Both CLI entry points consume the same checked artifact pair and native pipeline. A standalone
    // manifest carries expanded user-library assertions; no project reload or compiler initialization is needed. It holds its record's
    // lock (Begin) from its first write to its end.
    internal static async Task BuildManifest(string path, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        var record = Path.ChangeExtension(path, ".build.json");
        using var output = await Begin(record, report, cancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken));
        var manifest = document.RootElement;
        ValidateManifest(manifest);
        var ir = ResolvePath(LinkManifest.Text(manifest, "irFile"), directory);
        var optimization = LinkManifest.Text(manifest, "optimization");
        var paths = new ArtifactPaths(ir, path, record, Path.Combine(directory, Path.GetFileNameWithoutExtension(ir) + "." + optimization));
        await new NativeBuild(null, paths, manifest, directory, optimization, report, cancellationToken).Run();
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
    // original; a staged copy is reused only when its bytes still have that hash. The input is a stream (a program's file or the
    // compiler's resource), which the caller disposes.
    internal static string StageInput(Stream input, string stem, out string hash)
    {
        var directory = stem + ".native";
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var copy = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(copy);
            }

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
    /// calls take with the target values (<see cref="TataraContext"/>). <see cref="Run"/> runs the steps once, in order: the pair's
    /// identity, Tatara (the compiler's, <see cref="Tatara.Instance"/>, identified in the record's tools by its build, ABI and LLVM
    /// version), the libraries (each staged: the compiler's supplies from its resources, a program's from its input), the object, its
    /// inspection, the link to a private file, the report and the commit; the steps fill the record and <see cref="Run"/> writes each of
    /// its states. The commit, after the report and the last cancellation point, writes the success record, which names the private
    /// file's hash, and then moves the file to the executable's path, so an Application publishes its executable only on success (IMPL
    /// 20.3.2; Tatara decision 18): a step that fails before the commit, the report included, throws and leaves the previous executable
    /// and the record at the last incomplete state written, and a failed move writes the invalidation again and rethrows the move's
    /// failure. The private file is deleted on every outcome. IMPL 20.3.2 states the two windows (the owner's decision 19, O-468 (a)):
    /// should the process end between the success record and the move, or the move and the invalidation both fail (one IOException
    /// naming both, the move's as its inner exception), the attempt's success record stays, naming a hash no file at the path has. run
    /// --no-build runs only an executable whose hash the record names, so it refuses that one, and the next build invalidates the
    /// record.</summary>
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

        internal async Task Run()
        {
            var irHash = this.CheckPair();
            var tools = new JsonObject();
            this.record["tools"] = tools;
            this.record["irSha256"] = irHash;
            this.record["optimization"] = this.optimization;
            WriteRecord(this.paths.Record, this.record);
            var tatara = Tatara.Instance;
            tools.Add("tatara", new JsonObject { ["build"] = tatara.Build, ["abi"] = tatara.Abi, ["llvmVersion"] = tatara.LlvmVersion });
            var libraries = this.ResolveLibraries();
            WriteRecord(this.paths.Record, this.record);
            var obj = await this.Compile(tatara);
            var undefined = this.Inspect(tatara, obj);
            var executable = this.paths.Stem + "." + Guid.NewGuid().ToString("N") + ".exe"; // Private until the commit moves it into place.
            try
            {
                this.Link(tatara, obj, libraries, undefined, executable);
                this.context.Report(DiagnosticSeverity.Information, "Built: " + this.paths.Executable);
                this.context.CancellationToken.ThrowIfCancellationRequested();
                WriteRecord(this.paths.Record, this.record);
                try
                {
                    Replace(executable, this.paths.Executable);
                }
                catch (Exception move) when (IsOutputFailure(move))
                {
                    // Two output failures are one output failure naming both. Invalidate raises only output failures (WriteRecord's
                    // file writes and Replace), so any other failure there comes from a defect and propagates as itself, a fault no
                    // catch classifies (C-433 c5; COMPILER_ARCHITECTURE's catch boundaries).
                    try
                    {
                        Invalidate(this.paths.Record);
                    }
                    catch (Exception invalidation) when (IsOutputFailure(invalidation))
                    {
                        throw new IOException($"{move.Message} The build record was not invalidated again: {invalidation.Message}", move);
                    }

                    throw;
                }
            }
            finally
            {
                File.Delete(executable);
            }
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

        // The manifest's libraries in its order, each staged (the file the linker receives), checked against its assertions and
        // identified in the record by its staged path and hash (SPEC 20.4). A reserved name is the compiler's supply: its entry the one
        // this compiler writes (LinkManifest) and its bytes the compiler's (ToolchainIdentity.Supplies); any other is a program's, staged
        // from its input and checked against the project's supply. Every compiler's supply is listed.
        private List<string> ResolveLibraries()
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

                var supply = Array.Find(ToolchainIdentity.Supplies, x => x.Name == name);
                if (supply is not null)
                {
                    LinkManifest.CheckCompilerSupply(item, supply);
                }

                using var input = supply is null ? File.OpenRead(ResolvePath(LinkManifest.Text(item, "libraries.input"), this.outputDirectory)) : supply.Open();
                var path = StageInput(input, this.paths.Stem, out var hash);
                if (this.project is not null && supply is null)
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

            if (!Array.TrueForAll(ToolchainIdentity.Supplies, x => seen.Contains(x.Name)))
            {
                throw new InvalidDataException("Missing kernel32 or kimi_backend library.");
            }

            this.record["libraries"] = libraryIdentities;
            return libraries;
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

        // The executable: linked to Run's private file, so neither a failed link nor any failure before the commit publishes it, then the
        // record's linked state, naming the private file's hash, which Run's commit publishes before the file.
        private void Link(Tatara tatara, string obj, List<string> libraries, string undefined, string executable)
        {
            var link = new string[libraries.Count + 6];
            link[0] = obj;
            libraries.CopyTo(link, 1);
            link[^5] = "/entry:" + WindowsProfile.EntrySymbol;
            link[^4] = "/subsystem:" + WindowsProfile.Subsystem;
            link[^3] = "/nodefaultlib";
            link[^2] = "/Brepro";
            link[^1] = "/out:" + executable;
            tatara.Link(this.context, link);
            this.context.CancellationToken.ThrowIfCancellationRequested();
            this.record["status"] = "linked";
            this.record["executable"] = Path.GetFileName(this.paths.Executable);
            this.record["executableSha256"] = Hash(executable);
            this.record["objectUndefinedSymbols"] = undefined;
        }
    }
}
