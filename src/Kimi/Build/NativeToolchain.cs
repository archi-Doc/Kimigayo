// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The contract of Build (src/Kimi/Build, namespace Kimi.Build), P13, outside the compiler (the R7-R9 plan's §5.3, unit M2), stated
// here, in its entry file, for all its types.
// Owns: publication of a checked program's IR/manifest pair (EmissionArtifacts); the build record, the native build of a published
// pair (NativeToolchain.Build.cs), whose LLVM and LLD work runs in process through Tatara's four calls (the compiler's Tatara: linked into
// kimi.exe, or tatara.dll beside Kimi.dll), so a native build launches no tool and reads no toolchain root; the built Application's run;
// toolchain verify's self-check of what the compiler carries (NativeToolchain.Verification.cs, two steps: its Tatara's start check and
// its supplies' bytes against the catalog), which reads no directory and runs no tool, so no code here launches an LLVM tool; the
// toolchain's identity with the compiler's supplies (IMPL 20.4's reserved libraries, embedded and staged as a program's input is;
// ToolchainIdentity, with Kernel32Imports' DLL and generator names); the artifact paths and files (ArtifactPaths, ArtifactFiles). The
// development toolchain's tools and files are checked by src/backend/windows-x64/check-toolchain.ps1, which no code here reads.
// Input: an accepted compilation (publication); a published pair with its project's configuration, or a standalone manifest (the
// native build); the compiler's Tatara and embedded supplies (toolchain verify).
// Reads of the compiler, exactly these: the generation entry Compilation.Generate, with Compilation.Project and
// Compilation.Diagnostics, and its result Compilation.Generated (GeneratedModule: the output kind, the required libraries and the
// external declarations, each an ExternalFunction read for its Symbol and DllImport; R9-B1); the enum OutputKind, which project
// configuration states too (ProjectFile.OutputKind); the target profile WindowsProfile; CompilerRelease; CompilationBuildMetadata, which
// Project.Build reads and passes; and of Kimi.Diagnostics the record writer DiagnosticOwner.Report, DiagnosticCode, DiagnosticPartition and DiagnosticSeverity. Beside the compiler it reads
// project configuration: Project with its KimiOptions, ProjectFile, NativeConfiguration and NativeLibraryInput; and of Misc JobProcess,
// Kimi's launcher for the programs it runs, in which the run's program runs (PLAN G1188).
// Output: the pair, the build record, the staged libraries, the object and the executable, each published by
// replacement, the executable last: linked to a private name, it moves into place after the success record that names its hash, and a
// failed move invalidates the record again, so its path keeps the previous executable until a build succeeds (IMPL 20.3.2; Tatara
// decision 18; NativeBuild: in the two windows IMPL 20.3.2 states, decision 19, the attempt's success record names a hash no file at
// the path has, and run --no-build runs only an executable whose hash the record names); and, from publication only, records in the
// compiler's Emission partition (ArtifactPublicationFailed_Kd and InternalInvariant_Kd at the project; DECISIONS R5-M165). Every other
// failure is an exception: one IsToolchainFailure classifies is the toolchain's, which its host renders as a message; any other ends the
// result it interrupted as Faulted (CheckFaulted_Kd): under Project.Build the build's (R9-B2), under a command the command's own
// (CommandExecution, PLAN G984).
// Invariants: no file of Compiler/, Checking/, Lsp/, Testing/ or Diagnostics/ reads a Build type. Build's readers are Project and the
// commands, the hosts that call it, and DependencyLock, which reads ArtifactFiles.Replace only
// (ArchitectureRulesTest.ComponentIsReadOnlyByItsReaders). A build of a record (Project.Build, build --Manifest) holds the record's lock
// (Begin) from its first write to its last, so two builds of one record, at either optimization, never interleave.
// Must not: check source semantics, or read compiler state outside the list above.
// The enum OutputKind stays: RunProject compares ProjectFile.OutputKind with it and the manifest's output section takes it.
// The link manifest's fixed fields (one table), the compiler's supplies' entries (their rows) and its readers are LinkManifest's: no
// manifest read raises an accessor's exception (R9-B1).
// The build record's reader compares ArtifactFiles' total reads (Member, Text, Number) with what it expects, so a malformed record
// fails as its InvalidDataException (PLAN G983), and IsToolchainFailure lists no JSON accessor's type
// (KeyNotFoundException, InvalidOperationException). Known limit (R9-M10's, narrowed): inside the toolchain's calls the set still
// classifies by type whatever raised the exception, so one of its remaining kinds (publication's, R5-M165; Win32Exception;
// JsonException) raised by the host's report or a defect here prints as the toolchain's failure; any other type is a fault.
// Tatara's failures (design A2 §5) are the toolchain's: a Tatara that does not load or does not match an InvalidDataException naming the
// remedy; a failed call (return 1, or 2 after which every later call in the process fails naming the first cause) an IOException
// "Tatara failed: <operation>" with LLVM's or LLD's text, as a tool's failure is.
// The embedded catalog's reads (ToolchainIdentity) fail as its type initializer (TypeInitializationException), a compiler fault.
// toolchain verify states the catalog's invariant (Tatara B6b3): a supply whose embedded bytes differ from its row's SHA-256 is the
// toolchain's InvalidDataException.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Microsoft.Win32.SafeHandles;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

/// <summary>Owns build-record publication, the native build and the executable's run. Consumes checked product metadata
/// or validated emission artifacts; publishes success atomically and never checks source semantics.</summary>
internal static partial class NativeToolchain
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // A failure of a toolchain step (the native build, a Library's record, a run or a toolchain command): publication's kinds (an output,
    // a configuration or a supply), a program that does not start (run's), or a manifest or record that does not parse (JsonException)
    // or is not what its reader expects (its InvalidDataException, PLAN G983). Its readers apply it only around the toolchain's own calls:
    // Project.Build and Project.Run (a compiler step's exception is its result's fault, R9-B2), CommandExecution.Toolchain (build
    // --Manifest, run <exe>, toolchain verify) and TestCommand around its native build (PLAN G984).
    internal static bool IsToolchainFailure(Exception ex)
        => EmissionArtifacts.IsPublicationFailure(ex) || ex is Win32Exception or JsonException;

    // A build begins (IMPL 20.3.2): it creates the record's directory, takes its output's lock, the file beside its record
    // (Name.link.build.lock, opened without sharing and kept on disk), and then invalidates the record; the caller holds the lock until
    // the build ends. While another build holds it (IsSharingFailure), the build reports once that it waits and retries every 100 ms.
    // The token is read only in that wait, which it cancels before anything is written: a free lock is taken and the record invalidated
    // under a cancelled token too. Any other failure is the output's. Every exception after the open closes the lock again, so no failed
    // build leaves its output locked in a long-lived process. O0 and O2 share the record, so they share the lock. Its callers are the
    // two hosts that begin a build of a record, Project.Build and BuildManifest (test builds into a directory of its own), which read
    // nothing before it but the configuration.
    internal static async Task<FileStream> Begin(string record, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        var path = Path.ChangeExtension(record, ".lock");
        FileStream output;
        var waiting = false;
        while (true)
        {
            try
            {
                output = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                break;
            }
            catch (IOException ex) when (IsSharingFailure(ex))
            {
                if (!waiting)
                {
                    report(DiagnosticSeverity.Information, "Waiting for another build: " + path);
                    waiting = true;
                }
            }

            await Task.Delay(100, cancellationToken);
        }

        var begun = false;
        try
        {
            Invalidate(record);
            begun = true;
            return output;
        }
        finally
        {
            if (!begun)
            {
                output.Dispose();
            }
        }
    }

    // A checked Library's record, with the front end's metadata of its one target, which the project reads (a compiler step's read).
    internal static void RecordLibrary(Project project, CompilationBuildMetadata metadata, ArtifactPaths paths, CancellationToken cancellationToken)
    {
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
        if (Text(Member(root, "status")) is not ("linked" or "executed") ||
            Text(Member(root, "optimization")) != project.ProjectFile.Optimization ||
            Text(Member(root, "executableSha256")) != Hash(paths.Executable))
        {
            throw new InvalidDataException("The latest build failed or the binary was changed. Run 'kimi build' first.");
        }

        return await RunExecutable(paths.Executable, Path.GetFullPath(project.Directory.Length == 0 ? "." : project.Directory), cancellationToken);
    }

    // Runs a program and returns its exit code. The program runs in a job of its own (JobProcess), so its whole tree ends with the run:
    // after the program's exit its remaining descendants (as a test case's do), on cancellation the program with them, and when Kimi's
    // process ends (a second Ctrl+C included) with the job's last handle (PLAN G1188). It shares Kimi's standard input (Tatara decision
    // 15; when Kimi has none, StandardInput, the program reads end of input); its output is copied to Kimi's raw standard streams and
    // is complete when this returns or throws: the copies take no token and are awaited after the job's end, so the pipes reach their
    // end. After a cancellation the outcome is the cancellation (130): the copies are awaited without observing their outcome and the
    // cancellation is rethrown. The exit is awaited on a thread of its own.
    internal static async Task<int> RunExecutable(string executable, string workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Executable not found: " + executable);
        }

        using var program = new JobProcess(executable, workingDirectory, null, StandardInput());
        var output = Task.WhenAll(
            program.Stdout.CopyToAsync(Console.OpenStandardOutput(), CancellationToken.None),
            program.Stderr.CopyToAsync(Console.OpenStandardError(), CancellationToken.None));
        var exit = Task.Factory.StartNew(() => program.Wait(Timeout.Infinite), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await exit.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            program.Kill();
            await exit;
            await output.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            throw;
        }

        program.Kill(); // The descendants the program left end with the run.
        await output;
        return (int)program.ExitCode;
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
    };

    // Kimi's own standard input for run's program (Tatara decision 15), which the caller does not own; null when Kimi has none: no handle,
    // or a value that is no open handle of this process (one its parent passed without letting it inherit), so the program gets
    // JobProcess's closed input and reads end of input.
    private static SafeFileHandle? StandardInput()
    {
        var input = new SafeFileHandle(GetStdHandle(-10), ownsHandle: false);
        return !input.IsInvalid && GetHandleInformation(input.DangerousGetHandle(), out _) ? input : null;
    }

    // The record's invalidation: the incomplete record a build writes first (Begin) and again after a failed move of its executable.
    private static void Invalidate(string record) => WriteRecord(record, CreateBuildRecord());

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

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int kind);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(nint handle, out uint flags);
}
