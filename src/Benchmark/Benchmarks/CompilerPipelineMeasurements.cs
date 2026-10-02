// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;

namespace Benchmark;

// Ratios come from contiguous intervals within the same fresh compilation, never warm replays.
internal static class CompilerPipelineMeasurements
{
    private const int Warmups = 8;
    private const int SampleCount = 7;
    private const int Iterations = 8;
    private static readonly string[] StageNames = ["PreparationTokenizerParser", "BindingAndStartup", "ControlFlowAndOwnership", "EmissionLowering", "LlvmIrWriting"];

    internal static void Run()
    {
        var results = new List<object>();
        foreach (var benchmark in Inputs())
        {
            benchmark.Setup();
            for (var i = 0; i < Warmups; i++)
            {
                benchmark.FreshCompileToIr();
            }

            var samples = new Sample[SampleCount];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Measure(benchmark, Iterations);
            }

            var operations = SampleCount * Iterations;
            var totalTicks = samples.Sum(x => x.TotalTicks);
            var totalBytes = samples.Sum(x => x.TotalAllocatedBytes);
            Require(totalTicks > 0, "The measurement clock did not advance.");
            var stages = Enumerable.Range(0, StageNames.Length).Select(i => new
            {
                stage = i + 1,
                name = StageNames[i],
                meanMilliseconds = Milliseconds(samples.Sum(x => x.StageTicks[i]), operations),
                percentOfTotal = 100.0 * samples.Sum(x => x.StageTicks[i]) / totalTicks,
                meanAllocatedBytes = (double)samples.Sum(x => x.StageAllocatedBytes[i]) / operations,
            }).ToArray();
            results.Add(new
            {
                benchmark.Functions,
                benchmark.Branches,
                sourceUtf8Bytes = Encoding.UTF8.GetByteCount(benchmark.Source),
                sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(benchmark.Source))),
                operations,
                totalMeanMilliseconds = Milliseconds(totalTicks, operations),
                totalMeanAllocatedBytes = (double)totalBytes / operations,
                stages,
                samples,
            });
        }

        var report = new
        {
            schemaVersion = 1,
            measuredAtUtc = DateTimeOffset.UtcNow,
            compiler = Compilation.CompilerVersion,
            benchmarkModule = typeof(CompilerPipelineMeasurements).Module.ModuleVersionId,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount,
#if DEBUG
            configuration = "Debug",
#else
            configuration = "Release",
#endif
            target = "x86_64-pc-windows-msvc",
            stopwatchFrequency = Stopwatch.Frequency,
            warmups = Warmups,
            sampleCount = SampleCount,
            iterationsPerSample = Iterations,
            stages = StageNames,
            results,
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    internal static void Check()
    {
        Span<long> timestamps = stackalloc long[6];
        Span<long> allocations = stackalloc long[6];
        foreach (var benchmark in Inputs())
        {
            benchmark.Setup();
            for (var iteration = 0; iteration < 2; iteration++)
            {
                using var measured = new StringWriter();
                var compilation = benchmark.Compile(measured, timestamps, allocations);
                ValidateBoundaries(timestamps, allocations);

                // The split internal API must produce exactly the same IR as the public emitter.
                using var expected = new StringWriter();
                Require(compilation.Emission.WriteIr(expected, out var failure), failure);
                Require(measured.GetStringBuilder().Length > 0 && measured.ToString() == expected.ToString(), "Split emission changed the public IR output.");
                using var uninstrumented = new StringWriter();
                benchmark.Compile(uninstrumented);
                Require(measured.ToString() == uninstrumented.ToString(), "Instrumentation changed compilation output.");
            }

            var sample = Measure(benchmark, 2);
            Require(sample.StageTicks.Sum() == sample.TotalTicks, "Stage times do not cover the complete interval.");
            Require(sample.StageAllocatedBytes.Sum() == sample.TotalAllocatedBytes, "Stage allocations do not cover the complete interval.");
            Console.WriteLine($"PASS five-stage compiler pipeline: functions={benchmark.Functions}, branches={benchmark.Branches}; IR equivalence and accounting");
        }
    }

    private static Sample Measure(CompilerPipelineBenchmark benchmark, int iterations)
    {
        var stageTicks = new long[StageNames.Length];
        var stageBytes = new long[StageNames.Length];
        var collections = new int[3];
        Span<long> timestamps = stackalloc long[6];
        Span<long> allocations = stackalloc long[6];
        for (var generation = 0; generation < collections.Length; generation++)
        {
            collections[generation] = GC.CollectionCount(generation);
        }

        long totalTicks = 0;
        long totalBytes = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            benchmark.Compile(TextWriter.Null, timestamps, allocations);
            ValidateBoundaries(timestamps, allocations);
            totalTicks += timestamps[^1] - timestamps[0];
            totalBytes += allocations[^1] - allocations[0];
            for (var stage = 0; stage < StageNames.Length; stage++)
            {
                stageTicks[stage] += timestamps[stage + 1] - timestamps[stage];
                stageBytes[stage] += allocations[stage + 1] - allocations[stage];
            }
        }

        for (var generation = 0; generation < collections.Length; generation++)
        {
            collections[generation] = GC.CollectionCount(generation) - collections[generation];
        }

        return new(totalTicks, totalBytes, stageTicks, stageBytes, collections);
    }

    private static void ValidateBoundaries(ReadOnlySpan<long> timestamps, ReadOnlySpan<long> allocations)
    {
        for (var i = 1; i < timestamps.Length; i++)
        {
            Require(timestamps[i] >= timestamps[i - 1] && allocations[i] >= allocations[i - 1], "Measurement boundaries are not monotonic.");
        }
    }

    private static IEnumerable<CompilerPipelineBenchmark> Inputs()
    {
        foreach (var functions in new[] { 1, 32, 128 })
        {
            foreach (var branches in new[] { false, true })
            {
                yield return new() { Functions = functions, Branches = branches };
            }
        }
    }

    private static double Milliseconds(long ticks, int operations)
        => ticks * 1000.0 / Stopwatch.Frequency / operations;

    private static void Require(bool condition, string? failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure ?? "Compiler pipeline validation failed.");
        }
    }

    private sealed record Sample(long TotalTicks, long TotalAllocatedBytes, long[] StageTicks, long[] StageAllocatedBytes, int[] Collections);
}
