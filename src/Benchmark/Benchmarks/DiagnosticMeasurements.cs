// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;

namespace Benchmark;

/// <summary>
/// Measures the diagnostic path of the Diagnostics track (docs/dev/DIAGNOSTICS.md §9.4): checks through the check entry with
/// and without diagnostics, a warm rebind and report of a valid program, and a source with many independent errors.
/// Run with <c>--diagnostics</c> on the same machine, configuration and inputs before and after a change.
/// </summary>
internal static class DiagnosticMeasurements
{
    private const string Target = "x86_64-pc-windows-msvc";

    public static void Run()
    {
        var repository = FindRepository();
        var directory = Path.Combine(repository, "temp", "diagnostic-measurements");
        Directory.CreateDirectory(directory);
        int[] valid = [22, 32, 37, 41];
        int[] rejected = [24, 33, 34];
        var results = new Dictionary<string, object>();
        foreach (var number in valid.Concat(rejected))
        {
            var project = WriteProject(directory, $"Milestone{number}", File.ReadAllText(Path.Combine(repository, "tests", "milestones", $"Milestone{number}.kimi")));
            results[$"check-milestone{number}"] = Measure(() => Check(project), 8);
        }

        // One hundred independent errors: an unknown name, a mismatched Type and an immutable assignment per function.
        var many = new StringBuilder("public func main() => ()\n");
        for (var i = 0; i < 100; i++)
        {
            many.Append("func f").Append(i).Append("()\n    let value: i32 = true\n    missing").Append(i).Append("()\n    value = 1\n");
        }

        var manyProject = WriteProject(directory, "ManyErrors", many.ToString());
        results["check-300-errors"] = Measure(() => Check(manyProject), 8);

        var warm = Compilation.CreateForTest();
        Require(warm.Prepare(Target));
        warm.Kotonoha.AddSource(new SourceDocument("Milestone37.kimi", File.ReadAllText(Path.Combine(repository, "tests", "milestones", "Milestone37.kimi"))));
        Require(warm.Bind().IsComplete);
        results["warm-rebind-report-milestone37"] = Measure(
            () =>
            {
                Require(warm.Binding.Bind(BindingMode.Final).IsComplete);
                warm.Binding.ReportDiagnostics();
            },
            32);

        Console.WriteLine(JsonSerializer.Serialize(new { compiler = Compilation.CompilerVersion, runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static CheckOutput Check(string project)
    {
        if (!Project.TryCreate(Kimigayo.CreateSilent(), null, project, CheckInputSource.Disk, out var loaded, out var failure))
        {
            throw new InvalidOperationException(failure);
        }

        return CheckService.Run(loaded, Target, CheckMode.Product, false, CheckInputSource.Disk, CancellationToken.None);
    }

    private static string WriteProject(string directory, string name, string source)
    {
        var folder = Path.Combine(directory, name);
        Directory.CreateDirectory(folder);
        var project = Path.Combine(folder, name + ".kimiproj");
        File.WriteAllText(project, $"OutputKind=\"Application\" Targets={{\"{Target}\"}}");
        File.WriteAllText(Path.Combine(folder, name + ".kimi"), source);
        return project;
    }

    // Seven samples after a warm-up; the median and minimum of time and allocation per operation.
    private static object Measure(Action action, int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            action();
        }

        var nanoseconds = new double[7];
        var allocations = new double[7];
        for (var sample = 0; sample < nanoseconds.Length; sample++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }

            nanoseconds[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations;
        }

        Array.Sort(nanoseconds);
        Array.Sort(allocations);
        return new { medianMicroseconds = Math.Round(nanoseconds[3] / 1000, 1), minimumMicroseconds = Math.Round(nanoseconds[0] / 1000, 1), medianBytes = allocations[3] };
    }

    private static void Require(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "A measured check failed.");
        }
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
