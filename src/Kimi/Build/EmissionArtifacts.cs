// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi.Compiler;
using Kimi.Diagnostics;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

/// <summary>Publication of a checked, matched pre-optimization IR/manifest pair, which records its own failure. Does not run native tools.</summary>
public static class EmissionArtifacts
{
    /// <summary>Publishes both files, with the manifest last, or records why not in the Emission partition without claiming old files
    /// (DECISIONS R5-M165): the generation entry's records and, beside them, <c>ArtifactPublicationFailed_Kd</c> at the project, its cause
    /// as a note, for an output failure or a publication-time native-configuration or supply failure; <c>InternalInvariant_Kd</c> at the
    /// project when the compiler's embedded backend record is missing. Any other exception, such as a broken contract code generation
    /// throws, is not classified here: it leaves for the command's fault path (DECISIONS R5-M169). The publication inputs are checked
    /// first, since the generation entry writes into the temporary IR file (R5 C0a); code generation never reads them, so it runs once
    /// whatever they found, into no output when they failed (DECISIONS R5-M181); preparation has validated the native configuration
    /// (DependencyConfiguration). The required libraries' supplies are resolved after generation, from the generation result
    /// (Compilation.Generated, with the output kind and the external declarations the manifest states; R9-B1).</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <param name="paths">The artifact paths, or null for the project's.</param>
    /// <returns>Whether the complete matched pair was published.</returns>
    internal static bool Publish(Compilation compilation, ArtifactPaths? paths)
    {
        // The compilation's row: a compilation without one never reaches publication (the commands' gate), so reaching it is a broken
        // contract, as a missing backend record is.
        if (ToolchainIdentity.BackendVersion.Length == 0 || ToolchainIdentity.BackendSha256.Length != 64 ||
            TargetProfile.Find(compilation.TargetTriple.Value) is not { } row)
        {
            return FailBeforeGeneration(compilation, DiagnosticCode.InternalInvariant_Kd, null);
        }

        var settings = compilation.Project.ProjectFile;
        string directory, destination, manifest, outputDirectory;
        string? tempIr = null, tempManifest = null;
        StreamWriter? writer = null;
        try
        {
            // The publication's steps before and after code generation classify their failures; generation runs between them, outside
            // either catch, so only GenerateInto classifies its failure (its output's) and any other leaves for the fault (R5-M169).
            try
            {
                var project = compilation.Project;
                directory = Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory);
                paths ??= ArtifactPaths.Create(project, row);
                destination = paths.Ir;
                manifest = paths.Manifest;
                outputDirectory = Path.GetDirectoryName(destination)!;
                CheckLibraryInputs(settings, row);
                Directory.CreateDirectory(outputDirectory);
                var unique = Guid.NewGuid().ToString("N");
                tempIr = destination + "." + unique + ".tmp";
                tempManifest = manifest + "." + unique + ".tmp";
                writer = new StreamWriter(new FileStream(tempIr, FileMode.CreateNew, FileAccess.Write, FileShare.None), Utf8, 16384);
            }
            catch (Exception ex) when (IsPublicationFailure(ex))
            {
                return FailBeforeGeneration(compilation, DiagnosticCode.ArtifactPublicationFailed_Kd, ex.Message);
            }

            var generated = GenerateInto(compilation, writer);
            try
            {
                writer.Dispose(); // A flush failure is the output's.
                if (!generated)
                {
                    return false;
                }

                var module = compilation.Generated;
                var foreign = ResolveForeignSupplies(module.Libraries, settings, row);
                var hash = Hash(tempIr);
                using (var stream = new FileStream(tempManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    json.WriteStartObject();
                    LinkManifest.WriteFixed(json, row);
                    json.WriteString("optimization", settings.Optimization);
                    if (compilation.Project.Input is { } input)
                    {
                        // The pair names its input, which its build's record names (IMPL 20.3.1), relative to the output directory as
                        // the manifest's other paths are.
                        json.WriteString("input", Path.GetRelativePath(outputDirectory, input));
                    }

                    WriteOutput(json, Path.GetFileName(destination), hash, module, row);
                    WriteLibraries(json, foreign, row, module.Libraries, directory, outputDirectory);
                    json.WriteEndObject();
                }

                Replace(tempIr, destination);
                Replace(tempManifest, manifest);
                return true;
            }
            catch (Exception ex) when (IsPublicationFailure(ex))
            {
                Fail(compilation, DiagnosticCode.ArtifactPublicationFailed_Kd, ex.Message);
                return false;
            }
        }
        finally
        {
            try
            {
                writer?.Dispose(); // While a generation exception unwinds, a flush failure replaces it and becomes the fault.
            }
            finally
            {
                DeleteTemporary(tempIr);
                DeleteTemporary(tempManifest);
            }
        }
    }

    /// <summary>Generates into the publication's output through the generation entry and records only the output's failure (DECISIONS
    /// R5-M169): code generation does no file I/O but through this writer (it reads embedded templates), so an <c>IOException</c> or
    /// <c>UnauthorizedAccessException</c> is the output's, <c>ArtifactPublicationFailed_Kd</c> with its cause. Every other exception, such
    /// as a broken contract code generation throws, leaves unclassified for the command's fault path (<c>CheckFaulted_Kd</c>, DIAGNOSTICS
    /// rule 9). Publish's other output operations (paths, directories, files, the manifest, the replacement) map their failures
    /// themselves.</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <param name="output">The output, written only when generation succeeds.</param>
    /// <returns>Whether the module was written; otherwise the Emission partition records why.</returns>
    internal static bool GenerateInto(Compilation compilation, TextWriter output)
    {
        try
        {
            return compilation.Generate(output);
        }
        catch (Exception ex) when (IsOutputFailure(ex))
        {
            Fail(compilation, DiagnosticCode.ArtifactPublicationFailed_Kd, ex.Message);
            return false;
        }
    }

    // An output, configuration or supply failure a publication step throws (DECISIONS R5-M165); any other exception is a fault (R5-M169).
    internal static bool IsPublicationFailure(Exception ex)
        => IsOutputFailure(ex) || ex is InvalidDataException or ArgumentException or NotSupportedException;

    // A publication failure, at the project, beside the generation's records (R5-M181).
    private static void Fail(Compilation compilation, DiagnosticCode code, string? cause)
        => compilation.Diagnostics.Report(DiagnosticPartition.Emission, code, compilation.Project.FilePath, note: cause);

    // A publication failure before generation (DECISIONS R5-M181): code generation never reads publication's inputs, so it still runs, into
    // no output, for its own records, and the failure is recorded after it, since it clears the partition first, also when it throws.
    private static bool FailBeforeGeneration(Compilation compilation, DiagnosticCode code, string? cause)
    {
        try
        {
            GenerateInto(compilation, TextWriter.Null);
        }
        finally
        {
            Fail(compilation, code, cause);
        }

        return false;
    }

    // Checks every configured library's input under the row's key, even when unused (SPEC 20.4), of the configuration preparation
    // validated (no supply names a reserved library: NativeConfiguration).
    private static void CheckLibraryInputs(ProjectFile settings, TargetProfile row)
    {
        if (settings.NativeLibraries.TryGetValue(row.Target, out var libraries))
        {
            foreach (var (_, library) in libraries)
            {
                CheckLibraryPath(library.Input, row);
            }

            foreach (var library in libraries.Packaged)
            {
                CheckLibraryPath(library.Input, row);
            }
        }
    }

    // IMPL 20.4: the supplies of the module's required libraries (the generation result's, one per library) but the compiler's (a reserved
    // supply of the target profile, which needs no supply), sorted by Ordinal name. Only the root module's own self-targeted supplies connect yet: a logical name alone cannot identify another
    // module's requirement, and combined-module native records are not generated, so Binding makes a dependency module's import, but
    // a reserved supply's, the located Unsupported_Kd (DECISIONS BU-92).
    private static List<(string Name, string Kind, string Input, string? Sha256)>? ResolveForeignSupplies(
        IReadOnlyList<(string Library, string Kind)> required, ProjectFile settings, TargetProfile row)
    {
        List<(string Name, string Kind, string Input, string? Sha256)>? result = null;
        for (var i = 0; i < required.Count; i++)
        {
            var (name, kind) = required[i];
            if (row.Supply(name) is not null)
            {
                continue;
            }

            var supply = settings.NativeLibraries.TryGetValue(row.Target, out var libraries) ? libraries.GetValueOrDefault(name) : null;
            if (supply is null)
            {
                throw new InvalidDataException($"Native requirement '{name}' has no NativeLibraries supply for {row.Target}.");
            }

            var (_, sha256) = NativeConfiguration.Expand(settings, row.Target, name, supply);
            (result ??= new()).Add((name, kind, supply.Input, sha256));
        }

        result?.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static void CheckLibraryPath(string input, TargetProfile row)
    {
        CheckPath(input);
        if (!Path.GetExtension(input).Equals(row.InputExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"NativeLibraries inputs for {row.Target} must be {row.InputExtension} files.");
        }
    }

    // The published module, from the generation result: the IR file with its hash, the output kind with the module's entry (the
    // generation result's) and an Application's subsystem (the row's, null on a row without one), and every external function it declares, its externals then its runtime's imports, each once with its providing library, which the
    // native build's object inspection and dependency check read (SPEC 22.6; LinkAmbiguity, T's ruling 32; NamedProvider).
    private static void WriteOutput(Utf8JsonWriter json, string irFile, string irHash, GeneratedModule module, TargetProfile row)
    {
        var library = module.OutputKind == OutputKind.Library;
        json.WriteString("irFile", irFile);
        json.WriteString("irSha256", irHash);
        json.WriteString("outputKind", library ? "Library" : "Application");
        json.WriteString("entry", module.Entry);
        json.WriteString("subsystem", library ? null : row.Subsystem);
        json.WriteStartArray("externalDeclarations");
        foreach (var external in module.Externals)
        {
            WriteDeclaration(json, external.Symbol, external.Library);
        }

        foreach (var import in module.RuntimeImports)
        {
            WriteDeclaration(json, import.Symbol, import.Library);
        }

        json.WriteEndArray();
    }

    // One external declaration: its symbol and providing library, a libraries entry, whose Kind gives the symbol's referenced form.
    private static void WriteDeclaration(Utf8JsonWriter json, string symbol, string library)
    {
        json.WriteStartObject();
        json.WriteString("symbol", symbol);
        json.WriteString("library", library);
        json.WriteEndObject();
    }

    // The libraries in Ordinal name order (SPEC 20.3.2): the row's compiler supplies, each its row's entry (LinkManifest), every one but
    // the row's C runtime always and the C runtime when the module requires it (IMPL 21.5.2; the generation result's decision), and the
    // foreign supplies around them, an input with a directory relative to the output directory.
    private static void WriteLibraries(
        Utf8JsonWriter json,
        List<(string Name, string Kind, string Input, string? Sha256)>? foreign,
        TargetProfile row,
        IReadOnlyList<(string Library, string Kind)> required,
        string projectDirectory,
        string outputDirectory)
    {
        json.WriteStartArray("libraries");
        var next = 0;
        foreach (var supply in ToolchainIdentity.Supplies)
        {
            if (supply.Row != row || (supply.Supply.Name == row.CRuntime && !Requires(supply.Supply.Name)))
            {
                continue;
            }

            WriteForeign(supply.Supply.Name);
            LinkManifest.WriteCompilerSupply(json, supply);
        }

        WriteForeign(null);
        json.WriteEndArray();

        // Writes the supplies ordered before the reserved name (all remaining ones for null).
        void WriteForeign(string? before)
        {
            for (; next < (foreign?.Count ?? 0) && (before is null || string.CompareOrdinal(foreign![next].Name, before) < 0); next++)
            {
                var (name, kind, input, sha256) = foreign![next];
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteString("kind", kind);
                json.WriteString("input", Relative(input));
                if (sha256 is not null)
                {
                    json.WriteString("sha256", sha256);
                }

                json.WriteEndObject();
            }
        }

        string Relative(string input) => HasDirectory(input) ? Path.GetRelativePath(outputDirectory, Path.GetFullPath(input, projectDirectory)) : input;

        bool Requires(string name)
        {
            foreach (var (library, _) in required)
            {
                if (library == name)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static void DeleteTemporary(string? path)
    {
        if (path is not null)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool HasDirectory(string path) => path.Contains('/') || path.Contains('\\');
}
