// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The contract of Build (src/Kimi/Build, namespace Kimi.Build), P13, outside the compiler (the R7-R9 plan's §5.3, unit M2), stated
// here, in its entry file, for all its types.
// Owns: publication of a checked program's IR/manifest pair (EmissionArtifacts); the build record, the native build of a published
// pair (NativeToolchain.Build.cs), the built Application's run and the installed toolchain's verification
// (NativeToolchain.Verification.cs); the toolchain's identity and resolution (ToolchainIdentity, ToolchainResolver); kernel32's import
// identity (Kernel32Imports); the artifact paths and files (ArtifactPaths, ArtifactFiles).
// Input: an accepted compilation (publication); a published pair with its project's configuration, or a standalone manifest (the
// native build); the installed toolchain.
// Reads of the compiler, exactly these: the generation entry Compilation.Generate, with Compilation.Project and
// Compilation.Diagnostics; Binding.Startup.OutputKind and Binding.LibraryImports (an import's Library and Kind); the enum OutputKind,
// which project configuration states too (ProjectFile.OutputKind); the target profile WindowsProfile; CompilerRelease;
// CompilationBuildMetadata, through Project.BuildMetadata; the Backend's LlvmModuleText.SymbolFromName; and of Kimi.Diagnostics the
// record writer DiagnosticOwner.Report, DiagnosticCode, DiagnosticPartition and DiagnosticSeverity. Beside the compiler it reads
// project configuration: Project with its KimiOptions, ProjectFile, NativeConfiguration and NativeLibraryInput.
// Output: the pair, the build record, the staged libraries, the object with its inspection and the executable, each published by
// replacement; and, from publication only, records in the compiler's Emission partition (ArtifactPublicationFailed_Kd and
// InternalInvariant_Kd at the project; DECISIONS R5-M165). Every other failure is an exception its host renders (IsToolchainFailure).
// Invariants: no file of Compiler/, Checking/, Lsp/, Testing/ or Diagnostics/ reads a Build type. Build's readers are Project and the
// commands, the hosts that call it, and DependencyLock, which reads ArtifactFiles.Replace only
// (ArchitectureRulesTest.ComponentIsReadOnlyByItsReaders).
// Must not: check source semantics, or read compiler state outside the list above.
// Temporary: the two reads of Binding (Startup.OutputKind, LibraryImports) and SymbolFromName (ReadExternalDeclarations' IR re-parse)
// end with the plan's unit R9-B1, when the generation result carries the output kind, the required libraries and the external
// declarations. The enum OutputKind stays: RunProject compares ProjectFile.OutputKind with it and the manifest's output section takes it.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Diagnostics;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

/// <summary>Owns build-record publication and native tool/executable execution. Consumes checked product metadata
/// or validated emission artifacts; publishes success atomically and never checks source semantics.</summary>
internal static partial class NativeToolchain
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // A failure of the native build or of a toolchain command: publication's kinds (an output, a configuration or a supply), a tool that
    // does not start, or a manifest, record or installation file that is not the JSON its reader expects.
    internal static bool IsToolchainFailure(Exception ex)
        => EmissionArtifacts.IsPublicationFailure(ex) || ex is Win32Exception or JsonException or KeyNotFoundException or InvalidOperationException;

    internal static void Invalidate(ArtifactPaths paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!);
        WriteRecord(paths.Record, CreateBuildRecord());
    }

    internal static void RecordLibrary(Project project, ArtifactPaths paths, CancellationToken cancellationToken)
    {
        var metadata = project.BuildMetadata.Single();
        var record = new JsonObject
        {
            ["status"] = "checked", ["outputKind"] = "Library", ["input"] = project.FilePath,
            ["target"] = metadata.TargetTriple, ["debug"] = metadata.Debug, ["optimization"] = project.ProjectFile.Optimization,
            ["languageVersion"] = metadata.LanguageVersion, ["compilerVersion"] = metadata.CompilerVersion,
        };
        cancellationToken.ThrowIfCancellationRequested();
        WriteRecord(paths.Record, record);
    }

    internal static async Task<int> RunProject(Project project, CancellationToken cancellationToken)
    {
        var paths = ArtifactPaths.Create(project);
        if (project.ProjectFile.OutputKind != OutputKind.Application || !File.Exists(paths.Record) || !File.Exists(paths.Executable))
        {
            throw new InvalidDataException("No built Application is available. Run 'kimi build' first.");
        }

        using var record = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.Record, cancellationToken));
        var root = record.RootElement;
        if (root.GetProperty("status").GetString() is not ("linked" or "executed") ||
            root.GetProperty("optimization").GetString() != project.ProjectFile.Optimization ||
            root.GetProperty("executableSha256").GetString() != Hash(paths.Executable))
        {
            throw new InvalidDataException("The latest build failed or the binary was changed. Run 'kimi build' first.");
        }

        return await RunExecutable(paths.Executable, Path.GetFullPath(project.Directory.Length == 0 ? "." : project.Directory), cancellationToken);
    }

    internal static async Task<int> RunExecutable(string executable, string workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Executable not found: " + executable);
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start) ?? throw new IOException("Cannot start: " + executable);
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput(), cancellationToken);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError(), cancellationToken);
        await Wait(process, cancellationToken);
        await Task.WhenAll(stdout, stderr);
        return process.ExitCode;
    }

    internal static string ParseVersion(string output)
    {
        string? version = null;
        foreach (Match match in VersionPattern().Matches(output))
        {
            var value = match.Groups[1].Value;
            if (version is not null && version != value)
            {
                version = null;
                break;
            }

            version = value;
        }

        return version ?? throw new InvalidDataException("Cannot obtain unambiguous LLVM version: " + output.Trim());
    }

    /// <summary>Replaces checkout and user-profile roots with stable placeholders in published records.</summary>
    /// <param name="text">The text to redact.</param>
    /// <param name="directory">The project directory.</param>
    /// <returns>The redacted text.</returns>
    internal static string Redact(string text, string directory)
    {
        text = RedactRoot(text, Path.GetFullPath(directory.Length == 0 ? "." : directory), "/_/project");
        return RedactRoot(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "/_/user");

        static string RedactRoot(string text, string root, string replacement)
        {
            root = root.TrimEnd('\\', '/');
            if (root.Length == 0)
            {
                return text;
            }

            // The same root can appear JSON-escaped, with forward slashes, or with native separators.
            text = ReplaceRoot(text, root.Replace("\\", "\\\\", StringComparison.Ordinal), replacement);
            text = ReplaceRoot(text, root.Replace('\\', '/'), replacement);
            return ReplaceRoot(text, root, replacement);
        }

        static string ReplaceRoot(string text, string root, string replacement)
        {
            var index = text.IndexOf(root, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            var copied = 0;
            while (index >= 0)
            {
                // Replace only whole path prefixes, followed by a separator, whitespace, quote or the end.
                var end = index + root.Length;
                if (end == text.Length || text[end] is '\\' or '/' or '"' or '\'' || char.IsWhiteSpace(text[end]))
                {
                    builder.Append(text, copied, index - copied).Append(replacement);
                    copied = end;
                    index = end < text.Length ? text.IndexOf(root, end, StringComparison.OrdinalIgnoreCase) : -1;
                }
                else
                {
                    index = text.IndexOf(root, index + 1, StringComparison.OrdinalIgnoreCase);
                }
            }

            return builder.Append(text, copied, text.Length - copied).ToString();
        }
    }

    private static JsonObject CreateBuildRecord() => new()
    {
        ["status"] = "incomplete", ["compilerVersion"] = CompilerRelease.Version, ["llvmVersion"] = ToolchainIdentity.LlvmVersion,
        ["toolchainVerification"] = "not-performed", ["reportedVersionsMatched"] = null, ["unverifiedToolchain"] = true,
    };

    private static async Task<string> ExecuteTool(
        string tool, string[] args, string directory, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken, bool showErrors = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = Process.Start(start) ?? throw new IOException("Cannot start tool: " + tool);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await Wait(process, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Tool timed out: " + tool);
        }

        var output = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0)
        {
            throw new IOException($"Tool failed: {tool}; exit={process.ExitCode}\n{output}{errors}");
        }

        if (showErrors && !string.IsNullOrWhiteSpace(errors))
        {
            report(DiagnosticSeverity.Warning, errors.Trim());
        }

        return showErrors ? output : output + errors;
    }

    private static async Task Wait(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private static void WriteRecord(string path, JsonObject record)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, record.ToJsonString(JsonOptions), Utf8);
            Replace(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    [GeneratedRegex(@"(?im)^\s*(?:.*\b(?:LLVM|clang) version|LLD)\s+([0-9]+\.[0-9]+\.[0-9]+[^\s()]*)")]
    private static partial Regex VersionPattern();
}
