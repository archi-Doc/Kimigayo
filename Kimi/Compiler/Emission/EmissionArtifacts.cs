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
            if (!compilation.Emission.TryPrepare(out var plan, out failure))
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

                    if (name == "kernel32")
                    {
                        throw new InvalidDataException("kernel32 is generated automatically. Remove the kernel32 entry from NativeLibraries.");
                    }
                    else if (name == "kimi_backend")
                    {
                        backend = library;
                    }
                }
            }

            if (backend.Kind != "static")
            {
                throw new InvalidDataException("kimi_backend must be static.");
            }

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
                plan.WriteIr(writer);
            }

            var hash = Hash(tempIr);
            using (var stream = new FileStream(tempManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteNumber("schemaVersion", 2);
                json.WriteString("target", WindowsProfile.Target);
                json.WriteStartObject("codegen");
                json.WriteString("profile", WindowsProfile.Name);
                json.WriteString("llvmVersion", WindowsProfile.LlvmVersion);
                json.WriteString("cpu", "x86-64");
                json.WriteStartArray("features");
                json.WriteStringValue("+sse2");
                json.WriteEndArray();
                json.WriteString("relocationModel", "pic");
                json.WriteString("codeModel", "small");
                json.WriteString("unwindTables", "async");
                json.WriteString("optimization", settings.Optimization);
                json.WriteEndObject();
                json.WriteStartObject("backendSupport");
                json.WriteString("packageId", "kimi-backend-windows-x64");
                json.WriteNumber("abiVersion", 1);
                json.WriteString("packageVersion", WindowsProfile.BackendVersion);
                json.WriteString("library", "kimi_backend");
                json.WriteString("artifactSha256", WindowsProfile.BackendSha256);
                json.WriteStartArray("providedSymbols");
                json.WriteStringValue("__chkstk");
                json.WriteStringValue("memcpy");
                json.WriteStringValue("memmove");
                json.WriteStringValue("memset");
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteString("irFile", Path.GetFileName(destination));
                json.WriteString("irSha256", hash);
                json.WriteString("outputKind", "Application");
                json.WriteString("entry", "__kimi_start");
                json.WriteString("subsystem", "console");
                json.WriteStartArray("libraries");
                json.WriteStartObject();
                json.WriteString("name", "kernel32");
                json.WriteString("kind", "import");
                json.WriteString("generator", Kernel32Imports.Generator);
                json.WriteString("dll", Kernel32Imports.Dll);
                json.WriteString("definitionSha256", Kernel32Imports.DefinitionSha256);
                json.WriteEndObject();
                WriteLibrary(json, "kimi_backend", backend, directory, outputDirectory);
                json.WriteEndArray();
                json.WriteStartArray("providedRuntimeSymbols");
                json.WriteStringValue("_fltused");
                json.WriteEndArray();
                json.WriteStartArray("expectedUndefinedSymbols");
                json.WriteEndArray();
                if (llvm is not null)
                {
                    json.WriteStartObject("toolchain");
                    json.WriteString("llvmBin", llvm);
                    json.WriteEndObject();
                }

                json.WriteEndObject();
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

    private static void WriteLibrary(Utf8JsonWriter json, string name, NativeLibraryInput input, string projectDirectory, string outputDirectory)
    {
        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteString("kind", input.Kind);
        json.WriteString("input", HasDirectory(input.Input) ? Path.GetRelativePath(outputDirectory, Path.GetFullPath(input.Input, projectDirectory)) : input.Input);
        json.WriteEndObject();
    }
}
