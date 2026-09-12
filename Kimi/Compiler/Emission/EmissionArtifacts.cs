// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json;
using static Kimi.Compiler.ArtifactFiles;

namespace Kimi.Compiler;

/// <summary>Publication of a checked, matched pre-optimization IR/manifest pair. Does not run native tools.</summary>
public static class EmissionArtifacts
{
    /// <summary>Publishes both files, with the manifest last, or reports a failure without claiming old files.</summary>
    /// <param name="compilation">The analyzed compilation.</param>
    /// <param name="irPath">The published IR path on success.</param>
    /// <param name="failure">The diagnostic on failure.</param>
    /// <returns>Whether the complete matched pair was published.</returns>
    public static bool Publish(Compilation compilation, out string? irPath, out string? failure)
        => Publish(compilation, null, out irPath, out failure);

    internal static bool Publish(Compilation compilation, ArtifactPaths? paths, out string? irPath, out string? failure)
    {
        irPath = null;
        string? tempIr = null;
        string? tempManifest = null;
        try
        {
            if (!compilation.Emission.TryPrepare(out var module, out failure))
            {
                return false;
            }

            var project = compilation.Project;
            var settings = project.ProjectFile;
            if (WindowsProfile.BackendVersion.Length == 0 || WindowsProfile.BackendSha256.Length != 64)
            {
                throw new InvalidDataException("The native backend has no adopted version/hash; a candidate cannot certify artifact publication.");
            }

            var directory = Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory);
            paths ??= ArtifactPaths.Create(project);
            var destination = paths.Ir;
            var manifest = paths.Manifest;
            var outputDirectory = Path.GetDirectoryName(destination)!;
            var backend = ResolveBackend(settings);
            if (HasDirectory(backend.Input))
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
                module.WriteIr(writer);
            }

            var hash = Hash(tempIr);
            using (var stream = new FileStream(tempManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteManifest(json, settings, backend, Path.GetFileName(destination), hash, directory, outputDirectory, llvm);
            }

            File.Move(tempIr, destination, true);
            File.Move(tempManifest, manifest, true);
            irPath = destination;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failure = ex.Message;
            return false;
        }
        finally
        {
            DeleteTemporary(tempIr);
            DeleteTemporary(tempManifest);
        }
    }

    // Validates every configured library, even when unused, and selects the backend supply (SPEC 20.8.2).
    private static NativeLibraryInput ResolveBackend(ProjectFile settings)
    {
        var backend = new NativeLibraryInput { Kind = "static", Input = WindowsProfile.BackendFile };
        if (settings.NativeLibraries.TryGetValue(WindowsProfile.Target, out var libraries))
        {
            if (libraries is null)
            {
                throw new InvalidDataException("NativeLibraries target entries must be mappings.");
            }

            foreach (var (name, library) in libraries)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Contains('\0') || library is null || library.Kind is not ("import" or "static"))
                {
                    throw new InvalidDataException("NativeLibraries requires nonempty logical names and import/static entries.");
                }

                CheckPath(library.Input);
                if (!Path.GetExtension(library.Input).Equals(".lib", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("NativeLibraries inputs must be .lib files.");
                }

                if (name == Kernel32Imports.LibraryName)
                {
                    throw new InvalidDataException("kernel32 is generated automatically. Remove the kernel32 entry from NativeLibraries.");
                }
                else if (name == WindowsProfile.BackendLibrary)
                {
                    backend = library;
                }
            }
        }

        if (backend.Kind != "static")
        {
            throw new InvalidDataException("kimi_backend must be static.");
        }

        return backend;
    }

    private static void WriteManifest(Utf8JsonWriter json, ProjectFile settings, NativeLibraryInput backend, string irFile, string irHash, string projectDirectory, string outputDirectory, string? llvm)
    {
        json.WriteStartObject();
        json.WriteNumber("schemaVersion", 2);
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
        json.WriteString("outputKind", "Application");
        json.WriteString("entry", WindowsProfile.EntrySymbol);
        json.WriteString("subsystem", WindowsProfile.Subsystem);
        json.WriteStartArray("libraries");
        json.WriteStartObject();
        json.WriteString("name", Kernel32Imports.LibraryName);
        json.WriteString("kind", "import");
        json.WriteString("generator", Kernel32Imports.Generator);
        json.WriteString("dll", Kernel32Imports.Dll);
        json.WriteString("definitionSha256", Kernel32Imports.DefinitionSha256);
        json.WriteEndObject();
        json.WriteStartObject();
        json.WriteString("name", WindowsProfile.BackendLibrary);
        json.WriteString("kind", backend.Kind);
        json.WriteString("input", HasDirectory(backend.Input) ? Path.GetRelativePath(outputDirectory, Path.GetFullPath(backend.Input, projectDirectory)) : backend.Input);
        json.WriteEndObject();
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
