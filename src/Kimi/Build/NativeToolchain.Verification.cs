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
    // bytes against the catalog's SHA-256 and, once they match, its stored facts against those bytes (VerifySupply), every row's in one
    // form (the Tatara plan's B7b7, C-469 n1; SupplyFacts); then each row's linker script's embedded bytes against the catalog's SHA-256,
    // stated in that form without facts (VerifyScript, through the supplies' one compare; a script is not inspected; the Tatara plan's
    // B9f). A load or start-check failure and a supply's or a script's mismatch, of its bytes or its facts, are the toolchain's
    // InvalidDataException (the catalog row and facts row tests own the supplies' invariants; a mismatch means a defective build of
    // Kimi). Each statement is reported as an Information line. The report (--Report) is written first as incomplete; at the end it is
    // passed, or failed with the error for the toolchain's failure or a cancellation, and a fault (a malformed facts file among them)
    // leaves it incomplete.
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
                    report(DiagnosticSeverity.Information, $"{Subject(supply.Row, supply.Supply.Name, file)}: {VerifySupply(supply, file, supplies)} (the catalog's), facts matched");
                }
            }

            foreach (var (row, file) in ToolchainIdentity.Scripts)
            {
                report(DiagnosticSeverity.Information, $"{Subject(row, "linker script", file)}: {VerifyScript(row, file)} (the catalog's)");
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

    // One file of a compiler supply: the SHA-256 of its embedded bytes against its catalog's, then, once they match, its stored facts
    // loaded with that hash through the build's loader (SupplyFacts, never LinkInputs: verify states facts it opened and keeps no
    // entry); its entry naming the supply, the row and the file is added to the report's supplies with factsMatched false before either
    // check can throw, and set true once the facts load ("facts matched": they name these bytes and parse; that they are the current
    // Tatara's reading is NativeToolchainTest's facts row's). Returns the hash.
    internal static string VerifySupply(CompilerSupply supply, SupplyFile file, JsonArray supplies)
    {
        var hash = Sha256(file);
        var entry = new JsonObject
        {
            ["name"] = supply.Supply.Name, ["row"] = supply.Row.Name, ["file"] = file.Name, ["sha256"] = hash, ["expectedSha256"] = file.Sha256,
            ["hashMatched"] = hash == file.Sha256, ["factsMatched"] = false,
        };
        supplies.Add((JsonNode)entry);
        var subject = Subject(supply.Row, supply.Supply.Name, file);
        SupplyFacts(subject, file, Matched(subject, file, hash));
        entry["factsMatched"] = true;
        return hash;
    }

    // A row's linker script: the SHA-256 of its embedded bytes against its catalog's (Matched), stated without facts (a script is not
    // inspected). Returns the hash.
    internal static string VerifyScript(TargetProfile row, SupplyFile file) => Matched(Subject(row, "linker script", file), file, Sha256(file));

    // A file's subject in the statements: its row, supply (or "linker script") and catalog name.
    private static string Subject(TargetProfile row, string supply, SupplyFile file) => $"{row.Name} {supply} {file.Name}";

    // A compiler's file's hash against its catalog's SHA-256, the one compare of a supply's file and a script: the hash, else the
    // toolchain's InvalidDataException naming the file.
    private static string Matched(string subject, SupplyFile file, string hash) =>
        hash == file.Sha256 ? hash : throw new InvalidDataException($"The compiler's {subject} does not match its catalog SHA-256.");

    // The SHA-256 of a compiler's file's embedded bytes.
    private static string Sha256(SupplyFile file)
    {
        using var bytes = file.Open();
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
