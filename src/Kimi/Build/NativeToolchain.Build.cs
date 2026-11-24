// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Diagnostics;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

internal static partial class NativeToolchain
{
    // The staged inputs' facts for the dependency step (LinkAmbiguity, design §4): keyed by the staged copy's SHA-256, the hash staging
    // computes, so an entry states that content and never changes; never invalidated or evicted, one per distinct staged input per
    // process. A build of an input already kept reads no archive.
    internal static readonly ConcurrentDictionary<string, LinkInput> LinkInputs = new(StringComparer.Ordinal);

    // The row's fixed runtime references: what any module of the row may reference besides the external declarations its manifest
    // states, the backend supply's symbols, which LLVM's code calls without a declaration (__chkstk; IMPL 21.4.7). The runtime's kernel32
    // imports are no second list here: the manifest's declarations name them (publication writes them from the generation result;
    // LinkAmbiguity, T's rulings 21 (ii) and 28).
    private static readonly HashSet<string> RuntimeReferences = new(ToolchainIdentity.Backend.Symbols, StringComparer.Ordinal);

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

    [GeneratedRegex(@"(?m)^(?:; ModuleID = .*|source_filename = .*|!\d+ = .*!DIFile\(.*)$")]
    private static partial Regex ModulePathPattern();

    /// <summary>One native build of a checked IR/manifest pair: its inputs, its record and its LLVM and LLD work, which Tatara's calls do in
    /// process (no tool is launched). The constructor takes the build's fixed inputs: the project whose supplies it checks (none for a
    /// standalone manifest), the pair, the root its records redact, the optimization and the host's report and cancellation, which Tatara's
    /// calls take with the target values (<see cref="TataraContext"/>). <see cref="Run"/> runs the steps once, in order: the pair's
    /// identity, with the input the manifest names, resolved as its other paths are (absent for a pair emitted from memory or by an
    /// earlier compiler), Tatara (the compiler's, <see cref="Tatara.Instance"/>, identified in the record's tools by its build, ABI and LLVM
    /// version), the libraries (each staged: the compiler's supplies found by their catalog hash or staged from their resources, a program's
    /// from its input), the object, its inspection, the dependency check (an ambiguous native dependency, or one inconsistent with its
    /// declared Kind, fails before the link, CheckDependencies, over the libraries' facts kept per process, LinkInputs), the link to a
    /// private file, the report and the commit;
    /// the steps fill the record and <see cref="Run"/> writes each of its states. The commit, after the report and the last cancellation
    /// point, writes the success record, which names the private file's hash, and then moves the file to the executable's path, so an Application publishes its executable
    /// only on success (IMPL 20.3.2; Tatara decision 18): a step that fails before the commit, the report included, throws and leaves the
    /// previous executable and the record at the last incomplete state written, and a failed move writes the invalidation again and rethrows
    /// the move's failure. The private file is deleted on every outcome. IMPL 20.3.2 states the two windows (the owner's decision 19, O-468
    /// (a)): should the process end between the success record and the move, or the move and the invalidation both fail (one IOException
    /// naming both, the move's as its inner exception), the attempt's success record stays, naming a hash no file at the path has. run
    /// --no-build runs only an executable whose hash the record names, so it refuses that one, and the next build invalidates the record.
    /// From the pair's hash to its end the build holds its IR open for reading only (G1202), each staged library from its check, released
    /// with the private file on every outcome (StagedHold), and its O2 text and object from their creation, which the build writes from
    /// Tatara's returned bytes, the text until emit has read it (Intermediates): a write, replacement or deletion of any of them fails
    /// meanwhile, so Tatara compiles and LLD links the bytes the record names and what the build derived from them.</summary>
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

            // The staged libraries, each with its manifest name and staged hash and held with read sharing only from its check to the
            // build's end, so the dependency step reads and LLD links the bytes the record names; the finally releases them with the
            // private file on every outcome (StagedHold). The object is held from its creation (Compile) to the build's end, so the
            // inspection and LLD read the object emit produced (Intermediates).
            var libraries = new List<(string Name, string Kind, string Sha256, FileStream Held)>();
            var executable = this.paths.Stem + "." + Guid.NewGuid().ToString("N") + ".exe"; // Private until the commit moves it into place.
            try
            {
                this.ResolveLibraries(libraries);
                WriteRecord(this.paths.Record, this.record);
                using var obj = this.Compile(tatara);
                var (undefined, module) = this.Inspect(tatara, obj.Name);
                this.CheckDependencies(tatara, module, libraries);
                this.Link(tatara, obj.Name, libraries, undefined, executable);
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
                    library.Held.Dispose();
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

        // An intermediate the build writes, held from its creation (Intermediates; DECISIONS T-M17's rule): created with write access and
        // read sharing only, written, flushed and returned open, so until the caller releases it no write, replacement or deletion of it,
        // no rename of its folder and no open without read sharing succeeds; a reader that does not share write is refused too. LLVM's and
        // LLD's opens for reading share read, write and delete, so emit, the inspection and LLD read the bytes written (Intermediates'
        // design §3, probed). A failure after the open closes the handle and propagates.
        private static FileStream Hold(string path, byte[] bytes)
        {
            var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var held = false;
            try
            {
                file.Write(bytes);
                file.Flush();
                held = true;
                return file;
            }
            finally
            {
                if (!held)
                {
                    file.Dispose();
                }
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

        // The manifest's libraries in its order, each staged (the file the linker receives) and added, held, with its name, Kind and
        // staged hash, to Run's list as soon as it is staged, so Run releases it on every outcome; then checked against its assertions and
        // identified in the record by its staged path and hash (IMPL 20.4). A reserved name is the compiler's supply: its entry the one
        // this compiler writes (LinkManifest) and its bytes the compiler's (ToolchainIdentity.Supplies), found by their catalog hash or
        // staged from its resource (StageSupply); any other is a program's, staged from its input and checked against the project's
        // supply. Every compiler's supply is listed.
        private void ResolveLibraries(List<(string Name, string Kind, string Sha256, FileStream Held)> libraries)
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

                libraries.Add((name, kind, hash, library));
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
        // written. Tatara returns each output and the build writes it through Hold: the O2 text is held until emit has read it, and the
        // object is returned held, for Run to release at the build's end.
        private FileStream Compile(Tatara tatara)
        {
            var obj = this.paths.Stem + ".obj";
            var level = this.optimization[1] - '0';
            if (this.optimization != "O2")
            {
                return Hold(obj, tatara.Emit(this.context, this.paths.Ir, obj, level));
            }

            var ir = ModulePathPattern().Replace(tatara.Optimize(this.context, this.paths.Ir), m => Redact(m.Value, this.directory));
            using var optimized = Hold(this.paths.Stem + ".ll", Utf8.GetBytes(ir));
            return Hold(obj, tatara.Emit(this.context, optimized.Name, obj, level));
        }

        // The object's dependencies and shape: an undefined symbol is a fixed runtime reference (RuntimeReferences) or an external the
        // module declares (the manifest's, the runtime's imports among them) in the same form; one _fltused definition and no default library. Unwind information is not judged
        // here: IMPL 21.4's is the ABI's, which every definition's uwtable attribute (the profile's UnwindTables) asks of LLVM, an entry
        // for each function that allocates stack, saves a nonvolatile register or calls, and none for a frameless leaf, so an object
        // whose functions are all leaves has none. Returns the record's form of the undefined symbols, llvm-nm's posix lines, in which an
        // undefined symbol's value and size are 0, and the object's facts, which the dependency step reads (one inspection).
        private (string Undefined, ObjectMember Module) Inspect(Tatara tatara, string obj)
        {
            var facts = tatara.Inspect(this.context, obj);
            var module = facts.Members[0]; // Tatara's own object: one member
            Dictionary<string, bool>? declared = null;
            var lines = new string[module.Undefined.Count];
            for (var i = 0; i < lines.Length; i++)
            {
                var symbol = module.Undefined[i].Name;
                lines[i] = symbol + " " + module.Undefined[i].Kind + " 0 0";
                if (RuntimeReferences.Contains(symbol))
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

            var markers = module.DefinedExternal.Count(s => s.Name == TargetProfile.Windows.FloatMarker && s.Kind is 'B' or 'D');
            if (markers != 1 || facts.HasDefaultLibrary)
            {
                throw new InvalidDataException("Invalid _fltused definition or hidden default library.");
            }

            return (string.Join('\n', lines), module);
        }

        // IMPL 20.4: an ambiguous native dependency, or one inconsistent with its declared Kind, is an error, never resolved by link
        // order (LinkAmbiguity; design Revisions 0-3a, its §2 and §3; Kind, kind-design.md §2 and §3). Each library's facts are Tatara's
        // inspection of its staged copy, read once per process (LinkInputs). The closure starts from the module's declared references,
        // the manifest's external declarations in their referenced form and the fixed RuntimeReferences, the same at O0 and O2 (never the
        // object's undefined symbols), and from the references of the library files the link loads whole (DependencyClosure); a symbol
        // it references that two or more libraries define, a unit it loads that its library's Kind does not admit and a declared import
        // bound to code fail the build before the link, every line in one failure: the native build's input failure, the record left
        // incomplete.
        private void CheckDependencies(Tatara tatara, ObjectMember module, List<(string Name, string Kind, string Sha256, FileStream Held)> libraries)
        {
            var inputs = new (string Name, LinkClass Admits, LinkInput Input)[libraries.Count];
            for (var i = 0; i < inputs.Length; i++)
            {
                var (name, kind, sha256, held) = libraries[i];
                var facts = LinkInputs.GetOrAdd(
                    sha256, static (_, read) => LinkInput.Read(read.Tatara.Inspect(read.Context, read.Path)), (Tatara: tatara, Context: this.context, Path: held.Name));
                inputs[i] = (name, kind == "static" ? LinkClass.Code : LinkClass.Import, facts);
            }

            var closure = new DependencyClosure(inputs, module);
            foreach (var (symbol, dllimport) in ReadExternalDeclarations(this.manifest))
            {
                closure.Reference(dllimport ? "__imp_" + symbol : symbol); // SPEC 22.6: a dllimport external is referenced through __imp_.
            }

            foreach (var symbol in RuntimeReferences)
            {
                closure.Reference(symbol);
            }

            if (closure.Failures() is { Length: > 0 } lines)
            {
                throw new InvalidDataException(string.Join('\n', lines));
            }
        }

        // The executable: linked to Run's private file, so neither a failed link nor any failure before the commit publishes it, then the
        // record's linked state, naming the private file's hash, which Run's commit publishes before the file. LLD reads no environment
        // (/lldignoreenv: no LINK or _LINK_) and detects no toolchain (/winsysroot: the build's own inputs folder, which holds no VC or
        // Windows Kits tree), so the link uses only its absolute inputs (IMPL 20.3.2, 20.4; LC3 D1). LLD opens the held object and each
        // held library by its path, for reading, which the holds admit (StagedHold, Intermediates).
        private void Link(Tatara tatara, string obj, List<(string Name, string Kind, string Sha256, FileStream Held)> libraries, string undefined, string executable)
        {
            var link = new string[libraries.Count + 9];
            link[0] = "lld-link";
            link[1] = obj;
            for (var i = 0; i < libraries.Count; i++)
            {
                link[i + 2] = libraries[i].Held.Name;
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

    /// <summary>A staged input's facts as the dependency step reads them (LinkAmbiguity, design §2): Tatara's inspection
    /// (<c>Facts</c>); an archive's <c>Picks</c>, for each symbol its index names, the member the first entry names, which lld-link loads
    /// for it, and whether that member's definition of it is a copy (text kind 5; on COFF the index is the pick, ELF's comes with B7b7);
    /// <c>Whole</c>, an input that is no archive and of form o or i, which the link loads whole (one of form s, a linked image, defines
    /// nothing for it); <c>Classes</c>, each member's class (Kind, kind-design.md §2).</summary>
    internal sealed record LinkInput(ObjectFacts Facts, Dictionary<string, (int Member, bool Copy)> Picks, bool Whole, LinkClass[] Classes)
    {
        /// <summary>Reads an input's facts once.</summary>
        /// <param name="facts">Tatara's inspection of the staged input.</param>
        /// <returns>The facts in the step's form.</returns>
        internal static LinkInput Read(ObjectFacts facts)
        {
            var picks = new Dictionary<string, (int Member, bool Copy)>(StringComparer.Ordinal);
            foreach (var (member, symbol) in facts.Index)
            {
                if (!picks.ContainsKey(symbol))
                {
                    picks.Add(symbol, (member, facts.Members[member].Comdats.Contains(symbol)));
                }
            }

            var classes = facts.Members.Select(static x => x.Form switch
            {
                'i' => LinkClass.Import,
                's' => LinkClass.Image,
                _ => x.DefinedExternal.Any(static s => s.Kind == 'I') ? LinkClass.Import : LinkClass.Code,
            });
            return new(facts, picks, !facts.IsArchive && facts.Members[0].Form != 's', [.. classes]);
        }
    }

    /// <summary>A unit's class, which a library's Kind admits or not (Kind, kind-design.md §2; IMPL 20.4, 21.2): <c>Code</c>, an object
    /// whose code the program contains (static); <c>Import</c>, an import member, a COFF short import member (form i) or an object defining
    /// a global in an <c>.idata</c> section (llvm-nm's letter I: import descriptors, long-format import members) (import); <c>Image</c>, a
    /// linked image (form s), which no Kind admits.</summary>
    internal enum LinkClass : byte
    {
        /// <summary>An object whose code the program contains.</summary>
        Code,

        /// <summary>An import member.</summary>
        Import,

        /// <summary>A linked image.</summary>
        Image,
    }

    // One build's dependency closure (LinkAmbiguity, design §2 "The closure"; DECISIONS T-M<n>), order-free across libraries: the units
    // are the files the link loads whole (the module's object, a whole library input) and the members it may load. A referenced symbol
    // (a unit's strong undefined symbol, U; a weak one loads nothing; or its /include: directive) that no whole file defines joins each
    // library's pick for it, one member per library, and two or more libraries picking it are an ambiguous dependency, the copies of one
    // COMDAT symbol counting as one library. An /alternatename:A=B of a joined unit makes B referenced once the references are done when
    // A is referenced and neither a whole file nor any library defines A (then the references resume). Each library input that is no
    // archive and each pick is judged against its library's Kind, which admits one class (Admits); an inconsistent pick still joins, so
    // every other failure is found (Kind, kind-design.md §2 with its corrections 1-3).
    private sealed class DependencyClosure
    {
        private readonly (string Name, LinkClass Admits, LinkInput Input)[] inputs;
        private readonly HashSet<string> whole = new(StringComparer.Ordinal); // The symbols the whole files define.
        private readonly HashSet<string> referenced = new(StringComparer.Ordinal);
        private readonly Stack<string> pending = new();
        private readonly HashSet<(int Input, int Member)> joined = [];
        private readonly List<(string From, string To)> alternates = []; // The joined units' unused /alternatename: pairs.
        private readonly HashSet<string> imports = new(StringComparer.Ordinal); // The module's references through __imp_ (K2).
        private readonly List<(string Library, string Head, string? Symbol)> inconsistent = []; // K: each line's head and its symbols.
        private readonly List<(string Symbol, string Line)> symbols = []; // The ambiguous symbols' and K2's lines.

        // The whole files: their definitions first, then their references; each library input that is no archive judged (K).
        internal DependencyClosure((string Name, LinkClass Admits, LinkInput Input)[] inputs, ObjectMember module)
        {
            this.inputs = inputs;
            this.whole.UnionWith(module.DefinedExternal.Select(static x => x.Name));
            for (var i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].Input.Whole)
                {
                    this.whole.UnionWith(inputs[i].Input.Facts.Members[0].DefinedExternal.Select(static x => x.Name));
                }
            }

            for (var i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].Input.Whole)
                {
                    this.Join(i, 0);
                }

                if (!inputs[i].Input.Facts.IsArchive)
                {
                    this.Judge(i, 0, null);
                }
            }
        }

        // A reference of the module's.
        internal void Reference(string symbol)
        {
            this.pending.Push(symbol);
            if (symbol.StartsWith("__imp_", StringComparison.Ordinal))
            {
                this.imports.Add(symbol);
            }
        }

        // The closure, then every line (design §3; Kind §3): the Kind's in Ordinal library order, each line's symbols in Ordinal order,
        // then the ambiguous symbols' and K2's in Ordinal symbol order, libraries in Ordinal order.
        internal string[] Failures()
        {
            do
            {
                while (this.pending.TryPop(out var symbol))
                {
                    this.Resolve(symbol);
                }
            }
            while (this.UseAlternates());

            var kind = this.inconsistent.OrderBy(static x => x.Library, StringComparer.Ordinal).ThenBy(static x => x.Head, StringComparer.Ordinal).GroupBy(static x => x.Head).Select(
                static x => x.Key + (x.First().Symbol is null ? "." : $" for {Names(x.Select(static s => s.Symbol!).Order(StringComparer.Ordinal).Select(static s => $"'{s}'"))}."));
            return [.. kind, .. this.symbols.OrderBy(static x => x.Symbol, StringComparer.Ordinal).ThenBy(static x => x.Line, StringComparer.Ordinal).Select(static x => x.Line)];
        }

        // "a", "a and b", "a, b and c", in the given order.
        private static string Names(IEnumerable<string> names)
        {
            var list = names.ToList();
            return list.Count == 1 ? list[0] : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
        }

        // A referenced symbol, once: unless a whole file defines it, each library's pick joins and is judged (K), and two or more
        // candidates are recorded; a module's import that no library picks is K2's.
        private void Resolve(string symbol)
        {
            if (!this.referenced.Add(symbol) || this.whole.Contains(symbol))
            {
                return;
            }

            List<string>? libraries = null;
            var (candidates, copies) = (0, false);
            for (var i = 0; i < this.inputs.Length; i++)
            {
                if (this.inputs[i].Input.Picks.TryGetValue(symbol, out var pick))
                {
                    candidates += pick.Copy && copies ? 0 : 1;
                    copies |= pick.Copy;
                    (libraries ??= []).Add(this.inputs[i].Name);
                    this.Join(i, pick.Member);
                    this.Judge(i, pick.Member, symbol);
                }
            }

            if (candidates > 1)
            {
                this.symbols.Add((symbol, $"Ambiguous native symbol '{symbol}': defined by the libraries {Names(libraries!.Order(StringComparer.Ordinal))}."));
            }
            else if (libraries is null && this.imports.Contains(symbol))
            {
                this.Unbound(symbol[6..]);
            }
        }

        // K: a unit the link loads, a library input that is no archive (symbol null) or the library's pick for the symbol, recorded under
        // its library when its class is not the one the library's Kind admits; a linked image as an archive member has its own line (T's
        // ruling 24).
        private void Judge(int input, int member, string? symbol)
        {
            var (name, admits, facts) = this.inputs[input];
            var unit = facts.Classes[member];
            if (unit == admits)
            {
                return;
            }

            var declared = $"Native library '{name}' is declared {(admits == LinkClass.Code ? "static" : "import")}, but ";
            var head = (symbol, unit) switch
            {
                (null, LinkClass.Image) => declared + "its input is a linked image (a DLL or shared object), not a library",
                (null, _) => declared + $"its input is {(unit == LinkClass.Import ? "an import member" : "an object")}, which the link loads whole",
                (_, LinkClass.Image) => $"Native library '{name}' holds a linked image (a DLL or shared object) as an archive member",
                _ => declared + $"the link would load {(unit == LinkClass.Import ? "import members" : "code")} from it",
            };
            this.inconsistent.Add((name, head, unit == LinkClass.Image ? null : symbol));
        }

        // K2 (Kind §2, correction 3; NamedProvider's rule P replaces it, PLAN G1333): the module's import of X through __imp_X, which no
        // whole file defines and no library picks, while a library input that the link loads whole or a library's pick defines X, would
        // bind to that code by LLD's local-import rule; one line naming those libraries. With X defined nowhere LLD's undefined symbol stands.
        private void Unbound(string target)
        {
            var libraries = this.inputs.Where(x => x.Input.Picks.ContainsKey(target) || (x.Input.Whole && x.Input.Facts.Members[0].DefinedExternal.Any(s => s.Name == target)))
                .Select(static x => x.Name).Order(StringComparer.Ordinal).ToList();
            if (libraries.Count > 0)
            {
                var (noun, verb) = libraries.Count == 1 ? ("library", "defines") : ("libraries", "define");
                this.symbols.Add((target, $"Native function '{target}' is imported, but no import member defines it, and the {noun} {Names(libraries)} {verb} it as code."));
            }
        }

        // A unit joins once: its strong references and /include: names are referenced, its /alternatename: pairs kept.
        private void Join(int input, int member)
        {
            if (!this.joined.Add((input, member)))
            {
                return;
            }

            var unit = this.inputs[input].Input.Facts.Members[member];
            foreach (var symbol in unit.Undefined)
            {
                if (symbol.Kind == 'U')
                {
                    this.pending.Push(symbol.Name);
                }
            }

            foreach (var directive in unit.Directives)
            {
                if (directive.Kind == 'I')
                {
                    this.pending.Push(directive.Name);
                }
                else
                {
                    this.alternates.Add((directive.Name, directive.Target!));
                }
            }
        }

        // The pairs whose source is referenced and stays undefined (no whole file defines it, no library picks it) reference their
        // target, each once; whether any did.
        private bool UseAlternates()
        {
            var used = false;
            for (var k = this.alternates.Count - 1; k >= 0; k--)
            {
                var (from, to) = this.alternates[k];
                if (this.referenced.Contains(from) && !this.whole.Contains(from) && !Array.Exists(this.inputs, x => x.Input.Picks.ContainsKey(from)))
                {
                    this.pending.Push(to);
                    this.alternates.RemoveAt(k);
                    used = true;
                }
            }

            return used;
        }
    }
}
