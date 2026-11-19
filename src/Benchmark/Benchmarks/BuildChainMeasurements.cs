// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Benchmark;

/// <summary>
/// The BuildChain series (BuildChain.md): single-shot <c>kimi build</c>, a new <c>dotnet Kimi.dll build</c> process each time, created
/// inside a job object from its first instruction; per run the wall time, the job's CPU time, peak committed memory and TotalProcesses.
/// One compiler, or two interleaved A, B, A, ..., A from one folder under neutral names (the same compiler twice is the A=A pass).
/// Run with <c>--build-chain &lt;name&gt; &lt;Kimi.dll folder A&gt; [&lt;folder B&gt;] [--rounds n] [--toolchain root]</c>.
/// </summary>
internal static partial class BuildChainMeasurements
{
    private const string Target = "x86_64-pc-windows-msvc";

    // The large program's generator parameters (BuildChain.md): item Types, functions and loop steps per function.
    private const int LargeTypes = 32;
    private const int LargeFunctions = 256;
    private const int LargeSteps = 8;

    // Every input by name, repository source (null: generated here) and SHA-256 of its bytes, fixed at A1 and not re-chosen.
    private static readonly (string Name, string? Source, string Sha256)[] Inputs =
    [
        ("Empty", null, "A80291B54336AE5BAAE2D979887B2287CDB7D325BDFCFD5B3DF7AD306FDC7815"),
        ("Hello", "docs/examples/Hello/Hello.kimi", "145BE933320F459F09AB3407A0DB7C43C562016D9930605E6B0A18153536E731"),
        ("Milestone1", "tests/milestones/Milestone1.kimi", "9BB0047DD071BDF9A59247677C00C56205281B254421456C07CE3347D1343441"),
        ("Milestone17", "tests/milestones/Milestone17.kimi", "1914A012B7146EF344646C7847EEE8EFEEE599680DA2BA2172FCD49678A71581"),
        ("Milestone30", "tests/milestones/Milestone30.kimi", "8A2D0FE6A0DFB4C5F20B7D33B9B39BF4A48033C83921C57C29B99AE32B932533"),
        ("Large", null, "D4B7F9FA4884B5F1121A2C84244D03BAD1802DFDEF1A81146E614CBD9546871B"),
    ];

    public static void Run(string[] args)
    {
        var positional = new List<string>();
        var rounds = 20;
        string? toolchainOption = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rounds" when i + 1 < args.Length:
                    rounds = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--toolchain" when i + 1 < args.Length:
                    toolchainOption = args[++i];
                    break;
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        var repository = FindRepository();
        var toolchain = Path.GetFullPath(toolchainOption ?? Path.Combine(repository, "toolchain"));
        var output = Path.Combine(repository, "artifacts", "benchmarks", positional.FirstOrDefault() ?? string.Empty);
        var work = Path.Combine(repository, "temp", "build-chain", positional.FirstOrDefault() ?? string.Empty);
        if (positional.Count is < 2 or > 3 || positional.Any(p => p.StartsWith('-')) || rounds < 1 || Directory.Exists(output) || Directory.Exists(work))
        {
            throw new ArgumentException("Use --build-chain <new run name> <Kimi.dll folder A> [<folder B>] [--rounds n] [--toolchain root]; the name must be unused under artifacts/benchmarks and temp/build-chain.");
        }

        Directory.CreateDirectory(output);
        var dotnet = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).Select(d => Path.Combine(d, "dotnet.exe")).First(File.Exists);
        var variants = positional.Skip(1).Select((folder, i) => (Name: i == 0 ? "A" : "B", Folder: Path.GetFullPath(folder), Copy: Path.Combine(work, "c", i == 0 ? "A" : "B"))).ToArray();
        var conditions = new Dictionary<string, object?>
        {
            ["started"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            ["machine"] = Environment.MachineName,
            ["processors"] = Environment.ProcessorCount,
            ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["powerScheme"] = Capture("powercfg", "/getactivescheme"),
            ["dotnet"] = Capture(dotnet, "--version"),
            ["dotnetHost"] = dotnet,
            ["defenderRealTimeProtection"] = Capture("powershell.exe", "-NoProfile", "-Command", "(Get-MpComputerStatus).RealTimeProtectionEnabled"),
            ["environment"] = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("KIMI_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).Select(k => $"{k}={Environment.GetEnvironmentVariable(k)}").ToArray(),
            ["pinning"] = "none",
            ["tiering"] = "default",
            ["rounds"] = rounds,
            ["order"] = variants.Length == 1 ? "A per round" : "A, B, A, ..., A per row (rounds + 1 runs of A, rounds runs of B)",
            ["toolchainRoot"] = toolchain,
            ["compilers"] = variants.Select(v => new { v.Name, v.Folder, v.Copy, KimiDllSha256 = Sha256(File.ReadAllBytes(Path.Combine(v.Folder, "Kimi.dll"))) }).ToArray(),
            ["busyProcessesAtStart"] = Busy(),
        };
        var rows = new List<object>();
        var summary = new StringBuilder($"# BuildChain {positional[0]}\n\n| Row | Compiler | First wall ms | p50 wall ms | p95 wall ms | p50 CPU ms | p95 CPU ms | p50 peak commit MiB | TotalProcesses |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
        var status = "incomplete";
        string? failure = null;
        try
        {
            foreach (var variant in variants)
            {
                foreach (var file in Directory.EnumerateFiles(variant.Folder, "*", SearchOption.AllDirectories))
                {
                    var copy = Path.Combine(variant.Copy, Path.GetRelativePath(variant.Folder, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(file, copy);
                }
            }

            // Every input is checked before the first run.
            var texts = Inputs.Select(i => i.Source is null ? Generate(i.Name) : File.ReadAllBytes(Path.Combine(repository, i.Source))).ToArray();
            var changed = Inputs.Zip(texts).Where(p => Sha256(p.Second) != p.First.Sha256).Select(p => $"{p.First.Name} {Sha256(p.Second)}").ToArray();
            if (changed.Length != 0)
            {
                throw new InvalidDataException($"Inputs differ from their fixed sources: {string.Join(", ", changed)}.");
            }

            foreach (var ((name, _, _), text) in Inputs.Zip(texts))
            {
                foreach (var level in new[] { "O0", "O2" })
                {
                    var sourceProject = WriteInput(Path.Combine(work, "in", $"{name}-{level}", "source"), name, level, text);
                    var pairProject = WriteInput(Path.Combine(work, "in", $"{name}-{level}", "pair"), name, level, text);
                    var log = Path.Combine(work, "in", $"{name}-{level}", "emit.log");
                    Require(Measure(dotnet, [Path.Combine(variants[0].Copy, "Kimi.dll"), "emit", pairProject, "--ToolchainRoot", toolchain], Path.GetDirectoryName(pairProject)!, log), log);
                    var manifest = Path.Combine(Path.GetDirectoryName(pairProject)!, "bin", Target, $"{name}.link.json");
                    foreach (var (kind, arguments) in new[] { ("source", new[] { "build", sourceProject }), ("manifest", new[] { "build", "--Manifest", manifest }) })
                    {
                        rows.Add(MeasureRow($"{name} {level} {kind}", variants.Select(v => (v.Name, v.Copy)).ToArray(), [.. arguments, "--ToolchainRoot", toolchain], (dotnet, rounds), summary));
                    }
                }
            }

            status = "passed";
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            throw;
        }
        finally
        {
            conditions["finished"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            conditions["busyProcessesAtEnd"] = Busy();
            var inputs = Inputs.Select(i => new { i.Name, Source = i.Source ?? "generated (BuildChainMeasurements.Generate)", i.Sha256 }).ToArray();
            File.WriteAllText(Path.Combine(output, "rounds.json"), JsonSerializer.Serialize(new { status, error = failure, conditions, inputs, rows }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            File.WriteAllText(Path.Combine(output, "summary.md"), summary.Append($"\nStatus: {status}{(failure is null ? string.Empty : $" ({failure})")}. Raw rounds and conditions: rounds.json.\n").ToString());
            Console.WriteLine($"Summary: {output} ({status})");
        }
    }

    // One row: every run in order, A alone or A, B, A, ..., A; the first run of each compiler is reported apart and kept in p50/p95.
    private static object MeasureRow(string row, (string Name, string Copy)[] variants, string[] arguments, (string Dotnet, int Rounds) run, StringBuilder summary)
    {
        var order = Enumerable.Range(0, variants.Length == 1 ? run.Rounds : (2 * run.Rounds) + 1).Select(i => variants[i % variants.Length]).ToArray();
        var rounds = new List<(string Variant, Round Round)>();
        foreach (var (name, copy) in order)
        {
            var log = Path.Combine(copy, "..", $"{name}.log");
            // arguments[^3] is the project or the manifest; the run starts in its folder.
            var round = Measure(run.Dotnet, [Path.Combine(copy, "Kimi.dll"), .. arguments], Path.GetDirectoryName(arguments[^3])!, log);
            Require(round, log);
            rounds.Add((name, round));
        }

        var statistics = new Dictionary<string, object>();
        foreach (var (name, _) in variants)
        {
            var mine = rounds.Where(r => r.Variant == name).Select(r => r.Round).ToArray();
            var wall = mine.Select(r => r.WallMilliseconds).ToArray();
            var cpu = mine.Select(r => r.UserMilliseconds + r.KernelMilliseconds).ToArray();
            var peak = mine.Select(r => (double)r.PeakCommitBytes).ToArray();
            var processes = $"{mine.Min(r => r.TotalProcesses)}-{mine.Max(r => r.TotalProcesses)}";
            statistics[name] = new
            {
                First = mine[0],
                WallP50 = Percentile(wall, 0.5),
                WallP95 = Percentile(wall, 0.95),
                CpuP50 = Percentile(cpu, 0.5),
                CpuP95 = Percentile(cpu, 0.95),
                PeakCommitBytesP50 = Percentile(peak, 0.5),
                PeakCommitBytesP95 = Percentile(peak, 0.95),
                TotalProcesses = processes,
            };
            var line = string.Create(CultureInfo.InvariantCulture, $"| {row} | {name} | {mine[0].WallMilliseconds:F0} | {Percentile(wall, 0.5):F0} | {Percentile(wall, 0.95):F0} | {Percentile(cpu, 0.5):F0} | {Percentile(cpu, 0.95):F0} | {Percentile(peak, 0.5) / 1048576:F1} | {processes} |");
            summary.Append(line).Append('\n');
            Console.WriteLine(line);
        }

        return new { row, arguments, rounds = rounds.Select(r => new { r.Variant, r.Round }).ToArray(), statistics };
    }

    // Nearest rank over every round, the first included.
    private static double Percentile(double[] values, double fraction) => values.Order().ElementAt((int)Math.Ceiling(fraction * values.Length) - 1);

    private static string WriteInput(string directory, string name, string level, byte[] text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".kimi"), text);
        var project = Path.Combine(directory, name + ".kimiproj");
        File.WriteAllText(project, $"Targets=\n  \"{Target}\"\nOutputKind=\"Application\"\nOptimization=\"{level}\"\n");
        return project;
    }

    // The generated inputs: Empty is the smallest Application; Large has LargeTypes structs and a call chain of LargeFunctions
    // functions, each constructing one struct and running a loop of LargeSteps steps with a branch.
    private static byte[] Generate(string name)
    {
        var text = new StringBuilder("public func main()\n    return\n");
        if (name == "Large")
        {
            text.Clear();
            for (var t = 0; t < LargeTypes; t++)
            {
                text.Append(CultureInfo.InvariantCulture, $"struct Pair{t}\n    public var left: i64\n    public var right: i64\n\n    public init(left: i64, right: i64)\n        self.left = left\n        self.right = right\n\n");
            }

            for (var k = 0; k < LargeFunctions; k++)
            {
                text.Append(CultureInfo.InvariantCulture, $"func step{k}(x: i64) -> i64\n    let pair = Pair{k % LargeTypes}.init(x & 0xFF, {k})\n    var total: i64 = pair.left + pair.right\n    var i: i64 = 0\n    while i < {LargeSteps}\n        if (total & 1) == 0\n            total = (total * 3 + i) & 0xFFFF\n        else\n            total = (total + {k} + i) & 0xFFFF\n        i = i + 1\n");
                text.Append(k == 0 ? "    return total\n\n" : string.Create(CultureInfo.InvariantCulture, $"    return (total + step{k - 1}(total & 0xFF)) & 0xFFFF\n\n"));
            }

            text.Append(CultureInfo.InvariantCulture, $"public func main()\n    let checksum = step{LargeFunctions - 1}(7)\n    if checksum < 0\n        $abort(\"negative\")\n    Console.writeLine(\"done\")\n");
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static void Require(Round round, string log)
    {
        if (round.ExitCode != 0)
        {
            throw new InvalidOperationException($"Exit {round.ExitCode}: {log}\n{File.ReadAllText(log)}");
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // Builds, tests and native tools with their CPU seconds so far (as benchmark-o2-corpus.ps1 records them).
    private static string[] Busy() => Process.GetProcesses()
        .Where(p => p.ProcessName is "dotnet" or "MSBuild" or "VBCSCompiler" or "xUnitTest" or "testhost" or "clang" or "lld-link" or "opt" or "llc" or "Kimi")
        .Select(p => $"{p.ProcessName} {p.Id} {p.TotalProcessorTime.TotalSeconds:F1} s").ToArray();

    private static string Capture(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, UseShellExecute = false };
        arguments.ToList().ForEach(start.ArgumentList.Add);
        using var process = Process.Start(start)!;
        var text = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return text;
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kimigayo.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
