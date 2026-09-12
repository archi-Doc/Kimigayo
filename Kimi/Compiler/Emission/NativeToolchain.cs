// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>Runs the Windows LLVM tools and existing executables without a shell.</summary>
internal static class NativeToolchain
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] ToolNames = ["opt", "llc", "lld-link", "llvm-nm", "llvm-readobj"];
    private static readonly string[] BackendSymbols = ["__chkstk", "memcpy", "memmove", "memset"];
    private static readonly HashSet<string> AllowedUndefined = new(
        BackendSymbols.Concat(new[] { "__imp_GetProcessHeap", "__imp_HeapAlloc", "__imp_HeapFree", "__imp_GetStdHandle", "__imp_WriteFile", "__imp_GetLastError", "__imp_ExitProcess" }),
        StringComparer.Ordinal);

    internal sealed record Artifacts(string Ir, string Manifest, string Record, string Stem)
    {
        internal string Executable => this.Stem + ".exe";
    }

    internal static bool IsToolchainFailure(Exception ex)
        => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception or JsonException or KeyNotFoundException or InvalidOperationException;

    internal static Artifacts GetArtifacts(Project project)
    {
        if (project.ProjectFile.Optimization is not ("O0" or "O2"))
        {
            throw new InvalidDataException("Optimization must be O0 or O2.");
        }

        var directory = Path.GetFullPath(string.IsNullOrEmpty(project.Directory) ? "." : project.Directory);
        var ir = ResolvePath(project.ProjectFile.OutputPath ?? Path.Combine("bin", WindowsProfile.Target, project.Name + ".ll"), directory);
        if (!Path.GetExtension(ir).Equals(".ll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OutputPath must have a .ll extension.");
        }

        var manifest = Path.ChangeExtension(ir, ".link.json");
        return new(ir, manifest, Path.ChangeExtension(manifest, ".build.json"), Path.Combine(Path.GetDirectoryName(ir)!, Path.GetFileNameWithoutExtension(ir) + "." + project.ProjectFile.Optimization));
    }

    internal static void Invalidate(Artifacts paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!);
        WriteRecord(paths.Record, new() { ["status"] = "incomplete" });
    }

    internal static async Task Build(Project project, Artifacts paths, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native build currently requires Windows.");
        }

        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.Manifest, cancellationToken));
        var manifest = document.RootElement;
        ValidateManifest(manifest);
        var outputDirectory = Path.GetDirectoryName(paths.Ir)!;
        if (ResolvePath(manifest.GetProperty("irFile").GetString()!, outputDirectory) != paths.Ir || Hash(paths.Ir) != manifest.GetProperty("irSha256").GetString())
        {
            throw new InvalidDataException("IR/manifest SHA-256 or path mismatch.");
        }

        var bin = project.KimiOptions.LlvmBin ?? project.ProjectFile.LlvmBin;
        if (string.IsNullOrWhiteSpace(bin))
        {
            throw new InvalidDataException("Configure LlvmBin in the project or specify --LlvmBin. LLVM is not installed automatically.");
        }

        bin = ResolvePath(bin, project.KimiOptions.LlvmBin is null && project.Directory.Length != 0 ? Path.GetFullPath(project.Directory) : Directory.GetCurrentDirectory());
        var tools = new Dictionary<string, string>(StringComparer.Ordinal);
        var identities = new Dictionary<string, object>();
        var matched = true;
        var record = new Dictionary<string, object?>
        {
            ["status"] = "incomplete", ["compilerVersion"] = CompilerRelease.Version, ["llvmVersion"] = WindowsProfile.LlvmVersion,
            ["tools"] = identities, ["irSha256"] = Hash(paths.Ir), ["optimization"] = project.ProjectFile.Optimization,
        };
        foreach (var name in ToolNames)
        {
            var tool = Path.Combine(bin, name + ".exe");
            string version;
            string actual;
            try
            {
                version = await ExecuteTool(tool, ["--version"], outputDirectory, report, cancellationToken, showErrors: false);
                actual = ParseVersion(version);
            }
            catch (Exception ex) when (IsToolchainFailure(ex))
            {
                throw new InvalidDataException($"Cannot obtain LLVM version: tool={tool}; expected={WindowsProfile.LlvmVersion}; {ex.Message}", ex);
            }

            var same = actual == WindowsProfile.LlvmVersion;
            if (!same)
            {
                var message = $"LLVM version mismatch: tool={tool}; expected={WindowsProfile.LlvmVersion}; actual={actual}.";
                if (!project.KimiOptions.AllowUnpinnedToolchain)
                {
                    throw new InvalidDataException(message + " Use --AllowUnpinnedToolchain true only for exploratory builds.");
                }

                report(DiagnosticSeverity.Warning, message + " Continuing with an unverified toolchain.");
            }

            matched &= same;
            tools.Add(name, tool);
            identities.Add(name, new { path = Redact(tool, project.Directory), version = Redact(version, project.Directory), actualVersion = actual, expectedVersion = WindowsProfile.LlvmVersion, versionMatched = same, sha256 = Hash(tool) });
        }

        record["reportedVersionsMatched"] = matched;
        var dlltool = Path.Combine(bin, "llvm-dlltool.exe");
        var dlltoolHash = Hash(dlltool);
        var dlltoolMatched = dlltoolHash == Kernel32Imports.DlltoolSha256;
        if (!dlltoolMatched && !project.KimiOptions.AllowUnpinnedToolchain)
        {
            throw new InvalidDataException("llvm-dlltool SHA-256 mismatch. Use --AllowUnpinnedToolchain true only for exploratory builds.");
        }

        if (!dlltoolMatched)
        {
            report(DiagnosticSeverity.Warning, "llvm-dlltool SHA-256 mismatch; continuing with an unverified toolchain.");
        }

        tools.Add("llvm-dlltool", dlltool);
        identities.Add("llvm-dlltool", new { path = Redact(dlltool, project.Directory), sha256 = dlltoolHash, expectedSha256 = Kernel32Imports.DlltoolSha256, hashMatched = dlltoolMatched });
        record["unverifiedToolchain"] = !matched || !dlltoolMatched;
        WriteRecord(paths.Record, record);
        var libraries = new List<string>();
        var libraryIdentities = new List<object>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest.GetProperty("libraries").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            var kind = item.GetProperty("kind").GetString();
            if (!seen.Add(name) || kind is not ("import" or "static"))
            {
                throw new InvalidDataException("Invalid or duplicate native library.");
            }

            string path;
            if (name == "kernel32")
            {
                Kernel32Imports.ValidateManifest(item);
                path = await GenerateKernel32(tools, paths.Stem, report, cancellationToken);
                record["kernel32"] = new { generator = Kernel32Imports.Generator, dll = Kernel32Imports.Dll, definitionSha256 = Kernel32Imports.DefinitionSha256, sha256 = Hash(path) };
            }
            else
            {
                path = ResolvePath(item.GetProperty("input").GetString()!, outputDirectory);
            }

            var hash = Hash(path);
            if ((name == "kernel32" && kind != "import") || (name == "kimi_backend" && (kind != "static" || hash != WindowsProfile.BackendSha256)))
            {
                throw new InvalidDataException("Native library kind or backend SHA-256 mismatch.");
            }

            libraries.Add(path);
            libraryIdentities.Add(new { name, path = Redact(path, project.Directory), sha256 = hash });
        }

        if (!seen.Contains("kernel32") || !seen.Contains("kimi_backend"))
        {
            throw new InvalidDataException("Missing kernel32 or kimi_backend library.");
        }

        record["libraries"] = libraryIdentities;
        WriteRecord(paths.Record, record);
        Task<string> Tool(string name, params string[] args) => ExecuteTool(tools[name], args, outputDirectory, report, cancellationToken);
        await Tool("opt", "-passes=verify", "-disable-output", paths.Ir);
        var selectedIr = paths.Ir;
        if (project.ProjectFile.Optimization == "O2")
        {
            selectedIr = paths.Stem + ".ll";
            await Tool("opt", "-S", "-passes=default<O2>", "-mtriple=" + WindowsProfile.Target, paths.Ir, "-o", selectedIr);
            var ir = await File.ReadAllTextAsync(selectedIr, cancellationToken);
            ir = Regex.Replace(ir, @"(?m)^(?:; ModuleID = .*|source_filename = .*|!\d+ = .*!DIFile\(.*)$", m => Redact(m.Value, project.Directory));
            await File.WriteAllTextAsync(selectedIr, ir, new UTF8Encoding(false), cancellationToken);
            await Tool("opt", "-passes=verify", "-disable-output", selectedIr);
        }

        var obj = paths.Stem + ".obj";
        await Tool("llc", "-" + project.ProjectFile.Optimization, "-filetype=obj", "-mtriple=" + WindowsProfile.Target, "-mcpu=x86-64", "-mattr=+sse2", "-relocation-model=pic", "-code-model=small", selectedIr, "-o", Path.GetFileName(obj));
        var undefined = await Tool("llvm-nm", "--undefined-only", "--format=posix", obj);
        foreach (var line in undefined.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!AllowedUndefined.Contains(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]))
            {
                throw new InvalidDataException("Unsupported actual object dependency: " + line);
            }
        }

        var defined = await Tool("llvm-nm", "--defined-only", "--extern-only", "--format=posix", obj);
        var inspection = await Tool("llvm-readobj", "--unwind", "--coff-directives", obj);
        if (Regex.Matches(defined, @"(?m)^_fltused [BD] ").Count != 1 || !inspection.Contains("RuntimeFunction", StringComparison.Ordinal) || inspection.Contains("DEFAULTLIB", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Invalid _fltused definition, unwind information or hidden default library.");
        }

        await File.WriteAllTextAsync(paths.Stem + ".inspection.txt", Redact(inspection, project.Directory), cancellationToken);
        // Link to a fresh file; a failed link must never publish a partially written executable.
        var temporary = paths.Stem + "." + Guid.NewGuid().ToString("N") + ".exe";
        try
        {
            await Tool("lld-link", new[] { obj }.Concat(libraries).Concat(new[] { "/entry:__kimi_start", "/subsystem:console", "/nodefaultlib", "/Brepro", "/out:" + temporary }).ToArray());
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, paths.Executable, true);
            record["status"] = "linked";
            record["executable"] = Path.GetFileName(paths.Executable);
            record["executableSha256"] = Hash(paths.Executable);
            record["objectUndefinedSymbols"] = undefined.Trim();
            WriteRecord(paths.Record, record);
        }
        finally
        {
            File.Delete(temporary);
        }

        report(DiagnosticSeverity.Information, "Built: " + paths.Executable);
    }

    internal static void ValidateManifest(JsonElement root)
    {
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
        {
            throw new InvalidDataException("Link manifest schema 2 is required. Re-emit the manifest for generated kernel32 imports.");
        }

        var code = root.GetProperty("codegen");
        var support = root.GetProperty("backendSupport");
        if (root.GetProperty("target").GetString() != WindowsProfile.Target ||
            root.GetProperty("outputKind").GetString() != "Application" || root.GetProperty("entry").GetString() != "__kimi_start" || root.GetProperty("subsystem").GetString() != "console" ||
            code.GetProperty("profile").GetString() != WindowsProfile.Name || code.GetProperty("llvmVersion").GetString() != WindowsProfile.LlvmVersion ||
            code.GetProperty("cpu").GetString() != "x86-64" || !code.GetProperty("features").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "+sse2" }) ||
            code.GetProperty("relocationModel").GetString() != "pic" || code.GetProperty("codeModel").GetString() != "small" || code.GetProperty("unwindTables").GetString() != "async" ||
            code.GetProperty("optimization").GetString() is not ("O0" or "O2"))
        {
            throw new InvalidDataException("Manifest does not describe the supported Windows Application profile.");
        }

        if (support.GetProperty("packageId").GetString() != "kimi-backend-windows-x64" || support.GetProperty("abiVersion").GetInt32() != 1 ||
            support.GetProperty("packageVersion").GetString() != WindowsProfile.BackendVersion || support.GetProperty("artifactSha256").GetString() != WindowsProfile.BackendSha256 ||
            support.GetProperty("library").GetString() != "kimi_backend" || !support.GetProperty("providedSymbols").EnumerateArray().Select(x => x.GetString()).SequenceEqual(BackendSymbols) ||
            !root.GetProperty("providedRuntimeSymbols").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "_fltused" }) ||
            root.GetProperty("expectedUndefinedSymbols").EnumerateArray().Any(x => x.GetProperty("provider").GetString() != "kimi_backend" || !BackendSymbols.Contains(x.GetProperty("symbol").GetString())))
        {
            throw new InvalidDataException("Invalid backend supply identity or runtime dependency.");
        }
    }

    internal static async Task<int> RunProject(Project project, CancellationToken cancellationToken)
    {
        var paths = GetArtifacts(project);
        if (project.ProjectFile.OutputKind != OutputKind.Application || !File.Exists(paths.Record) || !File.Exists(paths.Executable))
        {
            throw new InvalidDataException("No built Application is available. Run 'kimi build' first.");
        }

        using var record = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.Record, cancellationToken));
        var root = record.RootElement;
        if (root.GetProperty("status").GetString() is not ("linked" or "executed") ||
            root.GetProperty("optimization").GetString() != project.ProjectFile.Optimization || root.GetProperty("executableSha256").GetString() != Hash(paths.Executable))
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
        var versions = Regex.Matches(output, @"(?im)^\s*(?:.*\b(?:LLVM|clang) version|LLD)\s+([0-9]+\.[0-9]+\.[0-9]+[^\s()]*)")
            .Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        return versions.Length == 1 ? versions[0] : throw new InvalidDataException("Cannot obtain unambiguous LLVM version: " + output.Trim());
    }

    private static async Task<string> GenerateKernel32(Dictionary<string, string> tools, string stem, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        var staging = stem + ".kernel32-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "kernel32.def"), Kernel32Imports.Definition, new UTF8Encoding(false), cancellationToken);
            await ExecuteTool(tools["llvm-dlltool"], ["-m", "i386:x86-64", "-d", "kernel32.def", "-l", "kernel32.lib"], staging, report, cancellationToken);
            var dll = await ExecuteTool(tools["llvm-dlltool"], ["-I", "kernel32.lib"], staging, report, cancellationToken);
            var inspection = await ExecuteTool(tools["llvm-readobj"], ["--file-headers", "kernel32.lib"], staging, report, cancellationToken);
            Kernel32Imports.ValidateLibrary(dll, inspection);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(Path.Combine(staging, "kernel32.def"), stem + ".kernel32.def", true);
            File.Move(Path.Combine(staging, "kernel32.lib"), stem + ".kernel32.lib", true);
            return stem + ".kernel32.lib";
        }
        finally
        {
            // Exact generated files in a fresh staging directory, never a recursive user-directory cleanup.
            File.Delete(Path.Combine(staging, "kernel32.def"));
            File.Delete(Path.Combine(staging, "kernel32.lib"));
            Directory.Delete(staging);
        }
    }

    private static async Task<string> ExecuteTool(string tool, string[] args, string directory, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken, bool showErrors = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
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

    private static string ResolvePath(string value, string directory)
    {
        if (string.IsNullOrWhiteSpace(value) || value.AsSpan().IndexOfAny("\0\r\n\"") >= 0 || value.StartsWith('-'))
        {
            throw new InvalidDataException("Invalid artifact/tool/library path.");
        }

        return Path.GetFullPath(value, directory);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WriteRecord(string path, Dictionary<string, object?> record)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, JsonOptions), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string Redact(string text, string directory)
    {
        foreach (var (root, replacement) in new[] { (Path.GetFullPath(directory.Length == 0 ? "." : directory), "/_/project"), (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "/_/user") })
        {
            if (root.Length == 0)
            {
                continue;
            }

            foreach (var form in new[] { root.Replace("\\", "\\\\", StringComparison.Ordinal), root.Replace('\\', '/'), root })
            {
                text = Regex.Replace(text, Regex.Escape(form.TrimEnd('\\', '/')) + "(?=[\\\\/\\s\"']|$)", _ => replacement, RegexOptions.IgnoreCase);
            }
        }

        return text;
    }
}
