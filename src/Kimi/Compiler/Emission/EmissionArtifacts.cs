// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json;
using Kimi.Diagnostics;
using static Kimi.Compiler.ArtifactFiles;

namespace Kimi.Compiler;

/// <summary>Publication of a checked, matched pre-optimization IR/manifest pair, which records its own failure. Does not run native tools.</summary>
public static class EmissionArtifacts
{
    /// <summary>Publishes both files, with the manifest last, or records why not in the Emission partition without claiming old files
    /// (DECISIONS R5-M165): the generation entry's records; <c>ArtifactPublicationFailed_Kd</c> at the project, its cause as a note, for an
    /// output failure or a publication-time native-configuration or supply failure; <c>InternalInvariant_Kd</c> at the project when the
    /// compiler's embedded backend record is missing or code generation fails a contract check while it writes the output (GenerateInto's
    /// mapping). The publication inputs are checked first, since the generation entry writes into
    /// the temporary IR file (R5 C0a); preparation has validated the native configuration (DependencyConfiguration).</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <param name="paths">The artifact paths, or null for the project's.</param>
    /// <param name="irPath">The published IR path on success.</param>
    /// <returns>Whether the complete matched pair was published.</returns>
    internal static bool Publish(Compilation compilation, ArtifactPaths? paths, out string? irPath)
    {
        irPath = null;
        if (WindowsProfile.BackendVersion.Length == 0 || WindowsProfile.BackendSha256.Length != 64)
        {
            Fail(compilation, DiagnosticCode.InternalInvariant_Kd, null);
            return false;
        }

        string? tempIr = null;
        string? tempManifest = null;
        try
        {
            var project = compilation.Project;
            var settings = project.ProjectFile;
            var directory = Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory);
            paths ??= ArtifactPaths.Create(project);
            var destination = paths.Ir;
            var manifest = paths.Manifest;
            var outputDirectory = Path.GetDirectoryName(destination)!;
            var backend = ResolveBackend(settings);
            var foreign = ResolveForeignSupplies(compilation, settings);
            if (backend is not null && HasDirectory(backend.Input))
            {
                var actual = Hash(Path.GetFullPath(backend.Input, directory));
                if (!actual.Equals(WindowsProfile.BackendSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The selected backend archive does not match the adopted SHA-256.");
                }
            }

            string? llvm = null;
            if (settings.LlvmBin is { } bin)
            {
                CheckPath(bin);
                llvm = Path.GetRelativePath(outputDirectory, Path.GetFullPath(bin, directory));
            }

            Directory.CreateDirectory(outputDirectory);
            var unique = Guid.NewGuid().ToString("N");
            tempIr = destination + "." + unique + ".tmp";
            tempManifest = manifest + "." + unique + ".tmp";
            using (var writer = new StreamWriter(new FileStream(tempIr, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(false), 16384))
            {
                if (!GenerateInto(compilation, writer))
                {
                    return false;
                }
            }

            var hash = Hash(tempIr);
            using (var stream = new FileStream(tempManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteManifest(json, settings, compilation.Binding.Startup.OutputKind, backend, foreign, Path.GetFileName(destination), hash, directory, outputDirectory, llvm);
            }

            Replace(tempIr, destination);
            Replace(tempManifest, manifest);
            irPath = destination;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Fail(compilation, DiagnosticCode.ArtifactPublicationFailed_Kd, ex.Message);
            return false;
        }
        finally
        {
            DeleteTemporary(tempIr);
            DeleteTemporary(tempManifest);
        }
    }

    /// <summary>Generates into the publication's output through the generation entry, separating the output's failure from a broken
    /// contract of code generation (DECISIONS R5-M165): an I/O failure of the output is <c>ArtifactPublicationFailed_Kd</c> with its
    /// cause, and an argument, data or support exception from code generation's own checks is <c>InternalInvariant_Kd</c>. Publish's
    /// other output operations (paths, directories, files, the manifest, the replacement) map their failures themselves.</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <param name="output">The output, written only when generation succeeds.</param>
    /// <returns>Whether the module was written; otherwise the Emission partition records why.</returns>
    internal static bool GenerateInto(Compilation compilation, TextWriter output)
    {
        try
        {
            return compilation.Generate(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(compilation, DiagnosticCode.ArtifactPublicationFailed_Kd, ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException)
        {
            Fail(compilation, DiagnosticCode.InternalInvariant_Kd, null);
            return false;
        }
    }

    // A publication failure, at the project, replaces the partition's records as a generation does.
    private static void Fail(Compilation compilation, DiagnosticCode code, string? cause)
    {
        compilation.Diagnostics.Invalidate(DiagnosticPartition.Emission);
        compilation.Diagnostics.Report(DiagnosticPartition.Emission, code, compilation.Project.FilePath, note: cause);
    }

    // Checks every configured library's input, even when unused, and selects the backend supply (SPEC 20.4) of the configuration
    // preparation validated.
    private static NativeLibraryInput? ResolveBackend(ProjectFile settings)
    {
        NativeLibraryInput? backend = null;
        if (settings.NativeLibraries.TryGetValue(WindowsProfile.Target, out var libraries))
        {
            foreach (var (name, library) in libraries)
            {
                CheckLibraryPath(library.Input);
                if (name == WindowsProfile.BackendLibrary)
                {
                    var (kind, sha256) = NativeConfiguration.Expand(settings, WindowsProfile.Target, name, library);
                    if (kind != "static")
                    {
                        throw new InvalidDataException("kimi_backend must be static.");
                    }

                    if (sha256 is not null && !sha256.Equals(WindowsProfile.BackendSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The kimi_backend Sha256 assertion does not match the adopted SHA-256.");
                    }

                    backend = library;
                }
            }

            foreach (var library in libraries.Packaged)
            {
                CheckLibraryPath(library.Input);
            }
        }

        return backend;
    }

    // SPEC 20.3.2: libraries required by external declarations, sorted by Ordinal name. Only the root
    // module's own self-targeted supplies connect yet: a logical name alone cannot identify another
    // module's requirement, and combined-module native records are not generated.
    private static List<(string Name, string Kind, string Input, string? Sha256)>? ResolveForeignSupplies(Compilation compilation, ProjectFile settings)
    {
        List<(string Name, string Kind, string Input, string? Sha256)>? result = null;
        var imports = compilation.Binding.LibraryImports;
        for (var i = 0; i < imports.Count; i++)
        {
            var import = imports[i];
            if (import.Library == Kernel32Imports.LibraryName)
            {
                continue;
            }

            if (!ReferenceEquals(import.Decl.Declaration.CodeContext.Kotonoha, compilation.Kotonoha))
            {
                throw new InvalidDataException("Native supplies required by dependency modules are not linked yet.");
            }

            var known = false;
            for (var j = 0; j < (result?.Count ?? 0) && !known; j++)
            {
                known = result![j].Name == import.Library;
            }

            if (known)
            {
                continue;
            }

            var supply = settings.NativeLibraries.TryGetValue(WindowsProfile.Target, out var libraries) ? libraries.GetValueOrDefault(import.Library) : null;
            if (supply is null)
            {
                throw new InvalidDataException($"Native requirement '{import.Library}' has no NativeLibraries supply for {WindowsProfile.Target}.");
            }

            var (_, sha256) = NativeConfiguration.Expand(settings, WindowsProfile.Target, import.Library, supply);
            (result ??= new()).Add((import.Library, import.Kind, supply.Input, sha256));
        }

        result?.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static void CheckLibraryPath(string input)
    {
        CheckPath(input);
        if (!Path.GetExtension(input).Equals(".lib", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("NativeLibraries inputs must be .lib files.");
        }
    }

    private static void WriteManifest(Utf8JsonWriter json, ProjectFile settings, OutputKind outputKind, NativeLibraryInput? backend, List<(string Name, string Kind, string Input, string? Sha256)>? foreign, string irFile, string irHash, string projectDirectory, string outputDirectory, string? llvm)
    {
        json.WriteStartObject();
        json.WriteNumber("schemaVersion", 3);
        json.WriteString("target", WindowsProfile.Target);
        json.WriteStartObject("codegen");
        json.WriteString("profile", WindowsProfile.Name);
        json.WriteString("llvmVersion", WindowsProfile.LlvmVersion);
        json.WriteString("cpu", WindowsProfile.Cpu);
        WriteStrings(json, "features", [WindowsProfile.Features]);
        json.WriteString("relocationModel", WindowsProfile.RelocationModel);
        json.WriteString("codeModel", WindowsProfile.CodeModel);
        json.WriteString("unwindTables", WindowsProfile.UnwindTables);
        json.WriteString("optimization", settings.Optimization);
        json.WriteEndObject();
        json.WriteStartObject("backendSupport");
        json.WriteString("packageId", WindowsProfile.BackendPackageId);
        json.WriteNumber("abiVersion", WindowsProfile.BackendAbiVersion);
        json.WriteString("packageVersion", WindowsProfile.BackendVersion);
        json.WriteString("library", WindowsProfile.BackendLibrary);
        json.WriteString("artifactSha256", WindowsProfile.BackendSha256);
        WriteStrings(json, "providedSymbols", WindowsProfile.ProvidedSymbols);
        json.WriteEndObject();
        json.WriteString("irFile", irFile);
        json.WriteString("irSha256", irHash);
        json.WriteString("outputKind", outputKind == OutputKind.Library ? "Library" : "Application");
        json.WriteString("entry", outputKind == OutputKind.Library ? null : WindowsProfile.EntrySymbol);
        json.WriteString("subsystem", outputKind == OutputKind.Library ? null : WindowsProfile.Subsystem);
        json.WriteStartArray("libraries");
        var next = 0;
        WriteForeign(Kernel32Imports.LibraryName);
        json.WriteStartObject();
        json.WriteString("name", Kernel32Imports.LibraryName);
        json.WriteString("kind", "import");
        json.WriteString("generator", Kernel32Imports.Generator);
        json.WriteString("dll", Kernel32Imports.Dll);
        json.WriteString("definitionSha256", Kernel32Imports.DefinitionSha256);
        json.WriteEndObject();
        WriteForeign(WindowsProfile.BackendLibrary);
        json.WriteStartObject();
        json.WriteString("name", WindowsProfile.BackendLibrary);
        json.WriteString("kind", "static");
        if (backend is null)
        {
            json.WriteString("resolution", "toolchain");
        }
        else
        {
            json.WriteString("input", HasDirectory(backend.Input) ? Path.GetRelativePath(outputDirectory, Path.GetFullPath(backend.Input, projectDirectory)) : backend.Input);
        }

        json.WriteEndObject();
        WriteForeign(null);
        json.WriteEndArray();
        WriteStrings(json, "providedRuntimeSymbols", [WindowsProfile.FloatMarker]);
        WriteStrings(json, "expectedUndefinedSymbols", []);
        if (llvm is not null)
        {
            json.WriteStartObject("toolchain");
            json.WriteString("llvmBin", llvm);
            json.WriteEndObject();
        }

        json.WriteEndObject();

        // Writes the supplies ordered before the reserved name (all remaining ones for null).
        void WriteForeign(string? before)
        {
            for (; next < (foreign?.Count ?? 0) && (before is null || string.CompareOrdinal(foreign![next].Name, before) < 0); next++)
            {
                var (name, kind, input, sha256) = foreign![next];
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteString("kind", kind);
                json.WriteString("input", HasDirectory(input) ? Path.GetRelativePath(outputDirectory, Path.GetFullPath(input, projectDirectory)) : input);
                if (sha256 is not null)
                {
                    json.WriteString("sha256", sha256);
                }

                json.WriteEndObject();
            }
        }
    }

    private static void WriteStrings(Utf8JsonWriter json, string name, ReadOnlySpan<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
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
