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
    /// (DependencyConfiguration).</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <param name="paths">The artifact paths, or null for the project's.</param>
    /// <param name="irPath">The published IR path on success.</param>
    /// <returns>Whether the complete matched pair was published.</returns>
    internal static bool Publish(Compilation compilation, ArtifactPaths? paths, out string? irPath)
    {
        irPath = null;
        if (ToolchainIdentity.BackendVersion.Length == 0 || ToolchainIdentity.BackendSha256.Length != 64)
        {
            return FailBeforeGeneration(compilation, DiagnosticCode.InternalInvariant_Kd, null);
        }

        var settings = compilation.Project.ProjectFile;
        string directory, destination, manifest, outputDirectory;
        string? tempIr = null, tempManifest = null, llvm = null;
        NativeLibraryInput? backend;
        List<(string Name, string Kind, string Input, string? Sha256)>? foreign;
        StreamWriter? writer = null;
        try
        {
            // The publication's steps before and after code generation classify their failures; generation runs between them, outside
            // either catch, so only GenerateInto classifies its failure (its output's) and any other leaves for the fault (R5-M169).
            try
            {
                var project = compilation.Project;
                directory = Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory);
                paths ??= ArtifactPaths.Create(project);
                destination = paths.Ir;
                manifest = paths.Manifest;
                outputDirectory = Path.GetDirectoryName(destination)!;
                backend = ResolveBackend(settings);
                foreign = ResolveForeignSupplies(compilation, settings);
                if (backend is not null && HasDirectory(backend.Input) &&
                    !Hash(Path.GetFullPath(backend.Input, directory)).Equals(ToolchainIdentity.BackendSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The selected backend archive does not match the adopted SHA-256.");
                }

                if (settings.LlvmBin is { } bin)
                {
                    CheckPath(bin);
                    llvm = Path.GetRelativePath(outputDirectory, Path.GetFullPath(bin, directory));
                }

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

                var hash = Hash(tempIr);
                using (var stream = new FileStream(tempManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    json.WriteStartObject();
                    WriteProfile(json, settings.Optimization);
                    WriteOutput(json, Path.GetFileName(destination), hash, compilation.Binding.Startup.OutputKind);
                    WriteLibraries(json, backend, foreign, directory, outputDirectory);
                    WriteLinkInputs(json, llvm);
                    json.WriteEndObject();
                }

                Replace(tempIr, destination);
                Replace(tempManifest, manifest);
                irPath = destination;
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

                    if (sha256 is not null && !sha256.Equals(ToolchainIdentity.BackendSha256, StringComparison.OrdinalIgnoreCase))
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
    // module's requirement, and combined-module native records are not generated, so Binding makes a
    // dependency module's import, but kernel32's, the located Unsupported_Kd (DECISIONS BU-92).
    private static List<(string Name, string Kind, string Input, string? Sha256)>? ResolveForeignSupplies(
        Compilation compilation, ProjectFile settings)
    {
        List<(string Name, string Kind, string Input, string? Sha256)>? result = null;
        var imports = compilation.Binding.LibraryImports;
        for (var i = 0; i < imports.Count; i++)
        {
            var import = imports[i];
            if (import.Library == WindowsProfile.Kernel32Library)
            {
                continue;
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

    // The manifest's fixed head (IMPL 20.8.3, schema 3): the target, the profile's code generation settings with the project's
    // optimization, and the adopted backend supply's identity.
    private static void WriteProfile(Utf8JsonWriter json, string optimization)
    {
        json.WriteNumber("schemaVersion", 3);
        json.WriteString("target", WindowsProfile.Target);
        json.WriteStartObject("codegen");
        json.WriteString("profile", WindowsProfile.Name);
        json.WriteString("llvmVersion", ToolchainIdentity.LlvmVersion);
        json.WriteString("cpu", WindowsProfile.Cpu);
        WriteStrings(json, "features", [WindowsProfile.Features]);
        json.WriteString("relocationModel", WindowsProfile.RelocationModel);
        json.WriteString("codeModel", WindowsProfile.CodeModel);
        json.WriteString("unwindTables", WindowsProfile.UnwindTables);
        json.WriteString("optimization", optimization);
        json.WriteEndObject();
        json.WriteStartObject("backendSupport");
        json.WriteString("packageId", ToolchainIdentity.BackendPackageId);
        json.WriteNumber("abiVersion", ToolchainIdentity.BackendAbiVersion);
        json.WriteString("packageVersion", ToolchainIdentity.BackendVersion);
        json.WriteString("library", WindowsProfile.BackendLibrary);
        json.WriteString("artifactSha256", ToolchainIdentity.BackendSha256);
        WriteStrings(json, "providedSymbols", WindowsProfile.ProvidedSymbols);
        json.WriteEndObject();
    }

    // The published module: the IR file with its hash, and the output kind with an Application's entry and subsystem.
    private static void WriteOutput(Utf8JsonWriter json, string irFile, string irHash, OutputKind outputKind)
    {
        json.WriteString("irFile", irFile);
        json.WriteString("irSha256", irHash);
        json.WriteString("outputKind", outputKind == OutputKind.Library ? "Library" : "Application");
        json.WriteString("entry", outputKind == OutputKind.Library ? null : WindowsProfile.EntrySymbol);
        json.WriteString("subsystem", outputKind == OutputKind.Library ? null : WindowsProfile.Subsystem);
    }

    // The libraries in Ordinal name order (SPEC 20.3.2): the generated kernel32 import library, the backend archive (the toolchain's, or
    // the configured input) and the foreign supplies around them, an input with a directory relative to the output directory.
    private static void WriteLibraries(
        Utf8JsonWriter json,
        NativeLibraryInput? backend,
        List<(string Name, string Kind, string Input, string? Sha256)>? foreign,
        string projectDirectory,
        string outputDirectory)
    {
        json.WriteStartArray("libraries");
        var next = 0;
        WriteForeign(WindowsProfile.Kernel32Library);
        json.WriteStartObject();
        json.WriteString("name", WindowsProfile.Kernel32Library);
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
            json.WriteString("input", Relative(backend.Input));
        }

        json.WriteEndObject();
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
    }

    // What the link takes beside the libraries: the runtime symbols the module provides, no expected undefined symbol, and the
    // configured LLVM directory.
    private static void WriteLinkInputs(Utf8JsonWriter json, string? llvm)
    {
        WriteStrings(json, "providedRuntimeSymbols", [WindowsProfile.FloatMarker]);
        WriteStrings(json, "expectedUndefinedSymbols", []);
        if (llvm is not null)
        {
            json.WriteStartObject("toolchain");
            json.WriteString("llvmBin", llvm);
            json.WriteEndObject();
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
