// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Build;

internal static partial class NativeToolchain
{
    // toolchain verify (IMPL 20.3; Tatara decision 20 Q3 (a)): a self-check of what the compiler carries, which reads no directory and
    // runs no tool. Two steps: its Tatara (Tatara.Instance: loaded, bound, its start check); each supply's embedded bytes against the
    // catalog's SHA-256 (VerifySupply): windows-x64's supplies, then each file of linux-x64's c (Tatara B7b4). A load or start-check
    // failure and a supply's mismatch are the toolchain's InvalidDataException (the catalog row test owns the supplies' invariant; a
    // mismatch means a defective build of Kimi). Each statement is reported as an Information line. The report (--Report) is written first as incomplete; at the end it is passed, or failed with the error for the
    // toolchain's failure or a cancellation, and a fault leaves it incomplete.
    internal static void Verify(string? reportPath, Action<DiagnosticSeverity, string> report, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var supplies = new JsonArray();
        var record = new JsonObject
        {
            ["status"] = "incomplete", ["compilerVersion"] = CompilerRelease.Version, ["llvmVersion"] = ToolchainIdentity.LlvmVersion,
        };
        if (reportPath is not null)
        {
            reportPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            WriteRecord(reportPath, record);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tatara = Tatara.Instance;
            record["tatara"] = new JsonObject { ["build"] = tatara.Build, ["abi"] = tatara.Abi, ["llvmVersion"] = tatara.LlvmVersion };
            report(DiagnosticSeverity.Information, $"Tatara: build {tatara.Build}, ABI {tatara.Abi}, LLVM {tatara.LlvmVersion}");
            record["supplies"] = supplies;
            foreach (var supply in ToolchainIdentity.Supplies)
            {
                report(DiagnosticSeverity.Information, $"{supply.Supply.Name}: {VerifySupply(supply, supplies)} (the catalog's)");
            }

            foreach (var file in ToolchainIdentity.Musl)
            {
                report(DiagnosticSeverity.Information, $"{file.Row} {file.Supply} {file.Name}: {VerifySupply(file, supplies)} (the catalog's)");
            }

            record["status"] = "passed";
        }
        catch (Exception ex) when (IsToolchainFailure(ex) || ex is OperationCanceledException)
        {
            record["status"] = "failed";
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

    // One supply's statement: the SHA-256 of its embedded bytes against its row's (the catalog's), added to the report's supplies before
    // a mismatch throws. Returns the hash.
    internal static string VerifySupply(CompilerSupply supply, JsonArray supplies) =>
        VerifyBytes(supply.Open(), supply.Sha256, new JsonObject { ["name"] = supply.Supply.Name }, supply.Supply.Name, supplies);

    // One file of a row's supply: the same statement, its entry naming the row and the file.
    internal static string VerifySupply(SupplyFile file, JsonArray supplies) =>
        VerifyBytes(file.Open(), file.Sha256, new JsonObject { ["name"] = file.Supply, ["row"] = file.Row, ["file"] = file.Name }, $"{file.Row} {file.Supply} {file.Name}", supplies);

    private static string VerifyBytes(Stream bytes, string expected, JsonObject entry, string subject, JsonArray supplies)
    {
        string hash;
        using (bytes)
        {
            hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        entry["sha256"] = hash;
        entry["expectedSha256"] = expected;
        entry["hashMatched"] = hash == expected;
        supplies.Add((JsonNode)entry);
        return hash == expected ? hash : throw new InvalidDataException($"The compiler's {subject} does not match its catalog SHA-256.");
    }
}
