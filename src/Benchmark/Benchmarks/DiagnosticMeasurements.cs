// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;

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
        int[] valid = [19, 24, 35, 30];
        int[] rejected = [36, 39, 40];
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

        // Three hundred bare transfers of Non-Copy locals, each offering a Transfer repair candidate (SPEC 23.1.3), against three
        // hundred transfers through a shared reference, which refute Take and offer none: the difference per record is a candidate's cost.
        const string Declarations = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => ()\nstruct Holder\n    public var item: Resource\n    public init(item: Resource) => self.item = item\nfunc consume(value: Resource) => ()\npublic func main() => ()\n";
        var transfers = new StringBuilder(Declarations);
        var refuted = new StringBuilder(Declarations);
        for (var i = 0; i < 100; i++)
        {
            transfers.Append("func g").Append(i).Append("()\n    let a = Resource.init(1)\n    let b = Resource.init(2)\n    let c = Resource.init(3)\n    consume(a)\n    consume(b)\n    consume(c)\n");
            refuted.Append("func g").Append(i).Append("(h: ref/Holder)\n    consume(h.item)\n    consume(h.item)\n    consume(h.item)\n");
        }

        var transfersProject = WriteProject(directory, "Transfers", transfers.ToString());
        var refutedProject = WriteProject(directory, "RefutedTransfers", refuted.ToString());
        results["check-300-transfer-candidates"] = Measure(() => Check(transfersProject), 8);
        results["check-300-transfers-without-candidates"] = Measure(() => Check(refutedProject), 8);

        // Input line indexing is already established. Record formation should depend on the quoted window, not on
        // the length of the unquoted prefix. Keep identical windows and sample conditions at both input sizes.
        foreach (var prefix in new[] { 2000, 1000000 })
        {
            var owner = new DiagnosticOwner();
            var document = new SourceDocument("excerpt.kimi", new string('x', prefix) + "bad" + new string('z', 300));
            owner.GetOrAddCollection("excerpt").For(document).Add(new(prefix, 3), DiagnosticCode.SemicolonNotAllowed_Kd);
            owner.Finalize();
            results[$"finalize-excerpt-{prefix}-prefix"] = Measure(() => owner.Finalize(), 8);
        }

        var warm = Compilation.CreateForTest();
        Require(warm.Prepare(Target));
        warm.Kotonoha.AddSource(new SourceDocument("Milestone35.kimi", File.ReadAllText(Path.Combine(repository, "tests", "milestones", "Milestone35.kimi"))));
        Require(warm.Bind().IsComplete);
        results["warm-rebind-report-milestone35"] = Measure(
            () =>
            {
                Require(warm.Binding.Bind(BindingMode.Final).IsComplete);
                warm.Binding.ReportDiagnostics();
            },
            32);

        results["override-failure-binding"] = OverrideFailures();
        results["operator-binding"] = OperatorBindings();
        results["attribute-causes"] = AttributeCauses(directory);

        Console.WriteLine(JsonSerializer.Serialize(new { compiler = Compilation.CompilerVersion, runtime = Environment.Version.ToString(), results }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // The same legal and rejected operations before and after applicability changes; diagnostics never enter the timed window.
    private static object OperatorBindings()
    {
        const int Count = 128;
        var accepted = new StringBuilder("public func main() => ()\n");
        var rejected = new StringBuilder("struct S\n    public var value: i32 = 0\npublic func main() => ()\n");
        for (var i = 0; i < Count; i++)
        {
            accepted.Append((i % 4) switch
            {
                0 => $"func operatorCase{i}(x: i32) -> i32 => (x + 2) << 1\n",
                1 => $"func operatorCase{i}(x: f64) -> bool => -x < 2.0\n",
                2 => $"func operatorCase{i}<T>(x: T) -> T\n    T is PrimitiveInteger\n    return x << 1\n",
                _ => $"func operatorCase{i}(p: raw/i32) -> raw/i32\n    unsafe\n        return p + 2\n",
            });
            rejected.Append((i % 8) switch
            {
                0 => $"func operatorCase{i}(x: u32) => -x\n",
                1 => $"func operatorCase{i}(p: raw/i32) => p * 2\n",
                2 => $"func operatorCase{i}<T>(x: T) => x << 1\n",
                3 => $"func operatorCase{i}(x: S) => x << 1\n",
                4 => $"func operatorCase{i}(p: raw/i32, q: raw/i32) => p < q\n",
                5 => $"func operatorCase{i}(x: bool, y: bool) => x < y\n",
                6 => $"func operatorCase{i}<T>(x: T) => x & 2\n",
                _ => $"func operatorCase{i}(x: S) => x & 2\n",
            });
        }

        return new { accepted = MeasureOperatorBinding(accepted.ToString(), 0), rejected = MeasureOperatorBinding(rejected.ToString(), Count) };
    }

    private static object MeasureOperatorBinding(string source, int failures)
    {
        var retained = new long[7];
        for (var sample = 0; sample < retained.Length; sample++)
        {
            retained[sample] = RetainedOperatorBinding(source, failures);
        }

        var cold = Measure(() => BindOperators(source, failures), 8);
        var compilation = BindOperators(source, failures);
        var warm = Measure(() => Require(compilation.Bind().IsComplete == (failures == 0)), 32);
        GC.KeepAlive(compilation);
        return new { source, failures, retainedBytes = retained, medianRetainedBytes = retained.Order().ElementAt(3), cold, warm };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long RetainedOperatorBinding(string source, int failures)
    {
        var before = GC.GetTotalMemory(true);
        var compilation = BindOperators(source, failures);
        var retained = GC.GetTotalMemory(true) - before;
        GC.KeepAlive(compilation);
        return retained;
    }

    private static Compilation BindOperators(string source, int failures)
    {
        var compilation = Compilation.CreateForTest();
        Require(compilation.Prepare(Target));
        compilation.Kotonoha.AddSource(new SourceDocument("operators.kimi", source));
        Require(compilation.Bind().IsComplete == (failures == 0));
        Require(compilation.Binding.Issues.Count == failures);
        return compilation;
    }

    // Equivalent declaration workloads isolate retained rejection causes from normal Binding; publication is measured separately.
    private static object AttributeCauses(string directory)
    {
        const int Count = 128;
        var cases = new (string Name, string Attribute)[]
        {
            ("normal", string.Empty), ("test", "#Test"), ("layout", "#Layout(\"bad\")"),
            ("unknown", "#Unknown"), ("import", "#LibraryImport(\"library\", \"symbol\")"),
        };
        var results = new Dictionary<string, object>();
        foreach (var item in cases)
        {
            var source = new StringBuilder();
            for (var i = 0; i < Count; i++)
            {
                if (item.Attribute.Length != 0)
                {
                    source.AppendLine(item.Attribute);
                }

                source.Append("struct Entry").Append(i).AppendLine("\n    let value: i32");
            }

            var text = source.ToString();
            var sourcePath = Path.Combine(directory, "attribute-causes-" + item.Name + ".kimi");
            File.WriteAllText(sourcePath, text);
            var rejected = item.Attribute.Length != 0;
            var retained = new long[7];
            for (var sample = 0; sample < retained.Length; sample++)
            {
                retained[sample] = RetainedAttributeCauses(text, rejected, Count);
            }

            var cold = Measure(() => BindAttributeCauses(text, rejected, Count), 8);
            var compilation = BindAttributeCauses(text, rejected, Count);
            var warm = Measure(() => Require(compilation.Bind().IsComplete != rejected), 32);
            var publication = Measure(
                () =>
                {
                    Require(compilation.Bind().IsComplete != rejected);
                    compilation.Binding.ReportDiagnostics();
                    Require(compilation.Diagnostics.Finalize().Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error) == rejected);
                },
                32);
            results[item.Name] = new
            {
                count = Count,
                sourcePath,
                sourceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                pipeline = compilation.Pipeline.ToString(),
                retainedBytes = retained,
                medianRetainedBytes = retained.Order().ElementAt(3),
                cold,
                warm,
                publication,
            };
            GC.KeepAlive(compilation);
        }

        return new
        {
            gcServer = System.Runtime.GCSettings.IsServerGC,
            gcLatencyMode = System.Runtime.GCSettings.LatencyMode.ToString(),
            processorCount = Environment.ProcessorCount,
            results,
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long RetainedAttributeCauses(string source, bool rejected, int count)
    {
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var compilation = BindAttributeCauses(source, rejected, count);
        var retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(compilation);
        return retained;
    }

    private static Compilation BindAttributeCauses(string source, bool rejected, int count)
    {
        var compilation = Compilation.CreateForTest();
        Require(compilation.Prepare(Target));
        compilation.Kotonoha.AddSource(new SourceDocument("attribute-causes.kimi", source));
        Require(compilation.Bind().IsComplete != rejected);
        var declarations = compilation.Kotonoha.RootKoto.NestedContainers;
        Require(declarations.Count == count);
        Require(declarations.All(x => (x.BindingState == BindingState.Invalid) == rejected));
        return compilation;
    }

    // Fixed failed programs measure warm Binding and the retained compilation, including its failure records.
    private static object OverrideFailures()
    {
        const int Count = 128;
        var source = new StringBuilder();
        for (var i = 0; i < Count; i++)
        {
            source.Append($"open struct Base{i}\n    public virtual func read(self: objref/Self) -> i32 => 1\nstruct Derived{i} : Base{i}\n    override func read(self: objref/Self) -> i64 => 2\n");
        }

        var text = source.ToString();
        var retained = new long[7];
        for (var sample = 0; sample < retained.Length; sample++)
        {
            retained[sample] = RetainedOverrideFailures(text, Count);
        }

        var cold = Measure(() => BindOverrideFailures(text, Count), 8);
        var compilation = BindOverrideFailures(text, Count);
        var warm = Measure(() => Require(!compilation.Bind().IsComplete), 32);
        GC.KeepAlive(compilation);
        return new { count = Count, retainedBytes = retained, medianRetainedBytes = retained.Order().ElementAt(3), cold, warm };
    }

    // A separate stack frame retires the preceding sample before the next GC baseline.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long RetainedOverrideFailures(string source, int count)
    {
        var before = GC.GetTotalMemory(true);
        var compilation = BindOverrideFailures(source, count);
        var retained = GC.GetTotalMemory(true) - before;
        GC.KeepAlive(compilation);
        return retained;
    }

    private static Compilation BindOverrideFailures(string source, int count)
    {
        var compilation = Compilation.CreateForTest();
        Require(compilation.Prepare(Target));
        compilation.Kotonoha.AddSource(new SourceDocument("override-failures.kimi", source));
        Require(!compilation.Bind().IsComplete);
        Require(compilation.Binding.Issues.Count(x => x.Code == DiagnosticCode.OverrideContractMismatch_Kd) == count);
        return compilation;
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

        return new
        {
            medianMicroseconds = Math.Round(nanoseconds.Order().ElementAt(3) / 1000, 1),
            minimumMicroseconds = Math.Round(nanoseconds.Min() / 1000, 1),
            medianBytes = allocations.Order().ElementAt(3),
            nanoseconds,
            allocations,
        };
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
