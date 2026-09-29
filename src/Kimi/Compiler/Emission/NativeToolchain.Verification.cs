// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kimi.Diagnostics;
using static Kimi.Compiler.ArtifactFiles;

namespace Kimi.Compiler;

internal static partial class NativeToolchain
{
    // Installation and explicit verification cover the entire distributed tool set.
    private static readonly string[] InstallationTools = ["clang", "opt", "llc", "lld-link", "llvm-nm", "llvm-readobj", "llvm-objdump", "llvm-lib", "llvm-dlltool"];

    internal static string RequireInstalledFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Toolchain file not found: {path}. Run src/backend/windows-x64/setup.ps1 for the selected toolchain.", path);
        }

        return path;
    }

    internal static async Task Verify(string? configuredRoot, string? configuredBin, string? reportPath, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        var root = ToolchainResolver.ResolveRoot(configuredRoot);
        var bin = configuredBin is null ? root : ResolvePath(configuredBin, Directory.GetCurrentDirectory());
        var timer = Stopwatch.StartNew();
        var identities = new JsonObject();
        var record = new JsonObject
        {
            ["status"] = "incomplete", ["toolchainVerification"] = "failed", ["compilerVersion"] = CompilerRelease.Version,
            ["llvmVersion"] = WindowsProfile.LlvmVersion, ["reportedVersionsMatched"] = null, ["unverifiedToolchain"] = true,
            ["root"] = Redact(root, Directory.GetCurrentDirectory()), ["llvmBin"] = Redact(bin, Directory.GetCurrentDirectory()), ["tools"] = identities,
        };
        if (reportPath is not null)
        {
            reportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            WriteRecord(reportPath, record);
        }

        try
        {
            using var installation = JsonDocument.Parse(await File.ReadAllBytesAsync(RequireInstalledFile(Path.Combine(root, "installation.json")), cancellationToken));
            var installed = installation.RootElement;
            if (installed.GetProperty("schemaVersion").GetInt32() != 1 || installed.GetProperty("profile").GetString() != WindowsProfile.Name)
            {
                throw new InvalidDataException("Invalid toolchain installation record. Run setup.ps1 again.");
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
                    var banner = await ExecuteTool(path, ["--version"], root, report, cancellationToken, showErrors: false);
                    var actual = ParseVersion(banner);
                    identity["actualVersion"] = actual;
                    identity["expectedVersion"] = WindowsProfile.LlvmVersion;
                    identity["versionMatched"] = actual == WindowsProfile.LlvmVersion;
                    if (actual != WindowsProfile.LlvmVersion)
                    {
                        record["reportedVersionsMatched"] = false;
                        throw new InvalidDataException($"LLVM version mismatch: tool={path}; expected={WindowsProfile.LlvmVersion}; actual={actual}.");
                    }
                }

                var expected = installed.GetProperty("tools").GetProperty(name).GetString();
                identity["expectedSha256"] = expected;
                identity["hashMatched"] = hash == expected;
                if (hash != expected || (name == Kernel32Imports.Generator && hash != Kernel32Imports.DlltoolSha256))
                {
                    throw new InvalidDataException($"Tool executable SHA-256 mismatch: {path}. Run setup.ps1 after an intentional update.");
                }
            }

            record["reportedVersionsMatched"] = true;
            // Supporting DLLs are part of the installed tool inputs, not project inputs.
            foreach (var dll in installed.GetProperty("supportingDlls").EnumerateObject())
            {
                if (Path.GetFileName(dll.Name) != dll.Name || Hash(RequireInstalledFile(Path.Combine(bin, dll.Name))) != dll.Value.GetString())
                {
                    throw new InvalidDataException("Toolchain supporting DLL SHA-256 mismatch: " + dll.Name);
                }
            }

            var backend = RequireInstalledFile(Path.Combine(root, "windows_x64", WindowsProfile.BackendFile));
            var backendHash = Hash(backend);
            record["backendSha256"] = backendHash;
            if (backendHash != WindowsProfile.BackendSha256)
            {
                throw new InvalidDataException("Adopted backend SHA-256 mismatch: " + backend);
            }

            var kernel = RequireInstalledFile(Path.Combine(root, "windows_x64", "kernel32.lib"));
            using var metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(RequireInstalledFile(Path.ChangeExtension(kernel, ".json")), cancellationToken));
            var entry = metadata.RootElement;
            Kernel32Imports.ValidateInstallation(entry, Hash(kernel));
            if (Hash(RequireInstalledFile(Path.ChangeExtension(kernel, ".def"))) != Kernel32Imports.DefinitionSha256)
            {
                throw new InvalidDataException("Installed kernel32 definition SHA-256 mismatch.");
            }

            var importedDll = await ExecuteTool(Path.Combine(bin, "llvm-dlltool.exe"), ["-I", kernel], root, report, cancellationToken);
            var inspection = await ExecuteTool(Path.Combine(bin, "llvm-readobj.exe"), ["--file-headers", kernel], root, report, cancellationToken);
            Kernel32Imports.ValidateLibrary(importedDll, inspection);
            record["kernel32"] = JsonNode.Parse(entry.GetRawText());
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
}
