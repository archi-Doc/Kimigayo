// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kimi.Compiler;
using Kimi.Diagnostics;
using static Kimi.Build.ArtifactFiles;

namespace Kimi.Build;

internal static partial class NativeToolchain
{
    // The installation's command, which every setup remedy names: setup installs from the pinned LLVM release archive, extracted.
    internal static readonly string SetupCommand = $"src/backend/windows-x64/setup.ps1 -LlvmRoot <the LLVM {ToolchainIdentity.LlvmVersion} archive, extracted>";

    // Installation and explicit verification cover the entire distributed tool set.
    private static readonly string[] InstallationTools =
        ["clang", "opt", "llc", "lld-link", "llvm-nm", "llvm-readobj", "llvm-objdump", "llvm-lib", "llvm-dlltool"];

    internal static string RequireInstalledFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Toolchain file not found: {path}. Run {SetupCommand} for the selected toolchain.", path);
        }

        return path;
    }

    internal static async Task Verify(
        string? configuredRoot, string? configuredBin, string? reportPath, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        var root = ToolchainResolver.ResolveRoot(configuredRoot);
        var bin = configuredBin is null ? root : ResolvePath(configuredBin, Directory.GetCurrentDirectory());
        var timer = Stopwatch.StartNew();
        var identities = new JsonObject();
        var record = new JsonObject
        {
            ["status"] = "incomplete", ["toolchainVerification"] = "failed", ["compilerVersion"] = CompilerRelease.Version,
            ["llvmVersion"] = ToolchainIdentity.LlvmVersion, ["reportedVersionsMatched"] = null, ["unverifiedToolchain"] = true,
            ["root"] = Redact(root, Directory.GetCurrentDirectory()), ["llvmBin"] = Redact(bin, Directory.GetCurrentDirectory()),
            ["tools"] = identities,
        };
        if (reportPath is not null)
        {
            reportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            WriteRecord(reportPath, record);
        }

        try
        {
            var installationFile = RequireInstalledFile(Path.Combine(root, "installation.json"));
            using var installation = JsonDocument.Parse(await File.ReadAllBytesAsync(installationFile, cancellationToken));
            // The record's shape before any tool: schema 1 of this profile, with the tools' and the supporting DLLs' hashes by name and
            // tatara.dll's hash.
            var installed = installation.RootElement;
            var tools = Member(installed, "tools");
            var supportingDlls = Member(installed, "supportingDlls");
            var tataraSha256 = Text(Member(Member(installed, "tatara"), "sha256"));
            if (Number(Member(installed, "schemaVersion")) != 1 || Text(Member(installed, "profile")) != WindowsProfile.Name ||
                tools.ValueKind != JsonValueKind.Object || supportingDlls.ValueKind != JsonValueKind.Object || tataraSha256 is null)
            {
                throw new InvalidDataException($"Invalid toolchain installation record. Run {SetupCommand} again.");
            }

            foreach (var name in InstallationTools)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = RequireInstalledFile(Path.Combine(bin, name + ".exe"));
                var hash = Hash(path);
                var identity = new JsonObject { ["path"] = Redact(path, Directory.GetCurrentDirectory()), ["sha256"] = hash };
                identities.Add(name, identity);
                if (name is not ("llvm-lib" or "llvm-dlltool"))
                {
                    var banner = await ToolProcess.Execute(path, ["--version"], root, report, cancellationToken, showErrors: false);
                    var actual = ParseVersion(banner);
                    identity["actualVersion"] = actual;
                    identity["expectedVersion"] = ToolchainIdentity.LlvmVersion;
                    identity["versionMatched"] = actual == ToolchainIdentity.LlvmVersion;
                    if (actual != ToolchainIdentity.LlvmVersion)
                    {
                        record["reportedVersionsMatched"] = false;
                        throw new InvalidDataException($"LLVM version mismatch: tool={path}; expected={ToolchainIdentity.LlvmVersion}; actual={actual}.");
                    }
                }

                var expected = Text(Member(tools, name));
                identity["expectedSha256"] = expected;
                identity["hashMatched"] = hash == expected;
                if (hash != expected || (name == Kernel32Imports.Generator && hash != Kernel32Imports.DlltoolSha256))
                {
                    throw new InvalidDataException($"Tool executable SHA-256 mismatch: {path}. Run {SetupCommand} after an intentional update.");
                }
            }

            record["reportedVersionsMatched"] = true;
            // Supporting DLLs are part of the installed tool inputs, not project inputs.
            foreach (var dll in supportingDlls.EnumerateObject())
            {
                if (Path.GetFileName(dll.Name) != dll.Name || Hash(RequireInstalledFile(Path.Combine(bin, dll.Name))) != Text(dll.Value))
                {
                    throw new InvalidDataException("Toolchain supporting DLL SHA-256 mismatch: " + dll.Name);
                }
            }

            // tatara.dll after the tools: Tatara.Load's checks (found, loaded and bound, its ABI and LLVM version), then its hash.
            var tatara = Tatara.Load(root);
            var tataraHash = Hash(tatara.Path);
            record["tatara"] = new JsonObject
            {
                ["path"] = Redact(tatara.Path, Directory.GetCurrentDirectory()), ["sha256"] = tataraHash, ["build"] = tatara.Build,
                ["abi"] = tatara.Abi, ["llvmVersion"] = tatara.LlvmVersion,
            };
            if (tataraHash != tataraSha256)
            {
                throw new InvalidDataException($"tatara.dll SHA-256 mismatch: {tatara.Path}. Run {SetupCommand} again.");
            }

            var backend = RequireInstalledFile(Path.Combine(root, "windows_x64", ToolchainIdentity.BackendFile));
            var backendHash = Hash(backend);
            record["backendSha256"] = backendHash;
            if (backendHash != ToolchainIdentity.BackendSha256)
            {
                throw new InvalidDataException("Adopted backend SHA-256 mismatch: " + backend);
            }

            await VerifyKernel32(root, bin, record, report, cancellationToken);
            record["toolchainVerification"] = "passed";
            record["unverifiedToolchain"] = false;
            record["status"] = "passed";
        }
        catch (Exception ex) when (IsToolchainFailure(ex) || ex is OperationCanceledException)
        {
            record["error"] = Redact(ex.Message, Directory.GetCurrentDirectory());
            throw;
        }
        finally
        {
            record["seconds"] = timer.Elapsed.TotalSeconds;
            if (reportPath is not null)
            {
                WriteRecord(reportPath, record);
            }
        }
    }

    // The installed kernel32 import library: its recorded generation conditions and hash, its definition, and the DLL, architecture and
    // symbol set the tools read from it. The record keeps the installation's entry.
    private static async Task VerifyKernel32(
        string root, string bin, JsonObject record, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        var kernel = RequireInstalledFile(Path.Combine(root, "windows_x64", "kernel32.lib"));
        var metadataFile = RequireInstalledFile(Path.ChangeExtension(kernel, ".json"));
        using var metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(metadataFile, cancellationToken));
        var entry = metadata.RootElement;
        Kernel32Imports.ValidateInstallation(entry, Hash(kernel));
        if (Hash(RequireInstalledFile(Path.ChangeExtension(kernel, ".def"))) != Kernel32Imports.DefinitionSha256)
        {
            throw new InvalidDataException("Installed kernel32 definition SHA-256 mismatch.");
        }

        var importedDll = await ToolProcess.Execute(Path.Combine(bin, "llvm-dlltool.exe"), ["-I", kernel], root, report, cancellationToken);
        var inspection = await ToolProcess.Execute(Path.Combine(bin, "llvm-readobj.exe"), ["--file-headers", kernel], root, report, cancellationToken);
        Kernel32Imports.ValidateLibrary(importedDll, inspection);
        record["kernel32"] = JsonNode.Parse(entry.GetRawText());
    }
}

/// <summary>An LLVM tool's one run for toolchain verify, the only reader: file-scoped, so no other file (a native build's above all)
/// can launch a tool. Its standard output (with its standard error when <c>showErrors</c> is false), a Warning for its standard error
/// otherwise; a nonzero exit or a run past five minutes is an IOException, the toolchain's failure.</summary>
file static class ToolProcess
{
    internal static async Task<string> Execute(
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
            await NativeToolchain.Wait(process, timeout.Token);
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
}
