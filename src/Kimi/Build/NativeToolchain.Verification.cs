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
    // runs no tool. Two steps: its Tatara (Tatara.Instance: loaded, bound, its start check); each compiler supply's file's embedded
    // bytes against the catalog's SHA-256 (VerifySupply), every row's in one form (the Tatara plan's B7b7, C-469 n1). A load or start-check
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
                foreach (var file in supply.Files)
                {
                    report(DiagnosticSeverity.Information, $"{Subject(supply, file)}: {VerifySupply(supply, file, supplies)} (the catalog's)");
                }
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

    // One file of a compiler supply: the SHA-256 of its embedded bytes against its catalog's, its entry naming the supply, the row and
    // the file added to the report's supplies before a mismatch throws. Returns the hash.
    internal static string VerifySupply(CompilerSupply supply, SupplyFile file, JsonArray supplies)
    {
        string hash;
        using (var bytes = file.Open())
        {
            hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        supplies.Add((JsonNode)new JsonObject
        {
            ["name"] = supply.Supply.Name, ["row"] = supply.Row.Name, ["file"] = file.Name, ["sha256"] = hash, ["expectedSha256"] = file.Sha256,
            ["hashMatched"] = hash == file.Sha256,
        });
        return hash == file.Sha256 ? hash : throw new InvalidDataException($"The compiler's {Subject(supply, file)} does not match its catalog SHA-256.");
    }

    // A file's subject in the statements: its row, supply and catalog name.
    private static string Subject(CompilerSupply supply, SupplyFile file) => $"{supply.Row.Name} {supply.Supply.Name} {file.Name}";
}
