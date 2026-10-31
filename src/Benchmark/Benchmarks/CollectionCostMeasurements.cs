// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;

namespace Benchmark;

// The instance collector's cost inside one compilation (PLAN10-P3's collection share; R5 plan §5.3 item 10, §5.4 item 6, §5.5 item 7;
// DECISIONS R5-M148), without a compiler hook. For each compiler pipeline input, a mir-pipeline compilation is bound and its MIR built, validated
// and analyzed outside timing by the two components Compilation.BuildMir composes (MirBuilder, then MirAnalysis), not by BuildMir, which
// also collects (on every pipeline since DECISIONS BU-48). Three fresh collectors then run over that program in order, each timed with its
// construction: cold, the compilation's first collection, with its Layout (the collection-and-layout share, and the check-time cost of
// collection); freshLayout, a new Layout with the Type context's memos warm; warm, the compilation's Layout again. Layout's first
// computations are freshLayout - warm and the Type context's first queries cold - freshLayout, both per operation inside one compilation.
// `--collection-cost-check` times nothing: it requires the three collections to equal each other and the compilation's own checked set
// (its Analyze's) element by element (the items' keys in admission order, the records' codes and locations), the comparison to fail on a
// reordered collection and on a changed record, and cold to allocate more than warm, so a warm "cold" fails.
internal static class CollectionCostMeasurements
{
    private const int Warmups = 4;
    private const int Samples = 7;
    private const int Iterations = 4;

    private static readonly string[] Variants = ["cold", "freshLayout", "warm"];

    internal static void Run(bool check)
    {
        var results = new List<object>();
        foreach (var shape in Enum.GetValues<PipelineShape>())
        {
            foreach (var size in new[] { 1, 32, 64, 128 })
            {
                var input = new CompilerPipelineBenchmark { Shape = shape, Size = size };
                input.Setup();
                if (check)
                {
                    Check(input);
                }
                else
                {
                    results.Add(Result(input));
                }
            }
        }

        if (check)
        {
            CheckRecordComparison();
            return;
        }

        var report = new
        {
            schemaVersion = 2,
            measuredAtUtc = DateTimeOffset.UtcNow,
            compiler = Compilation.CompilerVersion,
            compilerModule = typeof(Compilation).Module.ModuleVersionId,
            benchmarkModule = typeof(CollectionCostMeasurements).Module.ModuleVersionId,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processors = Environment.ProcessorCount,
            affinity = OperatingSystem.IsWindows() ? (long)Process.GetCurrentProcess().ProcessorAffinity : -1,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            stopwatchFrequency = Stopwatch.Frequency,
            warmups = Warmups,
            samples = Samples,
            iterationsPerSample = Iterations,
            variants = Variants,
            results,
        };
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Console.WriteLine(JsonSerializer.Serialize(report, options));
    }

    // One input's measurements, in a frame of its own, so that no compilation of another input stays reachable during them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object Result(CompilerPipelineBenchmark input)
    {
        for (var i = 0; i < Warmups; i++)
        {
            Operation(input.Source);
        }

        var ticks = Variants.Select(_ => new long[Samples * Iterations]).ToArray();
        var bytes = Variants.Select(_ => new long[Samples * Iterations]).ToArray();
        var counts = (Bodies: 0, Items: 0, Records: 0);
        for (var i = 0; i < Samples * Iterations; i++)
        {
            var operation = Operation(input.Source);
            for (var v = 0; v < Variants.Length; v++)
            {
                (ticks[v][i], bytes[v][i]) = (operation.Ticks[v], operation.Bytes[v]);
            }

            counts = (operation.Bodies, operation.Cold.Items.Length, operation.Cold.Records.Count);
        }

        var variants = Enumerable.Range(0, Variants.Length).Select(v => new
        {
            name = Variants[v],
            medianMilliseconds = Milliseconds(Median(ticks[v])),
            meanMilliseconds = Milliseconds(ticks[v].Sum()) / ticks[v].Length,
            minMilliseconds = Milliseconds(ticks[v].Min()),
            meanAllocatedBytes = (double)bytes[v].Sum() / bytes[v].Length,
            ticks = ticks[v],
        }).ToArray();
        return new
        {
            shape = input.Shape.ToString(),
            size = input.Size,
            mirBodies = counts.Bodies,
            collectedItems = counts.Items,
            collectorRecords = counts.Records,
            layoutMedianMilliseconds = Milliseconds(Median(Difference(ticks[1], ticks[2]))),
            firstQueriesMedianMilliseconds = Milliseconds(Median(Difference(ticks[0], ticks[1]))),
            variants,
        };
    }

    // One operation: a checked mir-pipeline compilation and its built, validated and analyzed MIR outside timing, then the three collections over
    // that program, cold first, each timed with its construction; the three must be equal element by element.
    private static Collections Operation(string source)
    {
        var c = Checked(source);
        var program = new MirAnalysis(c.Types).Run(new MirBuilder(c).Build(), c.SourceModules.Length);
        Require(program.Records.Count == 0 && program.Findings.Count == 0, "MIR construction or validation failed.");
        Layout[] layouts = [c.Layout, new Layout(), c.Layout];
        var (ticks, bytes) = (new long[layouts.Length], new long[layouts.Length]);
        CheckedSet? cold = null;
        for (var v = 0; v < layouts.Length; v++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var set = new InstanceCollector(c.Types, layouts[v]).Run(program, c.SourceModules.Length);
            ticks[v] = Stopwatch.GetTimestamp() - start;
            bytes[v] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            cold ??= set;
            Require(Difference(cold, set) is null, $"The {Variants[v]} collection differs from the cold one: {Difference(cold, set)}.");
        }

        return new(c, program.Bodies.Length, cold!, ticks, bytes);
    }

    // The check of one input: cold is the compilation's first collection (it allocates more than warm), the collections equal the
    // compilation's own checked set element by element, and the comparison fails on the same collection reordered.
    private static void Check(CompilerPipelineBenchmark input)
    {
        var operation = Operation(input.Source);
        Require(operation.Bytes[0] > operation.Bytes[2], $"cold allocates {operation.Bytes[0]} B, warm {operation.Bytes[2]} B: cold is not cold.");
        Require(operation.Compilation.Analyze().IsVerified, "The analysis rejected the program.");
        var own = operation.Compilation.Instances ?? throw new InvalidOperationException("The compilation has no checked set.");
        Require(Difference(operation.Cold, own) is null, $"The collection differs from the compilation's own: {Difference(operation.Cold, own)}.");
        var keys = Keys(operation.Cold);
        if (keys.Length > 1)
        {
            Require(Difference(keys, [.. keys[1..], keys[0]], operation.Cold.Records, own.Records) is not null, "A reordered collection passes.");
        }

        Console.WriteLine($"PASS collection cost: shape={input.Shape}, size={input.Size}, MIR bodies={operation.Bodies}, items={keys.Length}, " +
            $"records={operation.Cold.Records.Count}, cold {operation.Bytes[0]} B > warm {operation.Bytes[2]} B");
    }

    // The record half of the comparison fails on a changed code and on a changed location (the inputs have no records of their own).
    private static void CheckRecordComparison()
    {
        Problem[] records = [new(DiagnosticCode.Unsupported_Kd, new(0, 1))];
        Require(Difference([], [], records, [new(DiagnosticCode.Unsupported_Kd, new(0, 1))]) is null, "Equal records differ.");
        Require(Difference([], [], records, [new(DiagnosticCode.InternalInvariant_Kd, new(0, 1))]) is not null, "A changed code passes.");
        Require(Difference([], [], records, [new(DiagnosticCode.Unsupported_Kd, new(0, 2))]) is not null, "A changed location passes.");
        Console.WriteLine("PASS collection cost: the comparison fails on a reordered collection, a changed record code and a changed location");
    }

    // A compilation of the source on the mir pipeline, prepared, parsed, bound and started.
    private static Compilation Checked(string source)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Mir;
        Require(c.Prepare("x86_64-pc-windows-msvc"), "Target preparation failed.");
        c.Kotonoha.AddSource(new SourceDocument("CollectionCost.kimi", source));
        Require(c.Bind().IsComplete, "Binding failed.");
        Require(c.Binding.CheckStartup(OutputKind.Application).IsComplete, "Startup failed.");
        return c;
    }

    private static string? Difference(CheckedSet a, CheckedSet b) => Difference(Keys(a), Keys(b), a.Records, b.Records);

    // The first difference of two collections: their items' keys in admission order, then their records' codes and locations.
    private static string? Difference(MonoKey[] a, MonoKey[] b, IReadOnlyList<Problem> recordsA, IReadOnlyList<Problem> recordsB)
    {
        if (a.Length != b.Length || recordsA.Count != recordsB.Count)
        {
            return $"items {a.Length}/{b.Length}, records {recordsA.Count}/{recordsB.Count}";
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return $"item {i}: {a[i].Kind}/{b[i].Kind}";
            }
        }

        for (var i = 0; i < recordsA.Count; i++)
        {
            if (recordsA[i] != recordsB[i])
            {
                return $"record {i}: {recordsA[i]}/{recordsB[i]}";
            }
        }

        return null;
    }

    private static MonoKey[] Keys(CheckedSet set)
    {
        var keys = new MonoKey[set.Items.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = set.Items[i].Key;
        }

        return keys;
    }

    private static long[] Difference(long[] minuend, long[] subtrahend) => minuend.Zip(subtrahend, (x, y) => x - y).ToArray();

    private static long Median(long[] values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure);
        }
    }

    // One operation's compilation, its program's bodies, the cold collection and each variant's ticks and allocated bytes.
    private sealed record Collections(Compilation Compilation, int Bodies, CheckedSet Cold, long[] Ticks, long[] Bytes);
}
