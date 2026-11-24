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
        new NativeBuild(project, paths, document.RootElement, project.Directory, project.ProjectFile.Optimization, report, cancellationToken).Run();
    }

    // Both CLI entry points consume the same checked artifact pair and native pipeline. A standalone
    // manifest carries expanded user-library assertions; no project reload or compiler initialization is needed. It builds the pair as
    // emit writes it: every path is named from the manifest's own name (ArtifactPaths), so its IR is the Name.ll beside it (CheckPair).
    // It holds its record's lock (Begin) from its first write to its end.
    internal static async Task BuildManifest(string path, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var ir = ArtifactPaths.IrOf(path);
        using var output = await Begin(ArtifactPaths.RecordOf(ir), report, cancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken));
        var manifest = document.RootElement;
        ValidateManifest(manifest);
        var optimization = LinkManifest.Text(manifest, "optimization");
        new NativeBuild(null, ArtifactPaths.Create(ir, optimization), manifest, Path.GetDirectoryName(path)!, optimization, report, cancellationToken).Run();
    }

    // The manifest a native build takes: the fixed head as this compiler writes it (LinkManifest), then an Application's output at O0 or
    // O2 whose external declarations each state a nonempty symbol, once, and its dllimport form (the declarations' one rule, checked
    // before any tool). Every read is LinkManifest's, so a malformed manifest fails here, naming its field.
    internal static void ValidateManifest(JsonElement root)
    {
        LinkManifest.CheckFixed(root);
        if (LinkManifest.Text(root, "optimization") is not ("O0" or "O2") || LinkManifest.Text(root, "outputKind") != "Application" ||
            LinkManifest.Text(root, "entry") != TargetProfile.Windows.EntrySymbol || LinkManifest.Text(root, "subsystem") != TargetProfile.Windows.Subsystem)
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

    // IMPL 20.4: the linker receives a fixed snapshot named by its content hash, never the mutable original; a staged copy is reused
    // only when its bytes still have that hash (HoldStaged). A program's input is copied first and named by the copy's hash; a compiler's
    // supply is found by its catalog hash (StageSupply). The input is a stream (a program's file or the compiler's resource), which the
    // caller disposes; the folder is the build's staged inputs (ArtifactPaths.Native). The result is the staged copy held (HoldStaged),
    // which the caller releases after the link; a copy this call moved into place and then finds changed or gone at its hold is an
    // output failure (StagedHold).
    internal static FileStream StageInput(Stream input, string folder, out string hash)
    {
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var copy = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(copy);
            }

            hash = Hash(temporary);
            if (HoldStaged(folder, hash, out var staged) is { } held)
            {
                return held;
            }

            Replace(temporary, staged);
            return HoldStaged(folder, hash, out _) ?? throw new IOException($"The staged library changed before the build held it: {staged}.");
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // A compiler's supply staged (IMPL 20.4; LC3 S1): the copy its catalog hash names is reused, held, when its bytes, the whole file
    // hashed, still have that hash, and the resource is not opened; otherwise the resource is staged as an input is, named by the copy's
    // hash. The build compares nothing with the catalog: the catalog row test owns the resource's hash (DECISIONS T-M8). The result is the
    // held copy, as StageInput's.
    internal static FileStream StageSupply(CompilerSupply supply, string folder, out string hash)
    {
        if (HoldStaged(folder, supply.Sha256, out _) is { } held)
        {
            hash = supply.Sha256;
            return held;
        }

        using var input = supply.Open();
        return StageInput(input, folder, out hash);
    }

    // IMPL 20.4's reuse rule and the hold's start in one step (StagedHold; DECISIONS T-M17's structural start): the staged copy the hash
    // names, <stem>.native/<hash>.lib, is opened for reading with read sharing only and hashed from that handle; when its bytes still have
    // that hash it is returned open, held, so until the caller releases it no write, replacement or deletion of it, no rename of its
    // folder and no open without read sharing succeeds. LLD opens its inputs for reading and shares read, write and delete, so the hold
    // admits it (StagedHold's design §3). A missing copy or other bytes give null; a failed open or read propagates, the handle closed.
    private static FileStream? HoldStaged(string folder, string hash, out string staged)
    {
        staged = Path.Combine(folder, hash + ".lib");
        if (!File.Exists(staged))
        {
            return null;
        }

        var file = File.OpenRead(staged);
        var held = false;
        try
        {
            held = Hash(file) == hash;
            return held ? file : null;
        }
        finally
        {
            if (!held)
            {
                file.Dispose();
            }
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

    // The backend supply's symbols, linked directly, and each runtime declaration in its dllimport form (kernel32's __imp_ symbol).
    private static HashSet<string> CreateAllowedUndefined()
    {
        var allowed = new HashSet<string>(ToolchainIdentity.Backend.Symbols, StringComparer.Ordinal);
        foreach (var (symbol, _) in TargetProfile.Windows.RuntimeDeclarations)
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
    /// identity, with the input the manifest names, resolved as its other paths are (absent for a pair emitted from memory or by an
    /// earlier compiler), Tatara (the compiler's, <see cref="Tatara.Instance"/>, identified in the record's tools by its build, ABI and LLVM
    /// version), the libraries (each staged: the compiler's supplies found by their catalog hash or staged from their resources, a program's
    /// from its input), the object, its inspection, the link to a private file, the report and the commit; the steps fill the record and
    /// <see cref="Run"/> writes each of its states. The commit, after the report and the last cancellation point, writes the success record,
    /// which names the private file's hash, and then moves the file to the executable's path, so an Application publishes its executable
    /// only on success (IMPL 20.3.2; Tatara decision 18): a step that fails before the commit, the report included, throws and leaves the
    /// previous executable and the record at the last incomplete state written, and a failed move writes the invalidation again and rethrows
    /// the move's failure. The private file is deleted on every outcome. IMPL 20.3.2 states the two windows (the owner's decision 19, O-468
    /// (a)): should the process end between the success record and the move, or the move and the invalidation both fail (one IOException
    /// naming both, the move's as its inner exception), the attempt's success record stays, naming a hash no file at the path has. run
    /// --no-build runs only an executable whose hash the record names, so it refuses that one, and the next build invalidates the record.
    /// From the pair's hash to its end the build holds its IR open for reading only (G1202), and each staged library from its check, released
    /// with the private file on every outcome (StagedHold): a write, replacement or deletion of either fails meanwhile, so Tatara compiles
    /// and LLD links the bytes the record names.</summary>
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
                report, cancellationToken, TargetProfile.Windows.Target, TargetProfile.Windows.Cpu, TargetProfile.Windows.Features, TargetProfile.Windows.RelocationModel, TargetProfile.Windows.CodeModel);
        }

        internal void Run()
        {
            // The pair's IR, held with read sharing only from its hash to the build's end: no writer changes or replaces it before Tatara
            // reads it, so Tatara compiles the bytes the record names (G1202). The platform is checked after the hash, so a mismatched pair
            // fails as itself on every platform.
            this.CheckPair();
            using var ir = File.OpenRead(this.paths.Ir);
            var irHash = Hash(ir);
            if (irHash != LinkManifest.Text(this.manifest, "irSha256"))
            {
                throw new InvalidDataException("IR/manifest SHA-256 mismatch.");
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Native build currently requires Windows.");
            }

            this.record["input"] = LinkManifest.OptionalText(this.manifest, "input") is { } input ? ResolvePath(input, this.outputDirectory) : null;
            var tools = new JsonObject();
            this.record["tools"] = tools;
            this.record["irSha256"] = irHash;
            this.record["optimization"] = this.optimization;
            WriteRecord(this.paths.Record, this.record);
            var tatara = Tatara.Instance;
            tools.Add("tatara", new JsonObject { ["build"] = tatara.Build, ["abi"] = tatara.Abi, ["llvmVersion"] = tatara.LlvmVersion });

            // The staged libraries, each held with read sharing only from its check to the build's end, so LLD links the bytes the record
            // names; the finally releases them with the private file on every outcome (StagedHold).
            var libraries = new List<FileStream>();
            var executable = this.paths.Stem + "." + Guid.NewGuid().ToString("N") + ".exe"; // Private until the commit moves it into place.
            try
            {
                this.ResolveLibraries(libraries);
                WriteRecord(this.paths.Record, this.record);
                var obj = this.Compile(tatara);
                var undefined = this.Inspect(tatara, obj);
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
                foreach (var library in libraries)
                {
                    library.Dispose();
                }

                File.Delete(executable);
            }
        }

        // SPEC 20.4-2: another supply must be this project's own, with its expanded Kind and the
        // requirement/supply hash assertions checked against the actual bytes that are linked.
        private static void CheckSupply(Project project, string name, string? kind, string? hash)
        {
            var settings = project.ProjectFile;
            var supply = settings.NativeLibraries.TryGetValue(TargetProfile.Windows.Target, out var supplies) ? supplies.GetValueOrDefault(name) : null;
            var (expectedKind, expectedHash) = supply is null ? (null, null) : NativeConfiguration.Expand(settings, TargetProfile.Windows.Target, name, supply);
            if (supply is null || expectedKind != kind || (expectedHash is not null && !expectedHash.Equals(hash, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Native library '{name}' does not match its NativeLibraries Kind or Sha256 assertion.");
            }
        }

        // The pair's names, checked before Run opens the IR: the manifest names this IR, the Name.ll beside its Name.link.json (paths
        // compared as Windows compares them), and this optimization. So a build's IR is never another output's or one of its own files
        // (G1203).
        private void CheckPair()
        {
            if (!ResolvePath(LinkManifest.Text(this.manifest, "irFile"), this.outputDirectory).Equals(this.paths.Ir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("IR/manifest path mismatch: a link manifest Name.link.json names the IR Name.ll beside it.");
            }

            if (this.optimization != LinkManifest.Text(this.manifest, "optimization"))
            {
                throw new InvalidDataException("Project and manifest optimization differ; re-emit the inputs.");
            }
        }

        // The manifest's libraries in its order, each staged (the file the linker receives) and added, held, to Run's list as soon as it
        // is staged, so Run releases it on every outcome; then checked against its assertions and identified in the record by its staged
        // path and hash (IMPL 20.4). A reserved name is the compiler's supply: its entry the one this compiler writes (LinkManifest) and its
        // bytes the compiler's (ToolchainIdentity.Supplies), found by their catalog hash or staged from its resource (StageSupply); any
        // other is a program's, staged from its input and checked against the project's supply. Every compiler's supply is listed.
        private void ResolveLibraries(List<FileStream> libraries)
        {
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

                var supply = Array.Find(ToolchainIdentity.Supplies, x => x.Supply.Name == name);
                FileStream library;
                string hash;
                if (supply is not null)
                {
                    LinkManifest.CheckCompilerSupply(item, supply);
                    library = StageSupply(supply, this.paths.Native, out hash);
                }
                else
                {
                    using var input = File.OpenRead(ResolvePath(LinkManifest.Text(item, "libraries.input"), this.outputDirectory));
                    library = StageInput(input, this.paths.Native, out hash);
                }

                libraries.Add(library);
                if (supply is null && this.project is not null)
                {
                    CheckSupply(this.project, name, kind, hash);
                }

                if (LinkManifest.OptionalText(item, "libraries.sha256") is { } assertion &&
                    (assertion.Length != 64 || !assertion.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException($"Native library '{name}' does not match its manifest Sha256 assertion.");
                }

                libraryIdentities.Add((JsonNode)new JsonObject { ["name"] = name, ["path"] = Redact(library.Name, this.directory), ["sha256"] = hash });
            }

            if (!Array.TrueForAll(ToolchainIdentity.Supplies, x => seen.Contains(x.Supply.Name)))
            {
                throw new InvalidDataException("Missing kernel32 or kimi_backend library.");
            }

            this.record["libraries"] = libraryIdentities;
        }

        // The object: the IR verified, for O2 optimized, its module paths redacted, written and verified again, then compiled and
        // written. Tatara returns each output and the build writes it.
        private string Compile(Tatara tatara)
        {
            var selectedIr = this.paths.Ir;
            if (this.optimization == "O2")
            {
                selectedIr = this.paths.Stem + ".ll";
                var ir = ModulePathPattern().Replace(tatara.Optimize(this.context, this.paths.Ir), m => Redact(m.Value, this.directory));
                File.WriteAllBytes(selectedIr, Utf8.GetBytes(ir));
            }

            var obj = this.paths.Stem + ".obj";
            File.WriteAllBytes(obj, tatara.Emit(this.context, selectedIr, obj, this.optimization[1] - '0'));
            return obj;
        }

        // The object's dependencies and shape: an undefined symbol is a backend helper, a runtime import or an external the module
        // declares (the manifest's) in the same form; one _fltused definition and no default library. Unwind information is not judged
        // here: IMPL 21.4's is the ABI's, which every definition's uwtable attribute (the profile's UnwindTables) asks of LLVM, an entry
        // for each function that allocates stack, saves a nonvolatile register or calls, and none for a frameless leaf, so an object
        // whose functions are all leaves has none. Returns the record's form of the undefined symbols: llvm-nm's posix lines, in which an
        // undefined symbol's value and size are 0.
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

            var markers = facts.DefinedExternal.Count(s => s.Name == TargetProfile.Windows.FloatMarker && s.Kind is 'B' or 'D');
            if (markers != 1 || facts.HasDefaultLibrary)
            {
                throw new InvalidDataException("Invalid _fltused definition or hidden default library.");
            }

            return string.Join('\n', lines);
        }

        // The executable: linked to Run's private file, so neither a failed link nor any failure before the commit publishes it, then the
        // record's linked state, naming the private file's hash, which Run's commit publishes before the file. LLD reads no environment
        // (/lldignoreenv: no LINK or _LINK_) and detects no toolchain (/winsysroot: the build's own inputs folder, which holds no VC or
        // Windows Kits tree), so the link uses only its absolute inputs (IMPL 20.3.2, 20.4; LC3 D1). LLD opens each held library by its
        // path, for reading, which the hold admits (StagedHold).
        private void Link(Tatara tatara, string obj, List<FileStream> libraries, string undefined, string executable)
        {
            var link = new string[libraries.Count + 9];
            link[0] = "lld-link";
            link[1] = obj;
            for (var i = 0; i < libraries.Count; i++)
            {
                link[i + 2] = libraries[i].Name;
            }

            link[^7] = "/entry:" + TargetProfile.Windows.EntrySymbol;
            link[^6] = "/subsystem:" + TargetProfile.Windows.Subsystem;
            link[^5] = "/nodefaultlib";
            link[^4] = "/Brepro";
            link[^3] = "/lldignoreenv";
            link[^2] = "/winsysroot:" + this.paths.Native;
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
