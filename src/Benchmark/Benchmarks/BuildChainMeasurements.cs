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
/// One compiler, or two interleaved A, B, A, ..., A from one folder under neutral names (the same compiler twice is the A=A pass), or
/// with <c>--tatara</c> one compiler whose B side is <c>kimi tatara</c> reading the row's command from standard input.
/// Run with <c>--build-chain &lt;name&gt; &lt;Kimi.dll folder A&gt; [&lt;folder B&gt; | --tatara] [--rounds n]</c>.
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
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("BuildChain measures through Windows job objects.");
        }

        var positional = new List<string>();
        var rounds = 20;
        var tatara = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rounds" when i + 1 < args.Length:
                    rounds = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--tatara":
                    tatara = true;
                    break;
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        var repository = FindRepository();
        // The name is one plain path segment, so the output and work folders stay where they are declared.
        var name = positional.FirstOrDefault() ?? string.Empty;
        var output = Path.Combine(repository, "artifacts", "benchmarks", name);
        var work = Path.Combine(repository, "temp", "build-chain", name);
        if (positional.Count is < 2 or > 3 || positional.Any(p => p.StartsWith('-')) || rounds < 1 || (tatara && positional.Count != 2) || Directory.Exists(output) || Directory.Exists(work) ||
            name is "." or ".." || name.IndexOfAny([.. Path.GetInvalidFileNameChars(), Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException("Use --build-chain <new run name> <Kimi.dll folder A> [<folder B> | --tatara] [--rounds n]; the name is one plain path segment, unused under artifacts/benchmarks and temp/build-chain.");
        }

        var dotnet = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).Select(d => Path.Combine(d, "dotnet.exe")).FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("dotnet.exe is not on PATH.");
        var inherited = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("KIMI_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).Select(k => $"{k}={Environment.GetEnvironmentVariable(k)}").ToArray();
        var tiering = inherited.Where(v => v.StartsWith("DOTNET_TieredCompilation=", StringComparison.OrdinalIgnoreCase) || v.StartsWith("DOTNET_TC_", StringComparison.OrdinalIgnoreCase)).ToArray();
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
            ["environment"] = inherited,
            ["affinity"] = $"0x{(long)Process.GetCurrentProcess().ProcessorAffinity:X}",
            ["tiering"] = tiering.Length == 0 ? "default" : string.Join(", ", tiering),
            ["rounds"] = rounds,
            ["order"] = variants.Length == 1 && !tatara ? "A per round" : "A, B, A, ..., A per row (rounds + 1 runs of A, rounds runs of B)",
            ["tatara"] = tatara ? "B: kimi tatara, the row's command as its one line on standard input, then end of input; session rows k1, k10: k lines" : null,
            ["compilers"] = variants.Select(v => new
            {
                v.Name,
                v.Folder,
                v.Copy,
                KimiDllSha256 = Sha256(File.ReadAllBytes(Path.Combine(v.Folder, "Kimi.dll"))),
                FileVersionInfo.GetVersionInfo(Path.Combine(v.Folder, "Kimi.dll")).ProductVersion,
                Runtime = Runtime(dotnet, v.Folder),
            }).ToArray(),
            ["busyProcessesAtStart"] = Busy(),
        };
        var rows = new List<object>();
        var comparison = new StringBuilder("\n| Row | p50 wall B / A | p95 peak commit B - A MiB |\n| --- | --- | --- |\n");
        var sessions = new StringBuilder("\n| Session | First-line ms | Later-line ms | Growth MiB |\n| --- | --- | --- | --- |\n");
        var compared = false;
        var summary = new StringBuilder($"# BuildChain {name}\n\n| Row | Compiler | First wall ms | p50 wall ms | p95 wall ms | p50 CPU ms | p95 CPU ms | p95 peak commit MiB | TotalProcesses |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
        var status = "incomplete";
        string? failure = null;
        Directory.CreateDirectory(output);
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

            foreach (var ((input, _, _), text) in Inputs.Zip(texts))
            {
                foreach (var level in new[] { "O0", "O2" })
                {
                    var sourceProject = WriteInput(Path.Combine(work, "in", $"{input}-{level}", "source"), input, level, text);
                    var pairProject = WriteInput(Path.Combine(work, "in", $"{input}-{level}", "pair"), input, level, text);
                    var log = Path.Combine(work, "in", $"{input}-{level}", "emit.log");
                    Measure(dotnet, [Path.Combine(variants[0].Copy, "Kimi.dll"), "emit", pairProject], Path.GetDirectoryName(pairProject)!, log, "NUL");
                    var manifest = Path.Combine(Path.GetDirectoryName(pairProject)!, "bin", Target, $"{input}.link.json");
                    var kimi = Path.Combine(variants[0].Copy, "Kimi.dll");
                    foreach (var (kind, command) in new[] { ("source", new[] { "build", sourceProject }), ("manifest", new[] { "build", "--Manifest", manifest }) })
                    {
                        var lines = Path.Combine(work, "in", $"{input}-{level}", kind);
                        (string Name, string[] Command, string? Lines)[] sides = tatara ? [("A", [kimi, .. command], null), ("B", [kimi, "tatara"], Lines(lines, command, 1))] :
                            [.. variants.Select(v => (v.Name, (string[])[Path.Combine(v.Copy, "Kimi.dll"), .. command], (string?)null))];
                        var (record, figures) = MeasureRow($"{input} {level} {kind}", sides, dotnet, Path.GetDirectoryName(command[^1])!, rounds, summary);
                        rows.Add(record);
                        compared = figures.Length == 2;
                        if (compared)
                        {
                            comparison.Append(CultureInfo.InvariantCulture, $"| {input} {level} {kind} | {figures[1].WallP50 / figures[0].WallP50:F3} | {(figures[1].PeakP95 - figures[0].PeakP95) / 1048576:F1} |\n");
                        }

                        // The session's own figures (BuildChain.md): k = 1 and k = 10 lines of one source input in one process.
                        if (tatara && kind == "source" && level == "O2" && input is "Hello" or "Milestone30")
                        {
                            (record, figures) = MeasureRow($"{input} O2 session", [("k1", [kimi, "tatara"], Lines(lines, command, 1)), ("k10", [kimi, "tatara"], Lines(lines, command, 10))], dotnet, Path.GetDirectoryName(sourceProject)!, rounds, summary);
                            rows.Add(record);
                            sessions.Append(CultureInfo.InvariantCulture, $"| {input} O2 | {figures[0].WallP50:F0} | {(figures[1].WallP50 - figures[0].WallP50) / 9:F0} | {(figures[1].PeakP95 - figures[0].PeakP95) / 1048576:F1} |\n");
                        }
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
            File.WriteAllText(Path.Combine(output, "summary.md"), summary.Append(compared ? comparison : null).Append(tatara ? sessions : null).Append($"\nStatus: {status}{(failure is null ? string.Empty : $" ({failure})")}. Raw rounds and conditions: rounds.json.\n").ToString());
            Console.WriteLine($"Summary: {output} ({status})");
        }
    }

    // One row: every run in order, A alone or A, B, A, ..., A, each side its command and its standard input (a lines file, or NUL),
    // started in the project's or the manifest's folder; each side's first run is reported apart and kept in p50/p95.
    private static (object Record, (double WallP50, double PeakP95)[] Figures) MeasureRow(string row, (string Name, string[] Command, string? Lines)[] variants, string dotnet, string directory, int count, StringBuilder summary)
    {
        var order = Enumerable.Range(0, variants.Length == 1 ? count : (2 * count) + 1).Select(i => variants[i % variants.Length]).ToArray();
        var rounds = new List<(string Variant, Round Round)>();
        foreach (var (name, command, lines) in order)
        {
            var log = Path.Combine(Path.GetDirectoryName(command[0])!, "..", $"{name}.log");
            rounds.Add((name, Measure(dotnet, command, directory, log, lines ?? "NUL")));
        }

        var statistics = new Dictionary<string, object>();
        var figures = new (double WallP50, double PeakP95)[variants.Length];
        foreach (var (name, _, _) in variants)
        {
            var mine = rounds.Where(r => r.Variant == name).Select(r => r.Round).ToArray();
            var wall = mine.Select(r => r.WallMilliseconds).ToArray();
            var cpu = mine.Select(r => r.UserMilliseconds + r.KernelMilliseconds).ToArray();
            var peak = mine.Select(r => (double)r.PeakCommitBytes).ToArray();
            var processes = $"{mine.Min(r => r.TotalProcesses)}-{mine.Max(r => r.TotalProcesses)}";
            figures[statistics.Count] = (Percentile(wall, 0.5), Percentile(peak, 0.95));
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
            var line = string.Create(CultureInfo.InvariantCulture, $"| {row} | {name} | {mine[0].WallMilliseconds:F0} | {Percentile(wall, 0.5):F0} | {Percentile(wall, 0.95):F0} | {Percentile(cpu, 0.5):F0} | {Percentile(cpu, 0.95):F0} | {Percentile(peak, 0.95) / 1048576:F1} | {processes} |");
            summary.Append(line).Append('\n');
            Console.WriteLine(line);
        }

        var sides = variants.Select(v => new { v.Name, v.Command, Lines = v.Lines is null ? null : File.ReadAllText(v.Lines) }).ToArray();
        return (new { row, directory, sides, rounds = rounds.Select(r => new { r.Variant, r.Round }).ToArray(), statistics }, figures);
    }

    // A kimi tatara input: k lines of the command, its paths quoted, then end of input.
    private static string Lines(string path, string[] command, int k)
    {
        var line = string.Join(' ', command.Select(a => a.StartsWith('-') || a == "build" ? a : $"\"{a}\"")) + "\n";
        File.WriteAllText(path += $"-k{k}.lines", string.Concat(Enumerable.Repeat(line, k)));
        return path;
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
        return process.ExitCode == 0 ? text : throw new InvalidOperationException($"Exit {process.ExitCode}: {file} {string.Join(' ', arguments)}");
    }

    // The runtime that runs the compiler: its runtimeconfig's framework under the host's default roll-forward (Minor, latest
    // patch), so the highest installed patch of the requested major.minor; any other resolution fails here.
    private static string Runtime(string dotnet, string folder)
    {
        using var config = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "Kimi.runtimeconfig.json")));
        var framework = config.RootElement.GetProperty("runtimeOptions").GetProperty("framework");
        var requested = Version.Parse(framework.GetProperty("version").GetString()!);
        var prefix = $"{framework.GetProperty("name").GetString()} {requested.Major}.{requested.Minor}.";
        var installed = Capture(dotnet, "--list-runtimes").Split('\n').Where(l => l.StartsWith(prefix, StringComparison.Ordinal)).Select(l => Version.Parse(l.Split(' ')[1])).ToArray();
        return installed.Length != 0 ? $"{prefix.Split(' ')[0]} {installed.Max()}" : throw new InvalidOperationException($"No installed runtime {prefix}x (requested {requested}).");
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
